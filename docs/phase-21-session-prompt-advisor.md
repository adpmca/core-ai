# Phase 21 — AI-Assisted Session Prompt Advisor

## Goal

After an agent session completes, analyze the full execution trace to identify prompt weaknesses,
suggest targeted system prompt improvements, and surface new rules worth adding permanently. Results
are reviewable and approvable by the admin before any change is committed. The advisor can also run
**automatically in the background** after sessions complete, storing findings in a persistent
**Prompt Advisor inbox** for asynchronous review.

---

## Concept

The advisor feeds the session trace (turns, ReAct iterations, tool call sequences, corrections,
verification failures) into an LLM acting as a prompt engineering expert. It returns structured
findings: what went wrong, why, and exactly what to change. The admin accepts or rejects each
suggestion. Accepted changes are saved via the existing prompt history pipeline
(`source: "session_analysis"`). Accepted rules flow into the PendingRules queue.

**Two modes:**
- **On-demand** — admin clicks "Analyze" on any session; result returned immediately (cached 30 min)
- **Background** — per-agent opt-in flag triggers automatic analysis after each session; results
  stored in `PromptAdvisorRunEntity` inbox; admin reviews asynchronously

---

## What the Analyzer Detects

The `SessionTraceDbContext` already captures everything needed:

| Signal | Entity / Field | Interpretation |
|--------|---------------|----------------|
| High iterations per turn | `TraceSessionTurnEntity.TotalIterations` | Agent uncertain — instructions ambiguous |
| Correction iterations | `TraceIterationEntity.IsCorrection` | Agent self-corrected — prompt underspecified |
| Continuation windows | `TurnSummary.ContinuationWindows` | Response scope too broad |
| Verification failures | `TurnSummary.VerificationPassed = false` | Output quality constraints missing |
| Tool call loops | Same tool name 3+ times in one iteration | No guidance on when to stop |
| Failed delegation | `TraceDelegationChainEntity.Status = "failed"` | Delegation rules ambiguous |
| High execution time | `TraceSessionTurnEntity.ExecutionTimeMs` | Broad instructions causing exploration |
| Follow-up re-prompting | User message restating the same request | Agent missed original intent |
| Learned rules in session | `LearnedRuleEntity.SourceSessionId` | Rules worth promoting to system prompt |

---

## Architecture

### New Service: `IPromptAdvisorService`

**File:** `src/Diva.TenantAdmin/Services/PromptAdvisorService.cs`

```csharp
public interface IPromptAdvisorService
{
    /// <summary>Analyze a single session trace and return improvement suggestions.</summary>
    Task<PromptAnalysisResult?> AnalyzeSessionAsync(
        string sessionId, string agentId, int tenantId, CancellationToken ct = default);

    /// <summary>Analyze up to 5 sessions and aggregate findings.</summary>
    Task<PromptAnalysisResult?> AnalyzeBatchAsync(
        string[] sessionIds, string agentId, int tenantId, CancellationToken ct = default);

    /// <summary>Apply accepted suggestions — saves prompt version and/or queues rules.</summary>
    Task<PromptAdvisorApplyResult> ApplyAcceptedSuggestionsAsync(
        string agentId, int tenantId, AcceptedSuggestionsDto accepted, string appliedBy,
        CancellationToken ct = default);
}
```

### LLM Seam: `ISessionAnalysisLlmCaller`

Extracted to enable unit testing without a live LLM:

```csharp
// src/Diva.TenantAdmin/Services/ISessionAnalysisLlmCaller.cs
internal interface ISessionAnalysisLlmCaller
{
    Task<string> CallAsync(string systemPrompt, string userPayload, CancellationToken ct);
}
```

Default implementations selected by `LlmOptions.Provider`:
- `AnthropicSessionAnalysisLlmCaller` — Anthropic SDK
- `OpenAiCompatSessionAnalysisLlmCaller` — OpenAI-compatible endpoint

Tests inject `FakeSessionAnalysisLlmCaller` returning canned JSON.

### Concurrency Guard

`PromptAdvisorService` holds a `ConcurrentDictionary<string, SemaphoreSlim>` keyed by
`$"{tenantId}:{agentId}"`. `ApplyAcceptedSuggestionsAsync` acquires the semaphore before calling
`SavePromptVersionAsync` to prevent the `MAX(Version)` race condition (same pattern as
`AgentSessionService`). Controller maps `DbUpdateException` (unique constraint) → HTTP 409.

