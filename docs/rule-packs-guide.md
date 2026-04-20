# Rule Packs Guide

This guide explains how to configure Rule Packs across all supported hook points, including rule type compatibility, parameter definitions, and practical examples.

## 1. What A Rule Pack Is

A Rule Pack is a named, versioned bundle of ordered rules that execute at specific agent lifecycle hook points.

Core behaviors:
- Tenant-scoped by default (`TenantId`)
- Optional group sharing (`GroupId`)
- Supports starter packs (`TenantId = 0`) and cloning
- Ordered pack execution by `Priority` (lower runs first)
- Ordered rule execution by `OrderInPack` (lower runs first)
- Optional stop-within-pack via `StopOnMatch`

## 2. Lifecycle Hook Points

Supported hook points:
- `OnInit`
- `OnBeforeIteration`
- `OnToolFilter`
- `OnAfterToolCall`
- `OnBeforeResponse`
- `OnAfterResponse`
- `OnError`

Execution timing summary:
- `OnInit`: once before the ReAct loop starts
- `OnBeforeIteration`: at the start of each iteration
- `OnToolFilter`: after model proposes tool calls, before tool execution
- `OnAfterToolCall`: after each tool result is returned
- `OnBeforeResponse`: once before final response verification/output
- `OnAfterResponse`: once after response is emitted (side-effect stage)
- `OnError`: when LLM/tool errors happen, returns continue/retry/abort intent

## 3. Hook Point To Rule Type Compatibility

Rule type compatibility is enforced by backend validation.

| Hook Point | Allowed Rule Types |
|---|---|
| `OnInit` | `inject_prompt`, `tool_require`, `format_response`, `tool_transform` |
| `OnBeforeIteration` | `inject_prompt`, `tool_require`, `format_response`, `tool_transform`, `format_enforce`, `model_switch` |
| `OnToolFilter` | `block_pattern`, `tool_transform` |
| `OnAfterToolCall` | `regex_redact`, `append_text`, `block_pattern`, `require_keyword`, `format_enforce` |
| `OnBeforeResponse` | `regex_redact`, `append_text`, `block_pattern`, `require_keyword`, `format_enforce`, `format_response` |
| `OnAfterResponse` | `append_text`, `require_keyword`, `format_response`, `format_enforce` |
| `OnError` | `block_pattern`, `tool_require` |

Notes:
- Invalid combinations are rejected on create/update.
- UI only shows valid rule types for the selected hook point.

## 4. Rule Type Semantics

### `inject_prompt`
- Appends `Instruction` text to the working prompt.
- Typical use: inject policy or style guidance.

### `tool_require`
- If `Pattern` matches user query, injects instruction to call `ToolName`.
- Typical use: force retrieval tool for certain query classes.

### `format_response`
- Appends formatting instruction text.
- Typical use: enforce markdown table or structured sections.

### `format_enforce`
- Uses `Pattern` as regex validator for current text.
- If text does not match, prepends `Instruction` (if provided).

### `regex_redact`
- Uses `Pattern` regex to replace text with `Replacement` (or `[REDACTED]`).

### `append_text`
- Appends `Instruction` text to current output.

### `block_pattern`
- If `Pattern` matches, blocks current stage text.
- Uses `Instruction` as custom blocked message if provided.

### `require_keyword`
- If `Pattern` keyword is missing, appends reminder text.
- Uses `Instruction` if present, else appends a note with `Pattern`.

### `tool_transform`
- Context-sensitive transform behavior:
  - Prompt stages: injects transform instruction text
  - Tool-filter stage: transforms tool call JSON input via regex replace

### `model_switch`
- `HookPoint: OnBeforeIteration` only.
- Switches the LLM model (and optionally provider) for the next LLM call.
- Field mapping:
  - `Instruction` — target model ID string (same-provider switch)
  - `ToolName` — target LlmConfigId integer string (cross-provider switch; takes precedence over `Instruction`)
  - `Replacement` — optional max_tokens integer string
  - `Pattern` — optional regex; matched against the text selected by `MatchTarget`; blank = always trigger
  - `MatchTarget` — `"query"` (default) or `"response"` (see below)
