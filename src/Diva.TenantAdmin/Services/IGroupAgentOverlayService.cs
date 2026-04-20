using Diva.Infrastructure.Data.Entities;

namespace Diva.TenantAdmin.Services;

/// <summary>
/// Manages tenant-specific overlays for <see cref="GroupAgentTemplateEntity"/> instances.
/// An overlay stores only the delta fields the tenant wishes to change; null means "use template value".
/// Singleton-safe: all methods create a fresh DbContext per call via IDatabaseProviderFactory.
/// </summary>
public interface IGroupAgentOverlayService
{
    /// <summary>Returns all overlays for a tenant (cached, 5-min TTL).</summary>
    Task<List<TenantGroupAgentOverlayEntity>> GetOverlaysAsync(int tenantId, CancellationToken ct);

    /// <summary>Returns the overlay for a specific group template, or null if not applied.</summary>
    Task<TenantGroupAgentOverlayEntity?> GetOverlayAsync(int tenantId, string groupTemplateId, CancellationToken ct);

    /// <summary>
    /// Creates or updates the overlay for a group template (upsert).
    /// Verifies the tenant is a member of the template's group; throws InvalidOperationException if not.
    /// </summary>
    Task<TenantGroupAgentOverlayEntity> ApplyTemplateAsync(int tenantId, string groupTemplateId, ApplyGroupAgentOverlayDto dto, CancellationToken ct);

    /// <summary>Updates an existing overlay identified by its public Guid.</summary>
    Task<TenantGroupAgentOverlayEntity> UpdateOverlayAsync(int tenantId, string overlayGuid, UpdateGroupAgentOverlayDto dto, CancellationToken ct);

    /// <summary>Removes an overlay identified by its public Guid. The template remains visible but no longer active for the tenant.</summary>
    Task RemoveOverlayAsync(int tenantId, string overlayGuid, CancellationToken ct);

    /// <summary>Evicts the overlay cache for a specific tenant. Called after overlay mutations and template updates.</summary>
    void InvalidateCache(int tenantId);
}

// ── DTOs ──────────────────────────────────────────────────────────────────────

public record ApplyGroupAgentOverlayDto(
    bool IsEnabled = true,
    string? SystemPromptAddendum = null,
    string? ModelId = null,
    double? Temperature = null,
    string? ExtraToolBindingsJson = null,
    string? CustomVariablesJson = null,
    int? LlmConfigId = null,
    int? MaxOutputTokens = null);

public record UpdateGroupAgentOverlayDto(
    bool IsEnabled,
    string? SystemPromptAddendum,
    string? ModelId,
    double? Temperature,
    string? ExtraToolBindingsJson,
    string? CustomVariablesJson,
    int? LlmConfigId,
    int? MaxOutputTokens);
