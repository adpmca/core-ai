using System.Security.Claims;
using Diva.Core.Configuration;
using Diva.Sso;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Diva.Infrastructure.Auth;

/// <summary>
/// Multi-tenant JWT/opaque token validator.
///
/// JWT path:
///   - Decodes iss claim without verification → FindByIssuerAsync → per-tenant JWKS validation.
///   - Falls back to global OAuthOptions validation if no tenant config is found.
///
/// Opaque path:
///   - Reads X-Tenant-ID header → FindByTenantIdAsync → introspection endpoint.
///   - Returns null if no X-Tenant-ID header or no opaque config for that tenant.
///
/// Replaces the original OAuthTokenValidator (global OIDC only).
/// </summary>
public sealed class MultiTenantOAuthTokenValidator : IOAuthTokenValidator
{
    private readonly ISsoConfigResolver _resolver;
    private readonly ISsoTokenValidator _validator;
    private readonly OAuthTokenValidator _fallback;
    private readonly IHttpContextAccessor _httpCtx;
    private readonly ILogger<MultiTenantOAuthTokenValidator> _logger;
    private readonly ILocalAuthService _localAuth;
    private readonly OAuthOptions _options;
    private readonly AppBrandingOptions _branding;

    public MultiTenantOAuthTokenValidator(
        ISsoConfigResolver resolver,
        ISsoTokenValidator validator,
        OAuthTokenValidator fallback,
        IHttpContextAccessor httpCtx,
        ILogger<MultiTenantOAuthTokenValidator> logger,
        ILocalAuthService localAuth,
        IOptions<OAuthOptions> options,
        IOptions<AppBrandingOptions> branding)
    {
        _resolver  = resolver;
        _validator = validator;
        _fallback  = fallback;
        _httpCtx   = httpCtx;
        _logger    = logger;
        _localAuth = localAuth;
        _options   = options.Value;
        _branding  = branding.Value;
    }

    public async Task<ClaimsPrincipal?> ValidateTokenAsync(string token, CancellationToken ct)
    {
        if (SsoTokenValidator.IsJwt(token))
            return await ValidateJwtAsync(token, ct);

        return await ValidateOpaqueAsync(token, ct);
    }

    // ── JWT ───────────────────────────────────────────────────────────────────

    private async Task<ClaimsPrincipal?> ValidateJwtAsync(string token, CancellationToken ct)
    {
        var issuer = SsoTokenValidator.PeekIssuer(token);

        // Fast path: local username/password JWT — validated with symmetric key, no async needed
        if (issuer == _branding.LocalIssuer)
        {
            _logger.LogDebug("Validating local JWT");
            return _localAuth.ValidateLocalToken(token);
        }

        if (!string.IsNullOrEmpty(issuer))
        {
            var config = await _resolver.FindByIssuerAsync(issuer, ct);
            if (config is not null)
            {
                _logger.LogDebug("Validating JWT for issuer {Issuer} via per-tenant config", issuer);
                return await _validator.ValidateAsync(token, config, ct);
            }
        }

        // No per-tenant config found — fall back to global OAuthOptions
        _logger.LogDebug("No per-tenant config for issuer {Issuer} — using global validator", issuer);
        return await _fallback.ValidateTokenAsync(token, ct);
    }

    // ── Opaque token ──────────────────────────────────────────────────────────

    private async Task<ClaimsPrincipal?> ValidateOpaqueAsync(string token, CancellationToken ct)
    {
        var tenantIdHeader = _httpCtx.HttpContext?.Request.Headers["X-Tenant-ID"].FirstOrDefault();
        if (!int.TryParse(tenantIdHeader, out var tenantId) || tenantId <= 0)
        {
            // Fall back to DefaultTenantId when header is absent.
            // Covers custom providers that don't propagate tenant context and
            // single-tenant deployments where every token belongs to the same tenant.
            if (_options.DefaultTenantId > 0)
            {
                tenantId = _options.DefaultTenantId;
                _logger.LogDebug("X-Tenant-ID header missing — using DefaultTenantId {TenantId}", tenantId);
            }
            else
            {
                _logger.LogWarning("Opaque token received but X-Tenant-ID header is missing and DefaultTenantId is not configured");
                return null;
            }
        }

        var config = await _resolver.FindByTenantIdAsync(tenantId, ct);
        if (config is null)
        {
            _logger.LogWarning("No opaque SSO config found for tenant {TenantId}", tenantId);
            return null;
        }

        _logger.LogDebug("Validating opaque token for tenant {TenantId} via introspection", tenantId);
        var principal = await _validator.ValidateAsync(token, config, ct);
        if (principal is null) return null;

        return EnrichOpaquePrincipal(principal, tenantId);
    }

    /// <summary>
    /// Enriches a ClaimsPrincipal built from an opaque token's GetUserInfo response.
    ///
    /// Custom OAuth2 providers (e.g. totaleintegrated) don't include OIDC-standard
    /// claims in their userinfo response. Without this enrichment:
    ///
    ///   - tenant_id is missing  → TenantContext.TenantId = 0
    ///     → UpsertOnLoginAsync guard fires: if (tenant.TenantId &lt;= 0) return
    ///     → user profile is never created
    ///
    ///   - sub is missing        → TenantContext.UserId = ""
    ///     → same guard fires, or profile is created with empty UserId (duplicate key on next login)
    ///
    /// Fixes:
    ///   1. Inject tenant_id from the X-Tenant-ID header (already validated for routing).
    ///   2. Map sub ← username ← email so TenantClaimsExtractor always finds a user identifier.
    /// </summary>
    private static ClaimsPrincipal EnrichOpaquePrincipal(ClaimsPrincipal principal, int tenantId)
    {
        var identity = (ClaimsIdentity)principal.Identity!;

        // 1. Inject tenant_id if provider didn't include it
        if (!principal.HasClaim(c => c.Type == "tenant_id"))
            identity.AddClaim(new Claim("tenant_id", tenantId.ToString()));

        // 2. Ensure sub is present — fall back to username → email
        if (!principal.HasClaim(c => c.Type == "sub"))
        {
            var fallback = principal.FindFirstValue("username")
                        ?? principal.FindFirstValue("email")
                        ?? principal.FindFirstValue(ClaimTypes.Email);
            if (fallback is not null)
                identity.AddClaim(new Claim("sub", fallback));
        }

        return principal;
    }
}