### New DTOs: `src/Diva.Core/Models/PromptAdvisor/`

```csharp
// PromptAnalysisResult.cs
public record PromptAnalysisResult
{
    public string SessionId { get; init; }
    public string AgentId { get; init; }
    public SessionHealthSummary Health { get; init; }
    public IReadOnlyList<PromptIssue> Issues { get; init; }
    public IReadOnlyList<PromptSuggestion> Suggestions { get; init; }
    public IReadOnlyList<RuleSuggestion> NewRules { get; init; }
    public string? RevisedSystemPrompt { get; init; }   // full rewrite option
    public string Rationale { get; init; }
    // Scalability fields
    public bool IsTruncated { get; init; }              // payload was trimmed to fit context
    public int TurnsAnalyzed { get; init; }             // turns included in payload
    public int TurnsTotal { get; init; }                // total in session
    public string AnalyzedAt { get; init; }             // ISO timestamp
    public bool FromCache { get; init; }
}

// SessionHealthSummary.cs
public record SessionHealthSummary
{
    public int TotalTurns { get; init; }
    public double AvgIterationsPerTurn { get; init; }
    public int CorrectionIterations { get; init; }
    public int VerificationFailures { get; init; }
    public int ContinuationWindows { get; init; }
    public int ToolCallLoops { get; init; }
    public long TotalExecutionMs { get; init; }
    // Batch / background fields
    public int SessionCount { get; init; }              // 1 for single, N for batch
    public string[]? SessionIds { get; init; }
    public int HighSeverityIssues { get; init; }        // pre-computed for UI badge
}

// PromptIssue.cs
public record PromptIssue
{
    public string IssueType { get; init; }
    public string Severity { get; init; }    // "high" | "medium" | "low"
    public string Description { get; init; }
    public string Evidence { get; init; }    // trace excerpt
    public int[] AffectedTurns { get; init; }
}

// PromptSuggestion.cs
public record PromptSuggestion
{
    public string SuggestionId { get; init; }
    public string Type { get; init; }        // "add_section" | "replace_section" | "reword" | "add_constraint"
    public string? OriginalText { get; init; }
    public string SuggestedText { get; init; }
    public string Rationale { get; init; }
    public float Confidence { get; init; }
}

// RuleSuggestion.cs
public record RuleSuggestion
{
    public string RuleKey { get; init; }
    public string RuleCategory { get; init; }
    public string PromptInjection { get; init; }
    public string Rationale { get; init; }
    public float Confidence { get; init; }
    public string SourceEvidence { get; init; }
}

// AcceptedSuggestionsDto.cs
public record AcceptedSuggestionsDto
{
    public IReadOnlyList<string> AcceptedSuggestionIds { get; init; }
    public bool AcceptRevisedPrompt { get; init; }
    public IReadOnlyList<string> AcceptedRuleKeys { get; init; }
}

// PromptAdvisorApplyResult.cs
public record PromptAdvisorApplyResult
{
    public bool PromptUpdated { get; init; }
    public int? NewPromptVersion { get; init; }
    public int RulesQueued { get; init; }
}
```

---

## Scalability & Robustness

### Context Window Budget

`TraceToolCallEntity.ToolInput`/`ToolOutput` are unbounded — a large session can be 75+ MB.
The payload builder enforces `MAX_ADVISOR_CONTEXT_CHARS = 120_000` (~80K tokens with overhead):

- Truncate `ToolInput`/`ToolOutput` to **500 chars** each in the analysis payload (full text
  remains stored in the DB — this only affects what is sent to the LLM)
- Truncate `ThinkingText` to **300 chars** per iteration
- When the session still exceeds the budget after truncation: analyze only the **worst turns**
  (ranked by: `IsCorrection = true` + `VerificationPassed = false` + highest `TotalIterations`)
  until the char budget is exhausted
- Set `IsTruncated = true` and populate `TurnsAnalyzed` / `TurnsTotal` accordingly
- UI shows a yellow warning banner when `IsTruncated = true`

### Result Caching

`AnalyzeSessionAsync` caches results in `IMemoryCache` (already registered) for 30 minutes:

- Key: `$"prompt_advisor:{tenantId}:{agentId}:{sessionId}"`
- Cache is busted on `ApplyAcceptedSuggestionsAsync`
- `FromCache = true` in the result lets the UI show "Cached result from X min ago"

### Session Ownership Validation

`SessionTraceDbContext` has no EF query filters. After loading a session, validate:

```csharp
if (session.TenantId != tenantId)
    throw new UnauthorizedAccessException("Session does not belong to this tenant");
if (session.AgentId != agentId)
    return null; // 400 Bad Request from controller
```

### Deduplication on Apply

Before `SavePromptVersionAsync`, fetch the current prompt version and compare text. If identical,
skip the DB write and return the existing version number — prevents duplicate history on retry/double-click.

---

## LLM Analysis Payload

`PromptAdvisorService` builds this JSON and sends it with `prompts/agent-setup/prompt-advisor.txt`:

```json
{
  "agent": {
    "id": "agent-123",
    "name": "Support Bot",
    "currentSystemPrompt": "You are a support agent..."
  },
  "health": {
    "totalTurns": 5,
    "avgIterationsPerTurn": 4.8,
    "correctionIterations": 3,
    "verificationFailures": 1,
    "continuationWindows": 2,
    "toolCallLoops": 1
  },
  "turns": [
    {
      "turnNumber": 1,
      "userMessage": "...",
      "assistantMessage": "...",
      "iterations": [
        {
          "iterationNumber": 1,
          "thinkingText": "... (truncated to 300 chars)",
          "isCorrection": false,
          "toolCalls": [
            { "toolName": "search_kb",
              "input": "... (truncated to 500 chars)",
              "output": "... (truncated to 500 chars)" }
          ]
        }
      ],
      "hadCorrections": true,
      "executionTimeMs": 14200
    }
  ],
  "learnedRulesThisSession": [
    { "ruleKey": "always_check_order_status_first", "promptInjection": "..." }
  ]
}
```

---

## Apply Flow

1. **Full revised prompt** (`AcceptRevisedPrompt = true`):
   Calls `AgentSetupAssistant.SavePromptVersionAsync(source: "session_analysis", reason: "Session {sessionId} analysis")`

2. **Individual patches** (`AcceptRevisedPrompt = false`):
   Applies text patches to current prompt, saves as new version (after deduplication check).

3. **Accepted rules**:
   Calls `RuleLearningService.SaveLearnedRuleAsync(tenantId, rule, RuleApprovalMode.RequireAdmin)` for each.

---

## API Endpoints

**Controller:** `src/Diva.Host/Controllers/PromptAdvisorController.cs`

Auth: `EffectiveTenantId` pattern (same as `AdminController`) — regular users scoped to JWT tenant,
master admin can pass `tenantId` query param.

```
POST /api/prompt-advisor/analyze
  Body: { sessionId, agentId }
  Returns: PromptAnalysisResult  (cached 30 min)

POST /api/prompt-advisor/analyze-batch
  Body: { agentId, sessionIds: string[] }  (max 5)
  Returns: PromptAnalysisResult  (aggregated health + sampled worst turns)

POST /api/prompt-advisor/apply
  Body: { agentId, accepted: AcceptedSuggestionsDto }
  Returns: PromptAdvisorApplyResult
  Errors: 409 Conflict on concurrent write

GET  /api/prompt-advisor/runs
  Query: agentId?, status?, page, pageSize
  Returns: PagedResult<PromptAdvisorRunSummary>

GET  /api/prompt-advisor/runs/{runId}
  Returns: PromptAdvisorRunEntity with ResultJson deserialized

POST /api/prompt-advisor/runs/{runId}/dismiss
  Marks run "dismissed"

POST /api/prompt-advisor/runs/{runId}/apply
  Body: AcceptedSuggestionsDto
  Applies accepted items, marks run "applied"
```

---

## Prompt Template: `prompts/agent-setup/prompt-advisor.txt`

- You are a system prompt engineering expert. Analyze the session trace provided.
- Focus on: iteration inefficiency, self-correction patterns, tool misuse, ambiguous scope, missing constraints.
- For each issue, cite specific turn numbers and trace excerpts as evidence.
- Prefer surgical, targeted changes over full rewrites. Only suggest a full rewrite if > 3 distinct issues overlap.
- Surface any session-learned rules that belong permanently in the system prompt.
- Return JSON only, matching the PromptAnalysisResult schema exactly. No markdown fences.

