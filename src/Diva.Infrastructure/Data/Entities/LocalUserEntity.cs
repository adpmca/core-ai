namespace Diva.Infrastructure.Data.Entities;

/// <summary>
/// Persisted local username/password user.
/// Complements SSO-based UserProfileEntity for tenants that want local accounts.
/// </summary>
public class LocalUserEntity : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";

    /// <summary>
    /// PBKDF2-SHA256 hash stored as "base64(salt).base64(hash)".
    /// Never expose this field through any API response.
    /// </summary>
    public string PasswordHash { get; set; } = "";

    public string DisplayName { get; set; } = "";

    /// <summary>Role names for this user. Stored as JSON array via value converter.</summary>
    public string[] Roles { get; set; } = [];

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}
