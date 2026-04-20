# Phase 5: MCP Tool Infrastructure & Domain Tools

> **Status:** `[~]` In Progress — client infrastructure complete; domain tool servers deferred
> **Depends on:** [phase-03-oauth-tenant.md](phase-03-oauth-tenant.md), [phase-04-database.md](phase-04-database.md)
> **Blocks:** ~~[phase-08-agents.md](phase-08-agents.md)~~ — no longer blocks Phase 8
> **Project:** `Diva.Tools` (domain servers), `Diva.Infrastructure/LiteLLM` (agent runner), `Diva.Host/Controllers` (probe endpoint)
> **Architecture ref:** [arch-oauth-flow.md](arch-oauth-flow.md) (MCP header injection)

> **Decision:** Domain MCP tool servers (AnalyticsMcpServer, ReservationMcpServer) are business-specific and deferred until real data backends are available.
> The MCP **client** infrastructure is fully implemented: multi-server binding support, Docker MCP Gateway integration, and the `/api/agents/mcp-probe` endpoint are all complete.

---

## Goal

Build the MCP tool infrastructure (header injection, base class) and implement the first two domain tool servers: Analytics and Reservation. All MCP tools automatically receive OAuth token + TenantID headers.

---

## ✅ Completed: Multi-Server MCP Client Support

### Overview

`AnthropicAgentRunner` now connects to **all valid tool bindings** in parallel rather than only the first one. A `Dictionary<string, McpClient>` (tool name → owning client) is built so that tool calls are routed to the correct server.

### ConnectMcpClientsAsync

```csharp
// In Diva.Infrastructure/LiteLLM/AnthropicAgentRunner.cs
private async Task<Dictionary<string, McpClient>> ConnectMcpClientsAsync(
    AgentDefinitionEntity definition, CancellationToken ct)
{
    var bindings = JsonSerializer.Deserialize<List<McpToolBinding>>(
        definition.ToolBindings ?? "[]") ?? [];

    var clients = new Dictionary<string, McpClient>();
    foreach (var b in bindings)
    {
        // Skip incomplete bindings — requires a name AND (command or endpoint)
        if (string.IsNullOrWhiteSpace(b.Name)) continue;
        if (string.IsNullOrWhiteSpace(b.Command) && string.IsNullOrWhiteSpace(b.Endpoint)) continue;

        try
        {
            IClientTransport transport = !string.IsNullOrWhiteSpace(b.Endpoint)
                ? new HttpClientTransport(new HttpClientTransportOptions
                    { Endpoint = new Uri(b.Endpoint), Name = b.Name })
                : new StdioClientTransport(new StdioClientTransportOptions
                    { Name = b.Name, Command = b.Command!, Arguments = b.Args ?? [] });

            var client = await McpClient.CreateAsync(transport, cancellationToken: ct);
            clients[b.Name] = client;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect MCP binding '{Name}'", b.Name);
        }
    }
    return clients;
}
```

### BuildToolClientMapAsync

Builds a reverse-lookup dictionary from tool name to its owning `McpClient`:

```csharp
private static async Task<Dictionary<string, McpClient>> BuildToolClientMapAsync(
    Dictionary<string, McpClient> clients, CancellationToken ct)
{
    var map = new Dictionary<string, McpClient>(StringComparer.OrdinalIgnoreCase);
    foreach (var (_, client) in clients)
    {
        var tools = await client.ListToolsAsync(cancellationToken: ct);
        foreach (var tool in tools)
            map[tool.Name] = client;   // last-write wins on name collision
    }
    return map;
}
```

### Tool call routing

`ExecuteReActLoopAsync` uses the map to route calls (shared by both provider strategies):

```csharp
var targetClient = toolClientMap.GetValueOrDefault(toolName)
    ?? mcpClients.Values.First();  // fallback to first client if tool not found in map
var result = await targetClient.CallToolAsync(toolName, toolArgs, ct);
```

### Cleanup

```csharp
finally
{
    foreach (var c in mcpClients.Values)
        await c.DisposeAsync();
}
```

---

## ✅ Completed: MCP Probe Endpoint

