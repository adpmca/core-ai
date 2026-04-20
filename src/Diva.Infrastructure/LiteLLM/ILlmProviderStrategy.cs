using Diva.Core.Configuration;
using Diva.Infrastructure.Sessions;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace Diva.Infrastructure.LiteLLM;

/// <summary>
/// Abstracts LLM provider differences (Anthropic SDK vs OpenAI-compatible IChatClient)
/// so the ReAct loop can be implemented once regardless of provider.
/// Strategies are created per-execution — lightweight, holding only the message list and provider refs.
/// </summary>
internal interface ILlmProviderStrategy
{
    /// <summary>Build initial message history from conversation turns + user query, and set tool definitions.</summary>
    void Initialize(string systemPrompt, List<ConversationTurn> history, string userQuery, List<McpClientTool> mcpTools);

    /// <summary>Add extra tools (e.g. agent delegation tools) to the existing tool set after initialization.</summary>
    void AddExtraTools(IReadOnlyList<AIFunction> tools);

    /// <summary>Call the LLM and return a unified response. Stores the raw response internally for <see cref="CommitAssistantResponse"/>.</summary>
    Task<UnifiedLlmResponse> CallLlmAsync(CancellationToken ct);

    /// <summary>Token usage from the most recent LLM call. All fields zero if not yet called or unavailable.</summary>
    TokenUsage LastTokenUsage { get; }

    /// <summary>
    /// Stream the LLM response token by token. Yields <see cref="UnifiedStreamDelta"/> per token,
    /// followed by a final item with <see cref="UnifiedStreamDelta.IsDone"/> = true.
    /// Falls back to a single buffered call if the provider does not support streaming.
    /// </summary>
    IAsyncEnumerable<UnifiedStreamDelta> StreamLlmAsync(CancellationToken ct);

    /// <summary>No-tools LLM call for adaptive re-planning (reduced MaxTokens). Returns revised text or null.</summary>
    Task<string?> CallReplanAsync(CancellationToken ct);

    /// <summary>Add the last raw LLM response to the internal message history as an assistant message.</summary>
    void CommitAssistantResponse();

    /// <summary>Update the current system prompt so later LLM calls use the latest hook-mutated prompt.</summary>
    void UpdateSystemPrompt(string systemPrompt);

    /// <summary>Add tool results to the internal message history (respects OpenAI ordering rules).</summary>
    void AddToolResults(IReadOnlyList<UnifiedToolResult> results);

    /// <summary>Append a user message to the internal message history.</summary>
    void AddUserMessage(string text);

    /// <summary>Append an assistant message then a user message (used for error retry + verification correction).</summary>
    void AddAssistantThenUser(string assistantText, string userText);

    /// <summary>Point A: in-run context compaction. Modifies internal message list in place.</summary>
    void CompactHistory(string systemPrompt, ContextWindowOverrideOptions? agentOverride);

    /// <summary>Prepare for a new continuation window: compact + inject continuation context message.</summary>
    void PrepareNewWindow(string continuationContext, string systemPrompt, ContextWindowOverrideOptions? agentOverride);

    /// <summary>
    /// Switch the active model (and optionally max_tokens, API key, endpoint) for the NEXT
    /// <see cref="CallLlmAsync"/> call. In-flight message history is preserved.
    /// Use this for same-provider model switches and same-provider cross-endpoint switches.
    /// For cross-provider switches (Anthropic ↔ OpenAI), use
    /// <see cref="ExportHistory"/> + new strategy + <see cref="ImportHistory"/> instead.
    /// </summary>
    void SetModel(string model, int? maxTokens = null, string? apiKeyOverride = null, string? endpointOverride = null);

    /// <summary>
    /// Export the current in-flight message history to a provider-agnostic format.
    /// Called before discarding the strategy on a cross-provider switch so that history
    /// can be imported into the new strategy via <see cref="ImportHistory"/>.
    /// </summary>
    List<UnifiedHistoryEntry> ExportHistory();

    /// <summary>
    /// Import message history from a provider-agnostic format.
    /// Replaces what <see cref="Initialize"/> would have built from ConversationTurns.
    /// Used after creating a new strategy when switching providers mid-execution.
    /// <paramref name="tools"/> re-registers MCP tool definitions on the new strategy.
    /// </summary>
    void ImportHistory(List<UnifiedHistoryEntry> history, string systemPrompt, List<ModelContextProtocol.Client.McpClientTool> tools);
}

/// <summary>Unified LLM response — provider-agnostic representation.</summary>
internal sealed record UnifiedLlmResponse(
    string Text,
    IReadOnlyList<UnifiedToolCall> ToolCalls,
    bool HasToolCalls,
    string? StopReason = null);

/// <summary>A single tool call extracted from the LLM response.</summary>
internal sealed record UnifiedToolCall(string Id, string Name, string InputJson);

/// <summary>A tool execution result to be fed back to the LLM.</summary>
internal sealed record UnifiedToolResult(string ToolCallId, string ToolName, string Output, bool IsError);

/// <summary>
/// Token counts from the most recent LLM call. Fields default to 0 when unavailable.
/// <para>For Anthropic: <see cref="CacheRead"/> and <see cref="CacheCreation"/> are populated when
/// <c>PromptCaching = AutomaticToolsAndSystem</c> is set. <see cref="Input"/> reflects only the
/// non-cached portion; <see cref="TotalEffectiveInput"/> gives the full context size.</para>
/// <para>For OpenAI-compatible providers: <see cref="CacheRead"/> and <see cref="CacheCreation"/>
/// are always 0. Streaming mode also returns 0 for <see cref="Input"/> and <see cref="Output"/>
/// as ME.AI 10.4.1 does not expose usage in streaming updates.</para>
/// </summary>
internal readonly record struct TokenUsage(
    int Input, int Output, int CacheRead = 0, int CacheCreation = 0)
{
    /// <summary>Total context size processed: non-cached input + cache-read tokens.</summary>
    public int TotalEffectiveInput => Input + CacheRead + CacheCreation;
}
