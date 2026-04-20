# Architecture Decision Log

> Records all significant architectural choices made for Diva AI.
> Read this before proposing changes to avoid re-litigating settled decisions.
> Add new entries as decisions are made during implementation.

---

## Decision Format

```
### ADR-NNN: Title
- **Status:** Accepted | Superseded by ADR-XXX
- **Date:** YYYY-MM-DD
- **Decision:** What was chosen
- **Rationale:** Why
- **Alternatives considered:** What was rejected and why
- **Consequences:** What this locks in
```

---

## ADR-001: Agent Framework — Semantic Kernel over AutoGen

- **Status:** Accepted — reaffirmed 2026-03-23 (see ADR-014)
- **Date:** 2026-03-21
- **Decision:** Use Semantic Kernel (`Microsoft.SemanticKernel`) as the primary agent framework. Use `Microsoft.AutoGen` only for A2A (agent-to-agent) external protocol communication.
- **Rationale:** SK's `ChatCompletionAgent` + `AgentGroupChat` + `FunctionChoiceBehavior.Auto` natively implements the ReAct loop (Think → Act → Observe) without custom scaffolding. Direct Anthropic/OpenAI/Azure support via `AddAnthropicChatCompletion` etc. AutoGen is better suited for multi-process A2A scenarios but adds complexity for the core orchestration problem.
- **Alternatives considered:** AutoGen for everything — rejected because it adds inter-process overhead and complexity that isn't needed for intra-process agent orchestration.
- **Consequences:** SK experimental APIs require `#pragma warning disable SKEXP0110` at file level. Supervisor pipeline is custom (`ISupervisorPipelineStage`) with SK used specifically for the dispatch stage.

---

## ADR-002: TenantContext Model — Rich over Lean

- **Status:** Accepted
- **Date:** 2026-03-21
- **Decision:** Use the Rich TenantContext model containing all tenant, user, site, and session fields.
- **Rationale:** A lean model would require repeated DB lookups in every component to reconstruct tenant info. The rich model is built once in `TenantContextMiddleware` from the JWT and flows through the entire request. Eliminates N+1 context lookups and keeps all tenant-aware code testable with a simple constructor.
- **Alternatives considered:** Lean model (just TenantId + UserId) — rejected because it pushes per-request DB lookups into business logic.
- **Consequences:** `TenantContext` carries more data (~15 fields). It must be constructed only by `TenantContextMiddleware` (from JWT); business logic must never instantiate it with `new TenantContext()` except in tests.

---

## ADR-003: Supervisor Pipeline — Custom Stages over SK GroupChat for Orchestration

- **Status:** Accepted
- **Date:** 2026-03-21
- **Decision:** The Supervisor is implemented as a sequential `ISupervisorPipelineStage` pipeline (Decompose → CapabilityMatch → Dispatch → Monitor → Integrate → Deliver). SK `AgentGroupChat` is used within the `DispatchStage` to run worker agents.
- **Rationale:** The Supervisor needs explicit control over each stage for observability, error recovery, and partial retries. SK's `AgentGroupChat` at the top level would obscure per-stage telemetry and make it hard to insert custom logic between stages (e.g. monitoring, multi-channel delivery).
- **Alternatives considered:** Pure SK `AgentGroupChat` for everything — rejected because it doesn't support the Deliver stage (push to SignalR/email/Slack) or per-stage Prometheus metrics.
- **Consequences:** More code in `Diva.Agents/Supervisor/` but full control. New stages can be added by implementing `ISupervisorPipelineStage` and registering in DI (order matters).

---

## ADR-004: Database — SQLite Default, SQL Server Optional

- **Status:** Accepted
- **Date:** 2026-03-21
- **Decision:** SQLite is the default database (zero setup, EF query filters for tenant isolation). SQL Server is opt-in via `Database:Provider=SqlServer` and supports native RLS via `sp_set_session_context`.
- **Rationale:** Makes Diva runnable from `dotnet run` with no infrastructure. Enterprise deployments can switch to SQL Server without code changes — just config.
- **Alternatives considered:** PostgreSQL default — rejected to avoid requiring Docker for development. PostgreSQL support can be added later as a third provider.
- **Consequences:** EF query filters are the primary tenant isolation mechanism for SQLite. SQL Server deployments add Row-Level Security as a backstop layer. `DatabaseProviderFactory` abstracts the switch.

---

## ADR-005: LLM Routing — Direct Provider Default, LiteLLM Optional

