# Phase 14: A2A Protocol Support

> **Status:** `[x]` Complete
> **Completed:** 2026-04-12. Auth fixes + multi-agent listing added 2026-04-13.
> **Depends on:** [phase-08-agents.md](phase-08-agents.md), [phase-09-llm-client.md](phase-09-llm-client.md), [phase-10-api-host.md](phase-10-api-host.md)
> **Blocks:** Nothing (additive — layered on top of existing agent execution)
> **Projects:** `Diva.Core`, `Diva.Infrastructure`, `Diva.Host`
> **Architecture ref:** [arch-supervisor.md](arch-supervisor.md)

---

## Goal

Implement [A2A (Agent-to-Agent) protocol](https://google.github.io/A2A/) support to expose Diva agents as interoperable endpoints consumable by external orchestrators (LangChain, AutoGen, Google ADK, any A2A-compliant client), and to enable the Supervisor to delegate sub-tasks to remote agents over HTTP.

---

## Architecture Assessment — Current vs A2A

| A2A Concept | Diva Equivalent | Status |
|---|---|---|
| AgentCard (`/.well-known/agent.json`) | `AgentCapability` record (in-memory) | ❌ No HTTP endpoint |
| Task input | `AgentRequest` | ✅ Aligned |
| Task output | `AgentResponse` | ✅ Aligned |
| Task ID / async polling | `SessionId` (session, not task) | ❌ No persistent task ID |
| AgentExecutor interface | `IWorkerAgent.ExecuteAsync()` | ✅ Aligned |
| SSE streaming | `/invoke/stream` → `AgentStreamChunk` | ✅ (custom event format) |
| Agent discovery (local) | `DynamicAgentRegistry` | ✅ |
| Multi-agent orchestration | `SupervisorAgent` 7-stage pipeline | ✅ (in-process only) |
| A2A client (remote delegation) | None | ❌ Supervisor calls local workers only |
| Authentication negotiation | TenantContext in DI | ❌ No credential exchange for agent-to-agent calls |

**~60% aligned.** Core execution, streaming, and orchestration are in place. Missing: outward-facing A2A surface and remote federation.

---

## Files to Create / Modify

```
src/
├── Diva.Core/
│   └── Configuration/
│       └── A2AOptions.cs                               ← CREATE
│
├── Diva.Infrastructure/
│   ├── A2A/
│   │   ├── AgentCardBuilder.cs                         ← CREATE
│   │   ├── IA2AAgentClient.cs                          ← CREATE (Tier B)
│   │   └── A2AAgentClient.cs                           ← CREATE (Tier B)
│   └── Data/
│       └── Entities/
│           └── AgentTaskEntity.cs                      ← CREATE
│
└── Diva.Host/
    └── Controllers/
        ├── AgentCardController.cs                      ← CREATE
        └── AgentTaskController.cs                      ← CREATE

src/Diva.Infrastructure/Data/DivaDbContext.cs           ← MODIFY (add AgentTasks DbSet)
src/Diva.Infrastructure/Data/Entities/AgentDefinitionEntity.cs ← MODIFY (add A2AEndpoint, A2AAuthScheme — Tier B)
src/Diva.Agents/Supervisor/Stages/DispatchStage.cs      ← MODIFY (route to A2AAgentClient — Tier B)
src/Diva.Agents/Registry/DynamicAgentRegistry.cs        ← MODIFY (return remote agents — Tier B)
src/Diva.Host/Program.cs                               ← MODIFY (register A2A services, map well-known route)
src/Diva.Host/appsettings.json                         ← MODIFY (add A2A section)
```

---

## Step 1 — Configuration

### `Diva.Core/Configuration/A2AOptions.cs`

```csharp
namespace Diva.Core.Configuration;

public sealed class A2AOptions
{
    public const string SectionName = "A2A";

    public bool Enabled { get; init; } = false;

    /// <summary>Seconds before a running task is automatically failed.</summary>
    public int TaskTimeoutSeconds { get; init; } = 300;
}
```

### `appsettings.json` addition

```json
"A2A": {
  "Enabled": false,
  "TaskTimeoutSeconds": 300
}
```

---

## Step 2 — Task Persistence

### `Diva.Infrastructure/Data/Entities/AgentTaskEntity.cs`

```csharp
namespace Diva.Infrastructure.Data.Entities;

public sealed class AgentTaskEntity : ITenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();  // string, not Guid — matches AgentDefinitionEntity.Id pattern (Gap #25)
    public int TenantId { get; set; }

    public string AgentId { get; set; } = "";  // string — matches AgentDefinitionEntity.Id

    /// <summary>pending | working | completed | failed | canceled</summary>
    public string Status { get; set; } = "pending";

    public string? InputJson { get; set; }         // A2A task input (serialized)
    public string? OutputText { get; set; }        // Final response text
    public string? ErrorMessage { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public string? SessionId { get; set; }         // Diva session linked to this task
}
```

Add `DbSet<AgentTaskEntity> AgentTasks` to `DivaDbContext`. Run EF migration.

---

## Step 3 — AgentCard Builder

### `Diva.Infrastructure/A2A/AgentCardBuilder.cs`

Builds the A2A `AgentCard` JSON from `AgentDefinitionEntity`:

```json
{
  "name": "Analytics Agent",
  "description": "...",
  "url": "https://your-host/tasks/send",
  "version": "1.0",
  "capabilities": {
    "streaming": true,
    "pushNotifications": false
  },
  "skills": [
    { "id": "analytics", "name": "Analytics", "description": "..." }
  ],
  "authentication": {
    "schemes": ["Bearer"]
  }
}
```

---

## Step 4 — A2A Server Endpoints (Tier A)

### `AgentCardController.cs` — `GET /.well-known/agent.json`

- Returns `AgentCard` for the default published agent (or query `?agentId=` for specific)
- Guarded by `A2AOptions.Enabled`

### `AgentTaskController.cs`

#### `POST /tasks/send`
- Body: A2A task object (`{ id, message: { role, parts: [{ text }] }, sessionId? }`)
- Maps to `AgentRequest`, dispatches via `AnthropicAgentRunner.InvokeStreamAsync`
- Creates `AgentTaskEntity` (status = working)
- Returns SSE stream of `TaskStatusUpdateEvent` / `TaskArtifactUpdateEvent`

**Stream event mapping (Diva → A2A):**

| Diva `AgentStreamChunk.Type` | A2A Event |
|---|---|
| `plan`, `thinking`, `iteration_start` | `TaskStatusUpdateEvent { state: "working", message: ... }` |
| `tool_call`, `tool_result` | `TaskArtifactUpdateEvent { type: "tool_trace", data: ... }` |
| `final_response` | `TaskArtifactUpdateEvent { type: "text", data: content }` |
| `verification` | `TaskArtifactUpdateEvent { type: "verification", data: ... }` |
| `done` | `TaskStatusUpdateEvent { state: "completed" }` |
| `error` | `TaskStatusUpdateEvent { state: "failed", message: error }` |

#### `GET /tasks/{taskId}`
- Returns current task status and output from `AgentTaskEntity`

#### `DELETE /tasks/{taskId}`
- Sets task status to `canceled`, signals cancellation token if task is still running

---

## Step 5 — A2A Client (Tier B — remote agent delegation)

### `IA2AAgentClient.cs`

```csharp
public interface IA2AAgentClient
{
    Task<AgentCard> DiscoverAsync(string agentUrl, CancellationToken ct);
    IAsyncEnumerable<AgentStreamChunk> SendTaskAsync(
        string agentUrl, string apiKey, AgentRequest request, CancellationToken ct);
}
```

### `A2AAgentClient.cs`

1. `GET {agentUrl}/.well-known/agent.json` → parse `AgentCard`
2. `POST {card.url}/tasks/send` with Bearer token → stream SSE response
3. Translate A2A `TaskStatusUpdateEvent` / `TaskArtifactUpdateEvent` back to `AgentStreamChunk`

### `AgentDefinitionEntity` additions (DB migration required)

```csharp
/// <summary>If set, agent is remote — calls via A2A at this URL.</summary>
public string? A2AEndpoint { get; set; }

/// <summary>Bearer | ApiKey</summary>
public string? A2AAuthScheme { get; set; }

/// <summary>Secret stored encrypted. Resolved at runtime.</summary>
public string? A2ASecretRef { get; set; }
```

### `DispatchStage` modification

```csharp
// If matched agent has A2AEndpoint, route to A2AAgentClient instead of IWorkerAgent
if (!string.IsNullOrEmpty(agent.A2AEndpoint))
{
    // Delegate via HTTP using A2AAgentClient
}
else
{
    // Existing in-process dispatch
}
```

---

## Step 6 — DI Registration

```csharp
builder.Services.Configure<A2AOptions>(builder.Configuration.GetSection(A2AOptions.SectionName));

// Tier A
builder.Services.AddScoped<AgentCardBuilder>();

// Tier B
builder.Services.AddHttpClient<IA2AAgentClient, A2AAgentClient>();
```

---

## Verification Checklist

- [x] `GET /.well-known/agent.json` returns valid A2A `AgentCard` for a published agent (no auth required)
- [x] `GET /.well-known/agent.json?agentId={id}` returns card for a specific agent
- [x] `GET /.well-known/agents.json` returns array of all published agents (added 2026-04-13)
- [x] `POST /tasks/send` accepts A2A task, streams `TaskStatusUpdateEvent` + `TaskArtifactUpdateEvent`
- [x] `GET /tasks/{id}` returns task status after completion
- [x] `DELETE /tasks/{id}` cancels in-flight task and returns `canceled` status
- [x] `A2A.Enabled: false` (default) — all A2A endpoints return 404
- [x] Supervisor routes to remote agent via `A2AAgentClient` when `A2AEndpoint` is set
- [x] `TenantContextMiddleware` bypasses `/.well-known` — no 401 on agent discovery
- [x] Build passes: `dotnet build Diva.slnx`

### Post-completion fixes (2026-04-13)

| Issue | Fix |
|-------|-----|
| `/.well-known/agent.json` returned 401 | `TenantContextMiddleware` lacked `/.well-known` bypass; `AgentCardController` lacked `[AllowAnonymous]` |
| Only one agent discoverable | Added `GET /.well-known/agents.json` returning all published agents as an array |
