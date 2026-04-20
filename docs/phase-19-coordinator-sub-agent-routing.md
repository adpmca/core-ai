# Phase 19 — UI-Configurable Coordinator Agent with Explicit Sub-Agent Routing (Local + Remote A2A)

> **Status:** `[ ]` Not Started
> **Depends on:** [phase-08-agents.md](phase-08-agents.md), [phase-14-a2a.md](phase-14-a2a.md) (Agents-as-Tools complete)
> **Blocks:** Nothing
> **Project:** `Diva.Agents`, `Diva.Infrastructure`, `Diva.Host`, `admin-portal`
> **Estimated test count:** ~20 new tests

## Context

The Coordinator archetype exists but uses **implicit global capability matching** across all tenant
agents. There is no way to pin specific sub-agents, exclude unrelated agents, or define a routing
list in the UI. The `DecomposeStage` also only creates one sub-task (MVP).

**Relationship to Agents-as-Tools (Phase 14):** Phase 14 added `DelegateAgentIdsJson` for peer-to-peer
delegation via the tool pipeline — one agent calls another as a tool during its ReAct loop. Phase 19
is different: it creates a **supervisor pipeline** over a scoped agent set, with LLM-based task
decomposition, parallel dispatch, and result integration. The two features are complementary:
- **Agents-as-Tools** = ad-hoc peer delegation within a single agent's ReAct loop
- **Coordinator** = structured multi-agent orchestration with decomposition + integration

This phase adds:
- A `SubAgentIdsJson` field on `AgentDefinitionEntity` (list of pinned agent IDs)
- A new `OrchestratorAgent` worker that invokes the supervisor pipeline with a scoped registry
- A new `ScopedAgentRegistry` wrapping only the configured sub-agents
- LLM-based multi-task decomposition in `DecomposeStage`
- A `SubAgentSelector` UI panel in `AgentBuilder`, visible when archetype = `"coordinator"`

Remote A2A agents are handled transparently — they are registered as `RemoteA2A`-archetype agent
definitions (with `A2AEndpoint` + auth), then selected in the coordinator's sub-agent list just
like any local agent. No new A2A infrastructure is needed.

---

## Architecture

```
Admin configures:
  Coordinator agent  →  SubAgentIds: ["local-agent-A", "remote-a2a-agent-B"]

At runtime:
  User query → OrchestratorAgent.ExecuteAsync()
             → Resolves agents A and B from global registry
             → Creates ScopedAgentRegistry([A, B])
             → Runs SupervisorAgent with scoped registry:
                 DecomposeStage   → LLM decomposes into N sub-tasks
                 CapabilityMatch  → matches each task to A or B
                 DispatchStage    → executes A (local ReAct) + B (remote SSE) in parallel
                 IntegrateStage   → merges results
```

---

## Files to Create

### 1. `src/Diva.Agents/Registry/ScopedAgentRegistry.cs`
Implements `IAgentRegistry` backed by a fixed `List<IWorkerAgent>`.
- `GetAgentsForTenantAsync` → returns the fixed list (ignores tenantId)
- `FindBestMatchAsync` → same capability intersection + priority scoring as `DynamicAgentRegistry`
- `GetByIdAsync` → linear search on fixed list
- `Register` → no-op (scoped registry is read-only)

### 2. `src/Diva.Agents/Workers/OrchestratorAgent.cs`
Implements `IStreamableWorkerAgent`:

```
Constructor(AgentDefinitionEntity def, IAgentRegistry globalRegistry, ISupervisorAgent supervisor)

ExecuteAsync:
  1. Deserialize def.SubAgentIdsJson → string[] subAgentIds
  2. For each id: globalRegistry.GetByIdAsync(id, tenantId) → List<IWorkerAgent>
  3. Build ScopedAgentRegistry(resolved agents)
  4. Invoke supervisor.InvokeWithRegistryAsync(request, tenant, scopedRegistry, ct)
  5. Return AgentResponse

InvokeStreamAsync: same, yields AgentStreamChunks from supervisor SSE output
```

### 3. EF Migration
`src/Diva.Infrastructure/Data/Migrations/<timestamp>_Phase19_CoordinatorSubAgentRouting.cs`

Adds `SubAgentIds TEXT NULL` column to `AgentDefinitions` table.
Must include a matching `.Designer.cs` file — see CLAUDE.md gotcha on EF migrations.

### 4. `admin-portal/src/components/SubAgentSelector.tsx`
New React component:
- Props: `value: string[]`, `onChange: (ids: string[]) => void`, `excludeId: string`
- Fetches `GET /api/agents` on mount, filters to `status === "Published" && isEnabled && id !== excludeId`
- Checkbox list grouped by type (local vs RemoteA2A)
- Each row: agent name, archetype badge, capabilities chips

---

## Files to Modify

