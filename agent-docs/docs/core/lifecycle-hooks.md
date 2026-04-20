# Lifecycle Hooks

Lifecycle hooks are composable interceptors that plug into specific points of the agent execution lifecycle. They let you customize agent behavior — input validation, output formatting, tool filtering, error recovery, analytics logging — without modifying the core agent logic.

---

## Why Hooks Instead of Inheritance?

Traditional agent customization through class inheritance doesn't work well in a platform where tenant administrators configure agents through a UI. Hooks solve this by being:

- **Composable** — Multiple hooks can be active on the same agent, each handling a different concern
- **Configurable** — Hooks can be enabled, disabled, and reordered through the admin portal
- **Safe** — Hook failures are isolated and never crash the agent's execution stream
- **Ordered** — Each hook has a priority, so execution order is deterministic

Developers who want full programmatic control can still create custom agent classes, but hooks handle the vast majority of customization needs without writing code.

---

## The 7 Hook Points

The agent lifecycle has seven points where hooks can intercept:

```mermaid
sequenceDiagram
    participant Req as Request
    participant Init as OnInit
    participant Loop as ReAct Loop
    participant Before as OnBeforeIteration
    participant LLM
    participant Filter as OnToolFilter
    participant Tool as Tool Execution
    participant After as OnAfterToolCall
    participant Error as OnError
    participant BResp as OnBeforeResponse
    participant Verify as Verification
    participant AResp as OnAfterResponse

    Req->>Init: Agent execution starts
    Init-->>Loop: Setup complete
    
    loop Each ReAct Iteration
        Loop->>Before: Iteration N starting
        Before-->>LLM: (may modify system prompt)
        LLM-->>Filter: LLM returns tool calls
        Filter-->>Tool: (may filter/block tools)
        Tool-->>After: Tool result available
        After-->>Loop: (may transform result)
        
        opt If error occurs
            Tool-->>Error: Exception thrown
            Error-->>Loop: Recovery action
        end
    end
    
    Loop->>BResp: Final response ready
    BResp-->>Verify: (may transform text)
    Verify-->>AResp: Verification complete
    AResp-->>Req: Done (side effects)
```

---

### 1. OnInit

**When:** Once, when agent execution starts — before the ReAct loop begins.

**Use cases:**

- Load external resources (RAG indexes, configuration files)
- Validate or transform the incoming request
- Initialize shared state that other hooks will use
- Set up the system prompt based on request context

The `OnInit` hook has access to the full hook context, including the system prompt. Any modifications it makes to the prompt will be reflected in every subsequent LLM call.

---

### 2. OnBeforeIteration

**When:** At the start of each ReAct iteration, before the LLM is called.

**Use cases:**

- Inject iteration-specific context into the system prompt (e.g., "You've used 3 of 10 iterations")
- Implement rate limiting or cost guards
- Add dynamic instructions based on the current state of the conversation
- Trigger model switching based on custom heuristics

This is the most frequently called hook — it fires once per iteration across all continuation windows.

Two flags on the hook context are particularly useful for `OnBeforeIteration` logic:

| Flag | Set when | Typical use |
|------|----------|-------------|
| `WasTruncated` | The previous iteration hit the LLM's output token limit | Inject "keep responses concise" into the system prompt |
| `LastIterationResponse` | The previous iteration produced a text response (empty on iteration 1) | Pattern-match against the agent's last output to decide if a model switch is warranted |

---

### 3. OnToolFilter

**When:** After the LLM returns tool calls, before they are executed.

**Use cases:**

- Block specific tools based on business rules (e.g., "no destructive tools after business hours")
- Filter out duplicate tool calls
- Log tool usage for audit trails
- Implement progressive tool access (unlock tools after certain conditions are met)

The hook receives the list of proposed tool calls and returns a (potentially filtered) list. Any tool calls removed by the filter are never executed.

---

### 4. OnAfterToolCall

**When:** After each tool call completes, before the result is fed back to the LLM.

**Use cases:**

- Transform or sanitize tool output (e.g., redact PII)
- Enrich tool results with additional context
- Log tool results for analytics
- Trigger side effects based on tool outputs (e.g., send a notification when a booking is confirmed)

The hook receives the raw tool output and can return a modified version of it.

---

### 5. OnError

**When:** When a tool call or LLM call fails with an exception.

**Use cases:**

- Implement custom error recovery (retry with different parameters, fall back to a different tool)
- Send alerts on critical failures
- Log errors to external monitoring systems
- Gracefully degrade instead of failing the entire request

The hook returns a **recovery action** that tells the ReAct loop what to do:

| Action | Effect |
|--------|--------|
| **Continue** | Log the error and continue the loop normally (default behavior) |
| **Retry** | Retry the same tool call once |
| **Abort** | Stop the ReAct loop immediately and return an error response |

