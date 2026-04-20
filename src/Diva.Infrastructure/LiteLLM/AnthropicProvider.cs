using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Diva.Core.Configuration;
using Microsoft.Extensions.Options;

namespace Diva.Infrastructure.LiteLLM;

/// <summary>Real implementation — wraps AnthropicClient for production use.</summary>
public sealed class AnthropicProvider : IAnthropicProvider
{
    private readonly AnthropicClient _client;
    private readonly HttpClient _httpClient;

    public AnthropicProvider(IOptions<LlmOptions> opts, HttpClient httpClient)
    {
        _httpClient = httpClient;
        _client     = new AnthropicClient(new APIAuthentication(opts.Value.DirectProvider.ApiKey), httpClient);
    }

    public Task<MessageResponse> GetClaudeMessageAsync(MessageParameters parameters, CancellationToken ct, string? apiKeyOverride = null)
    {
        var client = apiKeyOverride is not null
            ? new AnthropicClient(new APIAuthentication(apiKeyOverride), _httpClient)
            : _client;
        return client.Messages.GetClaudeMessageAsync(parameters, ct);
    }

    public IAsyncEnumerable<MessageResponse> StreamClaudeMessageAsync(MessageParameters parameters, CancellationToken ct, string? apiKeyOverride = null)
    {
        var client = apiKeyOverride is not null
            ? new AnthropicClient(new APIAuthentication(apiKeyOverride), _httpClient)
            : _client;
        return client.Messages.StreamClaudeMessageAsync(parameters, ct);
    }
}
