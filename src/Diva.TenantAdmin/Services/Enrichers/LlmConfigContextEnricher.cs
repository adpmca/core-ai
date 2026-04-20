using Diva.Core.Configuration;
using Diva.Core.Models;
using Diva.Infrastructure.Data;
using Diva.Infrastructure.LiteLLM;
using Microsoft.Extensions.Logging;

namespace Diva.TenantAdmin.Services.Enrichers;

/// <summary>
/// Populates AgentSetupContext.AvailableLlmConfigs from the tenant's named LLM configurations.
/// These config IDs are referenced in model_switch rule suggestions.
/// </summary>
public sealed class LlmConfigContextEnricher : ISetupAssistantContextEnricher
{
    private readonly IDatabaseProviderFactory _db;
    private readonly ILogger<LlmConfigContextEnricher> _logger;

    public LlmConfigContextEnricher(IDatabaseProviderFactory db, ILogger<LlmConfigContextEnricher> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async ValueTask EnrichAsync(AgentSetupContext ctx, CancellationToken ct)
    {
        try
        {
            using var db = _db.CreateDbContext(TenantContext.System(ctx.TenantId));
            var tenantConfigs = db.TenantLlmConfigs
                .Where(c => c.Name != null) // named configs only (not the unnamed default)
                .OrderBy(c => c.Id)
                .ToList();

            var platformConfigs = db.PlatformLlmConfigs
                .OrderBy(c => c.Id)
                .ToList();

            // Prefer tenant-specific named configs; include platform configs as fallback
            var result = new List<SetupLlmConfigDto>();

            foreach (var c in tenantConfigs)
            {
                result.Add(new SetupLlmConfigDto(
                    c.Id,
                    c.Provider ?? "Anthropic",
                    c.Model ?? "unknown",
                    c.Name!));
            }

            foreach (var c in platformConfigs)
            {
                result.Add(new SetupLlmConfigDto(c.Id, c.Provider, c.Model, c.Name));
            }

            ctx.AvailableLlmConfigs = result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LlmConfigContextEnricher failed — AvailableLlmConfigs will be empty");
            ctx.AvailableLlmConfigs = [];
        }
    }
}
