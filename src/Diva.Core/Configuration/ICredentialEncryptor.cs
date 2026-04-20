namespace Diva.Core.Configuration;

/// <summary>
/// Encrypts and decrypts credential secrets (API keys) for storage in the database.
/// </summary>
public interface ICredentialEncryptor
{
    /// <summary>Encrypts a plaintext secret. Returns a base64 string containing IV + ciphertext + tag.</summary>
    string Encrypt(string plainText);

    /// <summary>Decrypts a base64 string produced by <see cref="Encrypt"/>. Returns the original plaintext.</summary>
    string Decrypt(string cipherText);
}
