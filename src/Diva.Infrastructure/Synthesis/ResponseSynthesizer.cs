using System.Text;
using Anthropic.SDK.Messaging;
using Diva.Core.Configuration;
using Diva.Core.Models;
using Diva.Infrastructure.LiteLLM;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Diva.Infrastructure.Synthesis;

/// <summary>
/// Synthesizes multiple agent results into one coherent response via a single LLM call.
/// Falls back to naive concatenation on any LLM error so the pipeline never stalls.
/// </summary>
public sealed class ResponseSynthesizer : IResponseSynthesizer
{
    private const int MaxSynthesisTokens = 2048;

    private readonly IAnthropicProvider _anthropic;
    private readonly IOpenAiProvider _openAi;
    private readonly LlmOptions _llm;
    private readonly ILogger<ResponseSynthesizer> _logger;

    public ResponseSynthesizer(
        IAnthropicProvider anthropic,
        IOpenAiProvider openAi,
        IOptions<LlmOptions> llm,
        ILogger<ResponseSynthesizer> logger)
    {
        _anthropic = anthropic;
        _openAi    = openAi;
        _llm       = llm.Value;
        _logger    = logger;
    }

    public async Task<string> SynthesizeAsync(
        string originalQuery,
        IReadOnlyList<AgentResponse> results,
        SupervisorLlmOverride? llmOverride,
        CancellationToken ct)
    {
        if (results.Count == 0) return string.Empty;
        if (results.Count == 1) return results[0].Content;

        try
        {
            var prompt = BuildPrompt(originalQuery, results);
            return await CallLlmAsync(prompt, llmOverride, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ResponseSynthesizer LLM call failed — falling back to concatenation");
            return Concatenate(results);
        }
    }

    private async Task<string> CallLlmAsync(string prompt, SupervisorLlmOverride? llmOverride, CancellationToken ct)
    {
        var provider = llmOverride?.Provider ?? _llm.DirectProvider.Provider;
        var model    = llmOverride?.Model    ?? _llm.DirectProvider.Model;
        var endpoint = llmOverride?.Endpoint ?? _llm.DirectProvider.Endpoint;

        if (provider.Equals("Anthropic", StringComparison.OrdinalIgnoreCase))
        {
            var parameters = new MessageParameters
            {
                Model     = model,
                MaxTokens = MaxSynthesisTokens,
                System    = [new SystemMessage("You are a helpful assistant that synthesizes multiple agent outputs into one clear response.")],
                Messages  =
                [
                    new Message
                    {
                        Role    = RoleType.User,
                        Content = [new Anthropic.SDK.Messaging.TextContent { Text = prompt }]
                    }
                ]
            };
            var msg = await _anthropic.GetClaudeMessageAsync(parameters, ct);
            return msg.Content.OfType<Anthropic.SDK.Messaging.TextContent>().FirstOrDefault()?.Text ?? Concatenate(null);
        }

        var chatClient = _openAi.CreateChatClient(model, endpointOverride: endpoint);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are a helpful assistant that synthesizes multiple agent outputs into one clear response."),
            new(ChatRole.User, prompt)
        };
        var result = await chatClient.GetResponseAsync(messages,
            new ChatOptions { MaxOutputTokens = MaxSynthesisTokens }, ct);
        return result.Text ?? Concatenate(null);
    }

    private static string BuildPrompt(string originalQuery, IReadOnlyList<AgentResponse> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Synthesize the following agent results into a single, coherent response that directly");
        sb.AppendLine("addresses the original user request. Write as one unified assistant — do not reference");
        sb.AppendLine("agent names, routing, or internal structure.");
        sb.AppendLine();
        sb.AppendLine($"ORIGINAL REQUEST:\n{originalQuery}");
        sb.AppendLine();
        sb.AppendLine("AGENT RESULTS:");
        foreach (var r in results)
            sb.AppendLine($"[{r.AgentName ?? "Agent"}]: {r.Content}");
        return sb.ToString();
    }

    private static string Concatenate(IReadOnlyList<AgentResponse>? results)
    {
        if (results is null or { Count: 0 }) return string.Empty;
        return string.Join("\n\n---\n\n", results.Select(r => $"**{r.AgentName ?? "Agent"}**\n{r.Content}"));
    }
}
