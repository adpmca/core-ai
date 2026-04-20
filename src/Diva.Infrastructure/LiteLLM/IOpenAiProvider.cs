using Microsoft.Extensions.AI;

namespace Diva.Infrastructure.LiteLLM;

/// <summary>
/// Thin wrapper around OpenAIClient — enables unit testing without a real endpoint.
/// Returns a bare IChatClient; callers may wrap with .AsBuilder().UseFunctionInvocation().Build() as needed.
/// </summary>
public interface IOpenAiProvider
{
    IChatClient CreateChatClient(string model, string? apiKeyOverride = null, string? endpointOverride = null);
}
