using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic.SDK.Messaging;
using Diva.Core.Configuration;
using Diva.Infrastructure.Context;
using Diva.Infrastructure.Sessions;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace Diva.Infrastructure.LiteLLM;

/// <summary>
/// <see cref="ILlmProviderStrategy"/> implementation for the Anthropic native SDK.
/// Created per-execution — holds mutable <see cref="Message"/> list + provider refs.
///
/// Prompt caching strategy (Anthropic breakpoints managed manually via PromptCacheType.FineGrained):
///   BP1 — static system block (base prompt + overrides + group rules): cache_control set in BuildSystemBlocks()
///   BP2 — tool definitions:                                            NOT set manually (Anthropic.SDK.Common.Tool
///          has no CacheControl property in SDK 5.10.0; relies on Anthropic API auto-caching tools when present)
///   BP3 — last prior-session history message:                          cache_control set in Initialize()
///   BP4 — most-recent tool-result message (sliding):                   cache_control moved in AddToolResults()
/// </summary>
internal sealed class AnthropicProviderStrategy : ILlmProviderStrategy
{
    private readonly IAnthropicProvider _anthropic;
    private readonly IContextWindowManager _ctx;
    private string _model;
    private int _maxTokens;
    private readonly bool _enableHistoryCaching;
    private string? _apiKeyOverride;
    private readonly Func<Func<Task<MessageResponse>>, CancellationToken, Task<MessageResponse>> _retry;

    // System prompt split: static (cacheable across sessions) + dynamic (volatile per session).
    private string _staticSystemPrompt;    // stable: base + group/tenant overrides + group rules
    private string _dynamicSystemPrompt;   // volatile: session rules, hook injections, caller instructions

    private List<Message> _messages = [];
    private IList<Anthropic.SDK.Common.Tool>? _tools;
    private MessageResponse? _lastResponse;
    private List<ContentBase>? _lastStreamedContent;
    private TokenUsage _lastTokenUsage;

    // Tracks the tool-results message that currently holds the sliding BP4 cache breakpoint.
    private Message? _slidingCacheBoundary;

    public AnthropicProviderStrategy(
        IAnthropicProvider anthropic,
        IContextWindowManager ctx,
        string model,
        int maxTokens,
        string staticSystemPrompt,
        string dynamicSystemPrompt,
        Func<Func<Task<MessageResponse>>, CancellationToken, Task<MessageResponse>> retry,
        bool enableHistoryCaching = true,
        string? apiKeyOverride = null)
    {
        _anthropic            = anthropic;
        _ctx                  = ctx;
        _model                = model;
        _maxTokens            = maxTokens;
        _staticSystemPrompt   = staticSystemPrompt;
        _dynamicSystemPrompt  = dynamicSystemPrompt;
        _enableHistoryCaching = enableHistoryCaching;
        _apiKeyOverride       = apiKeyOverride;
        _retry                = retry;
    }

    // ── System block builder ──────────────────────────────────────────────────

    /// <summary>
    /// Builds the System parameter for Anthropic API calls.
    /// When caching is enabled: two blocks — static (BP1, ephemeral) + dynamic (no marker).
    /// When caching is disabled: one combined block.
    /// </summary>
    private List<SystemMessage> BuildSystemBlocks()
    {
        var blocks = new List<SystemMessage>();

        if (_enableHistoryCaching)
        {
            // BP1: static block — marked ephemeral; stable across sessions for same agent+tenant.
            var staticBlock = new SystemMessage(_staticSystemPrompt);
            staticBlock.CacheControl = new CacheControl { Type = CacheControlType.ephemeral };
            blocks.Add(staticBlock);

            // Dynamic block — no cache_control; changes per session.
            if (!string.IsNullOrEmpty(_dynamicSystemPrompt))
                blocks.Add(new SystemMessage(_dynamicSystemPrompt));
        }
        else
        {
            // Caching disabled: single combined block (matches pre-caching behaviour).
            var combined = string.IsNullOrEmpty(_dynamicSystemPrompt)
                ? _staticSystemPrompt
                : _staticSystemPrompt + "\n\n" + _dynamicSystemPrompt;
            blocks.Add(new SystemMessage(combined));
        }

        return blocks;
    }

