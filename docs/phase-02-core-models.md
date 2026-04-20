# Phase 2: Core Models & Configuration

> **Status:** `[ ]` Not Started
> **Depends on:** [phase-01-setup.md](phase-01-setup.md)
> **Blocks:** all other phases (everything uses these models)
> **Project:** `Diva.Core`

---

## Goal

Define all shared domain models, DTOs, and configuration classes. No logic — pure data structures and interfaces. Everything else depends on these.

---

## Files to Create

```
src/Diva.Core/
├── Models/
│   ├── TenantContext.cs
│   ├── McpRequestContext.cs
│   ├── AgentRequest.cs
│   └── AgentResponse.cs
└── Configuration/
    ├── OAuthOptions.cs
    ├── AgentOptions.cs
    └── DatabaseOptions.cs
```

---

## TenantContext.cs

See full model in [arch-multi-tenant.md](arch-multi-tenant.md).

```csharp
namespace Diva.Core.Models;

public sealed class TenantContext
{
    // Core Identity (from OAuth JWT)
    public int TenantId { get; init; }
    public string TenantName { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string[] UserRoles { get; init; } = [];
    public int[] SiteIds { get; init; } = [];
    public int CurrentSiteId { get; init; }
    public string[] AgentAccess { get; init; } = [];

    // OAuth Token propagation
    public string AccessToken { get; init; } = string.Empty;
    public DateTime TokenExpiry { get; init; }
    public string? TeamApiKey { get; init; }

    // Request context
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString();
    public string? SessionId { get; init; }
    public Dictionary<string, string> CustomHeaders { get; init; } = new();

    // Helpers
    public bool CanAccessSite(int siteId) =>
        Role == "org_admin" || SiteIds.Contains(siteId);

    public bool CanUseAgent(string agentType) =>
        AgentAccess.Contains(agentType) || AgentAccess.Contains("*");
}
```

---

## McpRequestContext.cs

```csharp
namespace Diva.Core.Models;

public sealed class McpRequestContext
{
    public string Authorization { get; init; } = string.Empty;   // "Bearer <token>"
    public int TenantId { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public Dictionary<string, string> CustomHeaders { get; init; } = new();

    public static McpRequestContext FromTenantContext(TenantContext tenant) =>
        new()
        {
            Authorization  = $"Bearer {tenant.AccessToken}",
            TenantId       = tenant.TenantId,
            CorrelationId  = tenant.CorrelationId,
            CustomHeaders  = tenant.CustomHeaders
        };

    public Dictionary<string, string> ToHeaders()
    {
        var headers = new Dictionary<string, string>
        {
            ["Authorization"]    = Authorization,
            ["X-Tenant-ID"]      = TenantId.ToString(),
            ["X-Correlation-ID"] = CorrelationId
        };
        foreach (var (k, v) in CustomHeaders)
            headers[k] = v;
        return headers;
    }
}
```

---

## AgentRequest.cs

```csharp
namespace Diva.Core.Models;

public sealed class AgentRequest
{
    public string Query { get; init; } = string.Empty;
    public string? SessionId { get; init; }
    public string TriggerType { get; init; } = "user_request";  // "user_request" | "scheduled" | "event"
    public object? TriggerPayload { get; init; }
    public string? Source { get; init; }   // "gm_dashboard" | "mobile" | "api"
}
```

---

## AgentResponse.cs

```csharp
namespace Diva.Core.Models;

public sealed class AgentResponse
{
    public string Content { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
    public string AgentName { get; init; } = string.Empty;
    public List<string> ToolsUsed { get; init; } = [];
    public List<string> ReasoningSteps { get; init; } = [];
    public int TotalTokensUsed { get; init; }
    public TimeSpan ExecutionTime { get; init; }
    public bool Success { get; init; } = true;
    public string? ErrorMessage { get; init; }
    public List<FollowUpQuestion> FollowUpQuestions { get; init; } = [];
}

public sealed class FollowUpQuestion
{
    public string Type { get; init; } = string.Empty;   // "rule_confirmation" | "clarification"
    public string Text { get; init; } = string.Empty;
    public string[] Options { get; init; } = [];
    public object? Metadata { get; init; }
}
```

---

## OAuthOptions.cs

```csharp
namespace Diva.Core.Configuration;

public sealed class OAuthOptions
{
    public const string SectionName = "OAuth";

    public string Authority { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public bool ValidateIssuer { get; set; } = true;
    public bool ValidateAudience { get; set; } = true;
    public bool PropagateToken { get; set; } = true;
    public ClaimMappingsConfig ClaimMappings { get; set; } = new();
}

public sealed class ClaimMappingsConfig
{
    public string TenantId { get; set; } = "tenant_id";
    public string TenantName { get; set; } = "tenant_name";
    public string UserId { get; set; } = "sub";
    public string SiteIds { get; set; } = "site_ids";
    public string Roles { get; set; } = "roles";
    public string AgentAccess { get; set; } = "agent_access";
}
```

---

## DatabaseOptions.cs

```csharp
namespace Diva.Core.Configuration;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string Provider { get; set; } = "SQLite";   // "SQLite" | "SqlServer"
    public SQLiteOptions SQLite { get; set; } = new();
    public SqlServerOptions SqlServer { get; set; } = new();
}

public sealed class SQLiteOptions
{
    public string ConnectionString { get; set; } = "Data Source=diva.db";
}

public sealed class SqlServerOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public bool UseRls { get; set; } = true;
    public bool UseConnectionPerTenant { get; set; } = false;
}
```

---

## AgentOptions.cs

```csharp
namespace Diva.Core.Configuration;

public sealed class AgentOptions
{
    public const string SectionName = "Agents";

    public int MaxIterations { get; set; } = 10;
    public double DefaultTemperature { get; set; } = 0.7;
}
```

---

## Verification

- [ ] `dotnet build src/Diva.Core` succeeds with 0 errors
- [ ] All models are `sealed`, use `init` properties, have sensible defaults
- [ ] `McpRequestContext.ToHeaders()` includes Authorization, X-Tenant-ID, X-Correlation-ID + custom headers
- [ ] `TenantContext.CanAccessSite()` returns true for org_admin regardless of SiteIds
