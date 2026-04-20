# Phase 17: AI Agent Setup Assistant (Prompt + Rule Pack Suggestion and Refinement)

> **Status:** `[ ]` Not Started  
> **Depends on:** [phase-12-admin-portal.md](phase-12-admin-portal.md), [phase-16 rule packs implementation in INDEX](INDEX.md), [phase-09-llm-client.md](phase-09-llm-client.md), [phase-15-custom-agents.md](phase-15-custom-agents.md)  
> **Projects:** `Diva.Core`, `Diva.TenantAdmin`, `Diva.Infrastructure`, `Diva.Host`, `admin-portal`, `tests/Diva.TenantAdmin.Tests`  
> **Instruction contract:** [.github/instructions/agent-setup-assistant.instructions.md](../.github/instructions/agent-setup-assistant.instructions.md)

---

## Goal

Ship an in-product AI assistant that helps users:

1. Suggest a high-quality system prompt for an agent.
2. Suggest compatible rule packs aligned to hook lifecycle and archetype.
3. Refine existing system prompt and existing rule packs (edit mode, not just create mode).
4. Suggest model switching rules for tool-heavy agents using tenant-configured LLM config IDs.
5. Maintain version history for each agent system prompt and rule pack, including compare and restore.
6. Help users generate and validate regex expressions for rule fields with AI assistance.

The feature must remain future-proof by sourcing rule compatibility and archetypes dynamically.

---

## Scope

### In Scope

- Two backend suggestion endpoints for prompt and rule packs.
- Create and refine modes for existing agent setup.
- Prompt template files in `prompts/agent-setup/`.
- Dynamic compatibility and archetype injection.
- `model_switch` aware rule suggestions with `LlmConfigId` preference.
- Wizard UI in Agent Builder with review-before-apply.
- AI-assisted regex builder for rule fields that use `Pattern`.
- System prompt history and rule pack history timelines per agent.
- Compare and restore actions for historical versions.
- MSW mocks for local UI development.
- Unit tests for matrix alignment, sanitization, and suggestion behavior.

### Out of Scope

- Automated migration of all existing agents.
- Background long-running generation jobs.
- Runtime model switch execution changes (already handled by runner/hooks).
- Cross-tenant history aggregation or global history search.
- Arbitrary regex execution without safety validation.

---

## Architecture Summary

### Three Sources of Truth (must stay dynamic)

1. Rule compatibility matrix from `RulePackCompatibilityMatrix.ByHookPoint`.
2. Archetype catalog from `IArchetypeRegistry.GetAll()`.
3. Prompt templates from `prompts/agent-setup/*.txt` loaded via `PromptTemplateStore`.

No duplicated hardcoded matrix or archetype data in assistant service or UI constants.

### Assistant Flow

1. UI sends `AgentSetupContext` with mode and optional current assets.
2. Assistant enrichers append runtime context (archetype details, available LLM configs, future enrichers).
3. Service builds prompt from template + dynamic placeholders.
4. LLM returns structured JSON only.
5. Service validates and drops invalid combinations based on live matrix.
6. UI displays diffs/replacements and applies only on user confirmation.
7. On apply, the platform writes a new immutable history version for changed prompt/rule packs.

### Regex Assistant Flow

1. User opens regex helper from a rule field that uses `Pattern`.
2. UI sends natural-language intent, sample matching strings, and sample non-matching strings.
3. Backend builds a regex-specific prompt template and asks for structured output.
4. Service validates the returned regex for syntax and safety before returning it.
5. UI shows explanation, preview matches, and lets user accept or edit the expression manually.

### History Model (per agent, tenant-scoped)

- Prompt history is append-only versions with metadata (`Version`, `CreatedAtUtc`, `CreatedBy`, `Source`, `Reason`).
- Rule pack history is append-only versions per pack with the same metadata shape.
- Restoring a version creates a new latest version (no destructive overwrite of prior versions).
- `Source` values: `manual`, `assistant_create`, `assistant_refine`, `restore`.
- `Reason` captures optional user note or assistant action summary.

---

## Data Contracts

### `AgentSetupContext` additions

- `Mode`: `"create" | "refine"`
- `CurrentSystemPrompt?`: existing prompt when refining
- `CurrentRulePacks?`: existing packs when refining
- `AvailableLlmConfigs`: list of tenant-available model configs (`Id`, `Provider`, `Model`, `Label`)

### `SuggestedHookRuleDto` additions

- `LlmConfigId?`: preferred for `model_switch` rules
- `ModelOverride?`: fallback only when no config exists

### Refine Output Shape

Rule pack suggestion response should support operation semantics:

- `add`
- `update`
- `delete`
- `keep`

This can be either explicit operation fields or split lists, but the API contract must make edit intent unambiguous.

### History DTOs (new)

- `AgentPromptHistoryEntryDto`
- `RulePackHistoryEntryDto`
- `RestorePromptVersionRequestDto`
- `RestoreRulePackVersionRequestDto`

