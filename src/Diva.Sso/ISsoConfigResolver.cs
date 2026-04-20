namespace Diva.Sso;

/// <summary>
/// Resolves an SSO provider config from a token's identity signal.
/// Diva.Infrastructure provides a DB-backed implementation.
/// MCP servers provide a flat-file or env-var backed implementation.
/// </summary>
public interface ISsoConfigResolver
{
    /// <summary>
    /// Find provider config by JWT issuer claim (iss).
    /// Used for JWT tokens where the issuer is embedded in the token.
    /// </summary>
    Task<ISsoProviderConfig?> FindByIssuerAsync(string issuer, CancellationToken ct = default);

    /// <summary>
    /// Find active opaque provider config by tenant ID.
    /// Used when token is opaque and caller supplies X-Tenant-ID header.
    /// </summary>
    Task<ISsoProviderConfig?> FindByTenantIdAsync(int tenantId, CancellationToken ct = default);
}
