using Diva.Core.Models;
using Diva.Infrastructure.Data;
using Diva.Infrastructure.Data.Entities;
using Diva.TenantAdmin.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Diva.TenantAdmin.Tests;

/// <summary>
/// Integration tests for RulePackService.
/// Uses real SQLite (in-memory) per ADR-010 — no mocked DbContext.
/// </summary>
public class RulePackServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DivaDbContext _db;
    private readonly RulePackService _service;
    private readonly IMemoryCache _cache;

    public RulePackServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var opts = new DbContextOptionsBuilder<DivaDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new DivaDbContext(opts);
        _db.Database.EnsureCreated();

        _cache = new MemoryCache(new MemoryCacheOptions());
        _service = new RulePackService(
            new DirectDbFactory(opts),
            _cache,
            NullLogger<RulePackService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _cache.Dispose();
        _connection.Dispose();
    }

    // ── Tenant isolation ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetPacksAsync_ReturnsTenantScopedPacks_CrossTenantNotVisible()
    {
        _db.RulePacks.AddRange(
            new HookRulePackEntity { TenantId = 1, Name = "Pack-T1", Version = "1.0", Priority = 100, IsEnabled = true },
            new HookRulePackEntity { TenantId = 2, Name = "Pack-T2", Version = "1.0", Priority = 100, IsEnabled = true }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetPacksAsync(1, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("Pack-T1", result[0].Name);
    }

    // ── CRUD ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreatePackAsync_PersistsAndReturnsPack()
    {
        var dto = new CreateRulePackDto("PII Redaction", "Redacts SSNs and emails", null, 50, true);

        var created = await _service.CreatePackAsync(1, dto, CancellationToken.None);

        Assert.True(created.Id > 0);
        Assert.Equal("PII Redaction", created.Name);
        Assert.Equal(50, created.Priority);
        Assert.True(created.IsMandatory);
        Assert.True(created.IsEnabled);

        // Verify persisted
        var loaded = await _service.GetPackWithRulesAsync(1, created.Id, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal("PII Redaction", loaded!.Name);
    }

    [Fact]
    public async Task UpdatePackAsync_ModifiesExistingPack()
    {
        var pack = new HookRulePackEntity { TenantId = 1, Name = "Original", Version = "1.0", Priority = 100, IsEnabled = true };
        _db.RulePacks.Add(pack);
        await _db.SaveChangesAsync();

        var dto = new UpdateRulePackDto("Updated", "New desc", "2.0", 50, false, true, null, null, 1000);
        var updated = await _service.UpdatePackAsync(1, pack.Id, dto, CancellationToken.None);

        Assert.Equal("Updated", updated.Name);
        Assert.Equal("2.0", updated.Version);
        Assert.Equal(50, updated.Priority);
        Assert.False(updated.IsEnabled);
        Assert.True(updated.IsMandatory);
    }

    [Fact]
    public async Task UpdatePackAsync_ThrowsForWrongTenant()
    {
        var pack = new HookRulePackEntity { TenantId = 2, Name = "Other", Version = "1.0", Priority = 100, IsEnabled = true };
        _db.RulePacks.Add(pack);
        await _db.SaveChangesAsync();

        var dto = new UpdateRulePackDto("Hacked", null, "1.0", 100, true, false, null, null, 500);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.UpdatePackAsync(1, pack.Id, dto, CancellationToken.None));
    }

    [Fact]
    public async Task DeletePackAsync_RemovesPackAndRules()
    {
        var pack = new HookRulePackEntity { TenantId = 1, Name = "ToDelete", Version = "1.0", Priority = 100, IsEnabled = true };
        pack.Rules.Add(new HookRuleEntity { HookPoint = "OnInit", RuleType = "inject_prompt", Instruction = "test", OrderInPack = 1, IsEnabled = true });
        _db.RulePacks.Add(pack);
        await _db.SaveChangesAsync();

        await _service.DeletePackAsync(1, pack.Id, CancellationToken.None);

        Assert.Null(await _service.GetPackWithRulesAsync(1, pack.Id, CancellationToken.None));
        Assert.Empty(await _db.HookRules.ToListAsync());
    }

    [Fact]
    public async Task DeletePackAsync_ThrowsForMandatoryPack()
    {
        var pack = new HookRulePackEntity { TenantId = 1, Name = "Mandatory", Version = "1.0", Priority = 100, IsEnabled = true, IsMandatory = true };
        _db.RulePacks.Add(pack);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.DeletePackAsync(1, pack.Id, CancellationToken.None));
    }

    // ── Clone ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClonePackAsync_CopiesPackAndRulesWithNewTenant()
    {
        var source = new HookRulePackEntity
        {
            TenantId = 1, Name = "Source", Version = "2.0", Priority = 10,
            IsEnabled = true, IsMandatory = true, ActivationCondition = "revenue"
        };
        source.Rules.Add(new HookRuleEntity
        {
            HookPoint = "OnInit", RuleType = "inject_prompt",
            Instruction = "Always include revenue context", OrderInPack = 1, IsEnabled = true
        });
        source.Rules.Add(new HookRuleEntity
        {
            HookPoint = "OnBeforeResponse", RuleType = "regex_redact",
            Pattern = @"\b\d{3}-\d{2}-\d{4}\b", Replacement = "[SSN]", OrderInPack = 2, IsEnabled = true
        });
        _db.RulePacks.Add(source);
        await _db.SaveChangesAsync();

        var clone = await _service.ClonePackAsync(1, source.Id, "Cloned Pack", CancellationToken.None);

        Assert.NotEqual(source.Id, clone.Id);
        Assert.Equal("Cloned Pack", clone.Name);
        Assert.Equal("1.0", clone.Version); // Reset to 1.0
        Assert.False(clone.IsMandatory); // Clones are never mandatory
        Assert.Equal(source.Id, clone.ParentPackId); // Tracks inheritance

        var loaded = await _service.GetPackWithRulesAsync(1, clone.Id, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.Rules.Count);
    }

    [Fact]
    public async Task ClonePackAsync_CanCloneStarterPack()
    {
        // Starter packs have TenantId=0
        var starter = new HookRulePackEntity { TenantId = 0, Name = "Industry Standard", Version = "1.0", Priority = 100, IsEnabled = true };
        starter.Rules.Add(new HookRuleEntity
        {
            HookPoint = "OnBeforeResponse", RuleType = "append_text",
            Instruction = "Data sourced from industry benchmarks.", OrderInPack = 1, IsEnabled = true
        });
        _db.RulePacks.Add(starter);
        await _db.SaveChangesAsync();

        var clone = await _service.ClonePackAsync(5, starter.Id, "My Industry Pack", CancellationToken.None);

        Assert.Equal(5, clone.TenantId);
        Assert.Equal("My Industry Pack", clone.Name);
        Assert.Equal(starter.Id, clone.ParentPackId);
    }

    // ── Starter packs ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetStarterPacksAsync_ReturnsOnlyTenantZeroPacks()
    {
        _db.RulePacks.AddRange(
            new HookRulePackEntity { TenantId = 0, Name = "Starter-1", Version = "1.0", Priority = 100, IsEnabled = true },
            new HookRulePackEntity { TenantId = 0, Name = "Starter-2", Version = "1.0", Priority = 200, IsEnabled = true },
            new HookRulePackEntity { TenantId = 1, Name = "Tenant-Pack", Version = "1.0", Priority = 100, IsEnabled = true }
        );
        await _db.SaveChangesAsync();

        var starters = await _service.GetStarterPacksAsync(CancellationToken.None);

        Assert.Equal(2, starters.Count);
        Assert.All(starters, s => Assert.Equal(0, s.TenantId));
    }

    // ── Rule CRUD ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddRuleAsync_PersistsRuleInPack()
    {
        var pack = new HookRulePackEntity { TenantId = 1, Name = "Test", Version = "1.0", Priority = 100, IsEnabled = true };
        _db.RulePacks.Add(pack);
        await _db.SaveChangesAsync();

        var dto = new CreateHookRuleDto("OnBeforeResponse", "regex_redact", @"\b\d{9}\b", null, "[REDACTED]", null, 1, true);
        var rule = await _service.AddRuleAsync(1, pack.Id, dto, CancellationToken.None);

        Assert.True(rule.Id > 0);
        Assert.Equal("regex_redact", rule.RuleType);
        Assert.Equal(pack.Id, rule.PackId);
        Assert.True(rule.StopOnMatch);
    }

    [Fact]
    public async Task AddRuleAsync_ThrowsForWrongTenantPack()
    {
        var pack = new HookRulePackEntity { TenantId = 2, Name = "Other", Version = "1.0", Priority = 100, IsEnabled = true };
        _db.RulePacks.Add(pack);
        await _db.SaveChangesAsync();

        var dto = new CreateHookRuleDto("OnInit", "inject_prompt", null, "Injected!", null, null);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.AddRuleAsync(1, pack.Id, dto, CancellationToken.None));
    }

    [Fact]
    public async Task AddRuleAsync_ThrowsForInvalidHookPointRuleTypeCombo()
    {
        var pack = new HookRulePackEntity { TenantId = 1, Name = "Test", Version = "1.0", Priority = 100, IsEnabled = true };
        _db.RulePacks.Add(pack);
        await _db.SaveChangesAsync();

        var dto = new CreateHookRuleDto("OnToolFilter", "append_text", null, "x", null, null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.AddRuleAsync(1, pack.Id, dto, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateRuleAsync_ModifiesExistingRule()
    {
        var pack = new HookRulePackEntity { TenantId = 1, Name = "Test", Version = "1.0", Priority = 100, IsEnabled = true };
        pack.Rules.Add(new HookRuleEntity
        {
            HookPoint = "OnInit", RuleType = "inject_prompt", Instruction = "Original",
            OrderInPack = 1, IsEnabled = true, StopOnMatch = false
        });
        _db.RulePacks.Add(pack);
        await _db.SaveChangesAsync();
        var ruleId = pack.Rules.First().Id;

        var dto = new UpdateHookRuleDto("OnBeforeResponse", "block_pattern", @"secret\w+", null, null, null, 5, true, true, 200);
        var updated = await _service.UpdateRuleAsync(1, pack.Id, ruleId, dto, CancellationToken.None);

        Assert.Equal("OnBeforeResponse", updated.HookPoint);
        Assert.Equal("block_pattern", updated.RuleType);
        Assert.Equal(5, updated.OrderInPack);
        Assert.True(updated.StopOnMatch);
    }

    [Fact]
    public async Task UpdateRuleAsync_ThrowsForInvalidHookPointRuleTypeCombo()
    {
        var pack = new HookRulePackEntity { TenantId = 1, Name = "Test", Version = "1.0", Priority = 100, IsEnabled = true };
        pack.Rules.Add(new HookRuleEntity
        {
            HookPoint = "OnInit", RuleType = "inject_prompt", Instruction = "Original",
            OrderInPack = 1, IsEnabled = true, StopOnMatch = false
        });
        _db.RulePacks.Add(pack);
        await _db.SaveChangesAsync();
        var ruleId = pack.Rules.First().Id;

        var dto = new UpdateHookRuleDto("OnError", "append_text", null, "x", null, null, 1, true, false, 100);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.UpdateRuleAsync(1, pack.Id, ruleId, dto, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteRuleAsync_RemovesRule()
    {
        var pack = new HookRulePackEntity { TenantId = 1, Name = "Test", Version = "1.0", Priority = 100, IsEnabled = true };
        pack.Rules.Add(new HookRuleEntity { HookPoint = "OnInit", RuleType = "inject_prompt", Instruction = "x", OrderInPack = 1, IsEnabled = true });
        _db.RulePacks.Add(pack);
        await _db.SaveChangesAsync();
        var ruleId = pack.Rules.First().Id;

        await _service.DeleteRuleAsync(1, pack.Id, ruleId, CancellationToken.None);

        var loaded = await _service.GetPackWithRulesAsync(1, pack.Id, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Empty(loaded!.Rules);
    }

    [Fact]
    public async Task ReorderRulesAsync_UpdatesOrderInPack()
    {
        var pack = new HookRulePackEntity { TenantId = 1, Name = "Test", Version = "1.0", Priority = 100, IsEnabled = true };
        pack.Rules.Add(new HookRuleEntity { HookPoint = "OnInit", RuleType = "inject_prompt", Instruction = "A", OrderInPack = 1, IsEnabled = true });
        pack.Rules.Add(new HookRuleEntity { HookPoint = "OnInit", RuleType = "inject_prompt", Instruction = "B", OrderInPack = 2, IsEnabled = true });
        pack.Rules.Add(new HookRuleEntity { HookPoint = "OnInit", RuleType = "inject_prompt", Instruction = "C", OrderInPack = 3, IsEnabled = true });
        _db.RulePacks.Add(pack);
        await _db.SaveChangesAsync();

        var ids = pack.Rules.OrderByDescending(r => r.OrderInPack).Select(r => r.Id).ToArray(); // C, B, A

        await _service.ReorderRulesAsync(1, pack.Id, ids, CancellationToken.None);

        var loaded = await _service.GetPackWithRulesAsync(1, pack.Id, CancellationToken.None);
        var ordered = loaded!.Rules.OrderBy(r => r.OrderInPack).ToList();
        Assert.Equal("C", ordered[0].Instruction);
        Assert.Equal("B", ordered[1].Instruction);
        Assert.Equal("A", ordered[2].Instruction);
    }

    // ── Cache invalidation ────────────────────────────────────────────────────

    [Fact]
    public async Task CreatePackAsync_InvalidatesCache()
    {
        // Populate cache
        await _service.GetPacksAsync(1, CancellationToken.None);

        // Create a new pack
        await _service.CreatePackAsync(1, new CreateRulePackDto("New", null, null), CancellationToken.None);

        // Result should include new pack (cache was invalidated)
        var result = await _service.GetPacksAsync(1, CancellationToken.None);
        Assert.Single(result);
        Assert.Equal("New", result[0].Name);
    }
}