Each history entry must include actor and source metadata for auditability in the UI.

### Regex DTOs (new)

- `RegexSuggestionRequestDto`
- `RegexSuggestionDto`

`RegexSuggestionRequestDto` should include:

- `IntentDescription`
- `SampleMatches[]`
- `SampleNonMatches[]`
- `RuleType?`
- `HookPoint?`

`RegexSuggestionDto` should include:

- `Pattern`
- `Explanation`
- `Flags?`
- `Warnings[]`
- `PreviewMatches[]`
- `PreviewNonMatches[]`

---

## Backend Implementation Plan

### Step 1: Core and DTOs

- Create/update assistant DTOs under `Diva.Core`:
  - `AgentSetupContext`
  - `PromptSuggestionDto`
  - `SuggestedRulePackDto`
  - `SuggestedHookRuleDto`
- Include mode and current asset fields for refine workflows.

### Step 2: Assistant Service

- Add `IAgentSetupAssistant` in `Diva.TenantAdmin`.
- Add `AgentSetupAssistant` implementation:
  - `SuggestSystemPromptAsync(...)`
  - `SuggestRulePacksAsync(...)`
- Load templates via `PromptTemplateStore`.
- Inject matrix and archetypes dynamically at runtime.
- Validate LLM output and silently drop incompatible rule combinations.

### Step 3: Enricher Pipeline

- Add `ISetupAssistantContextEnricher` interface.
- Add baseline enrichers:
  - `ArchetypeContextEnricher`
  - `LlmConfigContextEnricher` (from tenant LLM configs)
- Register enrichers in DI in ordered sequence.

### Step 4: Controller Endpoints

In `AgentsController`:

- `POST /api/agents/suggest-prompt`
- `POST /api/agents/suggest-rule-packs`
- `POST /api/admin/rule-packs/suggest-regex`

Validation requirements:

- Enforce input limits and null-byte rejection.
- Apply tenant scoping using effective tenant resolution.
- Add per-tenant rate limiting.
- For regex suggestion, cap sample counts and sample string lengths.

### Step 4b: Regex Suggestion Service

- Add `SuggestRegexAsync(...)` to the assistant service or a focused `IRegexSuggestionService` if separation is cleaner.
- Load regex prompt template from `prompts/agent-setup/regex-generator.txt`.
- Require structured JSON response with a single regex pattern and explanation.
- Validate regex with the same runtime constraints used by `RulePackEngine` (timeout-aware compile path, invalid regex handling).
- Reject or warn on suspicious catastrophic-backtracking patterns before returning to UI.

### Step 5: History Persistence and APIs

- Add prompt history persistence in infrastructure with tenant-scoped entities.
- Add rule pack history persistence in infrastructure with tenant-scoped entities.
- Write history record whenever prompt or rule pack is changed from UI or assistant apply.
- Add read endpoints for history timelines and version detail.
- Add restore endpoints that create new versions from selected historical content.

Suggested API surface:

- `GET /api/agents/{agentId}/prompt-history`
- `POST /api/agents/{agentId}/prompt-history/{version}/restore`
- `GET /api/admin/rule-packs/{packId}/history`
- `POST /api/admin/rule-packs/{packId}/history/{version}/restore`

---

## `model_switch` Integration Plan

The runtime execution path already exists (`TenantRulePackHook` -> `StaticModelSwitcherHook` -> `ModelRouterHook` -> `AnthropicAgentRunner`).
Phase 17 only needs suggestion and wiring correctness.

### Required behavior in suggestions

- Suggest `model_switch` only for `OnBeforeIteration`.
- Prefer `LlmConfigId` from `AvailableLlmConfigs`.
- Use `ModelOverride` only as fallback.
- For tool-heavy agents, include at least one `model_switch` suggestion.

### Cost policy guidance in prompt template

- Tool-calling/replanning iterations: lower-cost config (haiku-tier)
- Consecutive failures >= 2: upgrade config (opus-tier)
- Final response: primary quality config (sonnet-tier)

---

## Prompt Template Plan

Create/maintain:

- `prompts/agent-setup/system-prompt-generator.txt`
- `prompts/agent-setup/rule-pack-generator.txt`
- `prompts/agent-setup/regex-generator.txt`

Required placeholders:

- `{{hook_point_matrix}}`
- `{{archetype_list}}`
- `{{available_llm_configs}}`
- core request placeholders (`agent_name`, `agent_description`, `tool_names`, mode fields)

Template rules:

- Strict JSON response schema.
- Explicit create vs refine instructions.
- `model_switch` section with field-mapping requirements.
- Regex prompt must require:
  - anchored pattern when appropriate
  - concise explanation of capture groups / alternation
  - no catastrophic nested quantifier constructs unless unavoidable
  - behavior aligned to provided positive and negative examples

---

## Frontend Plan

### Wizard UX (`AgentAssistantDrawer.tsx`)

Three-step flow:

1. Context confirmation (intent, mode, archetype, tools, constraints).
2. Prompt suggestion and editable preview.
3. Rule pack suggestion with compatibility warnings and apply actions.

