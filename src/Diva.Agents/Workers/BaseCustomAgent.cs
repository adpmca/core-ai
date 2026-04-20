using System.Runtime.CompilerServices;
using System.Text.Json;
using Diva.Core.Models;
using Diva.Infrastructure.Data.Entities;
using Diva.Infrastructure.LiteLLM;

namespace Diva.Agents.Workers;

/// <summary>
/// Abstract base for agents that need pre/post-processing around the standard ReAct loop.
/// Subclass this to add domain-specific logic without reimplementing the full runner.
/// </summary>
public abstract class BaseCustomAgent : IStreamableWorkerAgent
{
    private readonly AgentDefinitionEntity _definition;
    private readonly IAgentRunner _runner;

    protected BaseCustomAgent(AgentDefinitionEntity definition, IAgentRunner runner)
    {
        _definition = definition;
        _runner = runner;
    }

    protected AgentDefinitionEntity Definition => _definition;

    public AgentCapability GetCapability() => new()
    {
        AgentId = _definition.Id,
        AgentType = _definition.AgentType,
        Description = _definition.Description,
        Capabilities = JsonSerializer.Deserialize<string[]>(_definition.Capabilities ?? "[]") ?? [],
        Priority = 5,
    };

    /// <summary>Override to transform the request before the ReAct loop starts.</summary>
    protected virtual Task<AgentRequest> PreProcessAsync(AgentRequest request, TenantContext tenant, CancellationToken ct)
        => Task.FromResult(request);

    /// <summary>Override to transform the response after the ReAct loop completes.</summary>
    protected virtual Task<AgentResponse> PostProcessAsync(AgentResponse response, AgentRequest request, TenantContext tenant, CancellationToken ct)
        => Task.FromResult(response);

    /// <summary>Override to inject additional instructions into the system prompt at runtime.</summary>
    protected virtual Task<string?> GetAdditionalInstructionsAsync(TenantContext tenant, CancellationToken ct)
        => Task.FromResult<string?>(null);

    public async Task<AgentResponse> ExecuteAsync(AgentRequest request, TenantContext tenant, CancellationToken ct)
    {
        var processed = await PreProcessAsync(request, tenant, ct);
        var additional = await GetAdditionalInstructionsAsync(tenant, ct);
        var def = _definition;
        if (!string.IsNullOrEmpty(additional))
        {
            _definition.SystemPrompt = (_definition.SystemPrompt ?? "") + "\n\n" + additional;
        }

        var result = await _runner.RunAsync(def, processed, tenant, ct);
        return await PostProcessAsync(result, processed, tenant, ct);
    }

    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamAsync(
        AgentRequest request, TenantContext tenant, [EnumeratorCancellation] CancellationToken ct)
    {
        var processed = await PreProcessAsync(request, tenant, ct);
        var additional = await GetAdditionalInstructionsAsync(tenant, ct);

        if (!string.IsNullOrEmpty(additional))
        {
            _definition.SystemPrompt = (_definition.SystemPrompt ?? "") + "\n\n" + additional;
        }

        await foreach (var chunk in _runner.InvokeStreamAsync(_definition, processed, tenant, ct))
        {
            yield return chunk;
        }
    }
}
