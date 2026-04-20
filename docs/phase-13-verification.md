# Phase 13: Response Verification & Hallucination Detection

> **Status:** `[x]` Done
> **Depends on:** [phase-08-agents.md](phase-08-agents.md), [phase-09-llm-client.md](phase-09-llm-client.md)
> **Blocks:** Nothing (additive — can be layered on after Phase 10)
> **Projects:** `Diva.Core`, `Diva.Infrastructure`, `Diva.Agents`, `admin-portal`
> **Architecture ref:** [arch-response-verification.md](arch-response-verification.md)

---

## Goal

Add a verification layer that checks every agent response against the tool evidence collected during the ReAct loop. Ungrounded factual claims are flagged, scored, and — in Strict mode — blocked before delivery.

---

## Files to Create / Modify

```
src/
├── Diva.Core/
│   ├── Models/
│   │   └── VerificationResult.cs               ← CREATE
│   └── Configuration/
│       └── VerificationOptions.cs              ← CREATE
│
├── Diva.Infrastructure/
│   └── Verification/
│       └── ResponseVerifier.cs                 ← CREATE
│
└── Diva.Agents/
    └── Supervisor/
        └── Stages/
            └── VerifyStage.cs                  ← CREATE

prompts/
└── verify-response.txt                         ← CREATE

src/Diva.Core/Models/AgentResponse.cs           ← MODIFY (add Verification + ToolEvidence)
src/Diva.Agents/Supervisor/SupervisorState.cs   ← MODIFY (add ToolEvidence + Verification)
src/Diva.Agents/Supervisor/Stages/DispatchStage.cs ← MODIFY (accumulate ToolEvidence)
src/Diva.Infrastructure/LiteLLM/AnthropicAgentRunner.cs ← MODIFY (collect evidence, call verifier)
src/Diva.Host/Program.cs                        ← MODIFY (register ResponseVerifier, VerifyStage)
src/Diva.Host/appsettings.json                  ← MODIFY (add Verification section)
admin-portal/src/components/AgentChat.tsx       ← MODIFY (render verification badge)
admin-portal/src/api.ts                         ← MODIFY (add VerificationResult type)
```

---

## Step 1 — Core Models

### `Diva.Core/Models/VerificationResult.cs`

```csharp
namespace Diva.Core.Models;

public sealed class VerificationResult
{
    /// <summary>True if all factual claims are grounded in tool evidence.</summary>
    public bool IsVerified { get; init; }

    /// <summary>0.0 = no confidence (high hallucination risk), 1.0 = fully verified.</summary>
    public float Confidence { get; init; }

    /// <summary>Which mode produced this result: Off | ToolGrounded | LlmVerifier | Strict</summary>
    public string Mode { get; init; } = "Off";

    /// <summary>Specific claims identified as ungrounded (LlmVerifier/Strict mode only).</summary>
    public List<string> UngroundedClaims { get; init; } = [];

    /// <summary>True if Strict mode replaced the response content with a refusal.</summary>
    public bool WasBlocked { get; init; }

    /// <summary>One-sentence explanation from the LLM verifier (LlmVerifier/Strict mode only).</summary>
    public string? Reasoning { get; init; }
}
```

### `Diva.Core/Configuration/VerificationOptions.cs`

```csharp
namespace Diva.Core.Configuration;

public sealed class VerificationOptions
{
    public const string SectionName = "Verification";

    /// <summary>Off | ToolGrounded | LlmVerifier | Strict</summary>
    public string Mode { get; init; } = "ToolGrounded";

    /// <summary>Confidence below this value triggers a block in Strict mode.</summary>
    public float ConfidenceThreshold { get; init; } = 0.5f;

    /// <summary>Include the verifier's reasoning text in the API response.</summary>
    public bool IncludeReasoningInResponse { get; init; } = false;
}
```

### Modify `Diva.Core/Models/AgentResponse.cs`

Add two fields:

