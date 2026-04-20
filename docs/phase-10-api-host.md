# Phase 10: API Host, Controllers, SignalR & Observability

> **Status:** `[~]` In Progress — `Program.cs` + `AgentsController` done; SignalR hub, AdminController, full observability pending
> **Depends on:** [phase-08-agents.md](phase-08-agents.md), [phase-09-llm-client.md](phase-09-llm-client.md)
> **Blocks:** [phase-11-rule-learning.md](phase-11-rule-learning.md), [phase-12-admin-portal.md](phase-12-admin-portal.md)
> **Project:** `Diva.Host`

---

## Goal

Wire everything together in the ASP.NET Core host. Expose REST endpoints for agent invocation and admin management, SignalR hub for streaming, health checks for Kubernetes, and full observability (Serilog, Prometheus, OpenTelemetry).

---

## Files to Create

```
src/Diva.Host/
├── Program.cs
├── Controllers/
│   ├── AgentController.cs
│   ├── AdminController.cs
│   └── HealthController.cs
└── Hubs/
    └── AgentStreamHub.cs
```

---

## Program.cs (full service wiring)

```csharp
using Diva.Infrastructure.Auth;
using Diva.Infrastructure.Data;
using Diva.Infrastructure.Sessions;
using Diva.Infrastructure.LiteLLM;
using Diva.TenantAdmin.Services;
using Diva.TenantAdmin.Prompts;
using Diva.Agents.Workers;
using Diva.Agents.Supervisor;
using Diva.Agents.Registry;
using Diva.Tools.Core;
using Diva.Tools.Analytics;
using Diva.Tools.Reservation;
using Diva.Host.Hubs;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration ────────────────────────────────────────────
builder.Services.Configure<OAuthOptions>(
    builder.Configuration.GetSection(OAuthOptions.SectionName));
builder.Services.Configure<DatabaseOptions>(
    builder.Configuration.GetSection(DatabaseOptions.SectionName));
builder.Services.Configure<LlmOptions>(
    builder.Configuration.GetSection(LlmOptions.SectionName));
builder.Services.Configure<AgentOptions>(
    builder.Configuration.GetSection(AgentOptions.SectionName));

// ── Auth ─────────────────────────────────────────────────────
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IOAuthTokenValidator, OAuthTokenValidator>();
builder.Services.AddScoped<ITenantClaimsExtractor, TenantClaimsExtractor>();
builder.Services.AddTransient<HeaderPropagationHandler>();

// JWT bearer (for Swagger UI / direct API testing)
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["OAuth:Authority"];
        options.Audience  = builder.Configuration["OAuth:Audience"];
        options.SaveToken = true;  // Needed for BootstrapContext
    });
builder.Services.AddAuthorization();

// ── Database ─────────────────────────────────────────────────
builder.Services.AddSingleton<IDatabaseProviderFactory, DatabaseProviderFactory>();
builder.Services.AddScoped(sp =>
{
    var factory = sp.GetRequiredService<IDatabaseProviderFactory>();
    var ctx = sp.GetService<IHttpContextAccessor>()?.HttpContext;
    return factory.CreateDbContext(ctx?.Items["TenantContext"] as TenantContext);
});

// ── LLM ──────────────────────────────────────────────────────
builder.Services.AddSingleton<ILlmClientFactory, LlmClientFactory>();
builder.Services.AddHttpClient<LiteLLMClient>();
builder.Services.AddScoped<Kernel>(sp =>
{
    var factory = sp.GetRequiredService<ILlmClientFactory>();
    var ctx = sp.GetService<IHttpContextAccessor>()?.HttpContext;
    var tenant = ctx?.Items["TenantContext"] as TenantContext ?? new TenantContext();
    return factory.CreateKernel(tenant);
});

// ── Tenant Admin ──────────────────────────────────────────────
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ITenantBusinessRulesService, TenantBusinessRulesService>();
builder.Services.AddScoped<ITenantAwarePromptBuilder, TenantAwarePromptBuilder>();
builder.Services.AddSingleton<PromptTemplateStore>();

// ── MCP Tools ────────────────────────────────────────────────
builder.Services.AddScoped<McpHeaderPropagator>();
builder.Services.AddScoped<ITenantAwareMcpClient, TenantAwareMcpClient>();
builder.Services.AddScoped<AnalyticsMcpServer>();
builder.Services.AddScoped<ReservationMcpServer>();

// ── Sessions ─────────────────────────────────────────────────
builder.Services.AddScoped<ISessionService, SessionService>();

// ── Agents ───────────────────────────────────────────────────
builder.Services.AddScoped<AnalyticsAgent>();
builder.Services.AddScoped<ReservationAgent>();
builder.Services.AddScoped<ISupervisorAgent, SupervisorAgent>();

// Pipeline stages (ordered — IEnumerable<ISupervisorPipelineStage> preserves order)
builder.Services.AddScoped<ISupervisorPipelineStage, DecomposeStage>();
builder.Services.AddScoped<ISupervisorPipelineStage, CapabilityMatchStage>();
builder.Services.AddScoped<ISupervisorPipelineStage, DispatchStage>();
builder.Services.AddScoped<ISupervisorPipelineStage, MonitorStage>();
builder.Services.AddScoped<ISupervisorPipelineStage, IntegrateStage>();
builder.Services.AddScoped<ISupervisorPipelineStage, DeliverStage>();

builder.Services.AddSingleton<IAgentRegistry, DynamicAgentRegistry>(sp =>
{
    var registry = ActivatorUtilities.CreateInstance<DynamicAgentRegistry>(sp);
    registry.Register(sp.GetRequiredService<AnalyticsAgent>());
    registry.Register(sp.GetRequiredService<ReservationAgent>());
    return registry;
});

// ── Observability ─────────────────────────────────────────────
builder.Host.UseSerilog((ctx, config) => config
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "Diva")
    .WriteTo.Console(new JsonFormatter())
    .WriteTo.Seq(ctx.Configuration["Seq:ServerUrl"] ?? "http://seq:5341"));

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("Diva"))
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSqlClientInstrumentation()
        .AddSource("Diva.Agents")
        .AddOtlpExporter(opts => opts.Endpoint = new Uri(
            builder.Configuration["OTel:Endpoint"] ?? "http://otel-collector:4317")));

// ── Health Checks ─────────────────────────────────────────────
var hcBuilder = builder.Services.AddHealthChecks();
if (builder.Configuration["Database:Provider"] == "SqlServer")
    hcBuilder.AddSqlServer(builder.Configuration["Database:SqlServer:ConnectionString"]!);
hcBuilder
    .AddCheck<AgentRegistryHealthCheck>("agent-registry")
    .AddCheck<McpServerHealthCheck>("mcp-server");

// ── Controllers + SignalR ─────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ── CORS (for admin portal) ───────────────────────────────────
builder.Services.AddCors(options =>
    options.AddPolicy("AdminPortal", policy =>
        policy.WithOrigins("http://localhost:3000")
              .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();

// ── Middleware Pipeline ───────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AdminPortal");
app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseMiddleware<TenantContextMiddleware>();   // ← Custom: validates JWT + builds TenantContext
app.UseAuthorization();

// ── Endpoints ─────────────────────────────────────────────────
app.MapControllers();
app.MapHub<AgentStreamHub>("/hubs/agent");

app.MapHealthChecks("/health/live",  new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions());

// ── DB Migration (auto on startup) ───────────────────────────
using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDatabaseProviderFactory>();
    await factory.ApplyMigrationsAsync();
}

app.Run();
```

