namespace Diva.Infrastructure.Data.Entities;

/// <summary>
/// Persisted user profile, created/updated on every authenticated request.
/// Allows admins to view active users, disable accounts, and override agent access.
/// </summary>
public class UserProfileEntity : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; }

    /// <summary>JWT sub claim — unique per tenant. Natural key with TenantId.</summary>
    public string UserId { get; set; } = "";
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? AvatarUrl { get; set; }

    /// <summary>Roles from the JWT claims (mirrored from last login). JSON array via value converter.</summary>
    public string[] Roles { get; set; } = [];

    /// <summary>AgentAccess from the JWT claims (mirrored from last login). JSON array via value converter.</summary>
    public string[] AgentAccess { get; set; } = [];

    /// <summary>
    /// Admin-set overrides on top of JWT-granted agent access.
    /// When non-empty, takes precedence over JWT AgentAccess for CanInvokeAgent checks.
    /// JSON array via value converter.
    /// </summary>
    public string[] AgentAccessOverrides { get; set; } = [];

    /// <summary>
    /// When false, the user is blocked at the middleware level (403 Account disabled).
    /// Cached for 5 minutes to avoid a DB hit on every request.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastLoginAt { get; set; } = DateTime.UtcNow;

    /// <summary>Freeform JSON key-value bag for tenant-specific user attributes.</summary>
    public string? MetadataJson { get; set; }
}
