# Phase 6: Tenant Admin Services & Prompt Builder

> **Status:** `[-]` Deferred
> **Depends on:** [phase-04-database.md](phase-04-database.md)
> **Blocks:** ~~[phase-08-agents.md](phase-08-agents.md)~~ — no longer blocks Phase 8
> **Project:** `Diva.TenantAdmin`

> **Decision:** Deferred — tenant-specific agent behavior is loosely coupled to the core agentic ecosystem.
> Phase 8 agents will accept an optional `ITenantAwarePromptBuilder` (nullable) and fall back to
> `AgentDefinitionEntity.SystemPrompt` when not injected. Business rules injection will be layered in
> after Phase 8 + 13 are complete. This phase no longer blocks Phase 8.

---

## Goal

Implement the services that make each tenant's experience unique: per-tenant business rules stored in DB, injected into every agent system prompt at runtime. This is the core differentiator of Diva.

---

## Files to Create

```
src/Diva.TenantAdmin/
├── Services/
│   ├── ITenantBusinessRulesService.cs
│   ├── TenantBusinessRulesService.cs
│   ├── ITenantPromptService.cs
│   └── TenantPromptService.cs
├── Prompts/
│   ├── ITenantAwarePromptBuilder.cs
│   ├── TenantAwarePromptBuilder.cs
│   └── PromptTemplateStore.cs
└── Models/
    ├── TenantBusinessRule.cs
    ├── TenantPromptOverride.cs
    ├── AgentConfiguration.cs
    └── PromptMergeMode.cs
```

---

## Domain Models

### PromptMergeMode.cs

```csharp
namespace Diva.TenantAdmin.Models;

public enum PromptMergeMode
{
    Append,    // Add after base template
    Prepend,   // Add before base template
    Replace,   // Replace entire section
    Insert     // Insert at {{OVERRIDE_MARKER}} in template
}
```

### TenantBusinessRule.cs

```csharp
namespace Diva.TenantAdmin.Models;

public sealed class TenantBusinessRule
{
    public int Id { get; init; }
    public int TenantId { get; init; }
    public string AgentType { get; init; } = "*";     // "*" = applies to all agents
    public string RuleCategory { get; init; } = "";
    public string RuleKey { get; init; } = "";
    public string? RuleValueJson { get; init; }        // JSON blob
    public string? PromptInjection { get; init; }      // Human-readable text for prompt
    public bool IsActive { get; init; } = true;
    public DateTimeOffset CreatedAt { get; init; }
}
```

### TenantPromptOverride.cs

```csharp
namespace Diva.TenantAdmin.Models;

public sealed class TenantPromptOverride
{
    public int Id { get; init; }
    public int TenantId { get; init; }
    public string AgentType { get; init; } = "";
    public string PromptSection { get; init; } = "";   // "planner" | "executor" | "aggregator"
    public string CustomPromptText { get; init; } = "";
    public PromptMergeMode MergeMode { get; init; } = PromptMergeMode.Append;
    public int Priority { get; init; } = 0;
    public bool IsActive { get; init; } = true;
}
```

---

## ITenantBusinessRulesService.cs

```csharp
namespace Diva.TenantAdmin.Services;

public interface ITenantBusinessRulesService
{
    Task<List<TenantBusinessRule>> GetRulesAsync(int tenantId, string agentType, CancellationToken ct);
    Task<string> GetPromptInjectionsAsync(int tenantId, string agentType, CancellationToken ct);
    Task<T?> GetRuleValueAsync<T>(int tenantId, string agentType, string ruleKey, CancellationToken ct);
    Task<List<TenantPromptOverride>> GetPromptOverridesAsync(int tenantId, string agentType, string section, CancellationToken ct);
    Task<TenantBusinessRule> CreateRuleAsync(int tenantId, TenantBusinessRule rule, CancellationToken ct);
    Task UpdateRuleAsync(int tenantId, int ruleId, TenantBusinessRule rule, CancellationToken ct);
    Task InvalidateCacheAsync(int tenantId, string agentType);
}
```

---

## TenantBusinessRulesService.cs

