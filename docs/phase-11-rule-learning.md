# Phase 11: Dynamic Business Rule Learning

> **Status:** `[ ]` Not Started
> **Tier:** 1 — Core Agentic (implement before tenant integration)
> **Depends on:** [phase-08-agents.md](phase-08-agents.md)
> **Note:** `AdminController` approve/reject endpoints (Phase 10) are a Tier 2 concern — the core learning pipeline (extraction, session rules, DB persistence) can be fully implemented without it.
> **LlmRuleExtractor dependency:** Originally planned to use `LiteLLMClient` (not yet built). Use `AnthropicAgentRunner` directly instead — it already handles both Anthropic and OpenAI-compatible providers and is available now.
> **Project:** `Diva.Infrastructure/Learning` + updates to `Diva.Agents`

---

## Goal

Enable agents to detect when users define or correct business rules during conversation, suggest saving them, and support three approval modes: auto-save, admin review, or session-only (temporary).

---

## Learning Pipeline

```
User says: "Actually, for us revenue should exclude refunds"
              │
              ▼
    LlmRuleExtractor detects rule pattern
    → Extracts: { category: "reporting", key: "revenue_exclusions",
                  promptInjection: "Exclude REFUNDS from all revenue calculations" }
              │
              ▼
    Agent asks user:
    "I noticed a business rule. Save it?"
    Options: [Yes, save] [Just this session] [No, ignore]
              │
         ┌────┴───────────────────────┐
         │                            │
      "Yes, save"              "Just this session"
         │                            │
         ▼                            ▼
  LearnedRules table          SessionRuleManager
  status = "pending"          (IDistributedCache, 24h TTL)
         │
         ▼
  Admin reviews in
  Admin Portal → Approve/Reject/Edit
```

---

## Files to Create

```
src/Diva.Infrastructure/Learning/
├── IRuleLearningService.cs
├── RuleLearningService.cs
├── LlmRuleExtractor.cs
├── SessionRuleManager.cs
├── FeedbackLearningService.cs
└── PatternRuleDiscovery.cs
```

**Updates to:**
- `Diva.TenantAdmin/Prompts/TenantAwarePromptBuilder.cs` — inject session rules
- `Diva.Agents/Workers/BaseReActAgent.cs` — call rule extractor after each response
- `Diva.Host/Controllers/AdminController.cs` — approve/reject endpoints

---

## IRuleLearningService.cs

```csharp
namespace Diva.Infrastructure.Learning;

public interface IRuleLearningService
{
    Task<List<SuggestedRule>> ExtractRulesFromConversationAsync(
        string sessionId, string conversationTranscript, CancellationToken ct);

    Task<RuleSaveResult> SaveLearnedRuleAsync(
        int tenantId, SuggestedRule rule, RuleApprovalMode mode, CancellationToken ct);

    Task<List<SuggestedRule>> GetPendingRulesAsync(int tenantId, CancellationToken ct);

    Task ApproveRuleAsync(int tenantId, int ruleId, string reviewedBy, CancellationToken ct);
    Task RejectRuleAsync(int tenantId, int ruleId, string reviewedBy, string notes, CancellationToken ct);
}

public sealed class SuggestedRule
{
    public string? AgentType { get; init; }
    public string RuleCategory { get; init; } = "";
    public string RuleKey { get; init; } = "";
    public string? RuleValueJson { get; init; }
    public string PromptInjection { get; init; } = "";
    public string SourceSessionId { get; init; } = "";
    public float Confidence { get; init; }       // 0.0 - 1.0
    public DateTime SuggestedAt { get; init; } = DateTime.UtcNow;
}

public enum RuleApprovalMode { AutoApprove, RequireAdmin, SessionOnly }

public sealed class RuleSaveResult
{
    public bool Success { get; init; }
    public RuleApprovalMode Mode { get; init; }
    public int? RuleId { get; init; }   // null for SessionOnly
    public string Message { get; init; } = "";
}
```

---

## LlmRuleExtractor.cs

```csharp
namespace Diva.Infrastructure.Learning;

public class LlmRuleExtractor
{
    private readonly LiteLLMClient _llm;

    public async Task<List<SuggestedRule>> ExtractAsync(
        string conversationTranscript,
        string sessionId,
        CancellationToken ct)
    {
        var prompt = $"""
            Analyze this conversation and identify any business rules the user is defining or correcting.

            Look for patterns like:
            - "For us, X should be Y"
            - "Actually, we don't count X in Y"
            - "Our policy is X"
            - "We always want to see X when Y"
            - Corrections to agent behavior
            - Clarifications about business logic

            ## Conversation
            {conversationTranscript}

            ## Output
            Return a JSON array of extracted rules. Each rule has:
            - "agentType": which agent type this applies to ("Analytics", "Reservation", "*")
            - "ruleCategory": category ("reporting", "booking", "pricing", "terminology", "other")
            - "ruleKey": short identifier (snake_case)
            - "promptInjection": the text to inject into agent prompts
            - "confidence": 0.0-1.0 how confident you are this is a rule

            Return [] if no rules found. Return only valid JSON, no explanation.
            """;

        var response = await _llm.GenerateAsync(prompt, new TenantContext(), ct);

        try
        {
            var rules = JsonSerializer.Deserialize<List<ExtractedRuleDto>>(response) ?? [];
            return rules.Select(r => new SuggestedRule
            {
                AgentType       = r.AgentType,
                RuleCategory    = r.RuleCategory,
                RuleKey         = r.RuleKey,
                PromptInjection = r.PromptInjection,
                Confidence      = r.Confidence,
                SourceSessionId = sessionId
            }).ToList();
        }
        catch (JsonException)
        {
            return [];  // LLM returned non-JSON, ignore
        }
    }

    private record ExtractedRuleDto(
        string? AgentType,
        string RuleCategory,
        string RuleKey,
        string PromptInjection,
        float Confidence);
}
```

