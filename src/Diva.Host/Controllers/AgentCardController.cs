using Diva.Core.Configuration;
using Diva.Core.Models;
using Diva.Infrastructure.A2A;
using Diva.Infrastructure.Auth;
using Diva.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Diva.Host.Controllers;

[ApiController]
[AllowAnonymous]
public class AgentCardController : ControllerBase
{
    private readonly IDatabaseProviderFactory _db;
    private readonly IAgentCardBuilder _cardBuilder;
    private readonly A2AOptions _a2aOptions;

    public AgentCardController(
        IDatabaseProviderFactory db,
        IAgentCardBuilder cardBuilder,
        IOptions<A2AOptions> a2aOptions)
    {
        _db = db;
        _cardBuilder = cardBuilder;
        _a2aOptions = a2aOptions.Value;
    }

    /// <summary>GET /.well-known/agent.json — returns AgentCard for the default or specified agent.</summary>
    [HttpGet("/.well-known/agent.json")]
    public async Task<IActionResult> GetAgentCard([FromQuery] string? agentId, CancellationToken ct)
    {
        if (!_a2aOptions.Enabled)
            return NotFound(new { error = "A2A is not enabled" });

        var tenant = HttpContext.TryGetTenantContext() ?? TenantContext.System(1);
        using var db = _db.CreateDbContext(tenant);

        var agent = agentId is not null
            ? await db.AgentDefinitions.FirstOrDefaultAsync(a => a.Id == agentId && a.IsEnabled, ct)
            : await db.AgentDefinitions.FirstOrDefaultAsync(a => a.IsEnabled && a.Status == "Published", ct);

        if (agent is null)
            return NotFound(new { error = "No published agent found" });

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        return Ok(_cardBuilder.BuildCard(agent, baseUrl));
    }

    /// <summary>GET /.well-known/agents.json — returns all published AgentCards for discovery.</summary>
    [HttpGet("/.well-known/agents.json")]
    public async Task<IActionResult> GetAllAgentCards([FromQuery] int? tenantId, CancellationToken ct)
    {
        if (!_a2aOptions.Enabled)
            return NotFound(new { error = "A2A is not enabled" });

        var tenant = HttpContext.TryGetTenantContext()
            ?? (tenantId.HasValue ? TenantContext.System(tenantId.Value) : TenantContext.System(1));
        using var db = _db.CreateDbContext(tenant);

        var agents = await db.AgentDefinitions
            .Where(a => a.IsEnabled && a.Status == "Published")
            .OrderBy(a => a.DisplayName)
            .ToListAsync(ct);

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        return Ok(agents.Select(a => _cardBuilder.BuildCard(a, baseUrl)));
    }
}