`POST /api/agents/mcp-probe` — connects to an MCP server (HTTP or stdio) and returns its available tools. Used by the admin portal's Docker Gateway panel to discover tools before saving a binding.

```csharp
// In Diva.Host/Controllers/AgentsController.cs
[HttpPost("mcp-probe")]
public async Task<IActionResult> McpProbe([FromBody] McpProbeRequest req, CancellationToken ct)
{
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(TimeSpan.FromSeconds(30));

    IClientTransport transport;
    if (!string.IsNullOrWhiteSpace(req.Endpoint))
        transport = new HttpClientTransport(new HttpClientTransportOptions
            { Endpoint = new Uri(req.Endpoint), Name = "probe" });
    else if (!string.IsNullOrWhiteSpace(req.Command))
        transport = new StdioClientTransport(new StdioClientTransportOptions
            { Name = "probe", Command = req.Command, Arguments = req.Args ?? [] });
    else
        return BadRequest(new { error = "Provide either endpoint (HTTP) or command (stdio)" });

    await using var client = await McpClient.CreateAsync(transport, cancellationToken: cts.Token);
    var tools = await client.ListToolsAsync(cancellationToken: cts.Token);
    return Ok(new McpProbeResult(true, tools.Select(t => new McpToolInfo(t.Name, t.Description ?? "")).ToList(), null));
}

public record McpProbeRequest(string? Endpoint, string? Command, List<string>? Args);
public record McpToolInfo(string Name, string Description);
public record McpProbeResult(bool Success, List<McpToolInfo> Tools, string? Error);
```

---

## ✅ Completed: Docker MCP Gateway Integration

Docker MCP Toolkit (Docker Desktop 4.42+) multiplexes all configured MCP servers through a single gateway process. The correct connection method is **stdio** (`docker mcp gateway run`), not HTTP.

| Transport | Command | When to use |
|-----------|---------|-------------|
| stdio (default) | `docker mcp gateway run` | Docker Desktop with MCP Toolkit enabled |
| HTTP/SSE | `docker mcp gateway run --transport sse --port 8811` | Remote/headless environments |

### Stdio binding (recommended)

```json
{
  "name": "docker-mcp-gateway",
  "command": "docker",
  "args": ["mcp", "gateway", "run"],
  "env": {},
  "endpoint": "",
  "transport": "stdio"
}
```

### HTTP/SSE binding

```json
{
  "name": "docker-mcp-gateway",
  "command": "",
  "args": [],
  "env": {},
  "endpoint": "http://localhost:8811/sse",
  "transport": "http"
}
```

**Known tool set** (confirmed via mcp-probe against local Docker Desktop with MCP Toolkit):
- `openweather`: `get_current_weather`, `get_forecast`, `get_air_quality`
- `duckduckgo`: `search`, `fetch_content`
- `google-flights`: `get_flights_on_date`, `find_all_flights_in_range`, `get_round_trip_flights`
- `playwright`: 21 `browser_*` tools (screenshot, click, navigate, fill, etc.)
- Meta-tools: `mcp-add`, `mcp-find`, `code-mode`

---

## Files to Create (Domain Tools — Deferred)

```
src/Diva.Tools/
├── Core/
│   ├── IMcpToolServer.cs
│   ├── McpToolBase.cs
│   ├── McpHeaderPropagator.cs
│   └── TenantAwareMcpClient.cs
├── Analytics/
│   ├── AnalyticsMcpServer.cs
│   ├── GetMetricBreakdownTool.cs
│   ├── GetYoYTool.cs
│   ├── RunQueryTool.cs            ← Text-to-SQL (with security validation)
│   ├── GenSnapshotTool.cs
│   └── Models/
│       ├── GetMetricBreakdownParams.cs
│       ├── GetYoYParams.cs
│       └── RunQueryParams.cs
└── Reservation/
    ├── ReservationMcpServer.cs
    ├── CheckAvailabilityTool.cs
    ├── BookReservationTool.cs
    └── Models/
        ├── CheckAvailabilityParams.cs
        └── BookReservationParams.cs
```

---

## McpHeaderPropagator.cs