### Regex Builder UX

- Add `RegexAssistantDialog.tsx` or equivalent helper opened from rule rows using `Pattern`.
- Inputs: plain-English goal, example strings that should match, example strings that should not match.
- Output: suggested regex, explanation, preview table, warning badges.
- Add one-click "Use Regex" action to copy the pattern into the active rule editor field.
- Allow iterative refinement without leaving the rule editor.

### Refine UX requirements

- Toggle between create and refine mode.
- Show before/after for prompt text.
- Show rule pack operation preview (add/update/delete/keep).
- Require explicit user confirmation before applying edits.

### History UX requirements

- Add History tab in Agent Builder for system prompt versions.
- Add History tab in Rule Pack editor for pack versions.
- Show version list with actor, source, timestamp, and short reason.
- Provide compare view (selected version vs current).
- Provide restore action with confirmation modal and required reason input.
- After restore, refresh timelines and show the newly created head version.

### API + mock wiring

- Add assistant DTOs and calls in `admin-portal/src/api.ts`.
- Add MSW handlers in `admin-portal/src/mocks/handlers.ts` for both create and refine scenarios.
- Add regex suggestion API call and mock handler for positive/negative sample previews.

---

## Security and Guardrails

- Sanitize untrusted free-text fields before embedding in prompts.
- Strip known prompt-injection markers.
- Cap request lengths (`Description`, `AdditionalContext`, current asset payloads).
- Rate limit suggestion endpoints per tenant.
- Do not allow suggestion layer to execute tool calls.
- Enforce tenant isolation on all history queries and restore actions.
- Do not allow in-place mutation of historical rows; restore must append a new version.
- Validate AI-suggested regex before persisting or testing it in UI.
- Reuse the existing regex timeout / invalid-pattern handling approach to avoid ReDoS-style suggestions.
- Treat regex suggestion output as untrusted until server-side validation passes.

---

## Testing Plan

### `RulePackCompatibilityMatrixTests`

- Validate known valid/invalid combinations.
- Verify markdown table contains all hook points and rule types.
- Regression guard: every matrix combination maps to engine behavior (no orphan rule types).

### `AgentSetupAssistantTests`

- Invalid hook/rule combinations are dropped.
- Malformed JSON from LLM returns empty suggestions without crashing.
- Prompt contains injected matrix and archetype list.
- Enrichers run before LLM call.
- Input sanitization removes prompt-injection patterns.
- Tool-heavy contexts suggest `model_switch`.
- `model_switch` suggestions prefer valid `LlmConfigId` values.
- Refine mode generates edit-intent output (not full replacement only).

### Regex suggestion tests

- Regex suggestion returns structured pattern + explanation.
- Suggested regex matches all provided positive examples and rejects provided negative examples.
- Invalid or unsafe regex from the LLM is rejected or returned with warnings.
- Regex endpoint enforces sample-size and string-length limits.

### History tests

- Prompt updates create monotonic version numbers per agent.
- Rule pack updates create monotonic version numbers per pack.
- Restore operation appends a new version and preserves full prior history.
- History endpoints return only tenant-scoped data.
- Compare payload maps correct old vs current content.

---

## Documentation and Rollout

When implementation starts/ships:

1. Update [docs/INDEX.md](INDEX.md) with Phase 17 row and status transitions.
2. Update [docs/changelog.md](changelog.md) with implementation notes.
3. Update [docs/agents.md](agents.md) key files table with assistant files.
4. Keep [.github/instructions/agent-setup-assistant.instructions.md](../.github/instructions/agent-setup-assistant.instructions.md) synchronized with any contract changes.

---

## Milestones and Acceptance Criteria

### Milestone A: Backend Suggestion API

- Endpoints available and tenant-scoped.
- Dynamic matrix/archetype injection implemented.
- Basic create mode passes unit tests.

### Milestone B: Refine Mode

- Existing prompt and packs accepted as input.
- Output includes explicit edit intent.
- UI shows diff/operation preview before apply.

### Milestone C: `model_switch` Quality

- Suggestions include `model_switch` where appropriate.
- Suggested rules use `LlmConfigId` from tenant config list.
- No hardcoded model-string-first behavior.

### Milestone D: Documentation and Mock Completeness

- MSW scenarios cover create and refine flows.
- Docs and instruction contracts updated and in sync.

### Milestone E: Prompt and Rule Pack History

- Prompt and rule pack timelines available in UI.
- Compare and restore flows are functional and audited.
- Restores create new versions (no destructive edits).

### Milestone F: Regex Builder

- Rule editor exposes AI regex helper for `Pattern` fields.
- Suggestions are validated server-side before use.
- UI shows example-based preview and warnings before applying a regex.

Final acceptance: users can create and safely refine an agent's system prompt and rule packs with assistant guidance, review historical versions, restore prior versions safely, generate regex expressions with preview and safety validation, and generated output remains compatible with current runtime hook and model-switching behavior.
