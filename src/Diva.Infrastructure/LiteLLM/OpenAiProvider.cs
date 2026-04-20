using System.ClientModel;
using Diva.Core.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;

namespace Diva.Infrastructure.LiteLLM;

/// <summary>Real implementation — wraps OpenAIClient for production use.</summary>
public sealed class OpenAiProvider : IOpenAiProvider
{
    private readonly LlmOptions _opts;

    public OpenAiProvider(IOptions<LlmOptions> opts) => _opts = opts.Value;

    public IChatClient CreateChatClient(string model, string? apiKeyOverride = null, string? endpointOverride = null)
    {
        var dp = _opts.DirectProvider;
        var key      = apiKeyOverride ?? (string.IsNullOrEmpty(dp.ApiKey) ? "no-key" : dp.ApiKey);
        var endpoint = endpointOverride ?? dp.Endpoint;

        var credential    = new ApiKeyCredential(key);
        var clientOptions = new OpenAIClientOptions();
        if (!string.IsNullOrEmpty(endpoint))
            clientOptions.Endpoint = new Uri(endpoint);

        return new OpenAIClient(credential, clientOptions)
            .GetChatClient(model)
            .AsIChatClient();
    }
}