```csharp
namespace Diva.Tools.Core;

public class McpHeaderPropagator
{
    private readonly IHttpContextAccessor _httpContext;

    public McpHeaderPropagator(IHttpContextAccessor httpContext) => _httpContext = httpContext;

    public Dictionary<string, string> GetHeaders()
    {
        var tenant = _httpContext.HttpContext?.Items["TenantContext"] as TenantContext
            ?? throw new UnauthorizedAccessException("No TenantContext in request scope");

        return McpRequestContext.FromTenantContext(tenant).ToHeaders();
    }

    public TenantContext GetTenantContext() =>
        _httpContext.HttpContext?.Items["TenantContext"] as TenantContext
        ?? throw new UnauthorizedAccessException("No TenantContext in request scope");
}
```

---

## TenantAwareMcpClient.cs

Wraps the MCP `IMcpClient` to inject headers on every tool call:

```csharp
namespace Diva.Tools.Core;

public interface ITenantAwareMcpClient
{
    Task<IList<McpClientTool>> GetToolsAsync(string serverName, CancellationToken ct);
    Task<CallToolResult> InvokeToolAsync(string toolName, IDictionary<string, object?> parameters, CancellationToken ct);
}

public class TenantAwareMcpClient : ITenantAwareMcpClient
{
    private readonly IMcpClientFactory _mcpFactory;
    private readonly McpHeaderPropagator _propagator;
    private readonly ILogger<TenantAwareMcpClient> _logger;

    public async Task<CallToolResult> InvokeToolAsync(
        string toolName,
        IDictionary<string, object?> parameters,
        CancellationToken ct)
    {
        var headers = _propagator.GetHeaders();

        _logger.LogDebug(
            "MCP tool {Tool} called. TenantID={TenantId} CorrelationId={CorrelationId}",
            toolName,
            headers.GetValueOrDefault("X-Tenant-ID"),
            headers.GetValueOrDefault("X-Correlation-ID"));

        // Create MCP client with injected headers
        // Exact API depends on ModelContextProtocol SDK version
        using var client = await _mcpFactory.CreateClientWithHeadersAsync(headers, ct);
        return await client.CallToolAsync(toolName, parameters, ct);
    }
}
```

---

## McpToolBase.cs (abstract base for all tools)

```csharp
namespace Diva.Tools.Core;

public abstract class McpToolBase
{
    protected TenantContext TenantContext { get; private set; } = null!;
    protected McpRequestContext RequestContext { get; private set; } = null!;

    // Called by framework before ExecuteInternalAsync
    public void Initialize(TenantContext tenant)
    {
        TenantContext   = tenant;
        RequestContext  = McpRequestContext.FromTenantContext(tenant);
    }

    // Helper: call downstream API with propagated headers
    protected async Task<T?> CallDownstreamApiAsync<T>(
        HttpClient client, string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        foreach (var (key, value) in RequestContext.ToHeaders())
            request.Headers.TryAddWithoutValidation(key, value);

        var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(ct);
    }

    // Validate mandatory TenantID filter in SQL (used by RunQueryTool)
    protected static void ValidateSqlSecurity(string sql, int tenantId)
    {
        if (!sql.Contains($"TenantID = {tenantId}") &&
            !sql.Contains($"TenantID={tenantId}"))
            throw new SecurityException("Generated SQL must include WHERE TenantID filter");

        string[] forbidden = ["INSERT", "UPDATE", "DELETE", "DROP", "ALTER", "TRUNCATE"];
        if (forbidden.Any(f => sql.Contains(f, StringComparison.OrdinalIgnoreCase)))
            throw new SecurityException("DML operations are not allowed in agent queries");
    }
}
```

---

## AnalyticsMcpServer.cs

