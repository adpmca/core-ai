namespace Diva.Agents.Hooks.BuiltIn;

using Diva.Core.Models;

/// <summary>
/// Ensures the RAG agent's response includes source citations.
/// If no citations are detected, appends a warning note.
/// </summary>
public sealed class CitationEnforcerHook : IOnBeforeResponseHook
{
    public int Order => 50;

    public Task<string> OnBeforeResponseAsync(
        AgentHookContext context, string responseText, CancellationToken ct)
    {
        var hasCitations = responseText.Contains("[Source:", StringComparison.OrdinalIgnoreCase)
            || responseText.Contains("(ref:", StringComparison.OrdinalIgnoreCase)
            || responseText.Contains("[1]")
            || responseText.Contains("Source:");

        if (!hasCitations && !string.IsNullOrWhiteSpace(context.ToolEvidence))
        {
            responseText += "\n\n> **Note:** This response was generated from retrieved documents but specific source citations could not be automatically verified.";
        }

        return Task.FromResult(responseText);
    }
}