```csharp
/// <summary>Verification result for this response. Null if Mode=Off.</summary>
public VerificationResult? Verification { get; init; }

/// <summary>Concatenated raw text of all tool results used during this response.</summary>
public string ToolEvidence { get; init; } = string.Empty;
```

---

## Step 2 — Prompt Template

### `prompts/verify-response.txt`

```
You are a fact-checking assistant for an enterprise AI agent platform.

TOOL EVIDENCE (ground truth — verbatim data returned by MCP tool calls during this session):
---
{toolEvidence}
---

AGENT RESPONSE (the text to verify):
---
{agentResponse}
---

Your task: identify any factual claims in the agent response that are NOT supported by the
tool evidence and cannot be logically inferred from it.

Definition of a factual claim: a specific number, price, date, name, statistic, percentage,
event, or measurement. General knowledge, definitions, and explanations do NOT count.

Rules:
- If the tool evidence is empty and the response contains any factual claims, ALL such claims
  are ungrounded.
- If a claim can be directly calculated or logically inferred from the tool evidence, it is
  NOT ungrounded.
- Do not flag the agent's writing style, tone, or structure — only factual accuracy.

Respond with ONLY valid JSON (no markdown code block, no explanation outside the JSON):
{
  "confidence": <float between 0.0 and 1.0>,
  "is_verified": <true if confidence >= 0.6 and no critical ungrounded claims, else false>,
  "ungrounded_claims": ["<exact quote of claim 1>", "<exact quote of claim 2>"],
  "reasoning": "<one sentence summary of your finding>"
}
```

---

## Step 3 — ResponseVerifier Service

### `Diva.Infrastructure/Verification/ResponseVerifier.cs`