    // ── Initialisation ────────────────────────────────────────────────────────

    public void Initialize(string systemPrompt, List<ConversationTurn> history, string userQuery, List<McpClientTool> mcpTools)
    {
        _messages             = new List<Message>();
        _slidingCacheBoundary = null;

        foreach (var turn in history)
            _messages.Add(new Message
            {
                Role    = turn.Role == "assistant" ? RoleType.Assistant : RoleType.User,
                Content = [new Anthropic.SDK.Messaging.TextContent { Text = turn.Content }]
            });

        // BP3: mark the last history message's last content block as cacheable.
        // Caches everything before the current user query (BP1+BP2+history → single cache hit).
        if (_enableHistoryCaching && _messages.Count > 0)
        {
            var lastHist = _messages[^1];
            if (lastHist.Content is { Count: > 0 })
                lastHist.Content[^1].CacheControl = new CacheControl { Type = CacheControlType.ephemeral };
        }

        _messages.Add(new Message
        {
            Role    = RoleType.User,
            Content = [new Anthropic.SDK.Messaging.TextContent { Text = userQuery }]
        });

        _tools = mcpTools.Count > 0 ? mcpTools.Select(t => ToAnthropicTool(t)).ToList() : null;
        // Note: Anthropic.SDK.Common.Tool has no CacheControl property; tool caching is
        // handled by PromptCacheType.AutomaticToolsAndSystem in the request parameters.
    }

    /// <inheritdoc/>
    public void AddExtraTools(IReadOnlyList<AIFunction> tools)
    {
        if (tools.Count == 0) return;
        _tools ??= new List<Anthropic.SDK.Common.Tool>();
        foreach (var tool in tools)
            _tools.Add(ToAnthropicTool(tool));
    }

    // ── LLM calls ─────────────────────────────────────────────────────────────

    public async Task<UnifiedLlmResponse> CallLlmAsync(CancellationToken ct)
    {
        // Clear any stale streamed content so CommitAssistantResponse uses _lastResponse.
        _lastStreamedContent = null;

        var parameters = new MessageParameters
        {
            Model         = _model,
            MaxTokens     = _maxTokens,
            System        = BuildSystemBlocks(),
            Messages      = _messages,
            Tools         = _tools,
            ToolChoice    = _tools is { Count: > 0 }
                ? new ToolChoice { Type = ToolChoiceType.Auto, DisableParallelToolUse = false }
                : null,
            PromptCaching = PromptCacheType.FineGrained   // respects CacheControl on individual blocks
        };

        var response = await _retry(() => _anthropic.GetClaudeMessageAsync(parameters, ct, _apiKeyOverride), ct);
        _lastResponse = response;
        _lastTokenUsage = new TokenUsage(
            response.Usage?.InputTokens              ?? 0,
            response.Usage?.OutputTokens             ?? 0,
            response.Usage?.CacheReadInputTokens     ?? 0,
            response.Usage?.CacheCreationInputTokens ?? 0);

        var text = string.Join("\n", response.Content
            .OfType<Anthropic.SDK.Messaging.TextContent>()
            .Select(b => b.Text));

        var toolCalls = response.StopReason == "tool_use"
            ? response.Content.OfType<ToolUseContent>()
                .Select(tu => new UnifiedToolCall(tu.Id, tu.Name, tu.Input?.ToString() ?? "{}"))
                .ToList()
            : [];

        return new UnifiedLlmResponse(text, toolCalls, toolCalls.Count > 0, response.StopReason);
    }

    /// <inheritdoc/>
    public TokenUsage LastTokenUsage => _lastTokenUsage;

