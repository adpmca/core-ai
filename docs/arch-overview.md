# Architecture Overview

> **Status:** Reference — no code to write here
> **Purpose:** Core library decisions, platform philosophy, solution structure

---

## Platform Summary

**Diva** is an open-source, multi-tenant enterprise AI agent platform. Any SaaS application can integrate Diva to add AI agent capabilities to their existing workflows.

| Concern | Approach |
|---------|---------|
| Agent orchestration | Semantic Kernel AgentGroupChat (SK Agents framework) |
| ReAct loop | SK `ChatCompletionAgent` + `FunctionChoiceBehavior.Auto` |
| MCP tools | `ModelContextProtocol` .NET SDK |
| A2A protocol | `Microsoft.AutoGen` (external agent discovery only) |
| Multi-tenancy | OAuth-derived TenantContext propagated via headers |
| Business rules | Per-tenant DB-stored rules injected into prompts at runtime |
| Database | SQLite (default/dev), SQL Server (enterprise) |
| LLM gateway | Direct (Anthropic/OpenAI) or LiteLLM proxy (optional) |

---

## Library Strategy: "MAF" = Semantic Kernel + AutoGen

The implementation plan calls this "MAF (Microsoft Agent Framework)". In practice:

### Primary: Semantic Kernel (SK)
```xml
<PackageReference Include="Microsoft.SemanticKernel" Version="1.*" />
<PackageReference Include="Microsoft.SemanticKernel.Agents.Core" Version="1.*" />
```

