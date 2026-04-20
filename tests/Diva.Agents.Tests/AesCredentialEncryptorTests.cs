using Diva.Core.Configuration;
using Diva.Infrastructure.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Diva.Agents.Tests;

public class AesCredentialEncryptorTests
{
    private static ICredentialEncryptor CreateEncryptor(string? base64Key = null)
    {
        // Generate a valid 32-byte key if none provided
        base64Key ??= Convert.ToBase64String(new byte[32].Select((_, i) => (byte)i).ToArray());
        var options = Options.Create(new CredentialOptions { MasterKey = base64Key });
        return new AesCredentialEncryptor(options, NullLogger<AesCredentialEncryptor>.Instance);
    }

    [Fact]
    public void Encrypt_Decrypt_Roundtrip()
    {
        var encryptor = CreateEncryptor();
        var secret = "sk-test-api-key-12345";

        var encrypted = encryptor.Encrypt(secret);
        var decrypted = encryptor.Decrypt(encrypted);

        Assert.Equal(secret, decrypted);
        Assert.NotEqual(secret, encrypted);
    }

    [Fact]
    public void Encrypt_DifferentCalls_ProduceDifferentCiphertext()
    {
        var encryptor = CreateEncryptor();
        var secret = "same-secret";

        var a = encryptor.Encrypt(secret);
        var b = encryptor.Encrypt(secret);

        // AES-GCM uses random nonce, so ciphertext differs even for same plaintext
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Decrypt_TamperedCiphertext_Throws()
    {
        var encryptor = CreateEncryptor();
        var encrypted = encryptor.Encrypt("test");

        // Tamper with middleish byte
        var bytes = Convert.FromBase64String(encrypted);
        bytes[15] ^= 0xFF;
        var tampered = Convert.ToBase64String(bytes);

        Assert.ThrowsAny<Exception>(() => encryptor.Decrypt(tampered));
    }

    [Fact]
    public void Encrypt_EmptyString_Throws()
    {
        var encryptor = CreateEncryptor();
        Assert.ThrowsAny<ArgumentException>(() => encryptor.Encrypt(""));
    }

    [Fact]
    public void Constructor_InvalidKeyLength_Throws()
    {
        var shortKey = Convert.ToBase64String(new byte[16]);
        Assert.ThrowsAny<Exception>(() => CreateEncryptor(shortKey));
    }

    [Fact]
    public void Constructor_EmptyKey_UsesEphemeralKey()
    {
        // Empty key no longer throws — generates an ephemeral random key with a warning
        var encryptor = CreateEncryptor("");
        var encrypted = encryptor.Encrypt("test-secret");
        var decrypted = encryptor.Decrypt(encrypted);
        Assert.Equal("test-secret", decrypted);
    }

    [Fact]
    public void Encrypt_LongSecret_Roundtrips()
    {
        var encryptor = CreateEncryptor();
        var longSecret = new string('x', 10000);

        var encrypted = encryptor.Encrypt(longSecret);
        var decrypted = encryptor.Decrypt(encrypted);

        Assert.Equal(longSecret, decrypted);
    }

    [Fact]
    public void Encrypt_UnicodeSecret_Roundtrips()
    {
        var encryptor = CreateEncryptor();
        var secret = "密钥-🔑-ключ-clé";

        var encrypted = encryptor.Encrypt(secret);
        var decrypted = encryptor.Decrypt(encrypted);

        Assert.Equal(secret, decrypted);
    }
}
