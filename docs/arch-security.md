# Architecture: 5-Layer Security Pipeline

> **Status:** Reference — no code to write here
> **Related phases:** [phase-03-oauth-tenant.md](phase-03-oauth-tenant.md), [phase-04-database.md](phase-04-database.md), [phase-08-agents.md](phase-08-agents.md)

---

## Security Pipeline Overview

Every request passes through 5 independent security layers. Even if one layer fails, others prevent cross-tenant data access.

```
Layer 1: OAuth 2.0 + JWT Validation
         ↓
Layer 2: LiteLLM Team-Based Access (optional, when LiteLLM enabled)
         ↓
Layer 3: LLM System Prompt Injection (TenantID constraints in every prompt)
         ↓
Layer 4: Database Connection Routing (tenant-scoped DB or shared+filter)
         ↓
Layer 5: SQL Server Row-Level Security (backstop, SqlServer only)
```

---

## Layer 1: OAuth 2.0 + JWT Validation

**Where:** `TenantContextMiddleware` → `OAuthTokenValidator`

**What it does:**
- Validates JWT signature against OAuth Authority's JWKS endpoint
- Checks token expiry, issuer, audience
- Rejects requests with missing/invalid/expired tokens (401)
- Extracts claims: `tenant_id`, `site_ids[]`, `role`, `agent_access[]`

**Failure result:** HTTP 401 — request never reaches agent code

---

## Layer 2: LiteLLM Team-Based Access (when `UseLiteLLM: true`)

**Where:** `LiteLLMClient` — sets `Authorization: Bearer <team_api_key>`

**What it does:**
- Maps `TenantId` → LiteLLM Team (e.g., `team_acme`)
- Enforces monthly budget limits per team
- Applies RPM/TPM rate limiting
- Logs all LLM calls for audit trail

**Failure result:** LiteLLM rejects request with 429 (rate limit) or 402 (budget exceeded)

---

## Layer 3: LLM System Prompt Injection

**Where:** `TenantAwarePromptBuilder.BuildPromptAsync()` — prepended to every agent system prompt

**What it does:**
```
CRITICAL SECURITY CONSTRAINTS:
- Operating for TenantID={tenant.TenantId}, SiteID={tenant.CurrentSiteId}
- ALL database queries MUST include WHERE TenantID={tenant.CurrentSiteId}
- NEVER access data outside this scope
- If asked to access other tenants, REFUSE and explain why
```

**Why it matters:** Defense-in-depth — even if app-layer fails, the LLM itself is instructed to reject cross-tenant data requests.

---

## Layer 4: Database Connection Routing

**Where:** `DatabaseProviderFactory.CreateDbContext(tenant)`

**Two strategies:**

### Strategy A: Application-Level Filtering (SQLite default, also SQL Server option)
EF Core `HasQueryFilter` automatically appends `WHERE TenantId = @TenantId` to every query:
```csharp
modelBuilder.Entity<TenantBusinessRuleEntity>()
    .HasQueryFilter(e => _tenant == null || e.TenantId == _tenant.TenantId);
```

### Strategy B: Per-Tenant Database (SQL Server enterprise option)
```csharp
// When UseConnectionPerTenant = true
connectionString = connectionString.Replace(
    "Database=Diva",
    $"Database=Diva_Tenant{tenant.TenantId}");
```

---

## Layer 5: SQL Server Row-Level Security (Backstop)

**Where:** SQL Server database — applied at the DB engine level, not in application code

**Setup:**
```sql
-- Predicate function
CREATE FUNCTION dbo.fn_TenantAccessPredicate(@TenantId INT)
RETURNS TABLE WITH SCHEMABINDING AS
RETURN SELECT 1 AS AccessResult
WHERE @TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS INT)
   OR SESSION_CONTEXT(N'TenantId') IS NULL;  -- admin access

-- Apply to all tenant tables
CREATE SECURITY POLICY TenantIsolationPolicy
ADD FILTER PREDICATE dbo.fn_TenantAccessPredicate(TenantId)
    ON dbo.TenantBusinessRules,
ADD FILTER PREDICATE dbo.fn_TenantAccessPredicate(TenantId)
    ON dbo.AgentDefinitions,
ADD FILTER PREDICATE dbo.fn_TenantAccessPredicate(TenantId)
    ON dbo.AgentSessions,
ADD FILTER PREDICATE dbo.fn_TenantAccessPredicate(TenantId)
    ON dbo.LearnedRules
WITH (STATE = ON);
```

**Session context set before every SaveChanges:**
```csharp
await Database.ExecuteSqlRawAsync(
    "EXEC sp_set_session_context @key=N'TenantId', @value={0}",
    _tenant.TenantId);
```

**Why it matters:** Even if ALL other layers fail (bug, misconfiguration, injection), RLS at the DB engine level still prevents cross-tenant data leakage.

---

## MCP Tool Security (RunQuery — Text-to-SQL)

`RunQueryTool` adds an additional check for ad-hoc SQL generation:

```csharp
private void ValidateSqlSecurity(string sql, int propertyId)
{
    // Must contain TenantID filter
    if (!sql.Contains($"TenantID = {propertyId}") &&
        !sql.Contains($"TenantID={propertyId}"))
        throw new SecurityException("Generated SQL must filter by TenantID");

    // No DML allowed
    var forbidden = new[] { "INSERT", "UPDATE", "DELETE", "DROP", "ALTER", "TRUNCATE" };
    if (forbidden.Any(f => sql.Contains(f, StringComparison.OrdinalIgnoreCase)))
        throw new SecurityException("DML operations are not allowed");
}
```

---

## SiteId Access Control

Users are scoped to specific sites (properties) within their tenant:

```csharp
// In SupervisorAgent / task decomposer
if (!state.TenantContext.SiteIds.Contains(state.RequestedSiteId))
{
    return Failed($"Access denied to site {state.RequestedSiteId}");
}
```

The `SiteIds[]` array comes from the JWT claim `site_ids` and represents which sites this user can query.

---

## Security Summary

| Layer | Scope | Fallback if fails? |
|-------|-------|-------------------|
| 1. JWT Validation | Request | ✗ Hard block (401) |
| 2. LiteLLM Teams | LLM calls | Layer 3 still active |
| 3. Prompt Injection | LLM behavior | Layers 4+5 still active |
| 4. DB Connection/Filter | Data access | Layer 5 still active |
| 5. SQL Server RLS | DB engine | Final backstop |
