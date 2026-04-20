# Phase 8: SK Agents, Supervisor & Dynamic Registry

> **Status:** `[x]` Done
> **Depends on:** [phase-04-database.md](phase-04-database.md), [phase-07-sessions.md](phase-07-sessions.md) — Phase 5 + 6 deferred (see decisions.md ADR-011, ADR-012)
> **Blocks:** [phase-13-verification.md](phase-13-verification.md)
> **Project:** `Diva.Agents`
> **Architecture ref:** [arch-supervisor.md](arch-supervisor.md), [arch-overview.md](arch-overview.md)

---

## Goal

Implement agents using Semantic Kernel's `ChatCompletionAgent` and `AgentGroupChat`. Build the Supervisor pipeline and the dynamic agent registry that hot-loads agents from DB.

> ⚠️ SK Agents are experimental. Add `#pragma warning disable SKEXP0110` at project level.

---

## Revised Scope & Approach

> Phase 5 and Phase 6 are deferred. Phase 8 is self-contained with the following adjustments:

| Area | Original Plan | Revised Approach |
|---|---|---|
| SK Kernel | Via `LlmClientFactory` (Phase 9) | Built directly from `LlmOptions` using `IChatClient` (same pattern as `AnthropicAgentRunner`) |
| Static agents | `AnalyticsAgent`, `ReservationAgent` | **Out of scope** — only `DynamicReActAgent` (from DB) needed |
| `ITenantAwarePromptBuilder` | Required dependency | **Optional** (nullable) — falls back to `AgentDefinitionEntity.SystemPrompt` |
| Session service | Planned `ISessionService` | Use existing `AgentSessionService` (already implemented) |
| VerifyStage | Phase 8 | Reserved slot in pipeline — **implemented in Phase 13** |
| Invoke endpoint | Replace existing | **New** `POST /api/supervisor/invoke` alongside existing `POST /api/agents/{id}/invoke` |

### Kernel Construction (no LlmClientFactory needed)

```csharp
// Build SK kernel directly from LlmOptions — same provider split as AnthropicAgentRunner
private Kernel BuildKernel(LlmOptions opts)
{
    var builder = Kernel.CreateBuilder();
    var direct = opts.DirectProvider;

    if (direct.Provider.Equals("Anthropic", StringComparison.OrdinalIgnoreCase))
    {
        builder.AddAnthropicChatCompletion(direct.Model, direct.ApiKey);
    }
    else
    {
        // OpenAI-compatible: LM Studio, Ollama, LiteLLM, Azure OpenAI
        builder.AddOpenAIChatCompletion(
            modelId:  direct.Model,
            apiKey:   string.IsNullOrEmpty(direct.ApiKey) ? "no-key" : direct.ApiKey,
            endpoint: string.IsNullOrEmpty(direct.Endpoint) ? null : new Uri(direct.Endpoint));
    }

    return builder.Build();
}
```

### Supervisor Endpoint (alongside existing agent endpoint)

```
Existing (unchanged):  POST /api/agents/{id}/invoke   → AnthropicAgentRunner (single agent by ID)
New (Phase 8):         POST /api/supervisor/invoke    → SupervisorAgent (multi-agent, capability-routed)
```

The supervisor endpoint is the entry point for complex multi-step queries. Simple direct agent invocations continue to use the existing endpoint.

### Files to Create (revised)

```
src/Diva.Agents/
├── Workers/
│   ├── IWorkerAgent.cs                 ← interface
│   └── BaseReActAgent.cs               ← abstract SK-based agent
├── Supervisor/
│   ├── ISupervisorPipelineStage.cs
│   ├── SupervisorAgent.cs
│   ├── SupervisorState.cs
│   └── Stages/
│       ├── DecomposeStage.cs
│       ├── CapabilityMatchStage.cs
│       ├── DispatchStage.cs            ← accumulates ToolEvidence for Phase 13
│       ├── MonitorStage.cs
│       ├── IntegrateStage.cs
│       ├── VerifyStage.cs              ← placeholder, implemented in Phase 13
│       └── DeliverStage.cs
└── Registry/
    ├── IAgentRegistry.cs
    ├── AgentCapability.cs
    ├── DynamicAgentRegistry.cs         ← hot-loads AgentDefinitionEntity from DB
    └── DynamicReActAgent.cs            ← creates SK agent from DB definition

src/Diva.Host/Controllers/
└── SupervisorController.cs             ← POST /api/supervisor/invoke

NOTE: AnalyticsAgent.cs and ReservationAgent.cs are DEFERRED (domain-specific)
```

