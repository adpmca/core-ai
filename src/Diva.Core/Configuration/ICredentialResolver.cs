namespace Diva.Core.Configuration;

/// <summary>
/// Resolved credential ready for injection into HTTP headers.
/// </summary>
public sealed record ResolvedCredential(
    string ApiKey,
    string AuthScheme,
    string? CustomHeaderName);

/// <summary>
/// Resolves tenant-scoped credential references (by name) into decrypted API keys.
/// </summary>
public interface ICredentialResolver
{
    /// <summary>
    /// Looks up a named credential for the given tenant, decrypts the key,
    /// and validates it is active and not expired.
    /// Returns null if the credential does not exist or is invalid.
    /// </summary>
    Task<ResolvedCredential?> ResolveAsync(int tenantId, string credentialName, CancellationToken ct);
}