```csharp
using System.Text.Json;
using System.Text.RegularExpressions;
using Diva.Core.Configuration;
using Diva.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Diva.Infrastructure.Verification;

public sealed class ResponseVerifier
{
    private readonly VerificationOptions _opts;
    private readonly ILogger<ResponseVerifier> _logger;

    // For LlmVerifier/Strict: reuse the same LLM client via a lightweight request helper
    private readonly Func<string, string, CancellationToken, Task<string>>? _llmCall;

    public ResponseVerifier(
        IOptions<VerificationOptions> opts,
        ILogger<ResponseVerifier> logger,
        Func<string, string, CancellationToken, Task<string>>? llmCall = null)
    {
        _opts    = opts.Value;
        _logger  = logger;
        _llmCall = llmCall;
    }

    public async Task<VerificationResult> VerifyAsync(
        string responseText,
        IReadOnlyList<string> toolsUsed,
        string toolEvidence,
        CancellationToken ct)
    {
        _logger.LogDebug("Verifying response (mode={Mode}, tools={Count})", _opts.Mode, toolsUsed.Count);

        return _opts.Mode switch
        {
            "Off"          => Skipped(),
            "ToolGrounded" => ToolGroundedCheck(responseText, toolsUsed),
            "LlmVerifier"  => await LlmVerifyAsync(responseText, toolEvidence, block: false, ct),
            "Strict"       => await LlmVerifyAsync(responseText, toolEvidence, block: true, ct),
            _              => Skipped()
        };
    }

    // ── Modes ─────────────────────────────────────────────────────────────────

    private static VerificationResult Skipped() =>
        new() { IsVerified = true, Confidence = 1f, Mode = "Off" };

    private VerificationResult ToolGroundedCheck(string response, IReadOnlyList<string> toolsUsed)
    {
        if (toolsUsed.Count == 0 && ContainsFactualClaims(response))
        {
            _logger.LogWarning("Unverified response: factual claims present but no tools were called");
            return new VerificationResult
            {
                IsVerified       = false,
                Confidence       = 0.4f,
                Mode             = "ToolGrounded",
                UngroundedClaims = ["Response contains factual claims but no tools were called to support them"],
                Reasoning        = "No tool evidence available to verify factual assertions"
            };
        }

        return new VerificationResult
        {
            IsVerified = true,
            Confidence = toolsUsed.Count > 0 ? 0.85f : 0.95f,
            Mode       = "ToolGrounded"
        };
    }

    private async Task<VerificationResult> LlmVerifyAsync(
        string response, string evidence, bool block, CancellationToken ct)
    {
        if (_llmCall is null)
        {
            _logger.LogWarning("LlmVerifier mode requested but no LLM call delegate registered — falling back to ToolGrounded");
            return ToolGroundedCheck(response, []);
        }

        try
        {
            var prompt = BuildVerifierPrompt(response, evidence);
            var raw = await _llmCall(prompt, response, ct);
            var parsed = ParseVerifierResponse(raw);

            var shouldBlock = block && parsed.Confidence < _opts.ConfidenceThreshold;

            if (shouldBlock)
                _logger.LogWarning("Strict mode blocking response — confidence={Conf}", parsed.Confidence);

            return parsed with
            {
                Mode       = block ? "Strict" : "LlmVerifier",
                WasBlocked = shouldBlock,
                Reasoning  = _opts.IncludeReasoningInResponse ? parsed.Reasoning : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM verification call failed — allowing response through");
            return new VerificationResult { IsVerified = true, Confidence = 0.5f, Mode = "LlmVerifier" };
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string BuildVerifierPrompt(string response, string evidence)
    {
        // Prompt template is inline for now; move to prompts/verify-response.txt in production
        return $"""
            You are a fact-checking assistant. Check if the agent response is supported by the tool evidence.

            TOOL EVIDENCE:
            ---
            {(string.IsNullOrEmpty(evidence) ? "(no tools were called)" : evidence)}
            ---

            AGENT RESPONSE:
            ---
            {response}
            ---

            Respond ONLY with JSON:
            {{"confidence": 0.0-1.0, "is_verified": bool, "ungrounded_claims": ["..."], "reasoning": "..."}}
            """;
    }

    private static VerificationResult ParseVerifierResponse(string raw)
    {
        // Strip any markdown fences the LLM may have added
        var json = Regex.Replace(raw.Trim(), @"^```json?\s*|\s*```$", "", RegexOptions.Multiline).Trim();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var confidence = root.TryGetProperty("confidence", out var conf) ? (float)conf.GetDouble() : 0.5f;
        var isVerified = root.TryGetProperty("is_verified", out var iv) && iv.GetBoolean();
        var reasoning  = root.TryGetProperty("reasoning",   out var r)  ? r.GetString() : null;

        var claims = new List<string>();
        if (root.TryGetProperty("ungrounded_claims", out var uc) && uc.ValueKind == JsonValueKind.Array)
            foreach (var item in uc.EnumerateArray())
                if (item.GetString() is { } s)
                    claims.Add(s);

        return new VerificationResult
        {
            IsVerified       = isVerified,
            Confidence       = confidence,
            UngroundedClaims = claims,
            Reasoning        = reasoning
        };
    }

    /// <summary>
    /// Heuristic: does the text contain numbers or statistics that look like factual claims?
    /// Matches things like prices ($1,234), percentages (7.9%), counts (1,250 transactions).
    /// </summary>
    private static bool ContainsFactualClaims(string text) =>
        Regex.IsMatch(text, @"[\$\£\€]?\d[\d,\.]*\s*(%|transactions?|units?|pts?|points?)?",
            RegexOptions.IgnoreCase);
}
```

---

## Step 4 — VerifyStage (Supervisor Pipeline)

### `Diva.Agents/Supervisor/Stages/VerifyStage.cs`