---

## BaseReActAgent.cs

```csharp
namespace Diva.Agents.Workers;

#pragma warning disable SKEXP0110

public abstract class BaseReActAgent : IWorkerAgent
{
    private readonly Kernel _kernel;
    private readonly ITenantAwarePromptBuilder? _promptBuilder;  // ← optional (Phase 6 deferred)
    private readonly ILogger _logger;

    protected abstract string AgentType { get; }
    protected abstract string[] GetCapabilities();
    protected abstract string FallbackSystemPrompt { get; }  // ← used when promptBuilder not injected

    public AgentCapability GetCapability() => new()
    {
        AgentId       = $"{AgentType.ToLower()}-agent",
        AgentType     = AgentType,
        Description   = $"{AgentType} specialist agent",
        Capabilities  = GetCapabilities(),
        SupportedTools = [],
        Priority      = 10
    };

    public async Task<AgentResponse> ExecuteAsync(
        AgentRequest request,
        TenantContext tenant,
        ChatHistory? conversationHistory,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        // 1. Build system prompt — use TenantAwarePromptBuilder if available, else fallback
        var systemPrompt = _promptBuilder != null
            ? await _promptBuilder.BuildPromptAsync(
                tenant,
                AgentType,
                promptSection: "react-agent",
                variables: new() { ["TaskDescription"] = request.Query },
                ct)
            : FallbackSystemPrompt;

        // 2. Create SK ChatCompletionAgent with ReAct behavior
        var agent = new ChatCompletionAgent
        {
            Name         = $"{AgentType}Agent",
            Instructions = systemPrompt,
            Kernel       = BuildKernelWithTools(tenant)
        };

        // 3. Set up chat with history
        var chat = new AgentGroupChat(agent)
        {
            ExecutionSettings = new AgentGroupChatSettings
            {
                TerminationStrategy = new TaskCompletionTerminationStrategy { MaximumIterations = 10 }
            }
        };

        if (conversationHistory != null)
            foreach (var msg in conversationHistory)
                chat.AddChatMessage(msg);

        chat.AddChatMessage(new ChatMessageContent(AuthorRole.User, request.Query));

        // 4. Execute (SK handles Think → Act → Observe automatically via FunctionChoiceBehavior.Auto)
        var responses = new List<string>();
        var toolsUsed = new List<string>();

        await foreach (var msg in chat.InvokeAsync(ct))
        {
            responses.Add(msg.Content ?? "");
            // Collect tool invocations from SK metadata
        }

        sw.Stop();

        return new AgentResponse
        {
            Content       = responses.LastOrDefault() ?? "",
            AgentName     = $"{AgentType}Agent",
            ToolsUsed     = toolsUsed,
            ExecutionTime = sw.Elapsed,
            Success       = true
        };
    }

    private Kernel BuildKernelWithTools(TenantContext tenant)
    {
        // Clone kernel and inject tenant-aware MCP tools
        var tenantKernel = _kernel.Clone();
        // Add MCP tools as SK plugins
        // tenantKernel.Plugins.AddFromObject(mcpServer);
        return tenantKernel;
    }
}
```

---

---

## ISupervisorPipelineStage.cs

```csharp
namespace Diva.Agents.Supervisor;

public interface ISupervisorPipelineStage
{
    Task<SupervisorState> ExecuteAsync(SupervisorState state, CancellationToken ct);
}
```

## SupervisorState.cs

