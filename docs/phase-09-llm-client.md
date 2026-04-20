# Phase 9: LLM Client Factory & LiteLLM Integration

> **Status:** `[x]` Done
> **Depends on:** [phase-02-core-models.md](phase-02-core-models.md)
> **Blocks:** [phase-08-agents.md](phase-08-agents.md) (agents need LLM kernel)
> **Project:** `Diva.Infrastructure`

---

## Goal

Implement a factory that creates LLM clients either as direct provider connections (Anthropic/OpenAI/Azure) or via the optional LiteLLM proxy for centralized cost tracking, rate limiting, and multi-provider routing.

---

## Configuration Toggle

```json
{
  "LLM": {
    "UseLiteLLM": false,
    "DirectProvider": {
      "Provider": "Anthropic",
      "ApiKey": "${ANTHROPIC_API_KEY}",
      "Model": "claude-sonnet-4-20250514"
    },
    "LiteLLM": {
      "BaseUrl": "http://litellm:4000",
      "MasterKey": "${LITELLM_MASTER_KEY}",
      "DefaultModel": "claude-sonnet"
    }
  }
}
```

---

## Files to Create

```
src/Diva.Infrastructure/LiteLLM/
├── LlmOptions.cs
├── ILlmClientFactory.cs
├── LlmClientFactory.cs
└── LiteLLMClient.cs
```

---

## LlmOptions.cs

```csharp
namespace Diva.Infrastructure.LiteLLM;

public sealed class LlmOptions
{
    public const string SectionName = "LLM";

    public bool UseLiteLLM { get; set; } = false;
    public DirectProviderOptions DirectProvider { get; set; } = new();
    public LiteLLMOptions LiteLLM { get; set; } = new();
}

public sealed class DirectProviderOptions
{
    public string Provider { get; set; } = "Anthropic";   // "Anthropic" | "OpenAI" | "Azure"
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "claude-sonnet-4-20250514";
    public string? Endpoint { get; set; }   // For Azure OpenAI
    public string? DeploymentName { get; set; }
}

public sealed class LiteLLMOptions
{
    public string BaseUrl { get; set; } = "http://litellm:4000";
    public string MasterKey { get; set; } = "";
    public string DefaultModel { get; set; } = "claude-sonnet";
}
```

---

## ILlmClientFactory.cs

```csharp
namespace Diva.Infrastructure.LiteLLM;

public interface ILlmClientFactory
{
    /// <summary>
    /// Creates a configured SK Kernel for the given tenant.
    /// When LiteLLM is enabled, routes through LiteLLM proxy with tenant team key.
    /// When disabled, connects directly to configured provider.
    /// </summary>
    Kernel CreateKernel(TenantContext tenant);
}
```

---

## LlmClientFactory.cs

```csharp
namespace Diva.Infrastructure.LiteLLM;

public class LlmClientFactory : ILlmClientFactory
{
    private readonly LlmOptions _options;
    private readonly IServiceProvider _services;

    public Kernel CreateKernel(TenantContext tenant)
    {
        var builder = Kernel.CreateBuilder();

        if (_options.UseLiteLLM)
        {
            // Route through LiteLLM — OpenAI-compatible endpoint
            // Use tenant team API key if available, fall back to master key
            var apiKey = tenant.TeamApiKey ?? _options.LiteLLM.MasterKey;

            builder.AddOpenAIChatCompletion(
                modelId:  _options.LiteLLM.DefaultModel,
                apiKey:   apiKey,
                endpoint: new Uri(_options.LiteLLM.BaseUrl));
        }
        else
        {
            // Direct provider connection
            switch (_options.DirectProvider.Provider)
            {
                case "Anthropic":
                    builder.AddAnthropicChatCompletion(
                        modelId: _options.DirectProvider.Model,
                        apiKey:  _options.DirectProvider.ApiKey);
                    break;

                case "OpenAI":
                    builder.AddOpenAIChatCompletion(
                        modelId: _options.DirectProvider.Model,
                        apiKey:  _options.DirectProvider.ApiKey);
                    break;

                case "Azure":
                    builder.AddAzureOpenAIChatCompletion(
                        deploymentName: _options.DirectProvider.DeploymentName!,
                        endpoint:       _options.DirectProvider.Endpoint!,
                        apiKey:         _options.DirectProvider.ApiKey);
                    break;

                default:
                    throw new NotSupportedException(
                        $"Provider '{_options.DirectProvider.Provider}' not supported.");
            }
        }

        // Auto function calling (the "ACT" in ReAct)
        builder.Services.ConfigureHttpClientDefaults(c =>
            c.AddStandardResilienceHandler());

        return builder.Build();
    }
}
```

