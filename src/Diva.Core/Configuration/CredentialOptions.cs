namespace Diva.Core.Configuration;

/// <summary>
/// Configuration for credential encryption.
/// MasterKey must be a base64-encoded 256-bit (32-byte) key.
/// </summary>
public sealed class CredentialOptions
{
    public const string SectionName = "Credentials";

    /// <summary>Base64-encoded AES-256 master key (32 bytes). Required in production.</summary>
    public string MasterKey { get; set; } = string.Empty;
}