```csharp
using Diva.Agents.Supervisor;
using Diva.Infrastructure.Verification;

namespace Diva.Agents.Supervisor.Stages;

public class VerifyStage : ISupervisorPipelineStage
{
    private readonly ResponseVerifier _verifier;
    private readonly ILogger<VerifyStage> _logger;

    public VerifyStage(ResponseVerifier verifier, ILogger<VerifyStage> logger)
    {
        _verifier = verifier;
        _logger   = logger;
    }

    public async Task<SupervisorState> ExecuteAsync(SupervisorState state, CancellationToken ct)
    {
        // Collect all tool evidence accumulated by worker agents
        var allTools   = state.WorkerResults.SelectMany(r => r.ToolsUsed).ToList();
        var allEvidence = string.Join("\n\n", state.WorkerResults
            .Select(r => r.ToolEvidence)
            .Where(e => !string.IsNullOrEmpty(e)));

        var verification = await _verifier.VerifyAsync(
            state.IntegratedResult, allTools, allEvidence, ct);

        _logger.LogInformation(
            "Verification: mode={Mode} confidence={Conf:F2} verified={Ok} blocked={Blocked}",
            verification.Mode, verification.Confidence, verification.IsVerified, verification.WasBlocked);

        if (verification.WasBlocked)
        {
            state.IntegratedResult =
                "I was unable to verify the accuracy of this response with sufficient confidence. " +
                "Please refine your question or consult the source data directly.";
        }

        return state with { Verification = verification };
    }
}
```

---

## Step 5 — Wire into AnthropicAgentRunner

### Modifications to `RunAnthropicAsync`

```csharp
// 1. Collect tool evidence during ReAct loop (add alongside toolsUsed):
var toolEvidence = new List<string>();

// Inside the tool use loop:
toolEvidence.Add($"[Tool: {toolUse.Name}]\n{resultText}");

// 2. After the loop completes, verify the final response:
var evidence = string.Join("\n\n", toolEvidence);
var verification = await _verifier.VerifyAsync(finalResponse, toolsUsed, evidence, ct);

// 3. If Strict mode blocked it, replace the content:
var content = verification.WasBlocked
    ? "I was unable to verify the accuracy of this response. Please try a more specific question."
    : finalResponse;

// 4. Return with verification attached:
return new AgentResponse
{
    Success      = true,
    Content      = content,
    ToolEvidence = evidence,
    Verification = verification,
    // ... other fields
};
```

Same pattern for `RunOpenAiCompatibleAsync` — collect tool evidence from `FunctionCallContent` + result messages.

---

## Step 6 — SupervisorState Modifications

```csharp
// Add to SupervisorState:
public string ToolEvidence { get; set; } = "";         // accumulated by DispatchStage
public VerificationResult? Verification { get; set; }  // set by VerifyStage
```

### DispatchStage Modification

After each worker agent returns, accumulate its evidence:

```csharp
// After collecting worker result:
state.ToolEvidence += $"\n\n[Agent: {result.AgentName}]\n{result.ToolEvidence}";
```

---

## Step 7 — DI Registration

### `Program.cs`

```csharp
// Bind options
builder.Services.Configure<VerificationOptions>(
    builder.Configuration.GetSection(VerificationOptions.SectionName));

// Register verifier as singleton (stateless)
builder.Services.AddSingleton<ResponseVerifier>();

// Register pipeline stage (in order, between IntegrateStage and DeliverStage)
builder.Services.AddScoped<ISupervisorPipelineStage, DecomposeStage>();
builder.Services.AddScoped<ISupervisorPipelineStage, CapabilityMatchStage>();
builder.Services.AddScoped<ISupervisorPipelineStage, DispatchStage>();
builder.Services.AddScoped<ISupervisorPipelineStage, MonitorStage>();
builder.Services.AddScoped<ISupervisorPipelineStage, IntegrateStage>();
builder.Services.AddScoped<ISupervisorPipelineStage, VerifyStage>();    // ← NEW position
builder.Services.AddScoped<ISupervisorPipelineStage, DeliverStage>();
```

### `appsettings.json`

```json
"Verification": {
  "Mode": "ToolGrounded",
  "ConfidenceThreshold": 0.5,
  "IncludeReasoningInResponse": false
}
```

```json
// appsettings.Development.json — always-on for testing
"Verification": {
  "Mode": "ToolGrounded"
}
```

---

## Step 8 — Admin Portal

### `api.ts` additions

