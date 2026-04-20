namespace Diva.Infrastructure.A2A;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Diva.Core.Configuration;
using Diva.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Client for delegating tasks to remote agents via the A2A protocol.
/// Propagates X-A2A-Depth header to prevent infinite delegation loops.
/// </summary>
public sealed class A2AAgentClient : IA2AAgentClient
{
    private readonly HttpClient _http;
    private readonly A2AOptions _options;
    private readonly ILogger<A2AAgentClient> _logger;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public A2AAgentClient(HttpClient http, IOptions<A2AOptions> options, ILogger<A2AAgentClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<object> DiscoverAsync(string agentUrl, CancellationToken ct)
    {
        var cardUrl = agentUrl.TrimEnd('/') + "/.well-known/agent.json";
        var response = await _http.GetFromJsonAsync<JsonElement>(cardUrl, ct);
        return response;
    }

    public async IAsyncEnumerable<AgentStreamChunk> SendTaskAsync(
        string agentUrl, string? authToken, AgentRequest request,
        [EnumeratorCancellation] CancellationToken ct,
        string authScheme = "Bearer", string? customHeaderName = null,
        string? delegationToolName = null, string? agentId = null, string? agentName = null,
        string? remoteAgentId = null)
    {
        var taskUrl = agentUrl.TrimEnd('/') + "/tasks/send";
        if (!string.IsNullOrEmpty(remoteAgentId))
            taskUrl += "?agentId=" + Uri.EscapeDataString(remoteAgentId);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, taskUrl);
        httpRequest.Content = JsonContent.Create(new { query = request.Query, sessionId = request.SessionId }, options: _jsonOpts);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        if (!string.IsNullOrEmpty(authToken))
        {
            if (authScheme == "Custom" && !string.IsNullOrEmpty(customHeaderName))
                httpRequest.Headers.TryAddWithoutValidation(customHeaderName, authToken);
            else if (authScheme == "ApiKey")
                httpRequest.Headers.TryAddWithoutValidation("X-API-Key", authToken);
            else
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
        }

        // Propagate delegation depth
        var currentDepth = 0;
        if (request.Metadata.TryGetValue("a2a_depth", out var depthObj) && depthObj is int d)
            currentDepth = d;
        httpRequest.Headers.Add("X-A2A-Depth", (currentDepth + 1).ToString());

        using var response = await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        // Read A2A task ID from response header (set by AgentTaskController before streaming)
        // and emit a2a_delegation_start so the trace writer can correlate tool calls to A2A tasks.
        var a2aTaskId = response.Headers.TryGetValues("X-A2A-Task-Id", out var headerVals)
            ? headerVals.FirstOrDefault() : null;
        if (!string.IsNullOrEmpty(a2aTaskId) && !string.IsNullOrEmpty(delegationToolName))
        {
            yield return new AgentStreamChunk
            {
                Type               = "a2a_delegation_start",
                ToolName           = delegationToolName,
                A2ATaskId          = a2aTaskId,
                DelegatedAgentId   = agentId,
                DelegatedAgentName = agentName,
            };
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            if (!line.StartsWith("data: ")) continue;

            var json = line["data: ".Length..];
            if (string.IsNullOrWhiteSpace(json)) continue;

            AgentStreamChunk? chunk = null;
            try { chunk = JsonSerializer.Deserialize<AgentStreamChunk>(json, _jsonOpts); }
            catch (JsonException ex) { _logger.LogWarning(ex, "Failed to parse A2A SSE chunk"); }

            if (chunk is not null)
                yield return chunk;

            if (chunk?.Type == "done") yield break;
        }
    }
}
