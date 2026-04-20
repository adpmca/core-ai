# Testing Strategy

> **Rule:** Never mock the database in integration tests — use real SQLite.
> This was decided after a prior incident where mocked tests passed but a prod migration failed (see ADR-010).

---

## Test Projects

```
tests/
├── Diva.Agents.Tests/          → Agent execution, supervisor pipeline, registry
├── Diva.TenantAdmin.Tests/     → Business rules service, prompt builder, template store
└── Diva.Tools.Tests/           → MCP tool servers, SQL validation, header propagation
```

One test project per `src/` project. No test project for `Diva.Host` — use the integration test helpers in each project instead.

---

## Test Types

| Type | Scope | Database | External APIs |
|------|-------|----------|---------------|
| Unit | Single class/method | None (pure logic) | Mocked |
| Integration | Multiple layers | Real SQLite | Mocked |
| End-to-end | Full HTTP stack | Real SQLite | Real or mocked |

---

## Setup: Real SQLite in Tests

### Option A — In-Memory SQLite (fast, no file cleanup)

```csharp
public class DivaDbContextFactory
{
    public static DivaDbContext CreateInMemory()
    {
        var options = new DbContextOptionsBuilder<DivaDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        var db = new DivaDbContext(options);
        db.Database.OpenConnection();     // keep connection open — in-memory db dies with connection
        db.Database.EnsureCreated();
        return db;
    }
}
```

### Option B — Temp File SQLite (survives across connections, needed for multi-DbContext tests)

```csharp
public class TempSqliteFixture : IDisposable
{
    public string DbPath { get; } = Path.GetTempFileName() + ".db";

    public DivaDbContext CreateContext(int tenantId = 1)
    {
        var options = new DbContextOptionsBuilder<DivaDbContext>()
            .UseSqlite($"DataSource={DbPath}")
            .Options;

        var db = new DivaDbContext(options, tenantId);
        db.Database.EnsureCreated();
        return db;
    }

    public void Dispose() => File.Delete(DbPath);
}
```

### WebApplicationFactory (integration tests hitting full HTTP stack)

```csharp
public class DivaWebFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Replace real DbContext with SQLite in-memory
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<DivaDbContext>));
            if (descriptor != null) services.Remove(descriptor);

            services.AddDbContext<DivaDbContext>(options =>
                options.UseSqlite("DataSource=:memory:"));

            // Replace real JWT validation with test stub
            services.AddSingleton<IOAuthTokenValidator, TestOAuthTokenValidator>();
        });
    }
}
```

---

## Test Patterns

### Testing business rule service (integration)

```csharp
public class TenantBusinessRulesServiceTests : IDisposable
{
    private readonly TempSqliteFixture _fixture = new();

    [Fact]
    public async Task GetRulesAsync_ReturnsTenantScopedRules()
    {
        // Arrange
        using var db = _fixture.CreateContext(tenantId: 1);
        db.BusinessRules.Add(new TenantBusinessRuleEntity
        {
            TenantId = 1, AgentType = "Analytics",
            RuleCategory = "reporting", RuleKey = "revenue_exclusions",
            PromptInjection = "Exclude REFUNDS", IsActive = true
        });
        db.BusinessRules.Add(new TenantBusinessRuleEntity
        {
            TenantId = 2, AgentType = "Analytics",   // different tenant
            RuleCategory = "reporting", RuleKey = "other_rule",
            PromptInjection = "Should not appear", IsActive = true
        });
        await db.SaveChangesAsync();

        var service = new TenantBusinessRulesService(db, new MemoryCache(new MemoryCacheOptions()));

        // Act
        var rules = await service.GetRulesAsync(tenantId: 1, agentType: "Analytics", CancellationToken.None);

        // Assert
        Assert.Single(rules);
        Assert.Equal("revenue_exclusions", rules[0].RuleKey);
    }

    public void Dispose() => _fixture.Dispose();
}
```

### Testing tenant isolation (EF query filter)

