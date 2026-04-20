# Architecture: Response Verification & Hallucination Detection

> **Status:** Reference — implementation in [phase-13-verification.md](phase-13-verification.md)
> **Related phases:** [phase-08-agents.md](phase-08-agents.md), [phase-09-llm-client.md](phase-09-llm-client.md)

---

## The Problem

LLMs fabricate plausible-sounding facts when they have no grounded data source. In a business context this is dangerous:

- "What is today's Sensex?" → agent invents `62,150.45` with no tool call
- "Get last week's revenue" → agent fabricates a table from training data
- "What did the customer say in their last call?" → agent hallucinates a conversation

The existing flow has **zero post-processing** — the LLM output goes directly to the caller:

```
User → [ReAct Loop] → LLM response → AgentResponse (no checks)
```

---

## Verification Model

Every agent response carries an **evidence trail**: the raw text returned by every MCP tool call made during the ReAct loop. The verifier cross-checks the final response against this evidence.

### Claim Types

| Type | Definition | Risk |
|---|---|---|
| **Grounded** | Directly present in tool result text | Low — fully supported |
| **Inferred** | Logical conclusion from grounded data | Low — acceptable reasoning |
| **Ungrounded** | Factual assertion (number, name, date, event) with no tool backing | High — hallucination risk |

### Confidence Score

```
1.0  — all factual claims are grounded in tool evidence
0.7  — some claims are inferred (acceptable)
0.4  — factual claims present but no tools were called
0.0  — response contains specific facts but evidence explicitly contradicts them
```

---

## Verification Modes

Configured globally in `appsettings.json` (the default for all agents), or overridden per agent via `AgentDefinitionEntity.VerificationMode`:

| Mode | How it works | LLM cost | Use case |
|---|---|---|---|
| `Off` | No verification | None | Dev/internal |
| `ToolGrounded` | If no tools were called, flag all factual claims | None | Lightweight production check |
| `LlmVerifier` | Second-pass LLM call checks claims against tool evidence | ~1 extra call | High-stakes tenants |
| `Strict` | Same as LlmVerifier but blocks the response if confidence < threshold | ~1 extra call | Finance, healthcare |
| `Auto` | Runtime heuristic: picks cheapest sufficient check based on response length, tools called, and evidence available | 0–1 extra call | **Default** — recommended for most agents |

### Auto Mode Decision Tree

```
response < 80 chars?            → Off (nothing meaningful to check)
tools called + evidence present → LlmVerifier (full LLM check worthwhile)
tools called, no evidence       → ToolGrounded (unexpected; cheap fallback)
no tools + factual claims       → ToolGrounded (flag suspicious assertions)
no tools + conversational text  → Off (skip — purely conversational)
```

### Per-Agent Override

`AgentDefinitionEntity.VerificationMode` (nullable `string`) lets an admin pin a specific mode for any individual agent, independent of the global config:

| Agent type | `VerificationMode` field | Effective mode | Cost |
|------------|--------------------------|----------------|------|
| Chatbot (no tools) | `null` → global `"Auto"` | Off (conversational) | 0ms |
| Data agent (tools + evidence) | `null` → global `"Auto"` | LlmVerifier | ~500ms |
| Compliance agent | `"Strict"` | Strict | ~500ms + blocks |
| Dev/test agent | `"Off"` | Off | 0ms |

The `modeOverride` parameter on `VerifyAsync` carries this value from `AnthropicAgentRunner`; it takes precedence over the global config when non-null. `VerifyStage` (supervisor path) intentionally uses only the global config — it integrates results from multiple workers with no single agent definition.

---

## Where Verification Fits in the Flow

### Single Agent (AnthropicAgentRunner)

```
User request
     │
     ▼
┌──────────────────────────────────┐
│  ReAct Loop (Think → Act → Obs) │
│  Collects: finalResponse         │
│            toolsUsed[]           │
│            toolEvidence[]  ← NEW │
└──────────────┬───────────────────┘
               │
               ▼
┌──────────────────────────────────┐
│  ResponseVerifier.VerifyAsync()  │ ← NEW
│                                  │
│  ToolGrounded mode:              │
│    no tools called?              │
│    → confidence=0.4, flagged     │
│                                  │
│  LlmVerifier mode:               │
│    send (response + evidence)    │
│    to LLM verifier prompt        │
│    → extract UngroundedClaims    │
│    → confidence score            │
│                                  │
│  Strict mode:                    │
│    confidence < threshold?       │
│    → replace content with        │
│      "I cannot verify this"      │
│      WasBlocked = true           │
└──────────────┬───────────────────┘
               │
               ▼
        AgentResponse
        + Verification { IsVerified, Confidence,
                         UngroundedClaims, Mode, WasBlocked }
```

### Supervisor Pipeline

Inserted as a named stage between `IntegrateStage` and `DeliverStage`:

```
Decompose → CapabilityMatch → Dispatch → Monitor → Integrate → Verify → Deliver
                                                                  ↑
                                                            NEW STAGE
```

`VerifyStage` pulls `IntegratedResult` + collected `WorkerResults[].ToolEvidence` from `SupervisorState` and runs the same `ResponseVerifier`.

---

## Evidence Trail

The `toolEvidence` is accumulated during the ReAct loop — every tool call result is appended:

```csharp
// In ExecuteReActLoopAsync — tool evidence collected via ILlmProviderStrategy
var toolEvidence = new List<string>();     // collect here

// Inside foreach (var toolUse in response.Content.OfType<ToolUseContent>())
var resultText = string.Join("\n", callResult.Content.OfType<TextContentBlock>().Select(c => c.Text));
toolEvidence.Add($"[Tool: {toolUse.Name}]\n{resultText}");
```

All evidence is passed to the verifier as a single concatenated string. The LLM verifier prompt can then cross-check the final response against it.

---

## LLM Verifier Prompt

Stored in `prompts/verify-response.txt`:

```
You are a fact-checking assistant for an AI agent platform.

TOOL EVIDENCE (ground truth — data returned by real tool calls):
{toolEvidence}

AGENT RESPONSE (to verify):
{agentResponse}

Task: Identify factual claims in the agent response that are NOT supported by the tool evidence
and cannot be logically inferred from it. Focus on specific numbers, dates, names, prices,
statistics, or events that were asserted without evidence.

If no tools were called (tool evidence is empty) and the response contains specific facts,
treat all such facts as ungrounded.

Respond with ONLY this JSON (no markdown):
{
  "confidence": <float 0.0-1.0>,
  "is_verified": <bool>,
  "ungrounded_claims": ["<claim 1>", "<claim 2>"],
  "reasoning": "<one sentence>"
}
```

---

## New Models

### `VerificationResult` (in `Diva.Core/Models/`)

```csharp
public sealed class VerificationResult
{
    public bool IsVerified { get; init; }
    public float Confidence { get; init; }              // 0.0–1.0
    public string Mode { get; init; } = "Off";
    public List<string> UngroundedClaims { get; init; } = [];
    public bool WasBlocked { get; init; }               // true if Strict mode blocked the response
    public string? Reasoning { get; init; }             // LlmVerifier mode only
}
```

### `VerificationOptions` (in `Diva.Core/Configuration/`)

```csharp
public sealed class VerificationOptions
{
    public const string SectionName = "Verification";
    public string Mode { get; init; } = "ToolGrounded"; // Off | ToolGrounded | LlmVerifier | Strict | Auto
    public float ConfidenceThreshold { get; init; } = 0.5f;  // below this → blocked in Strict mode
    public bool IncludeReasoningInResponse { get; init; } = false;
    public string? VerifierModel { get; init; }    // pin a cheaper model for verification (falls back to agent model)
    public int MaxVerificationRetries { get; init; } = 1;  // inline correction retry count
}
```

### `AgentDefinitionEntity` (in `Diva.Infrastructure/Data/Entities/`)

```csharp
/// <summary>
/// Optional per-agent verification mode override.
/// Values: Off | ToolGrounded | LlmVerifier | Strict | Auto
/// Null = use the global Verification:Mode from appsettings.
/// </summary>
public string? VerificationMode { get; set; }
```

### `AgentResponse` additions

```csharp
// Add to existing AgentResponse:
public VerificationResult? Verification { get; init; }
```

### `SupervisorState` additions

```csharp
// Add to existing SupervisorState:
public string ToolEvidence { get; set; } = "";      // accumulated by DispatchStage from WorkerResults
public VerificationResult? Verification { get; set; } // set by VerifyStage
```

---

## New Files

```
src/
├── Diva.Core/
│   ├── Models/
│   │   └── VerificationResult.cs               ← NEW
│   └── Configuration/
│       └── VerificationOptions.cs              ← NEW
│
├── Diva.Infrastructure/
│   └── Verification/
│       └── ResponseVerifier.cs                 ← NEW (singleton service)
│
└── Diva.Agents/
    └── Supervisor/
        └── Stages/
            └── VerifyStage.cs                  ← NEW pipeline stage

prompts/
└── verify-response.txt                         ← NEW LLM verifier prompt
```

---

## `ResponseVerifier.cs` Skeleton