### 5. `src/Diva.Infrastructure/Data/Entities/AgentDefinitionEntity.cs`
Add after `ModelSwitchingJson`:
```csharp
/// <summary>JSON array of agent IDs this coordinator delegates to.</summary>
public string? SubAgentIdsJson { get; set; }
```

### 6. `src/Diva.Agents/Registry/DynamicAgentRegistry.cs`
Add new branch in the agent-loading block (~lines 61-65):
```csharp
// NEW: Coordinator with explicit sub-agent list → OrchestratorAgent
if (def.ArchetypeId == "coordinator" && !string.IsNullOrEmpty(def.SubAgentIdsJson))
    agents.Add(new OrchestratorAgent(def, this, _supervisor));
// Existing: Remote A2A
else if (!string.IsNullOrEmpty(def.A2AEndpoint))
    agents.Add(new RemoteA2AAgent(def, _a2aClient, _credentialResolver));
// Existing: Local ReAct
else
    agents.Add(new DynamicReActAgent(def, _runner));
```
Also inject `ISupervisorAgent` into `DynamicAgentRegistry` constructor.

### 7. `src/Diva.Agents/Supervisor/Stages/DecomposeStage.cs`
Add LLM-based decomposition path (already noted as "Future" in existing comment):
- When scoped registry has N > 1 agents, call LLM to decompose the query into sub-tasks
- Pass agent names/descriptions/capabilities to the LLM prompt to guide assignment
- Fall back to single sub-task if N = 1 or LLM decomposition fails

### 8. `src/Diva.Agents/Supervisor/SupervisorAgent.cs`
Add overload accepting a scoped registry:
```csharp
Task<AgentResponse> InvokeWithRegistryAsync(
    AgentRequest request, TenantContext tenant,
    IAgentRegistry scopedRegistry, CancellationToken ct)
```
Pass the scoped registry via `SupervisorState` (new nullable `ScopedRegistry` field) so
`CapabilityMatchStage` uses it instead of the global registry.

### 9. `src/Diva.Host/Controllers/AgentsController.cs`
Include `SubAgentIdsJson` in the agent definition mapping for `PUT`/`POST` /api/agents.
No new endpoint needed — `GET /api/agents` already returns all agents for the UI dropdown.

### 10. `src/Diva.Host/Program.cs`
Update `DynamicAgentRegistry` DI registration to inject `ISupervisorAgent`.

### 11. `admin-portal/src/components/AgentBuilder.tsx`
- Add `subAgentIdsJson?: string` to `AgentDefinition` interface
- Render `<SubAgentSelector>` in the archetype config section, visible only when `archetypeId === "coordinator"`
- Wire `value` from parsed `form.subAgentIdsJson`, `onChange` serializes back to JSON string
- Include in `form` state sent to PUT /api/agents

---

## Data Shape

`AgentDefinitionEntity.SubAgentIdsJson`:
```json
["agent-id-1", "agent-id-2", "remote-a2a-agent-id-3"]
```

Simple flat array of string IDs. Remote A2A agents are just regular agent definitions
(archetype = `"RemoteA2A"`) in the same tenant — resolved from the registry as `RemoteA2AAgent`
instances automatically by `DynamicAgentRegistry`.

---

## Dependency Order

1. `AgentDefinitionEntity` + EF migration  (Infrastructure)
2. `ScopedAgentRegistry`  (Agents)
3. `OrchestratorAgent`  (Agents — depends on ScopedAgentRegistry + ISupervisorAgent)
4. `DynamicAgentRegistry` changes  (Agents — depends on OrchestratorAgent)
5. `SupervisorAgent` overload  (Agents)
6. `DecomposeStage` LLM path  (Agents)
7. `AgentsController` binding  (Host)
8. `Program.cs` DI update  (Host)
9. `SubAgentSelector.tsx` + `AgentBuilder.tsx`  (admin-portal)

---

## Critical Files

| File | Change |
|------|--------|
| `src/Diva.Infrastructure/Data/Entities/AgentDefinitionEntity.cs` | +1 field `SubAgentIdsJson` |
| `src/Diva.Infrastructure/Data/Migrations/<ts>_Phase19_...cs` + `.Designer.cs` | new migration |
| `src/Diva.Agents/Registry/ScopedAgentRegistry.cs` | **new** |
| `src/Diva.Agents/Workers/OrchestratorAgent.cs` | **new** |
| `src/Diva.Agents/Registry/DynamicAgentRegistry.cs` | +new branch, +ISupervisorAgent ctor arg |
| `src/Diva.Agents/Supervisor/SupervisorAgent.cs` | +overload with scoped registry |
| `src/Diva.Agents/Supervisor/Stages/DecomposeStage.cs` | +LLM decompose path |
| `src/Diva.Host/Controllers/AgentsController.cs` | +SubAgentIdsJson binding |
| `src/Diva.Host/Program.cs` | update DynamicAgentRegistry DI |
| `admin-portal/src/components/AgentBuilder.tsx` | +SubAgentSelector section |
| `admin-portal/src/components/SubAgentSelector.tsx` | **new** |
| `admin-portal/src/api.ts` | +`subAgentIdsJson` on `AgentDefinition` |

