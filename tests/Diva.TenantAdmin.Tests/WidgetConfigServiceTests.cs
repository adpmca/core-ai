using Diva.Core.Models;
using Diva.Core.Models.Widgets;
using Diva.Infrastructure.Data;
using Diva.TenantAdmin.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Diva.TenantAdmin.Tests;

/// <summary>
/// Integration tests for WidgetConfigService.
/// Uses real SQLite (in-memory) — no mocked DbContext.
/// </summary>
public class WidgetConfigServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DivaDbContext _db;
    private readonly WidgetConfigService _service;

    public WidgetConfigServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var opts = new DbContextOptionsBuilder<DivaDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new DivaDbContext(opts);
        _db.Database.EnsureCreated();

        _service = new WidgetConfigService(new DirectDbFactory(opts));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // ── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_StoresWidget_ReturnsDto()
    {
        var request = new CreateWidgetRequest(
            AgentId: "agent-1",
            Name: "Test Widget",
            AllowedOrigins: ["https://example.com"],
            SsoConfigId: null,
            AllowAnonymous: true,
            WelcomeMessage: "Hello!",
            PlaceholderText: "Ask me anything",
            Theme: null,
            RespectSystemTheme: true,
            ShowBranding: true,
            ExpiresAt: null);

        var dto = await _service.CreateAsync(tenantId: 1, request);

        Assert.NotNull(dto);
        Assert.NotEmpty(dto.Id);
        Assert.Equal("Test Widget", dto.Name);
        Assert.Equal("agent-1", dto.AgentId);
        Assert.Equal(1, dto.TenantId);
        Assert.Equal(["https://example.com"], dto.AllowedOrigins);
        Assert.True(dto.AllowAnonymous);
        Assert.Equal("Hello!", dto.WelcomeMessage);
        Assert.True(dto.IsActive);
        // Null theme → Light defaults
        Assert.Equal(WidgetTheme.Light.Background, dto.Theme.Background);
    }

    [Fact]
    public async Task CreateAsync_WithCustomTheme_RoundTripsColors()
    {
        var customTheme = new WidgetTheme
        {
            Background = "#ff0000",
            Primary = "#00ff00",
            Preset = "custom",
        };
        var request = new CreateWidgetRequest(
            AgentId: "agent-2",
            Name: "Themed Widget",
            AllowedOrigins: ["https://app.com"],
            Theme: customTheme);

        var dto = await _service.CreateAsync(tenantId: 1, request);

        Assert.Equal("#ff0000", dto.Theme.Background);
        Assert.Equal("#00ff00", dto.Theme.Primary);
        Assert.Equal("custom", dto.Theme.Preset);
    }

    // ── Tenant isolation ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetForTenantAsync_ReturnsTenantScopedWidgets_CrossTenantNotVisible()
    {
        var r1 = new CreateWidgetRequest("agent-1", "T1 Widget", ["https://t1.com"]);
        var r2 = new CreateWidgetRequest("agent-2", "T2 Widget", ["https://t2.com"]);

        await _service.CreateAsync(tenantId: 1, r1);
        await _service.CreateAsync(tenantId: 2, r2);

        var t1 = await _service.GetForTenantAsync(tenantId: 1);
        var t2 = await _service.GetForTenantAsync(tenantId: 2);

        Assert.Single(t1);
        Assert.Equal("T1 Widget", t1[0].Name);
        Assert.Single(t2);
        Assert.Equal("T2 Widget", t2[0].Name);
    }

    // ── GetByIdAsync bypasses tenant filter ────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_BypassesTenantFilter_ReturnsAnyTenantWidget()
    {
        var dto = await _service.CreateAsync(tenantId: 5, new CreateWidgetRequest("agent-x", "Hidden Widget", ["https://x.com"]));

        // GetByIdAsync uses tenantId=0 context (no filter)
        var found = await _service.GetByIdAsync(dto.Id);

        Assert.NotNull(found);
        Assert.Equal(dto.Id, found!.Id);
        Assert.Equal(5, found.TenantId);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        var found = await _service.GetByIdAsync("nonexistent-id");
        Assert.Null(found);
    }

    // ── Update ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_ChangesFields_ReturnsUpdatedDto()
    {
        var dto = await _service.CreateAsync(tenantId: 1, new CreateWidgetRequest("agent-1", "Old Name", ["https://old.com"]));

        var updated = await _service.UpdateAsync(tenantId: 1, dto.Id, new CreateWidgetRequest(
            AgentId: "agent-2",
            Name: "New Name",
            AllowedOrigins: ["https://new.com", "https://other.com"],
            AllowAnonymous: false));

        Assert.Equal("New Name", updated.Name);
        Assert.Equal("agent-2", updated.AgentId);
        Assert.Equal(2, updated.AllowedOrigins.Length);
        Assert.False(updated.AllowAnonymous);
    }

    [Fact]
    public async Task UpdateAsync_ThrowsKeyNotFoundException_WhenNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _service.UpdateAsync(tenantId: 1, "bad-id", new CreateWidgetRequest("a", "N", ["https://x.com"])));
    }

    // ── Delete ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_RemovesWidget()
    {
        var dto = await _service.CreateAsync(tenantId: 1, new CreateWidgetRequest("agent-1", "To Delete", ["https://x.com"]));

        await _service.DeleteAsync(tenantId: 1, dto.Id);

        var list = await _service.GetForTenantAsync(tenantId: 1);
        Assert.Empty(list);
    }

    [Fact]
    public async Task DeleteAsync_IsIdempotent_WhenNotFound()
    {
        // Should not throw
        await _service.DeleteAsync(tenantId: 1, "nonexistent");
    }

    // ── Theme defaults ────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_NullTheme_DefaultsToLightPreset()
    {
        var dto = await _service.CreateAsync(tenantId: 1, new CreateWidgetRequest("agent-1", "W", ["https://x.com"], Theme: null));
        Assert.Equal(WidgetTheme.Light.Primary, dto.Theme.Primary);
        Assert.Equal(WidgetTheme.Light.HeaderBg, dto.Theme.HeaderBg);
    }
}
