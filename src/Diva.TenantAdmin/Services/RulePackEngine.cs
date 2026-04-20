using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Diva.Core.Models;
using Diva.Infrastructure.Data;
using Diva.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Diva.TenantAdmin.Services;

/// <summary>
/// Runtime engine that loads and evaluates Rule Packs against agent requests/responses.
/// Called from TenantRulePackHook at lifecycle hook points.
/// Thread-safe: pre-compiled regex cache, batched execution logging.
/// </summary>
public sealed class RulePackEngine : IAsyncDisposable, IDisposable
{
    private readonly IDatabaseProviderFactory _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<RulePackEngine> _logger;
    private readonly ITenantBusinessRulesService? _businessRules;
    private readonly ConcurrentDictionary<string, Regex> _regexCache = new();
    private readonly Channel<RuleExecutionLogEntity> _logChannel;
    private readonly Task _logFlusher;
    private readonly CancellationTokenSource _cts = new();
    private int _disposed;                             // 0 = live, 1 = disposed (Interlocked guard)

    // S6: raised from 500 to 1000 — business rules add regex patterns across all rule types
    private const int MaxRegexCacheSize = 1000;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

    public RulePackEngine(
        IDatabaseProviderFactory db,
        IMemoryCache cache,
        ILogger<RulePackEngine> logger,
        ITenantBusinessRulesService? businessRules = null)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
        _businessRules = businessRules;