---

## Reuse — No Changes Needed

- `RemoteA2AAgent`, `A2AAgentClient`, `ICredentialResolver` — used as-is
- `CapabilityMatchStage` — no changes, just receives a different registry instance
- `DispatchStage`, `IntegrateStage`, `VerifyStage` — no changes
- Existing `IAgentRegistry` scoring algorithm copied into `ScopedAgentRegistry`

---

## Detailed Code Samples

### ScopedAgentRegistry.cs

```csharp
namespace Diva.Agents.Registry;

public sealed class ScopedAgentRegistry : IAgentRegistry
{
    private readonly List<IWorkerAgent> _agents;

    public ScopedAgentRegistry(List<IWorkerAgent> agents) => _agents = agents;

    public void Register(IWorkerAgent agent) { /* no-op — read-only */ }

    public Task<List<IWorkerAgent>> GetAgentsForTenantAsync(int tenantId, CancellationToken ct)
        => Task.FromResult(_agents);

    public Task<IWorkerAgent?> FindBestMatchAsync(
        string[] requiredCapabilities, int tenantId, CancellationToken ct)
    {
        if (_agents.Count == 0) return Task.FromResult<IWorkerAgent?>(null);
        if (requiredCapabilities.Length == 0 || _agents.Count == 1)
            return Task.FromResult<IWorkerAgent?>(_agents.OrderByDescending(
                a => a.GetCapability().Priority).First());

        var best = _agents
            .Select(a => (Agent: a, Score: a.GetCapability().Capabilities
                .Intersect(requiredCapabilities, StringComparer.OrdinalIgnoreCase).Count()))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Agent.GetCapability().Priority)
            .Select(x => x.Agent)
            .FirstOrDefault();

        return Task.FromResult(best ?? _agents.OrderByDescending(
            a => a.GetCapability().Priority).FirstOrDefault());
    }

    public Task<IWorkerAgent?> GetByIdAsync(string agentId, int tenantId, CancellationToken ct)
        => Task.FromResult(_agents.FirstOrDefault(
            a => a.GetCapability().AgentId == agentId));
}
```

### OrchestratorAgent.cs

```csharp
namespace Diva.Agents.Workers;

public sealed class OrchestratorAgent : IWorkerAgent, IStreamableWorkerAgent
{
    private readonly AgentDefinitionEntity _definition;
    private readonly IAgentRegistry _globalRegistry;
    private readonly ISupervisorAgent _supervisor;

    public OrchestratorAgent(
        AgentDefinitionEntity definition,
        IAgentRegistry globalRegistry,
        ISupervisorAgent supervisor)
    {
        _definition = definition;
        _globalRegistry = globalRegistry;
        _supervisor = supervisor;
    }

    public AgentCapability GetCapability()
    {
        var caps = string.IsNullOrEmpty(_definition.Capabilities)
            ? Array.Empty<string>()
            : JsonSerializer.Deserialize<string[]>(_definition.Capabilities) ?? [];

        return new AgentCapability
        {
            AgentId      = _definition.Id,
            AgentType    = _definition.AgentType,
            Description  = _definition.Description,
            Capabilities = caps,
            Priority     = 8,  // Higher than DynamicReActAgent (5), lower than static (10)
        };
    }

    public async Task<AgentResponse> ExecuteAsync(
        AgentRequest request, TenantContext tenant, CancellationToken ct)
    {
        var scopedRegistry = await BuildScopedRegistryAsync(tenant.TenantId, ct);
        return await _supervisor.InvokeWithRegistryAsync(request, tenant, scopedRegistry, ct);
    }

    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamAsync(
        AgentRequest request, TenantContext tenant,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var scopedRegistry = await BuildScopedRegistryAsync(tenant.TenantId, ct);
        await foreach (var chunk in _supervisor.InvokeStreamWithRegistryAsync(
            request, tenant, scopedRegistry, ct))
        {
            yield return chunk;
        }
    }

    private async Task<ScopedAgentRegistry> BuildScopedRegistryAsync(int tenantId, CancellationToken ct)
    {
        var subAgentIds = JsonSerializer.Deserialize<List<string>>(
            _definition.SubAgentIdsJson ?? "[]") ?? [];

        var agents = new List<IWorkerAgent>();
        foreach (var id in subAgentIds)
        {
            var agent = await _globalRegistry.GetByIdAsync(id, tenantId, ct);
            if (agent is not null) agents.Add(agent);
        }

        return new ScopedAgentRegistry(agents);
    }
}
```