- **Status:** Accepted
- **Date:** 2026-03-21
- **Decision:** Default to direct provider connection (Anthropic/OpenAI/Azure via SK). LiteLLM proxy is opt-in via `LLM:UseLiteLLM=true`.
- **Rationale:** Reduces infrastructure requirements for development and small deployments. LiteLLM adds per-tenant cost tracking, rate limiting, and multi-provider routing for enterprise use — but adds a service dependency.
- **Alternatives considered:** LiteLLM always required — rejected because it forces Docker for development.
- **Consequences:** `ILlmClientFactory.CreateKernel()` branches on `UseLiteLLM` flag. When LiteLLM is enabled, the SK kernel uses the OpenAI-compatible endpoint at `LiteLLM:BaseUrl`. `LiteLLMClient` is used for non-SK direct calls (e.g. rule extraction, correction analysis).

---

## ADR-006: Dynamic Agent Registry — Capability Scoring

- **Status:** Accepted
- **Date:** 2026-03-21
- **Decision:** `DynamicAgentRegistry.FindBestMatchAsync` selects agents by counting capability keyword intersections, then by priority. Static agents (code-registered) have priority 10; dynamic agents from DB have priority 5.
- **Rationale:** Simple, deterministic, testable. No LLM call in the selection path — keeps routing latency low.
- **Alternatives considered:** LLM-based routing (ask LLM which agent to use) — rejected for latency and cost reasons; also non-deterministic in tests.
- **Consequences:** Agent capability arrays must be kept accurate and specific. Generic capabilities like "help" should not be used.

---

## ADR-007: Rule Learning Approval — Three Modes

- **Status:** Accepted
- **Date:** 2026-03-21
- **Decision:** Rule learning supports three approval modes: `AutoApprove` (immediately promotes to `TenantBusinessRules`), `RequireAdmin` (sits in `LearnedRules` with status `pending`), `SessionOnly` (stored in `IDistributedCache` for 24h, never persisted).
- **Rationale:** Different tenants have different trust requirements. Some want rules auto-applied; others require human review. Session-only mode lets users try a rule without committing.
- **Alternatives considered:** Always require admin review — rejected as too slow for high-trust tenants. No session-only mode — rejected as it forces persistence of every suggested rule.
- **Consequences:** `ITenantAwarePromptBuilder` must check both `TenantBusinessRules` (DB) and `SessionRuleManager` (cache) when building prompts.

---

## ADR-008: Prompt Templates — File-Based, Version-Aware

- **Status:** Accepted
- **Date:** 2026-03-21
- **Decision:** Prompt templates are `.txt` files in `prompts/{agent-type}/{section}.txt` with a YAML frontmatter header (`version`, `agent`, `section`). Loaded by `PromptTemplateStore` at startup.
- **Rationale:** Keeps prompts out of C# code, enables version tracking via git, and lets non-developers edit prompts without touching code.
- **Alternatives considered:** Hardcoded strings in C# — rejected because it requires redeployment for prompt changes. DB-stored prompts only — rejected because base templates should be in source control.
- **Consequences:** Prompt changes are git-committed. Tenant overrides are stored in DB (via `TenantPromptOverrideEntity`) and merged at runtime by `TenantAwarePromptBuilder`.

---

## ADR-009: MCP Header Injection — All Tenant Context via Headers

- **Status:** Accepted
- **Date:** 2026-03-21
- **Decision:** Every MCP tool call receives a standard set of headers: `Authorization: Bearer <token>`, `X-Tenant-ID`, `X-Correlation-ID`, plus any `X-Tenant-*` custom headers configured per tenant.
- **Rationale:** MCP tools run as separate processes/services and cannot share in-process state. Headers are the standard mechanism for propagating context across service boundaries in HTTP-based protocols.
- **Alternatives considered:** Embedding tenant context in tool parameters — rejected because it pollutes tool signatures and breaks the MCP protocol contract.
- **Consequences:** `McpHeaderPropagator` builds headers from `TenantContext` on every call. `HeaderPropagationHandler` (DelegatingHandler) ensures headers flow through `HttpClient` automatically.

---

## ADR-010: Integration Tests — Real SQLite, No DB Mocks

- **Status:** Accepted
- **Date:** 2026-03-21
- **Decision:** Integration tests use real SQLite (in-memory or temp file). Mocking `DbContext` or `IDbConnection` is not permitted in integration tests.
- **Rationale:** Mock DB tests passed while prod migrations failed in a prior project incident. Real SQLite catches actual query issues including EF query filter behavior, while being fast and requiring no external infrastructure.
- **Alternatives considered:** Mocked DbContext — rejected due to past incident. PostgreSQL test container — acceptable alternative for SQL Server-specific tests if needed.
- **Consequences:** Test projects reference `Diva.Infrastructure`. Test setup uses `WebApplicationFactory<Program>` or `DivaDbContext` with `UseInMemoryDatabase` / SQLite in-memory. See `docs/testing.md`.

