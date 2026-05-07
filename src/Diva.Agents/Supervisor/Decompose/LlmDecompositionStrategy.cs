using System.Text.Json;
using System.Text.RegularExpressions;
using Anthropic.SDK.Messaging;
using Diva.Core.Configuration;
using Diva.Core.Models;
using Diva.Infrastructure.LiteLLM;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Diva.Agents.Supervisor.Decompose;

/// <summary>
/// LLM-based decomposition for multi-step queries. Activated when at least 2 agents are
/// available AND no PreferredAgent is specified. Uses each agent's SupportedTools and
/// DelegateAgentIds so the LLM can make routing decisions based on true tool reach.
///
/// Provider + model are read from state.LlmOverride (set by OrchestratorAgent) with a
/// fallback to global IOptions&lt;LlmOptions&gt; — same two-path pattern as ResponseVerifier.
///
/// On any LLM error or parse failure, falls back to SingleTaskStrategy (no exception propagates).
/// Priority = 10 (beats SingleTaskStrategy which is 0).
/// </summary>
public sealed class LlmDecompositionStrategy : IDecompositionStrategy
{
    public const int MinAgentsForLlmDecomposition = 2;
    private const int MaxDecompositionTokens = 1024;

    private static readonly Regex JsonFencePattern =
        new(@"^```json?\s*|\s*```$", RegexOptions.Multiline | RegexOptions.Compiled);

    private readonly IAnthropicProvider _anthropic;
    private readonly IOpenAiProvider _openAi;
    private readonly LlmOptions _llm;
    private readonly SingleTaskStrategy _fallback;
    private readonly ILogger<LlmDecompositionStrategy> _logger;

    public int Priority => 10;

    public LlmDecompositionStrategy(
        IAnthropicProvider anthropic,
        IOpenAiProvider openAi,
        IOptions<LlmOptions> llm,
        SingleTaskStrategy fallback,
        ILogger<LlmDecompositionStrategy> logger)
    {
        _anthropic = anthropic;
        _openAi    = openAi;
        _llm       = llm.Value;
        _fallback  = fallback;
        _logger    = logger;
    }

    public bool CanHandle(SupervisorState state) =>
        state.AvailableAgents.Count >= MinAgentsForLlmDecomposition
        && string.IsNullOrEmpty(state.Request.PreferredAgent);

    public async Task<List<SubTask>> DecomposeAsync(SupervisorState state, CancellationToken ct)
    {
        try
        {
            var agentLines = state.AvailableAgents.Select(a =>
            {
                var cap       = a.GetCapability();
                var tools     = cap.SupportedTools.Length > 0
                    ? $"\n  tools: [{string.Join(", ", cap.SupportedTools)}]"
                    : string.Empty;
                var delegates = cap.DelegateAgentIds.Length > 0
                    ? $"\n  can also delegate to: [{string.Join(", ", cap.DelegateAgentIds)}]"
                    : string.Empty;
                return $"- {cap.AgentId}: {cap.Description}\n  capabilities: [{string.Join(", ", cap.Capabilities)}]{tools}{delegates}";
            }).ToList();

            _logger.LogInformation(
                "LlmDecompose: query={Query} agents={AgentCount} provider={Provider} model={Model}",
                state.Request.Query,
                agentLines.Count,
                state.LlmOverride?.Provider ?? _llm.DirectProvider.Provider,
                state.LlmOverride?.Model    ?? _llm.DirectProvider.Model);

            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("LlmDecompose: agent context =>\n{AgentContext}", string.Join("\n", agentLines));

            var prompt = BuildPrompt(state.Request.Query, agentLines);
            var raw    = await CallLlmAsync(prompt, state.LlmOverride, ct);

            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("LlmDecompose: raw LLM response => {Raw}", raw);

            var tasks  = ParseResponse(raw, state);

            if (tasks.Count == 0)
            {
                _logger.LogWarning("LlmDecompose: LLM returned 0 parseable tasks — falling back to SingleTaskStrategy");
                return await _fallback.DecomposeAsync(state, ct);
            }

            for (var i = 0; i < tasks.Count; i++)
                _logger.LogInformation(
                    "LlmDecompose: sub-task[{Index}/{Total}] capabilities=[{Caps}] description={Desc}",
                    i + 1, tasks.Count,
                    string.Join(", ", tasks[i].RequiredCapabilities),
                    tasks[i].Description);

            return tasks;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LlmDecompose: LLM call failed — falling back to SingleTaskStrategy");
            return await _fallback.DecomposeAsync(state, ct);
        }
    }

