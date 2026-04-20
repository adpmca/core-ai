using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Diva.Infrastructure.LiteLLM;

/// <summary>
/// A synthetic <see cref="AIFunction"/> that represents a peer agent available for delegation.
/// Injected alongside MCP tools so the LLM can choose to call another agent as a tool.
/// The actual execution is handled by <see cref="AgentToolExecutor"/>, not the MCP pipeline.
/// </summary>
public sealed class AgentDelegationTool : AIFunction
{
    private static readonly JsonElement s_schema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "query": {
              "type": "string",
              "description": "The question or task to delegate to this agent."
            },
            "context": {
              "type": "string",
              "description": "Optional additional context from the current conversation to pass along."
            }
          },
          "required": ["query"]
        }
        """).RootElement.Clone();

    public AgentDelegationTool(string agentId, string agentName, string? description, string[]? capabilities)
    {
        AgentId = agentId;
        Name = $"call_agent_{SanitizeName(agentName)}_{SanitizeName(agentId)}";
        Description = BuildDescription(agentName, description, capabilities);
        JsonSchema = s_schema;
    }

    public string AgentId { get; }

    public override string Name { get; }
    public override string Description { get; }
    public override JsonElement JsonSchema { get; }

    protected override ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        // Never called directly — execution is intercepted by AgentToolExecutor.
        throw new InvalidOperationException(
            $"AgentDelegationTool '{Name}' should not be invoked directly. " +
            "Use AgentToolExecutor for agent delegation calls.");
    }

    /// <summary>Prefix used to identify agent delegation tool calls in the ReAct loop.</summary>
    internal const string ToolNamePrefix = "call_agent_";

    internal static bool IsAgentDelegationTool(string toolName) =>
        toolName.StartsWith(ToolNamePrefix, StringComparison.Ordinal);

    private static string SanitizeName(string name) =>
        new(name.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());

    private static string BuildDescription(string agentName, string? description, string[]? capabilities)
    {
        var parts = new List<string> { $"Delegate a task to the '{agentName}' agent." };
        if (!string.IsNullOrWhiteSpace(description))
            parts.Add(description.Length > 200 ? description[..200] + "…" : description);
        if (capabilities is { Length: > 0 })
            parts.Add($"Capabilities: {string.Join(", ", capabilities)}.");
        return string.Join(" ", parts);
    }
}
