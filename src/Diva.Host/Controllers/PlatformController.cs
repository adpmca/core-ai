using Diva.Core.Models;
using Diva.Infrastructure.Auth;
using Diva.TenantAdmin.Services;
using Microsoft.AspNetCore.Mvc;

namespace Diva.Host.Controllers;

// ── Request DTOs ──────────────────────────────────────────────────────────────

public record CreateTenantRequest(string Name, string? LiteLLMTeamId = null, string? LiteLLMTeamKey = null);
public record UpdateTenantRequest(string Name, bool IsActive, string? LiteLLMTeamId = null, string? LiteLLMTeamKey = null);

/// <summary>
/// Platform-level administration endpoints — accessible only to master admin (TenantId=0, role=master_admin).
/// Manages tenants, which are the root entities of the multi-tenant hierarchy.
/// </summary>
[ApiController]
[Route("api/platform")]
public class PlatformController : ControllerBase
{
    private readonly ITenantManagementService _tenants;

    public PlatformController(ITenantManagementService tenants) => _tenants = tenants;

    /// <summary>Returns 403 if the current user is not a master admin.</summary>
    private IActionResult? RequireMasterAdmin()
    {
        var ctx = HttpContext.TryGetTenantContext();
        return ctx?.IsMasterAdmin == true ? null : StatusCode(403, "Master admin access required.");
    }

    // ── GET /api/platform/tenants ─────────────────────────────────────────────

    [HttpGet("tenants")]
    public async Task<IActionResult> ListTenants(CancellationToken ct)
    {
        if (RequireMasterAdmin() is { } err) return err;

        var tenants = await _tenants.GetAllAsync(ct);
        return Ok(tenants.Select(t => new
        {
            t.Id,
            t.Name,
            t.IsActive,
            t.CreatedAt,
            t.LiteLLMTeamId,
            siteCount = t.Sites.Count,
        }));
    }

    // ── GET /api/platform/tenants/{id} ────────────────────────────────────────

    [HttpGet("tenants/{id:int}")]
    public async Task<IActionResult> GetTenant(int id, CancellationToken ct)
    {
        if (RequireMasterAdmin() is { } err) return err;

        var tenant = await _tenants.GetByIdAsync(id, ct);
        if (tenant is null) return NotFound();

        return Ok(new
        {
            tenant.Id,
            tenant.Name,
            tenant.IsActive,
            tenant.CreatedAt,
            tenant.LiteLLMTeamId,
            sites = tenant.Sites.Select(s => new { s.Id, s.Name, s.TimeZone, s.IsActive }),
        });
    }

    // ── POST /api/platform/tenants ────────────────────────────────────────────

    [HttpPost("tenants")]
    public async Task<IActionResult> CreateTenant([FromBody] CreateTenantRequest req, CancellationToken ct)
    {
        if (RequireMasterAdmin() is { } err) return err;

        var dto    = new CreateTenantDto(req.Name, req.LiteLLMTeamId, req.LiteLLMTeamKey);
        var tenant = await _tenants.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetTenant), new { id = tenant.Id },
            new { tenant.Id, tenant.Name, tenant.IsActive, tenant.CreatedAt });
    }

    // ── PUT /api/platform/tenants/{id} ────────────────────────────────────────

    [HttpPut("tenants/{id:int}")]
    public async Task<IActionResult> UpdateTenant(int id, [FromBody] UpdateTenantRequest req, CancellationToken ct)
    {
        if (RequireMasterAdmin() is { } err) return err;

        try
        {
            var dto    = new UpdateTenantDto(req.Name, req.LiteLLMTeamId, req.LiteLLMTeamKey, req.IsActive);
            var tenant = await _tenants.UpdateAsync(id, dto, ct);
            return Ok(new { tenant.Id, tenant.Name, tenant.IsActive });
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    // ── DELETE /api/platform/tenants/{id} ─────────────────────────────────────

    [HttpDelete("tenants/{id:int}")]
    public async Task<IActionResult> DeleteTenant(int id, CancellationToken ct)
    {
        if (RequireMasterAdmin() is { } err) return err;

        await _tenants.DeleteAsync(id, ct);
        return NoContent();
    }
}