```csharp
namespace Diva.Agents.Supervisor;

public sealed class SupervisorState
{
    public string RequestId { get; init; } = Guid.NewGuid().ToString();
    public AgentRequest Request { get; init; } = null!;
    public TenantContext TenantContext { get; init; } = null!;
    public AgentSessionEntity? Session { get; set; }

    // Set by DecomposeStage
    public List<SubTask> SubTasks { get; set; } = [];

    // Set by CapabilityMatchStage
    public List<(SubTask Task, IWorkerAgent Agent)> DispatchPlan { get; set; } = [];

    // Set by DispatchStage / MonitorStage
    public List<AgentResponse> WorkerResults { get; set; } = [];

    // Set by IntegrateStage
    public string IntegratedResult { get; set; } = "";

    // Set by DeliverStage
    public DeliveryChannel DeliveryChannel { get; set; } = DeliveryChannel.Api;
    public bool DeliveryComplete { get; set; }

    public SupervisorStatus Status { get; set; } = SupervisorStatus.Running;
    public string? ErrorMessage { get; set; }
}

public record SubTask(string Description, string[] RequiredCapabilities, int SiteId, int TenantId);
public enum DeliveryChannel { Api, Dashboard, Email, Slack, Teams }
public enum SupervisorStatus { Running, Completed, Failed }
```

---

## SupervisorAgent.cs

```csharp
namespace Diva.Agents.Supervisor;

#pragma warning disable SKEXP0110

public class SupervisorAgent : ISupervisorAgent
{
    private readonly IEnumerable<ISupervisorPipelineStage> _stages;
    private readonly AgentSessionService _sessions;  // ← existing service from Phase 7 (not ISessionService)
    private readonly ILogger<SupervisorAgent> _logger;

    public async Task<AgentResponse> InvokeAsync(
        AgentRequest request,
        TenantContext tenant,
        CancellationToken ct)
    {
        // Load or create session via AgentSessionService.GetOrCreateAsync
        var session = await _sessions.GetOrCreateAsync(request.SessionId, tenant, ct);

        await _sessions.SaveTurnAsync(session.Id, "user", request.Query, null, ct);

        // Validate site access
        if (tenant.CurrentSiteId > 0 && !tenant.CanAccessSite(tenant.CurrentSiteId))
            return new AgentResponse
            {
                Success      = false,
                ErrorMessage = $"Access denied to site {tenant.CurrentSiteId}"
            };

        // Run pipeline
        var state = new SupervisorState
        {
            Request       = request,
            TenantContext = tenant,
            Session       = session
        };

        foreach (var stage in _stages)
        {
            state = await stage.ExecuteAsync(state, ct);
            if (state.Status == SupervisorStatus.Failed)
                break;
        }

        var response = new AgentResponse
        {
            Content   = state.IntegratedResult,
            SessionId = session.Id,
            Success   = state.Status == SupervisorStatus.Completed
        };

        await _sessions.SaveTurnAsync(session.Id, "assistant", response.Content,
            new SessionMessageMetadata { AgentName = "supervisor" }, ct);

        return response;
    }
}
```

---

## DispatchStage.cs (uses SK AgentGroupChat)

```csharp
namespace Diva.Agents.Supervisor.Stages;

#pragma warning disable SKEXP0110

public class DispatchStage : ISupervisorPipelineStage
{
    public async Task<SupervisorState> ExecuteAsync(SupervisorState state, CancellationToken ct)
    {
        var results = new List<AgentResponse>();

        // Execute each dispatched agent (parallel where possible)
        await Parallel.ForEachAsync(state.DispatchPlan, ct, async (plan, innerCt) =>
        {
            var (task, agent) = plan;
            var history = await GetSessionHistoryAsync(state, innerCt);
            var result = await agent.ExecuteAsync(
                new AgentRequest { Query = task.Description },
                state.TenantContext,
                history,
                innerCt);
            lock (results) results.Add(result);
        });

        return state with { WorkerResults = results };
    }
}
```

---

## IAgentRegistry.cs

```csharp
namespace Diva.Agents.Registry;

public interface IAgentRegistry
{
    void Register(IWorkerAgent agent);
    Task<List<IWorkerAgent>> GetAgentsForTenantAsync(int tenantId, CancellationToken ct);
    Task<IWorkerAgent?> FindBestMatchAsync(string[] requiredCapabilities, int tenantId, CancellationToken ct);
}

public record AgentCapability
{
    public string AgentId { get; init; } = "";
    public string AgentType { get; init; } = "";
    public string Description { get; init; } = "";
    public string[] Capabilities { get; init; } = [];
    public string[] SupportedTools { get; init; } = [];
    public int Priority { get; init; } = 10;
}
```