    public async IAsyncEnumerable<UnifiedStreamDelta> StreamLlmAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var parameters = new MessageParameters
        {
            Model         = _model,
            MaxTokens     = _maxTokens,
            System        = BuildSystemBlocks(),
            Messages      = _messages,
            Tools         = _tools,
            ToolChoice    = _tools is { Count: > 0 }
                ? new ToolChoice { Type = ToolChoiceType.Auto, DisableParallelToolUse = false }
                : null,
            PromptCaching = PromptCacheType.FineGrained   // respects CacheControl on individual blocks
        };

        // Clear stale state from any previous call so CommitAssistantResponse always reflects
        // this iteration. If the stream throws before we set _lastStreamedContent, the
        // fallback CallLlmAsync will set _lastResponse instead.
        _lastStreamedContent = null;
        _lastResponse        = null;

        var outputs = new List<MessageResponse>();
        string accText = "";

        await foreach (var response in _anthropic.StreamClaudeMessageAsync(parameters, ct, _apiKeyOverride))
        {
            outputs.Add(response);
            var deltaText = response.Delta?.Text;
            if (!string.IsNullOrEmpty(deltaText))
            {
                accText += deltaText;
                yield return new UnifiedStreamDelta(deltaText, false, null);
            }
        }

        // ── Token usage ───────────────────────────────────────────────────────
        // input tokens are on the message_start event (StreamStartMessage.Usage);
        // output tokens are on message_delta (last event with Usage.OutputTokens > 0).
        var startEvent = outputs.FirstOrDefault(r => r.StreamStartMessage?.Usage is not null);
        var finalUsage = outputs.LastOrDefault(r => r.Usage?.OutputTokens > 0);
        _lastTokenUsage = new TokenUsage(
            startEvent?.StreamStartMessage?.Usage?.InputTokens              ?? 0,
            finalUsage?.Usage?.OutputTokens                                 ?? 0,
            startEvent?.StreamStartMessage?.Usage?.CacheReadInputTokens     ?? 0,
            startEvent?.StreamStartMessage?.Usage?.CacheCreationInputTokens ?? 0);

        // ── Build content list for CommitAssistantResponse ────────────────────
        // Scan ALL outputs and collect tool calls deduplicated by ID.
        // The Anthropic SDK streaming state machine puts ToolCalls on the message_delta
        // event but NOT on the final message_stop event, so outputs[^1].ToolCalls is null
        // when parallel tool calls are returned. Scanning all outputs and deduping by ID
        // captures every tool call regardless of which event it appears on.
        var last = outputs.Count > 0 ? outputs[^1] : null;
        var contentBlocks = new List<ContentBase>();
        if (!string.IsNullOrEmpty(accText))
            contentBlocks.Add(new Anthropic.SDK.Messaging.TextContent { Text = accText });

        var toolCalls  = new List<UnifiedToolCall>();
        var seenIds    = new HashSet<string>(StringComparer.Ordinal);
        foreach (var resp in outputs)
        {
            if (resp.ToolCalls is not { Count: > 0 } funcs) continue;
            foreach (var f in funcs)
            {
                if (string.IsNullOrEmpty(f.Id) || !seenIds.Add(f.Id)) continue;

                // f.Arguments from streaming is JsonValuePrimitive<string> — not a JsonObject.
                // ToString() returns the raw JSON text; re-parse to get a proper JsonObject.
                // Guard against null/empty (tool with no args produces "" not "{}" in the SDK).
                var rawArgs = f.Arguments?.ToString();
                if (string.IsNullOrWhiteSpace(rawArgs)) rawArgs = "{}";
                var inputNode = JsonNode.Parse(rawArgs) ?? new JsonObject();
                contentBlocks.Add(new ToolUseContent { Id = f.Id, Name = f.Name, Input = inputNode });
                toolCalls.Add(new UnifiedToolCall(f.Id, f.Name, rawArgs));
            }
        }
        _lastStreamedContent = contentBlocks.Count > 0 ? contentBlocks : null;

