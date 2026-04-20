# Phase 3: OAuth Validation & Tenant Middleware

> **Status:** `[ ]` Not Started
> **Depends on:** [phase-02-core-models.md](phase-02-core-models.md)
> **Blocks:** [phase-05-mcp-tools.md](phase-05-mcp-tools.md), [phase-08-agents.md](phase-08-agents.md), [phase-10-api-host.md](phase-10-api-host.md)
> **Project:** `Diva.Infrastructure`
> **Architecture ref:** [arch-oauth-flow.md](arch-oauth-flow.md)

---

## Goal

Validate OAuth JWT tokens, extract tenant identity from claims, and make `TenantContext` available throughout the request pipeline via `HttpContext.Items`.

---

## Files to Create

```
src/Diva.Infrastructure/Auth/
├── OAuthTokenValidator.cs
├── TenantClaimsExtractor.cs
├── TenantContextMiddleware.cs
└── HeaderPropagationHandler.cs
```

---

## OAuthTokenValidator.cs

```csharp
namespace Diva.Infrastructure.Auth;

public interface IOAuthTokenValidator
{
    Task<ClaimsPrincipal?> ValidateAsync(string token, CancellationToken ct = default);
}

public class OAuthTokenValidator : IOAuthTokenValidator
{
    private readonly OAuthOptions _options;
    private readonly ILogger<OAuthTokenValidator> _logger;

    public OAuthTokenValidator(IOptions<OAuthOptions> options, ILogger<OAuthTokenValidator> logger)
    {
        _options = options.Value;
        _logger  = logger;
    }

    public async Task<ClaimsPrincipal?> ValidateAsync(string token, CancellationToken ct = default)
    {
        var handler = new JsonWebTokenHandler();

        var parameters = new TokenValidationParameters
        {
            ValidateIssuer           = _options.ValidateIssuer,
            ValidIssuer              = _options.Authority,
            ValidateAudience         = _options.ValidateAudience,
            ValidAudience            = _options.Audience,
            ValidateLifetime         = true,
            IssuerSigningKeys        = await GetSigningKeysAsync(ct),
            ClockSkew                = TimeSpan.FromMinutes(5)
        };

        var result = await handler.ValidateTokenAsync(token, parameters);

        if (!result.IsValid)
        {
            _logger.LogWarning("Token validation failed: {Reason}", result.Exception?.Message);
            return null;
        }

        return new ClaimsPrincipal(result.ClaimsIdentity);
    }

    private async Task<IEnumerable<SecurityKey>> GetSigningKeysAsync(CancellationToken ct)
    {
        // Fetch JWKS from Authority (cached in production via IMemoryCache)
        var configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            $"{_options.Authority}/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever());

        var config = await configManager.GetConfigurationAsync(ct);
        return config.SigningKeys;
    }
}
```

---

## TenantClaimsExtractor.cs

```csharp
namespace Diva.Infrastructure.Auth;

public interface ITenantClaimsExtractor
{
    TenantContext Extract(ClaimsPrincipal principal, IHeaderDictionary requestHeaders);
}

public class TenantClaimsExtractor : ITenantClaimsExtractor
{
    private readonly OAuthOptions _options;

    public TenantClaimsExtractor(IOptions<OAuthOptions> options) => _options = options.Value;

    public TenantContext Extract(ClaimsPrincipal principal, IHeaderDictionary requestHeaders)
    {
        var mappings = _options.ClaimMappings;

        // Parse site_ids — could be "1,2,3" or a JSON array claim
        var siteIdsClaim = principal.FindFirst(mappings.SiteIds)?.Value ?? string.Empty;
        var siteIds = siteIdsClaim
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s.Trim(), out var id) ? id : 0)
            .Where(id => id > 0)
            .ToArray();

        // Parse agent_access — could be "Analytics,Reservation" or JSON array
        var agentAccessClaim = principal.FindFirst(mappings.AgentAccess)?.Value ?? "*";
        var agentAccess = agentAccessClaim
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .ToArray();

        // Determine CurrentSiteId — from request header or first accessible site
        var requestedSite = requestHeaders["X-Site-ID"].FirstOrDefault();
        var currentSiteId = int.TryParse(requestedSite, out var sid) && siteIds.Contains(sid)
            ? sid
            : siteIds.FirstOrDefault();

        // Extract X-Tenant-* custom headers
        var customHeaders = requestHeaders
            .Where(h => h.Key.StartsWith("X-Tenant-", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(h => h.Key, h => h.Value.ToString());

        return new TenantContext
        {
            TenantId      = int.TryParse(principal.FindFirst(mappings.TenantId)?.Value, out var tid) ? tid : 0,
            TenantName    = principal.FindFirst(mappings.TenantName)?.Value ?? string.Empty,
            UserId        = principal.FindFirst(mappings.UserId)?.Value ?? string.Empty,
            Role          = principal.FindFirst(mappings.Roles)?.Value ?? string.Empty,
            UserRoles     = principal.FindAll(mappings.Roles).Select(c => c.Value).ToArray(),
            SiteIds       = siteIds,
            CurrentSiteId = currentSiteId,
            AgentAccess   = agentAccess,
            AccessToken   = GetTokenFromPrincipal(principal),
            TokenExpiry   = GetExpiry(principal),
            CorrelationId = requestHeaders["X-Correlation-ID"].FirstOrDefault()
                            ?? Guid.NewGuid().ToString(),
            CustomHeaders = customHeaders
        };
    }

    private static string GetTokenFromPrincipal(ClaimsPrincipal principal)
    {
        // The raw token is stored in the identity's bootstrap context when using JwtBearer
        if (principal.Identity is ClaimsIdentity { BootstrapContext: string token })
            return token;
        return string.Empty;
    }

    private static DateTime GetExpiry(ClaimsPrincipal principal)
    {
        var exp = principal.FindFirst(JwtRegisteredClaimNames.Exp)?.Value;
        return long.TryParse(exp, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
            : DateTime.UtcNow.AddHours(1);
    }
}
```