- **`MatchTarget: "query"`** — Pattern matched against the original user query.
- **`MatchTarget: "response"`** — Pattern matched against the LLM's text output from the *previous* iteration (`AgentHookContext.LastIterationResponse`). Empty on iteration 1 — rules with a non-blank pattern are silently skipped. Use this to switch model when the agent announces it is about to perform a specific action (e.g. `I will send the email`).
- Does not modify response text. The runner reads `ModelSwitchRequest` from `RuleEvalResult` and applies the switch before the next LLM call.
- First `model_switch` rule to fire wins (subsequent rules with no `StopOnMatch` produce a `ConflictWarning`).

## 5. Parameter Reference

## 5.1 Pack Parameters

| Field | Type | Required | Default | Description |
|---|---|---:|---|---|
| `Name` | string | Yes | - | Pack name |
| `Description` | string? | No | null | Human-readable pack description |
| `GroupId` | int? | No | null | Group shared scope |
| `Priority` | int | No | 100 | Pack execution order (lower first) |
| `IsEnabled` | bool | Update | true | Enables/disables pack |
| `IsMandatory` | bool | No | false | Mandatory packs cannot be deleted |
| `AppliesToJson` | string? | No | null | JSON array of archetypes, null = all |
| `ActivationCondition` | string? | No | null | Conditional activation (`regex:...`, `archetype:...`, plain text match) |
| `ParentPackId` | int? | No | null | Parent inheritance reference |
| `Version` | string | Update | `1.0` | Semantic/version label |
| `MaxEvaluationMs` | int | No | 500 | Max total evaluation per pack per stage |

## 5.2 Rule Parameters

| Field | Type | Required | Default | Description |
|---|---|---:|---|---|
| `HookPoint` | string | Yes | - | Lifecycle stage (`OnInit`, etc.) |
| `RuleType` | string | Yes | - | Rule behavior type |
| `Pattern` | string? | No | null | Regex/keyword match value |
| `Instruction` | string? | No | null | Prompt text, override message, or action hint |
| `Replacement` | string? | No | null | Replace value for regex transforms/redaction |
| `ToolName` | string? | No | null | Tool scoping field |
| `MatchTarget` | string | No | `"query"` | `"query"` or `"response"` — for `model_switch` at `OnBeforeIteration`: which text Pattern is matched against |
| `OrderInPack` | int | Yes | 1 | Rule order within pack |
| `IsEnabled` | bool | Update | true | Enables/disables a specific rule |
| `StopOnMatch` | bool | No | false | Stop remaining rules in same pack when matched |
| `MaxEvaluationMs` | int | No | 100 | Per-rule evaluation budget |

## 5.3 Error Hook Action Mapping (`OnError`)

`OnError` uses Rule Pack evaluation to select an `ErrorRecoveryAction`:
- `Continue`
- `Retry`
- `Abort`

Action selection behavior:
- If `Instruction` contains `abort`, action = `Abort`
- If `Instruction` contains `retry`, action = `Retry`
- If `Instruction` contains `continue`, action = `Continue`
- Else fallback by rule type:
  - `block_pattern` -> `Abort`
  - `tool_require` -> `Retry`
  - otherwise -> `Continue`

## 5.4 Hook Execution Stream Fields

On `hook_executed` events, the stream may include:
- `hookName`
- `hookPoint`
- `rulePackTriggeredCount`
- `rulePackTriggeredRules` (e.g. `regex_redact:modified`)
- `rulePackFilteredCount`
- `rulePackErrorAction`
- `rulePackBlocked`

## 6. API Usage

Base route:
- `/api/admin/rule-packs`