---

### 6. OnBeforeResponse

**When:** After the ReAct loop produces a final text response, before verification runs.

**Use cases:**

- Format the response (Markdown tables, bullet lists, specific structures)
- Redact sensitive information (PII, internal IDs, confidential data)
- Enforce output structure requirements (JSON schema, XML format)
- Add disclaimer text or legal notices

The hook receives the raw response text and returns a (potentially modified) version.

---

### 7. OnAfterResponse

**When:** After verification is complete, just before the response is returned to the caller. The last hook to fire.

**Use cases:**

- Log the complete interaction for analytics
- Trigger downstream webhooks (CRM updates, ticket creation)
- Update external dashboards
- Send follow-up notifications

This hook receives the complete `AgentResponse` (including verification results) and is purely for side effects — it cannot modify the response.

---

## Hook Execution Order

When multiple hooks are active, they execute in priority order within each hook point. Each hook has an `Order` value (lower = earlier). The built-in hooks use specific order values to establish a predictable chain:

| Order | Hook | Purpose |
|-------|------|---------|
| 2 | Tenant Rule Pack Hook | Evaluates Rule Pack rules at each hook point |
| 3 | Static Model Switcher | Applies per-agent model switching config |
| 4 | Model Router | Heuristic-based model switching |
| 100 | (Custom hooks default) | Any user-defined hooks |

Hooks at the same order value execute in registration order.

---

## Exception Safety

Hooks run inside the streaming ReAct loop, which uses `yield return` to emit SSE events. Since `yield return` cannot appear inside `try/catch` blocks in async iterators, a special **exception capture pattern** is used:

1. The hook call is wrapped in a `try/catch` that captures the exception into a temporary variable
2. After the catch block, the exception is checked and handled (logged, emitted as an error event)
3. The stream continues safely without crashing

This ensures that a buggy hook never takes down the entire agent execution stream. A hook failure is logged, an error event is emitted, and the agent continues (or breaks, depending on the severity).

---

## Rule Packs

**Rule Packs** extend the hook system by bundling configurable business rules that apply at multiple hook points. They are a database-driven way to inject behaviors without writing hook code.

A Rule Pack is a collection of typed rules (9 types supported) that a tenant administrator creates and assigns to agents. These rules are evaluated by the built-in Tenant Rule Pack Hook at the appropriate lifecycle points.

Rule types include:

| Rule Type | Hook Point | Effect |
|-----------|-----------|--------|
| Prompt injection | OnInit | Adds instructions to the system prompt |
| Tool filter | OnToolFilter | Blocks or allows specific tools |
| Output transform | OnBeforeResponse | Modifies response text (regex patterns) |
| Model switch | OnBeforeIteration | Changes the LLM model for specific iterations |
| Error recovery | OnError | Custom recovery behavior on failures |
| Compliance check | OnBeforeResponse | Flags content that violates policies |

The **model switch** rule type supports a `MatchTarget` field that controls which text the rule's pattern is matched against:

| MatchTarget value | Pattern matched against |
|--|--|
| `query` (default) | The original user query |
| `response` | The previous iteration's LLM response text |

Using `MatchTarget = "response"` lets you write rules like "switch to a stronger model when the agent says it is about to send an email" — triggering a model upgrade based on what the agent is planning to do next. On iteration 1 (no prior response), rules with `MatchTarget = "response"` and a non-blank pattern are automatically skipped.

Rule Packs include a **conflict analyzer** that detects contradictory rules (e.g., one rule allows a tool that another blocks) and warns administrators before they're activated.

---

## Built-In Hooks

Diva ships with six built-in hooks that handle core platform behaviors:

1. **Tenant Rule Pack Hook** (Order 2) — Evaluates all active Rule Pack rules at every supported hook point. This is the bridge between the declarative Rule Pack configuration and the hook pipeline.

2. **Static Model Switcher Hook** (Order 3) — Reads per-agent model switching configuration and applies model changes based on the iteration phase (tool-calling, final response, re-planning, failure escalation).

3. **Model Router Hook** (Order 4) — Heuristic-based model routing that automatically selects cheaper models for tool iterations and stronger models for synthesis, based on agent variables.

4. **Execution Mode Filter** — Enforces the agent's execution mode (ChatOnly, ReadOnly, Supervised) by filtering tools at the OnToolFilter hook point.

5. **Tool Access Level Filter** — Works with execution mode to filter tools based on their ReadOnly/ReadWrite/Destructive access level tags.

6. **Error Recovery Hook** — Default error handling that logs failures and implements the standard continue/retry/abort logic.

These hooks form the baseline behavior that all agents get automatically. Custom hooks layer on top of them, using the `Order` system to control whether they run before or after the built-in behaviors.