---

## ADR-011: Phase 5 Deferred — Domain MCP Servers Not Needed for Core Ecosystem

- **Status:** Accepted
- **Date:** 2026-03-21
- **Decision:** Phase 5 (MCP Tool Infrastructure with AnalyticsMcpServer, ReservationMcpServer) is deferred indefinitely. It no longer blocks Phase 8.
- **Rationale:** The MCP client side (McpToolBinding, Docker/stdio/http/sse transport, McpClient creation) is fully implemented in `AnthropicAgentRunner`. Domain-specific MCP tool servers are business-specific and not needed to validate the core agentic ecosystem.
- **Alternatives considered:** Implement Phase 5 as planned — rejected because it delays proving the core supervisor pipeline with domain-irrelevant infrastructure.
- **Consequences:** `Diva.Tools` project remains empty. When domain tools are needed, Phase 5 is revisited. `TenantAwareMcpClient` and `McpHeaderPropagator` are also deferred.

---

## ADR-012: Phase 6 Deferred — Tenant Behavior Loosely Coupled to Ecosystem

- **Status:** Accepted
- **Date:** 2026-03-21
- **Decision:** Phase 6 (TenantBusinessRulesService, TenantAwarePromptBuilder) is deferred. Agents in Phase 8 accept `ITenantAwarePromptBuilder` as an optional nullable dependency and fall back to `AgentDefinitionEntity.SystemPrompt` when not injected.
- **Rationale:** The core agentic pipeline (Supervisor, multi-agent routing, verification) should be proven independently of tenant customisation. Tight coupling to Phase 6 would delay the ecosystem unnecessarily. The loose coupling design means Phase 6 can be dropped in later with no changes to Phase 8 agent code.
- **Alternatives considered:** Implement Phase 6 before Phase 8 as planned — rejected because tenant rules are not needed to validate the orchestration layer.
- **Consequences:** All agents default to their `AgentDefinitionEntity.SystemPrompt` (or a generic fallback). Once Phase 6 is implemented, `ITenantAwarePromptBuilder` is registered in DI and automatically picked up by all agents. The `ITenantAwarePromptBuilder` interface must be defined in `Diva.Core` so `Diva.Agents` can reference it without creating a circular dependency.

---

## ADR-014: Framework & Package Review — March 2026 Reaffirmation

