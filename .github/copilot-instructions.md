# GitHub Copilot — Diva AI Platform Instructions

## Session Context

**Before generating code, read [`docs/agents.md`](../docs/agents.md).**
It is the single shared dev cycle guide — current platform state, key files, ReAct loop patterns,
SSE event master list, shared state map, context window injection sites, Anthropic SDK gotchas,
and all active deferred items.

Also check [`docs/changelog.md`](../docs/changelog.md) for completed features and the pending/deferred item list.

---

## Project Identity

Diva is a multi-tenant enterprise AI agent platform built on **.NET 10** + **Semantic Kernel (SK)**.
Agents are tenant-isolated, business-rule-driven, and dynamically configurable at runtime.

---

## Solution Layout

```
src/
  Diva.Core/           → Models, DTOs, interfaces, config classes. No external dependencies.
  Diva.Infrastructure/ → EF Core DbContext, OAuth/JWT validation, AnthropicAgentRunner, Sessions, Rule Learning
  Diva.Agents/         → SK ChatCompletionAgent wrappers, Supervisor pipeline, DynamicAgentRegistry
  Diva.Tools/          → MCP tool base classes, TenantAwareMcpClient, domain tool servers
  Diva.TenantAdmin/    → TenantBusinessRulesService, TenantAwarePromptBuilder, PromptTemplateStore
  Diva.Host/           → ASP.NET Core 10 host: controllers, SignalR hub, middleware wiring
admin-portal/          → React + Vite + TypeScript
tests/                 → Diva.Agents.Tests, Diva.Tools.Tests, Diva.TenantAdmin.Tests
prompts/               → Versioned .txt prompt template files
docs/                  → Phase-by-phase implementation docs + architecture references
```

---

## Language & Framework Versions

- C# 13 / .NET 10
- Semantic Kernel `1.x` (`Microsoft.SemanticKernel`)
- EF Core 10
- ASP.NET Core 10
- React + Vite + TypeScript 5
- `Anthropic.SDK 5.10.0`, `Microsoft.Extensions.AI.Abstractions 10.4.1`, `ModelContextProtocol` SDK

---

## C# Conventions

### Naming

```csharp
// Interfaces: I-prefix
public interface ITenantAwarePromptBuilder { }

// Async methods: Async suffix
public Task<AgentResponse> ExecuteAsync(AgentRequest request, CancellationToken ct);

// Options/config classes: Options suffix, bind from IConfiguration
public sealed class LlmOptions { public const string SectionName = "LLM"; }

// Entities: Entity suffix (DB models)
public class TenantBusinessRuleEntity : ITenantEntity { }

// Domain models (service layer): no suffix
public class TenantBusinessRule { }
```

### Namespaces — must match folder path exactly

```csharp
namespace Diva.Infrastructure.Learning;   // src/Diva.Infrastructure/Learning/
namespace Diva.Agents.Supervisor.Stages;  // src/Diva.Agents/Supervisor/Stages/
namespace Diva.TenantAdmin.Prompts;       // src/Diva.TenantAdmin/Prompts/
```

### Dependency Injection — always use constructor injection

```csharp
public class RuleLearningService : IRuleLearningService
{
    private readonly DivaDbContext _db;
    private readonly LlmRuleExtractor _extractor;

    public RuleLearningService(DivaDbContext db, LlmRuleExtractor extractor)
    {
        _db = db;
        _extractor = extractor;
    }
}
```

### CancellationToken — always last parameter, always named `ct`

```csharp
public async Task<List<SuggestedRule>> GetPendingRulesAsync(int tenantId, CancellationToken ct)
```

### Records for immutable data, sealed classes for services

```csharp
public record SubTask(string Description, string[] RequiredCapabilities, int SiteId, int TenantId);
public sealed class TenantContextMiddleware { }
```

---

## Key Types — Always Use These

