# Performance Improvements — Agent Response Latency

> **Status:** `[x]` Done
> **Related phases:** [phase-09-llm-client.md](phase-09-llm-client.md), [phase-11-rule-learning.md](phase-11-rule-learning.md), [phase-06-tenant-admin.md](phase-06-tenant-admin.md)

---

## Problem

Every agent invocation — even a trivial "hi" message with no tool calls — was slow due to four compounding issues:

1. A **second full LLM call** (rule extraction) ran synchronously after every response
2. **MCP clients were created and destroyed per-request** — including spawning docker processes for stdio bindings
3. **All four provider paths** (streaming Anthropic, streaming OpenAI, non-streaming Anthropic, non-streaming OpenAI) called `ListToolsAsync` multiple times, once for routing and again for tool definitions
4. **Prompt builder DB queries** ran sequentially (rules → session rules → overrides)

---

## Fix 1: Fire-and-Forget Rule Extraction

**File:** `src/Diva.Infrastructure/LiteLLM/AnthropicAgentRunner.cs`

`LlmRuleExtractor.ExtractAsync` makes a full LLM call to extract business rules from a conversation transcript. This was `await`ed before returning the agent response, adding 500–2000ms to every turn.

**Change:** Both the non-streaming path (`RunAsync`) and streaming path (`InvokeStreamAsync`) now schedule extraction via `Task.Run(..., CancellationToken.None)` and return immediately.

```csharp
// Non-blocking — does not delay the response
_ = Task.Run(async () =>
{
    try { await _ruleLearner.ExtractRulesFromConversationAsync(capturedSessionId, transcript, CancellationToken.None); }
    catch (Exception ex) { _logger.LogWarning(ex, "Background rule extraction failed"); }
}, CancellationToken.None);
```

**Trade-off:** Rule suggestions (`rule_suggestion` SSE chunk) are no longer emitted in the streaming path. Extraction still runs in the background and logs normally.

---

## Fix 2: MCP Client Cache

**New file:** `src/Diva.Infrastructure/LiteLLM/McpClientCache.cs`

MCP clients (especially stdio/docker) were created fresh on every invocation: process spawn + protocol handshake + `ListToolsAsync`. Even with parallel connections, this cost 100–500ms per binding per request.

**Change:** Singleton `McpClientCache` caches `Dictionary<string, McpClient>` instances keyed by `(agentId, MD5(toolBindings))` with a 30-minute TTL.

```
First request  → connects (cold; pays full spawn + handshake cost)
Subsequent     → returns cached clients (near-zero overhead)
Bindings change → hash mismatch → evicts old entry, reconnects
```

**Registration:** `builder.Services.AddSingleton<McpClientCache>()` in `Program.cs`.

Both `RunAsync` and `InvokeStreamAsync` now call `_mcpCache.GetOrConnectAsync(...)` instead of `ConnectMcpClientsAsync` directly. The `finally { DisposeAsync }` blocks were removed — the cache owns client lifetime.

---

## Fix 3: Single-Pass Tool Listing (`BuildToolDataAsync`)

**File:** `src/Diva.Infrastructure/LiteLLM/AnthropicAgentRunner.cs`

All four provider paths were calling `ListToolsAsync` redundantly:

| Path | Issue |
|------|-------|
| Non-streaming Anthropic | `BuildToolClientMapAsync` (called ListToolsAsync) + another `foreach mc.ListToolsAsync()` |
| Non-streaming OpenAI | Sequential `foreach mc.ListToolsAsync()` |
| Streaming Anthropic | `BuildToolClientMapAsync` at line 183 + another `foreach mc.ListToolsAsync()` |
| Streaming OpenAI | `BuildToolClientMapAsync` at line 183 + another `foreach mc.ListToolsAsync()` |

**Change:** Replaced `BuildToolClientMapAsync` with `BuildToolDataAsync` that returns both the routing map AND the raw tool list in a single parallel pass:

```csharp
private static async Task<(Dictionary<string, McpClient> Map, List<McpClientTool> Tools)> BuildToolDataAsync(
    Dictionary<string, McpClient> clients, CancellationToken ct)
{
    var map = new Dictionary<string, McpClient>(StringComparer.OrdinalIgnoreCase);
    var allTools = new List<McpClientTool>();
    var listTasks = clients.Values.Select(async client =>
    {
        var tools = await client.ListToolsAsync(cancellationToken: ct);
        return (client, tools);
    });
    foreach (var (client, tools) in await Task.WhenAll(listTasks))
        foreach (var tool in tools) { map[tool.Name] = client; allTools.Add(tool); }
    return (map, allTools);
}
```

All four paths consume `allMcpTools` directly without re-listing. `ListToolsAsync` is now called exactly once per MCP client per invocation.

---

## Fix 4: Parallel Prompt Builder DB Calls

**File:** `src/Diva.TenantAdmin/Prompts/TenantAwarePromptBuilder.cs`

`BuildAsync` called three data sources sequentially: business rules (DB), session rules (cache), and prompt overrides (DB). On a cold cache miss, two DB round-trips ran back-to-back.

**Change:** All three calls fire in parallel via `Task.WhenAll`:

```csharp
var staticRulesTask  = _rules.GetPromptInjectionsAsync(tenant.TenantId, agentType, ct);
var overridesTask    = _rules.GetPromptOverridesAsync(tenant.TenantId, agentType, ct);
var sessionRulesTask = tenant.SessionId is not null
    ? _sessionRules.GetSessionRulesAsync(tenant.SessionId, ct)
    : Task.FromResult(new List<SuggestedRule>());

await Task.WhenAll(staticRulesTask, overridesTask, sessionRulesTask);
```

---

## Fix 5: Parallel MCP Connections

**File:** `src/Diva.Infrastructure/LiteLLM/AnthropicAgentRunner.cs` — `ConnectMcpClientsAsync`

MCP server connections (primarily used on cold starts or when cache misses) previously connected to each binding sequentially. Replaced with `Task.WhenAll` so connections to multiple servers happen concurrently.

---

## Net Effect

For a "hi" query on an agent with 3 stdio MCP bindings (after first request warms the cache):

| Step | Before | After |
|------|--------|-------|
| MCP connect | 300–900ms (3× sequential docker spawn) | ~0ms (cache hit) |
| `ListToolsAsync` | 2× per provider path | 1× per invocation, parallel |
| Rule extraction | 500–2000ms (blocking LLM call) | 0ms (background) |
| Prompt builder | 20–60ms (sequential DB) | 10–30ms (parallel) |
| **Total pre-LLM** | **~1000–3000ms** | **~10–30ms** |

---

## Files Modified

| File | Change |
|------|--------|
| `src/Diva.Infrastructure/LiteLLM/McpClientCache.cs` | **New** — singleton MCP client cache |
| `src/Diva.Infrastructure/LiteLLM/AnthropicAgentRunner.cs` | Cache injection; `BuildToolDataAsync`; fire-and-forget rule extraction; parallel connections |
| `src/Diva.TenantAdmin/Prompts/TenantAwarePromptBuilder.cs` | Parallel DB/cache calls via `Task.WhenAll` |
| `src/Diva.Host/Program.cs` | `AddSingleton<McpClientCache>()` |