```csharp
namespace Diva.Tools.Analytics;

// Registered as MCP server — SK will expose each method as a KernelFunction/tool
public class AnalyticsMcpServer : McpToolBase
{
    private readonly DivaDbContext _db;
    private readonly ILlmClientFactory _llm;
    private readonly IPromptService _prompts;

    [McpServerTool, Description("Get revenue/metric breakdown by channel or period")]
    public async Task<ToolResult> GetMetricBreakdown(
        GetMetricBreakdownParams p, CancellationToken ct)
    {
        var query = _db.FactRevenue
            .Where(r => r.TenantID == TenantContext.CurrentSiteId)
            .Where(r => r.Date >= p.StartDate && r.Date <= p.EndDate);

        if (p.Channel != null)
            query = query.Where(r => r.Channel == p.Channel);

        var results = await query
            .GroupBy(r => r.Channel)
            .Select(g => new { Channel = g.Key, Total = g.Sum(r => r.Amount), Count = g.Count() })
            .ToListAsync(ct);

        return new ToolResult { Success = true, Data = results };
    }

    [McpServerTool, Description("Get year-over-year comparison for a metric")]
    public async Task<ToolResult> GetYoY(GetYoYParams p, CancellationToken ct)
    {
        // Implementation: compare current period vs same period last year
        // Always scoped to TenantContext.CurrentSiteId
        throw new NotImplementedException();
    }

    [McpServerTool, Description("Run an ad-hoc natural language query (Text-to-SQL)")]
    public async Task<ToolResult> RunQuery(RunQueryParams p, CancellationToken ct)
    {
        // 1. Get schema
        var schema = await GetRelevantSchemaAsync(p.Query, ct);

        // 2. Generate SQL via LLM
        var prompt = await _prompts.RenderPromptAsync("analytics", "text-to-sql", new()
        {
            ["Schema"]     = schema,
            ["UserQuery"]  = p.Query,
            ["PropertyId"] = TenantContext.CurrentSiteId.ToString(),
            ["DateContext"] = DateTime.Today.ToString("yyyy-MM-dd")
        });

        var sqlResponse = await _llm.CreateClient(TenantContext)
            .GenerateAsync(prompt, ct);

        var sql = ExtractSql(sqlResponse);

        // 3. Security validation — MANDATORY
        ValidateSqlSecurity(sql, TenantContext.CurrentSiteId);

        // 4. Execute
        var results = await _db.Database
            .SqlQueryRaw<dynamic>(sql)
            .ToListAsync(ct);

        return new ToolResult
        {
            Success = true,
            Data = new { Query = sql, RowCount = results.Count, Results = results.Take(1000) }
        };
    }

    [McpServerTool, Description("Generate daily analytics snapshot for a property")]
    public async Task<ToolResult> GenSnapshot(GenSnapshotParams p, CancellationToken ct)
        => throw new NotImplementedException();
}
```

---

## ReservationMcpServer.cs

```csharp
namespace Diva.Tools.Reservation;

public class ReservationMcpServer : McpToolBase
{
    [McpServerTool, Description("Check slot availability for a date range")]
    public async Task<ToolResult> CheckAvailability(CheckAvailabilityParams p, CancellationToken ct)
    {
        // Call downstream booking API with propagated headers
        var result = await CallDownstreamApiAsync<AvailabilityResult>(
            _bookingClient,
            $"/api/Reservation/availability?siteId={TenantContext.CurrentSiteId}&date={p.Date:yyyy-MM-dd}",
            ct);

        return new ToolResult { Success = true, Data = result };
    }

    [McpServerTool, Description("Book a reservation slot")]
    public async Task<ToolResult> BookReservation(BookReservationParams p, CancellationToken ct)
        => throw new NotImplementedException();
}
```

---

## Text-to-SQL Prompt (prompts/analytics/text-to-sql.v1.txt)

```
You are a SQL expert generating queries for a multi-tenant enterprise database.

## CRITICAL SECURITY REQUIREMENTS
- ALL queries MUST include: WHERE TenantID = {{PropertyId}}
- You can ONLY generate SELECT statements
- No INSERT, UPDATE, DELETE, DROP, ALTER, or TRUNCATE
- No access to system tables or stored procedures

## Database Schema
{{Schema}}

## Date Context
Today is {{DateContext}}. Use this for relative date references.

## User Question
{{UserQuery}}

## Output
Generate a single SQL SELECT statement. Include only the SQL, no explanation.

```sql
SELECT ...
```
```