---

## Background Job

### New Config: `PromptAdvisorOptions`

**File:** `src/Diva.Core/Configuration/PromptAdvisorOptions.cs`

```csharp
public sealed class PromptAdvisorOptions
{
    public const string SectionName = "PromptAdvisor";

    public bool EnableBackgroundAnalysis { get; set; } = false;
    public int PollIntervalSeconds { get; set; } = 60;
    public int MaxConcurrentJobs { get; set; } = 3;
    public int MinTurnsForAnalysis { get; set; } = 2;       // ignore trivial sessions
    public int MinCorrectionIterations { get; set; } = 1;   // only sessions with issues
    public int RunRetentionDays { get; set; } = 30;
}
```

Register in `Program.cs`:
```csharp
builder.Services.Configure<PromptAdvisorOptions>(
    builder.Configuration.GetSection(PromptAdvisorOptions.SectionName));
```

### New DB Entity: `PromptAdvisorRunEntity`

**File:** `src/Diva.Infrastructure/Data/Entities/PromptAdvisorRunEntity.cs`

```csharp
public sealed class PromptAdvisorRunEntity : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string AgentId { get; set; } = string.Empty;
    public string AgentName { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string[]? BatchSessionIds { get; set; }           // null for single-session
    public string Source { get; set; } = "manual";           // "manual" | "background"
    public string Status { get; set; } = "pending";          // "pending" | "analyzing" | "complete" | "failed" | "applied" | "dismissed"
    public string? ResultJson { get; set; }                  // serialized PromptAnalysisResult
    public string? ErrorMessage { get; set; }
    public int SuggestionsCount { get; set; }
    public int HighSeverityCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? ActedAt { get; set; }
    public string? ActedBy { get; set; }
}
```

Register in `DivaDbContext`:
```csharp
public DbSet<PromptAdvisorRunEntity> PromptAdvisorRuns => Set<PromptAdvisorRunEntity>();

// OnModelCreating:
modelBuilder.Entity<PromptAdvisorRunEntity>()
    .HasQueryFilter(e => _currentTenantId == 0 || e.TenantId == _currentTenantId);
modelBuilder.Entity<PromptAdvisorRunEntity>()
    .HasIndex(e => new { e.TenantId, e.Status, e.CreatedAt });
modelBuilder.Entity<PromptAdvisorRunEntity>()
    .HasIndex(e => new { e.TenantId, e.AgentId, e.SessionId });
```

**EF migration required:** new `PromptAdvisorRuns` table.

### Per-Agent Opt-In Flag

Add to `AgentDefinitionEntity`:
```csharp
public bool PromptAdvisorEnabled { get; set; } = false;
```

Exposed in `AgentBuilder.tsx` as a toggle: "Auto-analyze sessions".
EF migration: nullable column with `DEFAULT 0`.

### Trigger Hook: `PromptAdvisorTriggerHook : IOnAfterResponseHook`

**File:** `src/Diva.Infrastructure/Learning/PromptAdvisorTriggerHook.cs`

Fires after verification, before return to caller (`IOnAfterResponseHook` — last hook in pipeline).

```csharp
public sealed class PromptAdvisorTriggerHook : IOnAfterResponseHook
{
    public async Task OnAfterResponseAsync(AgentHookContext ctx, AgentResponse response, CancellationToken ct)
    {
        if (!_opts.EnableBackgroundAnalysis) return;

        using var scope = _sp.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDatabaseProviderFactory>();
        using var db = factory.CreateDbContext(TenantContext.System(ctx.Tenant.TenantId));

        var agent = await db.AgentDefinitions.FindAsync([ctx.AgentId], ct);
        if (agent is null || !agent.PromptAdvisorEnabled) return;

        await _runService.QueueRunAsync(
            ctx.Tenant.TenantId, ctx.AgentId, agent.Name, ctx.SessionId!, "background", ct);
    }
}
```

Register in `Program.cs`:
```csharp
builder.Services.AddSingleton<IAgentLifecycleHook, PromptAdvisorTriggerHook>();
```

### Run Service: `IPromptAdvisorRunService`

**File:** `src/Diva.TenantAdmin/Services/PromptAdvisorRunService.cs`