SK provides:
- `ChatCompletionAgent` — single ReAct agent (replaces `AgentBuilder.CreateReActAgent()`)
- `AgentGroupChat` — multi-agent orchestration (replaces plan's `AgentGroupChat`)
- `KernelPlugin` / `KernelFunction` — tool registration (replaces `IKernelFunction`)
- `FunctionChoiceBehavior.Auto` — automatic tool selection (the ReAct "ACT" step)
- `IChatHistory` — conversation memory

### Secondary: AutoGen (A2A only)
```xml
<PackageReference Include="Microsoft.AutoGen.Core" Version="0.14.*" />
<PackageReference Include="Microsoft.AutoGen.Agents" Version="0.14.*" />
```
Used only for A2A agent discovery (external agent cards, inter-service agent communication).

### MCP
```xml
<PackageReference Include="ModelContextProtocol" Version="0.1.*" />
<PackageReference Include="ModelContextProtocol.AspNetCore" Version="0.1.*" />
```

### Plan API → SK Translation

| Plan (conceptual) | Actual SK API |
|---|---|
| `AgentBuilder.CreateReActAgent(kernel)` | `new ChatCompletionAgent { Kernel = kernel, Instructions = prompt }` |
| `.WithTools(mcpTools)` | `kernel.Plugins.AddFromObject(server)` or MCP plugin |
| `.WithMaxIterations(10)` | `AgentGroupChatSettings { TerminationStrategy = new MaxIteration(10) }` |
| `.WithTermination(fn)` | Custom `TerminationStrategy` subclass |
| `AgentGroupChat(agents)` | `new AgentGroupChat(agents) { ExecutionSettings = settings }` |
| `.WithStreaming(true)` | `await foreach (var msg in chat.InvokeAsync())` |

> **Note on SK experimental APIs:** Use `#pragma warning disable SKEXP0110` for SK Agents APIs.

---

## What MAF/SK Provides vs. What We Build

### SK Provides (use as-is)
- ReAct agent loop (Think → Act → Observe via function calling)
- AgentGroupChat multi-agent orchestration
- Tool execution with error handling
- Streaming via `IAsyncEnumerable`
- Chat history / memory management
- OpenAI/Anthropic/Azure connectors

### We Build (custom)
- OAuth token validation + TenantContext extraction
- MCP header injection (OAuth token + custom tenant headers to tools)
- Tenant admin portal (UI for configuring agents, rules, prompts per tenant)
- Business rules engine (per-tenant rules injected into system prompts)
- Dynamic agent registry (hot-reload agents from DB)
- Database schema + EF Core context
- REST API + SignalR streaming endpoints
- React admin portal

---

## Solution Structure

```
Diva/
├── src/
│   ├── Diva.Core/
│   │   ├── Models/
│   │   │   ├── TenantContext.cs          ← canonical rich model
│   │   │   ├── McpRequestContext.cs      ← header bag for MCP calls
│   │   │   ├── AgentRequest.cs
│   │   │   └── AgentResponse.cs
│   │   └── Configuration/
│   │       ├── OAuthOptions.cs
│   │       ├── AgentOptions.cs
│   │       └── DatabaseOptions.cs
│   │
│   ├── Diva.Infrastructure/
│   │   ├── Auth/
│   │   │   ├── OAuthTokenValidator.cs
│   │   │   ├── TenantClaimsExtractor.cs
│   │   │   ├── TenantContextMiddleware.cs
│   │   │   └── HeaderPropagationHandler.cs
│   │   ├── Data/
│   │   │   ├── DivaDbContext.cs
│   │   │   ├── DatabaseProviderFactory.cs
│   │   │   ├── RlsInterceptor.cs         (SQL Server only)
│   │   │   ├── Entities/
│   │   │   └── Migrations/
│   │   ├── LiteLLM/
│   │   │   ├── ILlmClientFactory.cs
│   │   │   ├── LlmClientFactory.cs
│   │   │   └── LiteLLMClient.cs
│   │   ├── Sessions/
│   │   │   ├── ISessionService.cs
│   │   │   └── SessionService.cs
│   │   └── Learning/
│   │       ├── IRuleLearningService.cs
│   │       ├── RuleLearningService.cs
│   │       ├── LlmRuleExtractor.cs
│   │       ├── SessionRuleManager.cs
│   │       ├── FeedbackLearningService.cs
│   │       └── PatternRuleDiscovery.cs
│   │
│   ├── Diva.Agents/
│   │   ├── Workers/
│   │   │   ├── BaseReActAgent.cs         ← SK ChatCompletionAgent wrapper
│   │   │   ├── AnalyticsAgent.cs
│   │   │   └── ReservationAgent.cs
│   │   ├── Supervisor/
│   │   │   ├── SupervisorAgent.cs        ← SK AgentGroupChat orchestration
│   │   │   ├── ISupervisorPipelineStage.cs
│   │   │   ├── AgentSelector.cs
│   │   │   └── SupervisorPrompts.cs
│   │   └── Registry/
│   │       ├── IAgentRegistry.cs
│   │       ├── AgentRegistry.cs
│   │       ├── AgentCapability.cs
│   │       ├── DynamicAgentRegistry.cs   ← hot-reload from DB
│   │       └── DynamicReActAgent.cs
│   │
│   ├── Diva.Tools/
│   │   ├── Core/
│   │   │   ├── McpToolBase.cs
│   │   │   ├── McpHeaderPropagator.cs
│   │   │   └── TenantAwareMcpClient.cs
│   │   ├── Analytics/
│   │   │   ├── AnalyticsMcpServer.cs
│   │   │   ├── GetMetricBreakdownTool.cs
│   │   │   ├── GetYoYTool.cs
│   │   │   ├── RunQueryTool.cs           ← Text-to-SQL
│   │   │   └── GenSnapshotTool.cs
│   │   └── Reservation/
│   │       ├── ReservationMcpServer.cs
│   │       ├── CheckAvailabilityTool.cs
│   │       └── BookReservationTool.cs
│   │
│   ├── Diva.TenantAdmin/
│   │   ├── Services/
│   │   │   ├── ITenantBusinessRulesService.cs
│   │   │   ├── TenantBusinessRulesService.cs
│   │   │   ├── ITenantPromptService.cs
│   │   │   └── TenantPromptService.cs
│   │   ├── Prompts/
│   │   │   ├── ITenantAwarePromptBuilder.cs
│   │   │   ├── TenantAwarePromptBuilder.cs
│   │   │   └── PromptTemplateStore.cs
│   │   └── Models/
│   │       ├── TenantBusinessRule.cs
│   │       ├── TenantPromptOverride.cs
│   │       └── AgentConfiguration.cs
│   │
│   └── Diva.Host/
│       ├── Program.cs
│       ├── appsettings.json
│       ├── Controllers/
│       │   ├── AgentController.cs
│       │   ├── AdminController.cs
│       │   └── HealthController.cs
│       └── Hubs/
│           └── AgentStreamHub.cs
│
├── admin-portal/
│   ├── src/
│   │   ├── pages/
│   │   │   ├── BusinessRules.tsx
│   │   │   ├── PromptEditor.tsx
│   │   │   ├── AgentConfig.tsx
│   │   │   ├── AgentBuilder.tsx
│   │   │   ├── PendingRules.tsx
│   │   │   └── Dashboard.tsx
│   │   ├── components/
│   │   │   ├── RuleEditor.tsx
│   │   │   ├── PromptPreview.tsx
│   │   │   └── AgentToggle.tsx
│   │   └── api/
│   │       └── adminApi.ts
│   └── package.json
│
├── prompts/
│   ├── supervisor/orchestrator.v1.txt
│   ├── analytics/planner.v2.txt
│   ├── analytics/text-to-sql.v1.txt
│   └── shared/security-constraints.v1.txt
│
├── docs/                                ← THIS FOLDER
└── tests/
    ├── Diva.Agents.Tests/
    ├── Diva.Tools.Tests/
    └── Diva.TenantAdmin.Tests/
```

---

## Memory Architecture

```
WORKING MEMORY (~2000 tokens)       — current task, active to-do, recent tool results
    ↓ overflow/compression
SHORT-TERM MEMORY (~4000 tokens)    — summarized recent interactions, key decisions
    ↓ periodic summarization
LONG-TERM MEMORY (vector store)     — historical patterns, tenant knowledge (retrieved semantically)
```

SK handles this via `IChatHistory` (working/short-term) and `ISemanticTextMemory` (long-term).
Agent overrides configured per-agent in `appsettings.json` under `Memory:AgentOverrides`.

---

## Related Docs
- [arch-oauth-flow.md](arch-oauth-flow.md) — how OAuth token flows to MCP tools
- [arch-supervisor.md](arch-supervisor.md) — Supervisor pipeline + ReAct execution trace
- [arch-security.md](arch-security.md) — 5-layer security model
- [arch-multi-tenant.md](arch-multi-tenant.md) — TenantContext model, Tenant/Site hierarchy
