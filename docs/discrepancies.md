# Documentation vs Implementation Discrepancies

Last reviewed: 2026-04-12
Scope: Alignment review between [IMPLEMENTATION_PLAN.md](../IMPLEMENTATION_PLAN.md), [docs/INDEX.md](INDEX.md), phase docs, and current runtime code.

---

## Summary

| # | Discrepancy | Status after review |
|---|-------------|---------------------|
| 1 | Auth not enforced on invoke paths | ✅ **Resolved** — SSO + JWT + TenantContextMiddleware wired (Phase 3) |
| 2 | Phase 9 factory artifacts missing | 🟢 Not a gap — design changed, fully documented |
| 3 | Supervisor drops verification fields | ✅ Real gap — needs code fix |
| 4 | Phase doc statuses don't match INDEX | 🔧 Doc-only — fixed |
| 5 | Phase 10 controller naming wrong | 🔧 Doc-only — noted |
| 6 | Version metadata stale in INDEX | 🔧 Doc-only — fixed in INDEX.md |
| 7 | Delegation ID type mismatch | ✅ **Resolved** (2026-04-12) — frontend/backend/tests aligned on string IDs |
| 8 | A2A config default disabled | ✅ **Resolved** (2026-04-12) — `Enabled: true` in appsettings.json |
| 9 | Partial migrations in SQLite | ⚠️ Recurring risk — MigFix tool available |

---

## Discrepancy 1 — Auth not enforced on invoke paths

**Status: ✅ Resolved (2026-03-25)**

Auth is now fully active:
- `TenantContextMiddleware` is wired in `Program.cs`
- JWT Bearer authentication registered with `AddAuthentication()` + `AddJwtBearer()`
- Local dev uses `Diva.Sso` for JWT issuance
- `TenantContext.System(1)` pattern replaced with `HttpContext.GetTenantContext()`
- `EffectiveTenantId` pattern enforced in all controllers (JWT tenant takes precedence; query param only for master admin)

See [changelog.md — Auth/SSO Login Flow](changelog.md) and [phase-03-oauth-tenant.md](phase-03-oauth-tenant.md).

---

## Discrepancy 2 — Phase 9 factory artifacts missing

**Status: Not a gap — design intentionally changed**