### DecomposeStage — LLM Decomposition Prompt

```text
You are a task decomposition engine. Given a user query and a list of available agents,
break the query into sub-tasks that can be handled by the available agents.

Available agents:
{{#each agents}}
- {{name}} ({{agentType}}): {{description}}
  Capabilities: {{capabilities}}
{{/each}}

User query: {{query}}

Respond with a JSON array of sub-tasks:
[
  { "description": "...", "requiredCapabilities": ["..."], "assignedAgent": "agent-name" }
]

Rules:
- Each sub-task should map to exactly one agent
- If the query is simple enough for one agent, return a single sub-task
- Order sub-tasks by dependency (independent tasks first)
- Never create more sub-tasks than there are agents
```

---

## Edge Cases & Error Handling

| Scenario | Behaviour |
|----------|-----------|
| `SubAgentIdsJson` is null/empty | Fall back to `DynamicReActAgent` (no orchestration) |
| Referenced agent doesn't exist | Skip silently with warning log; proceed with available agents |
| All referenced agents missing | Return error response: "No sub-agents available for coordinator" |
| Remote A2A agent unreachable | Handled by existing `A2AAgentClient` resilience (retry + circuit breaker) |
| LLM decomposition fails | Fall back to single sub-task with full query, assigned to first agent |
| Circular delegation (coordinator references itself) | Prevented by `excludeId` check — same pattern as `AgentToolProvider` |
| Coordinator references another coordinator | Allowed — `ScopedAgentRegistry` resolves it as an `OrchestratorAgent`; depth limited by `MaxDelegationDepth` |

---

## Test Plan

### Unit tests (`tests/Diva.Agents.Tests/`)

| Test class | Tests | Description |
|------------|-------|-------------|
| `ScopedAgentRegistryTests.cs` | ~6 | Empty list, single agent, capability scoring, GetById, Register no-op |
| `OrchestratorAgentTests.cs` | ~6 | GetCapability, Execute with scoped registry, missing agents handled, empty SubAgentIdsJson |
| `DecomposeStageTests.cs` | ~4 | Single-task fallback, multi-task decomposition, LLM error fallback, agent count limit |
| `SupervisorScopedRegistryTests.cs` | ~4 | InvokeWithRegistryAsync passes scoped registry to stages, CapabilityMatchStage uses scoped |

### Integration test (manual)

1. Create two worker agents (one local, one RemoteA2A), publish both
2. Create a Coordinator agent, select both workers in SubAgentSelector, publish
3. `POST /api/supervisor/invoke` with a composite query
4. Verify `DecomposeStage` splits into 2 sub-tasks
5. Verify `DispatchStage` calls both workers in parallel
6. Verify `IntegrateStage` merges results into one response
7. Verify remote A2A worker receives its sub-task via SSE streaming

---

## Migration Notes

### EF Migration

```csharp
// 20260413000000_Phase19_CoordinatorSubAgentRouting.cs
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.AddColumn<string>(
        name: "SubAgentIdsJson",
        table: "AgentDefinitions",
        type: "TEXT",
        nullable: true);
}
```

**Must include `.Designer.cs`** — see CLAUDE.md gotcha on EF migrations. Copy `BuildTargetModel` from the previous migration's Designer.cs and add the new column.

---

## Interaction with Existing Features

| Feature | Impact |
|---------|--------|
| **Agents-as-Tools** (`DelegateAgentIdsJson`) | Complementary, not conflicting. A coordinator can also have `DelegateAgentIdsJson` for ad-hoc delegation within its own ReAct loop, separate from its `SubAgentIdsJson` orchestration list |
| **Rule Packs / Hooks** | Coordinator itself runs through the hook pipeline; sub-agents run their own hooks independently |
| **Model Switching** | Coordinator uses its own model config; sub-agents use their own. No cross-agent model sharing |
| **Verification** | Each sub-agent's response is verified independently by `VerifyStage`; coordinator can also have its own verification mode |
| **Context Window** | Each sub-agent gets its own context window; no shared history between sub-agents |
| **Prompt Caching** | Each sub-agent caches independently; coordinator's system prompt is cached separately |

---

## Verification

1. `dotnet build Diva.slnx` — clean build
2. `dotnet ef database update --project src/Diva.Infrastructure --startup-project src/Diva.Host -- --provider SQLite`
3. In Admin UI: create two worker agents (one local, one RemoteA2A archetype with endpoint), publish both
4. Create a Coordinator agent, select the two workers in SubAgentSelector, publish
5. `POST /api/supervisor/invoke` with a composite query
6. Verify `DispatchStage` calls both workers; `IntegrateStage` merges results
7. Verify the remote A2A worker receives its sub-task via SSE streaming
8. `dotnet test` — no regressions