    private async Task<string> CallLlmAsync(string prompt, SupervisorLlmOverride? llmOverride, CancellationToken ct)
    {
        var provider = llmOverride?.Provider  ?? _llm.DirectProvider.Provider;
        var model    = llmOverride?.Model     ?? _llm.DirectProvider.Model;
        var endpoint = llmOverride?.Endpoint  ?? _llm.DirectProvider.Endpoint;
        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation(
                "LlmDecompose: calling {Provider} model={Model} endpoint={Endpoint} source={Source}",
                provider, model, endpoint,
                llmOverride is not null ? "coordinator-override" : "global-default");

        if (provider.Equals("Anthropic", StringComparison.OrdinalIgnoreCase))
        {
            var parameters = new MessageParameters
            {
                Model     = model,
                MaxTokens = MaxDecompositionTokens,
                System    = [new SystemMessage("You are a JSON-only task decomposition assistant. Respond ONLY with a valid JSON array, no markdown.")],
                Messages  =
                [
                    new Message
                    {
                        Role    = RoleType.User,
                        Content = [new Anthropic.SDK.Messaging.TextContent { Text = prompt }]
                    }
                ]
            };
            var msg = await _anthropic.GetClaudeMessageAsync(parameters, ct);
            var text = msg.Content.OfType<Anthropic.SDK.Messaging.TextContent>().FirstOrDefault()?.Text ?? "[]";
            _logger.LogDebug("LlmDecompose: Anthropic responded ({Chars} chars)", text.Length);
            return text;
        }

        // OpenAI-compatible (LiteLLM proxy, LM Studio, Azure, etc.)
        _logger.LogDebug("LlmDecompose: using OpenAI-compatible path endpoint={Endpoint}", endpoint ?? "(sdk default)");
        var chatClient = _openAi.CreateChatClient(model, endpointOverride: endpoint);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are a JSON-only task decomposition assistant. Respond ONLY with a valid JSON array, no markdown."),
            new(ChatRole.User, prompt)
        };
        var result = await chatClient.GetResponseAsync(messages,
            new ChatOptions { MaxOutputTokens = MaxDecompositionTokens }, ct);
        var responseText = result.Text ?? "[]";
        _logger.LogDebug("LlmDecompose: OpenAI-compatible responded ({Chars} chars)", responseText.Length);
        return responseText;
    }

    private List<SubTask> ParseResponse(string raw, SupervisorState state)
    {
        var json  = JsonFencePattern.Replace(raw.Trim(), "").Trim();
        var start = json.IndexOf('[');
        var end   = json.LastIndexOf(']');
        if (start < 0 || end <= start) return [];

        json = json[start..(end + 1)];

        using var doc  = JsonDocument.Parse(json);
        var tasks = new List<SubTask>();

        foreach (var element in doc.RootElement.EnumerateArray())
        {
            var description = element.TryGetProperty("description", out var d) ? d.GetString() : null;
            if (string.IsNullOrWhiteSpace(description)) continue;

            var caps = new List<string>();
            if (element.TryGetProperty("required_capabilities", out var rc) && rc.ValueKind == JsonValueKind.Array)
                foreach (var cap in rc.EnumerateArray())
                    if (cap.GetString() is { Length: > 0 } s) caps.Add(s);

            tasks.Add(new SubTask(
                Description:          description,
                RequiredCapabilities: [.. caps],
                SiteId:               state.TenantContext.CurrentSiteId,
                TenantId:             state.TenantContext.TenantId,
                Instructions:         state.SupervisorInstructions));
        }

        return tasks;
    }

    private static string BuildPrompt(string query, IEnumerable<string> agentLines) =>
        "You are decomposing a user request into parallel sub-tasks, each routed to a specific agent.\n\n" +
        "AVAILABLE AGENTS:\n" +
        string.Join("\n", agentLines) + "\n\n" +
        "USER REQUEST:\n" +
        query + "\n\n" +
        "Rules:\n" +
        "1. Only decompose if sub-tasks can run FULLY IN PARALLEL with no data dependencies.\n" +
        "2. If task B needs the output of task A — keep them as ONE sub-task assigned to the agent\n" +
        "   that can handle both (possibly via delegation). Never split sequentially-dependent tasks.\n" +
        "3. If a single agent can handle the full request (including via delegation) — return ONE sub-task.\n" +
        "4. Match required_capabilities to the capability strings listed above (case-insensitive).\n" +
        "5. Each sub-task description must be fully self-contained — include all context the agent needs.\n\n" +
        "Respond ONLY with a JSON array (no markdown, no explanation):\n" +
        "[{\"description\": \"...\", \"required_capabilities\": [\"cap1\"]}]";
}