The original plan called for `ILlmClientFactory` / `LlmClientFactory` / `LiteLLMClient`. These were **never built** because the design shifted to `AnthropicAgentRunner` (see [phase-09-llm-client.md — As Built section](phase-09-llm-client.md#as-built--anthropicagentrunner-supersedes-planned-llmclientfactory-for-direct-provider)).

The shift was driven by a concrete runtime incompatibility (ME.AI version conflict between `Anthropic.SDK 5.10.0` and `Microsoft.Extensions.AI 10.4.1`).

**LiteLLM is fully supported** via the existing OpenAI-compatible path — point `DirectProvider` at the LiteLLM proxy URL.

**Phase 09 doc and INDEX.md both document the as-built reality.** No action needed.

---

## Discrepancy 3 — Supervisor drops verification and tool evidence

**Status: Real gap**

Verified in `SupervisorAgent.cs` lines 76–87: the final `AgentResponse` returned to the API includes `Success`, `Content`, `SessionId`, `ToolsUsed`, and `ExecutionTime` — but **not `Verification` or `ToolEvidence`**.

`VerifyStage` computes and attaches `VerificationResult` to each worker's `AgentResponse`, but `SupervisorAgent` does not aggregate these into its own response.

**Impact:** Clients calling `POST /api/supervisor/invoke` receive no verification metadata. The `verification` SSE chunk is only emitted by the direct agent streaming paths.

**To fix:** In `SupervisorAgent.cs`, aggregate verification from worker results:
```csharp
Verification  = state.WorkerResults.Select(r => r.Verification).FirstOrDefault(v => v is not null),
ToolEvidence  = string.Join("\n\n", state.WorkerResults.Select(r => r.ToolEvidence).Where(e => !string.IsNullOrEmpty(e)))
```

---

## Discrepancy 4 — Phase doc statuses out of sync with INDEX

**Status: Doc-only — fixed**

Phase docs had stale status headers that didn't match INDEX.md. Corrected:

| Phase doc | Was | Now | Reason |
|-----------|-----|-----|--------|
| `phase-03-oauth-tenant.md` | `[ ]` Not Started | `[x]` Done | Middleware classes exist and are complete — wiring deferred (see Discrepancy 1) |
| `phase-06-tenant-admin.md` | `[-]` Deferred | `[x]` Done | `TenantBusinessRulesService`, `TenantAwarePromptBuilder` fully implemented |
| `phase-10-api-host.md` | `[~]` In Progress | `[x]` Done | All deliverables complete — SignalR, AdminController, full observability all shipped |
| `phase-11-rule-learning.md` | `[ ]` Not Started | `[x]` Done | `IRuleLearningService`, `RuleLearningService`, `LlmRuleExtractor`, `SessionRuleManager`, all wired |

---

## Discrepancy 5 — Phase 10 controller naming mismatch

**Status: Doc-only — not fixed (phase-10-api-host.md is a historical reference)**

`phase-10-api-host.md` references `AgentController.cs` and `HealthController.cs`. Actual implementation:
- `AgentController.cs` → **`AgentsController.cs`** (full CRUD + invoke + stream)
- `HealthController.cs` → **does not exist** — health checks are mapped directly via `app.MapHealthChecks(...)` in `Program.cs`

The phase doc also notes this itself at the bottom (line 434). Since the phase docs are historical planning documents (not living API references), no change is strictly required. The INDEX.md and `agents.md` reflect the correct names.

---

## Discrepancy 6 — ASP.NET Core version stale in INDEX

**Status: Doc-only — fixed**

`docs/INDEX.md` Solution Structure section referred to "ASP.NET Core 8 host". `Diva.Host.csproj` targets `net10.0`. Fixed directly in INDEX.md.

---

## Discrepancy 7 — Delegation ID type mismatch (frontend/backend/tests)

**Status: ✅ Resolved (2026-04-12)**

**Root cause:** Three-layer type inconsistency:
1. `DelegateAgentSelector.tsx` stored selected IDs as `number[]` (`[1, 2]`) — but `AgentDefinitionEntity.Id` is a string (GUID)
2. `AgentToolProvider.cs` deserialized `DelegateAgentIdsJson` via `JsonSerializer.Deserialize<List<string>>` — silent `JsonException` when JSON contained numbers
3. `AgentDelegationTool.AgentId` was `int`, losing data for GUID-format agent IDs

**Fix applied across 7 files + 2 test files:**
- `DelegateAgentSelector.tsx`: `selectedIds: number[]` → `string[]`; backward-compatible parsing via `parsed.map((x: unknown) => String(x))`
- `AgentToolProvider.cs`: `JsonNode.Parse().AsArray()` handles both `["id"]` and `[1]` JSON
- `AgentDelegationTool.cs`: `AgentId: int` → `string`
- `AgentToolExecutor.cs`: Removed `.ToString()` on `tool.AgentId`
- `DelegationAgentResolver.cs`: Returns `cap.AgentType` as Name
- Tests updated to use string IDs

See [changelog.md — 2026-04-12 Bug Fixes](changelog.md).

---

## Discrepancy 8 — A2A config default disabled

**Status: ✅ Resolved (2026-04-12)**

`appsettings.json` shipped with `"A2A": { "Enabled": false }`. The A2A Settings UI correctly showed "Disabled" badge. Changed to `"Enabled": true`.

---

## Discrepancy 9 — Partial EF migrations in SQLite

**Status: ⚠️ Recurring risk — mitigation in place**

**Root cause:** Some EF migrations contain operations that fail silently in SQLite (e.g., `DropIndex` on non-existent index). The transaction rolls back, but EF records the migration as applied in `__EFMigrationsHistory`. Subsequent schema changes from that migration are never applied.

**Known instances:**
- `20260326195152_AddLlmConfigCatalog`: Partially applied — missing `Name` column on `TenantLlmConfigs` and `PlatformLlmConfigs`
- `20260325_AddLlmConfigId`: Partially applied — missing `LlmConfigId` column on `AgentDefinitions`

**Mitigation:** `tools/MigFix/Program.cs` — console tool that inspects SQLite schema and adds missing columns via `ALTER TABLE`. Has a generic `FixMissingColumn(conn, table, column, type, defaultValue)` helper.

**Prevention:** When writing migrations that target SQLite, avoid `DropIndex`/`DropColumn` in the same migration as `AddColumn`. Split into separate migrations if needed. Always test migration on a fresh database AND an existing database.