---

## SessionRuleManager.cs

Session-scoped rules using distributed cache (in-memory default, Redis for production):

```csharp
namespace Diva.Infrastructure.Learning;

public class SessionRuleManager : ISessionRuleManager
{
    private readonly IDistributedCache _cache;
    private static readonly TimeSpan SessionRuleTtl = TimeSpan.FromHours(24);

    private static string CacheKey(string sessionId) => $"session_rules:{sessionId}";

    public async Task AddRuleAsync(string sessionId, SuggestedRule rule, CancellationToken ct)
    {
        var rules = await GetSessionRulesAsync(sessionId, ct);
        rules.Add(rule);
        await _cache.SetAsync(
            CacheKey(sessionId),
            JsonSerializer.SerializeToUtf8Bytes(rules),
            new DistributedCacheEntryOptions { SlidingExpiration = SessionRuleTtl },
            ct);
    }

    public async Task<List<SuggestedRule>> GetSessionRulesAsync(string sessionId, CancellationToken ct)
    {
        var data = await _cache.GetAsync(CacheKey(sessionId), ct);
        return data == null
            ? []
            : JsonSerializer.Deserialize<List<SuggestedRule>>(data) ?? [];
    }

    public async Task ClearSessionRulesAsync(string sessionId, CancellationToken ct)
        => await _cache.RemoveAsync(CacheKey(sessionId), ct);
}
```

---

## RuleLearningService.cs

```csharp
namespace Diva.Infrastructure.Learning;

public class RuleLearningService : IRuleLearningService
{
    private readonly DivaDbContext _db;
    private readonly LlmRuleExtractor _extractor;
    private readonly ISessionRuleManager _sessionManager;

    public async Task<List<SuggestedRule>> ExtractRulesFromConversationAsync(
        string sessionId, string transcript, CancellationToken ct)
        => await _extractor.ExtractAsync(transcript, sessionId, ct);

    public async Task<RuleSaveResult> SaveLearnedRuleAsync(
        int tenantId, SuggestedRule rule, RuleApprovalMode mode, CancellationToken ct)
    {
        if (mode == RuleApprovalMode.SessionOnly)
        {
            await _sessionManager.AddRuleAsync(rule.SourceSessionId, rule, ct);
            return new RuleSaveResult { Success = true, Mode = mode, Message = "Applied to current session" };
        }

        var entity = new LearnedRuleEntity
        {
            TenantId        = tenantId,
            AgentType       = rule.AgentType,
            RuleCategory    = rule.RuleCategory,
            RuleKey         = rule.RuleKey,
            PromptInjection = rule.PromptInjection,
            Confidence      = rule.Confidence,
            Status          = mode == RuleApprovalMode.AutoApprove ? "approved" : "pending",
            SourceSessionId = rule.SourceSessionId
        };

        _db.LearnedRules.Add(entity);
        await _db.SaveChangesAsync(ct);

        if (mode == RuleApprovalMode.AutoApprove)
        {
            // Promote to active business rule immediately
            await PromoteToBusinessRuleAsync(tenantId, entity, ct);
        }

        return new RuleSaveResult { Success = true, Mode = mode, RuleId = entity.Id };
    }

    private async Task PromoteToBusinessRuleAsync(int tenantId, LearnedRuleEntity entity, CancellationToken ct)
    {
        _db.BusinessRules.Add(new TenantBusinessRuleEntity
        {
            TenantId        = tenantId,
            AgentType       = entity.AgentType ?? "*",
            RuleCategory    = entity.RuleCategory ?? "learned",
            RuleKey         = entity.RuleKey ?? $"learned_{entity.Id}",
            PromptInjection = entity.PromptInjection,
            IsActive        = true
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<SuggestedRule>> GetPendingRulesAsync(int tenantId, CancellationToken ct)
    {
        return await _db.LearnedRules
            .Where(r => r.TenantId == tenantId && r.Status == "pending")
            .OrderByDescending(r => r.LearnedAt)
            .Select(r => new SuggestedRule
            {
                AgentType       = r.AgentType,
                RuleCategory    = r.RuleCategory ?? "",
                RuleKey         = r.RuleKey ?? "",
                PromptInjection = r.PromptInjection ?? "",
                Confidence      = (float)r.Confidence,
                SourceSessionId = r.SourceSessionId ?? ""
            })
            .ToListAsync(ct);
    }
}
```

