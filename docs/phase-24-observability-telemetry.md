# Phase 24: Comprehensive Observability & Telemetry

> **Status:** `[ ]` Not Started
> **Depends on:** [phase-04-database.md](phase-04-database.md), [phase-09-llm-client.md](phase-09-llm-client.md), [phase-10-api-host.md](phase-10-api-host.md), [phase-12-admin-portal.md](phase-12-admin-portal.md)
> **Blocks:** Nothing (fully additive)
> **Projects:** `Diva.Core`, `Diva.Infrastructure`, `Diva.Host`, `admin-portal`
> **Tests:** `tests/Diva.Agents.Tests/Observability/` — ~15 new tests

---

## Goal

Add all four observability pillars — **distributed traces, metrics, structured logs, and LLM-level proxy logging** — using the official [OTel GenAI semantic conventions](https://opentelemetry.io/docs/specs/semconv/gen-ai/) as the wire standard. Add a **Settings > Observability** admin page for operator configuration. Integrate Helicone on-premise as an opt-in LLM proxy layer. Add Grafana + Tempo as the visualization stack.

Everything in this phase is **additive** — no existing behaviour changes.

---

## SOLID Design Constraints

Every new class in this phase enforces these principles:

| Principle | Rule |
|-----------|------|
| **S** | Each class has one reason to change. `DivaActivitySources` only holds ActivitySource instances. `ModelCostTable` only computes cost estimates. `HeliconeHttpHandler` only rewrites HTTP requests — zero LLM protocol knowledge. |
| **O** | Provider strategies and `AnthropicAgentRunner` depend on injected interfaces. Adding Datadog or Braintrust = new `ILlmSpanEmitter`/`ILlmMetricEmitter` implementation. Zero changes to callers. |
| **L** | `NullLlmSpanEmitter`, `NullLlmMetricEmitter`, `NullAgentEventLogger` are fully substitutable no-ops. All existing tests that construct strategies/runner directly continue to compile and pass unchanged. |
| **I** | Three focused interfaces instead of one mega-interface: `ILlmSpanEmitter` (OTel spans), `ILlmMetricEmitter` (metrics), `IAgentEventLogger` (structured Serilog). Provider strategies use only the first two; `AnthropicAgentRunner` uses all three. |
| **D** | `AnthropicAgentRunner` depends on `ILlmSpanEmitter`/`ILlmMetricEmitter`/`IAgentEventLogger`, not `DivaActivitySources` or `DivaMeter`. `ObservabilityConfigService` depends on `IObservabilityConfigRepository`, not `DivaDbContext` directly. |

---

## Known Gaps Addressed vs. Original Plan

The following issues were identified during gap analysis and are resolved in this document:

| # | Gap | Resolution |
|---|-----|-----------|
| 1 | `ObservabilityConfigService` was "scoped" — `DelegatingHandler` requires transient dependency | Register as **singleton** with internal `IServiceScopeFactory` (same as `TenantBusinessRulesService`) |
| 2 | `ObservabilityConfigEntity : ITenantEntity` with `TenantId=0` excluded by EF filter when real tenant active | Remove `ITenantEntity` — model like `PlatformLlmConfigEntity`, no query filter, single platform-level row |
| 3 | `AnthropicProviderStrategy` has 9+ existing `new` callsites in tests — constructor additions would break them | New emitter params are **optional** (`= null`); internal default to `Null*` implementations |
| 4 | `OpenAiProvider` creates `OpenAIClient` per-call with no injected `HttpClient` — can't attach handler | Register `OpenAiProvider` with named `HttpClient` (`"OpenAiProvider"`); inject `HttpClient` in constructor |
| 5 | `McpClientCache` owns cache hit/miss — metrics should go there, not `McpConnectionManager` | Add `ILlmMetricEmitter` to `McpClientCache`, not `McpConnectionManager` |
| 6 | `PrometheusUrl` not in config entity | Add `PrometheusUrl` column to `ObservabilityConfigEntity` |
| 7 | `docker/otel-collector-config.yaml` doesn't exist | Create from scratch (not "update") |
| 8 | NuGet packages not listed | `OpenTelemetry.Testing.Metrics` in test project only |
| 9 | `ModelSwitchCoordinator` also creates strategies — must forward emitters | Add optional emitter params to `ModelSwitchCoordinator`; it forwards them into created strategies |
| 10 | `HeliconeHttpHandler` DI pattern incomplete | `AddTransient<HeliconeHttpHandler>()` required before `AddHttpMessageHandler<HeliconeHttpHandler>()` |

---

## Phase 24a — Observability Abstractions (`Diva.Core`)

All new telemetry code depends on **interfaces**, not concretions. This enforces DIP and makes every concrete component independently testable.

### New folder: `src/Diva.Core/Observability/`

#### `ILlmSpanEmitter.cs`

```csharp
namespace Diva.Core.Observability;

/// <summary>
/// Emits OTel distributed trace spans for LLM calls.
/// Implemented by OtelLlmSpanEmitter; NullLlmSpanEmitter for tests/disabled.
/// </summary>
public interface ILlmSpanEmitter
{
    /// <summary>
    /// Starts a gen_ai.chat span. Dispose the returned scope to end the span.
    /// </summary>
    IDisposable StartLlmCall(LlmCallContext context);

    /// <summary>Records result attributes on the active span before it is disposed.</summary>
    void RecordLlmResult(LlmCallResult result);

    /// <summary>Starts a gen_ai.agent.invoke root span. Dispose to end it.</summary>
    IDisposable StartAgentInvoke(string agentName, int tenantId, string sessionId);

    /// <summary>Starts a gen_ai.agent.iteration child span. Dispose to end it.</summary>
    IDisposable StartIteration(int iterationNumber);

    /// <summary>Starts a gen_ai.tool.call span. Dispose to end it.</summary>
    IDisposable StartToolCall(string toolName, string callId);

    /// <summary>Starts an mcp.connect span. Dispose to end it.</summary>
    IDisposable StartMcpConnect(string serverName, string transport);

    /// <summary>Starts a gen_ai.agent.delegate span for A2A calls. Dispose to end it.</summary>
    IDisposable StartA2ADelegate(string targetAgentId, int targetTenantId);

    /// <summary>Marks the current span as failed with the given exception.</summary>
    void RecordException(Exception ex);
}
```

#### `ILlmMetricEmitter.cs`

```csharp
namespace Diva.Core.Observability;

public interface ILlmMetricEmitter
{
    void RecordTokenUsage(string model, string provider, string tokenType, long count, int tenantId);
    void RecordOperationDuration(string model, string provider, double seconds, int tenantId);
    void RecordTimeToFirstToken(string model, string provider, double milliseconds);
    void RecordToolCall(string toolName, string agentName, bool success, double durationMs);
    void RecordCostEstimate(string model, string provider, long inputTokens, long outputTokens, long cacheWriteTokens, long cacheReadTokens, int tenantId);
    void RecordAgentIterations(string agentName, int count);
    void RecordContinuationWindow(string agentName);
    void RecordMcpCacheHit(string serverName);
    void RecordMcpCacheMiss(string serverName);
    void RecordContextCompaction(string strategy);
    void RecordVerificationOutcome(string outcome, string mode);
}
```

#### `IAgentEventLogger.cs`

```csharp
namespace Diva.Core.Observability;

public interface IAgentEventLogger
{
    /// <summary>
    /// Pushes TenantId, AgentName, SessionId, TraceId, SpanId into Serilog LogContext.
    /// Dispose the returned scope to pop properties.
    /// </summary>
    IDisposable BeginAgentScope(string agentName, int tenantId, string sessionId);

    /// <summary>Updates the IterationNumber and WindowNumber properties in the current scope.</summary>
    void LogIterationStart(int iteration, int window);

    void LogToolCall(string toolName, string callId, object? input);
    void LogToolResult(string toolName, string callId, string output, double durationMs, bool success);
    void LogLlmCallSummary(string model, string provider, long inputTokens, long outputTokens, double durationMs, bool cacheHit);
}
```

#### `ILlmProxyHandler.cs`

```csharp
namespace Diva.Core.Observability;

/// <summary>
/// Marker interface for DelegatingHandler LLM proxy implementations.
/// Callers never reference the concrete handler — they depend only on this marker.
/// Enables registration and pipeline construction without coupling to Helicone directly.
/// </summary>
public interface ILlmProxyHandler { }
```

#### `IHeliconeContextProvider.cs`

```csharp
namespace Diva.Core.Observability;

public interface IHeliconeContextProvider
{
    HeliconeRequestContext? GetCurrentContext();
    void SetContext(HeliconeRequestContext? context);
}
```

#### `IObservabilityConfigService.cs`

```csharp
namespace Diva.Core.Observability;

public interface IObservabilityConfigService
{
    Task<ObservabilityConfig> GetConfigAsync(CancellationToken ct = default);
    Task SaveConfigAsync(ObservabilityConfig config, CancellationToken ct = default);
    void InvalidateCache();
}
```

#### Value objects / DTOs

**`LlmCallContext.cs`**
```csharp
namespace Diva.Core.Observability;

public sealed record LlmCallContext(
    string Model,
    string Provider,
    int TenantId,
    string SessionId,
    string AgentName,
    int MaxTokens);
```

**`LlmCallResult.cs`**
```csharp
namespace Diva.Core.Observability;

public sealed record LlmCallResult(
    string ResponseModel,
    string FinishReason,
    long InputTokens,
    long OutputTokens,
    long CacheCreationTokens,
    long CacheReadTokens,
    double DurationMs,
    double TimeToFirstTokenMs);
```

**`ObservabilityConfig.cs`** (domain model — NOT entity)
```csharp
namespace Diva.Core.Observability;

public sealed record ObservabilityConfig
{
    public bool HeliconeEnabled { get; init; }
    public string HeliconeBaseUrl { get; init; } = "http://helicone:8787";
    public string? HeliconeApiKey { get; init; }   // null = unchanged; set = new key
    public bool LogPromptContent { get; init; }    // off by default — privacy
    public bool CostTrackingEnabled { get; init; } = true;
    public int MetricsRetentionDays { get; init; } = 30;
    public string? PrometheusUrl { get; init; }    // e.g. http://prometheus:9090
}
```

**`HeliconeRequestContext.cs`**
```csharp
namespace Diva.Core.Observability;

public sealed record HeliconeRequestContext(
    string SessionId,
    string AgentName,
    string AgentId,
    int TenantId,
    string? UserId);
```

---

## Phase 24b — Concrete OTel Implementations (`Diva.Infrastructure`)

### New folder: `src/Diva.Infrastructure/Observability/`

#### `DivaActivitySources.cs` (Single Responsibility: owns ActivitySource singletons)

```csharp
using System.Diagnostics;

namespace Diva.Infrastructure.Observability;

/// <summary>
/// Static registry for all Diva OTel ActivitySource instances.
/// ActivitySource creation is expensive — these are singletons.
/// Registered once in Program.cs via .AddSource(...).
/// </summary>
public static class DivaActivitySources
{
    public static readonly ActivitySource LLM   = new("Diva.LLM",   "1.0");
    public static readonly ActivitySource Agent = new("Diva.Agent", "1.0");
    public static readonly ActivitySource Tool  = new("Diva.Tool",  "1.0");
    public static readonly ActivitySource Mcp   = new("Diva.MCP",   "1.0");
    public static readonly ActivitySource A2A   = new("Diva.A2A",   "1.0");

    public static readonly string[] AllSourceNames =
        ["Diva.LLM", "Diva.Agent", "Diva.Tool", "Diva.MCP", "Diva.A2A"];
}
```

#### `OtelLlmSpanEmitter.cs` (implements `ILlmSpanEmitter`)

Key implementation details:
- `StartLlmCall()` → starts `gen_ai.chat` activity on `DivaActivitySources.LLM`
  - Tags: `gen_ai.system` = provider.ToLower(), `gen_ai.request.model`, `gen_ai.request.max_tokens`
  - `ActivityKind.Client` per OTel GenAI spec
- `RecordLlmResult()` → sets `gen_ai.response.model`, `gen_ai.response.finish_reason`, all four token type attributes per [OTel Anthropic conventions](https://opentelemetry.io/docs/specs/semconv/gen-ai/anthropic/)
  - `gen_ai.usage.input_tokens`, `gen_ai.usage.output_tokens`
  - `gen_ai.usage.cache_creation_input_tokens`, `gen_ai.usage.cache_read_input_tokens`
  - Adds `gen_ai.choice` event only when `LogPromptContent` enabled (read from ambient config)
- `StartAgentInvoke()` → `gen_ai.agent.invoke` on `DivaActivitySources.Agent`
  - Tags: `gen_ai.agent.name`, `diva.tenant_id`, `diva.session_id`
  - Injects OTel Baggage: `diva.tenant_id`, `diva.session_id`, `diva.agent_name`
- `StartIteration()` → `gen_ai.agent.iteration` child on `DivaActivitySources.Agent`
- `StartToolCall()` → `gen_ai.tool.call` on `DivaActivitySources.Tool` per [OTel tool call spec](https://opentelemetry.io/docs/specs/semconv/gen-ai/gen-ai-spans/)
- `StartMcpConnect()` → `mcp.connect` on `DivaActivitySources.Mcp` per [OTel MCP spec](https://opentelemetry.io/docs/specs/semconv/gen-ai/mcp/)
- `StartA2ADelegate()` → `gen_ai.agent.delegate` on `DivaActivitySources.A2A`
  - W3C TraceContext already propagated automatically by `AddHttpClientInstrumentation()`
- `RecordException()` → `activity?.SetStatus(ActivityStatusCode.Error, ex.Message)` + `activity?.RecordException(ex)`

`IDisposable` scope pattern — each `Start*` method returns an `ActivityScope` record (inner type):
```csharp
private sealed record ActivityScope(Activity? Activity) : IDisposable
{
    public void Dispose() => Activity?.Stop();
}
```

#### `DivaMeter.cs` (Single Responsibility: owns Meter and instrument declarations)

```csharp
using System.Diagnostics.Metrics;

namespace Diva.Infrastructure.Observability;

/// <summary>
/// All Diva custom metrics instruments. Singleton — created once, registered with OTel in Program.cs.
/// </summary>
public sealed class DivaMeter : IDisposable
{
    private readonly Meter _meter = new("Diva", "1.0");

    // ── Standard OTel GenAI metrics ────────────────────────────────────────────
    /// <summary>gen_ai.client.token.usage — histogram per OTel GenAI metrics spec.</summary>
    public readonly Histogram<long> TokenUsage;

    /// <summary>gen_ai.client.operation.duration — histogram (seconds) per OTel GenAI metrics spec.</summary>
    public readonly Histogram<double> OperationDuration;

    // ── Custom Diva metrics ────────────────────────────────────────────────────
    public readonly Histogram<double> TimeToFirstToken;        // diva.llm.time_to_first_token (ms)
    public readonly Histogram<double> ToolExecutionDuration;   // diva.tool.execution.duration (ms)
    public readonly Counter<long>     ToolCallsTotal;          // diva.tool.calls.total
    public readonly Histogram<int>    AgentIterations;         // diva.agent.iterations (count per invocation)
    public readonly Counter<long>     ContinuationWindows;     // diva.agent.continuation_windows
    public readonly Counter<double>   EstimatedCostUsd;        // diva.llm.estimated_cost_usd
    public readonly Counter<long>     McpCacheHits;            // diva.mcp.cache.hits
    public readonly Counter<long>     McpCacheMisses;          // diva.mcp.cache.misses
    public readonly Counter<long>     ContextCompactions;      // diva.context.compactions.total
    public readonly Counter<long>     VerificationOutcomes;    // diva.verification.outcome

    public DivaMeter()
    {
        TokenUsage            = _meter.CreateHistogram<long>("gen_ai.client.token.usage", "{token}", "Measures number of input and output tokens used");
        OperationDuration     = _meter.CreateHistogram<double>("gen_ai.client.operation.duration", "s", "Duration of LLM call");
        TimeToFirstToken      = _meter.CreateHistogram<double>("diva.llm.time_to_first_token", "ms", "Time from request start to first streamed token");
        ToolExecutionDuration = _meter.CreateHistogram<double>("diva.tool.execution.duration", "ms", "Duration of a single MCP tool call");
        ToolCallsTotal        = _meter.CreateCounter<long>("diva.tool.calls.total", "{call}", "Total tool calls by name and outcome");
        AgentIterations       = _meter.CreateHistogram<int>("diva.agent.iterations", "{iteration}", "Number of ReAct iterations per agent invocation");
        ContinuationWindows   = _meter.CreateCounter<long>("diva.agent.continuation_windows", "{window}", "Context window continuation events");
        EstimatedCostUsd      = _meter.CreateCounter<double>("diva.llm.estimated_cost_usd", "USD", "Estimated LLM cost based on token counts and model pricing");
        McpCacheHits          = _meter.CreateCounter<long>("diva.mcp.cache.hits", "{hit}");
        McpCacheMisses        = _meter.CreateCounter<long>("diva.mcp.cache.misses", "{miss}");
        ContextCompactions    = _meter.CreateCounter<long>("diva.context.compactions.total", "{compaction}");
        VerificationOutcomes  = _meter.CreateCounter<long>("diva.verification.outcome", "{outcome}");
    }

    public void Dispose() => _meter.Dispose();
}
```

#### `OtelLlmMetricEmitter.cs` (implements `ILlmMetricEmitter`)

Delegates all calls to `DivaMeter` instruments. Constructor receives `DivaMeter`. Uses `ModelCostTable` for cost estimation.

Tags follow OTel semantic conventions: `gen_ai.system`, `gen_ai.request.model`, `gen_ai.token.type`, `diva.tenant_id`.

#### `ModelCostTable.cs` (Single Responsibility: pure cost lookup, no dependencies)

```csharp
namespace Diva.Infrastructure.Observability;

/// <summary>
/// Static cost-per-million-tokens lookup table.
/// Returns 0 for unknown models (never throws) — cost tracking is best-effort.
/// Update periodically as provider pricing changes.
/// </summary>
public static class ModelCostTable
{
    // (model_id, token_type) → USD per million tokens
    private static readonly Dictionary<(string, string), decimal> _rates = new()
    {
        // Claude Sonnet 4
        (("claude-sonnet-4-20250514", "input"),         3.00m),
        (("claude-sonnet-4-20250514", "output"),       15.00m),
        (("claude-sonnet-4-20250514", "cache_write"),   3.75m),
        (("claude-sonnet-4-20250514", "cache_read"),    0.30m),
        // Claude Haiku 3.5
        (("claude-haiku-3-5-20241022", "input"),        0.80m),
        (("claude-haiku-3-5-20241022", "output"),       4.00m),
        (("claude-haiku-3-5-20241022", "cache_write"),  1.00m),
        (("claude-haiku-3-5-20241022", "cache_read"),   0.08m),
        // GPT-4o
        (("gpt-4o",                    "input"),         2.50m),
        (("gpt-4o",                    "output"),       10.00m),
    };

    public static decimal GetCostPerMillionTokens(string model, string tokenType)
        => _rates.TryGetValue((model.ToLowerInvariant(), tokenType), out var rate) ? rate : 0m;

    public static decimal EstimateCost(string model, long inputTokens, long outputTokens,
        long cacheWriteTokens = 0, long cacheReadTokens = 0)
    {
        var m = model.ToLowerInvariant();
        return inputTokens      / 1_000_000m * GetCostPerMillionTokens(m, "input")
             + outputTokens     / 1_000_000m * GetCostPerMillionTokens(m, "output")
             + cacheWriteTokens / 1_000_000m * GetCostPerMillionTokens(m, "cache_write")
             + cacheReadTokens  / 1_000_000m * GetCostPerMillionTokens(m, "cache_read");
    }
}
```

#### `SerilogAgentEventLogger.cs` (implements `IAgentEventLogger`)

Key implementation details:
- `BeginAgentScope()` → calls `LogContext.PushProperty(...)` for `TenantId`, `AgentName`, `SessionId`; reads `Activity.Current?.TraceId.ToString()` + `Activity.Current?.SpanId.ToString()` to push `TraceId`/`SpanId` — this is the Serilog↔OTel cross-correlation bridge
- Returns an `IDisposable` that disposes all pushed properties in reverse order
- `LogLlmCallSummary()` uses destructured `{@LlmCall}` object for Seq indexing

#### `NullLlmSpanEmitter.cs` / `NullLlmMetricEmitter.cs` / `NullAgentEventLogger.cs` (Liskov-safe no-ops)

```csharp
namespace Diva.Infrastructure.Observability;

/// <summary>No-op ILlmSpanEmitter — used in tests and when tracing is disabled.</summary>
public sealed class NullLlmSpanEmitter : ILlmSpanEmitter
{
    public static readonly NullLlmSpanEmitter Instance = new();
    private static readonly IDisposable _noop = new NoopDisposable();

    public IDisposable StartLlmCall(LlmCallContext ctx) => _noop;
    public void RecordLlmResult(LlmCallResult result) { }
    public IDisposable StartAgentInvoke(string a, int t, string s) => _noop;
    public IDisposable StartIteration(int i) => _noop;
    public IDisposable StartToolCall(string n, string id) => _noop;
    public IDisposable StartMcpConnect(string n, string t) => _noop;
    public IDisposable StartA2ADelegate(string a, int t) => _noop;
    public void RecordException(Exception ex) { }

    private sealed class NoopDisposable : IDisposable { public void Dispose() { } }
}
```

---

## Phase 24c — DB-Backed Observability Config

> Pattern: follows `PlatformLlmConfigEntity` exactly — platform-level only, no `ITenantEntity`, no EF query filter.

### `src/Diva.Infrastructure/Data/Entities/ObservabilityConfigEntity.cs` (new)

```csharp
namespace Diva.Infrastructure.Data.Entities;

/// <summary>
/// Platform-wide observability configuration. Single row (Id=1).
/// NOT ITenantEntity — there is no tenant isolation for platform observability settings.
/// Same pattern as PlatformLlmConfigEntity.
/// </summary>
public class ObservabilityConfigEntity
{
    public int Id { get; set; } = 1;   // Always 1 — singleton row

    public bool HeliconeEnabled { get; set; }
    public string HeliconeBaseUrl { get; set; } = "http://helicone:8787";
    /// <summary>AES-256-GCM encrypted Helicone API key. Null if not configured.</summary>
    public string? HeliconeApiKeyEncrypted { get; set; }

    /// <summary>Off by default — privacy. Opt-in via UI.</summary>
    public bool LogPromptContent { get; set; }
    public bool CostTrackingEnabled { get; set; } = true;
    public int MetricsRetentionDays { get; set; } = 30;

    /// <summary>Prometheus HTTP API URL for the metrics summary endpoint. Optional.</summary>
    public string? PrometheusUrl { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
```

### `src/Diva.Infrastructure/Data/DivaDbContext.cs` (modified)

Add DbSet and index in `OnModelCreating`. No query filter (see gap analysis):

```csharp
// ── Observability Config (Phase 24) ──────────────────────────────────────
public DbSet<ObservabilityConfigEntity> ObservabilityConfigs => Set<ObservabilityConfigEntity>();
```

In `OnModelCreating`:
```csharp
modelBuilder.Entity<ObservabilityConfigEntity>()
    .HasKey(e => e.Id);
// No query filter — platform-level config, no tenant isolation needed
```

### EF Core Migration: `20260430000000_AddObservabilityConfig`

**`src/Diva.Infrastructure/Data/Migrations/20260430000000_AddObservabilityConfig.cs`**

```csharp
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Diva.Infrastructure.Data.Migrations
{
    public partial class AddObservabilityConfig : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ObservabilityConfigs",
                columns: table => new
                {
                    Id                       = table.Column<int>(type: "INTEGER", nullable: false),
                    HeliconeEnabled          = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    HeliconeBaseUrl          = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "http://helicone:8787"),
                    HeliconeApiKeyEncrypted  = table.Column<string>(type: "TEXT", nullable: true),
                    LogPromptContent         = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    CostTrackingEnabled      = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    MetricsRetentionDays     = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 30),
                    PrometheusUrl            = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt                = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ObservabilityConfigs", x => x.Id);
                });

            // Seed the singleton platform row
            migrationBuilder.InsertData(
                table: "ObservabilityConfigs",
                columns: new[] { "Id", "HeliconeEnabled", "HeliconeBaseUrl", "HeliconeApiKeyEncrypted",
                                 "LogPromptContent", "CostTrackingEnabled", "MetricsRetentionDays",
                                 "PrometheusUrl", "UpdatedAt" },
                values: new object[] { 1, false, "http://helicone:8787", null,
                                       false, true, 30, null, DateTime.UtcNow });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ObservabilityConfigs");
        }
    }
}
```

**`src/Diva.Infrastructure/Data/Migrations/20260430000000_AddObservabilityConfig.Designer.cs`**

Required — without this file EF cannot discover the migration. Must include:
- `[DbContext(typeof(DivaDbContext))]`
- `[Migration("20260430000000_AddObservabilityConfig")]`
- Full `BuildTargetModel` snapshot = copy the `DivaDbContextModelSnapshot.BuildModel` body and append the `ObservabilityConfigs` entity block

**`src/Diva.Infrastructure/Data/Migrations/DivaDbContextModelSnapshot.cs`** (modified)

Append `ObservabilityConfigs` entity block in `BuildModel` before the closing `#pragma warning restore`.

### `IObservabilityConfigRepository.cs` + `ObservabilityConfigRepository.cs`

**`src/Diva.Infrastructure/Observability/IObservabilityConfigRepository.cs`**

```csharp
namespace Diva.Infrastructure.Observability;

public interface IObservabilityConfigRepository
{
    Task<ObservabilityConfigEntity?> GetAsync(CancellationToken ct);
    Task UpsertAsync(ObservabilityConfigEntity entity, CancellationToken ct);
}
```

**`src/Diva.Infrastructure/Observability/ObservabilityConfigRepository.cs`**

Uses `IServiceScopeFactory` internally (same pattern as `DynamicAgentRegistry`) to avoid scoped-in-singleton issues.

```csharp
public sealed class ObservabilityConfigRepository : IObservabilityConfigRepository
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ObservabilityConfigRepository(IServiceScopeFactory scopeFactory)
        => _scopeFactory = scopeFactory;

    public async Task<ObservabilityConfigEntity?> GetAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IDatabaseProviderFactory>()
                      .CreateDbContext(null);   // null = TenantId=0 (bypasses all query filters)
        return await db.ObservabilityConfigs.FirstOrDefaultAsync(e => e.Id == 1, ct);
    }

    public async Task UpsertAsync(ObservabilityConfigEntity entity, CancellationToken ct)
    {
        entity.Id = 1; // always singleton
        entity.UpdatedAt = DateTime.UtcNow;
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IDatabaseProviderFactory>()
                      .CreateDbContext(null);
        var existing = await db.ObservabilityConfigs.FirstOrDefaultAsync(e => e.Id == 1, ct);
        if (existing is null)
            db.ObservabilityConfigs.Add(entity);
        else
        {
            db.Entry(existing).CurrentValues.SetValues(entity);
        }
        await db.SaveChangesAsync(ct);
    }
}
```

### `ObservabilityConfigService.cs` (implements `IObservabilityConfigService`)

> **SINGLETON** registration — NOT scoped. Required because `HeliconeHttpHandler` (transient) depends on it.
> Uses `IMemoryCache` with 5-minute TTL.

```csharp
public sealed class ObservabilityConfigService : IObservabilityConfigService
{
    private readonly IObservabilityConfigRepository _repo;
    private readonly ICredentialEncryptor _encryptor;
    private readonly IMemoryCache _cache;
    private const string CacheKey = "ObservabilityConfig";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public ObservabilityConfigService(
        IObservabilityConfigRepository repo,
        ICredentialEncryptor encryptor,
        IMemoryCache cache)
    {
        _repo      = repo;
        _encryptor = encryptor;
        _cache     = cache;
    }

    public async Task<ObservabilityConfig> GetConfigAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue(CacheKey, out ObservabilityConfig? cached) && cached is not null)
            return cached;

        var entity = await _repo.GetAsync(ct);
        var config = entity is null ? new ObservabilityConfig() : MapToConfig(entity);
        _cache.Set(CacheKey, config, CacheTtl);
        return config;
    }

    public async Task SaveConfigAsync(ObservabilityConfig config, CancellationToken ct = default)
    {
        var existing = await _repo.GetAsync(ct);
        var entity = existing ?? new ObservabilityConfigEntity();

        entity.HeliconeEnabled      = config.HeliconeEnabled;
        entity.HeliconeBaseUrl      = config.HeliconeBaseUrl;
        entity.LogPromptContent     = config.LogPromptContent;
        entity.CostTrackingEnabled  = config.CostTrackingEnabled;
        entity.MetricsRetentionDays = config.MetricsRetentionDays;
        entity.PrometheusUrl        = config.PrometheusUrl;

        // Only update encrypted key if a new value was provided
        if (config.HeliconeApiKey is not null)
            entity.HeliconeApiKeyEncrypted = _encryptor.Encrypt(config.HeliconeApiKey);

        await _repo.UpsertAsync(entity, ct);
        InvalidateCache();
    }

    public void InvalidateCache() => _cache.Remove(CacheKey);

    private static ObservabilityConfig MapToConfig(ObservabilityConfigEntity e) =>
        new()
        {
            HeliconeEnabled      = e.HeliconeEnabled,
            HeliconeBaseUrl      = e.HeliconeBaseUrl,
            HeliconeApiKey       = null, // never expose encrypted key; callers use HeliconeApiKeySet
            LogPromptContent     = e.LogPromptContent,
            CostTrackingEnabled  = e.CostTrackingEnabled,
            MetricsRetentionDays = e.MetricsRetentionDays,
            PrometheusUrl        = e.PrometheusUrl,
        };
}
```

---

## Phase 24d — Helicone HTTP Handler

### `src/Diva.Infrastructure/Observability/HeliconeContextProvider.cs`

```csharp
namespace Diva.Infrastructure.Observability;

/// <summary>
/// Carries the current Helicone request context across async calls using AsyncLocal.
/// AnthropicAgentRunner sets this before each LLM call so the DelegatingHandler can read it.
/// Registered as singleton (AsyncLocal is per-execution-context, not per-scope).
/// </summary>
public sealed class HeliconeContextProvider : IHeliconeContextProvider
{
    private static readonly AsyncLocal<HeliconeRequestContext?> _current = new();

    public HeliconeRequestContext? GetCurrentContext() => _current.Value;
    public void SetContext(HeliconeRequestContext? context) => _current.Value = context;
}
```

### `src/Diva.Infrastructure/Observability/HeliconeHttpHandler.cs` (implements `ILlmProxyHandler`)

```csharp
namespace Diva.Infrastructure.Observability;

/// <summary>
/// DelegatingHandler that rewrites LLM HTTP requests through a self-hosted Helicone proxy.
/// Zero coupling to Anthropic or OpenAI protocols — only rewrites base URL and injects headers.
/// Registered as Transient in DI (DelegatingHandlers must be transient or per-request).
/// IObservabilityConfigService is singleton so no captive dependency issue.
/// </summary>
public sealed class HeliconeHttpHandler : DelegatingHandler, ILlmProxyHandler
{
    private readonly IObservabilityConfigService _configService;
    private readonly IHeliconeContextProvider _contextProvider;

    public HeliconeHttpHandler(
        IObservabilityConfigService configService,
        IHeliconeContextProvider contextProvider)
    {
        _configService   = configService;
        _contextProvider = contextProvider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var config = await _configService.GetConfigAsync(ct);

        if (!config.HeliconeEnabled || string.IsNullOrEmpty(config.HeliconeBaseUrl))
            return await base.SendAsync(request, ct);

        // Rewrite base URL — preserve path and query
        var originalUri = request.RequestUri!;
        var proxyBase   = new Uri(config.HeliconeBaseUrl.TrimEnd('/'));
        request.RequestUri = new Uri(proxyBase, originalUri.PathAndQuery);

        // Auth header
        if (config.HeliconeApiKey is not null)
            request.Headers.TryAddWithoutValidation("Helicone-Auth", $"Bearer {config.HeliconeApiKey}");

        // Per-request context from AsyncLocal (set by AnthropicAgentRunner)
        var ctx = _contextProvider.GetCurrentContext();
        if (ctx is not null)
        {
            request.Headers.TryAddWithoutValidation("Helicone-Session-Id",     ctx.SessionId);
            request.Headers.TryAddWithoutValidation("Helicone-Session-Name",   ctx.AgentName);
            request.Headers.TryAddWithoutValidation("Helicone-User-Id",        $"{ctx.TenantId}/{ctx.UserId ?? "anon"}");
            request.Headers.TryAddWithoutValidation("Helicone-Property-AgentId",  ctx.AgentId);
            request.Headers.TryAddWithoutValidation("Helicone-Property-TenantId", ctx.TenantId.ToString());
        }

        return await base.SendAsync(request, ct);
    }
}
```

### `src/Diva.Infrastructure/Observability/NullLlmProxyHandler.cs`

```csharp
namespace Diva.Infrastructure.Observability;

/// <summary>No-op DelegatingHandler for unit tests — passthrough only.</summary>
public sealed class NullLlmProxyHandler : DelegatingHandler, ILlmProxyHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        => base.SendAsync(r, ct);
}
```

---

## Phase 24e — Wiring Emitters into Existing Classes

### `AnthropicProviderStrategy` (modified)

Add two **optional** constructor parameters at the end — zero impact on 9+ existing test callsites:

```csharp
public AnthropicProviderStrategy(
    IAnthropicProvider anthropic,
    IContextWindowManager ctx,
    string model,
    int maxTokens,
    string staticSystemPrompt,
    string dynamicSystemPrompt,
    Func<Func<Task<MessageResponse>>, CancellationToken, Task<MessageResponse>> retry,
    bool enableHistoryCaching = true,
    string? apiKeyOverride = null,
    ILlmSpanEmitter? spanEmitter = null,          // ← new optional
    ILlmMetricEmitter? metricEmitter = null)       // ← new optional
{
    // existing assignments...
    _spanEmitter   = spanEmitter   ?? NullLlmSpanEmitter.Instance;
    _metricEmitter = metricEmitter ?? NullLlmMetricEmitter.Instance;
}
```

Usage inside `ExecuteAsync`:
- Before LLM call: `using var span = _spanEmitter.StartLlmCall(new LlmCallContext(model, "Anthropic", ...))`
- After response: `_spanEmitter.RecordLlmResult(new LlmCallResult(...))`
- Metrics: `_metricEmitter.RecordTokenUsage(...)`, `_metricEmitter.RecordOperationDuration(...)`, `_metricEmitter.RecordTimeToFirstToken(...)`
- Cost: `_metricEmitter.RecordCostEstimate(...)` (only when `CostTrackingEnabled`)

### `OpenAiProviderStrategy` (modified)

Same pattern as above — add optional `ILlmSpanEmitter? = null`, `ILlmMetricEmitter? = null`.

Token usage sourced from `ChatResponseUpdate.Usage` (already available via `InvokeStreamAsync`).

### `ModelSwitchCoordinator` (modified)

Add optional emitter fields; forward them when constructing new `AnthropicProviderStrategy` or `OpenAiProviderStrategy`:

```csharp
public ModelSwitchCoordinator(
    IAnthropicProvider anthropic,
    IOpenAiProvider openAi,
    IContextWindowManager ctx,
    ILlmConfigResolver? resolver,
    ILogger<ModelSwitchCoordinator> logger,
    ILlmSpanEmitter? spanEmitter = null,           // ← new optional
    ILlmMetricEmitter? metricEmitter = null)       // ← new optional
```

### `AnthropicAgentRunner` (modified)

Add three **optional** parameters at the end of the existing 23-parameter constructor:

```csharp
ILlmSpanEmitter? spanEmitter = null,
IAgentEventLogger? agentEventLogger = null,
IHeliconeContextProvider? heliconeContext = null)
```

Internal assignments:
```csharp
_spanEmitter      = spanEmitter      ?? NullLlmSpanEmitter.Instance;
_agentEventLogger = agentEventLogger ?? NullAgentEventLogger.Instance;
_heliconeContext   = heliconeContext  ?? new HeliconeContextProvider(); // harmless noop when disabled
```

Usage in `InvokeStreamAsync`:
1. Start of method: `using var agentScope = _agentEventLogger.BeginAgentScope(definition.Name, tenant.TenantId, sessionId)`
2. Before each strategy invocation: `_heliconeContext.SetContext(new HeliconeRequestContext(sessionId, definition.Name, definition.Id, tenant.TenantId, tenant.UserId))`
3. Root agent span: `using var agentSpan = _spanEmitter.StartAgentInvoke(definition.Name, tenant.TenantId, sessionId)`
4. Per iteration: `using var iterSpan = _spanEmitter.StartIteration(iterationNumber)`
5. Log iteration: `_agentEventLogger.LogIterationStart(iterationNumber, windowNumber)`
6. Continuation window: `_metricEmitter.RecordContinuationWindow(definition.Name)`
7. End of invocation: `_metricEmitter.RecordAgentIterations(definition.Name, totalIterations)`
8. Set `traceparent` response header: `httpContext?.Response.Headers.TryAdd("traceparent", Activity.Current?.Id ?? "")`

### `McpClientCache` (modified — not `McpConnectionManager`)

Add optional `ILlmMetricEmitter? metricEmitter = null` to constructor. Call:
- On cache hit: `_metricEmitter?.RecordMcpCacheHit(definition.Name)`
- On cache miss: `_metricEmitter?.RecordMcpCacheMiss(definition.Name)`

### `OpenAiProvider` (modified — fixes gap #4)

Register with named `HttpClient` so `HeliconeHttpHandler` can be attached. Change constructor to accept `HttpClient`:

```csharp
public OpenAiProvider(IOptions<LlmOptions> opts, HttpClient httpClient)
{
    _opts = opts.Value;
    _httpClient = httpClient;
}
```

`CreateChatClient` passes `_httpClient` to `OpenAIClientOptions.Transport`:
```csharp
var clientOptions = new OpenAIClientOptions();
if (!string.IsNullOrEmpty(endpoint))
    clientOptions.Endpoint = new Uri(endpoint);
clientOptions.Transport = new HttpClientPipelineTransport(_httpClient);
```

---

## Phase 24f — Structured Log Enrichment

### `SerilogAgentEventLogger.cs`

Update Serilog output template in `appsettings.json`:

```json
"outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] [{TenantId:-}:{AgentName:-}:{SessionId:-}] TraceId={TraceId:-} {Message:lj}{NewLine}{Exception}"
```

Also add `Enrich.WithMachineName()` and `Enrich.WithProperty("service.version", "1.0")` to Serilog pipeline.

---

## Phase 24g — Prometheus Metrics Summary Reader

### `IMetricsSummaryReader.cs`

```csharp
namespace Diva.Infrastructure.Observability;

public interface IMetricsSummaryReader
{
    /// <summary>Returns null if Prometheus is not configured or unavailable.</summary>
    Task<MetricsSummaryDto?> GetLast24hSummaryAsync(CancellationToken ct = default);
}
```

### `PrometheusMetricsSummaryReader.cs`

Reads `PrometheusUrl` from `IObservabilityConfigService`. Queries Prometheus HTTP API (`/api/v1/query`) for:
- `sum(increase(gen_ai_client_token_usage_total[24h]))` by `gen_ai_token_type`
- `sum(increase(diva_llm_estimated_cost_usd_total[24h]))` by `diva_tenant_id`
- `topk(5, sum(increase(diva_tool_calls_total[24h])) by (tool_name))`

Returns `null` if `PrometheusUrl` is null/empty or if the HTTP call fails (graceful degradation).

### `MetricsSummaryDto.cs` (in `Diva.Core/Observability/`)

```csharp
namespace Diva.Core.Observability;

public sealed record MetricsSummaryDto(
    long InputTokens24h,
    long OutputTokens24h,
    long CacheCreationTokens24h,
    long CacheReadTokens24h,
    decimal EstimatedCostUsd24h,
    List<ToolCallSummary> TopTools);

public sealed record ToolCallSummary(string ToolName, long CallCount);
```

---

## Phase 24h — Backend API Controller

### `src/Diva.Host/Controllers/ObservabilityController.cs` (new)

```csharp
[ApiController]
[Route("api/admin/observability")]
[Authorize]
public sealed class ObservabilityController : ControllerBase
{
    [HttpGet("config")]
    public async Task<IActionResult> GetConfig(
        [FromServices] IObservabilityConfigService svc,
        CancellationToken ct)
    {
        var config = await svc.GetConfigAsync(ct);
        var entity = await _repo.GetAsync(ct); // to get HeliconeApiKeyEncrypted != null check
        return Ok(new ObservabilityConfigDto
        {
            HeliconeEnabled          = config.HeliconeEnabled,
            HeliconeBaseUrl          = config.HeliconeBaseUrl,
            HeliconeApiKeySet        = entity?.HeliconeApiKeyEncrypted is not null,
            LogPromptContent         = config.LogPromptContent,
            CostTrackingEnabled      = config.CostTrackingEnabled,
            MetricsRetentionDays     = config.MetricsRetentionDays,
            PrometheusUrl            = config.PrometheusUrl,
        });
    }

    [HttpPut("config")]
    public async Task<IActionResult> SaveConfig(
        [FromBody] UpdateObservabilityConfigDto dto,
        [FromServices] IObservabilityConfigService svc,
        CancellationToken ct)
    {
        await svc.SaveConfigAsync(new ObservabilityConfig
        {
            HeliconeEnabled      = dto.HeliconeEnabled,
            HeliconeBaseUrl      = dto.HeliconeBaseUrl ?? "http://helicone:8787",
            HeliconeApiKey       = dto.HeliconeApiKey,   // null = don't change
            LogPromptContent     = dto.LogPromptContent,
            CostTrackingEnabled  = dto.CostTrackingEnabled,
            MetricsRetentionDays = dto.MetricsRetentionDays,
            PrometheusUrl        = dto.PrometheusUrl,
        }, ct);
        return Ok();
    }

    [HttpPost("config/test-helicone")]
    public async Task<IActionResult> TestHelicone(
        [FromServices] IObservabilityConfigService svc,
        [FromServices] IHttpClientFactory httpFactory,
        CancellationToken ct)
    {
        var config = await svc.GetConfigAsync(ct);
        if (!config.HeliconeEnabled || string.IsNullOrEmpty(config.HeliconeBaseUrl))
            return BadRequest(new { success = false, message = "Helicone not enabled." });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var client = httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            var resp = await client.GetAsync($"{config.HeliconeBaseUrl.TrimEnd('/')}/healthcheck", ct);
            sw.Stop();
            return Ok(new { success = resp.IsSuccessStatusCode, message = resp.ReasonPhrase, latencyMs = sw.ElapsedMilliseconds });
        }
        catch (Exception ex)
        {
            sw.Stop();
            return Ok(new { success = false, message = ex.Message, latencyMs = sw.ElapsedMilliseconds });
        }
    }

    [HttpGet("metrics/summary")]
    public async Task<IActionResult> GetMetricsSummary(
        [FromServices] IMetricsSummaryReader reader,
        CancellationToken ct)
    {
        var summary = await reader.GetLast24hSummaryAsync(ct);
        return Ok(summary); // null → 200 with null body → frontend shows "not configured"
    }
}
```

### DTOs in `src/Diva.Core/Observability/`

**`ObservabilityConfigDto.cs`** — matches `ObservabilityConfigEntity` fields; `HeliconeApiKeySet: bool` instead of encrypted value (never return secrets).

**`UpdateObservabilityConfigDto.cs`** — all fields present; `HeliconeApiKey` is `string?` — only sent when user provides a new value.

---

## Phase 24i — Admin Portal UI

### `admin-portal/src/components/ObservabilitySettings.tsx` (new)

Follows the existing settings page pattern (see `CredentialManager.tsx`, `ApiKeyManager.tsx`).

**State:** `useState` — consistent with all other settings pages (no `react-hook-form`).

**Sections:**

1. **Helicone Integration** (collapsible, auto-expands when `heliconeEnabled = true`)
   - `Switch` toggle for `heliconeEnabled` (shadcn Switch)
   - Text input: `heliconeBaseUrl` (conditional render when enabled)
   - Password-style input: API Key — shows `"●●●●● Set"` when `heliconeApiKeySet = true`; new input only POSTed when user changes it (same pattern as `CredentialManager` update flow)
   - `Test Connection` button → `api.testHeliconeConnection()` → inline `Badge` with success/error + latency
   - Privacy note: `"API key stored encrypted (AES-256-GCM)"`

2. **Tracing & Logging**
   - `Switch`: `logPromptContent` (with inline warning: "Off by default — enables request/response content in traces. Review your data retention policy before enabling.")
   - `Switch`: `costTrackingEnabled`
   - Number input: `metricsRetentionDays`
   - Text input: `prometheusUrl` (optional — placeholder: `"http://prometheus:9090"`)

3. **Metrics Summary (read-only)**
   - 24h stat cards: Input Tokens, Output Tokens, Cache Tokens, Estimated Cost
   - Top 5 Tools table: tool name + call count
   - Skeleton loading state; shows "Connect Prometheus to see metrics" when `null`

**Submit:** single Save button → `api.updateObservabilityConfig(dto)` → `toast.success("Saved")` → reload.

### TypeScript types added to `admin-portal/src/api.ts`

```typescript
export interface ObservabilityConfigDto {
  heliconeEnabled: boolean;
  heliconeBaseUrl: string;
  heliconeApiKeySet: boolean;
  logPromptContent: boolean;
  costTrackingEnabled: boolean;
  metricsRetentionDays: number;
  prometheusUrl: string | null;
}

export interface UpdateObservabilityConfigDto {
  heliconeEnabled: boolean;
  heliconeBaseUrl: string;
  heliconeApiKey?: string;    // only set when user enters a new key
  logPromptContent: boolean;
  costTrackingEnabled: boolean;
  metricsRetentionDays: number;
  prometheusUrl: string | null;
}

export interface MetricsSummaryDto {
  inputTokens24h: number;
  outputTokens24h: number;
  cacheCreationTokens24h: number;
  cacheReadTokens24h: number;
  estimatedCostUsd24h: number;
  topTools: Array<{ toolName: string; callCount: number }>;
}
```

API methods added:
```typescript
getObservabilityConfig: () => Promise<ObservabilityConfigDto>
updateObservabilityConfig: (dto: UpdateObservabilityConfigDto) => Promise<void>
testHeliconeConnection: () => Promise<{ success: boolean; message: string; latencyMs: number }>
getObservabilityMetricsSummary: () => Promise<MetricsSummaryDto | null>
```

### Route + Navigation

**`admin-portal/src/App.tsx`** — add:
```tsx
<Route path="/settings/observability" element={<ObservabilitySettings />} />
```

**`admin-portal/src/components/layout/app-sidebar.tsx`** — add to Settings group (alongside `credentials`, `api-keys`):
```tsx
{ title: "Observability", url: "/settings/observability", icon: Activity }
```
`Activity` is from `lucide-react` (already a dependency).

### MSW Mock Handlers

**`admin-portal/src/mocks/handlers.ts`** — add four handlers:
```typescript
http.get(`${BASE}/api/admin/observability/config`, async () => {
  await delay(200);
  return HttpResponse.json({
    heliconeEnabled: false, heliconeBaseUrl: "http://helicone:8787",
    heliconeApiKeySet: false, logPromptContent: false,
    costTrackingEnabled: true, metricsRetentionDays: 30, prometheusUrl: null
  } satisfies ObservabilityConfigDto);
}),

http.put(`${BASE}/api/admin/observability/config`, async () => {
  await delay(300);
  return new HttpResponse(null, { status: 200 });
}),

http.post(`${BASE}/api/admin/observability/config/test-helicone`, async () => {
  await delay(500);
  return HttpResponse.json({ success: true, message: "OK", latencyMs: 42 });
}),

http.get(`${BASE}/api/admin/observability/metrics/summary`, async () => {
  await delay(200);
  return HttpResponse.json({
    inputTokens24h: 1_250_000, outputTokens24h: 320_000,
    cacheCreationTokens24h: 85_000, cacheReadTokens24h: 440_000,
    estimatedCostUsd24h: 4.87,
    topTools: [
      { toolName: "read_file", callCount: 142 },
      { toolName: "search", callCount: 98 },
      { toolName: "list_dir", callCount: 71 },
      { toolName: "write_file", callCount: 43 },
      { toolName: "run_command", callCount: 29 }
    ]
  } satisfies MetricsSummaryDto);
}),
```

---

## Phase 24j — Infrastructure: OTel Collector + Grafana + Tempo

### `docker/otel-collector-config.yaml` (new — file doesn't exist yet)

```yaml
receivers:
  otlp:
    protocols:
      grpc:
        endpoint: "0.0.0.0:4317"
      http:
        endpoint: "0.0.0.0:4318"

processors:
  batch:
    send_batch_size: 512
    timeout: 5s

  memory_limiter:
    check_interval: 1s
    limit_mib: 512
    spike_limit_mib: 128

  resource:
    attributes:
      - key: deployment.environment
        value: "${DEPLOYMENT_ENVIRONMENT:-development}"
        action: upsert

exporters:
  prometheusremotewrite:
    endpoint: "http://prometheus:9090/api/v1/write"
    tls:
      insecure: true

  otlp/tempo:
    endpoint: "http://tempo:4317"
    tls:
      insecure: true

  debug:
    verbosity: detailed

service:
  telemetry:
    logs:
      level: "warn"
  pipelines:
    traces:
      receivers:  [otlp]
      processors: [memory_limiter, resource, batch]
      exporters:  [otlp/tempo]
    metrics:
      receivers:  [otlp]
      processors: [memory_limiter, resource, batch]
      exporters:  [prometheusremotewrite]
    logs:
      receivers:  [otlp]
      processors: [memory_limiter, batch]
      exporters:  [debug]
```

### `docker-compose.enterprise.yml` additions

```yaml
  grafana:
    image: grafana/grafana:11.4.0
    container_name: diva-grafana
    ports:
      - "3001:3000"
    environment:
      GF_SECURITY_ADMIN_PASSWORD: "${GRAFANA_ADMIN_PASSWORD:-admin}"
      GF_PATHS_PROVISIONING: /etc/grafana/provisioning
    volumes:
      - ./docker/grafana/provisioning:/etc/grafana/provisioning:ro
      - ./docker/grafana/dashboards:/var/lib/grafana/dashboards:ro
      - grafana_data:/var/lib/grafana
    networks: [diva-net]
    depends_on: [prometheus, tempo]

  tempo:
    image: grafana/tempo:2.6.0
    container_name: diva-tempo
    command: ["-config.file=/etc/tempo.yaml"]
    ports:
      - "3200:3200"
      - "4317"    # internal otlp grpc
    volumes:
      - ./docker/tempo.yaml:/etc/tempo.yaml:ro
      - tempo_data:/tmp/tempo
    networks: [diva-net]

  helicone:
    image: helicone/helicone-proxy:latest
    container_name: diva-helicone
    ports:
      - "8787:8787"
    environment:
      HELICONE_API_KEY: "${HELICONE_API_KEY:-}"
    networks: [diva-net]
    profiles: ["helicone"]   # opt-in: docker compose --profile helicone up
```

### `docker/grafana/provisioning/datasources.yml` (new)

```yaml
apiVersion: 1
datasources:
  - name: Prometheus
    type: prometheus
    url: http://prometheus:9090
    isDefault: true
    access: proxy

  - name: Tempo
    type: tempo
    url: http://tempo:3200
    access: proxy
    jsonData:
      tracesToLogsV2:
        datasourceUid: seq   # cross-link traces → Seq logs via TraceId
      serviceMap:
        datasourceUid: Prometheus
```

### `docker/grafana/provisioning/dashboards.yml` (new)

```yaml
apiVersion: 1
providers:
  - name: Diva
    type: file
    folder: Diva
    options:
      path: /var/lib/grafana/dashboards
```

### `docker/tempo.yaml` (new)

Minimal Tempo config for local development — uses local storage, exposes gRPC 4317:

```yaml
stream_over_http_enabled: true
server:
  http_listen_port: 3200

distributor:
  receivers:
    otlp:
      protocols:
        grpc:
          endpoint: "0.0.0.0:4317"

storage:
  trace:
    backend: local
    local:
      path: /tmp/tempo/blocks

compactor:
  compaction:
    block_retention: 168h   # 7 days
```

### Grafana Dashboards

Create four JSON dashboards in `docker/grafana/dashboards/`. These are standard Grafana panel JSON — full panel JSON omitted here for brevity; implement from scratch using the provisioned datasources.

| File | Key panels |
|------|-----------|
| `llm-overview.json` | Token usage over time (stacked by type), estimated cost by tenant (bar), model distribution (pie), LLM latency p50/p95/p99 (histogram heatmap) |
| `agent-performance.json` | Iteration count distribution (histogram), tool call count per tool (bar), error rates by provider (time series), continuation window frequency |
| `tool-execution.json` | Per-tool duration histogram, top 10 tools by call count, MCP cache hit rate gauge |
| `infrastructure.json` | ASP.NET Core request rate + latency (from OTel auto-instrumentation), DB query latency, memory/CPU |

### `.env.example` additions

```
HELICONE_API_KEY=
GRAFANA_ADMIN_PASSWORD=admin
DEPLOYMENT_ENVIRONMENT=development
```

---

## Phase 24k — Program.cs Wiring

### `src/Diva.Host/Program.cs` (modified)

**OpenAI provider HttpClient** (replace existing `AddSingleton<IOpenAiProvider, OpenAiProvider>()`):
```csharp
builder.Services.AddHttpClient("OpenAiProvider", client =>
    client.Timeout = TimeSpan.FromSeconds(llmTimeoutSec));
builder.Services.AddSingleton<IOpenAiProvider>(sp =>
{
    var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
    var httpClient = httpFactory.CreateClient("OpenAiProvider");
    var opts = sp.GetRequiredService<IOptions<LlmOptions>>();
    return new OpenAiProvider(opts, httpClient);
});
```

**Observability services:**
```csharp
// ── Phase 24: Observability ────────────────────────────────────────────────
builder.Services.AddSingleton<DivaMeter>();
builder.Services.AddSingleton<ILlmSpanEmitter, OtelLlmSpanEmitter>();
builder.Services.AddSingleton<ILlmMetricEmitter, OtelLlmMetricEmitter>();
builder.Services.AddSingleton<IAgentEventLogger, SerilogAgentEventLogger>();
builder.Services.AddSingleton<IHeliconeContextProvider, HeliconeContextProvider>();
builder.Services.AddSingleton<IObservabilityConfigRepository, ObservabilityConfigRepository>();
builder.Services.AddSingleton<IObservabilityConfigService, ObservabilityConfigService>();
builder.Services.AddSingleton<IMetricsSummaryReader, PrometheusMetricsSummaryReader>();

// Helicone DelegatingHandler — must be Transient (DelegatingHandler requirement)
builder.Services.AddTransient<HeliconeHttpHandler>();
```

**Attach Helicone handler to both named HttpClients:**
```csharp
builder.Services.AddHttpClient("AnthropicProvider", client =>
    client.Timeout = TimeSpan.FromSeconds(llmTimeoutSec))
    .AddHttpMessageHandler<HeliconeHttpHandler>();

builder.Services.AddHttpClient("OpenAiProvider", client =>
    client.Timeout = TimeSpan.FromSeconds(llmTimeoutSec))
    .AddHttpMessageHandler<HeliconeHttpHandler>();
```

**OTel registration (extend existing `.WithTracing()` and `.WithMetrics()`):**
```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(brandingForOtel.ApiAudience))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource(DivaActivitySources.AllSourceNames)    // ← new
        .AddOtlpExporter(o => o.Endpoint = new Uri(otelEndpoint)))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddMeter("Diva")                                 // ← new
        .AddOtlpExporter(o => o.Endpoint = new Uri(otelEndpoint)));
```

**Inject emitters into `AnthropicAgentRunner` registration:**
```csharp
builder.Services.AddSingleton<AnthropicAgentRunner>(sp => new AnthropicAgentRunner(
    sp.GetRequiredService<IOptions<LlmOptions>>(),
    // ... all existing params ...
    spanEmitter:      sp.GetRequiredService<ILlmSpanEmitter>(),
    agentEventLogger: sp.GetRequiredService<IAgentEventLogger>(),
    heliconeContext:  sp.GetRequiredService<IHeliconeContextProvider>()));
```

---

## Phase 24l — Context Propagation

1. **OTel Baggage** — set in `OtelLlmSpanEmitter.StartAgentInvoke()`:
   ```csharp
   Baggage.SetBaggage("diva.tenant_id",  tenantId.ToString());
   Baggage.SetBaggage("diva.session_id", sessionId);
   Baggage.SetBaggage("diva.agent_name", agentName);
   ```
2. **W3C `traceparent` header on SSE response** — in `AgentsController` before streaming begins:
   ```csharp
   if (Activity.Current?.Id is { } traceId)
       Response.Headers.TryAdd("traceparent", traceId);
   ```
3. **A2A propagation** — automatic via `AddHttpClientInstrumentation()`; no manual work needed.
4. **Serilog↔OTel bridge** — `SerilogAgentEventLogger.BeginAgentScope()` reads `Activity.Current?.TraceId` and pushes it as `TraceId` property so Seq logs carry the same trace ID as Tempo spans.

---

## NuGet Package Changes

**New packages — test project only (`tests/Diva.Agents.Tests/Diva.Agents.Tests.csproj`):**

```xml
<PackageReference Include="OpenTelemetry.Testing.Metrics" Version="1.*" />
```

**No new packages needed in `Diva.Core` or `Diva.Infrastructure`:**
- `System.Diagnostics.ActivitySource` and `System.Diagnostics.Metrics.Meter` are built into .NET 10 (no NuGet package needed)
- All OTel exporter packages are already in `Diva.Host` / `Diva.Infrastructure`
- `Serilog.Context.LogContext` is part of `Serilog.Core` (already a dependency)

---

## Testing

All test files go in `tests/Diva.Agents.Tests/Observability/`.

### `HeliconeHttpHandlerTests.cs` (unit)

```csharp
[Fact]
public async Task SendAsync_WhenDisabled_PassesThroughUnchanged()

[Fact]
public async Task SendAsync_WhenEnabled_RewritesBaseUrlAndInjectsHeaders()
// Assert: request.RequestUri.Host == "helicone", all 5 headers present

[Fact]
public async Task SendAsync_WhenEnabledButNoContext_OnlyInjectsAuthHeader()
// _heliconeContext.GetCurrentContext() returns null
```

Test infrastructure: inline `MockInnerHandler` that captures the `HttpRequestMessage`:
```csharp
private sealed class MockInnerHandler : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
    {
        LastRequest = r;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
```

### `OtelLlmMetricEmitterTests.cs` (unit)

Uses `MetricCollector<T>` from `OpenTelemetry.Testing.Metrics`:

```csharp
[Fact]
public void RecordTokenUsage_RecordsAllFourTokenTypes()
// Assert: collector has 4 measurements with correct gen_ai.token.type tags

[Fact]
public void RecordCostEstimate_UsesModelCostTable_ForKnownModel()
// Assert: diva.llm.estimated_cost_usd counter incremented with correct value
```

### `ModelCostTableTests.cs` (unit)

```csharp
[Fact]
public void GetCost_KnownModel_ReturnsExpectedRate()

[Fact]
public void GetCost_UnknownModel_ReturnsZero_NotThrow()

[Fact]
public void EstimateCost_AllFourTokenTypes_SumsCorrectly()
```

### `ObservabilityConfigServiceTests.cs` (unit)

Uses `NSubstitute` for `IObservabilityConfigRepository`, real `MemoryCache`:

```csharp
[Fact]
public async Task GetConfigAsync_CacheHit_DoesNotQueryRepository()
// Call twice; assert _repo.GetAsync received(1) call

[Fact]
public async Task SaveConfigAsync_WhenNewApiKeyProvided_EncryptsBeforePersisting()
// Assert _encryptor.Encrypt() called

[Fact]
public async Task InvalidateCache_ForcesDatabaseRoundtripOnNextGet()
```

### `ObservabilityConfigRepositoryTests.cs` (integration — real SQLite)

```csharp
[Fact]
public async Task UpsertAsync_ThenGetAsync_ReturnsCorrectValues()
// real in-memory SQLite; create entity; upsert; get; assert all fields round-trip

[Fact]
public async Task UpsertAsync_CalledTwice_UpdatesExistingRow_NotDuplicates()
```

---

## File List

### New — `Diva.Core`

| File | Type |
|------|------|
| `src/Diva.Core/Observability/ILlmSpanEmitter.cs` | Interface |
| `src/Diva.Core/Observability/ILlmMetricEmitter.cs` | Interface |
| `src/Diva.Core/Observability/IAgentEventLogger.cs` | Interface |
| `src/Diva.Core/Observability/ILlmProxyHandler.cs` | Interface (marker) |
| `src/Diva.Core/Observability/IHeliconeContextProvider.cs` | Interface |
| `src/Diva.Core/Observability/IObservabilityConfigService.cs` | Interface |
| `src/Diva.Core/Observability/LlmCallContext.cs` | Record |
| `src/Diva.Core/Observability/LlmCallResult.cs` | Record |
| `src/Diva.Core/Observability/ObservabilityConfig.cs` | Record (domain model) |
| `src/Diva.Core/Observability/HeliconeRequestContext.cs` | Record |
| `src/Diva.Core/Observability/ObservabilityConfigDto.cs` | DTO |
| `src/Diva.Core/Observability/UpdateObservabilityConfigDto.cs` | DTO |
| `src/Diva.Core/Observability/MetricsSummaryDto.cs` | DTO |

### New — `Diva.Infrastructure`

| File | Type |
|------|------|
| `src/Diva.Infrastructure/Observability/DivaActivitySources.cs` | Static class |
| `src/Diva.Infrastructure/Observability/DivaMeter.cs` | Class |
| `src/Diva.Infrastructure/Observability/OtelLlmSpanEmitter.cs` | `ILlmSpanEmitter` impl |
| `src/Diva.Infrastructure/Observability/OtelLlmMetricEmitter.cs` | `ILlmMetricEmitter` impl |
| `src/Diva.Infrastructure/Observability/SerilogAgentEventLogger.cs` | `IAgentEventLogger` impl |
| `src/Diva.Infrastructure/Observability/ModelCostTable.cs` | Static helper |
| `src/Diva.Infrastructure/Observability/NullLlmSpanEmitter.cs` | Null-object |
| `src/Diva.Infrastructure/Observability/NullLlmMetricEmitter.cs` | Null-object |
| `src/Diva.Infrastructure/Observability/NullAgentEventLogger.cs` | Null-object |
| `src/Diva.Infrastructure/Observability/HeliconeContextProvider.cs` | `IHeliconeContextProvider` impl |
| `src/Diva.Infrastructure/Observability/HeliconeHttpHandler.cs` | `DelegatingHandler + ILlmProxyHandler` |
| `src/Diva.Infrastructure/Observability/NullLlmProxyHandler.cs` | Null-object |
| `src/Diva.Infrastructure/Observability/IObservabilityConfigRepository.cs` | Interface |
| `src/Diva.Infrastructure/Observability/ObservabilityConfigRepository.cs` | Repository |
| `src/Diva.Infrastructure/Observability/ObservabilityConfigService.cs` | `IObservabilityConfigService` impl |
| `src/Diva.Infrastructure/Observability/IMetricsSummaryReader.cs` | Interface |
| `src/Diva.Infrastructure/Observability/PrometheusMetricsSummaryReader.cs` | Impl |
| `src/Diva.Infrastructure/Data/Entities/ObservabilityConfigEntity.cs` | EF entity |
| `src/Diva.Infrastructure/Data/Migrations/20260430000000_AddObservabilityConfig.cs` | Migration |
| `src/Diva.Infrastructure/Data/Migrations/20260430000000_AddObservabilityConfig.Designer.cs` | Migration designer |

### Modified — `Diva.Infrastructure`

| File | Change |
|------|--------|
| `src/Diva.Infrastructure/Data/DivaDbContext.cs` | Add `DbSet<ObservabilityConfigEntity>` + `OnModelCreating` entry |
| `src/Diva.Infrastructure/Data/Migrations/DivaDbContextModelSnapshot.cs` | Append `ObservabilityConfigs` entity block |
| `src/Diva.Infrastructure/LiteLLM/AnthropicProviderStrategy.cs` | Add optional `ILlmSpanEmitter?`, `ILlmMetricEmitter?` params |
| `src/Diva.Infrastructure/LiteLLM/OpenAiProviderStrategy.cs` | Same |
| `src/Diva.Infrastructure/LiteLLM/ModelSwitchCoordinator.cs` | Add optional emitter params; forward to created strategies |
| `src/Diva.Infrastructure/LiteLLM/AnthropicAgentRunner.cs` | Add optional `ILlmSpanEmitter?`, `IAgentEventLogger?`, `IHeliconeContextProvider?` params |
| `src/Diva.Infrastructure/LiteLLM/OpenAiProvider.cs` | Accept `HttpClient` in constructor; use in `CreateChatClient()` |
| `src/Diva.Infrastructure/LiteLLM/McpClientCache.cs` | Add optional `ILlmMetricEmitter?`; record cache hits/misses |

### New — `Diva.Host`

| File | Type |
|------|------|
| `src/Diva.Host/Controllers/ObservabilityController.cs` | Controller |

### Modified — `Diva.Host`

| File | Change |
|------|--------|
| `src/Diva.Host/Program.cs` | Register observability services, extend OTel config, attach Helicone handler, wire `OpenAiProvider` HttpClient |
| `src/Diva.Host/appsettings.json` | Update Serilog output template to include `{TraceId}` and `{SpanId}` |

### New — `admin-portal`

| File | Type |
|------|------|
| `admin-portal/src/components/ObservabilitySettings.tsx` | Settings page |

### Modified — `admin-portal`

| File | Change |
|------|--------|
| `admin-portal/src/App.tsx` | Add `/settings/observability` route |
| `admin-portal/src/components/layout/app-sidebar.tsx` | Add nav entry |
| `admin-portal/src/api.ts` | Add 4 API methods + 3 TypeScript types |
| `admin-portal/src/mocks/handlers.ts` | Add 4 mock handlers |

### New — Infrastructure

| File | Type |
|------|------|
| `docker/otel-collector-config.yaml` | OTel Collector config (create from scratch) |
| `docker/tempo.yaml` | Grafana Tempo config |
| `docker/grafana/provisioning/datasources.yml` | Grafana datasource provisioning |
| `docker/grafana/provisioning/dashboards.yml` | Grafana dashboard provisioning config |
| `docker/grafana/dashboards/llm-overview.json` | Grafana dashboard |
| `docker/grafana/dashboards/agent-performance.json` | Grafana dashboard |
| `docker/grafana/dashboards/tool-execution.json` | Grafana dashboard |
| `docker/grafana/dashboards/infrastructure.json` | Grafana dashboard |

### Modified — Infrastructure

| File | Change |
|------|--------|
| `docker-compose.enterprise.yml` | Add `grafana`, `tempo`, opt-in `helicone` (profile: `helicone`) services |
| `.env.example` | Add `HELICONE_API_KEY`, `GRAFANA_ADMIN_PASSWORD`, `DEPLOYMENT_ENVIRONMENT` |

### New — Tests

| File | Type |
|------|------|
| `tests/Diva.Agents.Tests/Observability/HeliconeHttpHandlerTests.cs` | Unit |
| `tests/Diva.Agents.Tests/Observability/OtelLlmMetricEmitterTests.cs` | Unit |
| `tests/Diva.Agents.Tests/Observability/ModelCostTableTests.cs` | Unit |
| `tests/Diva.Agents.Tests/Observability/ObservabilityConfigServiceTests.cs` | Unit |
| `tests/Diva.Agents.Tests/Observability/ObservabilityConfigRepositoryTests.cs` | Integration (SQLite) |

### Modified — Tests

| File | Change |
|------|--------|
| `tests/Diva.Agents.Tests/Diva.Agents.Tests.csproj` | Add `OpenTelemetry.Testing.Metrics` package reference |

---

## Verification Checklist

1. `dotnet build Diva.slnx` — zero errors; no existing test callsites broken (`AnthropicProviderStrategy` and `AnthropicAgentRunner` new params are all optional)
2. `dotnet test` — all existing tests pass; 15+ new observability tests pass
3. Start enterprise stack: `docker compose -f docker-compose.yml -f docker-compose.enterprise.yml up -d`
4. Submit an agent request → Seq shows every log line with `TenantId`, `AgentName`, `SessionId`, `TraceId`, `SpanId`
5. Grafana (port 3001) → Prometheus datasource → metrics browser shows `gen_ai_client_token_usage_total` and `diva_tool_execution_duration_bucket`
6. Grafana Explore → Tempo → search traces → find `gen_ai.agent.invoke` root span with children `gen_ai.agent.iteration → gen_ai.chat + gen_ai.tool.call`
7. Navigate to `/settings/observability` → all three sections render correctly
8. Toggle Helicone on → URL + API key fields appear → `Test Connection` → green badge
9. Save → `PUT /api/admin/observability/config` → 200 → `toast.success("Saved")`
10. `docker compose --profile helicone up -d helicone` → enable Helicone in UI → run agent → Helicone proxy shows request logged with correct session/agent/tenant properties
11. Metrics Summary section shows 24h stats when Prometheus is connected; shows "Connect Prometheus" gracefully when `PrometheusUrl` is null
