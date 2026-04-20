using Diva.Core.Models;
using Diva.Infrastructure.Data.Entities;

namespace Diva.Infrastructure.LiteLLM;

/// <summary>
/// Abstraction over the agent execution engine.
/// Enables unit testing of BaseCustomAgent and controllers without the full LLM chain.
/// </summary>
public interface IAgentRunner
{
    Task<AgentResponse> RunAsync(
        AgentDefinitionEntity definition, AgentRequest request, TenantContext tenant, CancellationToken ct);

    IAsyncEnumerable<AgentStreamChunk> InvokeStreamAsync(
        AgentDefinitionEntity definition, AgentRequest request, TenantContext tenant, CancellationToken ct);
}
