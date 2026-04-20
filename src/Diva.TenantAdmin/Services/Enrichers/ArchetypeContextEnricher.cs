using Diva.Core.Configuration;
using Diva.Core.Models;
using Microsoft.Extensions.Logging;

namespace Diva.TenantAdmin.Services.Enrichers;

/// <summary>
/// Enriches AgentSetupContext with archetype-specific data from the live archetype registry.
/// This is the baseline enricher — always registered and always runs.
/// </summary>
public sealed class ArchetypeContextEnricher : ISetupAssistantContextEnricher
{
    private readonly IArchetypeRegistry _archetypes;
    private readonly ILogger<ArchetypeContextEnricher> _logger;

    public ArchetypeContextEnricher(IArchetypeRegistry archetypes, ILogger<ArchetypeContextEnricher> logger)
    {
        _archetypes = archetypes;
        _logger = logger;
    }

    public ValueTask EnrichAsync(AgentSetupContext ctx, CancellationToken ct)
    {
        // Validate and normalize the archetype ID from the live registry
        if (!string.IsNullOrWhiteSpace(ctx.ArchetypeId))
        {
            var archetype = _archetypes.GetById(ctx.ArchetypeId);
            if (archetype is null)
            {
                _logger.LogWarning("Unknown archetype '{ArchetypeId}' — falling back to 'general'", ctx.ArchetypeId);
                ctx.ArchetypeId = "general";
            }
        }

        return ValueTask.CompletedTask;
    }
}