---

## DynamicAgentRegistry.cs

Hot-loads agent definitions from DB and creates `DynamicReActAgent` instances:

```csharp
namespace Diva.Agents.Registry;

public class DynamicAgentRegistry : IAgentRegistry
{
    private readonly IDatabaseProviderFactory _db;  // ← not DivaDbContext directly (singleton safe)
    private readonly IServiceProvider _services;
    private readonly ConcurrentDictionary<string, IWorkerAgent> _staticAgents = new();

    public void Register(IWorkerAgent agent)
        => _staticAgents[agent.GetCapability().AgentId] = agent;

    public async Task<List<IWorkerAgent>> GetAgentsForTenantAsync(int tenantId, CancellationToken ct)
    {
        // 1. Static agents (registered in code)
        var agents = _staticAgents.Values.ToList();

        // 2. Dynamic agents from DB (tenant-specific)
        var definitions = await _db.AgentDefinitions
            .Where(d => d.TenantId == tenantId && d.IsEnabled && d.Status == "Published")
            .ToListAsync(ct);

        foreach (var def in definitions)
            agents.Add(new DynamicReActAgent(def, _services));

        return agents;
    }

    public async Task<IWorkerAgent?> FindBestMatchAsync(
        string[] requiredCapabilities, int tenantId, CancellationToken ct)
    {
        var agents = await GetAgentsForTenantAsync(tenantId, ct);

        return agents
            .Select(a => (Agent: a, Score: a.GetCapability().Capabilities
                .Intersect(requiredCapabilities, StringComparer.OrdinalIgnoreCase)
                .Count()))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Agent.GetCapability().Priority)
            .Select(x => x.Agent)
            .FirstOrDefault();
    }
}
```

---

## DynamicReActAgent.cs

Creates SK agents from DB definitions at runtime:

```csharp
namespace Diva.Agents.Registry;

#pragma warning disable SKEXP0110

public class DynamicReActAgent : IWorkerAgent
{
    private readonly AgentDefinitionEntity _definition;
    private readonly IServiceProvider _services;

    public AgentCapability GetCapability()
    {
        var caps = JsonSerializer.Deserialize<string[]>(_definition.Capabilities ?? "[]") ?? [];
        return new AgentCapability
        {
            AgentId      = _definition.Id,
            AgentType    = _definition.AgentType,
            Description  = _definition.Description,
            Capabilities = caps,
            Priority     = 5  // Dynamic agents have lower priority than static
        };
    }

    public async Task<AgentResponse> ExecuteAsync(
        AgentRequest request, TenantContext tenant,
        ChatHistory? history, CancellationToken ct)
    {
        var kernel = _services.GetRequiredService<Kernel>();
        // ITenantAwarePromptBuilder is optional — Phase 6 deferred
        var promptBuilder = _services.GetService<ITenantAwarePromptBuilder>();

        // Use DB-stored system prompt first; fall back to TenantAwarePromptBuilder if available
        var systemPrompt = _definition.SystemPrompt
            ?? (promptBuilder != null
                ? await promptBuilder.BuildPromptAsync(tenant, _definition.AgentType, "react-agent", [], ct)
                : $"You are a {_definition.AgentType} agent. Answer the user's question.");

        var agent = new ChatCompletionAgent
        {
            Name         = _definition.Name,
            Instructions = systemPrompt,
            Kernel       = kernel
        };

        // Execute with SK ... (same as BaseReActAgent)
        throw new NotImplementedException("Execute SK agent from DynamicReActAgent");
    }
}
```

---

## Service Registration

```csharp
// Phase 8 service registrations (Phase 5/6 services NOT required)
builder.Services.AddSingleton<IAgentRegistry, DynamicAgentRegistry>();
builder.Services.AddSingleton<ISupervisorAgent, SupervisorAgent>();

// Pipeline stages (ordered)
builder.Services.AddScoped<ISupervisorPipelineStage, DecomposeStage>();
builder.Services.AddScoped<ISupervisorPipelineStage, CapabilityMatchStage>();
builder.Services.AddScoped<ISupervisorPipelineStage, DispatchStage>();
builder.Services.AddScoped<ISupervisorPipelineStage, MonitorStage>();
builder.Services.AddScoped<ISupervisorPipelineStage, IntegrateStage>();
builder.Services.AddScoped<ISupervisorPipelineStage, VerifyStage>();   // Phase 13 — placeholder only
builder.Services.AddScoped<ISupervisorPipelineStage, DeliverStage>();

// NOTE: AnalyticsAgent, ReservationAgent, ILlmClientFactory, LiteLLMClient NOT registered here
// NOTE: AgentSessionService already registered (Phase 7)
```