- **Status:** Accepted
- **Date:** 2026-03-23
- **Decision:** Retain the existing framework stack without migration. No changes to agent framework, MCP client, or LLM provider strategy.
- **Rationale:** A full NuGet and GitHub changelog review was performed across all relevant .NET agent frameworks as of March 2026:
  - **Semantic Kernel 1.74.0** (released 2026-03-20) remains the latest stable release. SK ships approximately monthly; no 2.0 or breaking redesign is in progress.
  - **Microsoft.Agents 1.4.83** (`Microsoft.Agents.Builder`, `Microsoft.Agents.Core`, etc.) is the successor to Bot Framework / Azure Bot Service. Its `Microsoft.Agents.Extensions.Teams` and `Microsoft.Agents.CopilotStudio.Client` packages confirm it targets Teams bots and Copilot Studio channels — not enterprise analytics agent platforms. Not a fit for this project.
  - **SK → Microsoft.Agents migration samples** were added in SK 1.67 but are intended for customers migrating Teams bots, not for general-purpose agent platforms.
  - **No official Anthropic SK connector** exists. `Microsoft.SemanticKernel.Connectors.Anthropic` is not published on NuGet. The custom `IAnthropicProvider` / `AnthropicProvider` wrappers (ADR-015) remain the correct approach.
  - **ModelContextProtocol 1.1.0** is the latest stable MCP SDK. SK does not ship its own MCP client package; its own samples use `ModelContextProtocol` directly — exactly the approach used here. `McpClientCache` + `BuildToolClientMapAsync` is the right pattern.
  - **AutoGen .NET 0.2.3** has low adoption (107K downloads vs SK's 10M+) and remains unsuitable for replacing SK here.
  - **Microsoft.Extensions.AI 10.4.1**, **OpenAI SDK 2.9.1**, **Anthropic.SDK 5.10.0** are all on their latest stable versions.
- **Alternatives considered:** Migrating to Microsoft.Agents SDK — rejected (wrong scenario). Adopting SK's `OpenAIResponseAgent` — deferred (only relevant if switching to OpenAI Responses API). Replacing `McpClientCache` with SK-native MCP support — not possible (SK has no MCP client package).
- **Consequences:** All package version pins are confirmed current as of 2026-03-23. The next review should be triggered by a SK 2.0 release or an official Anthropic SK connector appearing on NuGet.

---

## ADR-015: LLM Provider Abstraction — IAnthropicProvider / IOpenAiProvider Interfaces

- **Status:** Accepted
- **Date:** 2026-03-23
- **Decision:** Extract thin `IAnthropicProvider` and `IOpenAiProvider` interfaces wrapping `AnthropicClient` and `OpenAIClient` respectively. Both are registered as singletons in DI and injected into `AnthropicAgentRunner` and `ResponseVerifier`.
- **Rationale:** `AnthropicClient` and `OpenAIClient` were originally constructed inline with `new` inside `AnthropicAgentRunner` and `ResponseVerifier`, making those classes untestable without real API keys. Extracting interfaces unlocks full unit test coverage via NSubstitute mocks with zero API calls.
- **Alternatives considered:** Mocking at the HTTP level (e.g. `DelegatingHandler`) — rejected as more fragile and harder to read than interface mocks. Replacing Anthropic.SDK with a generic `IChatClient` — not possible; `Anthropic.SDK` does not implement `IChatClient` and has no SK connector.
- **Consequences:** `IAnthropicProvider` exposes `GetClaudeMessageAsync` only (streaming is via `Anthropic.SDK` directly in `InvokeStreamAsync`). `IOpenAiProvider.CreateChatClient(model)` returns `IChatClient` (ME.AI abstraction). Both are in `Diva.Infrastructure/LiteLLM/`. When an official Anthropic SK connector or `IChatClient` implementation ships, `AnthropicProvider` is the only class that needs updating.

---

## ADR-013: Phase 8 Kernel — Direct IChatClient Construction, No LlmClientFactory

- **Status:** Accepted
- **Date:** 2026-03-21
- **Decision:** Phase 8 SK agents build their Kernel directly from `LlmOptions` using the same IChatClient/OpenAIClient pattern as `AnthropicAgentRunner`, without waiting for `LlmClientFactory` (the planned Phase 9 SK wrapper).
- **Rationale:** `LlmClientFactory` (SK Kernel builder) is a convenience wrapper not yet built. The underlying provider connection logic is already proven in `AnthropicAgentRunner`. Duplicating the small provider-selection block in `DynamicReActAgent` avoids a Phase 9 hard dependency.
- **Alternatives considered:** Block Phase 8 on Phase 9 LlmClientFactory — rejected because it delays the ecosystem for an abstraction that adds no new capability. Build a minimal LlmClientFactory now — rejected as premature; the abstraction should be built when there are multiple consumers.
- **Consequences:** When `LlmClientFactory` is eventually built (Phase 9), both `AnthropicAgentRunner` and `DynamicReActAgent` will be refactored to use it. Until then, provider selection is duplicated in two places — acceptable given the small size of the switch block.

---

## ADR-017: Microsoft Agent Framework 1.0 — Post-GA Evaluation (April 2026)

- **Status:** Accepted
- **Date:** 2026-04-09
- **Decision:** Do not migrate to Microsoft Agent Framework 1.0 (`Microsoft.Agents.AI.*`) at this time. Retain Semantic Kernel 1.x as the primary agent orchestration framework. Nominate `Microsoft.Agents.AI.A2A` for evaluation during Phase 14 implementation.
- **Rationale:** MAF 1.0 GA'd on April 3, 2026 — 6 days before this review. Three critical findings:
  1. **There are two distinct Microsoft products** with overlapping `Microsoft.Agents.*` namespaces that are frequently confused:
     - **`Microsoft.Agents.AI.*`** (Microsoft Agent Framework) — `github.com/microsoft/agent-framework` — general-purpose AI agent orchestration; SK + AutoGen merged; GA April 3, 2026. This is the product this ADR evaluates.
     - **`Microsoft.Agents.*`** (Microsoft 365 Agents SDK) — `github.com/microsoft/Agents` — Teams/M365/Copilot Studio channel deployment; Bot Framework successor; v1.4.83. This is what ADR-014 evaluated and rejected, and what CLAUDE.md's "Do NOT use" rule refers to.
  2. **SK 1.x is now in maintenance mode** — bug fixes and security patches only; no new features go into SK. Microsoft's own guidance: "For existing SK projects: stay on SK. For new projects: prefer Agent Framework." SK 1.x is supported until at least April 2027 (one year post-MAF-GA).
  3. **MAF 1.0 has no `AgentGroupChat` equivalent** and no built-in multi-tenant primitives. Diva's Supervisor `DispatchStage` depends on `AgentGroupChat`; the entire tenant isolation stack (EF query filters, `TenantContext`, per-tenant prompt overrides) was built on SK and would require a full rebuild on MAF.
  - MAF does offer compelling long-term advantages: native `Microsoft.Agents.AI.Anthropic` connector (replaces `IAnthropicProvider`), first-class MCP support, and `Microsoft.Agents.AI.Agent2Agent` (A2A — currently preview, not stable GA).
  - Migration blockers today: (a) 6 days post-GA; (b) no AgentGroupChat equivalent; (c) full multi-tenant rebuild required; (d) Agent2Agent is preview.
- **Alternatives considered:** Full migration to MAF 1.0 — rejected; no AgentGroupChat equivalent + multi-tenant rebuild required. Selective `Microsoft.Agents.AI.Anthropic` adoption — deferred; `IAnthropicProvider` wrapper is correct and tested. Selective `Microsoft.Agents.AI.Agent2Agent` for Phase 14 — nominated, but package is preview; evaluate when Phase 14 begins.
- **Consequences:** CLAUDE.md package table updated to distinguish both products. ADR-014 addendum: its "Microsoft.Agents" evaluation correctly targeted the M365 Agents SDK, not MAF 1.0 (which did not exist in March 2026). SK 1.x maintenance window of April 2027 is the outer bound for migration planning. Future migration path: `IAnthropicProvider` and `ILlmProviderStrategy` map cleanly to `IChatClient` adapters in MAF — migration can be incremental. Next review trigger: MAF 1.x reaches ~6 months post-GA (October 2026), OR Phase 14 (A2A) implementation begins.

---

## ADR-016: Tool Calling Optimisations — 2026 Best Practices

- **Status:** Accepted
- **Date:** 2026-03-23
- **Decision:** Apply four tool calling best practices to `AnthropicAgentRunner`:
  1. **`IsError: true` on failed Anthropic tool results** — set `ToolResultContent.IsError` when tool output starts with `"Error:"`, contains `"timed out"`, or is empty. Applies to streaming and non-streaming Anthropic paths only; `FunctionResultContent` (ME.AI/OpenAI) has no equivalent field.
  2. **Parallel tool execution in non-streaming Anthropic path** — `RunAnthropicAsync` replaced its sequential `foreach` loop with `Task.WhenAll`, matching the existing pattern in `InvokeStreamAsync`. Each tool also gets its own 30s `CancellationTokenSource`.
  3. **Tool result size guard** — `internal static TruncateResult(output, maxChars)` truncates oversized MCP results at `MaxToolResultChars` (default 8 000) with a re-query hint. Applied at all four tool output sites across all paths.
  4. **LLM retry with exponential back-off** — `CallWithRetryAsync<T>` wraps all four LLM call sites. Transient errors (HTTP 429/502/503, `TimeoutException`, non-cancelled `TaskCanceledException`) are retried up to `Retry.MaxRetries` (default 3) with `BaseDelayMs × 2^attempt` delay (2 s / 4 s / 8 s).
- **Rationale:** Anthropic's API uses `is_error` to distinguish failed tool results from valid data — without it the model may misinterpret `"Error: ..."` strings as real output. Parallel execution reduces latency for multi-tool turns. The size guard prevents large MCP payloads from consuming the context window. Retry logic avoids cascading failures from transient rate-limit responses common in production workloads.
- **Alternatives considered:** Polly for retry — deferred; `CallWithRetryAsync` is sufficient and adds no dependency. Per-path size limits — rejected for inconsistency. Circuit breaker — deferred until retry is stable in production.
- **Consequences:** `AgentOptions` gains `MaxToolResultChars` (default 8 000) and `Retry: { MaxRetries: 3, BaseDelayMs: 1000 }`. `TruncateResult` is `internal` and exposed to `Diva.Agents.Tests` via `[assembly: InternalsVisibleTo]`. Retry does not apply to tool execution (MCP calls already have individual 30 s timeouts). `IsError` applies only to Anthropic paths — OpenAI-compatible paths follow ME.AI conventions.
