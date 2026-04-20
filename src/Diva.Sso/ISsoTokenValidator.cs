using System.Security.Claims;

namespace Diva.Sso;

/// <summary>
/// Validates an SSO token (JWT or opaque) against a provider config.
/// Returns a ClaimsPrincipal on success, null on invalid/expired token.
/// </summary>
public interface ISsoTokenValidator
{
    Task<ClaimsPrincipal?> ValidateAsync(
        string token,
        ISsoProviderConfig config,
        CancellationToken ct = default);
}
