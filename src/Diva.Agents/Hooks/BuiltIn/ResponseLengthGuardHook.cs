namespace Diva.Agents.Hooks.BuiltIn;

using Diva.Core.Models;

/// <summary>
/// Truncates the agent's response if it exceeds a configurable character limit.
/// Configure via agent custom variable "max_response_length" (default: 10000 chars).
/// Useful for APIs with payload size limits or cost-conscious deployments.
/// </summary>
public sealed class ResponseLengthGuardHook : IOnBeforeResponseHook
{
    private const int DefaultMaxLength = 10_000;

    public int Order => 5; // Run very early — before any content-appending hooks

    public Task<string> OnBeforeResponseAsync(
        AgentHookContext context, string responseText, CancellationToken ct)
    {
        var maxStr = context.Variables.GetValueOrDefault("max_response_length");
        var max = int.TryParse(maxStr, out var parsed) ? parsed : DefaultMaxLength;

        if (responseText.Length <= max)
            return Task.FromResult(responseText);

        // Truncate at last word boundary before limit
        var truncated = responseText[..max];
        var lastSpace = truncated.LastIndexOf(' ');
        if (lastSpace > max * 0.8) // Only break at word if we don't lose too much
            truncated = truncated[..lastSpace];

        return Task.FromResult(
            truncated + $"\n\n> **Response truncated**: Original was {responseText.Length:N0} characters (limit: {max:N0}).");
    }
}
