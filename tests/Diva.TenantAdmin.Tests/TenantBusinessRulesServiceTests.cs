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
/// Integration tests for TenantBusinessRulesService.
/// Uses real SQLite (in-memory) per ADR-010 — no mocked DbContext.
/// </summary>
public class TenantBusinessRulesServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DivaDbContext _db;
    private readonly TenantBusinessRulesService _service;
    private readonly IMemoryCache _cache;

    public TenantBusinessRulesServiceTests()
    {
        // Keep the connection alive so multiple DbContext instances share the same in-memory DB.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var opts = new DbContextOptionsBuilder<DivaDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new DivaDbContext(opts);
        _db.Database.EnsureCreated();

        _cache   = new MemoryCache(new MemoryCacheOptions());
        _service = new TenantBusinessRulesService(
            new DirectDbFactory(opts),
            _cache,
            NullLogger<TenantBusinessRulesService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _cache.Dispose();
        _connection.Dispose();
    }

    // ── Tenant isolation ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetRulesAsync_ReturnsTenantScopedRules_CrossTenantNotVisible()
    {
        _db.BusinessRules.AddRange(
            new TenantBusinessRuleEntity { TenantId = 1, AgentType = "Analytics", RuleKey = "rule-t1", PromptInjection = "Tenant1 rule", IsActive = true },
            new TenantBusinessRuleEntity { TenantId = 2, AgentType = "Analytics", RuleKey = "rule-t2", PromptInjection = "Tenant2 rule", IsActive = true }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetRulesAsync(1, "Analytics", CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("rule-t1", result[0].RuleKey);
    }

    [Fact]
    public async Task GetRulesAsync_InactiveRules_NotReturned()
    {
        _db.BusinessRules.Add(new TenantBusinessRuleEntity
        {
            TenantId = 1, AgentType = "Analytics", RuleKey = "inactive", PromptInjection = "Old rule", IsActive = false
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetRulesAsync(1, "Analytics", CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetRulesAsync_WildcardAgentType_IncludedForAllAgents()
    {
        _db.BusinessRules.Add(new TenantBusinessRuleEntity
        {
            TenantId = 1, AgentType = "*", RuleKey = "global-rule", PromptInjection = "Global", IsActive = true
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetRulesAsync(1, "Analytics", CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("global-rule", result[0].RuleKey);
    }

    // ── Caching ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetRulesAsync_SecondCall_ReturnsCachedResult()
    {
        _db.BusinessRules.Add(new TenantBusinessRuleEntity
        {
            TenantId = 1, AgentType = "Analytics", RuleKey = "r1", PromptInjection = "Original", IsActive = true
        });
        await _db.SaveChangesAsync();

        var first = await _service.GetRulesAsync(1, "Analytics", CancellationToken.None);

        // Add another rule directly (bypassing service cache) — second call should NOT see it
        _db.BusinessRules.Add(new TenantBusinessRuleEntity
        {
            TenantId = 1, AgentType = "Analytics", RuleKey = "r2", PromptInjection = "New", IsActive = true
        });
        await _db.SaveChangesAsync();

        var second = await _service.GetRulesAsync(1, "Analytics", CancellationToken.None);

        Assert.Equal(first.Count, second.Count);   // cache hit — still 1 result
    }

    [Fact]
    public async Task InvalidateCache_ForcesDbRefetch()
    {
        _db.BusinessRules.Add(new TenantBusinessRuleEntity
        {
            TenantId = 1, AgentType = "Analytics", RuleKey = "original", PromptInjection = "Original", IsActive = true
        });
        await _db.SaveChangesAsync();

        var first = await _service.GetRulesAsync(1, "Analytics", CancellationToken.None);
        Assert.Single(first);

        // Add second rule and invalidate
        _db.BusinessRules.Add(new TenantBusinessRuleEntity
        {
            TenantId = 1, AgentType = "Analytics", RuleKey = "new-rule", PromptInjection = "New", IsActive = true
        });
        await _db.SaveChangesAsync();
        _service.InvalidateCache(1, "Analytics");

        var second = await _service.GetRulesAsync(1, "Analytics", CancellationToken.None);
        Assert.Equal(2, second.Count);
    }

    // ── CRUD ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateRuleAsync_PersistsRule()
    {
        var dto = new CreateRuleDto(
            AgentType:       "Analytics",
            RuleCategory:    "reporting",
            RuleKey:         "test-key",
            PromptInjection: "Test injection"
        );

        await _service.CreateRuleAsync(1, dto, CancellationToken.None);

        var rules = await _service.GetRulesAsync(1, "Analytics", CancellationToken.None);
        Assert.Single(rules);
        Assert.Equal("test-key", rules[0].RuleKey);
    }

    // ── Hook fields ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateRuleAsync_PersistsHookFields()
    {
        var dto = new CreateRuleDto(
            AgentType:       "*",
            RuleCategory:    "security",
            RuleKey:         "redact-key",
            PromptInjection: null,
            HookPoint:       "OnBeforeResponse",
            HookRuleType:    "regex_redact",
            Pattern:         @"\d{4}-\d{4}",
            OrderInPack:     5,
            StopOnMatch:     true
        );

        var created = await _service.CreateRuleAsync(1, dto, CancellationToken.None);

        Assert.Equal("OnBeforeResponse", created.HookPoint);
        Assert.Equal("regex_redact",    created.HookRuleType);
        Assert.Equal(@"\d{4}-\d{4}",   created.Pattern);
        Assert.Equal(5,                  created.OrderInPack);
        Assert.True(created.StopOnMatch);
    }

    [Fact]
    public async Task CreateRuleAsync_InvalidHookRuleTypeCombination_Throws()
    {
        var dto = new CreateRuleDto(
            AgentType:       "*",
            RuleCategory:    "bad",
            RuleKey:         "bad-combo",
            PromptInjection: null,
            HookPoint:       "OnBeforeIteration",
            HookRuleType:    "block_pattern"   // not allowed for OnBeforeIteration
        );

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CreateRuleAsync(1, dto, CancellationToken.None));
    }

    // ── Pack assignment ───────────────────────────────────────────────────────

    [Fact]
    public async Task AssignRuleToPackAsync_AssignsAndInvalidatesPackCache()
    {
        // Arrange: create a pack and a business rule
        var pack = new HookRulePackEntity
        {
            TenantId = 1, Name = "Pack A", IsEnabled = true
        };
        _db.RulePacks.Add(pack);
        await _db.SaveChangesAsync();

        var rule = new TenantBusinessRuleEntity
        {
            TenantId = 1, AgentType = "*", RuleKey = "to-assign", PromptInjection = "hi", IsActive = true
        };
        _db.BusinessRules.Add(rule);
        await _db.SaveChangesAsync();

        // Act
        await _service.AssignRuleToPackAsync(1, rule.Id, pack.Id, CancellationToken.None);

        // Assert: entity has RulePackId — reload to bypass EF change tracker
        _db.Entry(rule).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
        var updated = await _db.BusinessRules.FindAsync(rule.Id);
        Assert.Equal(pack.Id, updated!.RulePackId);
    }

    [Fact]
    public async Task AssignRuleToPackAsync_InvalidPackId_Throws()
    {
        var rule = new TenantBusinessRuleEntity
        {
            TenantId = 1, AgentType = "*", RuleKey = "r1", PromptInjection = "x", IsActive = true
        };
        _db.BusinessRules.Add(rule);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.AssignRuleToPackAsync(1, rule.Id, 9999, CancellationToken.None));
    }

    [Fact]
    public async Task AssignRuleToPackAsync_UnassignByPassingNull_ClearsRulePackId()
    {
        var pack = new HookRulePackEntity
        {
            TenantId = 1, Name = "Pack B", IsEnabled = true
        };
        _db.RulePacks.Add(pack);
        var rule = new TenantBusinessRuleEntity
        {
            TenantId = 1, AgentType = "*", RuleKey = "r2", PromptInjection = "y", IsActive = true
        };
        _db.BusinessRules.Add(rule);
        await _db.SaveChangesAsync();
        rule.RulePackId = pack.Id;
        await _db.SaveChangesAsync();

        await _service.AssignRuleToPackAsync(1, rule.Id, null, CancellationToken.None);

        _db.Entry(rule).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
        var updated = await _db.BusinessRules.FindAsync(rule.Id);
        Assert.Null(updated!.RulePackId);
    }

    [Fact]
    public async Task GetRulesForPackAsync_ReturnsSortedActiveLinkedRules()
    {
        var pack = new HookRulePackEntity
        {
            TenantId = 1, Name = "Pack C", IsEnabled = true
        };
        _db.RulePacks.Add(pack);
        await _db.SaveChangesAsync();

        _db.BusinessRules.AddRange(
            new TenantBusinessRuleEntity { TenantId = 1, AgentType = "*", RuleKey = "r-b", RulePackId = pack.Id, OrderInPack = 2, PromptInjection = "B", IsActive = true },
            new TenantBusinessRuleEntity { TenantId = 1, AgentType = "*", RuleKey = "r-a", RulePackId = pack.Id, OrderInPack = 1, PromptInjection = "A", IsActive = true },
            new TenantBusinessRuleEntity { TenantId = 1, AgentType = "*", RuleKey = "r-inactive", RulePackId = pack.Id, OrderInPack = 0, PromptInjection = "Z", IsActive = false }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetRulesForPackAsync(1, pack.Id, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("r-a", result[0].RuleKey);   // OrderInPack=1 first
        Assert.Equal("r-b", result[1].RuleKey);
    }
}

/// <summary>
/// Test-only factory — creates a new DivaDbContext instance each call using the shared connection,
/// so that services using `using var db = factory.CreateDbContext()` don't dispose the shared DB.
/// </summary>
internal sealed class DirectDbFactory : IDatabaseProviderFactory
{
    private readonly DbContextOptions<DivaDbContext> _options;
    public DirectDbFactory(DbContextOptions<DivaDbContext> options) => _options = options;
    public DivaDbContext CreateDbContext(TenantContext? tenant = null)
        => new(_options, tenant?.TenantId ?? 0);
    public Task ApplyMigrationsAsync() => Task.CompletedTask;
}