---

## LiteLLMClient.cs (for direct text generation, bypassing SK if needed)

```csharp
namespace Diva.Infrastructure.LiteLLM;

public class LiteLLMClient
{
    private readonly HttpClient _http;
    private readonly LlmOptions _options;

    public async Task<string> GenerateAsync(
        string prompt,
        TenantContext tenant,
        CancellationToken ct)
    {
        var apiKey = tenant.TeamApiKey ?? _options.LiteLLM.MasterKey;

        var request = new
        {
            model    = _options.LiteLLM.DefaultModel,
            messages = new[] { new { role = "user", content = prompt } },
            metadata = new
            {
                tenant_id  = tenant.TenantId,
                site_id    = tenant.CurrentSiteId,
                session_id = tenant.SessionId
            }
        };

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_options.LiteLLM.BaseUrl}/chat/completions");

        httpRequest.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
        httpRequest.Content = JsonContent.Create(request);

        var response = await _http.SendAsync(httpRequest, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return json
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";
    }
}
```

---

## LiteLLM Config (litellm_config.yaml)

```yaml
model_list:
  - model_name: claude-sonnet
    litellm_params:
      model: anthropic/claude-sonnet-4-20250514
      api_key: os.environ/ANTHROPIC_API_KEY
    model_info:
      max_tokens: 200000

  - model_name: gpt-4o
    litellm_params:
      model: openai/gpt-4o
      api_key: os.environ/OPENAI_API_KEY

litellm_settings:
  drop_params: true
  set_verbose: false

general_settings:
  master_key: sk-litellm-master-key
  database_url: postgresql://...
  enable_team_based_access: true
  store_spend_logs: true
  enable_rate_limiting: true
```

---

## LiteLLM Team Setup (per tenant)

```json
{
  "teams": [
    {
      "team_id": "team_acme",
      "team_alias": "Acme Corporation",
      "metadata": { "tenant_id": 1 },
      "max_budget": 500.00,
      "budget_duration": "monthly",
      "rpm_limit": 60,
      "tpm_limit": 100000
    }
  ]
}
```

`tenant.TeamApiKey` is the team-specific API key generated by LiteLLM and stored in the `TenantEntity.LiteLLMTeamId` field.

---

## When to Use LiteLLM

| Need | LiteLLM | Direct |
|------|---------|--------|
| Multi-provider routing | ✅ | ❌ |
| Per-tenant budget limits | ✅ | Manual |
| Cost tracking / audit | ✅ | Manual |
| Prompt caching | ✅ | ❌ |
| Single provider | — | ✅ simpler |
| Minimal latency | — | ✅ |
| Early development | — | ✅ |

---

## Service Registration

```csharp
builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection(LlmOptions.SectionName));
builder.Services.AddSingleton<ILlmClientFactory, LlmClientFactory>();
builder.Services.AddHttpClient<LiteLLMClient>();

// Register SK Kernel as scoped (per-request, per-tenant)
builder.Services.AddScoped<Kernel>(sp =>
{
    var factory = sp.GetRequiredService<ILlmClientFactory>();
    var httpContext = sp.GetService<IHttpContextAccessor>()?.HttpContext;
    var tenant = httpContext?.Items["TenantContext"] as TenantContext
                 ?? new TenantContext();  // Anonymous kernel for non-request scope
    return factory.CreateKernel(tenant);
});
```

---

## Verification

- [x] `UseLiteLLM: false` + `Provider: OpenAI` → routes to local LLM at `http://localhost:4141/`
- [x] `UseLiteLLM: false` + `Provider: Anthropic` → native Anthropic SDK, no ME.AI conflict
- [ ] `UseLiteLLM: true` → SK kernel routes through LiteLLM at configured BaseUrl
- [ ] LiteLLM: tenant team API key used when `tenant.TeamApiKey` is set
- [ ] LiteLLM: `metadata.tenant_id` included in every request (for cost tracking)
- [ ] Azure OpenAI: deployment name + endpoint correctly configured