---

## AgentController.cs

```csharp
namespace Diva.Host.Controllers;

[ApiController]
[Route("api/agent")]
public class AgentController : ControllerBase
{
    private readonly ISupervisorAgent _supervisor;

    // POST /api/agent/invoke
    [HttpPost("invoke")]
    public async Task<IActionResult> Invoke([FromBody] AgentRequest request, CancellationToken ct)
    {
        var tenant = HttpContext.GetTenantContext();
        var result = await _supervisor.InvokeAsync(request, tenant, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    // POST /api/agent/invoke/stream — Server-Sent Events
    [HttpPost("invoke/stream")]
    public async Task StreamInvoke([FromBody] AgentRequest request, CancellationToken ct)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";

        var tenant = HttpContext.GetTenantContext();

        await foreach (var chunk in _supervisor.InvokeStreamAsync(request, tenant, ct))
        {
            var data = JsonSerializer.Serialize(chunk);
            await Response.WriteAsync($"data: {data}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
    }
}
```

---

## AdminController.cs

```csharp
namespace Diva.Host.Controllers;

[ApiController]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly ITenantBusinessRulesService _rules;
    private readonly IAgentRegistry _registry;

    // ── Business Rules ───────────────────────────────────────

    [HttpGet("business-rules/{tenantId}")]
    public async Task<IActionResult> GetRules(int tenantId, CancellationToken ct)
        => Ok(await _rules.GetRulesAsync(tenantId, "*", ct));

    [HttpPost("business-rules/{tenantId}")]
    public async Task<IActionResult> CreateRule(int tenantId, [FromBody] TenantBusinessRule rule, CancellationToken ct)
    {
        var created = await _rules.CreateRuleAsync(tenantId, rule, ct);
        return Created($"/api/admin/business-rules/{tenantId}/{created.Id}", created);
    }

    [HttpPut("business-rules/{tenantId}/{ruleId}")]
    public async Task<IActionResult> UpdateRule(int tenantId, int ruleId, [FromBody] TenantBusinessRule rule, CancellationToken ct)
    {
        await _rules.UpdateRuleAsync(tenantId, ruleId, rule, ct);
        await _rules.InvalidateCacheAsync(tenantId, "*");
        return NoContent();
    }

    // ── Prompt Overrides ─────────────────────────────────────

    [HttpGet("prompts/{tenantId}")]
    public async Task<IActionResult> GetPromptOverrides(int tenantId, CancellationToken ct)
        => Ok(await _rules.GetPromptOverridesAsync(tenantId, "*", "*", ct));

    [HttpPost("prompts/{tenantId}")]
    public async Task<IActionResult> CreatePromptOverride(int tenantId, [FromBody] TenantPromptOverride o, CancellationToken ct)
        => throw new NotImplementedException();

    // ── Agent Definitions (Dynamic Agents) ───────────────────

    [HttpGet("agents/{tenantId}")]
    public async Task<IActionResult> GetAgents(int tenantId, CancellationToken ct)
        => Ok(await _registry.GetAgentsForTenantAsync(tenantId, ct));

    [HttpPost("agents/{tenantId}")]
    public async Task<IActionResult> CreateAgent(int tenantId, [FromBody] AgentDefinitionEntity def, CancellationToken ct)
        => throw new NotImplementedException();

    // ── Learned Rules (from Phase 11) ────────────────────────

    [HttpGet("learned-rules/{tenantId}")]
    public async Task<IActionResult> GetPendingRules(int tenantId, CancellationToken ct)
        => throw new NotImplementedException();   // Wired in Phase 11

    [HttpPost("learned-rules/{tenantId}/{ruleId}/approve")]
    public async Task<IActionResult> ApproveRule(int tenantId, int ruleId, CancellationToken ct)
        => throw new NotImplementedException();
}
```