        var stopReason = last?.StopReason ?? (toolCalls.Count > 0 ? "tool_use" : "end_turn");
        yield return new UnifiedStreamDelta(null, true,
            new UnifiedLlmResponse(accText, toolCalls, toolCalls.Count > 0, stopReason));
    }

    public async Task<string?> CallReplanAsync(CancellationToken ct)
    {
        var parameters = new MessageParameters
        {
            Model         = _model,
            MaxTokens     = 2048,
            System        = BuildSystemBlocks(),
            Messages      = _messages,
            Tools         = null,
            PromptCaching = PromptCacheType.FineGrained   // respects CacheControl on individual blocks
        };

        var response = await _anthropic.GetClaudeMessageAsync(parameters, ct, _apiKeyOverride);
        var text = string.Join("\n", response.Content
            .OfType<Anthropic.SDK.Messaging.TextContent>()
            .Select(b => b.Text));
        return string.IsNullOrEmpty(text) ? null : text;
    }

    // ── Message mutations ─────────────────────────────────────────────────────

    public void CommitAssistantResponse()
    {
        if (_lastStreamedContent is not null)
        {
            _messages.Add(new Message { Role = RoleType.Assistant, Content = _lastStreamedContent });
            _lastStreamedContent = null;
            return;
        }
        if (_lastResponse is null) return;
        _messages.Add(new Message { Role = RoleType.Assistant, Content = _lastResponse.Content });
    }

    public void AddToolResults(IReadOnlyList<UnifiedToolResult> results)
    {
        var toolResults = results.Select(r => (ContentBase)new Anthropic.SDK.Messaging.ToolResultContent
        {
            ToolUseId = r.ToolCallId,
            Content   = [new Anthropic.SDK.Messaging.TextContent { Text = r.Output }],
            IsError   = r.IsError
        }).ToList();

        var newMessage = new Message { Role = RoleType.User, Content = toolResults };
        _messages.Add(newMessage);

        // BP4 (sliding): move the cache marker to the newest tool-result block.
        // Clear the old marker first so we never have more than 4 total breakpoints.
        if (_enableHistoryCaching)
        {
            if (_slidingCacheBoundary?.Content is { Count: > 0 } prev)
                prev[^1].CacheControl = null;

            if (newMessage.Content.Count > 0)
                newMessage.Content[^1].CacheControl = new CacheControl { Type = CacheControlType.ephemeral };

            _slidingCacheBoundary = newMessage;
        }
    }

    public void AddUserMessage(string text) =>
        _messages.Add(new Message
        {
            Role    = RoleType.User,
            Content = [new Anthropic.SDK.Messaging.TextContent { Text = text }]
        });

    public void AddAssistantThenUser(string assistantText, string userText)
    {
        _messages.Add(new Message
        {
            Role    = RoleType.Assistant,
            Content = [new Anthropic.SDK.Messaging.TextContent { Text = assistantText }]
        });
        _messages.Add(new Message
        {
            Role    = RoleType.User,
            Content = [new Anthropic.SDK.Messaging.TextContent { Text = userText }]
        });
    }

    /// <summary>
    /// Updates the dynamic (volatile) system prompt block only.
    /// Called by hooks that inject per-iteration content (rule packs, session context).
    /// The static block (_staticSystemPrompt) is never mutated after construction.
    /// </summary>
    public void UpdateSystemPrompt(string dynamicSystemPrompt) => _dynamicSystemPrompt = dynamicSystemPrompt;

    // ── Context window management ─────────────────────────────────────────────

    public void CompactHistory(string systemPrompt, ContextWindowOverrideOptions? agentOverride)
    {
        _messages             = _ctx.MaybeCompactAnthropicMessages(_messages, systemPrompt, agentOverride);
        _slidingCacheBoundary = null;   // _messages is a new allocation; old refs are stale

        // After compaction, orphaned CacheControl markers from BP3/BP4 may survive on kept
        // messages (the tail is copied by reference). Strip them all, then re-set BP3 so
        // we never exceed Anthropic's 4-breakpoint limit.
        if (_enableHistoryCaching)
            ResetCacheMarkersAfterCompaction();
    }

    public void PrepareNewWindow(string continuationContext, string systemPrompt, ContextWindowOverrideOptions? agentOverride)
    {
        _messages             = _ctx.MaybeCompactAnthropicMessages(_messages, systemPrompt, agentOverride);
        _slidingCacheBoundary = null;

        if (_enableHistoryCaching)
            ResetCacheMarkersAfterCompaction();

        _messages.Add(new Message
        {
            Role    = RoleType.User,
            Content = [new Anthropic.SDK.Messaging.TextContent { Text = continuationContext }]
        });
        // No cache_control on the continuation context message — it is short, changes every
        // window, and a breakpoint here would consume BP4 before any tool results appear.
    }

    /// <summary>
    /// Clears all CacheControl markers from message content blocks, then re-sets BP3
    /// on the last message before the user query (if history exists).
    /// Called after compaction to prevent orphaned markers from accumulating past
    /// the Anthropic API's 4-breakpoint limit.
    /// </summary>
    private void ResetCacheMarkersAfterCompaction()
    {
        // Strip all CacheControl from every content block in the message list.
        foreach (var msg in _messages)
        {
            if (msg.Content is null) continue;
            foreach (var block in msg.Content)
                block.CacheControl = null;
        }

        // Re-set BP3: mark the last message before the trailing user query as cacheable.
        // After compaction the structure is [first, summary, ...kept_tail].
        // The "last history" message to cache is the one just before the final user message.
        if (_messages.Count >= 2)
        {
            var lastHist = _messages[^1]; // best candidate — last message in compacted list
            // Walk back to find a message with content (skip empty).
            for (int j = _messages.Count - 1; j >= 0; j--)
            {
                if (_messages[j].Content is { Count: > 0 })
                {
                    _messages[j].Content[^1].CacheControl = new CacheControl { Type = CacheControlType.ephemeral };
                    break;
                }
            }
        }
    }

    // ── Per-iteration model switching ─────────────────────────────────────────

    /// <inheritdoc/>
    public void SetModel(string model, int? maxTokens = null, string? apiKeyOverride = null, string? endpointOverride = null)
    {
        _model = model;
        if (maxTokens.HasValue) _maxTokens = maxTokens.Value;
        if (apiKeyOverride is not null) _apiKeyOverride = apiKeyOverride;
        // endpointOverride ignored for Anthropic SDK (no per-call endpoint support)
    }

    // ── Cross-provider history transfer ───────────────────────────────────────

    /// <inheritdoc/>
    public List<UnifiedHistoryEntry> ExportHistory()
    {
        var result = new List<UnifiedHistoryEntry>();
        foreach (var msg in _messages)
        {
            var parts = new List<UnifiedHistoryPart>();
            foreach (var block in msg.Content)
            {
                switch (block)
                {
                    case Anthropic.SDK.Messaging.TextContent tc when !string.IsNullOrEmpty(tc.Text):
                        parts.Add(new TextHistoryPart(tc.Text));
                        break;
                    case ToolUseContent tu:
                        parts.Add(new ToolCallHistoryPart(tu.Id, tu.Name, tu.Input?.ToString() ?? "{}"));
                        break;
                    case Anthropic.SDK.Messaging.ToolResultContent tr:
                        var output = tr.Content?.OfType<Anthropic.SDK.Messaging.TextContent>()
                                        .FirstOrDefault()?.Text ?? "";
                        parts.Add(new ToolResultHistoryPart(tr.ToolUseId, output, tr.IsError ?? false));
                        break;
                }
            }
            if (parts.Count > 0)
                result.Add(new UnifiedHistoryEntry
                {
                    Role  = msg.Role == RoleType.Assistant ? "assistant" : "user",
                    Parts = parts
                });
        }
        return result;
    }

    /// <inheritdoc/>
    public void ImportHistory(List<UnifiedHistoryEntry> history, string systemPrompt, List<McpClientTool> tools)
    {
        // After a cross-provider model switch the coordinator only has the combined system prompt.
        // Treat it as the static block; dynamic starts empty (hooks will re-inject at OnBeforeIteration).
        _staticSystemPrompt   = systemPrompt;
        _dynamicSystemPrompt  = string.Empty;
        _messages             = new List<Message>();
        _lastResponse         = null;
        _slidingCacheBoundary = null;

        foreach (var entry in history)
        {
            var role    = entry.Role == "assistant" ? RoleType.Assistant : RoleType.User;
            var content = new List<ContentBase>();

            foreach (var part in entry.Parts)
            {
                switch (part)
                {
                    case TextHistoryPart tp:
                        content.Add(new Anthropic.SDK.Messaging.TextContent { Text = tp.Text });
                        break;
                    case ToolCallHistoryPart tc:
                        content.Add(new ToolUseContent
                        {
                            Id    = tc.Id,
                            Name  = tc.Name,
                            Input = JsonNode.Parse(tc.InputJson)
                        });
                        break;
                    case ToolResultHistoryPart tr:
                        content.Add(new Anthropic.SDK.Messaging.ToolResultContent
                        {
                            ToolUseId = tr.ToolCallId,
                            Content   = [new Anthropic.SDK.Messaging.TextContent { Text = tr.Output }],
                            IsError   = tr.IsError
                        });
                        break;
                }
            }

            if (content.Count > 0)
                _messages.Add(new Message { Role = role, Content = content });
        }

        _tools = tools.Count > 0 ? tools.Select(t => ToAnthropicTool(t)).ToList() : null;
        // Note: Tool has no CacheControl property in Anthropic.SDK 5.10.0; tool caching is
        // handled via PromptCacheType.FineGrained on the system block (BP1).
    }

    // ── Anthropic SDK tool conversion ─────────────────────────────────────────

    internal static Anthropic.SDK.Common.Tool ToAnthropicTool(AIFunction tool)
    {
        var schemaNode = JsonNode.Parse(tool.JsonSchema.GetRawText());
        var func = new Anthropic.SDK.Common.Function(
            tool.Name,
            tool.Description ?? string.Empty,
            schemaNode!);
        return new Anthropic.SDK.Common.Tool(func);
    }

    // ── Internal test accessors ───────────────────────────────────────────────

    /// <summary>For tests only. Returns the CacheControl on the last history message's last block (BP3).</summary>
    internal CacheControl? GetHistoryBoundaryCacheControl()
    {
        // History boundary is on the message immediately before the user query.
        // _messages[^1] = user query; _messages[^2] = last history turn (if it exists).
        if (_messages.Count < 2) return null;
        var hist = _messages[^2];
        return hist.Content is { Count: > 0 } ? hist.Content[^1].CacheControl : null;
    }

    /// <summary>For tests only. Returns the content block that currently holds the sliding BP4 cache marker.</summary>
    internal ContentBase? GetSlidingBoundaryContent()
        => _slidingCacheBoundary?.Content is { Count: > 0 }
            ? _slidingCacheBoundary.Content[^1]
            : null;

    /// <summary>For tests only. Counts all content blocks with CacheControl set across all messages.</summary>
    internal int CountCacheControlMarkers()
    {
        int count = 0;
        foreach (var msg in _messages)
        {
            if (msg.Content is null) continue;
            foreach (var block in msg.Content)
                if (block.CacheControl is not null) count++;
        }
        return count;
    }
}
