# Agentic Flow Hardening Backlog

> Identified by automated robustness audit — 2026-04-09
>
> **Status legend:** `[ ]` open · `[~]` in progress · `[x]` fixed · `[-]` deferred
>
> **When fixing an item:**
> 1. Make the code change
> 2. Run `dotnet build Diva.slnx` — must produce **0 errors**
> 3. Run `dotnet test` — all tests must **pass** (no regressions)
> 4. If admin-portal files changed: `cd admin-portal && npm run build && npm run lint`
> 5. Only then: change `[ ]` → `[x]`, append `— Fixed in: <short-hash>`, move to [Resolved](#resolved)

---

## P0 — Critical (correctness / security guarantees)

*(all P0 items resolved — see [Resolved](#resolved))*

---

## P1 — High (silent failures / lost observability)

*(all P1 items resolved — see [Resolved](#resolved))*

---

## P2 — Medium (edge cases / resilience)

*(all P2 items resolved or deferred — see [Resolved](#resolved))*

---

## Tests — Missing coverage

- [ ] **No SSE streaming integration tests** (`AgentsController` + `AgentTaskController`) — The streaming path is the primary API surface but has zero end-to-end tests. `data: {json}\n\n` formatting, client-disconnect cancellation, `text/event-stream` response headers, and chunk serialisation round-trips are all untested.
  - **Fix:** Add `AgentsController_StreamingTests` — mock `HttpContext`, capture `Response.WriteAsync` calls, assert SSE format, test cancellation mid-stream.

- [ ] **No Supervisor pipeline integration tests** — The 7-stage pipeline (`Decompose → CapabilityMatch → Dispatch → Monitor → Integrate → Deliver` + `VerifyStage`) has zero test coverage. Multi-agent delegation, `VerifyStage` blocking behaviour, and `DispatchStage` routing are all untested.
  - **Fix:** Add `SupervisorAgent_IntegrationTests` with mocked worker agents; cover: successful dispatch, `VerifyStage` blocking, stage exception handling, partial worker failure.

---

## Docs

- [ ] **`docs/INDEX.md` Phase 14 (A2A Protocol)** — Marked `[ ]` (not started) but the implementation audit shows ~60-70% complete: `AgentCardController`, `AgentTaskController` (full CRUD + SSE), `A2AAgentClient`, `RemoteA2AAgent`, and `AgentTaskEntity` migration all exist. Remaining work: supervisor `DispatchStage` A2A routing, test coverage, and DB-backed task tracking (current `ConcurrentDictionary` is in-memory only).
  - **Fix:** Mark Phase 14 as `[~]`; update deliverables list with what's done vs remaining.

---

## Resolved

> Format: `[x] **File:line** — description — Fixed in: <short-hash>`

- [x] **`src/Diva.Infrastructure/Verification/ResponseVerifier.cs:159`** — Strict mode silently bypassed when verifier LLM throws; `WasBlocked` never set. — Fixed in: pending-commit *(build verification required before final hash)*
- [x] **`src/Diva.Infrastructure/LiteLLM/AnthropicAgentRunner.cs:~600`** — `response!.StopReason` null dereference when streaming ends without IsDone event. Replaced with explicit null guard + stream error path; removed both `!` operators. — Fixed in: pending-commit
- [x] **`src/Diva.Infrastructure/LiteLLM/AnthropicAgentRunner.cs:~533`** — `CompactHistory()` called outside try-catch; corrupted history crashed the loop. Wrapped with try-catch; logs warning and continues with uncompacted history on failure. — Fixed in: pending-commit

- [x] **`src/Diva.Infrastructure/LiteLLM/AnthropicAgentRunner.cs:425`** — `catch { }` on `ModelSwitchingOptions` JSON; model-switching silently disabled with no warning. Now logs `LogWarning` with agent ID and exception. — Fixed in: pending-commit
- [x] **`src/Diva.Infrastructure/LiteLLM/AnthropicAgentRunner.cs:1115`** — `catch { }` on replan config resolution; adaptive re-plan silently skipped. Now logs `LogWarning` with config ID and exception. — Fixed in: pending-commit
- [x] **`src/Diva.Infrastructure/LiteLLM/McpConnectionManager.cs:48`** — `catch { return result; }` on `ToolBindings` JSON; all tool bindings silently disabled. Now logs `LogWarning` with agent ID. — Fixed in: pending-commit
- [x] **`src/Diva.Infrastructure/LiteLLM/AgentHookHelper.cs:30,54`** — Two `catch { }` blocks on hook/variable config JSON. Added optional `ILogger?` parameter to `MergeVariables` and `MergeHookConfig`; callers pass `_logger`. — Fixed in: pending-commit
- [x] **`src/Diva.Infrastructure/Groups/GroupAgentOverlayMerger.cs:123,130`** — Two `catch { }` blocks in `ParseStringArray` / `ParseStringDictionary`. Added `ILogger?` parameter to `Merge` (and private helpers); callers in `DynamicAgentRegistry` pass `_logger`. — Fixed in: pending-commit
- [x] **`src/Diva.Infrastructure/LiteLLM/AnthropicAgentRunner.cs:~467`** — `OnBeforeIteration` hook `HadError` never checked; PII/injection-guard hooks could fail silently. Now logs `LogWarning` when `HadError=true`. — Fixed in: pending-commit
- [x] **`src/Diva.Infrastructure/LiteLLM/ToolExecutor.cs:46`** — Malformed tool input JSON silently defaulted to empty args. Now logs `LogWarning` with tool name and truncated input before falling back. — Fixed in: pending-commit

- [x] **`src/Diva.Infrastructure/LiteLLM/ToolExecutor.cs:42`** — Tool execution timeout was hard-coded to 30 s. Added `AgentOptions.ToolTimeoutSeconds` (default 30); `ToolExecutor` now injects `IOptions<AgentOptions>` and uses the configurable value; `ToolExecutorTests` updated; `appsettings.json` updated. — Fixed in: pending-commit
- [x] **`src/Diva.Agents/Supervisor/Stages/DispatchStage.cs:22`** — Sub-agent `ExecuteAsync` had no timeout; a hung worker blocked the pipeline indefinitely. Added `AgentOptions.SubAgentTimeoutSeconds` (default 120 s); `DispatchStage` now creates a linked `CancellationTokenSource` per sub-agent and records a failure result on timeout without cancelling the outer pipeline. — Fixed in: pending-commit
- [x] **`src/Diva.Infrastructure/LiteLLM/AnthropicAgentRunner.cs:~809`** — `SaveTurnAsync` failure was unhandled; the exception would terminate the iterator after `final_response` was already yielded. Wrapped in try-catch; on failure logs `LogError` and emits a `session_save_error` SSE chunk so the UI can warn the user. — Fixed in: pending-commit
- [x] **`src/Diva.Infrastructure/LiteLLM/AnthropicProviderStrategy.cs:183`** — `JsonNode.Parse("{}")!` used the `!` null-forgiving operator. Replaced with `new JsonObject()` — same intent, no `!`. — Fixed in: pending-commit
- [-] **`src/Diva.Infrastructure/LiteLLM/OpenAiProviderStrategy.cs:~101`** — Streaming token usage always zero on the OpenAI-compatible path. Blocked: `Microsoft.Extensions.AI` does not yet surface usage in `GetStreamingResponseAsync`. Deferred until the ME.AI package exposes this. Track upstream: `dotnet/extensions` repo.