Common endpoints:
- `GET /api/admin/rule-packs?tenantId=1`
- `GET /api/admin/rule-packs/{id}?tenantId=1`
- `POST /api/admin/rule-packs?tenantId=1`
- `PUT /api/admin/rule-packs/{id}?tenantId=1`
- `DELETE /api/admin/rule-packs/{id}?tenantId=1`
- `POST /api/admin/rule-packs/{sourceId}/clone?tenantId=1`
- `POST /api/admin/rule-packs/{packId}/rules?tenantId=1`
- `PUT /api/admin/rule-packs/{packId}/rules/{ruleId}?tenantId=1`
- `DELETE /api/admin/rule-packs/{packId}/rules/{ruleId}?tenantId=1`
- `POST /api/admin/rule-packs/{packId}/reorder?tenantId=1`
- `GET /api/admin/rule-packs/{id}/conflicts?tenantId=1`
- `POST /api/admin/rule-packs/{id}/test?tenantId=1`
- `GET /api/admin/rule-packs/{id}/export?tenantId=1`
- `POST /api/admin/rule-packs/import?tenantId=1`

## 7. Examples By Hook Point

### 7.1 OnInit: inject policy text

```json
{
  "hookPoint": "OnInit",
  "ruleType": "inject_prompt",
  "instruction": "Always cite source tools and confidence.",
  "orderInPack": 1,
  "stopOnMatch": false,
  "maxEvaluationMs": 100
}
```

### 7.2 OnBeforeIteration: reinforce iteration strategy

```json
{
  "hookPoint": "OnBeforeIteration",
  "ruleType": "format_enforce",
  "pattern": "(?i)step|plan|verify",
  "instruction": "In this iteration, state plan, execute, then verify.",
  "orderInPack": 2,
  "maxEvaluationMs": 100
}
```

### 7.2b OnBeforeIteration: model_switch based on user query

Switch to a stronger model when the query mentions contracts (matches original user query):

```json
{
  "hookPoint": "OnBeforeIteration",
  "ruleType": "model_switch",
  "pattern": "(?i)contract|legal|compliance",
  "instruction": "claude-opus-4-6",
  "matchTarget": "query",
  "orderInPack": 1,
  "stopOnMatch": true,
  "maxEvaluationMs": 100
}
```

### 7.2c OnBeforeIteration: model_switch based on previous iteration response