```csharp
namespace Diva.Infrastructure.Verification;

public sealed class ResponseVerifier
{
    private readonly VerificationOptions _opts;
    private readonly ILogger<ResponseVerifier> _logger;

    // Injected for LlmVerifier/Strict modes only
    private readonly AnthropicAgentRunner? _runner;

    public async Task<VerificationResult> VerifyAsync(
        string responseText,
        IReadOnlyList<string> toolsUsed,
        string toolEvidence,
        CancellationToken ct)
    {
        return _opts.Mode switch
        {
            "Off"           => Skipped(),
            "ToolGrounded"  => ToolGroundedCheck(responseText, toolsUsed),
            "LlmVerifier"   => await LlmVerifyAsync(responseText, toolEvidence, ct),
            "Strict"        => await StrictVerifyAsync(responseText, toolEvidence, ct),
            _               => Skipped()
        };
    }

    private static VerificationResult Skipped() =>
        new() { IsVerified = true, Confidence = 1f, Mode = "Off" };

    private VerificationResult ToolGroundedCheck(string response, IReadOnlyList<string> toolsUsed)
    {
        // No tools called → flag if response contains numbers/dates/statistics
        if (toolsUsed.Count == 0 && ContainsFactualClaims(response))
            return new() { IsVerified = false, Confidence = 0.4f, Mode = "ToolGrounded",
                           UngroundedClaims = ["Response contains factual claims but no tools were called"] };

        return new() { IsVerified = true, Confidence = 0.85f, Mode = "ToolGrounded" };
    }

    private async Task<VerificationResult> LlmVerifyAsync(string response, string evidence, CancellationToken ct)
    {
        // Call LLM with verify-response.txt prompt
        // Parse JSON result
        // Return VerificationResult
        throw new NotImplementedException();
    }

    private async Task<VerificationResult> StrictVerifyAsync(string response, string evidence, CancellationToken ct)
    {
        var result = await LlmVerifyAsync(response, evidence, ct);
        if (result.Confidence < _opts.ConfidenceThreshold)
            return result with { WasBlocked = true };
        return result;
    }

    // Heuristic: contains digits ≥ 3 chars (prices, stats, percentages, dates)
    private static bool ContainsFactualClaims(string text) =>
        System.Text.RegularExpressions.Regex.IsMatch(text, @"\d{2,}");
}
```

---

## `VerifyStage.cs` Skeleton

```csharp
namespace Diva.Agents.Supervisor.Stages;

public class VerifyStage : ISupervisorPipelineStage
{
    private readonly ResponseVerifier _verifier;

    public async Task<SupervisorState> ExecuteAsync(SupervisorState state, CancellationToken ct)
    {
        // Collect all tool evidence from worker results
        var evidence = string.Join("\n\n", state.WorkerResults
            .Select(r => r.ToolEvidence)
            .Where(e => !string.IsNullOrEmpty(e)));

        var verification = await _verifier.VerifyAsync(
            state.IntegratedResult,
            state.WorkerResults.SelectMany(r => r.ToolsUsed).ToList(),
            evidence,
            ct);

        if (verification.WasBlocked)
        {
            state.IntegratedResult =
                "I was unable to verify the accuracy of this response. " +
                "Please ask again with a more specific question or check the source directly.";
        }

        return state with { Verification = verification };
    }
}
```

---

## Frontend: Verification Badge

The admin portal `AgentChat` renders a verification badge under each agent response:

| Badge | Meaning |
|---|---|
| ✅ Verified | All claims grounded in tool data |
| ⚠️ Unverified | No tools called — claims may be hallucinated |
| ❌ Blocked | Strict mode blocked a low-confidence response |
| 🔍 Flagged claims | LlmVerifier found specific ungrounded assertions |

---

## appsettings.json Configuration

```json
"Verification": {
  "Mode": "Auto",
  "ConfidenceThreshold": 0.5,
  "IncludeReasoningInResponse": true
}
```

Pin a specific (cheaper) model for verification:
```json
"Verification": {
  "Mode": "Auto",
  "VerifierModel": "claude-haiku-4-5-20251001"
}
```

Override to strict for a high-stakes environment:
```json
// appsettings.Production.json
"Verification": {
  "Mode": "Strict",
  "ConfidenceThreshold": 0.65
}
```

---

## Tradeoffs & Decisions

| Question | Decision | Reason |
|---|---|---|
| Where to verify? | Both single-agent and supervisor paths | Verification must be universal, not only when Supervisor is used |
| Evidence format? | Raw tool result text (not structured) | Simpler to pass through; LLM verifier handles parsing |
| Block or warn? | Warn by default, block only in `Strict` mode | Blocking breaks UX; warning lets users decide |
| Which LLM for verification? | Same provider as the agent | Avoids second API key; can use a cheaper/faster model via config |
| Verification of inferences? | Not blocked — only ungrounded assertions | Inferences are expected and useful; only fabricated facts are dangerous |

---

## Dependency on Phase 13

This architecture depends on:
1. `AgentResponse.ToolEvidence` being populated by `AnthropicAgentRunner` ← Update Phase 9 runner
2. `SupervisorState.ToolEvidence` being accumulated by `DispatchStage` ← Update Phase 8
3. `VerifyStage` registered in the pipeline between `IntegrateStage` and `DeliverStage`
4. `ResponseVerifier` registered as `Singleton` in DI