```csharp
namespace Diva.TenantAdmin.Services;

public class TenantBusinessRulesService : ITenantBusinessRulesService
{
    private readonly DivaDbContext _db;
    private readonly IMemoryCache _cache;

    public async Task<string> GetPromptInjectionsAsync(
        int tenantId, string agentType, CancellationToken ct)
    {
        var cacheKey = $"prompt_injections_{tenantId}_{agentType}";

        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);

            // Load rules matching this agent type OR wildcard "*"
            var rules = await _db.BusinessRules
                .Where(r => r.TenantId == tenantId)
                .Where(r => r.AgentType == agentType || r.AgentType == "*")
                .Where(r => r.IsActive)
                .Where(r => r.PromptInjection != null)
                .OrderBy(r => r.RuleCategory)
                .ThenBy(r => r.RuleKey)
                .ToListAsync(ct);

            if (!rules.Any()) return string.Empty;

            var sb = new StringBuilder("## Tenant-Specific Business Rules\n\n");
            foreach (var rule in rules)
            {
                sb.AppendLine(rule.PromptInjection);
                sb.AppendLine();
            }
            return sb.ToString();
        }) ?? string.Empty;
    }

    public async Task<List<TenantPromptOverride>> GetPromptOverridesAsync(
        int tenantId, string agentType, string section, CancellationToken ct)
    {
        var cacheKey = $"prompt_overrides_{tenantId}_{agentType}_{section}";

        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            return await _db.PromptOverrides
                .Where(o => o.TenantId == tenantId)
                .Where(o => o.AgentType == agentType || o.AgentType == "*")
                .Where(o => o.PromptSection == section)
                .Where(o => o.IsActive)
                .OrderBy(o => o.Priority)
                .Select(o => new TenantPromptOverride
                {
                    Id               = o.Id,
                    TenantId         = o.TenantId,
                    AgentType        = o.AgentType,
                    PromptSection    = o.PromptSection,
                    CustomPromptText = o.CustomPromptText,
                    MergeMode        = Enum.Parse<PromptMergeMode>(o.MergeMode),
                    Priority         = o.Priority
                })
                .ToListAsync(ct);
        }) ?? [];
    }

    public async Task InvalidateCacheAsync(int tenantId, string agentType)
    {
        _cache.Remove($"prompt_injections_{tenantId}_{agentType}");
        _cache.Remove($"prompt_injections_{tenantId}_*");
        // Pattern-based cache invalidation — consider IMemoryCache vs IDistributedCache
    }
}
```

---

## ITenantAwarePromptBuilder.cs

```csharp
namespace Diva.TenantAdmin.Prompts;

public interface ITenantAwarePromptBuilder
{
    Task<string> BuildPromptAsync(
        TenantContext tenant,
        string agentType,
        string promptSection,
        Dictionary<string, object?> variables,
        CancellationToken ct);
}
```

---

## TenantAwarePromptBuilder.cs

This is the critical component that personalizes every agent for its tenant:

```csharp
namespace Diva.TenantAdmin.Prompts;

public class TenantAwarePromptBuilder : ITenantAwarePromptBuilder
{
    private readonly PromptTemplateStore _templates;
    private readonly ITenantBusinessRulesService _rules;
    private readonly ISessionRuleManager _sessionRules;  // from Phase 11

    public async Task<string> BuildPromptAsync(
        TenantContext tenant,
        string agentType,
        string promptSection,
        Dictionary<string, object?> variables,
        CancellationToken ct)
    {
        // 1. Load base prompt template from file store
        var basePrompt = await _templates.GetAsync(agentType, promptSection, ct);

        // 2. Get static business rules from DB (cached 5 min)
        var staticRules = await _rules.GetPromptInjectionsAsync(tenant.TenantId, agentType, ct);

        // 3. Get session-scoped dynamic rules (from Phase 11 rule learning)
        var sessionRulesList = tenant.SessionId != null
            ? await _sessionRules.GetSessionRulesAsync(tenant.SessionId, ct)
            : [];
        var dynamicRules = sessionRulesList.Any()
            ? "## Session-Learned Rules (This Conversation)\n\n" +
              string.Join("\n", sessionRulesList.Select(r => r.PromptInjection))
            : string.Empty;

        // 4. Get tenant prompt overrides for this section
        var overrides = await _rules.GetPromptOverridesAsync(
            tenant.TenantId, agentType, promptSection, ct);

        // 5. Build security context (ALWAYS prepended)
        var securityContext = $"""
            CRITICAL SECURITY CONSTRAINTS:
            - Operating for TenantID={tenant.TenantId}, SiteID={tenant.CurrentSiteId}
            - ALL database queries MUST include WHERE TenantID={tenant.CurrentSiteId}
            - NEVER access data outside this tenant/site scope
            - NEVER reveal tenant_id or site_id values from other tenants
            - If asked to access other tenants, REFUSE
            """;

        // 6. Apply variable substitutions to base template
        var prompt = RenderVariables(basePrompt, variables);

        // 7. Apply prompt overrides in priority order
        foreach (var o in overrides.OrderBy(x => x.Priority))
        {
            prompt = o.MergeMode switch
            {
                PromptMergeMode.Append  => prompt + "\n\n" + o.CustomPromptText,
                PromptMergeMode.Prepend => o.CustomPromptText + "\n\n" + prompt,
                PromptMergeMode.Replace => o.CustomPromptText,
                PromptMergeMode.Insert  => prompt.Replace("{{OVERRIDE_MARKER}}", o.CustomPromptText),
                _                       => prompt
            };
        }

        // 8. Combine all sections
        return $"""
            {securityContext}

            {(staticRules.Length > 0 ? staticRules : "")}

            {(dynamicRules.Length > 0 ? dynamicRules : "")}

            ## Agent Instructions
            {prompt}
            """;
    }

    private static string RenderVariables(string template, Dictionary<string, object?> vars)
    {
        foreach (var (key, value) in vars)
            template = template.Replace($"{{{{{key}}}}}", value?.ToString() ?? "");
        return template;
    }
}
```