Switch to a stronger model when the agent announces it is about to send an email (matches the LLM's previous iteration text):

```json
{
  "hookPoint": "OnBeforeIteration",
  "ruleType": "model_switch",
  "pattern": "(?i)I will send|about to send|sending.*email",
  "instruction": "claude-opus-4-6",
  "matchTarget": "response",
  "orderInPack": 1,
  "stopOnMatch": true,
  "maxEvaluationMs": 100
}
```

Note: this rule is silently skipped on iteration 1 (no prior response). It fires from iteration 2 onward when the previous LLM output matches the pattern.

### 7.3 OnToolFilter: block risky tool call

```json
{
  "hookPoint": "OnToolFilter",
  "ruleType": "block_pattern",
  "toolName": "sql_exec",
  "pattern": "(?i)drop\\s+table|truncate",
  "instruction": "Blocked by tool policy",
  "orderInPack": 1,
  "stopOnMatch": true,
  "maxEvaluationMs": 100
}
```

### 7.4 OnAfterToolCall: redact secrets

```json
{
  "hookPoint": "OnAfterToolCall",
  "ruleType": "regex_redact",
  "pattern": "(?i)api[_-]?key\\s*[:=]\\s*\\S+",
  "replacement": "api_key=[REDACTED]",
  "orderInPack": 1,
  "maxEvaluationMs": 100
}
```

### 7.5 OnBeforeResponse: require compliance keyword

```json
{
  "hookPoint": "OnBeforeResponse",
  "ruleType": "require_keyword",
  "pattern": "compliance",
  "instruction": "Include a compliance disclaimer section.",
  "orderInPack": 3,
  "maxEvaluationMs": 100
}
```

### 7.6 OnAfterResponse: post-response annotation (side-effect stage)

```json
{
  "hookPoint": "OnAfterResponse",
  "ruleType": "append_text",
  "instruction": "Post-response QA annotation",
  "orderInPack": 1,
  "maxEvaluationMs": 100
}
```

Important:
- `OnAfterResponse` runs after response emission.
- Changes are recorded in metadata for observability; they do not rewrite the already-sent final response.

### 7.7 OnError: retry transient tool failures

```json
{
  "hookPoint": "OnError",
  "ruleType": "tool_require",
  "toolName": "get_weather",
  "pattern": "(?i)timeout|rate limit|503",
  "instruction": "retry",
  "orderInPack": 1,
  "stopOnMatch": true,
  "maxEvaluationMs": 100
}
```


## 8. Model Switching via Rule Packs

### Dynamic Model Selection (e.g., Low-Cost Model for Email)

You can dynamically switch the LLM model used for a given iteration by setting `hookCtx.ModelOverride` in a custom hook or rule at the `OnBeforeIteration` stage. This allows you to route specific tasks (such as email generation) to a lower-cost model based on query content or other context.

**How to use:**

- Add a custom hook or rule to your rule pack at `OnBeforeIteration`.
- In the hook/rule, check if the user query or current task matches your criteria (e.g., contains "email").
- If matched, set `hookCtx.ModelOverride = "your-low-cost-model-name"`.
- The agent will use the specified model for that iteration only.

**Example: Custom Hook for Email Model Switching**

```csharp
// In your IOnBeforeIterationHook implementation:
public async Task OnBeforeIterationAsync(AgentHookContext ctx, CancellationToken ct)
{
  if (ctx.UserQuery.Contains("email", StringComparison.OrdinalIgnoreCase))
    ctx.ModelOverride = "gpt-3.5-turbo"; // or your preferred low-cost model
}
```

**Result:**
When the agent detects it is about to generate an email, it will automatically switch to the specified model for that iteration. All other queries continue to use the default model.

> **Note:** If your platform supports a data-driven `switch_model` rule type, you can use that instead of a custom hook. Otherwise, use the above pattern in a custom hook.

---
## 9. Recommended Patterns

- Keep pack `Priority` coarse (e.g., 10, 50, 100, 200) for easy insertion later.
- Use `StopOnMatch` for hard-block rules early in the pack.
- Prefer specific `ToolName` scoping on tool-related rules.
- Use dry-run test endpoint before enabling new packs.
- Keep regex patterns short and targeted to avoid timeout/complexity risks.

## 10. Troubleshooting

### "Unsupported rule configuration" on create/update
- Cause: invalid `HookPoint` + `RuleType` pairing.
- Fix: choose one from compatibility matrix in section 3.

### Rule saved but does not appear to run
- Check pack `IsEnabled`
- Check rule `IsEnabled`
- Check `ActivationCondition`
- Check `AppliesToJson` vs current archetype
- Check ordering and `StopOnMatch` behavior

### Tool call vanished from execution
- Likely filtered by `OnToolFilter` `block_pattern` rule.
- Confirm using `hook_executed` timeline details (`filtered` count).

### Error handling did not retry
- Ensure `OnError` rules match `toolName` and `pattern`.
- Ensure action resolves to `Retry` (via `instruction` or rule-type fallback).

## 10. Quick Start Recipe

1. Create pack with `Priority = 100`, `IsEnabled = true`
2. Add `OnInit` `inject_prompt` rule for baseline policy
3. Add `OnToolFilter` `block_pattern` for dangerous tool patterns
4. Add `OnAfterToolCall` `regex_redact` for secret scrubbing
5. Add `OnBeforeResponse` `require_keyword` for compliance text
6. Dry-run with sample query/response
7. Enable in production tenant and monitor hook timeline details