---

## AgentStreamHub.cs

```csharp
namespace Diva.Host.Hubs;

[Authorize]
public class AgentStreamHub : Hub
{
    private readonly ISupervisorAgent _supervisor;

    public async Task JoinTenantGroup(int siteId)
    {
        var tenant = Context.GetHttpContext()?.GetTenantContext();
        if (tenant != null && tenant.CanAccessSite(siteId))
            await Groups.AddToGroupAsync(Context.ConnectionId,
                $"tenant_{tenant.TenantId}_site_{siteId}");
    }

    // Push result to all clients in tenant+site group (called from DeliverStage)
    public static async Task PushResultAsync(
        IHubContext<AgentStreamHub> hub,
        int tenantId, int siteId, object result)
    {
        await hub.Clients
            .Group($"tenant_{tenantId}_site_{siteId}")
            .SendAsync("AgentResponse", result);
    }
}
```

---

## Trigger Sources

### Scheduled (Temporal)

```csharp
[Workflow]
public class DailySnapshotWorkflow
{
    [WorkflowRun]
    public async Task RunAsync(DailySnapshotInput input)
    {
        foreach (var siteId in input.SiteIds)
        {
            await Workflow.ExecuteActivityAsync(
                (AgentActivities a) => a.InvokeSupervisorAsync(new AgentRequest
                {
                    Query       = $"Generate daily analytics snapshot for site {siteId}",
                    TriggerType = "scheduled"
                }));
        }
    }
}
// Schedule: "0 7 * * *" (7:00 AM daily per tenant)
```