---

## As Built — AnthropicAgentRunner (supersedes planned LlmClientFactory for direct provider)

The planned `LlmClientFactory` approach (building an SK `Kernel`) was **not used** for the direct provider path. Instead, `AnthropicAgentRunner` was built as the primary agent execution engine. This was driven by a critical discovery:

> **ME.AI version conflict:** `Anthropic.SDK 5.10.0` was compiled against `Microsoft.Extensions.AI.Abstractions 10.3.0` but the project uses `10.4.1`. Using `AnthropicClient` via `IChatClient` caused `MissingMethodException: HostedMcpServerTool.get_AuthorizationToken()` at runtime.

**Solution — split provider paths:**

| Provider | Client | Why |
|---|---|---|
| `"Anthropic"` | Native `Anthropic.SDK` (`AnthropicClient`) + manual ReAct loop | Avoids ME.AI version conflict entirely |
| Everything else | `IChatClient` (`OpenAIClient.AsIChatClient()`) via `OpenAiProviderStrategy` (manual ReAct loop, no `UseFunctionInvocation`) | Works cleanly with LM Studio, Ollama, LiteLLM, Azure OpenAI |

**Key implementation discoveries (Anthropic.SDK 5.10.0):**

| Planned type | Actual type |
|---|---|
| `List<Anthropic.SDK.Messaging.Tool>` for `MessageParameters.Tools` | `IList<Anthropic.SDK.Common.Tool>` — use `new Tool(new Function(name, desc, JsonNode))` |
| `StopReason.ToolUse` enum value | Plain string `"tool_use"` |
| `Message.Content = "string"` | `List<ContentBase>` — must use `[new TextContent { Text = "..." }]` |
| `ToolResultContent.Content = "string"` | `List<ContentBase>` — same pattern |
| `TextBlock` | `Anthropic.SDK.Messaging.TextContent` |
| `TextContent` (ambiguous with ME.AI) | Qualify as `Anthropic.SDK.Messaging.TextContent` |
| MCP: `TextContent` | `ModelContextProtocol.Protocol.TextContentBlock` |

