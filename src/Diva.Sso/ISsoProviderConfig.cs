namespace Diva.Sso;

/// <summary>
/// Configuration contract for an SSO provider.
/// Implemented by DB-backed (TenantSsoConfigEntity), env-var, or flat-file stores.
/// Consuming projects (Diva.Infrastructure, MCP servers) provide their own implementation.
/// </summary>
public interface ISsoProviderConfig
{
    string ProviderName { get; }           // "google" | "azure" | "okta" | "generic"
    string TokenType { get; }              // "jwt" | "opaque"
    string Issuer { get; }                 // JWT: iss claim value; Opaque: tenant identifier string
    string ClientId { get; }
    string ClientSecret { get; }
    string? Authority { get; }             // OIDC discovery base URL (auto-populates endpoints)
    string? AuthorizationEndpoint { get; } // explicit /authorize URL (overrides discovery)
    string? TokenEndpoint { get; }         // explicit /token URL
    string? UserinfoEndpoint { get; }      // explicit /userinfo URL
    /// <summary>
    /// RFC 7662 introspection endpoint. When set, opaque tokens are validated by POST to this URL.
    /// When null and UserinfoEndpoint is set, userinfo-based validation is used instead
    /// (GET UserinfoEndpoint with Bearer token — 200 = valid, claims extracted from response).
    /// </summary>
    string? IntrospectionEndpoint { get; }
    string Audience { get; }
    bool UseRoleMappings { get; }
    bool UseTeamMappings { get; }
    string? ClaimMappingsJson { get; }     // JSON: per-provider claim name overrides
    string? LogoutUrl { get; }             // provider logout endpoint (used by portal redirect-on-logout)
}
