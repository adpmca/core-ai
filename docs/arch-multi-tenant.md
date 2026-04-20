# Architecture: Multi-Tenant Hierarchy & TenantContext Model

> **Status:** Reference — no code to write here
> **Related phases:** [phase-02-core-models.md](phase-02-core-models.md), [phase-03-oauth-tenant.md](phase-03-oauth-tenant.md), [phase-04-database.md](phase-04-database.md)

---

## Tenant / Site Hierarchy

```
TENANT (Organization)                  TenantId = 1  "Acme Corporation"
│
└── SITES (Properties / Locations)
    ├── SiteId = 1  "North Campus"
    ├── SiteId = 2  "South Campus"
    ├── SiteId = 3  "East Campus"
    ├── SiteId = 4  "West Campus"
    └── SiteId = 5  "Central Campus"

USERS are scoped within a Tenant to one or more Sites:
├── Org Admin    → site_ids: [1,2,3,4,5]  (all sites)
├── GM (Site 1)  → site_ids: [1]
└── GM (Site 3)  → site_ids: [3]
```

**Rule:** A user can only query data for sites listed in their `SiteIds[]` claim. The agent validates this on every request.

---

## Canonical TenantContext Model

This is the single authoritative model (merged from both versions in IMPLEMENTATION_PLAN.md):

```csharp
// src/Diva.Core/Models/TenantContext.cs
public sealed class TenantContext
{
    // ── Core Identity (from OAuth JWT claims) ──────────────────────────────
    public int TenantId { get; init; }
    public string TenantName { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;       // "org_admin" | "gm" | "staff"
    public string[] UserRoles { get; init; } = [];
    public int[] SiteIds { get; init; } = [];               // Sites user can access
    public int CurrentSiteId { get; init; }                 // Active site for this request
    public string[] AgentAccess { get; init; } = [];        // ["Analytics", "Reservation"]

    // ── OAuth Token (for propagation to MCP tools) ─────────────────────────
    public string AccessToken { get; init; } = string.Empty;
    public DateTime TokenExpiry { get; init; }
    public string? TeamApiKey { get; init; }                 // LiteLLM team key (optional)

    // ── Request Context ────────────────────────────────────────────────────
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString();
    public string? SessionId { get; init; }
    public Dictionary<string, string> CustomHeaders { get; init; } = new();

    // ── Convenience ────────────────────────────────────────────────────────
    public bool CanAccessSite(int siteId) =>
        Role == "org_admin" || SiteIds.Contains(siteId);

    public bool CanUseAgent(string agentType) =>
        AgentAccess.Contains(agentType) || AgentAccess.Contains("*");
}
```

---

## JWT Claims Structure

The main application must include these claims in the OAuth token:

```json
{
  "sub": "user-123",
  "tenant_id": "1",
  "tenant_name": "Acme Corporation",
  "site_ids": "1,2,3",
  "role": "gm",
  "agent_access": "Analytics,Reservation",
  "exp": 1700000000
}
```

`ClaimMappings` in `appsettings.json` maps claim names → TenantContext fields (configurable per deployment).

---

## Data Models (DB Entities)

```csharp
public class TenantEntity
{
    public int Id { get; set; }
    public string Name { get; set; }          // "Acme Corporation"
    public string? DisplayName { get; set; }
    public string? LiteLLMTeamId { get; set; } // "team_acme"
    public decimal MonthlyBudget { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Settings { get; set; }       // JSON
    public List<SiteEntity> Sites { get; set; } = [];
}

public class SiteEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string Name { get; set; }            // "East Campus"
    public string? TimeZone { get; set; }
    public TenantEntity Tenant { get; set; }
}
```

---

## Site Scoping in Agent Requests

Every agent request carries `TenantContext`. The supervisor validates site access before dispatching:

```csharp
// SupervisorAgent — before any work starts
if (!tenantContext.CanAccessSite(requestedSiteId))
    return Fail($"Access denied: user {tenantContext.UserId} cannot access site {requestedSiteId}");

// All sub-tasks inherit the validated site context
foreach (var task in subTasks)
{
    task.SiteId   = requestedSiteId;
    task.TenantId = tenantContext.TenantId;
}
```

---

## Database Isolation Strategies

| Strategy | When used | How |
|----------|-----------|-----|
| Application-level filter | SQLite (default), SQL Server basic | EF Core `HasQueryFilter(e => e.TenantId == _tenant.TenantId)` |
| Row-Level Security | SQL Server + `UseRls: true` | `sp_set_session_context` + DB security policy |
| Database-per-tenant | SQL Server + `UseConnectionPerTenant: true` | Connection string swapped per request |

See [phase-04-database.md](phase-04-database.md) for full implementation.

---

## Multi-Tenant Business Rules

Each tenant has separate business rules, prompts, and terminology stored in DB:

- **Tenant A:** free cancellation 24h, revenue = SALES+SERVICES+RETAIL, terminology: "client"/"engagement"
- **Tenant B:** free cancellation 48h, revenue = PRODUCTS+SERVICES+MAINTENANCE, ARPU as primary KPI

Site-level overrides can further specialize rules within a tenant (optional, see [phase-06-tenant-admin.md](phase-06-tenant-admin.md)).