```csharp
public interface IPromptAdvisorRunService
{
    Task<int> QueueRunAsync(int tenantId, string agentId, string agentName,
        string sessionId, string source, CancellationToken ct);

    Task<PagedResult<PromptAdvisorRunSummary>> GetRunsAsync(
        int tenantId, string? agentId, string? status, int page, int pageSize, CancellationToken ct);

    Task<PromptAdvisorRunEntity?> GetRunAsync(int tenantId, int runId, CancellationToken ct);
    Task DismissRunAsync(int tenantId, int runId, CancellationToken ct);
    Task MarkAppliedAsync(int tenantId, int runId, string appliedBy, CancellationToken ct);
    Task<int> GetPendingCountAsync(int tenantId, CancellationToken ct);   // for sidebar badge
}
```

### Background Service: `PromptAdvisorHostedService`

**File:** `src/Diva.Infrastructure/Learning/PromptAdvisorHostedService.cs`

Pattern matches `AgentTaskCleanupService` exactly:

```
ExecuteAsync():
  await Task.Yield()
  while not cancelled:
    await Task.Delay(PollIntervalSeconds * 1000, ct)
    try: await ProcessPendingRunsAsync(ct)
    catch OperationCanceledException: break
    catch ex: log warning, continue

ProcessPendingRunsAsync():
  load up to MaxConcurrentJobs "pending" runs from DivaDbContext (system context)
  skip any run whose AgentId is already in _inFlight ConcurrentDictionary
  for each run:
    mark Status = "analyzing", StartedAt = UtcNow, save
    _inFlight[run.AgentId] = run.Id
    Task.Run(() => RunAnalysisAsync(run, CancellationToken.None))  ← fire-and-forget

RunAnalysisAsync(run):
  try:
    result = await _advisorService.AnalyzeSessionAsync(run.SessionId, run.AgentId, run.TenantId, ct)
    update run: Status = "complete", ResultJson = serialize(result),
                SuggestionsCount = result.Suggestions.Count,
                HighSeverityCount = result.Health.HighSeverityIssues,
                CompletedAt = UtcNow
    push SignalR: AgentStreamHub.PushChunkAsync(hub, tenantId.ToString(),
        new AgentStreamChunk { Type = "advisor_run_complete",
          Data = { runId, agentId, agentName, suggestionCount, highSeverityCount } })
  catch ex:
    update run: Status = "failed", ErrorMessage = ex.Message
  finally:
    _inFlight.TryRemove(run.AgentId, ...)
```

Register in `Program.cs`:
```csharp
builder.Services.AddHostedService<PromptAdvisorHostedService>();
```

---

## Frontend

### On-Demand View: `admin-portal/src/components/PromptAdvisor.tsx`

Full-page at `/sessions/:id/advisor` and `/prompt-advisor/runs/:runId`.

Entry points:
- Session Detail page → "Analyze for Prompt Improvements" button
- Inbox list → "Review →" link (loads from stored `ResultJson`, no new LLM call)
- Agent Builder → "Analyze Recent Sessions" button (batch path, last 5 completed)

```
┌─────────────────────────────────────────────────────────┐
│ Session Health                                           │
│  5 turns · 4.8 avg iterations · 3 corrections ⚠        │
│  ⚠ Analysis truncated to 3 of 5 turns (context limit)   │
├─────────────────────────────────────────────────────────┤
│ Issues Found                                            │
│  [HIGH]  Ambiguous scope — turns 2, 4                   │
│  [MED]   Tool call loop in turn 3                       │
├─────────────────────────────────────────────────────────┤
│ Prompt Suggestions                          [Accept All] │
│  ☑ Add tool usage constraint                            │
│  ☐ Reword goal statement                               │
│  ☑ Add output format section                           │
├─────────────────────────────────────────────────────────┤
│ New Rules to Add                                        │
│  ☑ always_check_order_status_first  (0.91)              │
├─────────────────────────────────────────────────────────┤
│ Full Revised Prompt Preview             [Show Diff ↕]   │
├─────────────────────────────────────────────────────────┤
│              [Apply Selected]   [Discard All]           │
└─────────────────────────────────────────────────────────┘
```

### Inbox: `admin-portal/src/components/PromptAdvisorHistory.tsx`