---

## TenantContextMiddleware.cs

```csharp
namespace Diva.Infrastructure.Auth;

public class TenantContextMiddleware
{
    private readonly RequestDelegate _next;

    public TenantContextMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext context,
        IOAuthTokenValidator validator,
        ITenantClaimsExtractor extractor,
        ILogger<TenantContextMiddleware> logger)
    {
        // Skip middleware for health checks
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await _next(context);
            return;
        }

        // Extract Bearer token
        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        var token = authHeader?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(token))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Missing authorization token" });
            return;
        }

        // Validate token
        var principal = await validator.ValidateAsync(token, context.RequestAborted);

        if (principal == null)
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid or expired token" });
            return;
        }

        // Build TenantContext and store in request scope
        var tenantContext = extractor.Extract(principal, context.Request.Headers);

        if (tenantContext.TenantId == 0)
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Token missing tenant_id claim" });
            return;
        }

        context.Items["TenantContext"] = tenantContext;

        logger.LogDebug(
            "Request authenticated for TenantId={TenantId} SiteId={SiteId} UserId={UserId}",
            tenantContext.TenantId, tenantContext.CurrentSiteId, tenantContext.UserId);

        await _next(context);
    }
}

// Extension to access TenantContext from controllers
public static class HttpContextExtensions
{
    public static TenantContext GetTenantContext(this HttpContext context) =>
        context.Items["TenantContext"] as TenantContext
        ?? throw new InvalidOperationException("TenantContext not found. Is TenantContextMiddleware registered?");
}
```

---

## HeaderPropagationHandler.cs

Used as a `DelegatingHandler` for downstream HttpClient calls (not MCP, but other HTTP services):

```csharp
namespace Diva.Infrastructure.Auth;

public class HeaderPropagationHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HeaderPropagationHandler(IHttpContextAccessor accessor) => _httpContextAccessor = accessor;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var context = _httpContextAccessor.HttpContext?.Items["TenantContext"] as TenantContext;

        if (context != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);
            request.Headers.TryAddWithoutValidation("X-Tenant-ID", context.TenantId.ToString());
            request.Headers.TryAddWithoutValidation("X-Correlation-ID", context.CorrelationId);

            foreach (var (key, value) in context.CustomHeaders)
                request.Headers.TryAddWithoutValidation(key, value);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
```

---

## Service Registration (in Program.cs)

```csharp
// In Diva.Host/Program.cs
builder.Services.Configure<OAuthOptions>(builder.Configuration.GetSection(OAuthOptions.SectionName));
builder.Services.AddSingleton<IOAuthTokenValidator, OAuthTokenValidator>();
builder.Services.AddScoped<ITenantClaimsExtractor, TenantClaimsExtractor>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<HeaderPropagationHandler>();

// Register middleware
app.UseMiddleware<TenantContextMiddleware>();
```

---

## Verification

- [ ] Valid JWT → `TenantContext` populated in `HttpContext.Items`
- [ ] Missing JWT → 401 returned, no further middleware executed
- [ ] Expired JWT → 401 returned
- [ ] `X-Tenant-*` request headers → copied into `TenantContext.CustomHeaders`
- [ ] `X-Site-ID` header validates against `SiteIds[]` claim
- [ ] Health check endpoints bypass middleware (no token required)
