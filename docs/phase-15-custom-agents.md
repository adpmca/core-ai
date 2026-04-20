# Phase 15: Custom Pluggable Agent Framework + A2A Integration

> **Status:** `[ ]` Not Started  
> **Depends on:** [phase-08-agents.md](phase-08-agents.md), [phase-09-llm-client.md](phase-09-llm-client.md), [phase-14-a2a.md](phase-14-a2a.md)  
> **Prerequisite:** Tenant Grouping feature (already implemented — `ITenantGroupService`, `ILlmConfigResolver`, `IGroupMembershipCache`, `TenantAwarePromptBuilder` group rule/override merge)  
> **Blocks:** Nothing (additive — enriches existing agent execution)  
> **Projects:** `Diva.Core`, `Diva.Infrastructure`, `Diva.Agents`, `Diva.Host`, `admin-portal`  
> **Architecture ref:** [arch-supervisor.md](arch-supervisor.md), [arch-overview.md](arch-overview.md)

---

## Goal

Enable tenant admins and developers to create **custom, domain-specific agents** by composing pre-built behaviors (archetypes), overriding lifecycle hooks, injecting custom pre/post-processing logic, and exposing these agents via the **A2A (Agent-to-Agent) protocol** for interoperability with external orchestrators.

This follows the latest agentic AI trends:
- **Composable agent architectures** (LangGraph, CrewAI, AutoGen)
- **Agent archetypes / blueprints** (pre-configured behavioral templates)
- **Lifecycle hooks** (pluggable pre/post processing at each ReAct stage)
- **Agent-to-Agent protocol** (Google A2A — federated discovery + delegation)
- **MCP tool composition** (agents compose their tool surface dynamically)
- **Agent skill marketplace** (shareable archetypes across tenants)

---

## Architecture Assessment — Current vs Target

| Concept | Current State | Target |
|---------|--------------|--------|
| Agent creation | Flat `AgentDefinitionEntity` → `DynamicReActAgent` proxy | Archetype-based creation with behavior hooks |
| Behavior customization | System prompt + config knobs + **group business rules (Priority 50) + tenant rules (Priority 100) via `TenantAwarePromptBuilder`** | Lifecycle hook pipeline (OnInit → OnBeforeIteration → OnToolFilter → OnAfterToolCall → OnBeforeResponse → OnAfterResponse) layered on top of existing group/tenant rule injection |
| Agent archetypes | None — all agents are "general" | Built-in archetypes: RAG, CodeAnalyst, DataAnalyst, Conversational, Researcher, Coordinator |
| Execution modes | None — all agents run Full ReAct with all tools | `ExecutionMode` enum: Full, ChatOnly, ReadOnly, Supervised — enforced at runner's tool-loading choke point |
| Tool access tagging | None — tools are flat, no read/write classification | `ToolAccessLevel` per `McpToolBinding`: ReadOnly, ReadWrite, Destructive — drives ReadOnly mode filtering without separate tool lists |
| Hook execution | None | DB-stored hook configs + optional C# plugin classes |
| Pre/post processing | **Group prompt overrides (Prepend/Append/Replace) + tenant prompt overrides via `TenantAwarePromptBuilder`** | Per-agent input transformers + output formatters via lifecycle hooks (additive to existing group/tenant overrides) |
| LLM configuration | **4-level hierarchy (platform → group → tenant → agent) via `ILlmConfigResolver` with 2-min TTL cache** | Same — already implemented. Archetype defaults feed into agent-level config which sits at the bottom of this chain. |
| A2A exposure | None (Phase 14 planned) | AgentCard + /tasks/send + /tasks/{id} + remote delegation |
| Agent composition | Supervisor dispatches to flat workers | Agents can delegate to other agents (local + remote via A2A) |
| Group agent templates | **`GroupAgentTemplateEntity` exists but NOT loaded by `DynamicAgentRegistry`** | Group-shared agent templates appear in member tenants' agent registries |
| UI | Agent Builder (identity, model, tools, advanced) | + Archetype selector, Hook editor, A2A config, Agent composition panel |

---

## Gap Analysis (all resolved)

The following gaps were identified during code-level review against the actual codebase and resolved in this plan:

| # | Gap | Severity | Resolution |
|---|-----|----------|------------|
| 1 | `RunByTypeAsync` does not exist on `AnthropicAgentRunner` | HIGH | Changed `BaseCustomAgent` to accept `IDatabaseProviderFactory` and look up the `AgentDefinitionEntity` itself, then call `Runner.RunAsync(definition, ...)`. No new method needed on the runner. |
| 2 | Hooks inside `async IAsyncEnumerable` + `yield return` can crash the stream if they throw | HIGH | All hook call sites use the safe exception-capture pattern (`Exception? hookEx = null; try { ... } catch { hookEx = ex; } if (hookEx ...) yield return error; break;`). Documented as mandatory pattern in Step 6. |
| 3 | No streaming path for `RemoteA2AAgent` — `IWorkerAgent.ExecuteAsync` is non-streaming, but UI streams via `InvokeStreamAsync` directly | MEDIUM | Added `IStreamableWorkerAgent` interface with `InvokeStreamAsync` method. `RemoteA2AAgent` implements it. `AgentsController` detects this at the streaming endpoint and delegates accordingly. |
| 4 | Hook resolution via `AppDomain.GetAssemblies()` scanning is fragile, slow, and untestable | MEDIUM | Replaced with `HookTypeRegistry` — a startup-built `Dictionary<string, Type>` populated by assembly scanning once. Runtime resolution is O(1) dictionary lookup. |
| 5 | No `OnError` hook for custom error handling, fallback logic, or alert webhooks | MEDIUM | Added 7th hook: `IOnErrorHook.OnErrorAsync(context, toolName, exception)` returning `ErrorRecoveryAction` (Continue / Retry / Abort). |
| 6 | Archetype applied only at creation-time vs runtime — ambiguous merge semantics | LOW-MED | Clarified: archetypes are applied at **both** creation-time (pre-fill form) and **runtime** (merge archetype defaults under agent overrides). Agent-level config always wins. Merge logic documented in Step 6. |
| 7 | No `hook_executed` SSE events — hooks are invisible to the UI | LOW | Added `hook_executed` chunk type to `AgentStreamChunk` with `hookName`, `hookPoint`, `durationMs`. Emitted after each hook runs. |
| 8 | Template variable syntax (`{{variable}}`) unspecified — no engine choice | LOW | Specified simple `string.Replace` for `{{key}}` patterns. No external template engine needed. Escape `\{\{` for literal braces. |
| 9 | No tenant-scoped custom archetypes — `ArchetypeRegistry` has no DB persistence | LOW | Moved to "Future Extensions" explicitly. Built-in archetypes cover initial needs. |
| 10 | `AgentTaskEntity.Id` type mismatch (Phase 14 used `Guid`, rest of codebase uses `string`) | LOW | Standardised on `string` (GUID string) to match `AgentDefinitionEntity.Id`. |
| 11 | `DynamicAgentRegistry` does not load `GroupAgentTemplateEntity` — group-shared agents invisible to member tenants | MEDIUM | Step 14 expanded: inject `ITenantGroupService`, call `GetAgentTemplatesForTenantAsync(tenantId)`, map to `DynamicReActAgent` alongside tenant-owned definitions. |
| 12 | `AgentHookPipeline` is a sealed concrete class with no interface — not mockable for runner/controller unit tests | MEDIUM | Extract `IAgentHookPipeline` interface into `Diva.Core`. Runner and controllers depend on the interface. Tests can substitute with NSubstitute. |
| 13 | `AnthropicAgentRunner` has no interface — `BaseCustomAgent` and controllers couple to the concrete class | MEDIUM | Extract `IAgentRunner` interface (covers `RunAsync` + `InvokeStreamAsync`). `BaseCustomAgent` depends on `IAgentRunner`, not the concrete runner. Enables unit testing custom agents without the full LLM chain. |
| 14 | `AgentCardBuilder` has no interface — `AgentCardController` can't be tested in isolation | LOW | Extract `IAgentCardBuilder` interface. Controller depends on interface, mockable in tests. |
| 15 | `AgentRequest` / `AgentResponse` are `sealed class`, not `record` — `with` expressions in `BaseCustomAgent` won't compile | HIGH | Changed `BaseCustomAgent` to construct new instances manually instead of using `with` expressions. Same fix applied to `ComplianceAgent` example. |
| 16 | `McpBindingsJson` doesn't exist on `AgentDefinitionEntity` or `GroupAgentTemplateEntity` — mapping in Step 14 won't compile | HIGH | Fixed `MapGroupTemplateToDefinition` to use `ToolBindings` (the actual property name on both entities). |
| 17 | `BaseCustomAgent` pre/post-processing bypassed during SSE streaming — controller falls through to runner directly | MEDIUM | `BaseCustomAgent` now implements `IStreamableWorkerAgent` with an `InvokeStreamAsync` that wraps the runner call with pre/post-processing. Controller's `IStreamableWorkerAgent` detection picks it up automatically. |
| 18 | Template variable `{{key}}` resolution described in Design Decisions but not placed in Step 6's runner integration code | LOW | Added explicit template resolution step in Step 6 pseudocode, between archetype lookup and `hookCtx` creation. |
| 19 | `GroupAgentTemplateEntity` won't receive the new archetype/hooks fields added to `AgentDefinitionEntity` in Step 5 | LOW | Documented as intentional MVP scope — group templates adopt archetype fields in a future phase. Added to Future Extensions. |
| 20 | `hook_executed` SSE event type missing from `AgentStreamChunk` XML doc comment enum | LOW | Step 6 now notes that the doc comment must be updated to include `hook_executed` in the type listing. |
| 21 | No `GetByIdAsync` on `IAgentRegistry` — A2A `POST /tasks/send?agentId=X` can't resolve a specific agent efficiently | MEDIUM | Add `Task<IWorkerAgent?> GetByIdAsync(string agentId, int tenantId, CancellationToken ct)` to `IAgentRegistry`. `DynamicAgentRegistry` implements it with a direct DB lookup + static fallback. `AgentTaskController` and `AgentsController` use it for targeted resolution. |
| 22 | `DynamicReActAgent` depends on concrete `AnthropicAgentRunner`, not `IAgentRunner` interface — breaks testability and DI consistency | MEDIUM | Updated `DynamicReActAgent` constructor to accept `IAgentRunner` instead of `AnthropicAgentRunner`. `DynamicAgentRegistry` also updated to hold `IAgentRunner`. Consistent with `BaseCustomAgent` (Gap #13). |
| 23 | No cancellation propagation for in-flight A2A tasks — `DELETE /tasks/{id}` can't stop a running agent | MEDIUM | Added `ConcurrentDictionary<string, CancellationTokenSource>` to `AgentTaskController`. Each `POST /tasks/send` creates a linked CTS keyed by task ID. `DELETE` looks up and cancels via that CTS. CTS removed on task completion/failure. |
| 24 | A2A delegation chains lack depth protection — Agent A → B → A creates infinite loop | LOW | Added `X-A2A-Depth` header tracking. `A2AAgentClient.SendTaskAsync` increments depth on outbound calls. `AgentTaskController` rejects with 400 if depth ≥ `A2AOptions.MaxDelegationDepth` (default 5). |
| 25 | Phase 14 `AgentTaskEntity.Id` uses `Guid`, Phase 15 standardised on `string` — cross-doc inconsistency | LOW | Updated Phase 14 doc: `AgentTaskEntity.Id` → `string`, `AgentId` → `string`. Matches `AgentDefinitionEntity.Id` pattern and Phase 15's correction. |
| 26 | No execution mode — agents can't run in chat-only or read-only mode; all tools always available | MEDIUM | Added `AgentExecutionMode` enum (Full, ChatOnly, ReadOnly, Supervised) to `Diva.Core`. New `ExecutionMode` field on `AgentDefinitionEntity`. Runner enforces at the same choke point as `ToolFilterJson`. ChatOnly clears all tools; ReadOnly filters by `McpToolBinding.Access` tag. |
| 27 | ReadOnly mode would require a separate tool whitelist — UX burden and config drift | MEDIUM | Added `ToolAccessLevel` enum (ReadOnly, ReadWrite, Destructive) on `McpToolBinding`. Admin tags each binding once. Runner filters by access level when `ExecutionMode == ReadOnly`. No separate list needed. UI auto-suggests access level from binding name patterns. |

---

## Design Decisions

### Why Lifecycle Hooks (not just inheritance)?

Traditional class inheritance doesn't scale for tenant admins who configure agents via UI. Instead:

1. **Archetypes** define a bundle of default behaviors (hooks, tool preferences, system prompt template, default config)
2. **Hooks** are lightweight, composable interceptors stored as JSON config or resolved as C# classes via DI
3. **Custom C# agents** can still inherit `BaseCustomAgent` for developers who want full control
4. Both paths produce an `IWorkerAgent` — the supervisor and registry see no difference

This is the **Strategy + Template Method + Chain of Responsibility** hybrid that LangGraph/CrewAI use under the hood.

### Hook Pipeline (runs inside `AnthropicAgentRunner.ExecuteReActLoopAsync`)

```
Request arrives
  │
  ├─ OnInit(context)              ← Setup: load RAG index, init state, validate input
  │       └─ yield hook_executed
  │
  ├─ For each ReAct iteration:
  │   ├─ OnBeforeIteration(i)     ← Inject context, modify system prompt mid-run
  │   │       └─ yield hook_executed
  │   ├─ LLM call
  │   ├─ OnToolFilter(tools)      ← Dynamic allow/deny based on iteration state
  │   │       └─ yield hook_executed
  │   ├─ Tool execution
  │   ├─ OnAfterToolCall(result)  ← Transform tool results, trigger side effects
  │   │       └─ yield hook_executed
  │   ├─ OnError(ex)?             ← If tool/LLM failed: custom recovery logic
  │   │       └─ yield hook_executed
  │   └─ (loop)
  │
  ├─ OnBeforeResponse(text)       ← Format, redact PII, enforce output structure
  │       └─ yield hook_executed
  ├─ Verification
  ├─ OnAfterResponse(response)    ← Log analytics, trigger webhooks, update CRM
  │       └─ yield hook_executed
  │
  └─ Return AgentResponse
```

**Critical: Hooks inside `yield return` context.** `ExecuteReActLoopAsync` is an `async IAsyncEnumerable<AgentStreamChunk>`, so `yield return` cannot appear inside `try/catch`. Every hook call site MUST use the safe exception-capture pattern:

```csharp
// MANDATORY PATTERN for hooks inside the streaming ReAct loop
Exception? hookEx = null;
try { await _hookPipeline.RunOnBeforeIterationAsync(hooks, hookCtx, iteration, ct); }
catch (Exception ex) { hookEx = ex; }
if (hookEx is not null)
{
    _logger.LogWarning(hookEx, "Hook failed at OnBeforeIteration, iteration {I}", iteration);
    yield return new AgentStreamChunk { Type = "error", ErrorMessage = $"Hook error: {hookEx.Message}" };
    break; // or continue, depending on severity
}
yield return new AgentStreamChunk { Type = "hook_executed", HookName = "OnBeforeIteration", HookPoint = "before_iteration" };
```

This pattern is already used in the runner for LLM calls (line ~299 of `AnthropicAgentRunner.cs`).

### Template Variable Engine

Archetype `SystemPromptTemplate` values use `{{variable_name}}` placeholders. These are resolved via simple `string.Replace`:

```csharp
foreach (var (key, value) in customVars)
    template = template.Replace($"{{{{{key}}}}}", value);
```

- No external template engine (Scriban, Handlebars) is needed.
- For literal `{{` in prompt text, use `\{\{` which is unescaped after variable resolution.
- Variables come from `AgentDefinitionEntity.CustomVariablesJson` merged under archetype suggestions.

### Archetype Merge Strategy (Creation-Time + Runtime)

Archetypes apply at **two** points:

1. **Creation-time (UI):** When a user selects an archetype in the Agent Builder, archetype defaults pre-fill the form (system prompt, temperature, hooks, capabilities, etc.). The user can override any field.

2. **Runtime (execution):** Before each execution, the runner merges archetype defaults with agent-level overrides. **Agent-level config always wins** (non-null agent fields override archetype defaults):

```
Effective value = agent.Field ?? archetype.DefaultField ?? globalDefault
```

This means updating an archetype's defaults affects all agents that haven't explicitly overridden that field.

### Prompt Priority Hierarchy (with Tenant Grouping)

With the tenant grouping feature already in place, the full prompt assembly order during agent execution is:

```
1. Archetype base system prompt template  (Phase 15 — resolved via {{variable}} engine)
2. Group prompt overrides                 (existing — Prepend/Append/Replace via TenantAwarePromptBuilder)
3. Tenant prompt overrides                (existing — Prepend/Append/Replace, applied AFTER group overrides)
4. Group business rules                   (existing — injected at Priority=50)
5. Tenant business rules                  (existing — injected at Priority=100)
6. Session-scoped rules                   (existing — injected by TenantAwarePromptBuilder)
7. Hook modifications (OnInit)            (Phase 15 — hooks may modify hookCtx.SystemPrompt)
8. Hook modifications (OnBeforeIteration) (Phase 15 — per-iteration prompt injection)
```

Archetype templates (step 1) are resolved **before** the prompt enters `TenantAwarePromptBuilder`, so group/tenant overrides can further refine the archetype's base prompt. Hook modifications (steps 7-8) run **after** the full prompt is assembled, giving hooks final say.

The `ILlmConfigResolver` 4-level hierarchy (platform → group → tenant → agent) determines which model/provider is used, independent of prompt assembly.

### A2A Integration Strategy

Phase 14 defines the A2A HTTP surface. This phase adds:
- **Archetype-aware AgentCard** — skills populated from archetype capabilities
- **Agent delegation hooks** — `OnDelegateToAgent` hook for agent-to-agent calls
- **Remote agent as archetype** — "RemoteA2A" archetype wraps `IA2AAgentClient`

---

## Tier A — Core Framework (implement first)

### Step 1: Agent Archetype Model

#### `Diva.Core/Configuration/AgentArchetype.cs` — CREATE

```csharp
namespace Diva.Core.Configuration;

/// <summary>
/// Defines an agent archetype — a reusable behavioral template with
/// pre-configured hooks, tools, and defaults.
/// </summary>
public sealed class AgentArchetype
{
    /// <summary>Unique archetype ID (e.g. "rag", "code-analyst", "data-analyst").</summary>
    public string Id { get; init; } = "";

    /// <summary>Human-readable name shown in Agent Builder UI.</summary>
    public string DisplayName { get; init; } = "";

    /// <summary>Description of what this archetype does.</summary>
    public string Description { get; init; } = "";

    /// <summary>Icon identifier for the UI (e.g. "database", "code", "search", "brain").</summary>
    public string Icon { get; init; } = "bot";

    /// <summary>Category for grouping in the archetype gallery.</summary>
    public string Category { get; init; } = "General";

    /// <summary>Default system prompt template. Supports {{variable}} placeholders.</summary>
    public string SystemPromptTemplate { get; init; } = "";

    /// <summary>Default capabilities assigned to agents created from this archetype.</summary>
    public string[] DefaultCapabilities { get; init; } = [];

    /// <summary>Suggested MCP tool server names.</summary>
    public string[] SuggestedTools { get; init; } = [];

    /// <summary>Default hook configuration (JSON key → hook class name or inline config).</summary>
    public Dictionary<string, string> DefaultHooks { get; init; } = [];

    /// <summary>Default temperature.</summary>
    public double DefaultTemperature { get; init; } = 0.7;

    /// <summary>Default max iterations.</summary>
    public int DefaultMaxIterations { get; init; } = 10;

    /// <summary>Default verification mode.</summary>
    public string? DefaultVerificationMode { get; init; }

    /// <summary>Recommended pipeline stage overrides.</summary>
    public Dictionary<string, bool>? PipelineStageDefaults { get; init; }

    /// <summary>Default execution mode for agents created from this archetype.</summary>
    public AgentExecutionMode DefaultExecutionMode { get; init; } = AgentExecutionMode.Full;
}

/// <summary>
/// Controls what an agent is allowed to do at runtime.
/// Enforced at the runner's tool-loading choke point (same location as ToolFilterJson).
/// </summary>
public enum AgentExecutionMode
{
    /// <summary>Full ReAct loop — LLM can plan, call tools, iterate, verify (default).</summary>
    Full,

    /// <summary>Chat only — all tools removed before LLM sees them. LLM can only reason and respond from system prompt + conversation history.</summary>
    ChatOnly,

    /// <summary>Read-only — only tools with ToolAccessLevel.ReadOnly are loaded. Write/Destructive tools removed.</summary>
    ReadOnly,

    /// <summary>Supervised — tool calls require human approval via SignalR before execution (future).</summary>
    Supervised,
}

/// <summary>
/// Classifies an MCP tool binding's access level for ExecutionMode enforcement.
/// Tagged per-binding in Agent Builder UI. Auto-suggested from binding name.
/// </summary>
public enum ToolAccessLevel
{
    /// <summary>Search, lookup, list, get, read — safe in ReadOnly mode.</summary>
    ReadOnly,

    /// <summary>Create, update, execute — blocked in ReadOnly mode (default).</summary>
    ReadWrite,

    /// <summary>Delete, drop, purge — blocked in ReadOnly mode; optionally blockable in Full mode via hooks.</summary>
    Destructive,
}
```

### Step 2: Lifecycle Hook Interfaces

#### `Diva.Core/Models/IAgentLifecycleHook.cs` — CREATE

```csharp
namespace Diva.Core.Models;

/// <summary>
/// Base interface for all agent lifecycle hooks.
/// Hooks are resolved per-request via DI or constructed from JSON config.
/// </summary>
public interface IAgentLifecycleHook
{
    /// <summary>Execution order. Lower = earlier. Default = 100.</summary>
    int Order => 100;
}

/// <summary>Called once when agent execution starts, before the ReAct loop.</summary>
public interface IOnInitHook : IAgentLifecycleHook
{
    Task OnInitAsync(AgentHookContext context, CancellationToken ct);
}

/// <summary>Called at the start of each ReAct iteration.</summary>
public interface IOnBeforeIterationHook : IAgentLifecycleHook
{
    Task OnBeforeIterationAsync(AgentHookContext context, int iteration, CancellationToken ct);
}

/// <summary>Called after LLM returns tool calls, before execution. Can filter/modify tool list.</summary>
public interface IOnToolFilterHook : IAgentLifecycleHook
{
    Task<List<UnifiedToolCallRef>> OnToolFilterAsync(
        AgentHookContext context, List<UnifiedToolCallRef> toolCalls, CancellationToken ct);
}

/// <summary>Called after each tool call completes.</summary>
public interface IOnAfterToolCallHook : IAgentLifecycleHook
{
    Task<string> OnAfterToolCallAsync(
        AgentHookContext context, string toolName, string toolOutput, bool isError, CancellationToken ct);
}

/// <summary>Called after the ReAct loop produces a final text response, before verification.</summary>
public interface IOnBeforeResponseHook : IAgentLifecycleHook
{
    Task<string> OnBeforeResponseAsync(AgentHookContext context, string responseText, CancellationToken ct);
}

/// <summary>Called after verification, before returning to caller. Last chance for side effects.</summary>
public interface IOnAfterResponseHook : IAgentLifecycleHook
{
    Task OnAfterResponseAsync(AgentHookContext context, AgentResponse response, CancellationToken ct);
}

/// <summary>
/// Called when a tool call or LLM call fails. Allows custom error recovery.
/// Return an ErrorRecoveryAction to control flow.
/// </summary>
public interface IOnErrorHook : IAgentLifecycleHook
{
    Task<ErrorRecoveryAction> OnErrorAsync(
        AgentHookContext context, string? toolName, Exception exception, CancellationToken ct);
}

/// <summary>Instructs the ReAct loop how to proceed after an error hook runs.</summary>
public enum ErrorRecoveryAction
{
    /// <summary>Log and continue the iteration loop normally (default runner behavior).</summary>
    Continue,
    /// <summary>Retry the same tool call once.</summary>
    Retry,
    /// <summary>Abort the ReAct loop immediately and return an error response.</summary>
    Abort,
}

/// <summary>Mutable context bag passed through all hooks for a single agent execution.</summary>
public sealed class AgentHookContext
{
    public AgentRequest Request { get; init; } = null!;
    public TenantContext Tenant { get; init; } = null!;
    public string AgentId { get; init; } = "";
    public string ArchetypeId { get; init; } = "";
    public string SessionId { get; set; } = "";

    /// <summary>Hook-specific state. Hooks can store/retrieve arbitrary data here.</summary>
    public Dictionary<string, object?> State { get; } = [];

    /// <summary>System prompt that will be sent to the LLM. Hooks can modify this.</summary>
    public string SystemPrompt { get; set; } = "";

    /// <summary>Custom variables resolved from archetype + agent config.</summary>
    public Dictionary<string, string> Variables { get; init; } = [];

    /// <summary>Accumulated tool evidence (read-only snapshot for hooks).</summary>
    public string ToolEvidence { get; set; } = "";

    /// <summary>Current iteration number (updated by runner before each hook call).</summary>
    public int CurrentIteration { get; set; }

    /// <summary>Number of consecutive tool failures (read from runner state).</summary>
    public int ConsecutiveFailures { get; set; }
}

/// <summary>Lightweight reference to a tool call (for filter hooks).</summary>
public sealed class UnifiedToolCallRef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string InputJson { get; init; } = "";
    public bool Filtered { get; set; } = false;
}
```

### Step 3: Agent Hook Pipeline Runner

#### `Diva.Core/Models/IAgentHookPipeline.cs` — CREATE

```csharp
namespace Diva.Core.Models;

/// <summary>
/// Abstraction over the hook pipeline — enables mocking in unit tests.
/// Placed in Diva.Core so both Diva.Agents and Diva.Infrastructure can depend on it.
/// </summary>
public interface IAgentHookPipeline
{
    List<IAgentLifecycleHook> ResolveHooks(Dictionary<string, string> hookConfig, string archetypeId);
    Task RunOnInitAsync(List<IAgentLifecycleHook> hooks, AgentHookContext ctx, CancellationToken ct);
    Task RunOnBeforeIterationAsync(List<IAgentLifecycleHook> hooks, AgentHookContext ctx, int iteration, CancellationToken ct);
    Task<List<UnifiedToolCallRef>> RunOnToolFilterAsync(List<IAgentLifecycleHook> hooks, AgentHookContext ctx, List<UnifiedToolCallRef> calls, CancellationToken ct);
    Task<string> RunOnAfterToolCallAsync(List<IAgentLifecycleHook> hooks, AgentHookContext ctx, string toolName, string output, bool isError, CancellationToken ct);
    Task<ErrorRecoveryAction> RunOnErrorAsync(List<IAgentLifecycleHook> hooks, AgentHookContext ctx, string? toolName, Exception exception, CancellationToken ct);
    Task<string> RunOnBeforeResponseAsync(List<IAgentLifecycleHook> hooks, AgentHookContext ctx, string text, CancellationToken ct);
    Task RunOnAfterResponseAsync(List<IAgentLifecycleHook> hooks, AgentHookContext ctx, AgentResponse response, CancellationToken ct);
}
```

#### `Diva.Agents/Hooks/AgentHookPipeline.cs` — CREATE

```csharp
namespace Diva.Agents.Hooks;

using Diva.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Resolves and executes lifecycle hooks for an agent execution.
/// Implements IAgentHookPipeline for mockability in tests.
/// Hooks are loaded from:
///   1. Archetype defaults (registered at startup)
///   2. Agent definition HooksJson (per-agent overrides)
///   3. DI container (globally registered hooks)
///
/// IMPORTANT: Hook resolution uses HookTypeRegistry (built once at startup)
/// instead of runtime assembly scanning. This is fast, safe, and testable.
/// </summary>
public sealed class AgentHookPipeline : IAgentHookPipeline
{
    private readonly IServiceProvider _sp;
    private readonly HookTypeRegistry _typeRegistry;
    private readonly ILogger<AgentHookPipeline> _logger;

    public AgentHookPipeline(
        IServiceProvider sp,
        HookTypeRegistry typeRegistry,
        ILogger<AgentHookPipeline> logger)
    {
        _sp = sp;
        _typeRegistry = typeRegistry;
        _logger = logger;
    }

    /// <summary>
    /// Resolve all hooks for an agent, ordered by priority.
    /// hookConfig is the JSON dict from AgentDefinitionEntity.HooksJson
    /// merged with archetype defaults.
    /// Uses HookTypeRegistry (O(1) lookup) instead of AppDomain assembly scanning.
    /// </summary>
    public List<IAgentLifecycleHook> ResolveHooks(
        Dictionary<string, string> hookConfig,
        string archetypeId)
    {
        var hooks = new List<IAgentLifecycleHook>();

        foreach (var (hookPoint, className) in hookConfig)
        {
            if (string.IsNullOrWhiteSpace(className)) continue;

            var hookType = _typeRegistry.Resolve(className);
            if (hookType is null)
            {
                _logger.LogWarning(
                    "Hook class '{ClassName}' for point '{HookPoint}' not found in registry",
                    className, hookPoint);
                continue;
            }

            var instance = ActivatorUtilities.CreateInstance(_sp, hookType) as IAgentLifecycleHook;
            if (instance is not null)
                hooks.Add(instance);
        }

        return hooks.OrderBy(h => h.Order).ToList();
    }

    public async Task RunOnInitAsync(
        List<IAgentLifecycleHook> hooks, AgentHookContext ctx, CancellationToken ct)
    {
        foreach (var hook in hooks.OfType<IOnInitHook>())
        {
            _logger.LogDebug("Running OnInit hook: {Hook}", hook.GetType().Name);
            await hook.OnInitAsync(ctx, ct);
        }
    }

    public async Task RunOnBeforeIterationAsync(
        List<IAgentLifecycleHook> hooks, AgentHookContext ctx, int iteration, CancellationToken ct)
    {
        foreach (var hook in hooks.OfType<IOnBeforeIterationHook>())
            await hook.OnBeforeIterationAsync(ctx, iteration, ct);
    }

    public async Task<List<UnifiedToolCallRef>> RunOnToolFilterAsync(
        List<IAgentLifecycleHook> hooks, AgentHookContext ctx,
        List<UnifiedToolCallRef> calls, CancellationToken ct)
    {
        var current = calls;
        foreach (var hook in hooks.OfType<IOnToolFilterHook>())
            current = await hook.OnToolFilterAsync(ctx, current, ct);
        return current;
    }

    public async Task<string> RunOnAfterToolCallAsync(
        List<IAgentLifecycleHook> hooks, AgentHookContext ctx,
        string toolName, string output, bool isError, CancellationToken ct)
    {
        var current = output;
        foreach (var hook in hooks.OfType<IOnAfterToolCallHook>())
            current = await hook.OnAfterToolCallAsync(ctx, toolName, current, isError, ct);
        return current;
    }

    public async Task<ErrorRecoveryAction> RunOnErrorAsync(
        List<IAgentLifecycleHook> hooks, AgentHookContext ctx,
        string? toolName, Exception exception, CancellationToken ct)
    {
        var action = ErrorRecoveryAction.Continue;
        foreach (var hook in hooks.OfType<IOnErrorHook>())
        {
            var result = await hook.OnErrorAsync(ctx, toolName, exception, ct);
            if (result > action) action = result; // Most severe action wins
        }
        return action;
    }

    public async Task<string> RunOnBeforeResponseAsync(
        List<IAgentLifecycleHook> hooks, AgentHookContext ctx, string text, CancellationToken ct)
    {
        var current = text;
        foreach (var hook in hooks.OfType<IOnBeforeResponseHook>())
            current = await hook.OnBeforeResponseAsync(ctx, current, ct);
        return current;
    }

    public async Task RunOnAfterResponseAsync(
        List<IAgentLifecycleHook> hooks, AgentHookContext ctx, AgentResponse response, CancellationToken ct)
    {
        foreach (var hook in hooks.OfType<IOnAfterResponseHook>())
            await hook.OnAfterResponseAsync(ctx, response, ct);
    }
}
```

#### `Diva.Agents/Hooks/HookTypeRegistry.cs` — CREATE

```csharp
namespace Diva.Agents.Hooks;

using System.Collections.Frozen;
using Diva.Core.Models;

/// <summary>
/// Startup-built registry mapping hook class names to their Types.
/// Replaces fragile AppDomain.GetAssemblies() scanning at runtime.
/// Built once during DI registration, then used for O(1) lookups.
/// </summary>
public sealed class HookTypeRegistry
{
    private readonly FrozenDictionary<string, Type> _map;

    public HookTypeRegistry(IEnumerable<Type> hookTypes)
    {
        _map = hookTypes
            .Where(t => typeof(IAgentLifecycleHook).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
            .ToFrozenDictionary(t => t.Name, t => t, StringComparer.OrdinalIgnoreCase);
    }

    public Type? Resolve(string className) =>
        _map.GetValueOrDefault(className);

    public IReadOnlyList<string> RegisteredHookNames => [.. _map.Keys];

    /// <summary>
    /// Scan assemblies once at startup and build the registry.
    /// Called from Program.cs during DI registration.
    /// </summary>
    public static HookTypeRegistry BuildFromAssemblies(params System.Reflection.Assembly[] assemblies)
    {
        var types = assemblies
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch { return []; }
            })
            .Where(t => typeof(IAgentLifecycleHook).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);

        return new HookTypeRegistry(types);
    }
}
```

**DI registration (in Program.cs):**

```csharp
// Build hook type registry ONCE at startup — replaces runtime assembly scanning
var hookRegistry = HookTypeRegistry.BuildFromAssemblies(
    typeof(AgentHookPipeline).Assembly,               // Diva.Agents (built-in hooks)
    typeof(AnthropicAgentRunner).Assembly              // Diva.Infrastructure (if any hooks there)
);
builder.Services.AddSingleton(hookRegistry);
builder.Services.AddSingleton<IAgentHookPipeline, AgentHookPipeline>();
builder.Services.AddSingleton<IAgentRunner, AnthropicAgentRunner>();  // ← IAgentRunner for testability
builder.Services.AddSingleton<IAgentCardBuilder, AgentCardBuilder>(); // ← IAgentCardBuilder for testability
```

### Step 4: Built-in Archetypes + Example Hooks

#### `Diva.Agents/Archetypes/BuiltInArchetypes.cs` — CREATE

```csharp
namespace Diva.Agents.Archetypes;

using Diva.Core.Configuration;

/// <summary>
/// Registry of built-in agent archetypes. Tenant admins select an archetype
/// when creating an agent — it pre-fills defaults and attaches standard hooks.
/// </summary>
public static class BuiltInArchetypes
{
    public static readonly AgentArchetype General = new()
    {
        Id = "general",
        DisplayName = "General Assistant",
        Description = "Versatile agent for open-ended tasks. No specialised pre-processing.",
        Icon = "bot",
        Category = "General",
        SystemPromptTemplate = "You are a helpful AI assistant for {{company_name}}. Answer user questions accurately and concisely.",
        DefaultCapabilities = ["general", "question-answering", "summarization"],
        DefaultTemperature = 0.7,
        DefaultMaxIterations = 10,
    };

    public static readonly AgentArchetype Rag = new()
    {
        Id = "rag",
        DisplayName = "RAG Knowledge Agent",
        Description = "Retrieval-Augmented Generation agent. Always grounds answers in retrieved documents. Ideal for knowledge bases, documentation QA, and policy lookup.",
        Icon = "database",
        Category = "Knowledge",
        SystemPromptTemplate = """
            You are a knowledge assistant for {{company_name}}.
            ALWAYS search the knowledge base before answering.
            ALWAYS cite your sources with document names and section references.
            If the knowledge base does not contain relevant information, say so explicitly.
            Never fabricate information not found in retrieved documents.
            """,
        DefaultCapabilities = ["rag", "knowledge-base", "document-search", "citation"],
        SuggestedTools = ["knowledge-search", "document-retrieval"],
        DefaultHooks = new()
        {
            ["OnBeforeResponse"] = "CitationEnforcerHook",
        },
        DefaultTemperature = 0.3,
        DefaultMaxIterations = 8,
        DefaultVerificationMode = "ToolGrounded",
    };

    public static readonly AgentArchetype CodeAnalyst = new()
    {
        Id = "code-analyst",
        DisplayName = "Code Analyst",
        Description = "Specialised in code review, debugging, refactoring suggestions, and technical documentation. Enforces structured output for code blocks.",
        Icon = "code",
        Category = "Engineering",
        SystemPromptTemplate = """
            You are a senior software engineer and code analyst for {{company_name}}.
            When reviewing code:
            1. Identify bugs, security issues, and performance problems
            2. Suggest concrete fixes with code examples
            3. Follow the team's coding conventions
            4. Use markdown code blocks with language identifiers
            Language: {{primary_language}}
            """,
        DefaultCapabilities = ["code-review", "debugging", "refactoring", "documentation"],
        SuggestedTools = ["code-search", "linter", "test-runner"],
        DefaultHooks = new()
        {
            ["OnBeforeResponse"] = "CodeBlockFormatterHook",
        },
        DefaultTemperature = 0.2,
        DefaultMaxIterations = 15,
        DefaultVerificationMode = "LlmVerifier",
    };

    public static readonly AgentArchetype DataAnalyst = new()
    {
        Id = "data-analyst",
        DisplayName = "Data Analyst",
        Description = "Analyses datasets, generates SQL queries, creates chart descriptions, and explains statistical findings. Structures output with tables and metrics.",
        Icon = "chart",
        Category = "Analytics",
        SystemPromptTemplate = """
            You are a data analyst for {{company_name}}.
            When analysing data:
            1. Always validate your SQL/queries before presenting results
            2. Present findings with clear metrics and comparisons
            3. Use markdown tables for structured data
            4. Explain statistical significance when relevant
            Database: {{database_type}}
            """,
        DefaultCapabilities = ["data-analysis", "sql", "statistics", "visualization", "reporting"],
        SuggestedTools = ["sql-query", "data-export", "chart-generator"],
        DefaultHooks = new()
        {
            ["OnAfterToolCall"] = "SqlResultFormatterHook",
            ["OnBeforeResponse"] = "MetricsHighlighterHook",
        },
        DefaultTemperature = 0.3,
        DefaultMaxIterations = 12,
        DefaultVerificationMode = "Strict",
    };

    public static readonly AgentArchetype Researcher = new()
    {
        Id = "researcher",
        DisplayName = "Research Agent",
        Description = "Deep-dive research agent that systematically explores topics using multiple sources, synthesises findings, and produces structured reports with citations.",
        Icon = "search",
        Category = "Research",
        SystemPromptTemplate = """
            You are a research specialist for {{company_name}}.
            Your research methodology:
            1. Break the question into sub-questions
            2. Search multiple sources for each sub-question
            3. Cross-reference findings across sources
            4. Synthesise into a structured report with sections
            5. Always cite sources and note confidence levels
            Focus area: {{research_domain}}
            """,
        DefaultCapabilities = ["research", "web-search", "synthesis", "report-generation", "citation"],
        SuggestedTools = ["web-search", "document-retrieval", "knowledge-search"],
        DefaultHooks = new()
        {
            ["OnInit"] = "ResearchPlannerHook",
            ["OnBeforeResponse"] = "ReportStructurerHook",
        },
        DefaultTemperature = 0.5,
        DefaultMaxIterations = 20,
        DefaultVerificationMode = "LlmVerifier",
        PipelineStageDefaults = new()
        {
            ["Decompose"] = true, // research benefits from sub-task decomposition
        },
    };

    public static readonly AgentArchetype Coordinator = new()
    {
        Id = "coordinator",
        DisplayName = "Multi-Agent Coordinator",
        Description = "Orchestrates sub-tasks across multiple agents. Decomposes complex requests, delegates to specialised agents, and integrates results. A2A-compatible for remote delegation.",
        Icon = "network",
        Category = "Orchestration",
        SystemPromptTemplate = """
            You are a task coordinator for {{company_name}}.
            Your role is to break complex requests into sub-tasks and delegate them to specialised agents.
            Available agents will be provided as tools.
            For each sub-task:
            1. Identify the best agent for the job
            2. Formulate a clear, self-contained instruction
            3. Collect and synthesise results
            4. Resolve any conflicts between agent outputs
            """,
        DefaultCapabilities = ["orchestration", "delegation", "synthesis", "planning"],
        DefaultHooks = new()
        {
            ["OnInit"] = "AgentDiscoveryHook",
        },
        DefaultTemperature = 0.4,
        DefaultMaxIterations = 25,
        PipelineStageDefaults = new()
        {
            ["Decompose"] = true,
            ["CapabilityMatch"] = true,
        },
    };

    public static readonly AgentArchetype Conversational = new()
    {
        Id = "conversational",
        DisplayName = "Conversational Agent",
        Description = "Optimised for multi-turn conversation with memory, personality, and emotional intelligence. Ideal for customer support, onboarding, and guided workflows.",
        Icon = "message-circle",
        Category = "Communication",
        SystemPromptTemplate = """
            You are a conversational AI assistant for {{company_name}}.
            Personality: {{personality_traits}}
            Guidelines:
            1. Be warm, empathetic, and professional
            2. Remember context from earlier in the conversation
            3. Ask clarifying questions when needed
            4. Guide users through multi-step processes
            5. Escalate to human support when you cannot help
            """,
        DefaultCapabilities = ["conversation", "customer-support", "onboarding", "faq"],
        DefaultHooks = new()
        {
            ["OnInit"] = "ConversationContextLoaderHook",
            ["OnAfterResponse"] = "SentimentTrackerHook",
        },
        DefaultTemperature = 0.8,
        DefaultMaxIterations = 6,
        DefaultVerificationMode = "Off",
        DefaultExecutionMode = AgentExecutionMode.ChatOnly, // Conversational agents default to chat-only — no tool execution
    };

    public static readonly AgentArchetype RemoteA2A = new()
    {
        Id = "remote-a2a",
        DisplayName = "Remote A2A Agent",
        Description = "Proxy agent that delegates to an external agent via the A2A protocol. Configure the remote endpoint URL and authentication.",
        Icon = "globe",
        Category = "Federation",
        SystemPromptTemplate = "", // Not used — remote agent has its own prompt
        DefaultCapabilities = ["a2a", "remote", "federation"],
        DefaultHooks = new()
        {
            ["OnInit"] = "A2ADiscoveryHook",
        },
        DefaultTemperature = 0,
        DefaultMaxIterations = 1,
    };

    /// <summary>All built-in archetypes indexed by ID.</summary>
    public static readonly IReadOnlyDictionary<string, AgentArchetype> All =
        new Dictionary<string, AgentArchetype>(StringComparer.OrdinalIgnoreCase)
        {
            [General.Id] = General,
            [Rag.Id] = Rag,
            [CodeAnalyst.Id] = CodeAnalyst,
            [DataAnalyst.Id] = DataAnalyst,
            [Researcher.Id] = Researcher,
            [Coordinator.Id] = Coordinator,
            [Conversational.Id] = Conversational,
            [RemoteA2A.Id] = RemoteA2A,
        };
}
```

#### `Diva.Agents/Hooks/BuiltIn/CitationEnforcerHook.cs` — CREATE (example)

```csharp
namespace Diva.Agents.Hooks.BuiltIn;

using Diva.Core.Models;

/// <summary>
/// Ensures the RAG agent's response includes source citations.
/// If no citations are detected, appends a warning note.
/// </summary>
public sealed class CitationEnforcerHook : IOnBeforeResponseHook
{
    public int Order => 50;

    public Task<string> OnBeforeResponseAsync(
        AgentHookContext context, string responseText, CancellationToken ct)
    {
        // Simple heuristic: look for citation patterns like [Source: ...] or (ref: ...)
        var hasCitations = responseText.Contains("[Source:", StringComparison.OrdinalIgnoreCase)
            || responseText.Contains("(ref:", StringComparison.OrdinalIgnoreCase)
            || responseText.Contains("[1]")
            || responseText.Contains("Source:");

        if (!hasCitations && !string.IsNullOrWhiteSpace(context.ToolEvidence))
        {
            responseText += "\n\n> **Note:** This response was generated from retrieved documents but specific source citations could not be automatically verified.";
        }

        return Task.FromResult(responseText);
    }
}
```

### Step 5: AgentDefinitionEntity — Add Archetype & Hooks Fields

#### Modify `Diva.Infrastructure/Data/Entities/AgentDefinitionEntity.cs`

Add these properties:

```csharp
/// <summary>Archetype ID (e.g. "rag", "code-analyst"). Null = "general".</summary>
public string? ArchetypeId { get; set; }

/// <summary>JSON dictionary of hook point → hook class name. Merged with archetype defaults at runtime.</summary>
public string? HooksJson { get; set; }

/// <summary>A2A endpoint URL for remote agents. When set, execution delegates via A2A client.</summary>
public string? A2AEndpoint { get; set; }

/// <summary>A2A auth scheme: Bearer | ApiKey. Used when calling remote agents.</summary>
public string? A2AAuthScheme { get; set; }

/// <summary>Secret reference for A2A auth. Resolved from secure storage at runtime.</summary>
public string? A2ASecretRef { get; set; }

/// <summary>Execution mode: Full (default), ChatOnly, ReadOnly, Supervised. Controls tool availability at runtime.</summary>
public string ExecutionMode { get; set; } = "Full";
```

Create EF migration for these new columns (including `ExecutionMode`).

#### Modify `McpToolBinding` (in `Diva.Infrastructure/LiteLLM/AnthropicAgentRunner.cs`)

Add `Access` property for `ExecutionMode.ReadOnly` enforcement (Gap #27):

```csharp
public sealed class McpToolBinding
{
    // ... existing properties (Name, Command, Args, Env, Endpoint, Transport, PassSsoToken, PassTenantHeaders) ...

    /// <summary>
    /// Access level classification for ExecutionMode enforcement.
    /// "ReadOnly" = safe in ReadOnly mode; "ReadWrite" = blocked in ReadOnly (default); "Destructive" = blocked in ReadOnly, optionally blockable in Full mode via hooks.
    /// Auto-suggested from binding name in Agent Builder UI.
    /// </summary>
    public string Access { get; set; } = "ReadWrite";
}
```

**No separate tool whitelist is needed.** When `ExecutionMode == ReadOnly`, the runner inspects each binding's `Access` tag and removes non-ReadOnly tools. Admins tag each binding **once** when configuring the agent — the same binding list serves all execution modes.

**Auto-suggestion in Agent Builder UI** (frontend helper — reduces manual tagging):

```typescript
function suggestAccessLevel(name: string): 'ReadOnly' | 'ReadWrite' | 'Destructive' {
  if (/delete|drop|purge|remove|destroy|truncate/i.test(name)) return 'Destructive';
  if (/search|lookup|get|list|read|query|fetch|find|check|status/i.test(name)) return 'ReadOnly';
  return 'ReadWrite';
}
```

### Step 6: Wire Hooks into AnthropicAgentRunner

#### Modify `Diva.Infrastructure/LiteLLM/AnthropicAgentRunner.cs`

**Note:** The runner constructor already accepts optional `ILlmConfigResolver? resolver` and `IPromptBuilder? promptBuilder` parameters (added by tenant grouping). Phase 15 adds `IAgentHookPipeline` (interface, not concrete) and `HookTypeRegistry` as additional optional constructor dependencies alongside these:

```csharp
public AnthropicAgentRunner(
    // ... existing required deps ...
    ILlmConfigResolver? resolver = null,       // ← tenant grouping (existing)
    IPromptBuilder? promptBuilder = null,       // ← tenant grouping (existing)
    IAgentHookPipeline? hookPipeline = null,   // ← Phase 15 (new) — INTERFACE for testability
    HookTypeRegistry? hookTypeRegistry = null)  // ← Phase 15 (new)
```

**AnthropicAgentRunner must also implement `IAgentRunner`** (new interface) so that `BaseCustomAgent` and controllers can depend on the interface, not the concrete class:

```csharp
// Diva.Core/Models/IAgentRunner.cs — CREATE
namespace Diva.Core.Models;

/// <summary>
/// Abstraction over the agent execution engine.
/// Enables unit testing of BaseCustomAgent and controllers without the full LLM chain.
/// </summary>
public interface IAgentRunner
{
    Task<AgentResponse> RunAsync(
        AgentDefinitionEntity definition, AgentRequest request, TenantContext tenant, CancellationToken ct);
    IAsyncEnumerable<AgentStreamChunk> InvokeStreamAsync(
        AgentDefinitionEntity definition, AgentRequest request, TenantContext tenant, CancellationToken ct);
}

// AnthropicAgentRunner.cs — add interface implementation:
public sealed class AnthropicAgentRunner : IAgentRunner
```

**ExecutionMode enforcement** — immediately after existing `FilterTools()` call, before tools reach the LLM:

```csharp
// Existing: static per-tool allow/deny filter
FilterTools(definition.ToolFilterJson, toolClientMap, allMcpTools, _logger);

// NEW: ExecutionMode enforcement (Gap #26)
var executionMode = Enum.TryParse<AgentExecutionMode>(definition.ExecutionMode, true, out var em)
    ? em : AgentExecutionMode.Full;

switch (executionMode)
{
    case AgentExecutionMode.ChatOnly:
        if (toolClientMap.Count > 0)
            _logger.LogInformation("ExecutionMode=ChatOnly: removing all {Count} tools", toolClientMap.Count);
        toolClientMap.Clear();
        allMcpTools.Clear();
        break;

    case AgentExecutionMode.ReadOnly:
        // Parse bindings to check Access tag (Gap #27)
        var bindingList = !string.IsNullOrWhiteSpace(definition.ToolBindings)
            ? JsonSerializer.Deserialize<List<McpToolBinding>>(definition.ToolBindings,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? []
            : new List<McpToolBinding>();
        var readOnlyNames = bindingList
            .Where(b => b.Access.Equals("ReadOnly", StringComparison.OrdinalIgnoreCase))
            .Select(b => b.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var toRemove = toolClientMap.Keys.Where(name => !readOnlyNames.Contains(name)).ToList();
        foreach (var name in toRemove)
        {
            toolClientMap.Remove(name);
            allMcpTools.RemoveAll(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
        if (toRemove.Count > 0)
            _logger.LogInformation("ExecutionMode=ReadOnly: removed {Count} non-read tool(s)", toRemove.Count);
        break;

    case AgentExecutionMode.Supervised:
        // Future: emit tool_approval_required SSE events at tool execution time
        _logger.LogInformation("ExecutionMode=Supervised: tools loaded, approval required per call");
        break;
}
```

Inject `AgentHookPipeline` into the runner. In `ExecuteReActLoopAsync`:

```csharp
// Resolve archetype and template variables BEFORE prompt enters TenantAwarePromptBuilder
var archetype = _archetypeRegistry?.GetById(definition.ArchetypeId ?? "general");
if (archetype?.SystemPromptTemplate is { Length: > 0 } archetypeTemplate)
{
    var vars = MergeVariables(archetype, definition.CustomVariablesJson);
    var resolved = archetypeTemplate;
    foreach (var (key, val) in vars)
        resolved = resolved.Replace($"{{{{{key}}}}}", val);
    systemPrompt = resolved; // feeds into TenantAwarePromptBuilder as base prompt
}

// Before ReAct loop starts:
var hookCtx = new AgentHookContext
{
    Request = request,
    Tenant = tenant,
    AgentId = definition.Id,
    ArchetypeId = definition.ArchetypeId ?? "general",
    SessionId = sessionId,
    SystemPrompt = systemPrompt,
    Variables = customVars,
};

var mergedHookConfig = MergeHookConfig(archetype.DefaultHooks, definition.HooksJson);
var hooks = _hookPipeline.ResolveHooks(mergedHookConfig, hookCtx.ArchetypeId);
await _hookPipeline.RunOnInitAsync(hooks, hookCtx, ct);
yield return new AgentStreamChunk { Type = "hook_executed", HookName = "OnInit", HookPoint = "init" };

// Use hookCtx.SystemPrompt (hooks may have modified it)
systemPrompt = hookCtx.SystemPrompt;

// Inside iteration loop, before LLM call — SAFE EXCEPTION-CAPTURE PATTERN:
Exception? hookEx = null;
try { await _hookPipeline.RunOnBeforeIterationAsync(hooks, hookCtx, iteration, ct); }
catch (Exception e) { hookEx = e; _logger.LogWarning(e, "OnBeforeIteration hook failed"); }
if (hookEx is not null)
    yield return new AgentStreamChunk { Type = "error", ErrorMessage = $"Hook error: {hookEx.Message}" };
yield return new AgentStreamChunk { Type = "hook_executed", HookName = "OnBeforeIteration", HookPoint = "before_iteration" };

// After tool calls are identified:
hookEx = null;
try
{
    var toolRefs = toolCalls.Select(tc => new UnifiedToolCallRef
        { Id = tc.Id, Name = tc.Name, InputJson = tc.InputJson }).ToList();
    toolRefs = await _hookPipeline.RunOnToolFilterAsync(hooks, hookCtx, toolRefs, ct);
    // Remove filtered calls
    toolCalls = toolCalls.Where(tc => !toolRefs.First(r => r.Id == tc.Id).Filtered).ToList();
}
catch (Exception e) { hookEx = e; _logger.LogWarning(e, "OnToolFilter hook failed"); }
if (hookEx is not null)
    yield return new AgentStreamChunk { Type = "error", ErrorMessage = $"Hook error: {hookEx.Message}" };
yield return new AgentStreamChunk { Type = "hook_executed", HookName = "OnToolFilter", HookPoint = "tool_filter" };

// After each tool result — same safe pattern:
hookEx = null;
try { toolOutput = await _hookPipeline.RunOnAfterToolCallAsync(hooks, hookCtx, toolName, toolOutput, isError, ct); }
catch (Exception e) { hookEx = e; _logger.LogWarning(e, "OnAfterToolCall hook failed"); }
if (hookEx is not null)
    yield return new AgentStreamChunk { Type = "error", ErrorMessage = $"Hook error: {hookEx.Message}" };
yield return new AgentStreamChunk { Type = "hook_executed", HookName = "OnAfterToolCall", HookPoint = "after_tool_call" };

// On tool error — invoke OnError hooks:
hookEx = null;
ErrorRecoveryAction recovery = ErrorRecoveryAction.Continue;
try { recovery = await _hookPipeline.RunOnErrorAsync(hooks, hookCtx, toolName, toolException, ct); }
catch (Exception e) { hookEx = e; _logger.LogWarning(e, "OnError hook failed"); }
if (hookEx is not null)
    yield return new AgentStreamChunk { Type = "error", ErrorMessage = $"Hook error: {hookEx.Message}" };
yield return new AgentStreamChunk { Type = "hook_executed", HookName = "OnError", HookPoint = "error" };
// Use `recovery` to decide: Continue / Retry / Abort

// Before verification — same safe pattern:
hookEx = null;
try { responseText = await _hookPipeline.RunOnBeforeResponseAsync(hooks, hookCtx, responseText, ct); }
catch (Exception e) { hookEx = e; _logger.LogWarning(e, "OnBeforeResponse hook failed"); }
if (hookEx is not null)
    yield return new AgentStreamChunk { Type = "error", ErrorMessage = $"Hook error: {hookEx.Message}" };
yield return new AgentStreamChunk { Type = "hook_executed", HookName = "OnBeforeResponse", HookPoint = "before_response" };

// After final AgentResponse is built:
hookEx = null;
try { await _hookPipeline.RunOnAfterResponseAsync(hooks, hookCtx, agentResponse, ct); }
catch (Exception e) { hookEx = e; _logger.LogWarning(e, "OnAfterResponse hook failed"); }
if (hookEx is not null)
    yield return new AgentStreamChunk { Type = "error", ErrorMessage = $"Hook error: {hookEx.Message}" };
yield return new AgentStreamChunk { Type = "hook_executed", HookName = "OnAfterResponse", HookPoint = "after_response" };
```

**Note:** Every hook call site uses the safe exception-capture pattern required by async iterators.
The `hook_executed` SSE chunk is emitted after each hook runs. `AgentStreamChunk` needs these new properties:

```csharp
// Add to AgentStreamChunk (Diva.Core/Models/AgentStreamChunk.cs)
public string? HookName { get; set; }      // e.g. "OnBeforeIteration"
public string? HookPoint { get; set; }     // e.g. "before_iteration"
public double? HookDurationMs { get; set; } // Execution time in ms
```

**Also update the XML doc comment** on `AgentStreamChunk` to include `hook_executed` in the type values list:
```
///   hook_executed       — lifecycle hook completed (HookName, HookPoint, HookDurationMs)
```

### Step 7: Archetype Registry Service

#### `Diva.Agents/Archetypes/IArchetypeRegistry.cs` — CREATE

```csharp
namespace Diva.Agents.Archetypes;

using Diva.Core.Configuration;

public interface IArchetypeRegistry
{
    IReadOnlyList<AgentArchetype> GetAll();
    AgentArchetype? GetById(string archetypeId);
    void Register(AgentArchetype archetype);
}
```

#### `Diva.Agents/Archetypes/ArchetypeRegistry.cs` — CREATE

```csharp
namespace Diva.Agents.Archetypes;

using System.Collections.Concurrent;
using Diva.Core.Configuration;

public sealed class ArchetypeRegistry : IArchetypeRegistry
{
    private readonly ConcurrentDictionary<string, AgentArchetype> _archetypes = new(StringComparer.OrdinalIgnoreCase);

    public ArchetypeRegistry()
    {
        // Register all built-in archetypes
        foreach (var (id, archetype) in BuiltInArchetypes.All)
            _archetypes[id] = archetype;
    }

    public IReadOnlyList<AgentArchetype> GetAll() => [.. _archetypes.Values];

    public AgentArchetype? GetById(string archetypeId) =>
        _archetypes.GetValueOrDefault(archetypeId);

    /// <summary>Register a custom archetype (e.g. tenant-specific or plugin-loaded).</summary>
    public void Register(AgentArchetype archetype) =>
        _archetypes[archetype.Id] = archetype;
}
```

---

## Tier B — A2A Protocol Surface (Phase 14 + enhancements)

### Step 8: A2A Configuration

#### `Diva.Core/Configuration/A2AOptions.cs` — CREATE

```csharp
namespace Diva.Core.Configuration;

public sealed class A2AOptions
{
    public const string SectionName = "A2A";

    /// <summary>Enable A2A endpoints (AgentCard, /tasks/send, /tasks/{id}).</summary>
    public bool Enabled { get; init; } = false;

    /// <summary>Seconds before a running task is automatically failed.</summary>
    public int TaskTimeoutSeconds { get; init; } = 300;

    /// <summary>Base URL for AgentCard generation (auto-detected if null).</summary>
    public string? BaseUrl { get; init; }

    /// <summary>Max A2A delegation depth to prevent infinite loops (Gap #24). Default 5.</summary>
    public int MaxDelegationDepth { get; init; } = 5;
}
```

### Step 9: AgentCard Builder (Archetype-Aware)

#### `Diva.Infrastructure/A2A/AgentCardBuilder.cs` — CREATE

```csharp
namespace Diva.Infrastructure.A2A;

using Diva.Agents.Archetypes;
using Diva.Core.Configuration;
using Diva.Infrastructure.Data.Entities;
using Microsoft.Extensions.Options;
using System.Text.Json;

/// <summary>Interface for AgentCard generation — mockable in controller tests.</summary>
public interface IAgentCardBuilder
{
    object BuildCard(AgentDefinitionEntity agent, string baseUrl);
}

public sealed class AgentCardBuilder : IAgentCardBuilder
{
    private readonly IArchetypeRegistry _archetypes;
    private readonly A2AOptions _a2aOptions;

    public AgentCardBuilder(IArchetypeRegistry archetypes, IOptions<A2AOptions> a2aOptions)
    {
        _archetypes = archetypes;
        _a2aOptions = a2aOptions.Value;
    }

    public object BuildCard(AgentDefinitionEntity agent, string baseUrl)
    {
        var archetype = _archetypes.GetById(agent.ArchetypeId ?? "general");
        var capabilities = string.IsNullOrEmpty(agent.Capabilities)
            ? archetype?.DefaultCapabilities ?? []
            : JsonSerializer.Deserialize<string[]>(agent.Capabilities) ?? [];

        var url = _a2aOptions.BaseUrl ?? baseUrl;

        return new
        {
            name = agent.DisplayName.Length > 0 ? agent.DisplayName : agent.Name,
            description = agent.Description,
            url = $"{url}/tasks/send?agentId={agent.Id}",
            version = agent.Version.ToString(),
            capabilities = new
            {
                streaming = true,
                pushNotifications = false,
            },
            skills = capabilities.Select(c => new
            {
                id = c,
                name = c,
                description = $"Capability: {c}",
            }).ToArray(),
            authentication = new
            {
                schemes = new[] { "Bearer" },
            },
            defaultInputModes = new[] { "text" },
            defaultOutputModes = new[] { "text" },
        };
    }
}
```

### Step 10: Task Persistence Entity

#### `Diva.Infrastructure/Data/Entities/AgentTaskEntity.cs` — CREATE

```csharp
namespace Diva.Infrastructure.Data.Entities;

public sealed class AgentTaskEntity : ITenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public int TenantId { get; set; }
    public string AgentId { get; set; } = "";

    /// <summary>pending | working | completed | failed | canceled</summary>
    public string Status { get; set; } = "pending";

    public string? InputJson { get; set; }
    public string? OutputText { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? SessionId { get; set; }
}
```

Add `DbSet<AgentTaskEntity> AgentTasks` to `DivaDbContext`.

### Step 11: A2A Server Endpoints

#### `Diva.Host/Controllers/AgentCardController.cs` — CREATE

```csharp
// GET /.well-known/agent.json         → default published agent card
// GET /.well-known/agent.json?agentId= → specific agent card
// Guarded by A2AOptions.Enabled
```

#### `Diva.Host/Controllers/AgentTaskController.cs` — CREATE

```csharp
// POST /tasks/send       → accepts A2A task, dispatches via IAgentRunner (not concrete), streams SSE
// GET  /tasks/{taskId}   → returns task status from AgentTaskEntity
// DELETE /tasks/{taskId}  → cancels in-flight task via CTS lookup

// SSE event mapping (Diva → A2A):
// plan/thinking/iteration_start → TaskStatusUpdateEvent { state: "working" }
// tool_call/tool_result         → TaskArtifactUpdateEvent { type: "tool_trace" }
// final_response                → TaskArtifactUpdateEvent { type: "text" }
// verification                  → TaskArtifactUpdateEvent { type: "verification" }
// done                          → TaskStatusUpdateEvent { state: "completed" }
// error                         → TaskStatusUpdateEvent { state: "failed" }

// ── Task cancellation (Gap #23) ──────────────────────────────────────────
// Controller maintains a ConcurrentDictionary<string, CancellationTokenSource> for running tasks.
// POST /tasks/send: creates a linked CTS (from the HTTP request CT) keyed by taskId.
// DELETE /tasks/{taskId}: looks up the CTS and calls Cancel(). Also sets DB status to "canceled".
// On task completion/failure: removes the CTS entry.
private static readonly ConcurrentDictionary<string, CancellationTokenSource> _runningTasks = new();

// ── Delegation depth protection (Gap #24) ────────────────────────────────
// POST /tasks/send reads the X-A2A-Depth header (default 0).
// If depth >= A2AOptions.MaxDelegationDepth (default 5), return 400:
//   { "error": "A2A delegation depth exceeded (max: 5)" }
// Otherwise, pass depth+1 into AgentRequest.Metadata["a2a_depth"] for
// outbound A2AAgentClient calls to propagate as X-A2A-Depth header.
```

### Step 12: A2A Client (Remote Delegation)

#### `Diva.Infrastructure/A2A/IA2AAgentClient.cs` — CREATE

```csharp
namespace Diva.Infrastructure.A2A;

using Diva.Core.Models;

public interface IA2AAgentClient
{
    /// <summary>Discover a remote agent's capabilities.</summary>
    Task<object> DiscoverAsync(string agentUrl, CancellationToken ct);

    /// <summary>Send a task to a remote agent and stream results back as AgentStreamChunks.</summary>
    IAsyncEnumerable<AgentStreamChunk> SendTaskAsync(
        string agentUrl, string? authToken, AgentRequest request, CancellationToken ct);
}
```

#### `Diva.Infrastructure/A2A/A2AAgentClient.cs` — CREATE

Implements `IA2AAgentClient`:
1. `GET {url}/.well-known/agent.json` → parse capabilities
2. `POST {url}/tasks/send` with Bearer → stream SSE
3. Translate A2A events back to `AgentStreamChunk`
4. **Delegation depth propagation (Gap #24):** Read `a2a_depth` from `AgentRequest.Metadata`, send as `X-A2A-Depth: {depth+1}` header on outbound `POST /tasks/send`. This allows the receiving server to enforce its own depth limit.

### Step 13: Remote Agent Worker

#### `Diva.Agents/Workers/IStreamableWorkerAgent.cs` — CREATE

```csharp
namespace Diva.Agents.Workers;

using Diva.Core.Models;

/// <summary>
/// Extends IWorkerAgent with streaming support.
/// Agents that implement this interface can be streamed directly
/// from the /invoke/stream endpoint, bypassing the default
/// AnthropicAgentRunner.InvokeStreamAsync path.
///
/// This solves Gap #3: RemoteA2AAgent (and any custom streaming agent)
/// can participate in SSE streaming without materialising the full response.
/// </summary>
public interface IStreamableWorkerAgent : IWorkerAgent
{
    /// <summary>
    /// Stream agent execution as SSE chunks.
    /// The controller yields these directly to the HTTP response.
    /// </summary>
    IAsyncEnumerable<AgentStreamChunk> InvokeStreamAsync(
        AgentRequest request, TenantContext tenant, CancellationToken ct);
}
```

#### `Diva.Agents/Workers/RemoteA2AAgent.cs` — CREATE

```csharp
namespace Diva.Agents.Workers;

using Diva.Core.Models;
using Diva.Infrastructure.A2A;
using Diva.Infrastructure.Data.Entities;
using System.Runtime.CompilerServices;

/// <summary>
/// Worker agent that delegates execution to a remote agent via A2A protocol.
/// Created by DynamicAgentRegistry when AgentDefinitionEntity has A2AEndpoint set.
///
/// Implements IStreamableWorkerAgent so the streaming endpoint can delegate
/// directly without materialising the full response first.
/// </summary>
public sealed class RemoteA2AAgent : IStreamableWorkerAgent
{
    private readonly AgentDefinitionEntity _definition;
    private readonly IA2AAgentClient _a2aClient;

    public RemoteA2AAgent(AgentDefinitionEntity definition, IA2AAgentClient a2aClient)
    {
        _definition = definition;
        _a2aClient  = a2aClient;
    }

    public AgentCapability GetCapability() => new()
    {
        AgentId = _definition.Id,
        AgentType = _definition.AgentType,
        Description = _definition.Description,
        Capabilities = JsonSerializer.Deserialize<string[]>(_definition.Capabilities ?? "[]") ?? [],
        Priority = 3, // Lower priority than local agents
    };

    /// <summary>Non-streaming path — materialises all chunks into a single response.</summary>
    public async Task<AgentResponse> ExecuteAsync(
        AgentRequest request, TenantContext tenant, CancellationToken ct)
    {
        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in InvokeStreamAsync(request, tenant, ct))
        {
            chunks.Add(chunk);
        }

        var final = chunks.LastOrDefault(c => c.Type == "final_response");
        return new AgentResponse
        {
            Success = final is not null,
            Content = final?.Content ?? "Remote agent returned no response",
            AgentName = _definition.DisplayName,
        };
    }

    /// <summary>Streaming path — proxies A2A task events as SSE chunks.</summary>
    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamAsync(
        AgentRequest request, TenantContext tenant,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var chunk in _a2aClient.SendTaskAsync(
            _definition.A2AEndpoint!, _definition.A2ASecretRef, request, ct))
        {
            yield return chunk;
        }
    }
}
```

### Step 14: DynamicAgentRegistry — Route Remote + Group Agents

#### Modify `Diva.Agents/Registry/DynamicAgentRegistry.cs`

Inject `ITenantGroupService` into the registry. In `GetAgentsForTenantAsync`, load both tenant-owned definitions AND group-shared agent templates:

```csharp
// Dependencies (use IServiceProvider.CreateScope() since registry is Singleton)
private readonly IAgentRunner _runner;          // ← IAgentRunner, NOT concrete AnthropicAgentRunner (Gap #22)
private readonly IA2AAgentClient _a2aClient;
private readonly ITenantGroupService _groupService;

// NEW: GetByIdAsync for targeted agent resolution (Gap #21)
// Used by AgentTaskController (A2A) and AgentsController
public async Task<IWorkerAgent?> GetByIdAsync(string agentId, int tenantId, CancellationToken ct)
{
    // Check static agents first
    if (_static.TryGetValue(agentId, out var staticAgent))
        return staticAgent;

    // Then check DB
    using var db = _db.CreateDbContext();
    var def = await db.AgentDefinitions
        .FirstOrDefaultAsync(d => d.Id == agentId && d.TenantId == tenantId && d.IsEnabled, ct);
    if (def is null) return null;

    return !string.IsNullOrEmpty(def.A2AEndpoint)
        ? new RemoteA2AAgent(def, _a2aClient)
        : new DynamicReActAgent(def, _runner);  // ← uses IAgentRunner
}

// In GetAgentsForTenantAsync:
// 1. Existing tenant-owned agents
foreach (var def in definitions)
{
    if (!string.IsNullOrEmpty(def.A2AEndpoint))
        agents.Add(new RemoteA2AAgent(def, _a2aClient));
    else
        agents.Add(new DynamicReActAgent(def, _runner));  // ← IAgentRunner (Gap #22)
}

// 2. Group-shared agent templates (NEW — resolves gap #11)
// GroupAgentTemplateEntity contains shared agent configs pushed to member tenants
using var scope = _serviceProvider.CreateScope();
var groupService = scope.ServiceProvider.GetRequiredService<ITenantGroupService>();
var groupTemplates = await groupService.GetAgentTemplatesForTenantAsync(tenantId, ct);
foreach (var tmpl in groupTemplates)
{
    // Skip if tenant already has an agent with the same AgentType (tenant override wins)
    if (agents.Any(a => a.GetCapability().AgentType == tmpl.AgentType))
        continue;

    var groupDef = MapGroupTemplateToDefinition(tmpl, tenantId);
    agents.Add(new DynamicReActAgent(groupDef, _runner));
}
```

**Helper method:**

```csharp
private static AgentDefinitionEntity MapGroupTemplateToDefinition(
    GroupAgentTemplateEntity tmpl, int tenantId) => new()
{
    Id = $"group-{tmpl.GroupId}-{tmpl.AgentType}",
    TenantId = tenantId,
    AgentType = tmpl.AgentType,
    DisplayName = tmpl.DisplayName,
    SystemPrompt = tmpl.SystemPrompt,
    ModelId = tmpl.ModelId,
    Temperature = tmpl.Temperature,
    MaxIterations = tmpl.MaxIterations,
    Capabilities = tmpl.Capabilities,
    ToolBindings = tmpl.ToolBindings,
    IsEnabled = true,
};
```

---

## Tier C — Admin Portal UI Enhancements

### Step 15: Archetype API Endpoints + Streaming Path for IStreamableWorkerAgent

#### Modify streaming endpoint in `AgentsController.cs`:

```csharp
// In the /invoke/stream endpoint, BEFORE calling _runner.InvokeStreamAsync:
// Check if the resolved agent implements IStreamableWorkerAgent.
// If so, delegate streaming directly to it instead of the runner.

[HttpPost("invoke/stream")]
public async Task InvokeStream([FromBody] AgentInvokeRequest body, CancellationToken ct)
{
    // ... existing setup code ...

    // Resolve the agent from registry
    var agent = await _registry.ResolveAgentAsync(body.AgentId, tenant, ct);

    if (agent is IStreamableWorkerAgent streamable)
    {
        // Remote A2A agents (and any custom streaming agents) handle their own streaming
        await foreach (var chunk in streamable.InvokeStreamAsync(request, tenant, ct))
        {
            await WriteChunkAsync(chunk);
        }
    }
    else
    {
        // Default path — use AnthropicAgentRunner for local agents
        await foreach (var chunk in _runner.InvokeStreamAsync(definition, request, tenant, ct))
        {
            await WriteChunkAsync(chunk);
        }
    }
}
```

#### Add to `AgentsController.cs`:

```csharp
// GET /api/agents/archetypes → list all available archetypes
[HttpGet("archetypes")]
public IActionResult GetArchetypes([FromServices] IArchetypeRegistry registry)
    => Ok(registry.GetAll());

// GET /api/agents/archetypes/{id} → get archetype details
[HttpGet("archetypes/{id}")]
public IActionResult GetArchetype(string id, [FromServices] IArchetypeRegistry registry)
{
    var archetype = registry.GetById(id);
    return archetype is null ? NotFound() : Ok(archetype);
}
```

#### Add to `api.ts`:

```typescript
export interface AgentArchetype {
  id: string;
  displayName: string;
  description: string;
  icon: string;
  category: string;
  systemPromptTemplate: string;
  defaultCapabilities: string[];
  suggestedTools: string[];
  defaultHooks: Record<string, string>;
  defaultTemperature: number;
  defaultMaxIterations: number;
  defaultVerificationMode?: string;
  pipelineStageDefaults?: Record<string, boolean>;
}

// api methods:
getArchetypes(): Promise<AgentArchetype[]>
getArchetype(id: string): Promise<AgentArchetype>
```

### Step 16: Archetype Selector in Agent Builder

Add a new **step 0** to the Agent Builder before the existing tabs — an archetype gallery:

```
┌──────────────────────────────────────────────────────────────────┐
│  Choose an Agent Archetype                                        │
│                                                                    │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐              │
│  │ 🤖 General  │  │ 📚 RAG      │  │ 💻 Code     │              │
│  │ Assistant   │  │ Knowledge   │  │ Analyst     │              │
│  │             │  │ Agent       │  │             │              │
│  │ Open-ended  │  │ Grounded in │  │ Code review │              │
│  │ tasks       │  │ documents   │  │ & debugging │              │
│  └─────────────┘  └─────────────┘  └─────────────┘              │
│                                                                    │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐              │
│  │ 📊 Data     │  │ 🔍 Research │  │ 🔗 Multi-   │              │
│  │ Analyst     │  │ Agent       │  │ Agent Coord │              │
│  │             │  │             │  │             │              │
│  │ SQL, stats, │  │ Deep-dive   │  │ Orchestrate │              │
│  │ reports     │  │ synthesis   │  │ sub-agents  │              │
│  └─────────────┘  └─────────────┘  └─────────────┘              │
│                                                                    │
│  ┌─────────────┐  ┌─────────────┐                                │
│  │ 💬 Convers- │  │ 🌐 Remote   │                                │
│  │ ational     │  │ A2A Agent   │                                │
│  │             │  │             │                                │
│  │ Multi-turn, │  │ Federated   │                                │
│  │ empathetic  │  │ delegation  │                                │
│  └─────────────┘  └─────────────┘                                │
│                                                                    │
│  [ Start from scratch (blank) ]                                   │
│                                                                    │
└──────────────────────────────────────────────────────────────────┘
```

When an archetype is selected:
- Pre-fill system prompt from template
- Set default temperature, max iterations
- Add default capabilities
- Pre-configure hooks
- Show archetype-specific config (e.g. RAG shows knowledge base selector)

### Step 17: Hook Configuration UI

Add a new tab in Agent Builder: **Hooks**

```
┌──────────────────────────────────────────────────────────────────┐
│  Lifecycle Hooks                                                  │
│                                                                    │
│  Configure behaviour at each stage of agent execution.            │
│  Hooks from archetype "RAG Knowledge Agent" are pre-applied.      │
│                                                                    │
│  ┌─ OnInit ──────────────────────────────────────────────────┐    │
│  │  Hook: [none]                                    [Add ▾]  │    │
│  │  Runs once when agent starts. Use for setup.              │    │
│  └───────────────────────────────────────────────────────────┘    │
│                                                                    │
│  ┌─ OnBeforeIteration ───────────────────────────────────────┐    │
│  │  Hook: [none]                                    [Add ▾]  │    │
│  │  Runs before each ReAct iteration.                        │    │
│  └───────────────────────────────────────────────────────────┘    │
│                                                                    │
│  ┌─ OnToolFilter ────────────────────────────────────────────┐    │
│  │  Hook: [none]                                    [Add ▾]  │    │
│  │  Filter/modify tool calls before execution.               │    │
│  └───────────────────────────────────────────────────────────┘    │
│                                                                    │
│  ┌─ OnAfterToolCall ─────────────────────────────────────────┐    │
│  │  Hook: [SqlResultFormatterHook]            [Change] [✕]   │    │
│  │  Transform tool results after execution.                  │    │
│  └───────────────────────────────────────────────────────────┘    │
│                                                                    │
│  ┌─ OnBeforeResponse ────────────────────────────────────────┐    │
│  │  Hook: [CitationEnforcerHook] (from archetype)[Change][✕] │    │
│  │  Format/transform final response text.                    │    │
│  └───────────────────────────────────────────────────────────┘    │
│                                                                    │
│  ┌─ OnAfterResponse ─────────────────────────────────────────┐    │
│  │  Hook: [none]                                    [Add ▾]  │    │
│  │  Side effects after response (webhooks, analytics).       │    │
│  └───────────────────────────────────────────────────────────┘    │
│                                                                    │
└──────────────────────────────────────────────────────────────────┘
```

### Step 18: A2A Configuration Tab

For agents with archetype "Remote A2A Agent", show an additional panel:

```
┌──────────────────────────────────────────────────────────────────┐
│  A2A Remote Agent Configuration                                   │
│                                                                    │
│  Endpoint URL: [https://other-platform.com____________]           │
│  Auth Scheme:  [Bearer ▾]                                        │
│  Secret Ref:   [vault://a2a/agent-key___________________]        │
│                                                                    │
│  [ Test Connection ]   ✅ Connected — 5 skills discovered        │
│                                                                    │
│  Discovered Skills:                                               │
│  ┌──────────┐ ┌──────────┐ ┌──────────────┐                     │
│  │ analytics│ │ forecast │ │ data-export  │ ...                   │
│  └──────────┘ └──────────┘ └──────────────┘                     │
│                                                                    │
└──────────────────────────────────────────────────────────────────┘
```

---

## Tier D — Custom C# Agent Classes (Developer Path)

### Step 19: BaseCustomAgent Abstract Class

For developers who want full C# control:

#### `Diva.Agents/Workers/BaseCustomAgent.cs` — CREATE

```csharp
namespace Diva.Agents.Workers;

using System.Runtime.CompilerServices;
using Diva.Core.Models;
using Diva.Infrastructure.Data;
using Diva.Infrastructure.LiteLLM;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

/// <summary>
/// Base class for custom C# agents that want full control over lifecycle.
/// Override individual hook methods to customise behaviour.
/// Still delegates LLM execution to AnthropicAgentRunner.
///
/// NOTE: Does NOT call a non-existent RunByTypeAsync.
/// Instead, looks up AgentDefinitionEntity from DB by AgentType,
/// then calls Runner.RunAsync(definition, request, tenant, ct).
///
/// Implements IStreamableWorkerAgent so that pre/post-processing
/// also runs during SSE streaming (Gap #17 fix).
///
/// IMPORTANT: AgentRequest and AgentResponse are sealed classes,
/// NOT records — do NOT use `with` expressions (Gap #15 fix).
/// </summary>
public abstract class BaseCustomAgent : IStreamableWorkerAgent
{
    protected readonly IAgentRunner Runner;  // ← INTERFACE, not concrete AnthropicAgentRunner
    protected readonly IDatabaseProviderFactory DbFactory;
    protected readonly ILogger Logger;

    protected BaseCustomAgent(
        IAgentRunner runner,              // ← INTERFACE — enables unit testing with NSubstitute mock
        IDatabaseProviderFactory dbFactory,
        ILogger logger)
    {
        Runner = runner;
        DbFactory = dbFactory;
        Logger = logger;
    }

    /// <summary>Agent type identifier — must match AgentDefinitionEntity.AgentType in DB.</summary>
    protected abstract string AgentType { get; }

    /// <summary>Agent capabilities for routing.</summary>
    protected abstract string[] GetCapabilities();

    /// <summary>Human-readable description.</summary>
    protected virtual string Description => $"{AgentType} custom agent";

    /// <summary>Priority for capability matching. Higher = preferred.</summary>
    protected virtual int Priority => 10;

    public AgentCapability GetCapability() => new()
    {
        AgentId = AgentType,
        AgentType = AgentType,
        Description = Description,
        Capabilities = GetCapabilities(),
        Priority = Priority,
    };

    /// <summary>Override to transform the request before execution.</summary>
    protected virtual Task<AgentRequest> PreProcessAsync(
        AgentRequest request, TenantContext tenant, CancellationToken ct)
        => Task.FromResult(request);

    /// <summary>Override to transform the response after execution.</summary>
    protected virtual Task<AgentResponse> PostProcessAsync(
        AgentResponse response, AgentRequest request, TenantContext tenant, CancellationToken ct)
        => Task.FromResult(response);

    /// <summary>Override to inject additional system prompt content.</summary>
    protected virtual Task<string?> GetAdditionalInstructionsAsync(
        AgentRequest request, TenantContext tenant, CancellationToken ct)
        => Task.FromResult<string?>(null);

    /// <summary>Apply pre-processing + instruction injection, return (processed request, definition).</summary>
    private async Task<(AgentRequest Processed, AgentDefinitionEntity Definition)> PrepareAsync(
        AgentRequest request, TenantContext tenant, CancellationToken ct)
    {
        // Pre-process
        var processed = await PreProcessAsync(request, tenant, ct);

        // Inject additional instructions — AgentRequest is a sealed class, NOT a record,
        // so we must construct a new instance instead of using `with` (Gap #15).
        var extra = await GetAdditionalInstructionsAsync(processed, tenant, ct);
        if (!string.IsNullOrWhiteSpace(extra))
        {
            processed = new AgentRequest
            {
                Query = processed.Query,
                SessionId = processed.SessionId,
                ModelId = processed.ModelId,
                PreferredAgent = processed.PreferredAgent,
                TriggerType = processed.TriggerType,
                Metadata = processed.Metadata,
                Instructions = string.IsNullOrWhiteSpace(processed.Instructions)
                    ? extra
                    : $"{processed.Instructions}\n\n{extra}",
            };
        }

        // Look up agent definition from DB — BaseCustomAgent owns this lookup
        // because AnthropicAgentRunner.RunByTypeAsync does NOT exist.
        await using var db = DbFactory.CreateDbContext(TenantContext.System(tenant.TenantId));
        var definition = await db.AgentDefinitions
            .FirstOrDefaultAsync(a => a.AgentType == AgentType && a.TenantId == tenant.TenantId, ct)
            ?? throw new InvalidOperationException(
                $"No AgentDefinitionEntity found for AgentType='{AgentType}' in tenant {tenant.TenantId}");

        return (processed, definition);
    }

    public async Task<AgentResponse> ExecuteAsync(
        AgentRequest request, TenantContext tenant, CancellationToken ct)
    {
        var (processed, definition) = await PrepareAsync(request, tenant, ct);

        // Execute via runner using the looked-up definition
        var response = await Runner.RunAsync(definition, processed, tenant, ct);

        // Post-process
        return await PostProcessAsync(response, processed, tenant, ct);
    }

    /// <summary>
    /// Streaming path — ensures PreProcess + GetAdditionalInstructions run
    /// before streaming begins. Post-processing cannot transform the stream
    /// (it's fire-and-forget per chunk), so OnAfterResponse hooks should be
    /// used for post-stream side effects instead.
    /// Solves Gap #17: controller detects IStreamableWorkerAgent and delegates here.
    /// </summary>
    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamAsync(
        AgentRequest request, TenantContext tenant,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var (processed, definition) = await PrepareAsync(request, tenant, ct);

        await foreach (var chunk in Runner.InvokeStreamAsync(definition, processed, tenant, ct))
        {
            yield return chunk;
        }
    }
}
```

**Usage example (3 lines to add a custom agent):**

```csharp
public sealed class ComplianceAgent : BaseCustomAgent
{
    protected override string AgentType => "Compliance";
    protected override string[] GetCapabilities() =>
        ["compliance", "policy-check", "regulatory", "audit"];

    protected override string Description =>
        "Checks requests against company compliance policies";

    public ComplianceAgent(
        IAgentRunner runner,              // ← IAgentRunner, not concrete class
        IDatabaseProviderFactory dbFactory,
        ILogger<ComplianceAgent> logger)
        : base(runner, dbFactory, logger) { }

    protected override Task<AgentRequest> PreProcessAsync(
        AgentRequest request, TenantContext tenant, CancellationToken ct)
    {
        // Inject compliance context — AgentRequest is sealed class, not record (Gap #15)
        return Task.FromResult(new AgentRequest
        {
            Query = request.Query,
            SessionId = request.SessionId,
            ModelId = request.ModelId,
            PreferredAgent = request.PreferredAgent,
            TriggerType = request.TriggerType,
            Metadata = request.Metadata,
            Instructions = $"Check all responses against tenant {tenant.TenantId} compliance policies. "
                         + "Flag any regulatory concerns. "
                         + (request.Instructions ?? ""),
        });
    }

    protected override Task<AgentResponse> PostProcessAsync(
        AgentResponse response, AgentRequest request, TenantContext tenant, CancellationToken ct)
    {
        // Add compliance disclaimer — AgentResponse is sealed class, not record (Gap #15)
        if (response.Success)
        {
            return Task.FromResult(new AgentResponse
            {
                Success = response.Success,
                Content = response.Content + "\n\n---\n*This response has been checked against compliance policies.*",
                AgentName = response.AgentName,
                SessionId = response.SessionId,
                ToolsUsed = response.ToolsUsed,
                ExecutionTime = response.ExecutionTime,
                Verification = response.Verification,
                ToolEvidence = response.ToolEvidence,
            });
        }
        return Task.FromResult(response);
    }
}
```

**Registration (in Program.cs):**

```csharp
// Register custom agents — they appear alongside DB-defined agents
builder.Services.AddSingleton<IWorkerAgent, ComplianceAgent>();
```

---

## File Summary — All Changes

### CREATE (new files)

| File | Project | Purpose |
|------|---------|---------|
| `Configuration/AgentArchetype.cs` | Diva.Core | Archetype model |
| `Models/IAgentLifecycleHook.cs` | Diva.Core | Hook interfaces + AgentHookContext |
| `Models/IAgentHookPipeline.cs` | Diva.Core | Hook pipeline interface (mockable for runner/controller tests) |
| `Models/IAgentRunner.cs` | Diva.Core | Agent execution interface (mockable for BaseCustomAgent tests) |
| `Configuration/A2AOptions.cs` | Diva.Core | A2A config options |
| `Hooks/AgentHookPipeline.cs` | Diva.Agents | Hook resolution + execution |
| `Hooks/HookTypeRegistry.cs` | Diva.Agents | Startup-built hook class → Type map (replaces runtime assembly scanning) |
| `Archetypes/IArchetypeRegistry.cs` | Diva.Agents | Registry interface |
| `Archetypes/ArchetypeRegistry.cs` | Diva.Agents | Implementation |
| `Archetypes/BuiltInArchetypes.cs` | Diva.Agents | 8 built-in archetypes |
| `Hooks/BuiltIn/CitationEnforcerHook.cs` | Diva.Agents | Example RAG hook |
| `Hooks/BuiltIn/CodeBlockFormatterHook.cs` | Diva.Agents | Example code hook |
| `Hooks/BuiltIn/SqlResultFormatterHook.cs` | Diva.Agents | Example data hook |
| `Workers/BaseCustomAgent.cs` | Diva.Agents | Abstract base for C# custom agents |
| `Workers/IStreamableWorkerAgent.cs` | Diva.Agents | Streaming interface for worker agents (extends IWorkerAgent) |
| `Workers/RemoteA2AAgent.cs` | Diva.Agents | A2A remote delegation worker (implements IStreamableWorkerAgent) |
| `A2A/AgentCardBuilder.cs` | Diva.Infrastructure | A2A AgentCard generation |
| `A2A/IA2AAgentClient.cs` | Diva.Infrastructure | Remote A2A client interface |
| `A2A/A2AAgentClient.cs` | Diva.Infrastructure | Remote A2A client implementation |
| `Data/Entities/AgentTaskEntity.cs` | Diva.Infrastructure | A2A task persistence |
| `Controllers/AgentCardController.cs` | Diva.Host | `GET /.well-known/agent.json` |
| `Controllers/AgentTaskController.cs` | Diva.Host | A2A task lifecycle endpoints |
| `components/ArchetypeSelector.tsx` | admin-portal | Archetype gallery UI |
| `components/HookEditor.tsx` | admin-portal | Hook configuration UI |
| `components/A2AConfigPanel.tsx` | admin-portal | Remote A2A config panel |

### MODIFY (existing files)

| File | Change |
|------|--------|
| `AgentDefinitionEntity.cs` | Add `ArchetypeId`, `HooksJson`, `A2AEndpoint`, `A2AAuthScheme`, `A2ASecretRef`, `ExecutionMode` |
| `DivaDbContext.cs` | Add `DbSet<AgentTaskEntity>` |
| `AnthropicAgentRunner.cs` | Implement `IAgentRunner` interface; add `IAgentHookPipeline?` + `HookTypeRegistry?` as additional optional constructor params (alongside existing `ILlmConfigResolver?` + `IPromptBuilder?` from tenant grouping), add hook calls in `ExecuteReActLoopAsync` (safe exception-capture pattern at every yield site) |
| `DynamicAgentRegistry.cs` | Route to `RemoteA2AAgent` when `A2AEndpoint` is set; inject `IA2AAgentClient`; **also inject `ITenantGroupService` and load group agent templates** for member tenants (gap #11); **add `GetByIdAsync` method** (Gap #21); **use `IAgentRunner` instead of concrete `AnthropicAgentRunner`** (Gap #22) |
| `DynamicReActAgent.cs` | Change constructor from `AnthropicAgentRunner` to `IAgentRunner` (Gap #22) |
| `IAgentRegistry.cs` | Add `Task<IWorkerAgent?> GetByIdAsync(string agentId, int tenantId, CancellationToken ct)` (Gap #21) |
| `TenantAwarePromptBuilder.cs` | No direct changes — but archetype `SystemPromptTemplate` must be resolved BEFORE the prompt enters `BuildAsync()`, so group/tenant overrides apply on top of the archetype base prompt |
| `AgentsController.cs` | Add `/archetypes` endpoints; update `/invoke/stream` to detect `IStreamableWorkerAgent` and delegate streaming |
| `Program.cs` | Register archetype registry, `HookTypeRegistry`, hook pipeline, A2A services |
| `appsettings.json` | Add `A2A` section |
| `AgentBuilder.tsx` | Add archetype selector, hooks tab, A2A config tab |
| `api.ts` | Add archetype + hook + A2A API types and methods |
| `McpToolBinding` (in `AnthropicAgentRunner.cs`) | Add `Access` property (`string`, default `"ReadWrite"`) |

---

## Implementation Order

```
Tier A — Core framework (can start immediately)
  Step 1: AgentArchetype model                    ← no deps
  Step 2: Lifecycle hook interfaces               ← no deps
  Step 3: AgentHookPipeline                       ← depends on Step 2
  Step 4: BuiltInArchetypes + example hooks       ← depends on Step 1
  Step 5: AgentDefinitionEntity migration         ← DB schema change
  Step 6: Wire hooks into AnthropicAgentRunner    ← depends on Steps 3, 5
  Step 7: ArchetypeRegistry service               ← depends on Step 4

Tier B — A2A Protocol (depends on Tier A)
  Step 8:  A2AOptions                             ← no deps
  Step 9:  AgentCardBuilder                       ← depends on Steps 7, 8
  Step 10: AgentTaskEntity + migration            ← DB schema change
  Step 11: A2A server endpoints                   ← depends on Steps 9, 10
  Step 12: A2A client                             ← no deps
  Step 13: RemoteA2AAgent                         ← depends on Step 12
  Step 14: DynamicAgentRegistry A2A + group agent routing ← depends on Step 13 + ITenantGroupService

Tier C — Admin Portal UI (depends on Tier A)
  Step 15: Archetype API endpoints                ← depends on Step 7
  Step 16: ArchetypeSelector component            ← depends on Step 15
  Step 17: HookEditor component                   ← frontend only
  Step 18: A2AConfigPanel component               ← depends on Tier B

Tier D — C# Developer Path (independent)
  Step 19: BaseCustomAgent abstract class         ← only depends on IWorkerAgent
```

---

## Verification Checklist

### Tier A
- [ ] `AgentArchetype` model compiles and is serializable
- [ ] All 7 lifecycle hook interfaces compile (including `IOnErrorHook`)
- [ ] `HookTypeRegistry` builds at startup and resolves hooks by class name (O(1) lookup)
- [ ] `AgentHookPipeline` resolves hooks via `HookTypeRegistry` (no `AppDomain` scanning)
- [ ] All 8 built-in archetypes are registered
- [ ] `CitationEnforcerHook` runs in RAG archetype
- [ ] DB migration adds `ArchetypeId`, `HooksJson`, `A2AEndpoint`, `A2AAuthScheme`, `A2ASecretRef`, `ExecutionMode`
- [ ] DB migration Designer.cs includes all tenant grouping entities in model snapshot (9 group tables)
- [ ] Hooks fire at correct points in `ExecuteReActLoopAsync`
- [ ] `hook_executed` SSE events are emitted after each hook and visible in UI
- [ ] `OnError` hook fires on tool failure and returns correct `ErrorRecoveryAction`
- [ ] Safe exception-capture pattern enforced at every hook call site (no `yield return` inside `try/catch`)
- [ ] Template variables (`{{key}}`) in archetype prompts are resolved correctly
- [ ] Archetype runtime merge: agent-level overrides always win over archetype defaults
- [ ] Archetype selection pre-fills form in Agent Builder
- [ ] Archetype `SystemPromptTemplate` resolved BEFORE `TenantAwarePromptBuilder.BuildAsync()` — group/tenant overrides apply on top
- [ ] Prompt priority hierarchy: archetype → group overrides → tenant overrides → group rules (§50) → tenant rules (§100) → session rules → hook mods
- [ ] `ExecutionMode.ChatOnly` clears all tools — LLM sees zero tools, no `tool_call` events possible
- [ ] `ExecutionMode.ReadOnly` filters tools by `McpToolBinding.Access` — only `ReadOnly` bindings loaded
- [ ] `ExecutionMode.Full` (default) loads all tools as before
- [ ] `ToolAccessLevel` per `McpToolBinding` stored in binding JSON, survives serialization/deserialization
- [ ] Archetype `DefaultExecutionMode` pre-fills Agent Builder and applies at runtime (agent-level always wins)

### Tier B
- [ ] `GET /.well-known/agent.json` returns valid A2A AgentCard
- [ ] `POST /tasks/send` streams SSE events
- [ ] `GET /tasks/{id}` returns task status
- [ ] `DELETE /tasks/{id}` cancels running task via CTS lookup (Gap #23)
- [ ] `DELETE /tasks/{id}` sets DB status to `canceled` and CTS is cleaned up
- [ ] `A2A.Enabled: false` → all A2A endpoints return 404
- [ ] Delegation depth ≥ `MaxDelegationDepth` returns 400 on `POST /tasks/send` (Gap #24)
- [ ] `X-A2A-Depth` header is propagated through outbound `A2AAgentClient` calls
- [ ] `RemoteA2AAgent` delegates via `A2AAgentClient`
- [ ] `RemoteA2AAgent` streams via `IStreamableWorkerAgent.InvokeStreamAsync`
- [ ] `AgentsController` streaming endpoint detects `IStreamableWorkerAgent` and delegates
- [ ] `DynamicAgentRegistry` routes to remote agents correctly
- [ ] `DynamicAgentRegistry.GetByIdAsync` resolves specific agent by ID (Gap #21)
- [ ] `DynamicAgentRegistry` uses `IAgentRunner` (not concrete `AnthropicAgentRunner`) (Gap #22)
- [ ] `DynamicReActAgent` constructor accepts `IAgentRunner` (not concrete) (Gap #22)
- [ ] `DynamicAgentRegistry` loads group agent templates for member tenants (via `ITenantGroupService`)
- [ ] Group agent templates appear in registry alongside tenant-owned agents
- [ ] Tenant-owned agent with same `AgentType` overrides group template (tenant wins)

### Tier C
- [ ] Archetype gallery loads and displays all archetypes
- [ ] Selecting archetype pre-fills all form fields
- [ ] Hook editor shows hook points with dropdowns
- [ ] A2A config panel shows endpoint + auth + test connection
- [ ] Agent Builder saves `archetypeId` and `hooksJson`
- [ ] Agent Builder shows `ExecutionMode` dropdown (Full / ChatOnly / ReadOnly / Supervised)
- [ ] MCP binding row shows `Access` dropdown (ReadOnly / ReadWrite / Destructive) with auto-suggestion from name

### Tier D
- [ ] `BaseCustomAgent` depends on `IAgentRunner` interface, not concrete `AnthropicAgentRunner`
- [ ] `BaseCustomAgent` looks up `AgentDefinitionEntity` from DB (no `RunByTypeAsync` dependency)
- [ ] `BaseCustomAgent` derivative registers and executes
- [ ] Custom agent appears in registry alongside DB agents
- [ ] Pre/post processing hooks fire correctly
- [ ] `BaseCustomAgent` implements `IStreamableWorkerAgent` — streaming includes pre-processing (Gap #17)
- [ ] `BaseCustomAgent` uses manual construction (no `with` on sealed classes) (Gap #15)
- [ ] `BaseCustomAgent` unit testable with `Substitute.For<IAgentRunner>()` + `DirectDbFactory`

### Interfaces & Testability
- [ ] `IAgentHookPipeline` interface exists in Diva.Core and is mockable
- [ ] `IAgentRunner` interface exists in Diva.Core and is mockable
- [ ] `IAgentCardBuilder` interface exists in Diva.Infrastructure and is mockable
- [ ] All DI registrations use interface → sealed class pattern
- [ ] No concrete service class appears in any constructor (only interfaces/IOptions)

### Build
- [ ] `dotnet build Diva.slnx` passes
- [ ] `dotnet test` passes
- [ ] `npm run build` passes (admin-portal)
- [ ] `npm run lint` passes

---

## Testing Strategy

| Layer | Approach |
|-------|----------|
| Hook pipeline | Unit test — mock hooks via `IOnInitHook` etc., verify call order and data flow |
| IAgentHookPipeline | Unit test — `Substitute.For<IAgentHookPipeline>()` in runner tests to isolate hook logic |
| HookTypeRegistry | Unit test — build from test assembly, verify O(1) resolve by class name |
| OnError hook | Unit test — verify `ErrorRecoveryAction` (Continue/Retry/Abort) propagation |
| Archetype registry | Unit test — `IArchetypeRegistry` mockable; verify all built-in archetypes loadable |
| Archetype merge | Unit test — verify agent-level overrides win at both creation and runtime |
| Hook resolution | Integration test — real DI container, resolve hooks via `HookTypeRegistry` |
| hook_executed SSE | Integration test — verify chunk emitted after each hook with correct fields |
| A2A endpoints | Integration test (SQLite) — verify request/response format |
| A2A client | Unit test with mock HTTP handler — verify SSE parsing + `X-A2A-Depth` propagation |
| A2A task cancellation | Integration test — POST task, DELETE while running, verify status = `canceled` + CTS fires |
| A2A delegation depth | Unit test — verify 400 when `X-A2A-Depth >= MaxDelegationDepth`; verify header incremented on outbound |
| IAgentRegistry.GetByIdAsync | Unit test — static fallback, DB lookup, remote vs local routing |
| A2A card controller | Unit test — `Substitute.For<IAgentCardBuilder>()` to test controller in isolation |
| RemoteA2AAgent streaming | Unit test — `Substitute.For<IA2AAgentClient>()` + verify `IStreamableWorkerAgent.InvokeStreamAsync` proxies chunks |
| AgentBuilder UI | MSW mock — archetype list, hook config, A2A test connection |
| BaseCustomAgent | Unit test — `Substitute.For<IAgentRunner>()` + `DirectDbFactory` for DB lookup + pre/post processing pipeline |
| ExecutionMode ChatOnly | Integration test — set `ExecutionMode = "ChatOnly"`, verify `tools_available` reports 0 tools, no `tool_call` events in stream |
| ExecutionMode ReadOnly | Integration test — set `ExecutionMode = "ReadOnly"`, bind 3 tools (1 ReadOnly, 2 ReadWrite), verify only ReadOnly tool is available |
| ToolAccessLevel binding | Unit test — serialize/deserialize `McpToolBinding` with `Access = "ReadOnly"`, verify value survives round-trip |
| Access auto-suggestion | Unit test (frontend) — verify `suggestAccessLevel("search-knowledge")` returns `ReadOnly`, `suggestAccessLevel("delete-user")` returns `Destructive` |

---

## Future Extensions (Not in Scope)

- **Tenant-scoped archetypes** — tenants create custom archetypes via UI (Gap #9 — explicitly deferred)
- **Archetype marketplace** — share archetypes across tenants
- **Visual hook editor** — drag-and-drop hook pipeline builder
- **Hook scripting** — JavaScript/Python hooks evaluated at runtime (sandboxed)
- **A2A push notifications** — webhooks for task completion (A2A spec optional capability)
- **A2A query-based discovery** — agent search/filter RPC for federated platforms
- **A2A secret vault integration** — resolve `A2ASecretRef` from Azure Key Vault / HashiCorp Vault
- **Distributed task state** — replace in-memory `ConcurrentDictionary<string, CTS>` with Redis-backed task tracking for multi-node deployments
- **Agent versioning** — version control for agent configs with rollback
- **Agent metrics dashboard** — per-archetype performance analytics
- **GroupAgentTemplateEntity archetype fields** — add `ArchetypeId`, `HooksJson`, `A2AEndpoint`, `A2AAuthScheme`, `A2ASecretRef`, `ExecutionMode` to `GroupAgentTemplateEntity` so group-shared agents can leverage archetypes and hooks (Gap #19 — intentionally deferred from MVP)
- **Supervised execution mode** — `tool_approval_required` SSE event + SignalR approval/rejection flow for human-in-the-loop tool execution
- **MCP SDK annotations** — when `ModelContextProtocol` SDK adds tool annotation support (`ReadOnly`/`Destructive` flags from the MCP spec), auto-populate `ToolAccessLevel` from MCP metadata instead of manual tagging