Route: `/prompt-advisor` — add to sidebar with pending-count badge.

```
┌────────────────────────────────────────────────────────────────┐
│ Prompt Advisor                   [Filter: All Agents] [Status] │
├────────────────────────────────────────────────────────────────┤
│ Support Bot   Session 3fa8… · 2h ago                           │
│ 3 suggestions · 1 HIGH · background                [Review →]  │
├────────────────────────────────────────────────────────────────┤
│ Code Analyst  Session 8bc1… · 5h ago                           │
│ 2 suggestions · 0 HIGH · manual                    [Review →]  │
├────────────────────────────────────────────────────────────────┤
│ HR Bot        Session d291… · 1d ago · Applied by admin        │
│                                                    [Dismissed] │
└────────────────────────────────────────────────────────────────┘
```

### SignalR Notification

When `"advisor_run_complete"` chunk arrives on the existing `/hubs/agent` connection:
```
toast.info(`${agentName} — ${count} new prompt suggestions available`, {
  action: { label: "Review", onClick: () => navigate(`/prompt-advisor/runs/${runId}`) }
})
```

---

## Reused Infrastructure

| Need | Existing Component |
|------|--------------------|
| Fetch session trace | `SessionTraceDbContext` — all four entity types |
| LLM calling | Pattern from `LlmRuleExtractor` + `AgentSetupAssistant` |
| Save prompt version | `AgentSetupAssistant.SavePromptVersionAsync()` |
| Save rules | `RuleLearningService.SaveLearnedRuleAsync()` → PendingRules queue |
| Prompt history + restore | Already in Agent Builder |
| Truncation recovery | `TryExtractTruncatedStringField` in `AgentSetupAssistant.cs` |
| Result caching | `IMemoryCache` (already registered in `Program.cs`) |
| Background service pattern | `AgentTaskCleanupService` / `TraceCleanupService` |
| SignalR push | `AgentStreamHub.PushChunkAsync()` |
| Concurrency guard | `SemaphoreSlim` pattern (same as `AgentSessionService`) |

---

## New Files

| File | Action |
|------|--------|
| `src/Diva.Core/Models/PromptAdvisor/PromptAnalysisResult.cs` | Create |
| `src/Diva.Core/Models/PromptAdvisor/SessionHealthSummary.cs` | Create |
| `src/Diva.Core/Models/PromptAdvisor/PromptIssue.cs` | Create |
| `src/Diva.Core/Models/PromptAdvisor/PromptSuggestion.cs` | Create |
| `src/Diva.Core/Models/PromptAdvisor/RuleSuggestion.cs` | Create |
| `src/Diva.Core/Models/PromptAdvisor/AcceptedSuggestionsDto.cs` | Create |
| `src/Diva.Core/Models/PromptAdvisor/PromptAdvisorApplyResult.cs` | Create |
| `src/Diva.Core/Configuration/PromptAdvisorOptions.cs` | Create |
| `src/Diva.TenantAdmin/Services/IPromptAdvisorService.cs` | Create |
| `src/Diva.TenantAdmin/Services/PromptAdvisorService.cs` | Create |
| `src/Diva.TenantAdmin/Services/ISessionAnalysisLlmCaller.cs` | Create |
| `src/Diva.TenantAdmin/Services/IPromptAdvisorRunService.cs` | Create |
| `src/Diva.TenantAdmin/Services/PromptAdvisorRunService.cs` | Create |
| `src/Diva.Infrastructure/Data/Entities/PromptAdvisorRunEntity.cs` | Create |
| `src/Diva.Infrastructure/Learning/PromptAdvisorTriggerHook.cs` | Create |
| `src/Diva.Infrastructure/Learning/PromptAdvisorHostedService.cs` | Create |
| `src/Diva.Host/Controllers/PromptAdvisorController.cs` | Create |
| `prompts/agent-setup/prompt-advisor.txt` | Create |
| `admin-portal/src/components/PromptAdvisor.tsx` | Create |
| `admin-portal/src/components/PromptAdvisorHistory.tsx` | Create |
| `tests/Diva.TenantAdmin.Tests/PromptAdvisorServiceTests.cs` | Create |
| `tests/Diva.TenantAdmin.Tests/PromptAdvisorHostedServiceTests.cs` | Create |

**Modified files:**