```typescript
export interface VerificationResult {
  isVerified: boolean;
  confidence: number;       // 0.0–1.0
  mode: string;
  ungroundedClaims: string[];
  wasBlocked: boolean;
  reasoning?: string;
}

// Update AgentResponse:
export interface AgentResponse {
  // ... existing fields ...
  verification?: VerificationResult;
}
```

### `AgentChat.tsx` — Verification Badge

Rendered below each agent message bubble:

```tsx
{m.verification && (
  <div style={{ fontSize: "0.7rem", marginTop: 4, display: "flex", gap: 6, alignItems: "center" }}>
    {m.verification.wasBlocked ? (
      <span style={{ color: "#ef4444" }}>❌ Blocked — low confidence response</span>
    ) : m.verification.isVerified ? (
      <span style={{ color: "#22c55e" }}>✅ Verified (confidence: {Math.round(m.verification.confidence * 100)}%)</span>
    ) : (
      <span style={{ color: "#f59e0b" }}>
        ⚠️ Unverified
        {m.verification.ungroundedClaims.length > 0 &&
          ` — ${m.verification.ungroundedClaims.length} unsupported claim(s)`}
      </span>
    )}
  </div>
)}
```

---

## Verification Checklist

- [x] `VerificationResult` and `VerificationOptions` models created in `Diva.Core`
- [x] `AgentResponse.Verification` and `AgentResponse.ToolEvidence` added
- [x] `ResponseVerifier` service implemented and registered as Singleton
- [x] `AnthropicAgentRunner` collects `toolEvidence` and calls verifier after every run
- [x] `OpenAI-compatible` path also collects tool evidence and calls verifier
- [x] `VerifyStage` added to supervisor pipeline (between Integrate and Deliver)
- [x] `SupervisorState.ToolEvidence` accumulated by `DispatchStage`
- [x] `appsettings.json` includes `Verification` section
- [x] Admin portal renders verification badge (✅ / ⚠️ / ❌)
- [x] `LlmVerifier` mode tested: verifier correctly identifies the Sensex hallucination example
- [x] `Strict` mode tested: blocked response replaced with refusal message
- [x] `Off` mode tested: zero overhead, no verification field in response

---

## Post-Phase Improvements (added after initial implementation)

### Correction Retry — Inline in ReAct Loop (with Tool Access)

Verification now runs **inside the ReAct loop's exit point** rather than as a separate post-loop pass. When the model produces a candidate final response (no tool calls), `VerifyAsync` is called inline. If claims are ungrounded and retries remain, the bad response plus a correction prompt are injected into the conversation and the loop `continue`s — meaning the model can call tools in the next iteration to actually retrieve the missing data.

```
ReAct loop:
  → model returns text (no tool calls)
  → VerifyAsync inline
  → if failed + retries left: inject correction + continue (model may now call tools)
  → if passes or retries exhausted: break
```

The correction prompt tells the model: *"Please call the appropriate tools to retrieve evidence for these specific claims…"* — so tool-calling is actively encouraged rather than just rewriting from existing context.

`verificationRetries` (derived from `MaxVerificationRetries`) is tracked separately from `maxIterations`, so correction attempts do not consume the main iteration budget.

New config option in `VerificationOptions`:
```csharp
public int MaxVerificationRetries { get; init; } = 1;
```

New stream chunk type `"correction"` emitted when a retry is triggered, with the correction prompt text as `Content`.

For the non-streaming OpenAI-compatible path (`RunOpenAiCompatibleAsync`), which uses `UseFunctionInvocation` internally, the correction re-calls `GetResponseAsync` with tools still enabled so `UseFunctionInvocation` can trigger another tool loop if needed.

`GetCorrectionAsync` (the earlier tool-less rewrite helper) was removed; correction is now entirely handled by continuing the existing ReAct loop.

### Evidence Format Fix
`RunOpenAiCompatibleAsync` previously labelled tool evidence as `[Tool: call_abc123]` (opaque call ID). Fixed to build a `callId→name` lookup from `FunctionCallContent` so evidence reads `[Tool: GetReservations]`.

### Trivial Response Skip
LLM verification is skipped for responses under 80 characters — no factual content to check.