        _logChannel = Channel.CreateBounded<RuleExecutionLogEntity>(
            new BoundedChannelOptions(10_000)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
            });

        _logFlusher = Task.Run(FlushLogsAsync);
    }

    /// <summary>
    /// Resolves all active packs for a tenant (including group-inherited packs and business rules),
    /// filtered by activation condition and agent archetype.
    /// Business rules are merged as virtual HookRuleEntity records via BusinessRuleAdapter (T1, S1).
    /// </summary>
    public async Task<List<ResolvedRulePack>> ResolvePacksAsync(
        int tenantId, string agentArchetype, string userQuery, CancellationToken ct,
        string agentType = "*", string? agentId = null)
    {
        // S1: start business rules load immediately so it runs in parallel with the pack cache check / DB.
        // GetRulesAsync uses its own 5-min cache keyed by (tenantId, agentType, agentId).
        var rulesTask = _businessRules is not null
            ? _businessRules.GetRulesAsync(tenantId, agentType, ct, agentId)
            : Task.FromResult(new List<TenantBusinessRuleEntity>());

        var key = $"resolved_packs_{tenantId}";
        List<HookRulePackEntity>? allPacks;

        if (!_cache.TryGetValue(key, out allPacks) || allPacks is null)
        {
            using var db = _db.CreateDbContext();

            // Load tenant's own packs + group packs (via group membership)
            var groupIds = await db.TenantGroupMembers
                .IgnoreQueryFilters()
                .Where(m => m.TenantId == tenantId)
                .Select(m => m.GroupId)
                .ToListAsync(ct);

            allPacks = await db.RulePacks
                .IgnoreQueryFilters()
                .Where(p =>
                    (p.TenantId == tenantId) ||                          // tenant's own packs
                    (p.GroupId != null && groupIds.Contains(p.GroupId.Value))) // group packs
                .Include(p => p.Rules.Where(r => r.IsEnabled).OrderBy(r => r.OrderInPack))
                .Where(p => p.IsEnabled)
                .OrderBy(p => p.Priority)
                .AsNoTracking()
                .ToListAsync(ct);

            _cache.Set(key, allPacks, TimeSpan.FromMinutes(5));
        }

        // Await business rules (may already be done if cached, otherwise completes here)
        var businessRules = await rulesTask;

        // Group linked rules by their assigned pack (Gap #6: merge with tie-breaking handled by adapter)
        var linkedByPack = businessRules
            .Where(br => br.RulePackId.HasValue && br.IsActive)
            .GroupBy(br => br.RulePackId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Standalone rules (no pack assignment) will form a virtual pack
        var standaloneRules = businessRules
            .Where(br => !br.RulePackId.HasValue && br.IsActive)
            .ToList();

        var resolved = new List<ResolvedRulePack>();

        foreach (var pack in allPacks)
        {
            // Filter by AppliesTo archetype
            if (!MatchesArchetype(pack, agentArchetype))
                continue;

            // Filter by activation condition
            if (!MatchesActivation(pack, userQuery, agentArchetype))
                continue;

            // Merge any linked business rules into this pack's rules list
            var mergedRules = linkedByPack.TryGetValue(pack.Id, out var linked)
                ? BusinessRuleAdapter.MergeIntoPackRules(pack.Rules, linked)
                : pack.Rules;

            resolved.Add(new ResolvedRulePack(pack.Id, pack.Name, pack.Priority, pack.MaxEvaluationMs, mergedRules));
        }

        // Append virtual pack for standalone business rules at Priority=95 (between registered packs)
        if (standaloneRules.Count > 0)
        {
            var virtualPack = BusinessRuleAdapter.WrapAsVirtualPack(standaloneRules);
            // Insert in priority order
            var insertIdx = resolved.FindIndex(p => p.Priority > virtualPack.Priority);
            if (insertIdx < 0)
                resolved.Add(virtualPack);
            else
                resolved.Insert(insertIdx, virtualPack);
        }

        return resolved;
    }

    /// <summary>Evaluate OnInit rules before the first ReAct iteration starts.</summary>
    public RuleEvalResult EvaluateOnInit(
        List<ResolvedRulePack> packs, string systemPrompt, string userQuery, string? agentId, int tenantId)
        => EvaluateAtHookPoint(packs, "OnInit", systemPrompt, userQuery, agentId, tenantId);

    /// <summary>Evaluate OnBeforeIteration rules at the start of each ReAct iteration.</summary>
    /// <param name="lastIterationResponse">Response text from the previous iteration (empty on first iteration).</param>
    public RuleEvalResult EvaluateOnBeforeIteration(
        List<ResolvedRulePack> packs, string systemPrompt, string userQuery, string? agentId, int tenantId,
        string lastIterationResponse = "")
        => EvaluateAtHookPoint(packs, "OnBeforeIteration", systemPrompt, userQuery, agentId, tenantId, lastIterationResponse);

    /// <summary>Evaluate OnBeforeResponse rules against the final assembled response.</summary>
    public RuleEvalResult EvaluateOnBeforeResponse(
        List<ResolvedRulePack> packs, string responseText, string userQuery, string? agentId, int tenantId)
        => EvaluateAtHookPoint(packs, "OnBeforeResponse", responseText, userQuery, agentId, tenantId);

    /// <summary>Evaluate OnAfterToolCall rules against a tool's output text.</summary>
    public RuleEvalResult EvaluateOnAfterToolCall(
        List<ResolvedRulePack> packs, string toolOutput, string userQuery, string? agentId, int tenantId)
        => EvaluateAtHookPoint(packs, "OnAfterToolCall", toolOutput, userQuery, agentId, tenantId);

    /// <summary>Evaluate OnAfterResponse rules after the final response has been emitted.</summary>
    public RuleEvalResult EvaluateOnAfterResponse(
        List<ResolvedRulePack> packs, string responseText, string userQuery, string? agentId, int tenantId)
        => EvaluateAtHookPoint(packs, "OnAfterResponse", responseText, userQuery, agentId, tenantId);

    /// <summary>Evaluate OnToolFilter rules against planned tool calls before execution.</summary>
    public List<UnifiedToolCallRef> EvaluateOnToolFilter(
        List<ResolvedRulePack> packs,
        List<UnifiedToolCallRef> toolCalls,
        string userQuery,
        string? agentId,
        int tenantId)
    {
        var current = toolCalls
            .Select(tc => new UnifiedToolCallRef
            {
                Id = tc.Id,
                Name = tc.Name,
                InputJson = tc.InputJson,
                Filtered = tc.Filtered,
            })
            .ToList();

        foreach (var pack in packs)
        {
            var sw = Stopwatch.StartNew();

            foreach (var rule in pack.Rules.Where(r => r.HookPoint == "OnToolFilter"))
            {
                if (sw.ElapsedMilliseconds > pack.MaxEvaluationMs)
                {
                    _logger.LogWarning("Pack '{Pack}' exceeded max evaluation time ({Ms}ms), skipping remaining {HookPoint} rules",
                        pack.Name, pack.MaxEvaluationMs, "OnToolFilter");
                    break;
                }

                for (var index = 0; index < current.Count; index++)
                {
                    var result = EvaluateToolFilterRule(rule, current[index], userQuery);
                    LogExecution(pack.Id, rule.Id, agentId, tenantId, result.LogResult);

                    if (!result.LogResult.Triggered || result.ToolCall is null)
                        continue;

                    current[index] = result.ToolCall;
                    if (rule.StopOnMatch)
                        break;
                }
            }
        }

        return current;
    }

    /// <summary>Evaluate OnError rules and decide whether to continue, retry, or abort.</summary>
    public ErrorRuleEvalResult EvaluateOnError(
        List<ResolvedRulePack> packs,
        string? toolName,
        Exception exception,
        string userQuery,
        string? agentId,
        int tenantId)
    {
        var action = ErrorRecoveryAction.Continue;
        var triggeredRules = new List<TriggeredRuleInfo>();
        var errorText = exception.Message;

        foreach (var pack in packs)
        {
            var sw = Stopwatch.StartNew();

            foreach (var rule in pack.Rules.Where(r => r.HookPoint == "OnError"))
            {
                if (sw.ElapsedMilliseconds > pack.MaxEvaluationMs)
                {
                    _logger.LogWarning("Pack '{Pack}' exceeded max evaluation time ({Ms}ms), skipping remaining {HookPoint} rules",
                        pack.Name, pack.MaxEvaluationMs, "OnError");
                    break;
                }

                var result = EvaluateErrorRule(rule, toolName, errorText, userQuery);
                LogExecution(pack.Id, rule.Id, agentId, tenantId, result.LogResult);

                if (!result.LogResult.Triggered)
                    continue;

                triggeredRules.Add(new TriggeredRuleInfo(rule.Id, rule.RuleType, result.LogResult.Action));
                if (result.Action > action)
                    action = result.Action;

                if (rule.StopOnMatch)
                    break;
            }
        }

        return new ErrorRuleEvalResult(action, triggeredRules);
    }

    /// <summary>Dry-run test: evaluate all rules against sample data without side effects.</summary>
    public RulePackDryRunResult DryRun(HookRulePackEntity pack, string sampleQuery, string sampleResponse)
    {
        var onInitResult = new RuleEvalResult { ModifiedText = "" };
        var onBeforeIterationResult = new RuleEvalResult { ModifiedText = onInitResult.ModifiedText };
        var onBeforeResult = new RuleEvalResult { ModifiedText = sampleResponse };

        foreach (var rule in pack.Rules.Where(r => r.IsEnabled).OrderBy(r => r.OrderInPack))
        {
            if (rule.HookPoint == "OnInit")
            {
                var r = EvaluateRule(rule, onInitResult.ModifiedText, sampleQuery);
                if (r.Triggered) { onInitResult.ModifiedText = r.ModifiedText; onInitResult.TriggeredRules.Add(new(rule.Id, rule.RuleType, r.Action)); }
            }
            else if (rule.HookPoint == "OnBeforeIteration")
            {
                var baseText = string.IsNullOrEmpty(onBeforeIterationResult.ModifiedText)
                    ? onInitResult.ModifiedText
                    : onBeforeIterationResult.ModifiedText;
                var r = EvaluateRule(rule, baseText, sampleQuery);
                if (r.Triggered) { onBeforeIterationResult.ModifiedText = r.ModifiedText; onBeforeIterationResult.TriggeredRules.Add(new(rule.Id, rule.RuleType, r.Action)); }
                if (r.Blocked) onBeforeIterationResult.Blocked = true;
            }
            else if (rule.HookPoint == "OnBeforeResponse")
            {
                var r = EvaluateRule(rule, onBeforeResult.ModifiedText, sampleQuery);
                if (r.Triggered) { onBeforeResult.ModifiedText = r.ModifiedText; onBeforeResult.TriggeredRules.Add(new(rule.Id, rule.RuleType, r.Action)); }
                if (r.Blocked) onBeforeResult.Blocked = true;
            }
            else if (rule.HookPoint == "OnAfterToolCall")
            {
                var r = EvaluateRule(rule, onBeforeResult.ModifiedText, sampleQuery);
                if (r.Triggered) { onBeforeResult.ModifiedText = r.ModifiedText; onBeforeResult.TriggeredRules.Add(new(rule.Id, rule.RuleType, r.Action)); }
                if (r.Blocked) onBeforeResult.Blocked = true;
            }
        }

        var modifiedPrompt = string.IsNullOrEmpty(onBeforeIterationResult.ModifiedText)
            ? onInitResult.ModifiedText
            : onBeforeIterationResult.ModifiedText;

        return new RulePackDryRunResult(
            modifiedPrompt,
            onBeforeResult.ModifiedText,
            [.. onInitResult.TriggeredRules, .. onBeforeIterationResult.TriggeredRules, .. onBeforeResult.TriggeredRules],
            onBeforeIterationResult.Blocked || onBeforeResult.Blocked,
            onBeforeIterationResult.ModelSwitchRequest);
    }

    private RuleEvalResult EvaluateAtHookPoint(
        List<ResolvedRulePack> packs,
        string hookPoint,
        string initialText,
        string userQuery,
        string? agentId,
        int tenantId,
        string lastIterationResponse = "")
    {
        var result = new RuleEvalResult { ModifiedText = initialText };

        foreach (var pack in packs)
        {
            var sw = Stopwatch.StartNew();

            foreach (var rule in pack.Rules.Where(r => r.HookPoint == hookPoint))
            {
                if (sw.ElapsedMilliseconds > pack.MaxEvaluationMs)
                {
                    _logger.LogWarning("Pack '{Pack}' exceeded max evaluation time ({Ms}ms), skipping remaining {HookPoint} rules",
                        pack.Name, pack.MaxEvaluationMs, hookPoint);
                    break;
                }

                var ruleResult = EvaluateRule(rule, result.ModifiedText, userQuery, lastIterationResponse);
                LogExecution(pack.Id, rule.Id, agentId, tenantId, ruleResult);

                if (!ruleResult.Triggered)
                    continue;

                result.ModifiedText = ruleResult.ModifiedText;
                result.TriggeredRules.Add(new TriggeredRuleInfo(rule.Id, rule.RuleType, ruleResult.Action));

                // model_switch: extract model config from rule fields (first one wins)
                if (rule.RuleType == "model_switch" && result.ModelSwitchRequest is null)
                {
                    int? cfgId = int.TryParse(rule.ToolName, out var parsedCfg) ? parsedCfg : null;
                    int? mt    = int.TryParse(rule.Replacement, out var parsedMt) ? parsedMt : null;
                    var modelId = IsValidModelId(rule.Instruction) ? rule.Instruction : null;
                    if (!string.IsNullOrEmpty(rule.Instruction) && modelId is null)
                        _logger.LogWarning(
                            "Rule pack model_switch rule {RuleId} has an invalid Model ID (looks like description text, not a model ID). Ignoring Instruction field. Value: '{Value}'",
                            rule.Id, rule.Instruction);
                    result.ModelSwitchRequest = new ModelSwitchRequest(
                        ModelId:     modelId,
                        LlmConfigId: cfgId,
                        MaxTokens:   mt);
                }

                if (ruleResult.Blocked)
                {
                    result.Blocked = true;
                    return result;
                }

                if (rule.StopOnMatch)
                    break;
            }
        }

        return result;
    }

    private ToolFilterRuleEvalResult EvaluateToolFilterRule(HookRuleEntity rule, UnifiedToolCallRef toolCall, string userQuery)
    {
        try
        {
            return rule.RuleType switch
            {
                "block_pattern" => EvalToolFilterBlock(rule, toolCall),
                "tool_transform" => EvalToolFilterTransform(rule, toolCall),
                _ => new ToolFilterRuleEvalResult(toolCall, new SingleRuleResult { Triggered = false, ModifiedText = toolCall.InputJson, Action = "skipped" }),
            };
        }
        catch (RegexMatchTimeoutException)
        {
            _logger.LogWarning("Regex timeout for rule {RuleId} (type={Type})", rule.Id, rule.RuleType);
            return new ToolFilterRuleEvalResult(toolCall, new SingleRuleResult { Triggered = false, ModifiedText = toolCall.InputJson, Action = "timeout" });
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid regex pattern for rule {RuleId} (type={Type})", rule.Id, rule.RuleType);
            return new ToolFilterRuleEvalResult(toolCall, new SingleRuleResult { Triggered = false, ModifiedText = toolCall.InputJson, Action = "invalid_regex" });
        }
    }

    private ErrorRuleMatchResult EvaluateErrorRule(HookRuleEntity rule, string? toolName, string errorText, string userQuery)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(rule.ToolName)
                && !string.Equals(rule.ToolName, toolName, StringComparison.OrdinalIgnoreCase))
            {
                return new ErrorRuleMatchResult(
                    ErrorRecoveryAction.Continue,
                    new SingleRuleResult { Triggered = false, ModifiedText = errorText, Action = "skipped" });
            }

            if (!string.IsNullOrWhiteSpace(rule.Pattern))
            {
                var regex = GetOrCreateRegex(rule.Pattern);
                if (!regex.IsMatch(errorText))
                {
                    return new ErrorRuleMatchResult(
                        ErrorRecoveryAction.Continue,
                        new SingleRuleResult { Triggered = false, ModifiedText = errorText, Action = "skipped" });
                }
            }
            else if (string.IsNullOrWhiteSpace(rule.ToolName))
            {
                return new ErrorRuleMatchResult(
                    ErrorRecoveryAction.Continue,
                    new SingleRuleResult { Triggered = false, ModifiedText = errorText, Action = "skipped" });
            }

            var action = ParseRecoveryAction(rule.RuleType, rule.Instruction);
            return new ErrorRuleMatchResult(
                action,
                new SingleRuleResult { Triggered = true, ModifiedText = errorText, Action = action.ToString().ToLowerInvariant() });
        }
        catch (RegexMatchTimeoutException)
        {
            _logger.LogWarning("Regex timeout for rule {RuleId} (type={Type})", rule.Id, rule.RuleType);
            return new ErrorRuleMatchResult(
                ErrorRecoveryAction.Continue,
                new SingleRuleResult { Triggered = false, ModifiedText = errorText, Action = "timeout" });
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid regex pattern for rule {RuleId} (type={Type})", rule.Id, rule.RuleType);
            return new ErrorRuleMatchResult(
                ErrorRecoveryAction.Continue,
                new SingleRuleResult { Triggered = false, ModifiedText = errorText, Action = "invalid_regex" });
        }
    }

    // ── Rule evaluation core ──────────────────────────────────────────────────

    private SingleRuleResult EvaluateRule(HookRuleEntity rule, string text, string userQuery,
        string lastIterationResponse = "")
    {
        try
        {
            return rule.RuleType switch
            {
                "inject_prompt" => EvalInjectPrompt(rule, text),
                "tool_require" => EvalToolRequire(rule, text, userQuery),
                "format_response" => EvalFormatResponse(rule, text),
                "format_enforce" => EvalFormatEnforce(rule, text),
                "regex_redact" => EvalRegexRedact(rule, text),
                "append_text" => EvalAppendText(rule, text),
                "block_pattern" => EvalBlockPattern(rule, text),
                "require_keyword" => EvalRequireKeyword(rule, text),
                "tool_transform" => EvalToolTransform(rule, text, userQuery),
                "model_switch" => EvalModelSwitch(rule, text, userQuery, lastIterationResponse),
                _ => new SingleRuleResult { Triggered = false, ModifiedText = text, Action = "skipped" },
            };
        }
        catch (RegexMatchTimeoutException)
        {
            _logger.LogWarning("Regex timeout for rule {RuleId} (type={Type})", rule.Id, rule.RuleType);
            return new SingleRuleResult { Triggered = false, ModifiedText = text, Action = "timeout" };
        }
        catch (ArgumentException ex) // Covers RegexParseException (invalid patterns)
        {
            _logger.LogWarning(ex, "Invalid regex pattern for rule {RuleId} (type={Type})", rule.Id, rule.RuleType);
            return new SingleRuleResult { Triggered = false, ModifiedText = text, Action = "invalid_regex" };
        }
    }

    private SingleRuleResult EvalInjectPrompt(HookRuleEntity rule, string text)
    {
        if (string.IsNullOrWhiteSpace(rule.Instruction))
            return new() { Triggered = false, ModifiedText = text, Action = "skipped" };

        var modified = string.IsNullOrEmpty(text)
            ? rule.Instruction
            : text + "\n\n" + rule.Instruction;

        return new() { Triggered = true, ModifiedText = modified, Action = "injected" };
    }

    private SingleRuleResult EvalToolRequire(HookRuleEntity rule, string text, string userQuery)
    {
        if (string.IsNullOrWhiteSpace(rule.Pattern) || string.IsNullOrWhiteSpace(rule.ToolName))
            return new() { Triggered = false, ModifiedText = text, Action = "skipped" };

        var regex = GetOrCreateRegex(rule.Pattern);
        if (!regex.IsMatch(userQuery))
            return new() { Triggered = false, ModifiedText = text, Action = "skipped" };

        var instruction = $"\nIMPORTANT: When the user's request matches this context, you MUST use the '{rule.ToolName}' tool to get relevant data before responding.";
        return new() { Triggered = true, ModifiedText = text + instruction, Action = "injected" };
    }

    private SingleRuleResult EvalFormatResponse(HookRuleEntity rule, string text)
    {
        if (string.IsNullOrWhiteSpace(rule.Instruction))
            return new() { Triggered = false, ModifiedText = text, Action = "skipped" };

        var instruction = $"\nResponse Format Instructions: {rule.Instruction}";
        return new() { Triggered = true, ModifiedText = text + instruction, Action = "injected" };
    }

    private SingleRuleResult EvalFormatEnforce(HookRuleEntity rule, string text)
    {
        if (string.IsNullOrWhiteSpace(rule.Pattern))
            return new() { Triggered = false, ModifiedText = text, Action = "skipped" };

        // format_enforce checks that the response matches expected format
        var regex = GetOrCreateRegex(rule.Pattern);
        if (regex.IsMatch(text))
            return new() { Triggered = false, ModifiedText = text, Action = "skipped" };

        // Response doesn't match required format — wrap in correction notice
        var corrected = !string.IsNullOrWhiteSpace(rule.Instruction)
            ? $"{rule.Instruction}\n\n{text}"
            : text;
        return new() { Triggered = true, ModifiedText = corrected, Action = "modified" };
    }

    private SingleRuleResult EvalRegexRedact(HookRuleEntity rule, string text)
    {
        if (string.IsNullOrWhiteSpace(rule.Pattern))
            return new() { Triggered = false, ModifiedText = text, Action = "skipped" };

        var regex = GetOrCreateRegex(rule.Pattern);
        var replacement = rule.Replacement ?? "[REDACTED]";

        if (!regex.IsMatch(text))
            return new() { Triggered = false, ModifiedText = text, Action = "skipped" };

        var modified = regex.Replace(text, replacement);
        return new() { Triggered = true, ModifiedText = modified, Action = "modified" };
    }

    private static SingleRuleResult EvalAppendText(HookRuleEntity rule, string text)
    {
        if (string.IsNullOrWhiteSpace(rule.Instruction))
            return new() { Triggered = false, ModifiedText = text, Action = "skipped" };

        return new() { Triggered = true, ModifiedText = text + "\n\n" + rule.Instruction, Action = "appended" };
    }

    private SingleRuleResult EvalBlockPattern(HookRuleEntity rule, string text)
    {
        if (string.IsNullOrWhiteSpace(rule.Pattern))
            return new() { Triggered = false, ModifiedText = text, Action = "skipped" };

        var regex = GetOrCreateRegex(rule.Pattern);
        if (!regex.IsMatch(text))
            return new() { Triggered = false, ModifiedText = text, Action = "skipped" };

        var blockMsg = !string.IsNullOrWhiteSpace(rule.Instruction)
            ? rule.Instruction
            : "This response has been blocked by a content policy rule.";

        return new() { Triggered = true, ModifiedText = blockMsg, Blocked = true, Action = "blocked" };
    }

    private SingleRuleResult EvalRequireKeyword(HookRuleEntity rule, string text)
    {
        if (string.IsNullOrWhiteSpace(rule.Pattern))
            return new() { Triggered = false, ModifiedText = text, Action = "skipped" };

        // Pattern is the required keyword/phrase (case-insensitive check)
        if (text.Contains(rule.Pattern, StringComparison.OrdinalIgnoreCase))
            return new() { Triggered = false, ModifiedText = text, Action = "skipped" };

        // Keyword is missing — append it as a note
        var appendix = !string.IsNullOrWhiteSpace(rule.Instruction)
            ? rule.Instruction
            : $"\n\n> Note: {rule.Pattern}";

        return new() { Triggered = true, ModifiedText = text + appendix, Action = "appended" };
    }

    private SingleRuleResult EvalModelSwitch(HookRuleEntity rule, string text, string userQuery,
        string lastIterationResponse = "")
    {
        // Instruction = model ID string (same-provider switch)
        // ToolName    = LlmConfigId integer string (full config switch; takes precedence)
        // Replacement = optional max_tokens integer string
        // Pattern     = optional regex; matched against userQuery or lastIterationResponse per MatchTarget
        // MatchTarget = "query" (default) | "response"

        // Must have a valid model ID or a valid LlmConfigId to be actionable
        bool hasValidModelId = IsValidModelId(rule.Instruction);
        bool hasConfigId     = int.TryParse(rule.ToolName, out _);
        if (!hasValidModelId && !hasConfigId)
            return new() { Triggered = false, ModifiedText = text, Action = "skipped" };

        if (!string.IsNullOrWhiteSpace(rule.Pattern))
        {
            var matchTarget = rule.MatchTarget == "response" ? lastIterationResponse : userQuery;
            // If response-matching is requested but no response text exists yet (first iteration), skip
            if (string.IsNullOrEmpty(matchTarget))
                return new() { Triggered = false, ModifiedText = text, Action = "skipped" };
            var regex = GetOrCreateRegex(rule.Pattern);
            if (!regex.IsMatch(matchTarget))
                return new() { Triggered = false, ModifiedText = text, Action = "skipped" };
        }

        // model_switch must NOT modify ModifiedText — the runner reads ModelSwitchRequest separately
        return new() { Triggered = true, ModifiedText = text, Action = "model_switch" };
    }

    /// <summary>
    /// Returns true only if the string looks like a real model ID (no spaces, non-empty).
    /// Rejects accidental description text stored in the Instruction field.
    /// </summary>
    private static bool IsValidModelId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !value.Contains(' ');

    private SingleRuleResult EvalToolTransform(HookRuleEntity rule, string text, string userQuery)
    {
        // tool_transform injects an instruction to transform tool output format
        if (string.IsNullOrWhiteSpace(rule.Instruction))
            return new() { Triggered = false, ModifiedText = text, Action = "skipped" };

        if (!string.IsNullOrWhiteSpace(rule.Pattern))
        {
            var regex = GetOrCreateRegex(rule.Pattern);
            if (!regex.IsMatch(userQuery))
                return new() { Triggered = false, ModifiedText = text, Action = "skipped" };
        }

        return new() { Triggered = true, ModifiedText = text + "\n\n" + rule.Instruction, Action = "injected" };
    }

    private SingleRuleResult EvalMatchesToolCall(HookRuleEntity rule, UnifiedToolCallRef toolCall)
    {
        if (!string.IsNullOrWhiteSpace(rule.ToolName)
            && !string.Equals(rule.ToolName, toolCall.Name, StringComparison.OrdinalIgnoreCase))
        {
            return new() { Triggered = false, ModifiedText = toolCall.InputJson, Action = "skipped" };
        }

        if (string.IsNullOrWhiteSpace(rule.Pattern))
        {
            return new() { Triggered = true, ModifiedText = toolCall.InputJson, Action = "matched" };
        }

        var regex = GetOrCreateRegex(rule.Pattern);
        var target = $"{toolCall.Name}\n{toolCall.InputJson}";
        if (!regex.IsMatch(target))
            return new() { Triggered = false, ModifiedText = toolCall.InputJson, Action = "skipped" };

        return new() { Triggered = true, ModifiedText = toolCall.InputJson, Action = "matched" };
    }

    private ToolFilterRuleEvalResult EvalToolFilterBlock(HookRuleEntity rule, UnifiedToolCallRef toolCall)
    {
        var match = EvalMatchesToolCall(rule, toolCall);
        if (!match.Triggered)
            return new(toolCall, match);

        var updated = new UnifiedToolCallRef
        {
            Id = toolCall.Id,
            Name = toolCall.Name,
            InputJson = toolCall.InputJson,
            Filtered = true,
        };

        return new ToolFilterRuleEvalResult(updated, new SingleRuleResult { Triggered = true, ModifiedText = toolCall.InputJson, Action = "filtered" });
    }

    private ToolFilterRuleEvalResult EvalToolFilterTransform(HookRuleEntity rule, UnifiedToolCallRef toolCall)
    {
        if (!string.IsNullOrWhiteSpace(rule.ToolName)
            && !string.Equals(rule.ToolName, toolCall.Name, StringComparison.OrdinalIgnoreCase))
        {
            return new(toolCall, new SingleRuleResult { Triggered = false, ModifiedText = toolCall.InputJson, Action = "skipped" });
        }

        var replacement = rule.Replacement ?? rule.Instruction;
        if (string.IsNullOrWhiteSpace(replacement))
            return new(toolCall, new SingleRuleResult { Triggered = false, ModifiedText = toolCall.InputJson, Action = "skipped" });

        string modified;
        if (string.IsNullOrWhiteSpace(rule.Pattern))
        {
            modified = replacement;
        }
        else
        {
            var regex = GetOrCreateRegex(rule.Pattern);
            if (!regex.IsMatch(toolCall.InputJson))
                return new(toolCall, new SingleRuleResult { Triggered = false, ModifiedText = toolCall.InputJson, Action = "skipped" });
            modified = regex.Replace(toolCall.InputJson, replacement);
        }

        if (string.Equals(modified, toolCall.InputJson, StringComparison.Ordinal))
            return new(toolCall, new SingleRuleResult { Triggered = false, ModifiedText = toolCall.InputJson, Action = "skipped" });

        var updated = new UnifiedToolCallRef
        {
            Id = toolCall.Id,
            Name = toolCall.Name,
            InputJson = modified,
            Filtered = toolCall.Filtered,
        };

        return new(updated, new SingleRuleResult { Triggered = true, ModifiedText = modified, Action = "modified" });
    }

    private static ErrorRecoveryAction ParseRecoveryAction(string ruleType, string? instruction)
    {
        if (!string.IsNullOrWhiteSpace(instruction))
        {
            if (instruction.Contains("abort", StringComparison.OrdinalIgnoreCase))
                return ErrorRecoveryAction.Abort;
            if (instruction.Contains("retry", StringComparison.OrdinalIgnoreCase))
                return ErrorRecoveryAction.Retry;
            if (instruction.Contains("continue", StringComparison.OrdinalIgnoreCase))
                return ErrorRecoveryAction.Continue;
        }

        return ruleType switch
        {
            "block_pattern" => ErrorRecoveryAction.Abort,
            "tool_require" => ErrorRecoveryAction.Retry,
            _ => ErrorRecoveryAction.Continue,
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private bool MatchesArchetype(HookRulePackEntity pack, string archetype)
    {
        if (string.IsNullOrWhiteSpace(pack.AppliesToJson))
            return true; // null = applies to all

        try
        {
            var list = JsonSerializer.Deserialize<string[]>(pack.AppliesToJson);
            return list is null || list.Length == 0 || list.Contains(archetype, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
        }
    }

    private bool MatchesActivation(HookRulePackEntity pack, string userQuery, string archetype)
    {
        if (string.IsNullOrWhiteSpace(pack.ActivationCondition))
            return true; // null = always active

        var condition = pack.ActivationCondition;

        if (condition.StartsWith("regex:", StringComparison.OrdinalIgnoreCase))
        {
            var pattern = condition[6..];
            try
            {
                var regex = GetOrCreateRegex(pattern);
                return regex.IsMatch(userQuery);
            }
            catch { return false; }
        }

        if (condition.StartsWith("archetype:", StringComparison.OrdinalIgnoreCase))
        {
            var target = condition[10..];
            return string.Equals(target, archetype, StringComparison.OrdinalIgnoreCase);
        }

        // Default: treat as plain text match against query
        return userQuery.Contains(condition, StringComparison.OrdinalIgnoreCase);
    }

    private Regex GetOrCreateRegex(string pattern)
    {
        if (_regexCache.TryGetValue(pattern, out var existing))
            return existing;

        // Evict oldest entries if cache is too large
        if (_regexCache.Count >= MaxRegexCacheSize)
        {
            // Simple eviction: clear half the cache
            var keys = _regexCache.Keys.Take(MaxRegexCacheSize / 2).ToList();
            foreach (var k in keys) _regexCache.TryRemove(k, out _);
        }

        var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout);
        _regexCache.TryAdd(pattern, regex);
        return regex;
    }

    private void LogExecution(int packId, int ruleId, string? agentId, int tenantId, SingleRuleResult result)
    {
        // Gap #5: ruleId < 0 = virtual business rule; map to BusinessRuleId column instead of RuleId
        var log = new RuleExecutionLogEntity
        {
            PackId   = packId,
            AgentId  = agentId,
            TenantId = tenantId,
            Triggered = result.Triggered,
            Action    = result.Action,
            ElapsedMs = 0, // Per-rule timing is lightweight; pack-level Stopwatch handles overall timeout
        };
        if (ruleId < 0)
        {
            log.BusinessRuleId = Math.Abs(ruleId);
            log.RuleId = 0;
        }
        else
        {
            log.RuleId = ruleId;
        }
        _logChannel.Writer.TryWrite(log);
    }

    private async Task FlushLogsAsync()
    {
        var batch = new List<RuleExecutionLogEntity>();
        var token = _cts.Token;

        while (!token.IsCancellationRequested)
        {
            try
            {
                // Wait for items or timeout (flush every 5 seconds)
                while (batch.Count < 100 && _logChannel.Reader.TryRead(out var item))
                    batch.Add(item);

                if (batch.Count == 0)
                {
                    // Block until an item arrives or cancellation
                    try
                    {
                        var item = await _logChannel.Reader.ReadAsync(token);
                        batch.Add(item);
                    }
                    catch (OperationCanceledException) { break; }
                    continue;
                }

                using var db = _db.CreateDbContext();
                db.RuleExecutionLogs.AddRange(batch);
                await db.SaveChangesAsync(CancellationToken.None);
                batch.Clear();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to flush {Count} rule execution logs", batch.Count);
                batch.Clear();
                await Task.Delay(TimeSpan.FromSeconds(5), token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _cts.Cancel();
        _logChannel.Writer.TryComplete();
        try { await _logFlusher.WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (OperationCanceledException) { /* already cancelled */ }
        catch (Exception ex) { _logger.LogWarning(ex, "RulePackEngine: log flusher did not complete cleanly"); }
        _cts.Dispose();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _cts.Cancel();
        _logChannel.Writer.TryComplete();
        // Best-effort synchronous wait — prefer DisposeAsync for clean shutdown
        try { _logFlusher.Wait(TimeSpan.FromSeconds(5)); }
        catch { /* ignore */ }
        _cts.Dispose();
    }
}

// ── Result types ──────────────────────────────────────────────────────────────

public sealed class ResolvedRulePack(int id, string name, int priority, int maxEvaluationMs, List<HookRuleEntity> rules)
{
    public int Id { get; } = id;
    public string Name { get; } = name;
    public int Priority { get; } = priority;
    public int MaxEvaluationMs { get; } = maxEvaluationMs;
    public List<HookRuleEntity> Rules { get; } = rules;
}

public sealed class RuleEvalResult
{
    public string ModifiedText { get; set; } = "";
    public bool Blocked { get; set; }
    public List<TriggeredRuleInfo> TriggeredRules { get; } = [];

    /// <summary>
    /// Populated when a model_switch rule fires at OnBeforeIteration.
    /// The runner reads this to switch model/provider before the next LLM call.
    /// </summary>
    public ModelSwitchRequest? ModelSwitchRequest { get; set; }
}

/// <summary>
/// Data extracted from a model_switch Rule Pack rule.
/// Either <see cref="ModelId"/> (same-provider) or <see cref="LlmConfigId"/> (any provider) is set.
/// </summary>
public sealed record ModelSwitchRequest(
    string?  ModelId,      // same-provider model ID (Instruction field)
    int?     LlmConfigId,  // full config switch (ToolName field parsed as int; takes precedence)
    int?     MaxTokens);   // Replacement field parsed as int (optional)

public record TriggeredRuleInfo(int RuleId, string RuleType, string Action);

public record RulePackDryRunResult(
    string ModifiedPrompt,
    string ModifiedResponse,
    List<TriggeredRuleInfo> TriggeredRules,
    bool Blocked,
    ModelSwitchRequest? ModelSwitchRequest = null);

public record ErrorRuleEvalResult(
    ErrorRecoveryAction Action,
    List<TriggeredRuleInfo> TriggeredRules);

internal sealed class SingleRuleResult
{
    public bool Triggered { get; init; }
    public string ModifiedText { get; init; } = "";
    public bool Blocked { get; init; }
    public string Action { get; init; } = "skipped";
}

internal sealed record ToolFilterRuleEvalResult(UnifiedToolCallRef ToolCall, SingleRuleResult LogResult);

internal sealed record ErrorRuleMatchResult(ErrorRecoveryAction Action, SingleRuleResult LogResult);
