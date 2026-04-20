namespace Diva.Infrastructure.LiteLLM;

/// <summary>
/// Provider-agnostic representation of a single conversation turn.
/// Used to transfer message history when switching providers mid-execution
/// via <see cref="ILlmProviderStrategy.ExportHistory"/> and
/// <see cref="ILlmProviderStrategy.ImportHistory"/>.
/// </summary>
internal sealed class UnifiedHistoryEntry
{
    public string Role { get; init; } = "";   // "user" | "assistant"
    public List<UnifiedHistoryPart> Parts { get; init; } = [];
}

internal abstract class UnifiedHistoryPart;

internal sealed class TextHistoryPart(string text) : UnifiedHistoryPart
{
    public string Text { get; } = text;
}

internal sealed class ToolCallHistoryPart(string id, string name, string inputJson) : UnifiedHistoryPart
{
    public string Id        { get; } = id;
    public string Name      { get; } = name;
    public string InputJson { get; } = inputJson;
}

/// <summary>
/// Tool result part. ToolName is intentionally omitted: Anthropic SDK ToolResultContent
/// only stores ToolUseId, not the tool name. OpenAI FunctionResultContent also only
/// requires callId + result. Reconstruct tool name from context if needed.
/// </summary>
internal sealed class ToolResultHistoryPart(string toolCallId, string output, bool isError) : UnifiedHistoryPart
{
    public string ToolCallId { get; } = toolCallId;
    public string Output     { get; } = output;
    public bool   IsError    { get; } = isError;
}
