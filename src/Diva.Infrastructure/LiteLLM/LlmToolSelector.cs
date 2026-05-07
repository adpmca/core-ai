using System.Text.Json;
using System.Text.RegularExpressions;
using Anthropic.SDK.Messaging;
using Diva.Core.Configuration;
using Diva.Core.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;

namespace Diva.Infrastructure.LiteLLM;

/// <summary>
/// LLM-based tool pre-filter. Before the ReAct loop starts, makes one lightweight LLM call
/// (tool names + descriptions only — no schemas) to select the subset of tools relevant to
/// the current query. Only fires when tool count exceeds SemanticToolFilterThreshold.
///
/// Works for both single-agent and multi-agent scenarios: in multi-agent mode each worker
/// receives its sub-task description as the query, so each independently narrows its own tools.
///
/// On any error or empty parse result, returns the original lists unchanged (no exception propagates).
/// Provider + model resolved from llmOverride (agent's resolved config) with global fallback.
/// </summary>
public sealed class LlmToolSelector : IToolSelectionStrategy
{
    private const int MaxSelectionTokens = 512;

    private static readonly Regex JsonFencePattern =
        new(@"^```json?\s*|\s*```$", RegexOptions.Multiline | RegexOptions.Compiled);

    private readonly IAnthropicProvider _anthropic;
    private readonly IOpenAiProvider _openAi;
    private readonly LlmOptions _llm;
    private readonly AgentOptions _agentOpts;
    private readonly ILogger<LlmToolSelector> _logger;

    public LlmToolSelector(
        IAnthropicProvider anthropic,
        IOpenAiProvider openAi,
        IOptions<LlmOptions> llm,
        IOptions<AgentOptions> agentOpts,
        ILogger<LlmToolSelector> logger)
    {
        _anthropic = anthropic;
        _openAi    = openAi;
        _llm       = llm.Value;
        _agentOpts = agentOpts.Value;
        _logger    = logger;
    }

    public async Task<(List<McpClientTool> Tools, Dictionary<string, McpClient> ClientMap)> SelectAsync(
        string query,
        List<McpClientTool> allMcpTools,
        Dictionary<string, McpClient> toolClientMap,
        SupervisorLlmOverride llmOverride,
        CancellationToken ct)
    {
        var threshold = _agentOpts.SemanticToolFilterThreshold;

        if (threshold <= 0 || allMcpTools.Count <= threshold)
        {
            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation(
                    "LlmToolSelector: {Count} tools — skipped (count {Count} <= threshold {Threshold})",
                    allMcpTools.Count, allMcpTools.Count, threshold);
            return (allMcpTools, toolClientMap);
        }

        try
        {
            _logger.LogInformation(
                "LlmToolSelector: {Count} tools → calling {Provider} model={Model} for selection",
                allMcpTools.Count, llmOverride.Provider, llmOverride.Model);

            var sw     = System.Diagnostics.Stopwatch.StartNew();
            var prompt = BuildPrompt(query, allMcpTools);
            var raw    = await CallLlmAsync(prompt, llmOverride, ct);

            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("LlmToolSelector: raw response => {Raw}", raw);

            var selected = ParseResponse(raw, allMcpTools);

            if (selected.Count == 0)
            {
                _logger.LogWarning("LlmToolSelector: LLM returned 0 valid tools — returning full tool list (fallback)");
                return (allMcpTools, toolClientMap);
            }

            // Apply MaxTools cap
            var maxTools = _agentOpts.SemanticToolFilterMaxTools;
            if (maxTools > 0 && selected.Count > maxTools)
                selected = selected.Take(maxTools).ToList();

            var filteredTools     = allMcpTools.Where(t => selected.Contains(t.Name)).ToList();
            var filteredClientMap = toolClientMap
                .Where(kv => selected.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            sw.Stop();
            _logger.LogInformation(
                "LlmToolSelector: selected {Selected}/{Total} tools in {Ms}ms",
                filteredTools.Count, allMcpTools.Count, sw.ElapsedMilliseconds);

            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("LlmToolSelector: selected=[{Names}]",
                    string.Join(", ", filteredTools.Select(t => t.Name)));

            return (filteredTools, filteredClientMap);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LlmToolSelector: LLM call failed — returning full tool list (fallback)");
            return (allMcpTools, toolClientMap);
        }
    }

    private async Task<string> CallLlmAsync(string prompt, SupervisorLlmOverride llmOverride, CancellationToken ct)
    {
        var provider = llmOverride.Provider;
        var model    = llmOverride.Model;
        var endpoint = llmOverride.Endpoint;

        if (provider.Equals("Anthropic", StringComparison.OrdinalIgnoreCase))
        {
            var parameters = new MessageParameters
            {
                Model     = model,
                MaxTokens = MaxSelectionTokens,
                System    = [new SystemMessage("You are a JSON-only tool selection assistant. Respond ONLY with a valid JSON array of tool names, no markdown.")],
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
            return msg.Content.OfType<Anthropic.SDK.Messaging.TextContent>().FirstOrDefault()?.Text ?? "[]";
        }

        // OpenAI-compatible (Ollama, LiteLLM, LM Studio, Azure, etc.)
        var chatClient = _openAi.CreateChatClient(model, endpointOverride: endpoint);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are a JSON-only tool selection assistant. Respond ONLY with a valid JSON array of tool names, no markdown."),
            new(ChatRole.User, prompt)
        };
        var result = await chatClient.GetResponseAsync(messages,
            new ChatOptions { MaxOutputTokens = MaxSelectionTokens }, ct);
        return result.Text ?? "[]";
    }

    private List<string> ParseResponse(string raw, List<McpClientTool> available)
    {
        var json  = JsonFencePattern.Replace(raw.Trim(), "").Trim();
        var start = json.IndexOf('[');
        var end   = json.LastIndexOf(']');
        if (start < 0 || end <= start) return [];

        json = json[start..(end + 1)];

        using var doc    = JsonDocument.Parse(json);
        var validNames   = available.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected     = new List<string>();

        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var name = el.GetString();
            if (!string.IsNullOrWhiteSpace(name) && validNames.Contains(name))
                selected.Add(name);
        }

        return selected;
    }

    private static string BuildPrompt(string query, List<McpClientTool> tools)
    {
        var lines = tools.Select(t => $"- {t.Name}: {t.Description}");
        return
            "Given this user request, select which tools are needed to answer it.\n\n" +
            $"USER REQUEST: {query}\n\n" +
            "AVAILABLE TOOLS:\n" +
            string.Join("\n", lines) + "\n\n" +
            "Rules:\n" +
            "1. Include a tool if it is directly or indirectly needed to complete the request.\n" +
            "2. When in doubt, include it — omitting a needed tool breaks the agent.\n" +
            "3. Respond ONLY with a JSON array of tool names: [\"tool1\", \"tool2\"]";
    }
}