**`AnthropicAgentRunner` features implemented:**
- Single unified `ExecuteReActLoopAsync` shared by all providers via `ILlmProviderStrategy` (strategy pattern)
- `AnthropicProviderStrategy`: Anthropic SDK message format
- `OpenAiProviderStrategy`: raw `IChatClient`, no `UseFunctionInvocation()` in any path
- MCP tool support: stdio (Docker/npx), HTTP, SSE transports via `McpToolBinding`
- Session history: prepends `ConversationTurn[]` to message list on every call
- Returns `SessionId` in every `AgentResponse`
- `tools_available` SSE chunk emitted before iteration 1 — `ToolCount: 0` means MCP connection failed or no bindings configured; check API logs for `"Failed to connect to MCP server"` warnings
- **Parallel tool execution**: when the LLM returns multiple tool calls in one response, all are executed concurrently via `Task.WhenAll`. `tool_call` events emitted first, then all results emitted in order after all complete.
- **Per-tool 30s timeout**: each `CallToolAsync` is wrapped with `CancellationTokenSource.CreateLinkedTokenSource` + `CancelAfter(30s)`. Timeout returns a user-readable message ("timed out after 30s. Try a narrower query.").
- **`IsError` on failed Anthropic tool results**: `ToolResultContent.IsError = true` when output starts with `"Error:"`, contains `"timed out"`, or is empty. Anthropic API uses this flag to distinguish error strings from valid data. OpenAI-compatible path omits this flag (`FunctionResultContent` has no equivalent).
- **Tool result size guard**: `TruncateResult(output, MaxToolResultChars)` truncates oversized MCP results at `Agent:MaxToolResultChars` (default 8 000 chars) with a re-query hint. Applied at the tool output site in `ExecuteReActLoopAsync`. Prevents large payloads from consuming the context window.
- **LLM retry with exponential back-off**: `CallWithRetryAsync<T>` wraps all four LLM call sites. Transient errors (HTTP 429/502/503, `TimeoutException`, non-cancelled `TaskCanceledException`) retry up to `Agent:Retry:MaxRetries` times (default 3) with `BaseDelayMs × 2^attempt` delay (2 s / 4 s / 8 s). Non-transient errors (e.g. 400 Bad Request) are not retried.
- **Tool planning prompt injection**: when `Agent:InjectToolStrategy = true` (default) and ≥1 MCP server is connected, a `## Tool use` block is appended to the system prompt instructing the LLM to: (1) identify ALL data needed before calling any tool, (2) call all independent tools in one batch turn, (3) never repeat a call with identical parameters already in evidence. An idempotency guard (`systemPrompt.Contains("batch")`) prevents double-injection when a tenant prompt override already contains batching language.
- **Verification correction scope**: The correction loop fires only when `WasBlocked=true` (Strict mode) or `Mode=="ToolGrounded"` (LLM skipped a tool it should have called). `LlmVerifier` and `Auto` mode results are informational — they populate the `verification` SSE chunk but do **not** trigger re-iteration. This prevents spurious 3rd iterations on tool-grounded responses (e.g. "get today's Toronto weather" → 2 iterations, not 3). Set `Mode=Strict` to enforce blocking + correction for hallucination control.
- **Auto mode verification**: Uses `ToolGrounded` heuristic (zero extra LLM cost) when tools were called and produced evidence. Set `Verification.Mode=LlmVerifier` or `Strict` explicitly to enable cross-checking of response claims against tool evidence.
- **Intra-batch tool call deduplication**: `DeduplicateCalls<T>(calls, keySelector)` groups tool calls by `(toolName, inputJson)` before `Task.WhenAll` executes them. Each unique pair executes exactly once; the single result is fanned out to all original callers (preserving one `ToolResultContent` per original `ToolUseId`, as required by the Anthropic API). Applied in the single `Task.WhenAll` site in `ExecuteReActLoopAsync` (shared by both strategies). Duplicate merges are logged at `Debug` level.
- **Plan detection**: if the LLM's first iteration text contains ≥2 numbered lines (`^\d+\.\s+`), it is emitted as a `plan` SSE chunk instead of `thinking`. Detected via `ParsePlanSteps()` regex helper.
- **Adaptive re-planning**: tracks `executionLog` + `consecutiveFailures` across all tool calls. After 2+ consecutive failures, an execution summary (✓/✗ per tool) is injected into the message history and a no-tools LLM call produces a revised plan, emitted as `plan_revised`. Both Anthropic and OpenAI paths share identical logic.
- **JSON case-insensitive binding**: `ConnectMcpClientsAsync` deserializes `ToolBindings` with `PropertyNameCaseInsensitive = true` — fixes silent skipping of bindings when frontend serializes camelCase JSON.

**Streaming path — tool calling diagnostic note:**

The streaming OpenAI path (`InvokeStreamAsync`) uses a **manual ReAct loop** (no `UseFunctionInvocation`). Tools are passed via `chatOptions.Tools`, but whether the LLM actually calls them depends entirely on the model's function-calling capability. Local models (LM Studio, Ollama) vary widely:

| Model | Tool calling |
|-------|-------------|
| GPT-4.1 (OpenAI API) | Reliable |
| LM Studio — qwen2.5-7b-instruct, llama-3.1-8b | Generally works |
| LM Studio — general-purpose / GGUF without tool template | Often ignored |

If `ToolCount > 0` in the `tools_available` chunk but no `tool_call` events follow, the **model is receiving tools but choosing not to call them** — switch to a model with reliable function-calling support.

**OpenAI path — tool_calls message ordering constraint:**

OpenAI requires all tool result messages to immediately follow the assistant `tool_calls` message, with no interleaving of `User` or `Assistant` messages. The re-plan logic was moved **outside** the tool `foreach` loop to respect this: tool results are collected and added to `oaiMessages` as one `ChatMessage(ChatRole.Tool, ...)` batch, then the re-plan User message is appended after.

**`AgentOptions` additions (2026-03-23):**
```json
"Agent": {
  "MaxToolResultChars": 8000,
  "InjectToolStrategy": true,
  "Retry": {
    "MaxRetries": 3,
    "BaseDelayMs": 1000
  }
}
```
`TruncateResult` and `DeduplicateCalls<T>` are `internal` and exposed to `Diva.Agents.Tests` via `[assembly: InternalsVisibleTo("Diva.Agents.Tests")]`.