---

## FeedbackLearningService.cs

Learns from user corrections to agent responses:

```csharp
namespace Diva.Infrastructure.Learning;

public class FeedbackLearningService
{
    private readonly LiteLLMClient _llm;
    private readonly IRuleLearningService _learning;

    public async Task ProcessCorrectionAsync(
        int tenantId, string sessionId,
        string originalResponse, string userCorrection, CancellationToken ct)
    {
        var analysis = await _llm.GenerateAsync($"""
            The AI agent gave this response:
            {originalResponse}

            The user corrected it with:
            {userCorrection}

            Analyze what business rule or preference this implies.
            Return JSON:
            {{
              "rule_type": "correction|preference|policy|none",
              "confidence": 0.0-1.0,
              "prompt_injection": "text to add to future prompts to prevent this error"
            }}
            """, new TenantContext(), ct);

        var parsed = JsonSerializer.Deserialize<CorrectionAnalysis>(analysis);

        if (parsed?.Confidence > 0.7 && parsed.RuleType != "none")
        {
            await _learning.SaveLearnedRuleAsync(tenantId, new SuggestedRule
            {
                RuleCategory    = "learned_from_correction",
                RuleKey         = $"correction_{DateTime.UtcNow:yyyyMMddHHmmss}",
                PromptInjection = parsed.PromptInjection,
                Confidence      = parsed.Confidence,
                SourceSessionId = sessionId
            }, RuleApprovalMode.RequireAdmin, ct);
        }
    }

    private record CorrectionAnalysis(string RuleType, float Confidence, string PromptInjection);
}
```

---

## Agent Integration (BaseReActAgent update)

```csharp
// Add to BaseReActAgent.ExecuteAsync after generating response
protected async Task<AgentResponse> PostProcessAsync(
    AgentResponse response,
    string sessionId,
    string conversationTranscript,
    TenantContext tenant,
    CancellationToken ct)
{
    var suggestedRules = await _learning.ExtractRulesFromConversationAsync(
        sessionId, conversationTranscript, ct);

    foreach (var rule in suggestedRules.Where(r => r.Confidence > 0.8))
    {
        response.FollowUpQuestions.Add(new FollowUpQuestion
        {
            Type    = "rule_confirmation",
            Text    = $"I noticed a business rule: \"{rule.PromptInjection}\". Save it?",
            Options = ["Yes, save it", "Just this session", "No, ignore"],
            Metadata = rule
        });
    }

    return response;
}
```

---

## Admin Portal — PendingRules.tsx

```typescript
// admin-portal/src/pages/PendingRules.tsx
export function PendingRulesPage() {
  const { tenantId } = useAuth();
  const { data: pendingRules } = useQuery(['pendingRules', tenantId],
    () => api.getPendingRules(tenantId));
  const approveMutation = useMutation((ruleId) => api.approveRule(tenantId, ruleId));
  const rejectMutation  = useMutation((ruleId) => api.rejectRule(tenantId, ruleId));

  return (
    <div>
      <h1>Learned Rules Pending Approval</h1>
      {pendingRules?.map(rule => (
        <RuleCard key={rule.id}>
          <Badge>{rule.ruleCategory}</Badge>
          <Confidence value={rule.confidence} />
          <Code>{rule.promptInjection}</Code>
          <ConversationLink sessionId={rule.sourceConversation} />
          <Button onClick={() => approveMutation.mutate(rule.id)}>Approve</Button>
          <Button onClick={() => rejectMutation.mutate(rule.id)}>Reject</Button>
        </RuleCard>
      ))}
    </div>
  );
}
```

---

## Service Registration

```csharp
builder.Services.AddScoped<IRuleLearningService, RuleLearningService>();
builder.Services.AddScoped<LlmRuleExtractor>();
builder.Services.AddScoped<ISessionRuleManager, SessionRuleManager>();
builder.Services.AddScoped<FeedbackLearningService>();

// For session-only rules: distributed cache (in-memory default, Redis for production)
builder.Services.AddDistributedMemoryCache();
// Production: builder.Services.AddStackExchangeRedisCache(...)
```

---

## Verification

- [ ] User says "we exclude refunds from revenue" → agent detects and asks to save
- [ ] "Just this session" → rule appears in next agent call within same session, not persisted
- [ ] "Yes, save it" (auto-approve) → rule immediately appears in `TenantBusinessRules` table
- [ ] "Yes, save it" (require-admin mode) → rule appears in `LearnedRules` with status `pending`
- [ ] Admin portal: `GET /api/admin/learned-rules/{tenantId}` returns pending rules
- [ ] Admin approves → `status` = `approved` + promoted to `TenantBusinessRules`
- [ ] Next session after approval: rule is included in agent system prompt