| Type | Location | Purpose |
|------|----------|---------|
| `TenantContext` | `Diva.Core.Models` | All tenant/user context — never construct manually in business logic |
| `AgentRequest` / `AgentResponse` | `Diva.Core.Models` | Agent I/O contracts |
| `AgentStreamChunk` | `Diva.Core.Models` | SSE event emitted per ReAct step (type values: `tools_available`, `plan`, `plan_revised`, `iteration_start`, `thinking`, `tool_call`, `tool_result`, `continuation_start`, `correction`, `final_response`, `verification`, `rule_suggestion`, `error`, `done`) |
| `AnthropicAgentRunner` | `Diva.Infrastructure.LiteLLM` | Primary agent execution engine — handles both Anthropic SDK and OpenAI-compatible providers |
| `McpToolBinding` | `Diva.Infrastructure.LiteLLM` | Per-binding config: `Name`, `Command`, `Args`, `Env`, `Endpoint`, `Transport`, `CredentialRef`, `PassSsoToken`, `PassTenantHeaders` |
| `ICredentialEncryptor` | `Diva.Core.Configuration` | AES-256-GCM encrypt/decrypt for MCP credential secrets |
| `ICredentialResolver` | `Diva.Core.Configuration` | Resolves `CredentialRef` → decrypted `ResolvedCredential` (ApiKey, AuthScheme, CustomHeaderName) |
| `IPlatformApiKeyService` | `Diva.Core.Configuration` | Platform API key create/validate/revoke/rotate (`diva_` prefix, SHA-256 hashed) |
| `DivaDbContext` | `Diva.Infrastructure.Data` | Single EF context — always scoped |
| `IPromptBuilder` | `Diva.Core` | Interface for prompt augmentation |
| `TenantAwarePromptBuilder` | `Diva.TenantAdmin.Prompts` | Augments base prompt with tenant rules, session rules, prompt overrides |
| `IWorkerAgent` | `Diva.Agents` | Interface all worker agents implement |
| `ISupervisorPipelineStage` | `Diva.Agents.Supervisor` | Each supervisor stage implements this |

---

## Agent Execution Architecture

`AnthropicAgentRunner` is the primary engine. It splits on `LlmOptions.DirectProvider.Provider`:

| Provider value | Path | Notes |
|---|---|---|
| `"Anthropic"` | Native `Anthropic.SDK` + manual ReAct loop | Avoids ME.AI version conflict |
| Anything else | `IChatClient` (OpenAI-compatible) + manual ReAct loop | Works for LM Studio, Ollama, LiteLLM, Azure OpenAI |

**ReAct loop pattern (streaming path):**

```
tools_available → [plan] → iteration_start → thinking → tool_call(s) → tool_result(s) → (repeat) → final_response → verification → done
```

**Parallel tool execution:** When the LLM returns multiple tool calls in one response, all are executed concurrently via `Task.WhenAll`. Pattern: emit all `tool_call` SSE events → execute all in parallel → emit all `tool_result` events in order.

**Plan detection:** If the LLM's first-iteration text contains ≥2 numbered lines (`^\d+\.\s+`), it is emitted as `plan` instead of `thinking`. Subsequent detection uses the same `ParsePlanSteps()` regex helper.

**Adaptive re-planning:** `executionLog` + `consecutiveFailures` track tool outcomes. After 2+ consecutive failures, an execution summary is injected into message history and a no-tools LLM call produces a revised plan (`plan_revised` chunk).

**Per-tool timeout:** Every `CallToolAsync` is wrapped with a 30-second `CancellationTokenSource`. Timeout returns `"Tool '{name}' timed out after 30s. Try a narrower query."`.

---

## Critical C# Patterns

### `yield return` cannot appear inside `try/catch` in async iterators

```csharp
// WRONG — compiler error
try { yield return chunk; } catch { }

// CORRECT — capture exception, yield outside the catch
Exception? ex = null;
try { result = await SomeAsync(); }
catch (Exception e) { ex = e; }
if (ex is not null) _logger.LogWarning(ex, "...");
yield return new AgentStreamChunk { ... };
```

### Tool result messages must immediately follow assistant tool_calls (OpenAI API rule)

```csharp
// Collect ALL tool results first, then add to message history
oaiMessages.Add(new ChatMessage(ChatRole.Tool, allToolResultContents));

// THEN add any User/Assistant re-plan messages
oaiMessages.Add(new ChatMessage(ChatRole.User, replanSummary));
```

### McpToolBinding JSON deserialization must be case-insensitive

```csharp
// Frontend serializes camelCase; C# properties are PascalCase
bindings = JsonSerializer.Deserialize<List<McpToolBinding>>(json,
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
```

### Anthropic.SDK 5.10.0 type mappings

| Use this | Not this |
|----------|----------|
| `IList<Anthropic.SDK.Common.Tool>` for `MessageParameters.Tools` | `List<Anthropic.SDK.Messaging.Tool>` |
| `"tool_use"` (string) for stop reason | `StopReason.ToolUse` enum |
| `[new Anthropic.SDK.Messaging.TextContent { Text = "..." }]` | `Content = "string"` |
| `Anthropic.SDK.Messaging.TextContent` | Unqualified `TextContent` (ambiguous with ME.AI) |
| `ModelContextProtocol.Protocol.TextContentBlock` | MCP `TextContent` |

### ME.AI ChatResponse API