| File | Change |
|------|--------|
| `src/Diva.Infrastructure/Data/DivaDbContext.cs` | Add `PromptAdvisorRuns` DbSet + model config |
| `src/Diva.Infrastructure/Data/Entities/AgentDefinitionEntity.cs` | Add `PromptAdvisorEnabled` bool |
| `src/Diva.Host/Program.cs` | Register options, services, hook, hosted service |
| `admin-portal/src/api.ts` | Add `analyzeSession()`, `analyzeBatch()`, `applyAdvisorSuggestions()`, `getAdvisorRuns()`, `getAdvisorRun()`, `dismissAdvisorRun()`, `applyAdvisorRun()` |
| `admin-portal/src/App.tsx` | Add routes + `"advisor_run_complete"` SignalR handler |
| `admin-portal/src/components/layout/app-sidebar.tsx` | Add "Prompt Advisor" nav item with pending badge |
| `admin-portal/src/components/SessionDetail.tsx` | Add "Analyze" button |
| `admin-portal/src/components/AgentBuilder.tsx` | Add "Auto-analyze sessions" toggle + "Analyze Recent" button |
| EF migration | `PromptAdvisorRuns` table + `AgentDefinitions.PromptAdvisorEnabled` column |

---

## Test Coverage

| Test | Covers |
|------|--------|
| `AnalyzeSession_TruncatesLargePayload` | Char budget; `IsTruncated = true`; worst-turns selection |
| `AnalyzeSession_ReturnsNull_WhenNotFound` | Graceful null from trace DB |
| `AnalyzeSession_Throws_WhenTenantMismatch` | Cross-tenant isolation |
| `AnalyzeSession_ReturnsCachedResult` | IMemoryCache hit; `FromCache = true` |
| `ApplyAccepted_SkipsSave_WhenPromptUnchanged` | Deduplication guard |
| `ApplyAccepted_QueuesPendingRules` | Rule queuing wired correctly |
| `ApplyAccepted_Returns409_OnConcurrentWrite` | SemaphoreSlim + constraint handling |
| `AnalyzeBatch_AggregatesHealthAcrossSessions` | Multi-session aggregation |
| `BackgroundJob_QueuesOnlyEnabledAgents` | Auto-trigger filter on `PromptAdvisorEnabled` |
| `BackgroundJob_MarksRunFailed_OnLlmError` | Job failure path; status = "failed" |
| `BackgroundJob_PushesSignalR_OnComplete` | `AgentStreamHub.PushChunkAsync` called |
| `TriggerHook_SkipsQueue_WhenBelowMinThreshold` | `MinTurnsForAnalysis` + `MinCorrectionIterations` |

---

## Dependencies

- Phase 7 (Session Management) ✓ — session data exists
- Phase 11 (Rule Learning) ✓ — `RuleLearningService` and PendingRules pipeline exist
- Phase 17 (Agent Setup Assistant) ✓ — `SavePromptVersionAsync` and prompt history exist
- Phase 15 (Custom Agents / Lifecycle Hooks) ✓ — `IOnAfterResponseHook` is wired
- Independent of Phase 19 (Coordinator) and Phase 20 (OSS Release)

---

## Verification

1. Run agent session with vague prompt → correction iterations occur
2. Agent has `PromptAdvisorEnabled = true` → `PromptAdvisorTriggerHook` queues a `PromptAdvisorRun`
3. Within 60s → `PromptAdvisorHostedService` processes the run → status becomes "complete"
4. SignalR `"advisor_run_complete"` arrives → toast notification appears in the portal
5. Navigate to `/prompt-advisor` → run appears in inbox with suggestion count and HIGH badge
6. Click "Review →" → `PromptAdvisor.tsx` loads from stored `ResultJson` (no new LLM call)
7. Accept one suggestion + one rule → `POST /api/prompt-advisor/runs/:id/apply`
   - Agent's prompt history shows new version with `source: "session_analysis"`
   - Rule appears in PendingRules page
   - Run status → "applied"; sidebar badge decrements
8. Manually trigger via Session Detail "Analyze" → response returns from cache on second call
9. Restore the previous prompt version via Agent Builder timeline — confirm prompt rollback works
10. Run `dotnet test tests/Diva.TenantAdmin.Tests` → all 12 prompt advisor tests pass