**Key files actually created:**
```
src/Diva.Infrastructure/LiteLLM/
├── LlmOptions.cs              ✓ (LlmOptions, DirectProviderOptions, LiteLLMOptions)
└── AnthropicAgentRunner.cs    ✓ implemented (replaces planned LlmClientFactory for direct use)
    └── McpToolBinding         ✓ (inner class: Name, Command, Args, Env, Endpoint, Transport)
src/Diva.Core/Configuration/
└── AgentOptions.cs            ✓ MaxToolResultChars, InjectToolStrategy, LlmRetryOptions, Retry property added
tests/Diva.Agents.Tests/
└── ToolOptimizationTests.cs   ✓ TruncateResult (4 tests), retry (3 tests), DeduplicateCalls (3 tests)
```

**`appsettings.Development.json` — local LLM config (added):**
```json
"LLM": {
  "UseLiteLLM": false,
  "DirectProvider": {
    "Provider": "OpenAI",
    "ApiKey": "no-key",
    "Model": "gpt-4.1",
    "Endpoint": "http://localhost:4141/"
  }
}
```

**LiteLLM support — how it works:**

LiteLLM exposes an OpenAI-compatible API, so it is already supported by the OpenAI-compatible path in `AnthropicAgentRunner`. To route through LiteLLM, configure `DirectProvider` to point at the proxy:

```json
"DirectProvider": {
  "Provider": "OpenAI",
  "ApiKey": "<litellm-master-key>",
  "Endpoint": "http://litellm:4000/",
  "Model": "claude-sonnet"   // model alias defined in litellm_config.yaml
}
```

The `docker-compose.enterprise.yml` sets these env vars automatically when using the enterprise stack. The `litellm_config.yaml` (repo root) defines model aliases mapping friendly names to provider-specific model IDs.

`LlmClientFactory` and `LiteLLMClient` (the originally planned classes) are **not required** — `AnthropicAgentRunner` handles all execution paths directly.

---

## Context Window Management (added 2026-03-23)

Two unbounded accumulations can overflow the context window as sessions grow:

- **Point B (cross-run)**: `AgentSessionService` loads all historical messages from the DB with no size limit. After 20+ turns with 8 K-char tool results, history alone can approach 50–80 K tokens.
- **Point A (in-run)**: Each ReAct iteration appends 2 messages (assistant + tool results). 10 iterations × 2 tools × 8 K chars ≈ 40 K tokens added within one run.

### IContextWindowManager

New injectable service (`src/Diva.Infrastructure/Context/`) with two compaction points:

| Point | Method | When | Strategy |
|---|---|---|---|
| **B — cross-run** | `CompactHistoryAsync` | After history load in `RunAsync` + `InvokeStreamAsync` | Sliding window; older turns → LLM or rule-based summary appended to system prompt |
| **A — in-run** | `strategy.CompactHistory()` (delegates to `MaybeCompactAnthropicMessages` / `MaybeCompactChatMessages`) | Before every LLM call (3 sites in `ExecuteReActLoopAsync`: main loop, re-plan, continuation boundary) | Rule-based compaction; last K messages kept verbatim |

### Point B — Cross-Run Compaction

When `history.Count > MaxHistoryTurns`, older turns are offloaded and summarized:

- **LLM summarization** (async): used when `SummarizerModel` is configured (explicit config) OR the session model is available. Falls back to rule-based on any LLM error.
- **Rule-based fallback**: bullets of the first 6 user query snippets from offloaded turns.

The summary is appended to the system prompt as `## Earlier session context\n{summary}`.

### Point A — In-Run Compaction

Before every LLM call, `ComputeCompactionPlan` estimates `(systemText + all messages) / 4` tokens. When estimated tokens exceed `BudgetTokens × CompactionThreshold`, messages between `messages[0]` (first) and the last `KeepLastRawMessages` are replaced with a single `[Prior context in this run — compacted]` summary message.

3 injection sites in `AnthropicAgentRunner.ExecuteReActLoopAsync`:
- Main loop top (before each LLM call)
- Re-plan block (before no-tools LLM call after ≥2 consecutive failures)
- Continuation boundary (via `strategy.PrepareNewWindow()`)

