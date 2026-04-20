using System.Security.Cryptography;
using System.Text;
using Diva.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Diva.Infrastructure.Auth;

/// <summary>
/// AES-256-GCM encryption for credential secrets stored in the database.
/// Layout: [12-byte nonce][ciphertext][16-byte tag] → base64.
/// When no MasterKey is configured, generates an ephemeral key for development.
/// Ephemeral keys are lost on restart — credentials encrypted with them become unrecoverable.
/// </summary>
public sealed class AesCredentialEncryptor : ICredentialEncryptor
{
    private const int NonceSize = 12; // AES-GCM standard nonce
    private const int TagSize = 16;   // AES-GCM standard tag
    private readonly byte[] _key;

    public AesCredentialEncryptor(IOptions<CredentialOptions> options, ILogger<AesCredentialEncryptor> logger)
    {
        var masterKey = options.Value.MasterKey;
        if (string.IsNullOrEmpty(masterKey))
        {
            _key = RandomNumberGenerator.GetBytes(32);
            logger.LogWarning(
                "Credentials:MasterKey is not configured — using an ephemeral random key. " +
                "Encrypted credentials will be LOST on restart. " +
                "Set a stable base64-encoded 32-byte key for production.");
        }
        else
        {
            _key = Convert.FromBase64String(masterKey);
            if (_key.Length != 32)
                throw new InvalidOperationException(
                    $"Credentials:MasterKey must be exactly 32 bytes (256 bits). Got {_key.Length} bytes.");
        }
    }

    public string Encrypt(string plainText)
    {
        ArgumentException.ThrowIfNullOrEmpty(plainText);

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        // Layout: nonce + ciphertext + tag
        var result = new byte[NonceSize + cipherBytes.Length + TagSize];
        nonce.CopyTo(result, 0);
        cipherBytes.CopyTo(result, NonceSize);
        tag.CopyTo(result, NonceSize + cipherBytes.Length);

        return Convert.ToBase64String(result);
    }

    public string Decrypt(string cipherText)
    {
        ArgumentException.ThrowIfNullOrEmpty(cipherText);

        var data = Convert.FromBase64String(cipherText);
        if (data.Length < NonceSize + TagSize)
            throw new CryptographicException("Encrypted data is too short.");

        var nonce = data[..NonceSize];
        var tag = data[^TagSize..];
        var cipherBytes = data[NonceSize..^TagSize];
        var plainBytes = new byte[cipherBytes.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipherBytes, tag, plainBytes);

        return Encoding.UTF8.GetString(plainBytes);
    }
}
