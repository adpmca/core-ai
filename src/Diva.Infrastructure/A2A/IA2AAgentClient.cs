namespace Diva.Infrastructure.A2A;

using Diva.Core.Models;

public interface IA2AAgentClient
{
    /// <summary>Discover a remote agent's capabilities.</summary>
    Task<object> DiscoverAsync(string agentUrl, CancellationToken ct);

    /// <summary>Send a task to a remote agent and stream results back as AgentStreamChunks.</summary>
    IAsyncEnumerable<AgentStreamChunk> SendTaskAsync(
        string agentUrl, string? authToken, AgentRequest request, CancellationToken ct,
        string authScheme = "Bearer", string? customHeaderName = null,
        string? delegationToolName = null, string? agentId = null, string? agentName = null,
        string? remoteAgentId = null);
}