### Configuration

```json
"Agent": {
  "ContextWindow": {
    "BudgetTokens": 120000,
    "CompactionThreshold": 0.65,
    "KeepLastRawMessages": 6,
    "MaxHistoryTurns": 20,
    "SummarizerModel": null
  }
}
```

`SummarizerModel: null` → use session model for LLM summarization; if not available → rule-based.

### Per-Agent Override

Set `ContextWindowJson` on an agent entity (JSON-serialized `ContextWindowOverrideOptions`) to override global defaults for that agent:

```json
{"MaxHistoryTurns": 5, "BudgetTokens": 60000}
```

Null fields fall through to global defaults. Stored as `string? ContextWindowJson` in `AgentDefinitionEntity` (migration: `AddAgentContextWindowConfig`).

### Token Estimation

`EstimateTokens(text) = text.Length / 4` — standard 4-chars-per-token approximation, no tokenizer dependency.

### Key Files

```
src/Diva.Infrastructure/Context/
├── IContextWindowManager.cs    ✓ interface (CompactHistoryAsync, MaybeCompact*)
└── ContextWindowManager.cs     ✓ implementation (Singleton; LLM + rule-based summary; ComputeCompactionPlan generic core)
src/Diva.Core/Configuration/
└── AgentOptions.cs             ✓ ContextWindowOptions + ContextWindowOverrideOptions added
src/Diva.Infrastructure/Data/Entities/
└── AgentDefinitionEntity.cs    ✓ ContextWindowJson nullable column added
tests/Diva.Agents.Tests/
├── ContextWindowTests.cs       ✓ 11 pure unit tests + 2 integration tests
└── Helpers/ContextWindowTestHelpers.cs  ✓ NoOpCtx() shared mock
```

### Future Extension Points

| Extension | How to add | Changes needed |
|---|---|---|
| New LLM provider (Gemini) | Add `MaybeCompactGeminiMessages` to `ContextWindowManager` | One call site in runner |
| Tokenizer-accurate counts | Replace `EstimateTokens` body | None |
| Admin UI for per-agent overrides | Expose `ContextWindowJson` via AgentsController CRUD | Controller + React only |
| Redis caching of summaries | Add `SummaryContent` to `AgentSessionEntity` | Schema + session service |
| OTel `agent_tokens_per_run` metric | Add counter in `ContextWindowManager` | None elsewhere |

---

## Tool Error Retry (added 2026-03-23)

### Problem

When a tool call returns an error (`"Error: invalid syntax"` or `{"status":"error","error":"..."}`), the LLM often produces a text-only response in the next iteration ("I'll fix this...") before issuing the corrected tool call. The `else` branch (no tool calls) immediately runs verification and breaks the loop, returning the acknowledgment text as the final answer.

### Fix — `hadToolErrors` flag + `BuildToolErrorRetryMessages`

Three manual ReAct loops (streaming Anthropic, streaming OAI, non-streaming Anthropic) now track a `bool hadToolErrors` flag. When a text-only response follows a tool error iteration, a retry prompt is injected rather than breaking.

**Error detection — `IsToolOutputError(string output)` (internal static):**

```csharp
internal static bool IsToolOutputError(string output) =>
    string.IsNullOrWhiteSpace(output) ||
    output.StartsWith("Error:") ||
    output.Contains("timed out") ||
    (output.TrimStart().StartsWith("{") &&
     (output.Contains("\"status\":\"error\"") || output.Contains("\"status\": \"error\"")));
```

Covers: `"Error: ..."` prefix, timeout messages, empty results, and JSON error objects (`{"status":"error","error":"..."}` — both with and without space after colon).