---

## Service Registration

```csharp
// In Program.cs
builder.Services.AddScoped<McpHeaderPropagator>();
builder.Services.AddScoped<ITenantAwareMcpClient, TenantAwareMcpClient>();
builder.Services.AddScoped<AnalyticsMcpServer>();
builder.Services.AddScoped<ReservationMcpServer>();

// Register MCP servers with SK kernel
kernel.Plugins.AddFromObject(sp.GetRequiredService<AnalyticsMcpServer>(), "Analytics");
kernel.Plugins.AddFromObject(sp.GetRequiredService<ReservationMcpServer>(), "Reservation");
```

---

## Parameter Models

### Analytics

```csharp
namespace Diva.Tools.Analytics.Models;

public record GetMetricBreakdownParams(
    DateOnly StartDate,
    DateOnly EndDate,
    string Metric,           // "Revenue", "Occupancy", "ADR", "RevPAR"
    string? Channel,         // null = all channels
    string? GroupBy);        // "channel", "day", "week", "month"

public record GetYoYParams(
    string Metric,
    DateOnly CurrentPeriodStart,
    DateOnly CurrentPeriodEnd,
    string? Channel);

public record RunQueryParams(
    string Query);           // Natural language question

public record GenSnapshotParams(
    DateOnly Date,
    string[] Metrics);       // Which metrics to include
```

### Reservation

```csharp
namespace Diva.Tools.Reservation.Models;

public record CheckAvailabilityParams(
    DateOnly Date,
    int PartySize,
    string? TimeSlot);       // "breakfast", "lunch", "dinner", null = all

public record BookReservationParams(
    DateOnly Date,
    string TimeSlot,
    int PartySize,
    string GuestName,
    string GuestEmail,
    string? SpecialRequests);
```

---

## Prerequisites for Implementation

Before implementing the domain tool servers, the following backends must be available:

| Backend | Used by | Purpose |
|---------|---------|---------|
| Analytics DB (read-only replica) | `AnalyticsMcpServer` | Revenue, occupancy, ADR data scoped by `TenantID` |
| Booking/PMS API | `ReservationMcpServer` | Availability checks and reservation creation |
| LLM endpoint | `RunQueryTool` (Text-to-SQL) | Natural language → SQL generation |

### Configuration

```json
// appsettings.json additions
{
  "DomainTools": {
    "Analytics": {
      "ConnectionString": "Data Source=analytics-replica.db",
      "MaxQueryRows": 1000,
      "AllowedTables": ["FactRevenue", "FactOccupancy", "DimDate", "DimChannel"],
      "QueryTimeoutSeconds": 30
    },
    "Reservation": {
      "BaseUrl": "https://pms-api.example.com",
      "TimeoutSeconds": 15,
      "RequireSsoToken": true
    }
  }
}
```

---

## Text-to-SQL Security Model

The `RunQueryTool` is the highest-risk tool in the platform. Multiple defence layers are required:

### Layer 1: Prompt engineering
- System prompt enforces SELECT-only, mandatory `WHERE TenantID = {{PropertyId}}`
- Schema is restricted to `AllowedTables` list — no system tables exposed

### Layer 2: Static SQL validation (`ValidateSqlSecurity`)
- Must contain `TenantID = {tenantId}` filter
- DML keywords blocked: `INSERT`, `UPDATE`, `DELETE`, `DROP`, `ALTER`, `TRUNCATE`, `EXEC`, `EXECUTE`
- Semicolon-separated statements blocked (single query only)
- Comments blocked (`--`, `/*`)

### Layer 3: Parameterized execution
- `TenantID` filter is injected as a parameter, not string concatenation
- Query timeout enforced at DB level (`CommandTimeout`)

### Layer 4: Result limiting
- Maximum 1000 rows returned
- Large text columns truncated to 500 chars