```csharp
[Fact]
public async Task QueryFilter_BlocksCrossTenantAccess()
{
    using var db = _fixture.CreateContext(tenantId: 1);
    // Seed data for tenant 2 by bypassing query filter
    db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
    using var adminDb = _fixture.CreateContext(tenantId: 0);  // tenantId=0 bypasses filter
    adminDb.BusinessRules.Add(new TenantBusinessRuleEntity { TenantId = 2, /* ... */ });
    await adminDb.SaveChangesAsync();

    // Tenant 1 context should not see tenant 2 data
    var count = await db.BusinessRules.CountAsync();
    Assert.Equal(0, count);
}
```

### Testing prompt builder (unit — mock only DB dependency)

```csharp
[Fact]
public async Task BuildPromptAsync_InjectsBusinessRules()
{
    // Arrange
    var mockRulesService = Substitute.For<ITenantBusinessRulesService>();
    mockRulesService.GetRulesAsync(1, "Analytics", Arg.Any<CancellationToken>())
        .Returns([new TenantBusinessRule { PromptInjection = "Exclude REFUNDS" }]);

    var store = new PromptTemplateStore("prompts/");
    var builder = new TenantAwarePromptBuilder(mockRulesService, store, NullLogger<TenantAwarePromptBuilder>.Instance);

    var tenant = new TenantContext { TenantId = 1 };

    // Act
    var prompt = await builder.BuildPromptAsync(tenant, "Analytics", "react-agent", [], CancellationToken.None);

    // Assert
    Assert.Contains("Exclude REFUNDS", prompt);
}
```

### Testing agent routing (unit)

```csharp
[Fact]
public async Task FindBestMatchAsync_SelectsHighestCapabilityScore()
{
    var registry = new DynamicAgentRegistry(CreateInMemoryDb(), services);
    registry.Register(new AnalyticsAgent(/* ... */));
    registry.Register(new TeeTimeAgent(/* ... */));

    var match = await registry.FindBestMatchAsync(
        requiredCapabilities: ["analytics", "revenue"],
        tenantId: 1,
        CancellationToken.None);

    Assert.IsType<AnalyticsAgent>(match);
}
```

---

## Per-Phase Test Coverage

| Phase | Key Tests |
|-------|-----------|
| 3 — OAuth | Middleware returns 401 on missing token; valid JWT builds correct TenantContext |
| 4 — Database | Query filters block cross-tenant reads; RLS `sp_set_session_context` called on SaveChanges |
| 5 — MCP Tools | `ValidateSqlSecurity` blocks `DROP`, `DELETE`; headers propagated on every tool call |
| 6 — Tenant Admin | Rules cached after first load; cache invalidated after update; prompt injection order correct |
| 7 — Sessions | Multi-turn history retrieved ordered by time; expired sessions not returned |
| 8 — Agents | AnalyticsAgent routes "revenue query"; DynamicAgent loaded from DB definition |
| 11 — Rule Learning | `LlmRuleExtractor` handles non-JSON response gracefully; SessionOnly rules not persisted |
| 12 — Admin Portal | `BusinessRules.tsx` renders table rows; mutation calls API and refetches |

---

## Running Tests

```bash
# All tests
dotnet test

# Single project
dotnet test tests/Diva.TenantAdmin.Tests

# With output
dotnet test --logger "console;verbosity=detailed"

# Specific test by name
dotnet test --filter "FullyQualifiedName~TenantBusinessRulesServiceTests"

# Coverage report (requires coverlet)
dotnet test --collect:"XPlat Code Coverage"
```

---

## What IS Acceptable to Mock

- `IOAuthTokenValidator` — avoid real HTTP to JWKS endpoint in tests
- `LiteLLMClient` / `ILlmClientFactory` — avoid real LLM API calls; return canned responses
- `IDistributedCache` — for session rule unit tests (not integration)
- `IHubContext<AgentStreamHub>` — for SignalR push assertions

## What is NOT Acceptable to Mock

- `DivaDbContext` — use real SQLite (per ADR-010)
- `ITenantBusinessRulesService` in DB integration tests — test the real implementation
- `TenantContextMiddleware` in HTTP integration tests — build a real `TenantContext` via `TestOAuthTokenValidator`
