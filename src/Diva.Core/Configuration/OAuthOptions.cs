namespace Diva.Core.Configuration;

public sealed class OAuthOptions
{
    public const string SectionName = "OAuth";

    /// <summary>
    /// When false, TenantContextMiddleware bypasses token validation and injects a system context.
    /// Set to false in Development when no live IdP is available.
    /// Must be true in Production.
    /// </summary>
    public bool Enabled { get; set; } = false;

    public string Authority { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public bool ValidateIssuer { get; set; } = true;
    public bool ValidateAudience { get; set; } = true;
    public bool PropagateToken { get; set; } = true;

    /// <summary>
    /// Fallback tenant ID for opaque token validation when the request carries no X-Tenant-ID header.
    /// Useful for single-tenant deployments and custom OAuth2 providers that don't propagate tenant context.
    /// Set to 0 (default) to require explicit X-Tenant-ID on every opaque token request.
    /// </summary>
    public int DefaultTenantId { get; set; } = 0;

    public ClaimMappingsOptions ClaimMappings { get; set; } = new();
}

public sealed class ClaimMappingsOptions
{
    public string TenantId    { get; set; } = "tenant_id";
    public string TenantName  { get; set; } = "tenant_name";
    public string UserId      { get; set; } = "sub";
    /// <summary>Claim field name for the user's email address. e.g. "mail" for Exchange/O365.</summary>
    public string Email       { get; set; } = "email";
    /// <summary>Claim field name for the user's display name. e.g. "displayName" for Azure AD.</summary>
    public string DisplayName { get; set; } = "name";
    public string SiteIds     { get; set; } = "site_ids";
    public string Roles       { get; set; } = "roles";
    public string AgentAccess { get; set; } = "agent_access";
    public string TeamApiKey  { get; set; } = "litellm_team_key";
}
