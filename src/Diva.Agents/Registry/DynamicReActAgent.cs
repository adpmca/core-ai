using System.Text.Json;
using Diva.Agents.Workers;
using Diva.Core.Models;
using Diva.Infrastructure.Data.Entities;
using Diva.Infrastructure.LiteLLM;

namespace Diva.Agents.Registry;

/// <summary>
/// A worker agent created at runtime from an AgentDefinitionEntity stored in the database.
/// Delegates execution to IAgentRunner (which handles both Anthropic and OpenAI-compatible paths).
/// </summary>
public sealed class DynamicReActAgent : IWorkerAgent, IStreamableWorkerAgent
{
    private readonly AgentDefinitionEntity _definition;
    private readonly IAgentRunner _runner;

    public DynamicReActAgent(AgentDefinitionEntity definition, IAgentRunner runner)
    {
        _definition = definition;
        _runner     = runner;
    }

    public AgentCapability GetCapability()
    {
        var caps = string.IsNullOrEmpty(_definition.Capabilities)
            ? Array.Empty<string>()
            : JsonSerializer.Deserialize<string[]>(_definition.Capabilities) ?? [];

        return new AgentCapability
        {
            AgentId      = _definition.Id,
            AgentType    = _definition.AgentType,
            Description  = _definition.Description,
            Capabilities = caps,
            // Dynamic agents have lower priority than statically registered agents
            Priority     = 5
        };
    }

    public Task<AgentResponse> ExecuteAsync(
        AgentRequest request,
        TenantContext tenant,
        CancellationToken ct)
    {
        return _runner.RunAsync(_definition, request, tenant, ct);
    }

    public IAsyncEnumerable<AgentStreamChunk> InvokeStreamAsync(
        AgentRequest request,
        TenantContext tenant,
        CancellationToken ct)
    {
        return _runner.InvokeStreamAsync(_definition, request, tenant, ct);
    }
}