---

## Adding New Agents (Future)

```csharp
// 1. Create agent class (3 lines)
public class WeatherAgent : BaseReActAgent
{
    protected override string AgentType => "Weather";
    protected override string[] GetCapabilities() =>
        ["weather", "forecast", "conditions", "rain-delay"];
}

// 2. Create MCP tool server (in Diva.Tools)
public class WeatherMcpServer : McpToolBase { ... }

// 3. Register in DI — Supervisor auto-discovers it
services.AddScoped<IWorkerAgent, WeatherAgent>();
```

---

## Verification

- [x] `DynamicReActAgent` is created from DB `AgentDefinitionEntity` and executes successfully
- [x] `SupervisorAgent` routes a query to the correct `DynamicReActAgent` via capability matching
- [x] Dynamic agent from DB is discovered and used when capability matches
- [x] Session history is passed to agents via `AgentSessionService` (multi-turn works)
- [x] `DynamicAgentRegistry.FindBestMatchAsync` returns highest-scoring agent
- [x] Parallel dispatch: two sub-tasks run concurrently (via `Parallel.ForEachAsync`)
- [x] `ITenantAwarePromptBuilder` is optional — agent falls back to `AgentDefinitionEntity.SystemPrompt` when not injected
- [x] `POST /api/supervisor/invoke` is reachable and returns a valid `AgentResponse`
- [x] Existing `POST /api/agents/{id}/invoke` continues to work unchanged
- [x] `VerifyStage` is registered in pipeline but acts as a placeholder (no-op until Phase 13)

---

## As Built — Deviations from Plan

| Area | Plan | Actual |
|---|---|---|
| `IWorkerAgent.ExecuteAsync` | Takes `ChatHistory?` param | No `ChatHistory?` — supervisor owns session; workers use `AgentSessionService` internally |
| `DynamicReActAgent` | Uses SK `ChatCompletionAgent` directly | Delegates to `AnthropicAgentRunner` (avoids duplicating provider split logic) |
| `DecomposeStage` | LLM-based query decomposition | Single sub-task for MVP; `PreferredAgent` hint respected via capabilities |
| `CapabilityMatchStage` | Capability-only fallback = fail | Fallback to highest-priority agent when no capability match (avoids "no agents" errors for simple deployments) |
| `SupervisorAgent` | Singleton (stage deps scoped) | All services Singleton — stages have no scoped deps |
| `BaseReActAgent` | Abstract class for static agents | Not created — static agents deferred; `DynamicReActAgent` is the only worker |

**Key files created:**
```
src/Diva.Agents/
├── Workers/
│   ├── IWorkerAgent.cs            ✓
│   └── AgentCapability.cs         ✓
├── Supervisor/
│   ├── ISupervisorPipelineStage.cs ✓
│   ├── ISupervisorAgent.cs         ✓
│   ├── SupervisorState.cs          ✓
│   ├── SupervisorAgent.cs          ✓
│   └── Stages/
│       ├── DecomposeStage.cs       ✓
│       ├── CapabilityMatchStage.cs ✓
│       ├── DispatchStage.cs        ✓
│       ├── MonitorStage.cs         ✓
│       ├── IntegrateStage.cs       ✓
│       ├── VerifyStage.cs          ✓ (no-op placeholder)
│       └── DeliverStage.cs         ✓
└── Registry/
    ├── IAgentRegistry.cs           ✓
    ├── DynamicAgentRegistry.cs     ✓ (hot-loads Published agents from DB)
    └── DynamicReActAgent.cs        ✓ (wraps AnthropicAgentRunner)

src/Diva.Host/Controllers/
└── SupervisorController.cs         ✓ POST /api/supervisor/invoke
```
