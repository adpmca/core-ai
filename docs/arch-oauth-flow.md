# Architecture: OAuth Flow & MCP Header Injection

> **Status:** Reference — no code to write here
> **Related phases:** [phase-03-oauth-tenant.md](phase-03-oauth-tenant.md), [phase-05-mcp-tools.md](phase-05-mcp-tools.md)

---

## OAuth Integration Flow

```
┌──────────────┐      ┌──────────────┐      ┌──────────────┐
│  MAIN APP    │      │  Diva API    │      │  MCP TOOLS   │
│  (OAuth      │      │  HOST        │      │  (Downstream │
│   Provider)  │      │              │      │   Services)  │
└──────┬───────┘      └──────┬───────┘      └──────┬───────┘
       │                     │                     │
       │  1. User authenticates with Main App       │
       │                                           │
       │  2. Main App calls Diva API               │
       │     with OAuth Bearer token               │
       │ ─────────────────────►                    │
       │                     │                     │
       │                     │  3. Diva validates  │
       │                     │     token, extracts │
       │                     │     TenantContext   │
       │                     │                     │
       │                     │  4. Agent calls     │
       │                     │     MCP tools with  │
       │                     │     OAuth + headers │
       │                     │ ─────────────────────►
       │                     │                     │
       │                     │                     │  5. MCP tool calls
       │                     │                     │     backend APIs
       │                     │                     │     with propagated token
```

---

## TenantContextMiddleware Pipeline

```
Incoming HTTP Request
        │
        ▼
┌────────────────────────────────────────┐
│  TenantContextMiddleware               │
│                                        │
│  1. Extract Bearer token from          │
│     Authorization header               │
│                                        │
│  2. Validate JWT (signature, expiry,   │
│     issuer, audience)                  │
│                                        │
│  3. Extract claims via                 │
│     TenantClaimsExtractor              │
│     using configurable ClaimMappings   │
│                                        │
│  4. Build TenantContext (rich model)   │
│     - TenantId, SiteIds[], Role        │
│     - AccessToken (for propagation)    │
│     - CustomHeaders (X-Tenant-* prefix)│
│                                        │
│  5. Store in HttpContext.Items         │
│     ["TenantContext"]                  │
└───────────────────┬────────────────────┘
                    │
                    ▼
            Controller / Agent
```

---

## MCP Header Injection

All MCP tool calls automatically receive:

```
Headers sent to every MCP Tool Server:
├── Authorization: Bearer eyJhbGci...    ← OAuth token forwarded
├── X-Tenant-ID: 12345
├── X-Correlation-ID: 550e8400-...
├── X-Tenant-Region: us-east-1           ← from X-Tenant-* request headers
├── X-Tenant-Environment: production
└── (any additional X-Tenant-* headers passed by client)
```

### TenantAwareMcpClient — How it works

```csharp
// Every MCP tool call goes through TenantAwareMcpClient
public async Task<ToolResult> InvokeToolAsync(string toolName, object parameters, CancellationToken ct)
{
    // 1. Get TenantContext from request scope
    var tenant = _httpContext.HttpContext?.Items["TenantContext"] as TenantContext
        ?? throw new UnauthorizedAccessException("No tenant context");

    // 2. Build header context
    var mcpContext = McpRequestContext.FromTenantContext(tenant);

    // 3. Inject headers into MCP call
    return await _inner.InvokeToolAsync(toolName, parameters, mcpContext, ct);
}
```

### McpRequestContext.ToHeaders()

```csharp
public Dictionary<string, string> ToHeaders() => new()
{
    ["Authorization"]    = Authorization,      // "Bearer <token>"
    ["X-Tenant-ID"]      = TenantId.ToString(),
    ["X-Correlation-ID"] = CorrelationId,
    // + all CustomHeaders (X-Tenant-* prefix)
};
```

---

## Per-Tenant Header Configuration

Tenants can define custom header rules in their configuration:

```json
{
  "TenantId": 1,
  "HeaderRules": {
    "X-Backend-Tenant": {
      "Source": "Claim",
      "SourceValue": "backend_tenant_id"
    },
    "X-API-Version": {
      "Source": "Static",
      "SourceValue": "v2"
    },
    "X-Region": {
      "Source": "RequestHeader",
      "SourceValue": "X-Tenant-Region"
    }
  }
}
```

`HeaderSource` enum: `Claim` | `Static` | `Transform` | `RequestHeader`

---

## OAuth appsettings.json Configuration

```json
{
  "OAuth": {
    "Authority": "https://your-identity-provider.com",
    "Audience": "diva-api",
    "ValidateIssuer": true,
    "ValidateAudience": true,
    "ClaimMappings": {
      "TenantId": "tenant_id",
      "TenantName": "tenant_name",
      "UserId": "sub",
      "SiteIds": "site_ids",
      "Roles": "roles",
      "AgentAccess": "agent_access"
    },
    "PropagateToken": true
  }
}
```

---

## Key Files

| File | Project | Role |
|------|---------|------|
| `Auth/OAuthTokenValidator.cs` | Diva.Infrastructure | JWT signature + expiry validation |
| `Auth/TenantClaimsExtractor.cs` | Diva.Infrastructure | Maps JWT claims → TenantContext fields |
| `Auth/TenantContextMiddleware.cs` | Diva.Infrastructure | Middleware pipeline entry point |
| `Auth/HeaderPropagationHandler.cs` | Diva.Infrastructure | DelegatingHandler for downstream HTTP calls |
| `Tools/Core/TenantAwareMcpClient.cs` | Diva.Tools | Wraps MCP client with header injection |
| `Tools/Core/McpHeaderPropagator.cs` | Diva.Tools | Builds header dict from TenantContext |
| `Core/Models/McpRequestContext.cs` | Diva.Core | DTO: header bag for MCP calls |