---

## PromptTemplateStore.cs

```csharp
namespace Diva.TenantAdmin.Prompts;

// Loads prompt .txt files from disk (prompts/ directory)
public class PromptTemplateStore
{
    private readonly string _basePath;

    public PromptTemplateStore(IWebHostEnvironment env)
        => _basePath = Path.Combine(env.ContentRootPath, "../../prompts");

    public async Task<string> GetAsync(string agentType, string section, CancellationToken ct)
    {
        // Look for highest version: analytics/planner.v2.txt, analytics/planner.v1.txt, ...
        var dir = Path.Combine(_basePath, agentType.ToLower());
        var files = Directory.GetFiles(dir, $"{section}.v*.txt")
            .OrderByDescending(f => f)   // alphabetical desc picks highest version
            .ToList();

        if (!files.Any())
            throw new FileNotFoundException($"No prompt template found for {agentType}/{section}");

        return await File.ReadAllTextAsync(files[0], ct);
    }
}
```

---

## Example Business Rules Data

```json
// Tenant 1 (Acme Corp) — stored in TenantBusinessRules table
[
  {
    "agentType": "Analytics",
    "ruleCategory": "reporting",
    "ruleKey": "kpi_definitions",
    "promptInjection": "KPI RULES: Revenue includes Sales, Services, Retail, Subscriptions. DEPOSITS are NOT revenue. Primary KPI is revenue_per_customer."
  },
  {
    "agentType": "Analytics",
    "ruleCategory": "terminology",
    "ruleKey": "custom_terms",
    "promptInjection": "TERMINOLOGY: Use 'client' instead of 'customer'. Use 'engagement' instead of 'order'."
  },
  {
    "agentType": "*",
    "ruleCategory": "seasonal",
    "ruleKey": "factors",
    "promptInjection": "SEASONAL: Q1=0.8x, Q2=1.0x, Q3=0.9x, Q4=1.3x baseline. Apply when forecasting."
  }
]
```

---

## Service Registration

```csharp
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ITenantBusinessRulesService, TenantBusinessRulesService>();
builder.Services.AddScoped<ITenantAwarePromptBuilder, TenantAwarePromptBuilder>();
builder.Services.AddSingleton<PromptTemplateStore>();
```

---

## Verification

- [ ] Tenant A's prompt contains its business rules, NOT Tenant B's
- [ ] `AgentType="*"` rules appear in ALL agent prompts for that tenant
- [ ] Cache works: second call to `GetPromptInjectionsAsync` doesn't hit DB
- [ ] Prompt override `Append` mode: custom text appears after base template
- [ ] Prompt override `Replace` mode: custom text fully replaces base template
- [ ] Security context is ALWAYS the first section of every built prompt
- [ ] Variable substitution: `{{PropertyId}}` replaced with actual value