Also checks `callResult.IsError == true` (MCP SDK's native error flag) alongside `IsToolOutputError`.

**Message injection helpers (internal static, directly testable):**

```csharp
internal const string ToolErrorRetryPrompt =
    "The previous tool call failed. Please retry with corrected parameters — make the corrected tool call now.";

// Anthropic path
internal static List<Message> BuildToolErrorRetryMessages(
    List<Message> messages, string acknowledgmentText) =>
    [..messages,
     new Message { Role = RoleType.Assistant,
         Content = [new Anthropic.SDK.Messaging.TextContent { Text = acknowledgmentText }] },
     new Message { Role = RoleType.User,
         Content = [new Anthropic.SDK.Messaging.TextContent { Text = ToolErrorRetryPrompt }] }];

// OAI path
internal static List<ChatMessage> BuildToolErrorRetryChatMessages(
    List<ChatMessage> messages, string acknowledgmentText) =>
    [..messages,
     new ChatMessage(ChatRole.Assistant, acknowledgmentText),
     new ChatMessage(ChatRole.User, ToolErrorRetryPrompt)];
```

**Loop flow:**

```
Iter N:   Tool call → error (IsError=true in history, hadToolErrors=true)
Iter N+1: LLM produces text only ("I'll fix this") →
          hadToolErrors=true → BuildToolErrorRetryMessages → continue
Iter N+2: LLM issues corrected tool call → success
Iter N+3: end_turn → break ✓
```

The `maxIterations` cap remains the absolute bound. `hadToolErrors` is reset in the re-planning block and at the start of each continuation window.

**Limitation:** `RunOpenAiCompatibleAsync` (non-streaming) uses `UseFunctionInvocation()` opaquely — no mid-loop injection possible. Deferred.

**Tests added to `ToolOptimizationTests.cs`:**

```
IsToolOutputError_ExceptionText_ReturnsTrue
IsToolOutputError_JsonStatusError_ReturnsTrue
IsToolOutputError_JsonStatusErrorWithSpace_ReturnsTrue
IsToolOutputError_SuccessJson_ReturnsFalse
IsToolOutputError_NormalText_ReturnsFalse
BuildToolErrorRetryMessages_AppendsTwoMessagesToHistory
BuildToolErrorRetryMessages_SetsCorrectRolesAndRetryPrompt
BuildToolErrorRetryChatMessages_AppendsTwoMessagesToHistory
BuildToolErrorRetryChatMessages_SetsCorrectRolesAndRetryPrompt
```

---

## Continuation Windows (added 2026-03-23)

### Problem

When a complex task requires more tool calls than `MaxIterations` allows (default 10), the loop exits with an incomplete or partial response. There was no mechanism to resume work beyond the iteration cap.

### Design — Outer Continuation Loop

All three manual ReAct loops are wrapped with an outer `for (int window = 0; window <= maxContinuations; window++)` loop. The inner `for (int i = 0; ...)` loop sets a `completedNaturally = true; break;` flag when it exits normally (final text response accepted). The outer loop breaks immediately when `completedNaturally` is true; otherwise it starts the next window.

```
Window 0: iter 1–10 → exhausted (completedNaturally=false)
          → compact history → inject continuation context → Window 1
Window 1: iter 11–20 → natural break (completedNaturally=true) → done ✓

Window 0: iter 1–10 → exhausted
Window 1: iter 11–20 → exhausted
Window 2: iter 21–30 → exhausted → outer loop ends (window==maxContinuations)
          → returns best-effort finalResponse
```

### Window Boundary Handling

At each `window > 0`:

1. **Compact history** — `MaybeCompactAnthropicMessages` / `MaybeCompactChatMessages` called proactively (uses LLM summarization if `SummarizerModel` configured)
2. **Inject continuation context** — `BuildContinuationContext(window, maxIterations, toolEvidence)` appended as a user message summarising all evidence gathered so far
3. **Emit `continuation_start` SSE chunk** — frontend shows "Continuing (window N)…" status
4. **Reset per-window state** — `iterationBase += maxIterations`, `consecutiveFailures = 0`, `hadToolErrors = false`, `executionLog.Clear()`, `planEmitted = false`, `verificationRetries = MaxVerificationRetries`

### Globally Unique Iteration Numbers

The `Iteration` field in SSE chunks (`iteration_start`, `thinking`, `tool_call`, `tool_result`) uses `iterationBase + i + 1` rather than `i + 1`. This ensures numbers never repeat across windows — required because the frontend matches chunks to iteration slots by number (`itersRef.find(i => i.number === chunk.iteration)`). Resetting to 1 would corrupt old slots, making continuation iterations appear as empty labels.

### State Across Windows

| State | Preserved | Reset |
|---|---|---|
| `messages` / `oaiMessages` | ✅ history + continuation prompt | — |
| `toolsUsed`, `toolEvidence` | ✅ accumulates | — |
| `finalResponse`, `lastVerification` | ✅ | — |
| `i` (iteration counter) | — | ✅ → 0 |
| `iterationBase` | ✅ advances | — |
| `consecutiveFailures`, `hadToolErrors` | — | ✅ → 0 / false |
| `executionLog`, `planEmitted` | — | ✅ cleared / false |
| `verificationRetries` | — | ✅ reset to max |

### `BuildContinuationContext` (internal static)

```csharp
internal static string BuildContinuationContext(
    int windowNumber, int maxIterations, IReadOnlyList<string> toolEvidence)
{
    var sb = new StringBuilder();
    sb.AppendLine($"[Continuation window {windowNumber + 1}]");
    sb.AppendLine($"The previous window used all {maxIterations} iterations without completing the task.");
    if (toolEvidence.Count > 0)
    {
        sb.AppendLine("Evidence gathered so far:");
        foreach (var e in toolEvidence) sb.AppendLine(e);
    }
    sb.AppendLine("Please continue executing the remaining steps needed to complete the task.");
    return sb.ToString();
}
```

### Configuration

```json
"Agent": {
  "MaxIterations": 10,
  "MaxContinuations": 2
}
```

`MaxContinuations = 2` → up to 3 total windows (1 initial + 2 continuations). Set to `0` to disable continuations entirely.

### Frontend Changes

- `AgentStreamChunk` interface: `continuationWindow?: number` added
- `continuation_start` case in `handleChunk` switch: shows "Continuing (window N)…" status

### New AgentOptions Fields

```json
"Agent": {
  "MaxIterations": 10,
  "MaxContinuations": 2,
  "ContextWindow": { ... }
}
```

### Tests Added to `ToolOptimizationTests.cs`

```
BuildContinuationContext_NoEvidence_ContainsWindowAndIterationCount
BuildContinuationContext_WithEvidence_ContainsEvidenceText
```

---

## Per-Iteration Model Switching (added 2026-03-28)

The ReAct loop supports switching model (and optionally provider) between iterations. See [`agents.md` → Per-Iteration Model Switching](agents.md#per-iteration-model-switching) for the full reference.

### `ILlmProviderStrategy` additions

```csharp
// Switch model for the next CallLlmAsync (same provider — preserves message history)
void SetModel(string model, int? maxTokens = null, string? apiKeyOverride = null, string? endpointOverride = null);

// Export in-flight message history to provider-agnostic format
List<UnifiedHistoryEntry> ExportHistory();

// Import message history from provider-agnostic format (used on cross-provider swap)
void ImportHistory(List<UnifiedHistoryEntry> history, string systemPrompt, List<McpClientTool> tools);
```

`UnifiedHistoryEntry` — provider-agnostic: `TextHistoryPart`, `ToolCallHistoryPart`, `ToolResultHistoryPart`.

### Hook pipeline (priority order)

| Hook | Order | Source |
|---|---|---|
| `TenantRulePackHook` | 2 | Rule Pack `model_switch` rule |
| `StaticModelSwitcherHook` | 3 | `AgentDefinitionEntity.ModelSwitchingJson` |
| `ModelRouterHook` | 4 | Agent Variables `model_router_mode` |

### `AgentHookContext` override signals

```csharp
int?    LlmConfigIdOverride   // full cross-provider switch (wins over ModelOverride)
string? ModelOverride          // same-provider model-only switch
int?    MaxTokensOverride
string? ApiKeyOverride
```

First hook to set either override wins. Runner clears them after applying.

### Runner loop additions

- State signals `__is_final_iteration` / `__last_had_tool_calls` set each iteration for hooks to read
- Model override block after `OnBeforeIteration`: same-provider → `SetModel`; cross-provider → `ExportHistory` + new strategy + `ImportHistory`
- Three failure scenarios handled (resolver null, history transfer failure, API call failure with `FallbackToOriginalOnError`)
- Replan model block before `CallReplanAsync` reads `__replan_config_id` / `__replan_model` from State
- `model_switch` SSE event emitted per switch: `FromModel`, `ToModel`, `FromProvider`, `ToProvider`, `Reason`

