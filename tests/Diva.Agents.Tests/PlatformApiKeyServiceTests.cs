using Diva.Core.Configuration;
using Diva.Infrastructure.Auth;
using Diva.Infrastructure.Data;
using Diva.Infrastructure.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Diva.Agents.Tests;

public class PlatformApiKeyServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<DivaDbContext> _dbOptions;
    private readonly PlatformApiKeyService _service;

    private const int TenantId = 42;

    public PlatformApiKeyServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _dbOptions = new DbContextOptionsBuilder<DivaDbContext>()
            .UseSqlite(_connection)
            .Options;

        var db = new DivaDbContext(_dbOptions);
        db.Database.EnsureCreated();

        var factory = new TestDatabaseProviderFactory(_dbOptions);
        var branding = Options.Create(new AppBrandingOptions());
        _service = new PlatformApiKeyService(factory, NullLogger<PlatformApiKeyService>.Instance, branding);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task CreateAsync_ReturnsKeyWithPrefix()
    {
        var result = await _service.CreateAsync(TenantId, "admin-user",
            new CreateApiKeyRequest("Test Key", "invoke", null, null), CancellationToken.None);

        Assert.StartsWith("diva_", result.RawKey);
        Assert.True(result.RawKey.Length > 20);
        Assert.Equal("Test Key", result.Name);
    }

    [Fact]
    public async Task ValidateAsync_ValidKey_ReturnsCorrectTenant()
    {
        var created = await _service.CreateAsync(TenantId, "admin-user",
            new CreateApiKeyRequest("Invoke Key", "invoke", null, null), CancellationToken.None);

        var validated = await _service.ValidateAsync(created.RawKey, CancellationToken.None);

        Assert.NotNull(validated);
        Assert.Equal(TenantId, validated.TenantId);
        Assert.Equal("invoke", validated.Scope);
        Assert.Equal("Invoke Key", validated.Name);
    }

    [Fact]
    public async Task ValidateAsync_InvalidKey_ReturnsNull()
    {
        var result = await _service.ValidateAsync("diva_nonexistent", CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAsync_RevokedKey_ReturnsNull()
    {
        var created = await _service.CreateAsync(TenantId, "admin-user",
            new CreateApiKeyRequest("Revoke Me", "admin", null, null), CancellationToken.None);

        await _service.RevokeAsync(TenantId, created.Id, CancellationToken.None);

        var validated = await _service.ValidateAsync(created.RawKey, CancellationToken.None);
        Assert.Null(validated);
    }

    [Fact]
    public async Task ValidateAsync_ExpiredKey_ReturnsNull()
    {
        // Create key then manually set it as expired
        var created = await _service.CreateAsync(TenantId, "admin-user",
            new CreateApiKeyRequest("Expiring", "invoke", null, null), CancellationToken.None);

        using (var db = new DivaDbContext(_dbOptions, 0))
        {
            var entity = await db.PlatformApiKeys.FirstAsync(k => k.Id == created.Id);
            entity.ExpiresAt = DateTime.UtcNow.AddHours(-1);
            await db.SaveChangesAsync();
        }

        var validated = await _service.ValidateAsync(created.RawKey, CancellationToken.None);
        Assert.Null(validated);
    }

    [Fact]
    public async Task ListAsync_ReturnsKeysForTenant()
    {
        await _service.CreateAsync(TenantId, "admin-user",
            new CreateApiKeyRequest("Key A", "invoke", null, null), CancellationToken.None);
        await _service.CreateAsync(TenantId, "admin-user",
            new CreateApiKeyRequest("Key B", "admin", null, null), CancellationToken.None);
        await _service.CreateAsync(999, "other-user",
            new CreateApiKeyRequest("Key Other", "invoke", null, null), CancellationToken.None);

        var keys = await _service.ListAsync(TenantId, CancellationToken.None);

        Assert.Equal(2, keys.Count);
    }

    [Fact]
    public async Task ListAsync_NeverReturnsRawKey()
    {
        var created = await _service.CreateAsync(TenantId, "admin-user",
            new CreateApiKeyRequest("Secret Key", "invoke", null, null), CancellationToken.None);

        var keys = await _service.ListAsync(TenantId, CancellationToken.None);

        // PlatformApiKeyInfo contains KeyPrefix (first 12 chars) but never the full key
        var key = Assert.Single(keys);
        Assert.True(key.KeyPrefix.Length <= 12);
    }

    [Fact]
    public async Task RotateAsync_RevokesOldAndReturnsNewKey()
    {
        var original = await _service.CreateAsync(TenantId, "admin-user",
            new CreateApiKeyRequest("Rotate Me", "invoke", null, null), CancellationToken.None);

        var rotated = await _service.RotateAsync(TenantId, original.Id, "admin-user", CancellationToken.None);

        Assert.NotEqual(original.RawKey, rotated.RawKey);
        Assert.StartsWith("diva_", rotated.RawKey);

        // Old key should no longer validate
        var oldValid = await _service.ValidateAsync(original.RawKey, CancellationToken.None);
        Assert.Null(oldValid);

        // New key should validate
        var newValid = await _service.ValidateAsync(rotated.RawKey, CancellationToken.None);
        Assert.NotNull(newValid);
    }

    [Fact]
    public async Task CreateAsync_WithAllowedAgents_PersistsAgentIds()
    {
        var created = await _service.CreateAsync(TenantId, "admin-user",
            new CreateApiKeyRequest("Agent-Scoped", "invoke", ["agent-1", "agent-2"], null),
            CancellationToken.None);

        var validated = await _service.ValidateAsync(created.RawKey, CancellationToken.None);

        Assert.NotNull(validated);
        Assert.NotNull(validated.AllowedAgentIds);
        Assert.Contains("agent-1", validated.AllowedAgentIds);
        Assert.Contains("agent-2", validated.AllowedAgentIds);
    }

    [Fact]
    public async Task CreateAsync_InvalidScope_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateAsync(TenantId, "admin-user",
                new CreateApiKeyRequest("Bad Scope", "superadmin", null, null),
                CancellationToken.None));
    }

    [Fact]
    public async Task RawKey_NeverStoredInDatabase()
    {
        var created = await _service.CreateAsync(TenantId, "admin-user",
            new CreateApiKeyRequest("Hash Only", "invoke", null, null), CancellationToken.None);

        using var db = new DivaDbContext(_dbOptions, 0);
        var entity = await db.PlatformApiKeys.FirstAsync(k => k.Id == created.Id);

        // Only hash and prefix are stored
        Assert.DoesNotContain(created.RawKey, entity.KeyHash);
        Assert.True(entity.KeyPrefix.Length <= 12);
        Assert.True(entity.KeyHash.Length == 64); // SHA-256 hex = 64 chars
    }
}