### Event-Driven (Kafka)

```csharp
public class AgentEventConsumer : IHostedService
{
    public async Task ProcessEventAsync(AgentEvent evt)
    {
        var query = evt.Type switch
        {
            "booking.confirmed" => $"Process booking confirmation {evt.Payload.BookingId}",
            "order.completed"   => $"Process F&B order {evt.Payload.OrderId}",
            "noshow.detected"   => $"Handle no-show for {evt.Payload.BookingId}",
            _                   => null
        };

        if (query != null)
            await _supervisor.InvokeAsync(new AgentRequest { Query = query, TriggerType = "event" }, ...);
    }
}
```

---

## Observability: Metrics

```csharp
public static class AgentMetrics
{
    public static readonly Counter TasksTotal = Metrics.CreateCounter(
        "agent_tasks_total", "Total agent tasks",
        labelNames: ["agent", "tenant", "site", "status"]);

    public static readonly Histogram TaskDuration = Metrics.CreateHistogram(
        "agent_task_duration_seconds", "Task duration",
        labelNames: ["agent", "tenant"],
        buckets: [0.1, 0.5, 1, 2, 5, 10, 30, 60]);

    public static readonly Counter LlmTokensUsed = Metrics.CreateCounter(
        "llm_tokens_total", "LLM tokens used",
        labelNames: ["tenant", "agent", "model"]);
}
```

---

## Verification

- [ ] `POST /api/agent/invoke` with valid JWT → returns agent response
- [ ] `POST /api/agent/invoke` with missing JWT → 401
- [ ] `POST /api/agent/invoke/stream` streams `text/event-stream` chunks
- [x] `GET /health/live` returns 200 without auth
- [x] `GET /health/ready` returns 200 when DB connected
- [ ] SignalR: client joining `tenant_1_site_1` group receives pushed results
- [x] Swagger UI accessible at `/swagger` in Development
- [ ] Prometheus metrics available at `/metrics`
- [ ] Serilog logs structured JSON with TenantId, SiteId, CorrelationId enriched

---

## As Built — Deviations from Plan

**What was implemented:**

| Area | Plan | Actual |
|---|---|---|
| Agent invocation route | `POST /api/agent/invoke` (supervisor-routed) | `POST /api/agents/{id}/invoke` (direct agent by ID) |
| Controller name | `AgentController` | `AgentsController` — full CRUD for `AgentDefinitionEntity` + invoke |
| Auth | JWT `[Authorize]` required | **No auth on invoke yet** — `TenantContext.System(tenantId: 1)` used as placeholder |
| Session service | `ISessionService` (scoped) | `AgentSessionService` (singleton) |
| Observability | Serilog + OpenTelemetry + Prometheus | Basic `ILogger` + minimal health checks only |
| CORS origin | `http://localhost:3000` hardcoded | Configurable via `AdminPortal:CorsOrigin` in appsettings |
| Auto-migrate | `factory.ApplyMigrationsAsync()` | `db.Database.MigrateAsync()` via `DivaDbContext` directly |

**`AgentsController` — actually implemented (not in original plan):**
```
GET    /api/agents           → list all agents
GET    /api/agents/{id}      → get agent by ID
POST   /api/agents           → create agent
PUT    /api/agents/{id}      → update agent
DELETE /api/agents/{id}      → delete agent
POST   /api/agents/{id}/invoke → invoke agent with { query, sessionId }
```

**`Program.cs` — actual registrations (simplified from plan):**
```csharp
builder.Services.Configure<LlmOptions>(...);
builder.Services.Configure<DatabaseOptions>(...);
builder.Services.AddDbContext<DivaDbContext>(...);          // scoped (for controllers)
builder.Services.AddSingleton<IDatabaseProviderFactory, DatabaseProviderFactory>();
builder.Services.AddSingleton<AgentSessionService>();
builder.Services.AddSingleton<AnthropicAgentRunner>();
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddHealthChecks();
// CORS from config, Swagger, auto-migrate on startup
```

**Still pending from original plan:**
- `AdminController` (business rules, prompt overrides, learned rules)
- `AgentStreamHub` (SignalR)
- `AgentController` (supervisor-routed invoke + SSE stream)
- Serilog structured logging
- OpenTelemetry tracing
- Prometheus metrics
- `TenantContextMiddleware` (auth is bypassed for now)