```csharp
// Use .Messages (plural) not .Message
var functionCalls = response.Messages
    .SelectMany(m => m.Contents.OfType<FunctionCallContent>())
    .ToList();

// FunctionResultContent constructor: (callId, result) — NOT 3 args
new FunctionResultContent(fc.CallId, toolOutput)
```

---

## Patterns to Follow

### Every DB entity must implement `ITenantEntity`

```csharp
public class LearnedRuleEntity : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; }   // Required — drives EF query filter
    // ...
}
```

### Agent worker implementation (3 lines to add a new agent)

```csharp
public class WeatherAgent : BaseReActAgent
{
    protected override string AgentType => "Weather";
    protected override string[] GetCapabilities() =>
        ["weather", "forecast", "conditions", "rain-delay"];
}
```

### Suppress SK experimental warnings at file level

```csharp
#pragma warning disable SKEXP0110
// SK ChatCompletionAgent, AgentGroupChat are experimental
```

### Always invalidate cache after business rule writes

```csharp
await _rules.UpdateRuleAsync(tenantId, ruleId, rule, ct);
await _rules.InvalidateCacheAsync(tenantId, "*");   // ← required
```

### TenantContext flows from middleware — never build it manually

```csharp
// In controllers
var tenant = HttpContext.GetTenantContext();

// In services — inject via constructor, receive from caller
public async Task<AgentResponse> ExecuteAsync(
    AgentRequest request, TenantContext tenant, CancellationToken ct)
```

### Prompt templates live in `prompts/` — never hardcode in C#

```csharp
// Load via PromptTemplateStore, not string literals
var template = await _promptStore.GetAsync(agentType, "section", ct);
```

### Docker MCP Gateway binding

```json
{ "name": "docker-mcp", "command": "docker", "args": ["mcp", "gateway", "run"] }
```
This is **stdio transport** (default). HTTP/SSE requires explicit `--transport sse --port 8811`.

---

## React / TypeScript Conventions

```tsx
// Components: PascalCase filename + named export
export function BusinessRulesPage() { }

// All API types in src/api.ts — AgentStreamChunk includes:
// toolCount, toolNames, planSteps, planText (added for plan/tools_available events)
export interface AgentStreamChunk {
  type: string;
  planSteps?: string[];
  planText?: string;
  toolCount?: number;
  // ...
}

// SSE streaming: use api.streamAgent() with onChunk callback
await api.streamAgent(agentId, query, sessionId, (chunk) => {
  switch (chunk.type) {
    case "tools_available":    /* show tool count */ break;
    case "plan":               /* render plan card */ break;
    case "plan_revised":       /* update plan card */ break;
    case "tool_call":          /* show tool name + input */ break;
    case "tool_result":        /* show output */ break;
    case "continuation_start": /* show "Continuing (window N)…" status */ break;
    case "correction":         /* verification triggered re-iteration */ break;
    case "final_response":     /* render answer */ break;
    case "verification":       /* render verification badge */ break;
  }
}, abortController.signal);
```

---

## What NOT to Generate

- Don't hardcode prompts as C# string literals — load from `prompts/` via `PromptTemplateStore`
- Don't use `IMemoryCache` directly for session rules — use `IDistributedCache`
- Don't add `using` statements for `Microsoft.AutoGen` unless explicitly for A2A external agent communication
- Don't create DbContext as singleton — it must be scoped
- Don't call `new TenantContext()` in business logic — it must come from JWT middleware
- Don't mock the database in integration tests — use real SQLite
- Don't skip `CancellationToken` parameters on any async method
- Don't use `yield return` inside `try/catch` in async iterators — capture exception first, yield after
- Don't add User/Assistant messages between tool result messages in OpenAI message history — all tool results must immediately follow the assistant tool_calls message
- Don't deserialize `McpToolBinding` JSON without `PropertyNameCaseInsensitive = true`
- Don't use `DynamicAgentRegistry` as singleton with scoped `DivaDbContext` directly — use `IServiceProvider.CreateScope()`
- Don't use `LlmClientFactory` / SK Kernel for direct provider path — use `AnthropicAgentRunner`

---

## File Reference

| Need | Read |
|------|------|
| Current platform state, patterns, key files | `docs/agents.md` ← start here |
| Completed features + pending items | `docs/changelog.md` |
| Doc vs implementation alignment | `docs/discrepancies.md` |
| Phase implementation details | `docs/phase-NN-*.md` |
| Architecture diagrams | `docs/arch-*.md` |
| Current phase status | `docs/INDEX.md` |
| Docker/K8s/appsettings | `docs/ref-config.md` |
| Coding standards | `docs/conventions.md` |
| Why decisions were made | `docs/decisions.md` |
| Test strategy | `docs/testing.md` |
