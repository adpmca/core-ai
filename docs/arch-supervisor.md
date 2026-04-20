# Architecture: Supervisor Pipeline & ReAct Loop

> **Status:** Reference — no code to write here
> **Related phase:** [phase-08-agents.md](phase-08-agents.md)

---

## ReAct Agent Loop (SK Implementation)

SK's `ChatCompletionAgent` with `FunctionChoiceBehavior.Auto` implements the ReAct pattern natively:

```
User message
     │
     ▼
┌─────────────────────────────────────────┐
│  THINK  (LLM reasons about what to do) │
│  → Decides which tool to call           │
└───────────────────┬─────────────────────┘
                    │
                    ▼
┌─────────────────────────────────────────┐
│  ACT    (SK auto-invokes the tool)     │
│  → MCP tool called with tenant headers  │
└───────────────────┬─────────────────────┘
                    │
                    ▼
┌─────────────────────────────────────────┐
│  OBSERVE  (tool result fed back to LLM) │
│  → LLM decides: done or next iteration? │
└───────────────────┬─────────────────────┘
                    │
          ┌─────────┴──────────┐
          │ more tools needed? │
         YES                  NO
          │                    │
          └──→ THINK again     └──→ Final response
```

### Example Execution Trace

```
User: "What was cart revenue for South Campus last month?"

ITERATION 1:
  THINK: Need cart revenue data for South Campus, last month
  ACT:   GetMetricBreakdown(channel="CART_RENTAL", location="South Campus", period="last_month")
  OBSERVE: {total: 24500, transactions: 1250, avg_per_round: 19.60}

ITERATION 2:
  THINK: I have the data. Get YoY for context.
  ACT:   GetYoY(metric="cart_revenue", location="South Campus")
  OBSERVE: {current: 24500, previous: 22700, change_pct: 7.9}

ITERATION 3:
  THINK: All data available. Synthesize response.
  ACT:   [No tool call — generate final answer]
  OUTPUT: "Cart rental revenue for South Campus last month was $24,500
           across 1,250 transactions (avg $19.60/round).
           This is 7.9% higher than the same period last year."

[DONE] — SK terminates loop
```

---

## Supervisor Pipeline Stages

The Supervisor orchestrates multiple worker agents through a sequential pipeline:

```
User Request
     │
     ▼
┌──────────────┐
│  DECOMPOSE   │ — LLM breaks request into sub-tasks
│              │   (e.g., "get analytics" + "check availability")
└──────┬───────┘
       │
       ▼
┌──────────────┐
│  CAPABILITY  │ — Match sub-tasks to registered agents
│   MATCH      │   via AgentCapability[] from DynamicAgentRegistry
└──────┬───────┘
       │
       ▼
┌──────────────┐
│   DISPATCH   │ — SK AgentGroupChat executes selected agents
│              │   in parallel or sequence
│              │   Accumulates ToolEvidence from each worker
└──────┬───────┘
       │
       ▼
┌──────────────┐
│   MONITOR    │ — Track agent progress, handle failures
│              │   Re-route to different agent if needed
└──────┬───────┘
       │
       ▼
┌──────────────┐
│  INTEGRATE   │ — Combine all worker results into coherent response
└──────┬───────┘
       │
       ▼
┌──────────────┐
│    VERIFY    │ — Cross-check response against ToolEvidence
│              │   Flag ungrounded claims, score confidence
│              │   Strict mode: block low-confidence responses
│              │   See: arch-response-verification.md
└──────┬───────┘
       │
       ▼
┌──────────────┐
│   DELIVER    │ — Send via Email | SignalR Dashboard | API | Slack
└──────────────┘
```

### Implementation Strategy

Sequential stages use `ISupervisorPipelineStage<TState>`:
```csharp
public interface ISupervisorPipelineStage<TState>
{
    Task<TState> ExecuteAsync(TState state, CancellationToken ct);
}
```

The Dispatch stage uses SK's `AgentGroupChat` for actual multi-agent coordination:
```csharp
var chat = new AgentGroupChat(selectedAgents)
{
    ExecutionSettings = new AgentGroupChatSettings
    {
        SelectionStrategy  = new CapabilityBasedSelector(_registry),
        TerminationStrategy = new TaskCompletionTerminationStrategy { MaximumIterations = 10 }
    }
};
await foreach (var msg in chat.InvokeAsync(ct)) { ... }
```

---

## Dynamic Agent Registration

Agents can be registered statically (code) or dynamically (DB at runtime):

```
┌─────────────────────────────────────────────────┐
│              AGENT REGISTRY                      │
│                                                  │
│  Static (code):                                  │
│  ├── AnalyticsAgent   capabilities: [analytics] │
│  ├── ReservationAgent     capabilities: [Reservation]   │
│  └── (future)                                    │
│                                                  │
│  Dynamic (from DB — AgentDefinitions table):     │
│  ├── Any agent defined in admin portal           │
│  └── Hot-reloaded without restart                │
└─────────────────────────────────────────────────┘
```

### Capability-Based Routing

```csharp
// AgentSelector matches task description to agent capabilities
public async Task<IWorkerAgent?> FindBestMatchAsync(string taskDescription, int tenantId)
{
    var agents = await _registry.GetAgentsForTenantAsync(tenantId);
    // Use LLM or keyword matching to find capability match
    // Priority: higher Priority value wins on tie
}
```

---

## Delivery Channels

After integration, results are delivered via:

| Channel | Implementation | When used |
|---------|---------------|-----------|
| API Response | `HttpContext.Response` (JSON) | Synchronous user requests |
| SSE Stream | `text/event-stream` | Real-time streaming to UI |
| SignalR | `IHubContext<AgentStreamHub>` | Dashboard push notifications |
| Email | SendGrid / SMTP | Scheduled reports, snapshots |
| Slack/Teams | Bot webhooks | Phase 2 (future) |

---

## Trigger Sources

```
SCHEDULED (Temporal/Airflow)    → Daily snapshots, weekly reports
EVENT-DRIVEN (Kafka consumer)   → booking.confirmed, order.completed, noshow.detected
USER REQUEST (REST API)         → POST /api/agent/invoke
```

All trigger sources converge on `SupervisorAgent.InvokeAsync(SupervisorRequest)`.

---

## AgentGroupChat Settings Reference

```csharp
// Capability-based selection — picks agent that matches task capabilities
new CapabilityBasedSelector(_registry)

// Task completion termination
new TaskCompletionTerminationStrategy
{
    MaximumIterations = 10,
    // Also terminates if response contains "[DONE]" or "[FINAL_ANSWER]"
}
```

See [phase-08-agents.md](phase-08-agents.md) for full implementation.