```csharp
// Enhanced ValidateSqlSecurity (replaces the basic version in McpToolBase)
protected static void ValidateSqlSecurity(string sql, int tenantId)
{
    if (string.IsNullOrWhiteSpace(sql))
        throw new SecurityException("Empty SQL query");

    // Must be a single statement
    if (sql.Count(c => c == ';') > 1)
        throw new SecurityException("Multiple SQL statements not allowed");

    // Must start with SELECT
    if (!sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
        throw new SecurityException("Only SELECT statements are allowed");

    // Must include tenant filter
    if (!sql.Contains($"TenantID = {tenantId}", StringComparison.OrdinalIgnoreCase) &&
        !sql.Contains("TenantID = @tenantId", StringComparison.OrdinalIgnoreCase))
        throw new SecurityException("Query must include WHERE TenantID filter");

    // Block DML and DDL
    string[] forbidden = ["INSERT ", "UPDATE ", "DELETE ", "DROP ", "ALTER ", "TRUNCATE ",
                          "EXEC ", "EXECUTE ", "CREATE ", "GRANT ", "REVOKE "];
    foreach (var f in forbidden)
    {
        if (sql.Contains(f, StringComparison.OrdinalIgnoreCase))
            throw new SecurityException($"'{f.Trim()}' operations are not allowed");
    }

    // Block comments (potential injection vector)
    if (sql.Contains("--") || sql.Contains("/*"))
        throw new SecurityException("SQL comments not allowed in generated queries");
}
```

---

## MCP Server Registration as AspNetCore Endpoints

Domain tool servers are registered as ASP.NET Core MCP server endpoints using `ModelContextProtocol.AspNetCore`:

```csharp
// In Program.cs — after other endpoint mappings
app.MapMcp("/mcp/analytics", options =>
{
    options.ServerInfo = new() { Name = "diva-analytics", Version = "1.0" };
})
.WithMetadata(new McpServerToolsAttribute(typeof(AnalyticsMcpServer)));

app.MapMcp("/mcp/reservation", options =>
{
    options.ServerInfo = new() { Name = "diva-reservation", Version = "1.0" };
})
.WithMetadata(new McpServerToolsAttribute(typeof(ReservationMcpServer)));
```

Agents connect to these as HTTP/SSE bindings:
```json
{ "name": "analytics", "endpoint": "http://localhost:5062/mcp/analytics", "transport": "http" }
```

---

## Test Plan

### Unit tests (`tests/Diva.Tools.Tests/`)

| Test class | Tests | Description |
|------------|-------|-------------|
| `ValidateSqlSecurityTests.cs` | ~10 | SELECT allowed, DML blocked, missing tenant filter, comments, multi-statement, empty |
| `McpHeaderPropagatorTests.cs` | ~4 | Header extraction from TenantContext, missing context throws |
| `AnalyticsMcpServerTests.cs` | ~6 | GetMetricBreakdown filters, RunQuery security, YoY calculation, GenSnapshot |
| `ReservationMcpServerTests.cs` | ~4 | Availability check, booking creation, header propagation, timeout handling |
| `TenantAwareMcpClientTests.cs` | ~4 | Header injection, logging, error handling |

### Integration tests (when backends available)

1. Run `RunQuery` with a natural language question → verify SQL includes tenant filter
2. Run `GetMetricBreakdown` → verify results scoped to correct tenant
3. Run `CheckAvailability` → verify OAuth token forwarded to booking API
4. Run `BookReservation` → verify booking created with correct tenant context
5. Run `mcp-probe` against analytics MCP server → verify tool list returned

---

## Verification

- [ ] `GetMetricBreakdown` only returns data for `TenantContext.CurrentSiteId`
- [ ] `RunQuery` throws `SecurityException` when SQL lacks TenantID filter
- [ ] `RunQuery` throws `SecurityException` on any DML keyword
- [ ] `RunQuery` blocks SQL comments and multi-statement queries
- [ ] `RunQuery` limits results to `MaxQueryRows` (1000)
- [ ] All MCP tool calls send `Authorization`, `X-Tenant-ID`, `X-Correlation-ID` headers
- [ ] `CheckAvailability` propagates OAuth token to booking service
- [ ] Analytics and Reservation MCP servers register at `/mcp/analytics` and `/mcp/reservation`
- [ ] Agents can connect to domain tools via HTTP/SSE binding