### Auto Verification Mode

A new `"Auto"` mode was added to `ResponseVerifier` to replace the previous fixed-cost default. Auto picks the cheapest sufficient check at runtime:

```csharp
private async Task<VerificationResult> AutoVerifyAsync(
    string responseText, IReadOnlyList<string> toolsUsed, string toolEvidence,
    CancellationToken ct, string? modelId)
{
    if (string.IsNullOrWhiteSpace(responseText) || responseText.Length < 80)
        return Skipped();
    if (toolsUsed.Count > 0 && !string.IsNullOrWhiteSpace(toolEvidence))
        return await LlmVerifyAsync(responseText, toolEvidence, block: false, ct, modelId);
    if (toolsUsed.Count > 0)
        return ToolGroundedCheck(responseText, toolsUsed);
    if (ContainsFactualClaims(responseText))
        return ToolGroundedCheck(responseText, toolsUsed);
    return Skipped();
}
```

`appsettings.json` global default changed from `"ToolGrounded"` to `"Auto"`. `"Auto"` is now the recommended default for most agents — it incurs zero cost on conversational replies and full LLM verification only when tools were called and evidence is available.

### Per-Agent `VerificationMode` Override

`AgentDefinitionEntity` gained a nullable `VerificationMode` property (EF migration: `AddAgentVerificationMode`). An admin can pin any mode for a specific agent, independent of the global config. The override flows via a new optional `modeOverride` parameter on `VerifyAsync`:

```csharp
public async Task<VerificationResult> VerifyAsync(
    string responseText,
    IReadOnlyList<string> toolsUsed,
    string toolEvidence,
    CancellationToken ct,
    string? modelId = null,
    string? modeOverride = null)   // per-agent override; takes precedence over global config
```

All `VerifyAsync` call sites in `AnthropicAgentRunner` (7 total — covering both streaming and non-streaming paths for Anthropic and OpenAI-compatible providers) pass `definition.VerificationMode`. `VerifyStage` in the supervisor pipeline is intentionally unchanged — it uses global config because it integrates results across multiple worker agents with no single agent definition in scope.

### Ungrounded Claims UI
The ⚠️ badge now lists each ungrounded claim as a bullet under the badge instead of showing only a count.

### Per-session Model Propagation
`VerifyAsync` now accepts `modelId?` and the verifier uses the same effective model as the agent (or `VerifierModel` from config if set). Previously hardcoded to `opts.Model`.

### `VerifierModel` Config
```csharp
public string? VerifierModel { get; init; }
```
Pin a specific (cheaper) model for verification, independent of the agent model. Falls back to the agent's effective model if null.

### Prompt Templates Removed
`PromptTemplateStore` and the `prompts/` directory were removed — they were never wired into `TenantAwarePromptBuilder` and had no runtime effect. Agent system prompts are stored in the database (set via Agent Builder in the admin portal) and augmented at runtime by `TenantAwarePromptBuilder` with business rules and session rules.

---

## Testing

```csharp
// Diva.Infrastructure.Tests/Verification/ResponseVerifierTests.cs

[Fact]
public async Task ToolGrounded_NoToolsCalled_FlagsFactualClaims()
{
    // Arrange: response with numbers, no tools used
    var result = await _verifier.VerifyAsync(
        "The Sensex is at 62,150 today.",
        toolsUsed: [],
        toolEvidence: "",
        ct: default);

    Assert.False(result.IsVerified);
    Assert.Equal("ToolGrounded", result.Mode);
    Assert.True(result.Confidence < 0.5f);
}

[Fact]
public async Task ToolGrounded_ToolsCalledWithData_PassesVerification()
{
    var result = await _verifier.VerifyAsync(
        "Revenue was $24,500 last month.",
        toolsUsed: ["GetMetricBreakdown"],
        toolEvidence: "[Tool: GetMetricBreakdown]\n{\"total\": 24500}",
        ct: default);

    Assert.True(result.IsVerified);
    Assert.True(result.Confidence >= 0.8f);
}
```
