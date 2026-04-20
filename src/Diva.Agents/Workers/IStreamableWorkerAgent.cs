namespace Diva.Agents.Workers;

using Diva.Core.Models;

/// <summary>
/// Extends IWorkerAgent with streaming support.
/// Agents that implement this interface can be streamed directly
/// from the /invoke/stream endpoint, bypassing the default runner path.
/// </summary>
public interface IStreamableWorkerAgent : IWorkerAgent
{
    IAsyncEnumerable<AgentStreamChunk> InvokeStreamAsync(
        AgentRequest request, TenantContext tenant, CancellationToken ct);
}
