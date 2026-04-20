using Anthropic.SDK.Messaging;

namespace Diva.Infrastructure.LiteLLM;

/// <summary>
/// Thin wrapper around Anthropic.SDK.AnthropicClient — enables unit testing without a real API key.
/// </summary>
public interface IAnthropicProvider
{
    Task<MessageResponse> GetClaudeMessageAsync(MessageParameters parameters, CancellationToken ct, string? apiKeyOverride = null);

    /// <summary>Stream the Claude response token by token. Each yielded <see cref="MessageResponse"/> contains the latest delta in <c>Delta.Text</c>.</summary>
    IAsyncEnumerable<MessageResponse> StreamClaudeMessageAsync(MessageParameters parameters, CancellationToken ct, string? apiKeyOverride = null);
}
