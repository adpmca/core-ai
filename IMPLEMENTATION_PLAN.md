# Diva - Open Source Enterprise Agentic AI Platform
## Multi-Agent AI System - .NET Implementation Plan

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/)
[![MAF](https://img.shields.io/badge/Microsoft-Agent%20Framework-purple.svg)](https://github.com/microsoft/agent-framework)

## Overview

**Diva** is an open-source, enterprise-wide, multi-tenant AI agent platform built on **Microsoft Agent Framework (MAF)**. It provides a unified architecture for deploying domain-specific AI agents across any SaaS application. Any organization can deploy Diva to add AI capabilities to their existing applications.

**Key Pattern**: ReAct (Reasoning + Acting) via MAF's built-in agent loops.

**Standards**: A2A (Agent-to-Agent) + MCP (Model Context Protocol)

**Philosophy**: Leverage MAF for orchestration, focus custom development on **tenant-specific business rules, OAuth integration, and admin UI**.

**Integration**: OAuth 2.0 token-based authentication with automatic tenant identification and header propagation to MCP tools.

---

## What MAF Provides vs What We Build

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    MAF PROVIDES (USE AS-IS)                                  │
│                                                                              │
│  ✅ ReAct Agent Loop          - Built-in Think → Act → Observe cycle        │
│  ✅ Graph Orchestration       - AgentGroupChat, StateGraph                  │
│  ✅ Tool Execution            - IKernelFunction with auto-retry             │
│  ✅ A2A Protocol              - Agent discovery and communication           │
│  ✅ MCP Client                - Connect to MCP tool servers                 │
│  ✅ Memory Abstraction        - IChatMemory, ISemanticTextMemory            │
│  ✅ OpenTelemetry             - Built-in distributed tracing                │
│  ✅ Streaming                 - IAsyncEnumerable response streaming         │
│                                                                              │
├─────────────────────────────────────────────────────────────────────────────┤
│                    WE BUILD (CUSTOM)                                         │
│                                                                              │
│  🔨 OAuth Integration         - Token validation, tenant extraction         │
│  🔨 MCP Header Injection      - OAuth token + custom headers to tools       │
│  🔨 Tenant Admin Portal       - UI for configuring agents per tenant        │
│  🔨 Business Rules Engine     - Per-tenant rules, KPIs, terminology         │
│  🔨 Tenant-Aware Prompts      - Dynamic prompt injection per tenant         │
│  🔨 Domain MCP Tools          - Pluggable domain-specific tool servers      │
│  🔨 Multi-Tenant Auth         - Tenant context extraction, RLS              │
│  🔨 Database Schema           - Rules, prompts, sessions tables             │
│  🔨 API Endpoints             - REST + SignalR for agent invocation         │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Solution Structure

```
Diva/                                            # Open Source Agentic AI Platform
├── src/
│   ├── Diva.Core/                               # Domain models, DTOs
│   │   ├── Models/
│   │   │   ├── TenantContext.cs                # OAuth-derived tenant info
│   │   │   ├── AgentRequest.cs
│   │   │   ├── AgentResponse.cs
│   │   │   └── McpRequestContext.cs            # Headers for MCP tools
│   │   └── Configuration/
│   │       ├── AgentOptions.cs
│   │       └── OAuthOptions.cs
│   │
│   ├── Diva.Agents/                             # MAF Agent definitions
│   │   ├── Supervisor/
│   │   │   ├── SupervisorAgent.cs              # Uses MAF AgentGroupChat
│   │   │   ├── SupervisorPrompts.cs
│   │   │   └── AgentSelector.cs                # Capability-based routing
│   │   ├── Workers/
│   │   │   ├── BaseReActAgent.cs               # MAF ReAct wrapper
│   │   │   └── [DomainAgents]/                 # Pluggable domain agents
│   │   ├── Registry/
│   │   │   ├── IAgentRegistry.cs
│   │   │   ├── AgentRegistry.cs
│   │   │   └── AgentCapability.cs
│   │   └── AgentCards/                         # A2A capability definitions
│   │       └── supervisor-agent.json
│   │
│   ├── Diva.Tools/                              # MCP Tool Infrastructure
│   │   ├── Core/
│   │   │   ├── IMcpToolServer.cs
│   │   │   ├── McpToolBase.cs                  # Base with header injection
│   │   │   ├── McpHeaderPropagator.cs          # OAuth + custom headers
│   │   │   └── TenantAwareMcpClient.cs         # Injects tenant context
│   │   └── [DomainTools]/                      # Pluggable domain tools
│   │
│   ├── Diva.TenantAdmin/                        # TENANT ADMIN (CUSTOM - KEY)
│   │   ├── Services/
│   │   │   ├── ITenantBusinessRulesService.cs
│   │   │   ├── TenantBusinessRulesService.cs
│   │   │   ├── ITenantPromptService.cs
│   │   │   └── TenantPromptService.cs
│   │   ├── Prompts/
│   │   │   ├── ITenantAwarePromptBuilder.cs
│   │   │   ├── TenantAwarePromptBuilder.cs
│   │   │   └── PromptTemplateStore.cs
│   │   ├── Models/
│   │   │   ├── TenantBusinessRule.cs
│   │   │   ├── TenantPromptOverride.cs
│   │   │   └── AgentConfiguration.cs
│   │   └── Validators/
│   │       └── BusinessRuleValidator.cs
│   │
│   ├── Diva.Infrastructure/                     # DB, Auth, LLM
│   │   ├── Data/
│   │   │   ├── DivaDbContext.cs
│   │   │   ├── RlsInterceptor.cs               # TenantID-based RLS
│   │   │   ├── Entities/
│   │   │   │   ├── TenantBusinessRuleEntity.cs
│   │   │   │   ├── TenantPromptOverrideEntity.cs
│   │   │   │   ├── AgentSessionEntity.cs
│   │   │   │   └── AgentConfigurationEntity.cs
│   │   │   └── Migrations/
│   │   ├── Auth/
│   │   │   ├── OAuthTokenValidator.cs          # Validate OAuth tokens
│   │   │   ├── TenantContextMiddleware.cs      # Extract tenant from token
│   │   │   ├── TenantClaimsExtractor.cs        # Parse tenant claims
│   │   │   └── HeaderPropagationHandler.cs     # Forward headers to MCP
│   │   ├── LiteLLM/
│   │   │   ├── LiteLLMClient.cs
│   │   │   └── LiteLLMOptions.cs
│   │   └── Sessions/
│   │       ├── ISessionService.cs
│   │       └── SessionService.cs
│   │
│   └── Diva.Host/                               # API + Agent hosting
│       ├── Program.cs
│       ├── appsettings.json
│       ├── Controllers/
│       │   ├── AgentController.cs
│       │   ├── AdminController.cs              # Tenant admin API
│       │   └── HealthController.cs
│       └── Hubs/
│           └── AgentStreamHub.cs
│
├── admin-portal/                               # ADMIN UI (CUSTOM - KEY)
│   ├── src/
│   │   ├── pages/
│   │   │   ├── BusinessRules.tsx               # Edit business rules
│   │   │   ├── PromptEditor.tsx                # Edit system prompts
│   │   │   ├── AgentConfig.tsx                 # Enable/disable agents
│   │   │   └── Dashboard.tsx                   # Usage & cost overview
│   │   ├── components/
│   │   │   ├── RuleEditor.tsx
│   │   │   ├── PromptPreview.tsx
│   │   │   └── AgentToggle.tsx
│   │   └── api/
│   │       └── adminApi.ts
│   └── package.json
│
├── prompts/                                    # Prompt templates (generic)
│   ├── supervisor/
│   │   └── orchestrator.v1.txt
│   └── shared/
│       ├── security-constraints.v1.txt
│       └── output-format.v1.txt
│
└── tests/
    ├── Diva.Agents.Tests/
    ├── Diva.Tools.Tests/
    └── Diva.TenantAdmin.Tests/
```

---

## Implementation Phases

### Phase 1: Project Setup & MAF Configuration
1. Create .NET 8 solution with Diva namespace
2. Install MAF NuGet packages:
   - `Microsoft.AgentFramework` (unified MAF package)
   - `Microsoft.AgentFramework.MCP`
   - `Microsoft.AgentFramework.A2A`
3. Configure LiteLLM as LLM gateway
4. Set up A2A and MCP in `appsettings.json`

### Phase 2: OAuth Integration & Tenant Identification (PRIORITY)
5. Implement OAuth token validation:
   - Validate tokens from main application
   - Extract TenantID from token claims
   - Support configurable claim mappings
6. Create `TenantContext` model with:
   - TenantID (from OAuth token)
   - UserID, UserRoles
   - Original OAuth token (for propagation)
   - Custom headers collection
7. Implement `TenantContextMiddleware`:
   - Extract and validate OAuth token
   - Parse tenant claims
   - Build TenantContext for request scope
8. Configure OAuth settings in `appsettings.json`

### Phase 3: MCP Header Injection (PRIORITY)
9. Create `McpRequestContext` for header propagation:
   - OAuth Bearer token
   - TenantID header
   - Correlation ID
   - Custom headers (configurable per tool)
10. Implement `TenantAwareMcpClient`:
    - Automatically inject headers to all MCP tool calls
    - Support per-tool header configuration
    - Log outgoing headers for debugging
11. Create `McpHeaderPropagator` service:
    - Manage header injection rules
    - Support header transformation
    - Allow tenant-specific header overrides

### Phase 4: Tenant Admin Infrastructure (CUSTOM)
12. Create database schema for tenant configuration:
    - `TenantBusinessRules` - Per-tenant business rules
    - `TenantPromptOverrides` - Custom prompt modifications
    - `AgentConfigurations` - Agent enable/disable per tenant
    - `AgentSessions` - Conversation history
13. Implement `TenantBusinessRulesService` with caching
14. Implement `TenantAwarePromptBuilder`:
    - Load base prompt template
    - Inject tenant business rules
    - Inject security context (TenantID-based)
    - Apply prompt overrides (append/prepend/replace)
15. Create Admin API endpoints:
    - `GET/POST/PUT /api/admin/business-rules/{tenantId}`
    - `GET/POST/PUT /api/admin/prompts/{tenantId}`
    - `GET/POST /api/admin/agents/{tenantId}`

### Phase 5: MCP Tool Server Infrastructure (CUSTOM)
16. Create `McpToolBase` abstract class:
    - Automatic TenantID injection
    - Header propagation to downstream services
    - Standardized error handling
17. Implement pluggable tool server architecture:
    - Domain-specific tools register via DI
    - Tools receive TenantContext automatically
    - Tools can access OAuth token for downstream calls

### Phase 6: ReAct Worker Agents (MAF + CUSTOM)
18. Create `BaseReActAgent` using MAF's ReAct loop:
    ```csharp
    var agent = AgentBuilder.CreateReActAgent()
        .WithKernel(kernel)
        .WithTools(tenantAwareMcpClient.GetTools())
        .WithPromptBuilder(tenantAwarePromptBuilder)
        .Build();
    ```
19. Domain agents extend `BaseReActAgent` with minimal code
20. Create A2A Agent Cards for each worker

### Phase 7: Supervisor Agent (MAF AgentGroupChat)
21. Implement `SupervisorAgent` using MAF's `AgentGroupChat`
22. Create `AgentSelector` for capability-based routing
23. Implement `AgentRegistry` for dynamic agent discovery
24. Configure A2A for external agent discovery

### Phase 8: Database & Provider Abstraction
25. Create EF Core DbContext with all entities
26. Implement database provider factory (SQLite default, SqlServer optional)
27. Create provider-agnostic tenant isolation (application-level for SQLite, RLS for SqlServer)
28. Add database migrations for both providers

### Phase 9: Dynamic Rule Learning (CUSTOM)
28. Implement `IRuleLearningService`:
    - Rule extraction from conversations via LLM
    - Session-scoped temporary rules
    - Persistent rule saving with approval workflow
29. Create `LlmRuleExtractor` for pattern detection
30. Implement `SessionRuleManager` for working memory rules
31. Create `FeedbackLearningService` for correction-based learning
32. Add `PatternRuleDiscovery` for historical pattern analysis
33. Update `TenantAwarePromptBuilder` to include dynamic rules
34. Create database tables: `LearnedRules`, `UserCorrections`

### Phase 10: Admin Portal UI (CUSTOM)
35. Create React admin portal:
    - Business Rules Editor (JSON form)
    - Prompt Editor (with preview)
    - Agent Configuration (enable/disable per tenant)
    - MCP Header Configuration
    - **Pending Rules Review** (approve/reject learned rules)
    - Usage Dashboard (cost, tokens, requests)
36. Connect to Admin API endpoints

### Phase 11: Dynamic Agent Registration via UI (CUSTOM)
37. Create `AgentDefinition` model for UI-managed agents:
    - Name, DisplayName, Description, AgentType
    - Capabilities, SupportedIntents for routing
    - SystemPrompt, PersonaName, Temperature, MaxIterations
    - ToolBindings (MCP server + allowed tools)
    - RoutingRules (intent patterns, priority)
38. Implement `DynamicAgentRegistry` with hot-reload:
    - Real-time change notifications
    - Runtime agent instance creation
    - Version tracking
39. Create `DynamicReActAgent` runtime wrapper:
    - Instantiates MAF agent from definition
    - Applies tenant-specific configuration
40. Add Admin Portal agent management pages:
    - Agent listing with status
    - Create/Edit agent form
    - System prompt editor with variables
    - Tool binding selection UI
    - Agent testing panel
41. Implement Draft/Publish workflow:
    - Save as draft (not active)
    - Test agent with sample inputs
    - Publish to activate
42. Create REST API for agent CRUD operations

### Phase 12: API Host & Integration
43. Set up ASP.NET Core host with MAF agent runtime
44. Create `/agent/invoke` endpoint for triggering tasks
45. Add SignalR hub for streaming agent responses
46. Expose health endpoints for Kubernetes

---

## Core Design Patterns

### 1. MAF ReAct Agent Loop (Built-in)

MAF provides the ReAct (Reasoning + Acting) pattern out of the box:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    MAF ReAct AGENT LOOP                                      │
│                                                                              │
│    ┌─────────────────────────────────────────────────────────────────┐      │
│    │                                                                  │      │
│    │         ┌──────────┐         ┌──────────┐         ┌──────────┐ │      │
│    │         │  THINK   │────────►│   ACT    │────────►│ OBSERVE  │ │      │
│    │         │(Reasoning)│         │  (Tool)  │         │ (Result) │ │      │
│    │         └──────────┘         └──────────┘         └────┬─────┘ │      │
│    │              ▲                                         │       │      │
│    │              │                                         │       │      │
│    │              └─────────────────────────────────────────┘       │      │
│    │                          (iterate until done)                   │      │
│    └─────────────────────────────────────────────────────────────────┘      │
│                                                                              │
│    What MAF Handles:                                                        │
│    ✅ Automatic tool selection based on reasoning                           │
│    ✅ Tool execution with error handling                                    │
│    ✅ Result observation and next-step planning                             │
│    ✅ Iteration limit and termination conditions                            │
│    ✅ Memory management during loop                                         │
│                                                                              │
│    What We Customize:                                                       │
│    🔨 System prompts (via TenantAwarePromptBuilder)                         │
│    🔨 Available tools (via MCP tool servers)                                │
│    🔨 Business rules injection per tenant                                   │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 2. Supervisor Agent (Capability-Based Routing)

```
┌──────────────────────────────────────────────────────────────────────────────┐
│                         SUPERVISOR AGENT                                      │
│                                                                              │
│  ┌────────────┐   ┌────────────┐   ┌────────────┐   ┌────────────┐          │
│  │ DECOMPOSER │──►│ CAPABILITY │──►│ DISPATCHER │──►│  MONITOR   │──►       │
│  │   NODE     │   │  MATCHER   │   │   NODE     │   │   NODE     │          │
│  └────────────┘   └────────────┘   └─────┬──────┘   └─────┬──────┘          │
│        ▲                                 │                │                  │
│        │                                 │                │                  │
│        │    ┌────────────────────────────┘                │                  │
│        │    │                                             │                  │
│        │    │  ┌──────────────────────────────────────────┼─────────────┐   │
│        │    │  │           WORKER REGISTRY                │             │   │
│        │    │  │                                          ▼             │   │
│        │    │  │  ┌─────────────────────────────────────────────────┐  │   │
│        │    │  │  │              REGISTERED AGENTS                   │  │   │
│        │    │  │  │                                                  │  │   │
│        │    │  │  │  ┌──────────┐ ┌──────────┐ ┌──────────┐         │  │   │
│        │    └──┼──┼─►│Analytics │ │ Reservation  │ │   F&B    │         │  │   │
│        │       │  │  │  Agent   │ │  Agent   │ │  Agent   │         │  │   │
│        │       │  │  └──────────┘ └──────────┘ └──────────┘         │  │   │
│        │       │  │                                                  │  │   │
│        │       │  │  ┌──────────┐ ┌──────────┐ ┌──────────┐         │  │   │
│        │       │  │  │ Weather  │ │Maintenan-│ │  Future  │  ...    │  │   │
│        │       │  │  │  Agent   │ │ce Agent  │ │  Agents  │         │  │   │
│        │       │  │  └──────────┘ └──────────┘ └──────────┘         │  │   │
│        │       │  │         (Future Plugins - Auto-Registered)       │  │   │
│        │       │  └──────────────────────────────────────────────────┘  │   │
│        │       │                                                         │   │
│        │       │  ┌──────────────────────────────────────────────────┐  │   │
│        │       │  │           EXTERNAL AGENTS (A2A Discovery)        │  │   │
│        │       │  │                                                  │  │   │
│        │       └──┼─►  Partner systems, 3rd-party agents via A2A     │  │   │
│        │          └──────────────────────────────────────────────────┘  │   │
│        │                                                                 │   │
│        └─────────────────────────────────────────────────────────────────┘   │
│                        (re-match to different agent on failure)              │
│                                                                              │
│  ┌────────────┐   ┌────────────┐                                            │
│  │ INTEGRATOR │──►│  DELIVERY  │                                            │
│  │   NODE     │   │   NODE     │                                            │
│  └────────────┘   └────────────┘                                            │
└──────────────────────────────────────────────────────────────────────────────┘
```

### 3. Memory Architecture (Token Optimization)

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        AGENT MEMORY SYSTEM                               │
│                                                                          │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │                    WORKING MEMORY (Hot)                          │    │
│  │                    Token Budget: ~2000 tokens                    │    │
│  │                                                                  │    │
│  │  • Current task context                                          │    │
│  │  • Active to-do list                                             │    │
│  │  • Recent tool results (last 3-5)                                │    │
│  │  • Immediate conversation context                                │    │
│  └──────────────────────────┬──────────────────────────────────────┘    │
│                             │ Overflow / Compression                     │
│                             ▼                                            │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │                   SHORT-TERM MEMORY (Warm)                       │    │
│  │                   Token Budget: ~4000 tokens                     │    │
│  │                                                                  │    │
│  │  • Summarized recent interactions                                │    │
│  │  • Completed task summaries                                      │    │
│  │  • Key decisions made                                            │    │
│  │  • Error patterns encountered                                    │    │
│  └──────────────────────────┬──────────────────────────────────────┘    │
│                             │ Periodic summarization                     │
│                             ▼                                            │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │                   LONG-TERM MEMORY (Cold)                        │    │
│  │                   Vector Store (unlimited)                       │    │
│  │                                                                  │    │
│  │  • Historical task patterns                                      │    │
│  │  • Tenant-specific knowledge                                     │    │
│  │  • User preferences                                              │    │
│  │  • Successful strategies                                         │    │
│  │  • Retrieved via semantic search when needed                     │    │
│  └─────────────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## OAuth Integration & Tenant Identification

Diva integrates with the main application via OAuth 2.0. The tenant is identified from the OAuth token.

### OAuth Flow

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    OAUTH INTEGRATION FLOW                                    │
│                                                                              │
│  ┌──────────────┐      ┌──────────────┐      ┌──────────────┐              │
│  │  MAIN APP    │      │   Diva API    │      │  MCP TOOLS   │              │
│  │  (OAuth      │      │   HOST       │      │  (Downstream │              │
│  │   Provider)  │      │              │      │   Services)  │              │
│  └──────┬───────┘      └──────┬───────┘      └──────┬───────┘              │
│         │                     │                     │                       │
│         │  1. User authenticates                    │                       │
│         │     with Main App                         │                       │
│         │                                           │                       │
│         │  2. Main App calls Diva API                │                       │
│         │     with OAuth Bearer token               │                       │
│         │ ─────────────────────►                    │                       │
│         │                     │                     │                       │
│         │                     │  3. Diva validates   │                       │
│         │                     │     token, extracts │                       │
│         │                     │     TenantID        │                       │
│         │                     │                     │                       │
│         │                     │  4. Agent calls     │                       │
│         │                     │     MCP tools with  │                       │
│         │                     │     OAuth token +   │                       │
│         │                     │     custom headers  │                       │
│         │                     │ ─────────────────────►                      │
│         │                     │                     │                       │
│         │                     │                     │  5. MCP tool uses     │
│         │                     │                     │     token to call     │
│         │                     │                     │     backend APIs      │
│         │                     │                     │                       │
└─────────────────────────────────────────────────────────────────────────────┘
```

### TenantContext Model

```csharp
public class TenantContext
{
    // Core Identity (from OAuth token)
    public int TenantId { get; init; }
    public string TenantName { get; init; }
    public string UserId { get; init; }
    public string[] UserRoles { get; init; }

    // OAuth Token (for propagation to MCP tools)
    public string AccessToken { get; init; }
    public DateTime TokenExpiry { get; init; }

    // Custom Headers (configurable per tenant)
    public Dictionary<string, string> CustomHeaders { get; init; } = new();

    // Request Context
    public string CorrelationId { get; init; }
    public string SessionId { get; init; }
}
```

### OAuth Configuration

```json
{
  "OAuth": {
    "Authority": "https://your-identity-provider.com",
    "Audience": "tei-api",
    "ValidateIssuer": true,
    "ValidateAudience": true,
    "ClaimMappings": {
      "TenantId": "tenant_id",
      "TenantName": "tenant_name",
      "UserId": "sub",
      "Roles": "roles"
    },
    "PropagateToken": true
  }
}
```

### TenantContextMiddleware

```csharp
public class TenantContextMiddleware
{
    private readonly RequestDelegate _next;
    private readonly OAuthOptions _options;

    public async Task InvokeAsync(HttpContext context, ITenantClaimsExtractor extractor)
    {
        // 1. Extract Bearer token
        var token = context.Request.Headers.Authorization
            .FirstOrDefault()?.Replace("Bearer ", "");

        if (string.IsNullOrEmpty(token))
        {
            context.Response.StatusCode = 401;
            return;
        }

        // 2. Validate and extract claims
        var claims = await extractor.ExtractClaimsAsync(token);

        // 3. Build TenantContext
        var tenantContext = new TenantContext
        {
            TenantId = claims.GetTenantId(_options.ClaimMappings.TenantId),
            TenantName = claims.GetValue(_options.ClaimMappings.TenantName),
            UserId = claims.GetValue(_options.ClaimMappings.UserId),
            UserRoles = claims.GetRoles(_options.ClaimMappings.Roles),
            AccessToken = token,
            TokenExpiry = claims.GetExpiry(),
            CorrelationId = context.Request.Headers["X-Correlation-ID"]
                ?? Guid.NewGuid().ToString(),
            CustomHeaders = ExtractCustomHeaders(context.Request.Headers)
        };

        // 4. Store in request scope
        context.Items["TenantContext"] = tenantContext;

        await _next(context);
    }

    private Dictionary<string, string> ExtractCustomHeaders(IHeaderDictionary headers)
    {
        // Extract headers with X-Tenant- prefix
        return headers
            .Where(h => h.Key.StartsWith("X-Tenant-"))
            .ToDictionary(h => h.Key, h => h.Value.ToString());
    }
}
```

---

## MCP Header Injection

All MCP tool calls automatically receive OAuth tokens and custom headers.

### Header Propagation Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    MCP HEADER INJECTION                                      │
│                                                                              │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │                    INCOMING REQUEST                                  │    │
│  │                                                                      │    │
│  │  Authorization: Bearer eyJhbGciOiJSUzI1NiIs...                      │    │
│  │  X-Correlation-ID: 550e8400-e29b-41d4-a716-446655440000             │    │
│  │  X-Tenant-Region: us-east-1                                         │    │
│  │  X-Tenant-Environment: production                                   │    │
│  │  X-Custom-Header: custom-value                                      │    │
│  └──────────────────────────────┬──────────────────────────────────────┘    │
│                                 │                                            │
│                                 ▼                                            │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │                    TenantAwareMcpClient                              │    │
│  │                                                                      │    │
│  │  • Extracts TenantContext from request scope                        │    │
│  │  • Builds McpRequestContext with headers                            │    │
│  │  • Injects headers into all MCP tool calls                          │    │
│  └──────────────────────────────┬──────────────────────────────────────┘    │
│                                 │                                            │
│                                 ▼                                            │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │                    MCP TOOL CALL                                     │    │
│  │                                                                      │    │
│  │  Headers sent to MCP Tool Server:                                   │    │
│  │  ├── Authorization: Bearer eyJhbGciOiJSUzI1NiIs...  (OAuth token)   │    │
│  │  ├── X-Tenant-ID: 12345                                              │    │
│  │  ├── X-Correlation-ID: 550e8400-e29b-41d4-a716-446655440000         │    │
│  │  ├── X-Tenant-Region: us-east-1                                      │    │
│  │  ├── X-Tenant-Environment: production                                │    │
│  │  └── X-Custom-Header: custom-value                                   │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### McpRequestContext

```csharp
public class McpRequestContext
{
    // Standard Headers (always sent)
    public string Authorization { get; init; }      // Bearer token
    public int TenantId { get; init; }
    public string CorrelationId { get; init; }

    // Custom Headers (configurable)
    public Dictionary<string, string> CustomHeaders { get; init; } = new();

    // Build from TenantContext
    public static McpRequestContext FromTenantContext(TenantContext tenant)
    {
        return new McpRequestContext
        {
            Authorization = $"Bearer {tenant.AccessToken}",
            TenantId = tenant.TenantId,
            CorrelationId = tenant.CorrelationId,
            CustomHeaders = tenant.CustomHeaders
        };
    }

    // Convert to HTTP headers
    public Dictionary<string, string> ToHeaders()
    {
        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = Authorization,
            ["X-Tenant-ID"] = TenantId.ToString(),
            ["X-Correlation-ID"] = CorrelationId
        };

        foreach (var (key, value) in CustomHeaders)
        {
            headers[key] = value;
        }

        return headers;
    }
}
```

### TenantAwareMcpClient

```csharp
public class TenantAwareMcpClient : IMcpClient
{
    private readonly IMcpClient _inner;
    private readonly IHttpContextAccessor _httpContext;
    private readonly ILogger<TenantAwareMcpClient> _logger;

    public async Task<ToolResult> InvokeToolAsync(
        string toolName,
        object parameters,
        CancellationToken ct)
    {
        // 1. Get TenantContext from request scope
        var tenantContext = _httpContext.HttpContext?.Items["TenantContext"]
            as TenantContext
            ?? throw new UnauthorizedAccessException("No tenant context");

        // 2. Build MCP request context with headers
        var mcpContext = McpRequestContext.FromTenantContext(tenantContext);

        // 3. Log outgoing headers (for debugging)
        _logger.LogDebug(
            "MCP tool {Tool} called with headers: {@Headers}",
            toolName, mcpContext.ToHeaders());

        // 4. Invoke tool with headers
        return await _inner.InvokeToolAsync(
            toolName,
            parameters,
            mcpContext,
            ct);
    }
}
```

### MCP Tool Base Class

```csharp
public abstract class McpToolBase<TParams, TResult>
{
    protected TenantContext TenantContext { get; private set; }
    protected McpRequestContext RequestContext { get; private set; }

    public async Task<ToolResult> ExecuteAsync(
        TParams parameters,
        McpRequestContext context,
        CancellationToken ct)
    {
        // Store context for derived classes
        RequestContext = context;
        TenantContext = await ResolveTenantContextAsync(context, ct);

        // Validate tenant access
        await ValidateTenantAccessAsync(ct);

        // Execute tool-specific logic
        var result = await ExecuteInternalAsync(parameters, ct);

        return new ToolResult { Success = true, Data = result };
    }

    protected abstract Task<TResult> ExecuteInternalAsync(
        TParams parameters,
        CancellationToken ct);

    // Helper: Call downstream API with propagated headers
    protected async Task<T> CallDownstreamApiAsync<T>(
        HttpClient client,
        string url,
        CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);

        // Propagate all headers to downstream service
        foreach (var (key, value) in RequestContext.ToHeaders())
        {
            request.Headers.TryAddWithoutValidation(key, value);
        }

        var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<T>(ct);
    }
}
```

### Header Configuration per Tenant

```csharp
public class TenantHeaderConfiguration
{
    public int TenantId { get; set; }
    public Dictionary<string, HeaderRule> HeaderRules { get; set; } = new();
}

public class HeaderRule
{
    public string HeaderName { get; set; }
    public HeaderSource Source { get; set; }  // Claim, Static, Transform
    public string SourceValue { get; set; }
    public string TransformExpression { get; set; }
}

public enum HeaderSource
{
    Claim,          // Extract from OAuth token claim
    Static,         // Fixed value
    Transform,      // Transform another value
    RequestHeader   // Pass through from incoming request
}

// Example configuration
{
  "TenantHeaderConfigurations": [
    {
      "TenantId": 1,
      "HeaderRules": {
        "X-Backend-Tenant": {
          "Source": "Claim",
          "SourceValue": "backend_tenant_id"
        },
        "X-API-Version": {
          "Source": "Static",
          "SourceValue": "v2"
        },
        "X-Region": {
          "Source": "RequestHeader",
          "SourceValue": "X-Tenant-Region"
        }
      }
    }
  ]
}
```

---

## Key Code Samples

### MAF ReAct Agent with Tenant-Aware Prompts

```csharp
public class AnalyticsAgent : IWorkerAgent
{
    private readonly IKernel _kernel;
    private readonly ITenantAwarePromptBuilder _promptBuilder;
    private readonly IMcpClient _mcpClient;

    public async Task<AgentResponse> ExecuteAsync(
        AgentRequest request,
        TenantContext tenant,
        CancellationToken ct)
    {
        // 1. Build tenant-specific system prompt
        var systemPrompt = await _promptBuilder.BuildPromptAsync(
            tenant,
            agentType: "Analytics",
            promptSection: "react-agent",
            variables: new Dictionary<string, object>
            {
                ["TaskDescription"] = request.Task,
                ["AvailableTools"] = GetToolDescriptions()
            },
            ct);

        // 2. Create MAF ReAct agent with tenant prompt
        var agent = AgentBuilder.CreateReActAgent(_kernel)
            .WithSystemPrompt(systemPrompt)
            .WithTools(await _mcpClient.GetToolsAsync("analytics", ct))
            .WithMaxIterations(10)
            .WithTermination(result => result.Contains("[DONE]"))
            .Build();

        // 3. Execute ReAct loop (MAF handles Think → Act → Observe)
        var result = await agent.InvokeAsync(request.UserMessage, ct);

        return new AgentResponse
        {
            Content = result.Content,
            ToolsUsed = result.ToolInvocations.Select(t => t.ToolName).ToList(),
            Reasoning = result.ReasoningSteps
        };
    }
}
```

### Supervisor with MAF AgentGroupChat

```csharp
public class SupervisorAgent
{
    private readonly IAgentRegistry _registry;
    private readonly ITenantAwarePromptBuilder _promptBuilder;

    public async Task<SupervisorResponse> OrchestrateAsync(
        string userRequest,
        TenantContext tenant,
        CancellationToken ct)
    {
        // 1. Get available agents for this tenant
        var agents = await _registry.GetAgentsForTenantAsync(tenant.TenantId, ct);

        // 2. Build supervisor prompt with tenant rules
        var supervisorPrompt = await _promptBuilder.BuildPromptAsync(
            tenant,
            agentType: "Supervisor",
            promptSection: "orchestrator",
            variables: new Dictionary<string, object>
            {
                ["AvailableAgents"] = agents.Select(a => a.Description),
                ["UserRequest"] = userRequest
            },
            ct);

        // 3. Create MAF AgentGroupChat for multi-agent orchestration
        var groupChat = new AgentGroupChat(agents.Select(a => a.Instance).ToArray())
        {
            ExecutionSettings = new()
            {
                SelectionStrategy = new CapabilityBasedSelector(_registry),
                TerminationStrategy = new TaskCompletionTermination()
            }
        };

        // 4. Add supervisor as orchestrator
        groupChat.AddChatMessage(new ChatMessage(AuthorRole.System, supervisorPrompt));
        groupChat.AddChatMessage(new ChatMessage(AuthorRole.User, userRequest));

        // 5. Execute group chat (MAF handles agent selection and coordination)
        var responses = new List<string>();
        await foreach (var message in groupChat.InvokeAsync(ct))
        {
            responses.Add(message.Content);
        }

        return new SupervisorResponse
        {
            FinalAnswer = responses.Last(),
            AgentContributions = groupChat.GetHistory()
        };
    }
}
```

### Tenant-Aware Prompt Builder (Custom)

```csharp
public class TenantAwarePromptBuilder : ITenantAwarePromptBuilder
{
    private readonly IPromptTemplateStore _templates;
    private readonly ITenantBusinessRulesService _rules;

    public async Task<string> BuildPromptAsync(
        TenantContext tenant,
        string agentType,
        string promptSection,
        Dictionary<string, object> variables,
        CancellationToken ct)
    {
        // 1. Load base template
        var basePrompt = await _templates.GetTemplateAsync(agentType, promptSection, ct);

        // 2. Get tenant business rules
        var businessRules = await _rules.GetPromptInjectionsAsync(
            tenant.TenantId, agentType, ct);

        // 3. Get tenant prompt overrides
        var overrides = await _rules.GetPromptOverridesAsync(
            tenant.TenantId, agentType, promptSection, ct);

        // 4. Build security context
        var securityContext = $"""
            CRITICAL SECURITY CONSTRAINTS:
            - Operating for TenantID={tenant.TenantId}, SiteID={tenant.CurrentSiteId}
            - ALL database queries MUST include WHERE TenantID={tenant.CurrentSiteId}
            - NEVER access data outside this scope
            """;

        // 5. Apply variable substitution
        var prompt = RenderVariables(basePrompt, variables);

        // 6. Apply overrides
        foreach (var override_ in overrides.OrderBy(o => o.Priority))
        {
            prompt = override_.MergeMode switch
            {
                PromptMergeMode.Append => prompt + "\n\n" + override_.CustomPromptText,
                PromptMergeMode.Prepend => override_.CustomPromptText + "\n\n" + prompt,
                PromptMergeMode.Replace => override_.CustomPromptText,
                _ => prompt
            };
        }

        // 7. Combine all sections
        return $"""
            {securityContext}

            ## Tenant Business Rules
            {businessRules}

            ## Agent Instructions
            {prompt}
            """;
    }
}
```

### Agent Registry (Custom)

```csharp
public interface IAgentRegistry
{
    Task RegisterAgentAsync(AgentCapability capability, CancellationToken ct);
    Task<List<RegisteredAgent>> GetAgentsForTenantAsync(int tenantId, CancellationToken ct);
    Task<RegisteredAgent?> FindBestMatchAsync(string taskDescription, int tenantId, CancellationToken ct);
}

public record AgentCapability
{
    public string AgentId { get; init; }
    public string AgentType { get; init; }
    public string Description { get; init; }
    public string[] Capabilities { get; init; }  // ["analytics", "reporting", "yoy"]
    public string[] SupportedTools { get; init; }
    public int Priority { get; init; }
}

public record RegisteredAgent
{
    public AgentCapability Capability { get; init; }
    public IWorkerAgent Instance { get; init; }
    public bool EnabledForTenant { get; init; }
}
```

### MCP Tool with Tenant Context (Custom)

```csharp
[McpTool("GetMetricBreakdown")]
public class GetMetricBreakdownTool : IMcpTool
{
    private readonly IDivaDbContext _db;

    public async Task<ToolResult> ExecuteAsync(
        GetMetricBreakdownParams parameters,
        TenantContext tenant,
        CancellationToken ct)
    {
        // Mandatory: Inject TenantID filter
        var query = _db.FactRevenue
            .Where(r => r.TenantID == tenant.CurrentSiteId)  // RLS
            .Where(r => r.Date >= parameters.StartDate)
            .Where(r => r.Date <= parameters.EndDate);

        if (parameters.Channel != null)
            query = query.Where(r => r.Channel == parameters.Channel);

        var results = await query
            .GroupBy(r => r.Channel)
            .Select(g => new
            {
                Channel = g.Key,
                TotalRevenue = g.Sum(r => r.Amount),
                TransactionCount = g.Count()
            })
            .ToListAsync(ct);

        return new ToolResult
        {
            Success = true,
            Data = results
        };
    }
}
```

---

## Configuration

### appsettings.json

```json
{
  "LLM": {
    "Provider": "Anthropic",
    "Anthropic": {
      "ApiKey": "...",
      "Model": "claude-sonnet-4-20250514"
    },
    "OpenAI": {
      "ApiKey": "...",
      "Model": "gpt-4o"
    }
  },
  "A2A": {
    "Enabled": true,
    "ServerPort": 8080,
    "AgentCardsPath": "./AgentCards"
  },
  "MCP": {
    "Enabled": true,
    "Servers": [
      { "Name": "analytics", "Transport": "stdio" }
    ]
  },
  "Memory": {
    "WorkingMemoryLimit": 2000,
    "ShortTermMemoryLimit": 4000,
    "ReservedTokens": 1000,
    "CompressionBatchSize": 5,
    "MaxLongTermResults": 3,
    "VectorStore": {
      "Provider": "InMemory",
      "ConnectionString": ""
    },
    "AgentOverrides": {
      "analytics-agent": {
        "WorkingMemoryLimit": 3000,
        "MaxToolResultsInContext": 5
      },
      "supervisor-agent": {
        "WorkingMemoryLimit": 4000,
        "EnableLongTermRetrieval": true
      }
    }
  },
  "Database": {
    "Provider": "SQLite",  // "SQLite" (default) or "SqlServer"
    "SQLite": {
      "ConnectionString": "Data Source=diva.db"
    },
    "SqlServer": {
      "ConnectionString": "Server=localhost;Database=Diva;Trusted_Connection=True;TrustServerCertificate=True"
    }
  }
}
```

---

## Dependencies (NuGet)

```xml
<!-- Microsoft Agent Framework -->
<PackageReference Include="Microsoft.Extensions.AI" Version="9.0.0" />
<PackageReference Include="Microsoft.SemanticKernel" Version="2.0.0" />
<PackageReference Include="Microsoft.AutoGen.Core" Version="1.0.0" />
<PackageReference Include="Microsoft.AutoGen.Agents" Version="1.0.0" />

<!-- MCP -->
<PackageReference Include="ModelContextProtocol" Version="1.0.0" />
<PackageReference Include="ModelContextProtocol.Server" Version="1.0.0" />

<!-- Infrastructure - Database -->
<PackageReference Include="Microsoft.EntityFrameworkCore" Version="9.0.0" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="9.0.0" />  <!-- Default -->
<PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="9.0.0" />  <!-- Optional -->
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="9.0.0" />

<!-- Infrastructure - Auth & Web -->
<PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="9.0.0" />
<PackageReference Include="Microsoft.AspNetCore.SignalR" Version="9.0.0" />
```

---

## Critical Files to Create

### Tenant Admin (CUSTOM - PRIORITY)
1. `src/Diva.TenantAdmin/Services/ITenantBusinessRulesService.cs`
2. `src/Diva.TenantAdmin/Services/TenantBusinessRulesService.cs`
3. `src/Diva.TenantAdmin/Prompts/ITenantAwarePromptBuilder.cs`
4. `src/Diva.TenantAdmin/Prompts/TenantAwarePromptBuilder.cs`
5. `src/Diva.TenantAdmin/Models/TenantBusinessRule.cs`
6. `src/Diva.TenantAdmin/Models/TenantPromptOverride.cs`
7. `src/Diva.TenantAdmin/Models/AgentConfiguration.cs`

### MCP Tools (CUSTOM)
8. `src/Diva.Tools/Analytics/AnalyticsMcpServer.cs`
9. `src/Diva.Tools/Analytics/GetMetricBreakdownTool.cs`
10. `src/Diva.Tools/Analytics/GetYoYTool.cs`
11. `src/Diva.Tools/Analytics/RunQueryTool.cs`
12. `src/Diva.Tools/Analytics/GenSnapshotTool.cs`
13. `src/Diva.Tools/Reservation/ReservationMcpServer.cs`
14. `src/Diva.Tools/Reservation/CheckAvailabilityTool.cs`
15. `src/Diva.Tools/Reservation/BookReservationTool.cs`

### Agents (MAF + CUSTOM)
16. `src/Diva.Agents/Workers/BaseReActAgent.cs`
17. `src/Diva.Agents/Workers/AnalyticsAgent.cs`
18. `src/Diva.Agents/Workers/ReservationAgent.cs`
19. `src/Diva.Agents/Supervisor/SupervisorAgent.cs`
20. `src/Diva.Agents/Registry/IAgentRegistry.cs`
21. `src/Diva.Agents/Registry/AgentRegistry.cs`
22. `src/Diva.Agents/AgentCards/analytics-agent.json`
23. `src/Diva.Agents/AgentCards/Reservation-agent.json`

### Infrastructure (CUSTOM)
24. `src/Diva.Infrastructure/Data/DivaDbContext.cs`
25. `src/Diva.Infrastructure/Data/RlsInterceptor.cs`
26. `src/Diva.Infrastructure/Auth/TenantContextMiddleware.cs`
27. `src/Diva.Infrastructure/LiteLLM/LiteLLMClient.cs`
28. `src/Diva.Infrastructure/Sessions/SessionService.cs`

### Host & API (CUSTOM)
29. `src/Diva.Host/Program.cs`
30. `src/Diva.Host/Controllers/AgentController.cs`
31. `src/Diva.Host/Controllers/AdminController.cs`
32. `src/Diva.Host/Hubs/AgentStreamHub.cs`

### Admin Portal UI (CUSTOM)
33. `admin-portal/src/pages/BusinessRules.tsx`
34. `admin-portal/src/pages/PromptEditor.tsx`
35. `admin-portal/src/pages/AgentConfig.tsx`
36. `admin-portal/src/pages/Dashboard.tsx`
37. `admin-portal/src/components/RuleEditor.tsx`
38. `admin-portal/src/api/adminApi.ts`

---

## Estimated Scope

| Phase | Files | Description | MAF vs Custom |
|-------|-------|-------------|---------------|
| 1. Setup | ~5 | Solution, packages, config | Config only |
| 2. Tenant Admin | ~10 | Business rules, prompts, models | **100% Custom** |
| 3. MCP Tools | ~12 | Analytics, Reservation tool servers | **100% Custom** |
| 4. ReAct Agents | ~8 | Worker agents using MAF ReAct | 70% MAF, 30% Custom |
| 5. Supervisor | ~5 | AgentGroupChat orchestration | 80% MAF, 20% Custom |
| 6. Database | ~6 | DbContext, entities, RLS | **100% Custom** |
| 7. Admin Portal UI | ~8 | React admin interface | **100% Custom** |
| 8. Host & API | ~6 | ASP.NET Core endpoints | **100% Custom** |
| **Total** | **~60** | Focused custom implementation | **~40% reduction** |

### Effort Comparison

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    EFFORT COMPARISON                                         │
│                                                                              │
│  ORIGINAL PLAN (Full Custom):                                               │
│  ├── Custom Graph Engine (nodes, strategies, policies)     ~15 files        │
│  ├── Custom Memory System                                  ~8 files         │
│  ├── Custom ReAct Implementation                           ~10 files        │
│  └── Total                                                 ~99 files        │
│                                                                              │
│  REVISED PLAN (MAF + Custom):                                               │
│  ├── MAF provides: Graph, Memory, ReAct                    0 files (free!)  │
│  ├── Tenant Admin (business rules, prompts)                ~10 files        │
│  ├── MCP Tools (domain-specific)                           ~12 files        │
│  ├── Agent wrappers + registry                             ~8 files         │
│  ├── Admin Portal UI                                       ~8 files         │
│  ├── Infrastructure + Host                                 ~12 files        │
│  └── Total                                                 ~60 files        │
│                                                                              │
│  SAVINGS: ~40 files (40% reduction)                                         │
│  FOCUS: Tenant-specific customization (your differentiator)                 │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## MAF ReAct Deep Dive

### How MAF ReAct Works

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    MAF ReAct EXECUTION FLOW                                  │
│                                                                              │
│  User: "What was cart revenue for South Campus last month?"                   │
│                                                                              │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │ ITERATION 1                                                          │    │
│  │                                                                      │    │
│  │ THINK: I need to get cart revenue data for South Campus for last      │    │
│  │        month. First, let me check the available metrics.            │    │
│  │                                                                      │    │
│  │ ACT:   GetMetricBreakdown(channel="CART_RENTAL",                    │    │
│  │                           location="South Campus",                     │    │
│  │                           period="last_month")                       │    │
│  │                                                                      │    │
│  │ OBSERVE: {total: 24500, transactions: 1250, avg_per_round: 19.60}   │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                                                              │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │ ITERATION 2                                                          │    │
│  │                                                                      │    │
│  │ THINK: I have the data. Let me also get YoY comparison for context. │    │
│  │                                                                      │    │
│  │ ACT:   GetYoY(metric="cart_revenue", location="South Campus")         │    │
│  │                                                                      │    │
│  │ OBSERVE: {current: 24500, previous: 22700, change_pct: 7.9}         │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                                                              │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │ ITERATION 3                                                          │    │
│  │                                                                      │    │
│  │ THINK: I have all the data needed. Let me synthesize the response.  │    │
│  │                                                                      │    │
│  │ ACT:   [No tool call - generate final response]                     │    │
│  │                                                                      │    │
│  │ OUTPUT: "Cart rental revenue for South Campus last month was $24,500  │    │
│  │          across 1,250 transactions (avg $19.60/round). This is      │    │
│  │          7.9% higher than the same period last year."               │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                                                              │
│  [DONE] - MAF terminates loop                                               │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### MAF ReAct Configuration Options

```csharp
// MAF provides these configuration options out of the box
var agent = AgentBuilder.CreateReActAgent(kernel)
    // System prompt (we inject tenant-specific content here)
    .WithSystemPrompt(tenantAwarePrompt)

    // Tools from MCP server
    .WithTools(mcpTools)

    // Iteration limits (prevents infinite loops)
    .WithMaxIterations(10)

    // Custom termination condition
    .WithTermination(result =>
        result.Contains("[DONE]") ||
        result.Contains("[FINAL_ANSWER]"))

    // Error handling (MAF handles tool errors)
    .WithToolErrorHandler((tool, error) =>
    {
        _logger.LogWarning("Tool {Tool} failed: {Error}", tool, error);
        return ToolErrorAction.Retry;  // or Skip, Fail
    })

    // Memory (MAF handles conversation history)
    .WithChatMemory(chatMemory)

    // Streaming (MAF streams Think/Act/Observe steps)
    .WithStreaming(true)

    .Build();
```

---

## Adding Future Agents

To add a new agent (e.g., Weather Agent):

```csharp
// 1. Create agent using MAF ReAct (minimal code!)
public class WeatherAgent : BaseReActAgent
{
    public WeatherAgent(
        IKernel kernel,
        IMcpClient mcpClient,
        ITenantAwarePromptBuilder promptBuilder)
        : base(kernel, mcpClient, promptBuilder, agentType: "Weather")
    {
    }

    protected override string[] GetCapabilities() =>
        ["weather-forecast", "course-conditions", "rain-delay-prediction"];
}

// 2. Create MCP tool server
public class WeatherMcpServer : IMcpServer
{
    [McpTool("GetWeatherForecast")]
    public Task<ToolResult> GetForecast(GetForecastParams p, TenantContext tenant);

    [McpTool("GetCourseConditions")]
    public Task<ToolResult> GetConditions(GetConditionsParams p, TenantContext tenant);
}

// 3. Register in DI
services.AddScoped<IWorkerAgent, WeatherAgent>();
services.AddMcpServer<WeatherMcpServer>("weather");
```

**No changes to Supervisor required** - auto-discovered via `IAgentRegistry`.

---

## Verification Checklist

1. **Unit Tests**: Mock MCP tools, test agent graph transitions
2. **Integration Tests**:
   - Test A2A agent discovery endpoint
   - Test MCP tool invocation
   - Test full supervisor → worker flow
3. **Manual Testing**:
   - Call `/agent/invoke` with "Generate daily snapshot for Property 1"
   - Verify A2A task delegation to Analytics Agent
   - Verify MCP tools execute with TenantID
   - Verify aggregated result returned

---

## LiteLLM Proxy Integration (OPTIONAL)

LiteLLM integration is **optional** and enabled via configuration. When disabled, agents call LLM providers directly via MAF's built-in clients.

### When to Use LiteLLM

| Use Case | LiteLLM | Direct |
|----------|---------|--------|
| Multi-provider routing | ✅ | ❌ |
| Per-tenant rate limiting | ✅ | Manual |
| Cost tracking & budgets | ✅ | Manual |
| Prompt caching | ✅ | ❌ |
| Single-provider deployment | ❌ | ✅ |
| Minimal latency | ❌ | ✅ |
| Simpler architecture | ❌ | ✅ |

### Configuration Toggle

```json
{
  "LLM": {
    "UseLiteLLM": false,  // Set to true to enable LiteLLM proxy

    // Direct provider config (when UseLiteLLM = false)
    "DirectProvider": {
      "Provider": "Anthropic",  // or "OpenAI", "Azure"
      "ApiKey": "${ANTHROPIC_API_KEY}",
      "Model": "claude-sonnet-4-20250514"
    },

    // LiteLLM config (when UseLiteLLM = true)
    "LiteLLM": {
      "BaseUrl": "http://litellm:4000",
      "MasterKey": "${LITELLM_MASTER_KEY}",
      "DefaultModel": "claude-sonnet"
    }
  }
}
```

### LLM Client Factory

```csharp
public interface ILlmClientFactory
{
    ILlmClient CreateClient(TenantContext tenant);
}

public class LlmClientFactory : ILlmClientFactory
{
    private readonly LlmOptions _options;
    private readonly IServiceProvider _services;

    public ILlmClient CreateClient(TenantContext tenant)
    {
        if (_options.UseLiteLLM)
        {
            // Route through LiteLLM proxy
            return _services.GetRequiredService<LiteLLMClient>();
        }
        else
        {
            // Direct provider connection
            return _options.DirectProvider.Provider switch
            {
                "Anthropic" => new AnthropicClient(_options.DirectProvider),
                "OpenAI" => new OpenAIClient(_options.DirectProvider),
                "Azure" => new AzureOpenAIClient(_options.DirectProvider),
                _ => throw new NotSupportedException($"Provider {_options.DirectProvider.Provider}")
            };
        }
    }
}
```

### Architecture (When LiteLLM Enabled)

When `UseLiteLLM: true`, all LLM calls route through a **single LiteLLM Proxy** instance for centralized control.

### Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                     ALL AGENTS                                   │
│  (Supervisor, Analytics, Reservation, F&B)                          │
└──────────────────────────┬──────────────────────────────────────┘
                           │ HTTP/OpenAI-compatible API
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│                    LiteLLM PROXY                                 │
│                                                                  │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐              │
│  │   Model     │  │    Rate     │  │  TenantID │              │
│  │   Routing   │  │   Limiting  │  │  Injection  │              │
│  └─────────────┘  └─────────────┘  └─────────────┘              │
│                                                                  │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐              │
│  │    Cost     │  │   Prompt    │  │   Logging   │              │
│  │  Tracking   │  │   Studio    │  │   & Audit   │              │
│  └─────────────┘  └─────────────┘  └─────────────┘              │
└──────────────────────────┬──────────────────────────────────────┘
                           │
           ┌───────────────┼───────────────┐
           ▼               ▼               ▼
    ┌───────────┐   ┌───────────┐   ┌───────────┐
    │ Anthropic │   │  OpenAI   │   │   Azure   │
    │  Claude   │   │  GPT-4o   │   │  OpenAI   │
    └───────────┘   └───────────┘   └───────────┘
```

### LiteLLM Configuration

```yaml
# litellm_config.yaml
model_list:
  - model_name: claude-sonnet
    litellm_params:
      model: anthropic/claude-sonnet-4-20250514
      api_key: os.environ/ANTHROPIC_API_KEY
    model_info:
      max_tokens: 200000

  - model_name: gpt-4o
    litellm_params:
      model: openai/gpt-4o
      api_key: os.environ/OPENAI_API_KEY
    model_info:
      max_tokens: 128000

litellm_settings:
  drop_params: true
  set_verbose: false

general_settings:
  master_key: sk-litellm-master-key
  database_url: postgresql://...

  # Team = Tenant mapping
  enable_team_based_access: true

  # Cost tracking
  store_spend_logs: true

  # Rate limiting per team
  enable_rate_limiting: true
```

### LiteLLM Teams (Per-Tenant)

```json
{
  "teams": [
    {
      "team_id": "team_acme",
      "team_alias": "Acme Corporation",
      "metadata": { "tenant_id": 1 },
      "max_budget": 500.00,
      "budget_duration": "monthly",
      "rpm_limit": 60,
      "tpm_limit": 100000
    },
    {
      "team_id": "team_beta",
      "team_alias": "Beta Industries",
      "metadata": { "tenant_id": 2 },
      "max_budget": 300.00,
      "budget_duration": "monthly",
      "rpm_limit": 40,
      "tpm_limit": 60000
    }
  ]
}
```

### TenantID Injection Middleware

```csharp
public class LiteLLMClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly string _litellmBaseUrl;

    public async Task<string> GenerateAsync(
        string prompt,
        TenantContext context,
        CancellationToken ct)
    {
        var request = new
        {
            model = "claude-sonnet",
            messages = new[]
            {
                new { role = "system", content = BuildSystemPrompt(context) },
                new { role = "user", content = prompt }
            },
            metadata = new
            {
                tenant_id = context.TenantId,
                site_id = context.SiteId,
                agent_name = context.AgentName,
                session_id = context.SessionId
            }
        };

        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", context.TeamApiKey);

        var response = await _http.PostAsJsonAsync(
            $"{_litellmBaseUrl}/chat/completions",
            request, ct);

        return await ParseResponseAsync(response, ct);
    }

    private string BuildSystemPrompt(TenantContext context)
    {
        return $"""
            You are an AI agent for the Diva Enterprise Platform.

            CRITICAL SECURITY CONSTRAINTS:
            - You are operating for TenantID={context.TenantId}, SiteID={context.SiteId}
            - ALL SQL queries MUST include WHERE TenantID={context.SiteId}
            - NEVER access data outside this tenant/site scope
            - If asked to access other tenants, REFUSE and explain why

            Current context:
            - Tenant: {context.TenantName}
            - Site: {context.SiteName}
            - User Role: {context.UserRole}
            """;
    }
}
```

### Cost Tracking Queries

```sql
-- Daily spend by tenant
SELECT
    team_id,
    DATE(created_at) as date,
    SUM(spend) as daily_spend,
    SUM(total_tokens) as daily_tokens
FROM spend_logs
GROUP BY team_id, DATE(created_at);

-- Spend by agent type
SELECT
    metadata->>'agent_name' as agent,
    SUM(spend) as total_spend
FROM spend_logs
WHERE team_id = 'team_acme'
GROUP BY metadata->>'agent_name';
```

---

## Trigger Sources

The system supports multiple trigger sources for invoking agent workflows.

### Trigger Types

```
┌─────────────────────────────────────────────────────────────────┐
│                      TRIGGER SOURCES                             │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐           │
│  │   SCHEDULED  │  │    EVENT     │  │     USER     │           │
│  │   TRIGGERS   │  │   TRIGGERS   │  │   REQUESTS   │           │
│  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘           │
│         │                 │                 │                    │
│  • Temporal/Airflow       │          • GM Dashboard              │
│  • Daily snapshots        │          • Booking Portal            │
│  • Weekly reports         │          • Mobile App                │
│  • Monthly summaries      │          • API Direct                │
│                           │                                      │
│                    • Kafka Events                                │
│                    • Booking confirmed                           │
│                    • Order completed                             │
│                    • No-show detected                            │
│                    • Weather alert                               │
│                                                                  │
└──────────────────────────┬──────────────────────────────────────┘
                           │
                           ▼
                    ┌──────────────┐
                    │  SUPERVISOR  │
                    │    AGENT     │
                    └──────────────┘
```

### Scheduled Triggers (Temporal)

```csharp
// Temporal workflow for daily snapshots
[Workflow]
public class DailySnapshotWorkflow
{
    [WorkflowRun]
    public async Task RunAsync(DailySnapshotInput input)
    {
        // For each property, trigger analytics snapshot
        foreach (var propertyId in input.PropertyIds)
        {
            await Workflow.ExecuteActivityAsync(
                (AgentActivities a) => a.InvokeSupervisorAsync(new SupervisorRequest
                {
                    TriggerType = "scheduled",
                    TriggerPayload = new { schedule = "daily_snapshot" },
                    TenantId = input.TenantId,
                    SiteId = propertyId,
                    TaskDescription = $"Generate daily analytics snapshot for property {propertyId}"
                }),
                new ActivityOptions { StartToCloseTimeout = TimeSpan.FromMinutes(5) }
            );
        }
    }
}

// Schedule configuration
[Schedule("daily-snapshot-tenant-1")]
public class TenantSnapshotSchedule
{
    public string CronExpression => "0 7 * * *";  // 7:00 AM daily
    public string WorkflowType => nameof(DailySnapshotWorkflow);
    public DailySnapshotInput Args => new()
    {
        TenantId = 1,
        TenantIds = new[] { 1, 2, 3, 4, 5 }  // All tenant locations
    };
}
```

### Event Triggers (Kafka)

```csharp
// Kafka consumer for real-time events
public class AgentEventConsumer : IHostedService
{
    private readonly IConsumer<string, AgentEvent> _consumer;
    private readonly ISupervisorAgent _supervisor;

    public async Task ProcessEventAsync(AgentEvent evt)
    {
        var request = evt.Type switch
        {
            "booking.confirmed" => new SupervisorRequest
            {
                TriggerType = "event",
                TriggerPayload = evt.Payload,
                TaskDescription = $"Process booking confirmation for {evt.Payload.BookingId}"
            },
            "order.completed" => new SupervisorRequest
            {
                TriggerType = "event",
                TaskDescription = $"Process completed F&B order {evt.Payload.OrderId}"
            },
            "noshow.detected" => new SupervisorRequest
            {
                TriggerType = "event",
                TaskDescription = $"Handle no-show for booking {evt.Payload.BookingId}"
            },
            _ => null
        };

        if (request != null)
        {
            await _supervisor.InvokeAsync(request);
        }
    }
}
```

### API Triggers (User Requests)

```csharp
[ApiController]
[Route("api/agent")]
public class AgentController : ControllerBase
{
    [HttpPost("invoke")]
    [Authorize]
    public async Task<IActionResult> InvokeAgent([FromBody] AgentInvokeRequest request)
    {
        var tenantContext = HttpContext.GetTenantContext();

        var result = await _supervisor.InvokeAsync(new SupervisorRequest
        {
            TriggerType = "user_request",
            TriggerPayload = new { source = request.Source },
            TenantId = tenantContext.TenantId,
            SiteId = tenantContext.SiteId,
            UserId = tenantContext.UserId,
            TaskDescription = request.Query,
            SessionId = request.SessionId  // For multi-turn conversations
        });

        return Ok(result);
    }

    [HttpPost("invoke/stream")]
    [Authorize]
    public async Task StreamInvokeAgent([FromBody] AgentInvokeRequest request)
    {
        Response.ContentType = "text/event-stream";

        await foreach (var chunk in _supervisor.InvokeStreamAsync(request))
        {
            await Response.WriteAsync($"data: {JsonSerializer.Serialize(chunk)}\n\n");
            await Response.Body.FlushAsync();
        }
    }
}
```

---

## Delivery Channels

Agent responses can be delivered through multiple channels.

### Delivery Architecture

```
┌──────────────────────────────────────────────────────────────────┐
│                    RESULT INTEGRATOR NODE                         │
│                                                                   │
│           Combines all worker results into final response         │
└──────────────────────────┬───────────────────────────────────────┘
                           │
                           ▼
┌──────────────────────────────────────────────────────────────────┐
│                      DELIVERY NODE                                │
│                                                                   │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐              │
│  │    EMAIL    │  │  DASHBOARD  │  │     API     │              │
│  │   Delivery  │  │    Push     │  │   Response  │              │
│  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘              │
│         │                │                │                      │
│         ▼                ▼                ▼                      │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐              │
│  │   SendGrid  │  │   SignalR   │  │  HTTP/JSON  │              │
│  │   /SMTP     │  │   WebSocket │  │   REST API  │              │
│  └─────────────┘  └─────────────┘  └─────────────┘              │
│                                                                   │
│  ┌─────────────┐  ┌─────────────┐                               │
│  │    SLACK    │  │    TEAMS    │   (Phase 2)                   │
│  │    Bot      │  │     Bot     │                               │
│  └─────────────┘  └─────────────┘                               │
└──────────────────────────────────────────────────────────────────┘
```

### Delivery Node Implementation

```csharp
public class DeliveryNode : IGraphNode<SupervisorState>
{
    private readonly IEmailService _email;
    private readonly IHubContext<AgentStreamHub> _signalR;

    public async Task<NodeResult<SupervisorState>> ExecuteAsync(
        SupervisorState state,
        GraphContext ctx,
        CancellationToken ct)
    {
        var delivery = state.DeliveryChannel switch
        {
            DeliveryChannel.Email => DeliverViaEmailAsync(state, ct),
            DeliveryChannel.Dashboard => DeliverViaDashboardAsync(state, ct),
            DeliveryChannel.Api => DeliverViaApiAsync(state, ct),
            DeliveryChannel.Slack => DeliverViaSlackAsync(state, ct),
            _ => throw new NotSupportedException()
        };

        await delivery;

        return new NodeResult(state with {
            DeliveryStatus = DeliveryStatus.Sent,
            DeliveredAt = DateTimeOffset.UtcNow
        });
    }

    private async Task DeliverViaEmailAsync(SupervisorState state, CancellationToken ct)
    {
        var html = await RenderEmailTemplateAsync(state.IntegratedResult);

        await _email.SendAsync(new EmailMessage
        {
            To = state.RecipientEmail,
            Subject = $"Daily Analytics Snapshot - {state.SiteName}",
            HtmlBody = html,
            Attachments = state.Attachments  // PDF reports, CSV exports
        }, ct);
    }

    private async Task DeliverViaDashboardAsync(SupervisorState state, CancellationToken ct)
    {
        // Push to connected dashboard clients via SignalR
        await _signalR.Clients
            .Group($"tenant_{state.TenantId}_site_{state.SiteId}")
            .SendAsync("AgentResponse", new
            {
                RequestId = state.RequestId,
                Result = state.IntegratedResult,
                Timestamp = DateTimeOffset.UtcNow
            }, ct);
    }
}
```

### Email Templates

```csharp
public interface IEmailTemplateRenderer
{
    Task<string> RenderSnapshotEmailAsync(AnalyticsSnapshot snapshot);
    Task<string> RenderAlertEmailAsync(AlertNotification alert);
    Task<string> RenderWeeklyReportAsync(WeeklyReport report);
}

// Template: daily_snapshot.html
/*
<html>
<body>
  <h1>Daily Analytics Snapshot</h1>
  <h2>{{SiteName}} - {{Date}}</h2>

  <div class="metrics">
    <div class="metric">
      <span class="label">Green Fees</span>
      <span class="value">${{GreenFees}}</span>
      <span class="yoy {{YoYClass}}">{{YoYGreenFees}}%</span>
    </div>
    <!-- More metrics -->
  </div>

  <div class="narrative">
    {{AiNarrative}}
  </div>

  <div class="recommendations">
    <h3>Recommendations</h3>
    {{#each Recommendations}}
      <p>• {{this}}</p>
    {{/each}}
  </div>
</body>
</html>
*/
```

---

## Five-Layer Security Architecture

### Security Flow

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        5-LAYER SECURITY PIPELINE                             │
│                                                                              │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │ LAYER 1: OAuth 2.0 + JWT Validation                                  │    │
│  │                                                                      │    │
│  │  • Validate JWT signature & expiry                                   │    │
│  │  • Extract claims: tenant_id, site_ids[], role, agent_access[]       │    │
│  │  • Reject if token invalid or expired                                │    │
│  └──────────────────────────────┬──────────────────────────────────────┘    │
│                                 │                                            │
│  ┌──────────────────────────────▼──────────────────────────────────────┐    │
│  │ LAYER 2: LiteLLM Team-Based Access                                   │    │
│  │                                                                      │    │
│  │  • Resolve tenant → LiteLLM team                                     │    │
│  │  • Enforce team budget limits                                        │    │
│  │  • Apply rate limiting (RPM/TPM)                                     │    │
│  │  • Log all requests for audit                                        │    │
│  └──────────────────────────────┬──────────────────────────────────────┘    │
│                                 │                                            │
│  ┌──────────────────────────────▼──────────────────────────────────────┐    │
│  │ LAYER 3: LLM System Prompt Injection                                 │    │
│  │                                                                      │    │
│  │  • Inject TenantID/SiteID constraints into every prompt              │    │
│  │  • "ALL SQL queries MUST include WHERE TenantID={SiteId}"        │    │
│  │  • Defense-in-depth: even if app layer fails, LLM is constrained     │    │
│  └──────────────────────────────┬──────────────────────────────────────┘    │
│                                 │                                            │
│  ┌──────────────────────────────▼──────────────────────────────────────┐    │
│  │ LAYER 4: Database Connection Routing                                 │    │
│  │                                                                      │    │
│  │  • Dedicated DB per tenant (tenant_1_db, tenant_2_db)                │    │
│  │  • OR shared DB with tenant isolation                                │    │
│  │  • Connection string resolved from tenant context                    │    │
│  └──────────────────────────────┬──────────────────────────────────────┘    │
│                                 │                                            │
│  ┌──────────────────────────────▼──────────────────────────────────────┐    │
│  │ LAYER 5: SQL Server Row-Level Security (Backstop)                    │    │
│  │                                                                      │    │
│  │  • RLS policies on all tenant tables                                 │    │
│  │  • sp_set_session_context @TenantID, @SiteID                         │    │
│  │  • Even if ALL other layers fail, RLS prevents cross-tenant access   │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Implementation

```csharp
// Layer 1: JWT Validation Middleware
public class TenantAuthenticationMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var token = context.Request.Headers.Authorization
            .ToString().Replace("Bearer ", "");

        var principal = await _tokenValidator.ValidateAsync(token);

        if (principal == null)
        {
            context.Response.StatusCode = 401;
            return;
        }

        // Extract tenant context from claims
        var tenantContext = new TenantContext
        {
            TenantId = int.Parse(principal.FindFirst("tenant_id")?.Value),
            SiteIds = principal.FindFirst("site_ids")?.Value
                .Split(',').Select(int.Parse).ToArray(),
            Role = principal.FindFirst("role")?.Value,
            AgentAccess = principal.FindFirst("agent_access")?.Value
                .Split(',').ToArray()
        };

        context.Items["TenantContext"] = tenantContext;
        await next(context);
    }
}

// Layer 4 & 5: Database with RLS
public class DivaDbContext : DbContext
{
    private readonly TenantContext _tenant;

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        // Layer 4: Route to correct database
        var connectionString = _tenant.TenantId switch
        {
            1 => _config["Database:Tenant1"],
            2 => _config["Database:Tenant2"],
            _ => _config["Database:Shared"]
        };

        options.UseSqlServer(connectionString);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        // Layer 5: Set RLS context before any operation
        await Database.ExecuteSqlRawAsync(
            "EXEC sp_set_session_context @key=N'TenantId', @value={0};" +
            "EXEC sp_set_session_context @key=N'SiteId', @value={1};",
            _tenant.TenantId, _tenant.CurrentSiteId);

        return await base.SaveChangesAsync(ct);
    }
}

// SQL Server RLS Policy
/*
CREATE SECURITY POLICY TenantIsolationPolicy
ADD FILTER PREDICATE dbo.fn_TenantAccessPredicate(TenantID)
    ON dbo.DailyMetrics,
ADD FILTER PREDICATE dbo.fn_TenantAccessPredicate(TenantID)
    ON dbo.ReservationBookings,
ADD FILTER PREDICATE dbo.fn_TenantAccessPredicate(TenantID)
    ON dbo.FBOrders
WITH (STATE = ON);

CREATE FUNCTION dbo.fn_TenantAccessPredicate(@TenantID int)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN SELECT 1 AS AccessResult
WHERE @TenantID = CAST(SESSION_CONTEXT(N'SiteId') AS int);
*/
```

---

## Multi-Tenant Architecture (TenantID vs SiteID)

### Hierarchy

```
┌─────────────────────────────────────────────────────────────────┐
│                      TENANT (Organization)                       │
│                      TenantID = 1                                │
│                      "Acme Corporation"                          │
│                                                                  │
│  ┌─────────────────────────────────────────────────────────────┐│
│  │                         SITES (Properties)                   ││
│  │                                                              ││
│  │  ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐││
│  │  │ SiteID  │ │ SiteID  │ │ SiteID  │ │ SiteID  │ │ SiteID  │││
│  │  │   = 1   │ │   = 2   │ │   = 3   │ │   = 4   │ │   = 5   │││
│  │  │ North   │ │ South   │ │  East   │ │  West   │ │Central  │││
│  │  │ Campus  │ │ Campus  │ │ Campus  │ │ Campus  │ │ Campus  │││
│  │  └─────────┘ └─────────┘ └─────────┘ └─────────┘ └─────────┘││
│  │                                                              ││
│  └─────────────────────────────────────────────────────────────┘│
│                                                                  │
│  ┌─────────────────────────────────────────────────────────────┐│
│  │                          USERS                               ││
│  │                                                              ││
│  │  ┌───────────────┐  ┌───────────────┐  ┌───────────────┐    ││
│  │  │   Org Admin   │  │ GM (North C) │  │ GM (East Campus) │    ││
│  │  │ site_ids: ALL │  │ site_ids: [1] │  │ site_ids: [3] │    ││
│  │  └───────────────┘  └───────────────┘  └───────────────┘    ││
│  │                                                              ││
│  └─────────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────────┘
```

### Data Models

```csharp
public class Tenant
{
    public int TenantId { get; set; }
    public string Name { get; set; }  // "Acme Corporation"
    public string DatabaseConnection { get; set; }  // Dedicated or shared
    public string LiteLLMTeamId { get; set; }  // "team_acme"
    public decimal MonthlyBudget { get; set; }
    public List<Site> Sites { get; set; }
}

public class Site
{
    public int SiteId { get; set; }
    public int TenantId { get; set; }
    public string Name { get; set; }  // "East Campus"
    public string TimeZone { get; set; }
    public Tenant Tenant { get; set; }
}

public class TenantContext
{
    public int TenantId { get; set; }
    public string TenantName { get; set; }
    public int[] SiteIds { get; set; }  // Sites user can access
    public int CurrentSiteId { get; set; }  // Active site for this request
    public string Role { get; set; }  // "org_admin", "gm", "staff"
    public string[] AgentAccess { get; set; }  // Which agents user can invoke
    public string TeamApiKey { get; set; }  // LiteLLM team key
}

// JWT Claims structure
public class TenantJwtClaims
{
    [JsonPropertyName("tenant_id")]
    public int TenantId { get; set; }

    [JsonPropertyName("tenant_name")]
    public string TenantName { get; set; }

    [JsonPropertyName("site_ids")]
    public int[] SiteIds { get; set; }

    [JsonPropertyName("role")]
    public string Role { get; set; }

    [JsonPropertyName("agent_access")]
    public string[] AgentAccess { get; set; }  // ["Analytics", "ReservationBooking"]
}
```

### Site Scoping in Agents

```csharp
public class TaskDecomposerNode : IGraphNode<SupervisorState>
{
    public async Task<NodeResult<SupervisorState>> ExecuteAsync(
        SupervisorState state,
        GraphContext ctx,
        CancellationToken ct)
    {
        // Validate user has access to requested site
        if (!state.TenantContext.SiteIds.Contains(state.RequestedSiteId))
        {
            return new NodeResult(state with
            {
                Status = SupervisorStatus.Failed,
                Error = $"Access denied to site {state.RequestedSiteId}"
            });
        }

        // Inject site context into all sub-tasks
        var subTasks = await _llm.DecomposeTaskAsync(state.UserRequest);

        foreach (var task in subTasks)
        {
            task.SiteId = state.RequestedSiteId;
            task.TenantId = state.TenantContext.TenantId;
        }

        return new NodeResult(state with { SubTasks = subTasks });
    }
}
```

---

## Session Management

Multi-turn conversation support with session persistence.

### Session Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                      USER CONVERSATION                           │
│                                                                  │
│  User: "How did East Campus do yesterday?"                    │
│  Agent: "Revenue was $24,500, up 8% from last week..."          │
│                                                                  │
│  User: "Compare that to South Campus"                              │
│  Agent: [Understands "that" = East Campus yesterday]          │
│         "South Campus had $28,200, 15% higher than East Campus..."   │
│                                                                  │
│  User: "Show me the trend for both"                              │
│  Agent: [Remembers both sites from context]                      │
│         [Generates comparison chart]                             │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                      SESSION STORAGE                             │
│                                                                  │
│  Sessions Table (TenantID scoped, RLS enforced)                  │
│  ├── SessionId (GUID)                                            │
│  ├── TenantId                                                    │
│  ├── SiteId                                                      │
│  ├── UserId                                                      │
│  ├── CreatedAt                                                   │
│  ├── LastActivityAt                                              │
│  └── ExpiresAt                                                   │
│                                                                  │
│  SessionMessages Table                                           │
│  ├── MessageId                                                   │
│  ├── SessionId (FK)                                              │
│  ├── Role ("user" | "assistant")                                 │
│  ├── Content                                                     │
│  ├── Timestamp                                                   │
│  └── Metadata (JSON: tokens, agent, tools_used)                  │
└─────────────────────────────────────────────────────────────────┘
```

### Session Models

```csharp
public class Session
{
    public Guid SessionId { get; set; }
    public int TenantId { get; set; }
    public int SiteId { get; set; }
    public string UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastActivityAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public SessionStatus Status { get; set; }
    public List<SessionMessage> Messages { get; set; }
}

public class SessionMessage
{
    public Guid MessageId { get; set; }
    public Guid SessionId { get; set; }
    public string Role { get; set; }  // "user" or "assistant"
    public string Content { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public SessionMessageMetadata Metadata { get; set; }
}

public class SessionMessageMetadata
{
    public int TokensUsed { get; set; }
    public string AgentName { get; set; }
    public string[] ToolsUsed { get; set; }
    public TimeSpan ExecutionTime { get; set; }
}
```

### Session Service

```csharp
public interface ISessionService
{
    Task<Session> CreateSessionAsync(TenantContext tenant, CancellationToken ct);
    Task<Session?> GetSessionAsync(Guid sessionId, CancellationToken ct);
    Task<Session> AddMessageAsync(Guid sessionId, SessionMessage message, CancellationToken ct);
    Task<List<SessionMessage>> GetConversationHistoryAsync(Guid sessionId, int limit, CancellationToken ct);
    Task ExpireSessionAsync(Guid sessionId, CancellationToken ct);
}

public class SessionService : ISessionService
{
    public async Task<List<SessionMessage>> GetConversationHistoryAsync(
        Guid sessionId,
        int limit,
        CancellationToken ct)
    {
        return await _db.SessionMessages
            .Where(m => m.SessionId == sessionId)
            .OrderByDescending(m => m.Timestamp)
            .Take(limit)
            .OrderBy(m => m.Timestamp)  // Return in chronological order
            .ToListAsync(ct);
    }
}
```

### Supervisor Session Integration

```csharp
public class SupervisorAgent
{
    public async Task<SupervisorResult> InvokeAsync(SupervisorRequest request)
    {
        // Load or create session
        var session = request.SessionId.HasValue
            ? await _sessions.GetSessionAsync(request.SessionId.Value, ct)
            : await _sessions.CreateSessionAsync(request.TenantContext, ct);

        // Build conversation context from session history
        var history = await _sessions.GetConversationHistoryAsync(
            session.SessionId,
            limit: 10,  // Last 10 messages
            ct);

        // Add user message to session
        await _sessions.AddMessageAsync(session.SessionId, new SessionMessage
        {
            Role = "user",
            Content = request.TaskDescription,
            Timestamp = DateTimeOffset.UtcNow
        }, ct);

        // Execute with conversation context
        var state = new SupervisorState
        {
            SessionId = session.SessionId,
            ConversationHistory = history,
            UserRequest = request.TaskDescription,
            // ... other state
        };

        var result = await _graph.ExecuteAsync(state, ct);

        // Save assistant response to session
        await _sessions.AddMessageAsync(session.SessionId, new SessionMessage
        {
            Role = "assistant",
            Content = result.IntegratedResult.ToString(),
            Timestamp = DateTimeOffset.UtcNow,
            Metadata = new SessionMessageMetadata
            {
                TokensUsed = result.TotalTokensUsed,
                AgentName = "supervisor",
                ToolsUsed = result.ToolsInvoked
            }
        }, ct);

        return result;
    }
}
```

---

## Tenant-Specific Business Rules & Custom Prompts

Each tenant can have customized business rules that get injected into agent prompts.

### Business Rules Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    TENANT BUSINESS RULES INJECTION                           │
│                                                                              │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │                    TENANT CONFIGURATION (DB)                         │    │
│  │                                                                      │    │
│  │  TenantBusinessRules Table                                           │    │
│  │  ├── TenantId                                                        │    │
│  │  ├── AgentType (Analytics, Reservation, F&B, Supervisor)                 │    │
│  │  ├── RuleCategory (booking, pricing, reporting, terminology)         │    │
│  │  ├── RuleKey                                                         │    │
│  │  ├── RuleValue (JSON)                                                │    │
│  │  ├── PromptInjection (text to inject into prompts)                   │    │
│  │  └── IsActive                                                        │    │
│  │                                                                      │    │
│  │  TenantPromptOverrides Table                                         │    │
│  │  ├── TenantId                                                        │    │
│  │  ├── AgentType                                                       │    │
│  │  ├── PromptSection (planner, executor, aggregator)                   │    │
│  │  ├── CustomPromptText                                                │    │
│  │  └── MergeMode (append, prepend, replace)                            │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                 │                                            │
│                                 ▼                                            │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │                    PROMPT BUILDER                                    │    │
│  │                                                                      │    │
│  │  1. Load base prompt template (analytics/planner.v2.txt)             │    │
│  │  2. Load tenant business rules for this agent                        │    │
│  │  3. Load tenant prompt overrides                                     │    │
│  │  4. Inject security context (TenantID, SiteID)                       │    │
│  │  5. Merge all into final prompt                                      │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                 │                                            │
│                                 ▼                                            │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │                    FINAL AGENT PROMPT                                │    │
│  │                                                                      │    │
│  │  [Base Template]                                                     │    │
│  │  + [Tenant Business Rules]                                           │    │
│  │  + [Tenant Custom Instructions]                                      │    │
│  │  + [Security Constraints]                                            │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Business Rules Data Models

```csharp
public class TenantBusinessRule
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string AgentType { get; set; }  // "Analytics", "Reservation", "F&B", "*" (all)
    public string RuleCategory { get; set; }
    public string RuleKey { get; set; }
    public JsonDocument RuleValue { get; set; }
    public string? PromptInjection { get; set; }  // Text to inject into prompts
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class TenantPromptOverride
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string AgentType { get; set; }
    public string PromptSection { get; set; }  // "planner", "executor", "aggregator"
    public string CustomPromptText { get; set; }
    public PromptMergeMode MergeMode { get; set; }
    public int Priority { get; set; }  // For ordering multiple overrides
    public bool IsActive { get; set; }
}

public enum PromptMergeMode
{
    Append,    // Add after base template
    Prepend,   // Add before base template
    Replace,   // Replace entire section
    Insert     // Insert at specific marker
}
```

### Example: Tenant A vs Tenant B Business Rules

```json
// Acme Corporation - TenantId: 1
{
  "tenant_id": 1,
  "business_rules": [
    {
      "agent_type": "Booking",
      "rule_category": "booking",
      "rule_key": "cancellation_policy",
      "rule_value": {
        "free_cancellation_hours": 24,
        "late_cancellation_fee_percent": 50,
        "no_show_fee_percent": 100
      },
      "prompt_injection": "BOOKING RULES: Free cancellation up to 24 hours before appointment. Late cancellations (within 24h) incur 50% fee. No-shows are charged 100%."
    },
    {
      "agent_type": "Booking",
      "rule_category": "booking",
      "rule_key": "member_priority",
      "rule_value": {
        "member_booking_window_days": 14,
        "public_booking_window_days": 7
      },
      "prompt_injection": "MEMBER PRIORITY: Members can book 14 days in advance. Public bookings open 7 days in advance."
    },
    {
      "agent_type": "Analytics",
      "rule_category": "reporting",
      "rule_key": "kpi_definitions",
      "rule_value": {
        "revenue_channels": ["SALES", "SERVICES", "RETAIL", "SUBSCRIPTIONS"],
        "exclude_from_revenue": ["DEPOSITS"],
        "primary_kpi": "revenue_per_customer"
      },
      "prompt_injection": "KPI RULES: Revenue includes Sales, Services, Retail, and Subscriptions. DEPOSITS are NOT revenue. Primary KPI is revenue per customer."
    },
    {
      "agent_type": "Analytics",
      "rule_category": "terminology",
      "rule_key": "custom_terms",
      "rule_value": {
        "customer": "client",
        "order": "engagement",
        "product": "solution"
      },
      "prompt_injection": "TERMINOLOGY: Use 'client' instead of 'customer'. Use 'engagement' instead of 'order'. Use 'solution' instead of 'product'."
    },
    {
      "agent_type": "Analytics",
      "rule_category": "seasonal",
      "rule_key": "seasonal_factors",
      "rule_value": {
        "Q1": 0.8,
        "Q2": 1.0,
        "Q3": 0.9,
        "Q4": 1.3
      },
      "prompt_injection": "SEASONAL ADJUSTMENTS: Q1 = 0.8x baseline, Q2 = 1.0x, Q3 = 0.9x, Q4 (Holiday) = 1.3x. Apply these when forecasting or comparing periods."
    }
  ]
}

// Beta Industries - TenantId: 2
{
  "tenant_id": 2,
  "business_rules": [
    {
      "agent_type": "Booking",
      "rule_category": "booking",
      "rule_key": "cancellation_policy",
      "rule_value": {
        "free_cancellation_hours": 48,
        "late_cancellation_fee_percent": 25,
        "no_show_fee_percent": 100
      },
      "prompt_injection": "BOOKING RULES: Free cancellation up to 48 hours before appointment. Late cancellations incur 25% fee. No-shows charged 100%."
    },
    {
      "agent_type": "Booking",
      "rule_category": "booking",
      "rule_key": "pricing_tiers",
      "rule_value": {
        "peak": { "start": "09:00", "end": "17:00", "multiplier": 1.5 },
        "standard": { "start": "17:00", "end": "21:00", "multiplier": 1.0 },
        "off_peak": { "start": "06:00", "end": "09:00", "multiplier": 0.7 }
      },
      "prompt_injection": "PRICING TIERS: Peak hours (9AM-5PM) = 1.5x base rate. Standard (5PM-9PM) = standard rate. Off-peak (6AM-9AM) = 0.7x rate."
    },
    {
      "agent_type": "Analytics",
      "rule_category": "reporting",
      "rule_key": "kpi_definitions",
      "rule_value": {
        "revenue_channels": ["PRODUCTS", "SERVICES", "MAINTENANCE", "CONSULTING"],
        "primary_kpi": "average_revenue_per_user"
      },
      "prompt_injection": "KPI RULES: Revenue includes Products, Services, Maintenance, and Consulting. Primary KPI is Average Revenue Per User (ARPU)."
    },
    {
      "agent_type": "*",
      "rule_category": "branding",
      "rule_key": "communication_style",
      "rule_value": {
        "tone": "professional",
        "formality": "high",
        "sign_off": "Beta Industries Support Team"
      },
      "prompt_injection": "COMMUNICATION STYLE: Use professional, formal tone. Sign off reports as 'Beta Industries Support Team'."
    }
  ]
}
```

### Business Rules Service

```csharp
public interface ITenantBusinessRulesService
{
    Task<List<TenantBusinessRule>> GetRulesAsync(int tenantId, string agentType, CancellationToken ct);
    Task<string> GetPromptInjectionsAsync(int tenantId, string agentType, CancellationToken ct);
    Task<T?> GetRuleValueAsync<T>(int tenantId, string agentType, string ruleKey, CancellationToken ct);
    Task<List<TenantPromptOverride>> GetPromptOverridesAsync(int tenantId, string agentType, string section, CancellationToken ct);
}

public class TenantBusinessRulesService : ITenantBusinessRulesService
{
    private readonly IDivaDbContext _db;
    private readonly IMemoryCache _cache;

    public async Task<string> GetPromptInjectionsAsync(
        int tenantId,
        string agentType,
        CancellationToken ct)
    {
        var cacheKey = $"prompt_injections_{tenantId}_{agentType}";

        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);

            var rules = await _db.TenantBusinessRules
                .Where(r => r.TenantId == tenantId)
                .Where(r => r.AgentType == agentType || r.AgentType == "*")
                .Where(r => r.IsActive)
                .Where(r => r.PromptInjection != null)
                .OrderBy(r => r.RuleCategory)
                .ToListAsync(ct);

            if (!rules.Any())
                return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine("## Tenant-Specific Business Rules");
            sb.AppendLine();

            foreach (var rule in rules)
            {
                sb.AppendLine(rule.PromptInjection);
                sb.AppendLine();
            }

            return sb.ToString();
        });
    }

    public async Task<T?> GetRuleValueAsync<T>(
        int tenantId,
        string agentType,
        string ruleKey,
        CancellationToken ct)
    {
        var rule = await _db.TenantBusinessRules
            .Where(r => r.TenantId == tenantId)
            .Where(r => r.AgentType == agentType || r.AgentType == "*")
            .Where(r => r.RuleKey == ruleKey)
            .Where(r => r.IsActive)
            .FirstOrDefaultAsync(ct);

        if (rule?.RuleValue == null)
            return default;

        return rule.RuleValue.Deserialize<T>();
    }
}
```

### Tenant-Aware Prompt Builder

```csharp
public interface ITenantAwarePromptBuilder
{
    Task<string> BuildPromptAsync(
        TenantContext tenant,
        string agentType,
        string promptSection,
        Dictionary<string, object> variables,
        CancellationToken ct);
}

public class TenantAwarePromptBuilder : ITenantAwarePromptBuilder
{
    private readonly IPromptService _basePrompts;
    private readonly ITenantBusinessRulesService _rules;
    private readonly ISecurityPromptBuilder _security;

    public async Task<string> BuildPromptAsync(
        TenantContext tenant,
        string agentType,
        string promptSection,
        Dictionary<string, object> variables,
        CancellationToken ct)
    {
        // 1. Load base template
        var basePrompt = await _basePrompts.GetPromptAsync(agentType, promptSection);

        // 2. Get tenant business rules injections
        var businessRules = await _rules.GetPromptInjectionsAsync(
            tenant.TenantId, agentType, ct);

        // 3. Get tenant prompt overrides
        var overrides = await _rules.GetPromptOverridesAsync(
            tenant.TenantId, agentType, promptSection, ct);

        // 4. Build security context
        var securityContext = _security.BuildSecurityPrompt(tenant);

        // 5. Apply variable substitutions
        var prompt = RenderVariables(basePrompt, variables);

        // 6. Apply overrides based on merge mode
        foreach (var override_ in overrides.OrderBy(o => o.Priority))
        {
            prompt = override_.MergeMode switch
            {
                PromptMergeMode.Append => prompt + "\n\n" + override_.CustomPromptText,
                PromptMergeMode.Prepend => override_.CustomPromptText + "\n\n" + prompt,
                PromptMergeMode.Replace => override_.CustomPromptText,
                PromptMergeMode.Insert => InsertAtMarker(prompt, override_.CustomPromptText),
                _ => prompt
            };
        }

        // 7. Combine all sections
        return $"""
            {securityContext}

            {businessRules}

            {prompt}
            """;
    }
}
```

---

## Dynamic Business Rule Learning

The system can **learn and suggest new business rules** during agentic iterations based on:
- User feedback and corrections
- Observed patterns in data
- Successful/failed interactions
- Explicit user instructions

### Learning Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    DYNAMIC RULE LEARNING PIPELINE                            │
│                                                                              │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │                    DURING AGENT ITERATION                            │    │
│  │                                                                      │    │
│  │  User: "Actually, for our company, revenue should exclude refunds"  │    │
│  │                           │                                          │    │
│  │                           ▼                                          │    │
│  │  ┌──────────────────────────────────────────────────────────────┐   │    │
│  │  │ RULE EXTRACTION (via LLM)                                    │   │    │
│  │  │                                                              │   │    │
│  │  │ Agent detects: "This looks like a business rule definition"  │   │    │
│  │  │ Extracts: {                                                  │   │    │
│  │  │   "rule_category": "reporting",                              │   │    │
│  │  │   "rule_key": "revenue_exclusions",                          │   │    │
│  │  │   "rule_value": { "exclude": ["REFUNDS"] },                  │   │    │
│  │  │   "prompt_injection": "Exclude REFUNDS from revenue calcs"   │   │    │
│  │  │ }                                                            │   │    │
│  │  └──────────────────────────────────────────────────────────────┘   │    │
│  │                           │                                          │    │
│  │                           ▼                                          │    │
│  │  ┌──────────────────────────────────────────────────────────────┐   │    │
│  │  │ RULE SUGGESTION                                              │   │    │
│  │  │                                                              │   │    │
│  │  │ Agent: "I've noted this as a business rule. Would you like   │   │    │
│  │  │         me to save it for future conversations?"             │   │    │
│  │  │                                                              │   │    │
│  │  │ User: "Yes, save it"                                         │   │    │
│  │  └──────────────────────────────────────────────────────────────┘   │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                 │                                            │
│                                 ▼                                            │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │                    RULE APPROVAL WORKFLOW                            │    │
│  │                                                                      │    │
│  │  Option A: Auto-approve (user has permission)                       │    │
│  │            └── Rule saved immediately, active for this tenant       │    │
│  │                                                                      │    │
│  │  Option B: Pending approval (requires admin)                        │    │
│  │            └── Rule saved as "pending", admin notified              │    │
│  │            └── Admin reviews in Admin Portal                        │    │
│  │            └── Admin approves/rejects/modifies                      │    │
│  │                                                                      │    │
│  │  Option C: Session-only (temporary)                                 │    │
│  │            └── Rule active only for current session                 │    │
│  │            └── Not persisted to database                            │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                 │                                            │
│                                 ▼                                            │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │                    RULE APPLIED                                      │    │
│  │                                                                      │    │
│  │  • Added to TenantBusinessRules table (or session memory)           │    │
│  │  • Cache invalidated                                                │    │
│  │  • Next iteration uses updated rules                                │    │
│  │  • All future sessions benefit from learned rule                    │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Rule Learning Service

```csharp
public interface IRuleLearningService
{
    // Extract potential rules from conversation
    Task<List<SuggestedRule>> ExtractRulesAsync(
        ConversationContext conversation,
        CancellationToken ct);

    // Save a learned rule (with approval workflow)
    Task<RuleSaveResult> SaveLearnedRuleAsync(
        int tenantId,
        SuggestedRule rule,
        RuleApprovalMode approvalMode,
        CancellationToken ct);

    // Get pending rules for admin approval
    Task<List<PendingRule>> GetPendingRulesAsync(
        int tenantId,
        CancellationToken ct);

    // Apply session-scoped temporary rules
    Task ApplySessionRuleAsync(
        string sessionId,
        SuggestedRule rule,
        CancellationToken ct);
}

public class SuggestedRule
{
    public string AgentType { get; set; }
    public string RuleCategory { get; set; }
    public string RuleKey { get; set; }
    public JsonDocument RuleValue { get; set; }
    public string PromptInjection { get; set; }
    public string SourceConversation { get; set; }  // Reference to where rule was learned
    public float Confidence { get; set; }            // LLM confidence in extraction
    public DateTime SuggestedAt { get; set; }
}

public enum RuleApprovalMode
{
    AutoApprove,      // User has permission, save immediately
    RequireAdmin,     // Send to admin for approval
    SessionOnly       // Apply only to current session
}
```

### Rule Extraction via LLM

```csharp
public class LlmRuleExtractor : IRuleExtractor
{
    private readonly ILlmClient _llm;
    private readonly IPromptService _prompts;

    public async Task<List<SuggestedRule>> ExtractRulesAsync(
        ConversationContext conversation,
        CancellationToken ct)
    {
        var prompt = await _prompts.GetPromptAsync("system", "rule-extraction");

        var extractionPrompt = $"""
            {prompt}

            ## Conversation to Analyze
            {conversation.ToTranscript()}

            ## Instructions
            Identify any business rules the user is defining or correcting.
            Look for patterns like:
            - "For us, X should be Y"
            - "Actually, we don't count X in Y"
            - "Our policy is X"
            - "We always want to see X when Y"
            - Corrections to agent behavior
            - Clarifications about business logic

            Return JSON array of extracted rules, or empty array if none found.
            """;

        var response = await _llm.GenerateAsync(extractionPrompt, ct);
        return ParseRules(response);
    }
}
```

### Session-Scoped Rules (Working Memory)

```csharp
public class SessionRuleManager
{
    private readonly IDistributedCache _cache;

    public async Task AddSessionRuleAsync(
        string sessionId,
        SuggestedRule rule,
        CancellationToken ct)
    {
        var key = $"session_rules:{sessionId}";
        var existing = await GetSessionRulesAsync(sessionId, ct);
        existing.Add(rule);

        await _cache.SetAsync(key,
            JsonSerializer.SerializeToUtf8Bytes(existing),
            new DistributedCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromHours(24)
            }, ct);
    }

    public async Task<List<SuggestedRule>> GetSessionRulesAsync(
        string sessionId,
        CancellationToken ct)
    {
        var key = $"session_rules:{sessionId}";
        var data = await _cache.GetAsync(key, ct);

        if (data == null) return new List<SuggestedRule>();

        return JsonSerializer.Deserialize<List<SuggestedRule>>(data);
    }
}
```

### Updated Prompt Builder (with Dynamic Rules)

```csharp
public class TenantAwarePromptBuilder : ITenantAwarePromptBuilder
{
    private readonly ITenantBusinessRulesService _rules;
    private readonly ISessionRuleManager _sessionRules;

    public async Task<string> BuildPromptAsync(
        TenantContext tenant,
        string agentType,
        string promptSection,
        Dictionary<string, object> variables,
        CancellationToken ct)
    {
        // 1. Get static tenant rules (from database)
        var staticRules = await _rules.GetPromptInjectionsAsync(
            tenant.TenantId, agentType, ct);

        // 2. Get dynamic session rules (learned this session)
        var sessionRules = await _sessionRules.GetSessionRulesAsync(
            tenant.SessionId, ct);

        var dynamicRulesText = sessionRules.Any()
            ? FormatSessionRules(sessionRules)
            : string.Empty;

        // 3. Combine static + dynamic rules
        var allRules = $"""
            ## Tenant Business Rules (Configured)
            {staticRules}

            ## Session-Learned Rules (This Conversation)
            {dynamicRulesText}
            """;

        // ... rest of prompt building
    }
}
```

### Agent Integration for Rule Learning

```csharp
public class ReActAgentWithLearning : BaseReActAgent
{
    private readonly IRuleLearningService _learning;

    protected override async Task<AgentResponse> PostProcessAsync(
        AgentResponse response,
        ConversationContext conversation,
        CancellationToken ct)
    {
        // After each response, check if user defined any rules
        var suggestedRules = await _learning.ExtractRulesAsync(conversation, ct);

        if (suggestedRules.Any())
        {
            foreach (var rule in suggestedRules.Where(r => r.Confidence > 0.8))
            {
                // High-confidence rules: ask user to confirm
                response.FollowUpQuestions.Add(new FollowUpQuestion
                {
                    Type = "rule_confirmation",
                    Text = $"I noticed you mentioned a business rule: \"{rule.PromptInjection}\". " +
                           "Would you like me to remember this for future conversations?",
                    Options = new[] { "Yes, save it", "Just for this session", "No, ignore" },
                    Metadata = new { Rule = rule }
                });
            }
        }

        return response;
    }

    protected override async Task HandleUserFeedbackAsync(
        UserFeedback feedback,
        CancellationToken ct)
    {
        if (feedback.Type == "rule_confirmation")
        {
            var rule = feedback.Metadata.Rule as SuggestedRule;

            var approvalMode = feedback.SelectedOption switch
            {
                "Yes, save it" => RuleApprovalMode.AutoApprove,
                "Just for this session" => RuleApprovalMode.SessionOnly,
                _ => null
            };

            if (approvalMode.HasValue)
            {
                await _learning.SaveLearnedRuleAsync(
                    _tenantContext.TenantId,
                    rule,
                    approvalMode.Value,
                    ct);
            }
        }
    }
}
```

### Feedback-Based Learning

```csharp
public class FeedbackLearningService
{
    // Track when users correct the agent
    public async Task ProcessCorrectionAsync(
        int tenantId,
        string sessionId,
        AgentResponse originalResponse,
        string userCorrection,
        CancellationToken ct)
    {
        // Use LLM to understand the correction
        var analysis = await _llm.GenerateAsync($"""
            The agent provided this response:
            {originalResponse.Content}

            The user corrected it with:
            {userCorrection}

            Analyze what business rule or preference this implies.
            Return JSON with:
            - rule_type: "correction" | "preference" | "policy" | "none"
            - rule_description: what the rule is
            - confidence: 0.0-1.0
            - suggested_prompt_injection: text to add to prompts
            """, ct);

        var parsed = JsonSerializer.Deserialize<CorrectionAnalysis>(analysis);

        if (parsed.Confidence > 0.7 && parsed.RuleType != "none")
        {
            // Store as potential rule for admin review
            await _learning.SaveLearnedRuleAsync(tenantId, new SuggestedRule
            {
                RuleCategory = "learned_from_correction",
                RuleKey = $"correction_{DateTime.UtcNow:yyyyMMddHHmmss}",
                PromptInjection = parsed.SuggestedPromptInjection,
                Confidence = parsed.Confidence,
                SourceConversation = sessionId
            }, RuleApprovalMode.RequireAdmin, ct);
        }
    }
}
```

### Pattern-Based Rule Discovery

```csharp
public class PatternRuleDiscovery
{
    // Analyze historical interactions to discover patterns
    public async Task<List<SuggestedRule>> DiscoverPatternsAsync(
        int tenantId,
        CancellationToken ct)
    {
        // Get recent successful interactions
        var interactions = await _db.AgentSessions
            .Where(s => s.TenantId == tenantId)
            .Where(s => s.UserRating >= 4)  // Positive feedback
            .OrderByDescending(s => s.CreatedAt)
            .Take(100)
            .ToListAsync(ct);

        // Use LLM to find patterns
        var analysis = await _llm.GenerateAsync($"""
            Analyze these successful agent interactions for tenant {tenantId}:

            {FormatInteractions(interactions)}

            Identify recurring patterns that could become business rules:
            - Terminology preferences
            - Calculation methods
            - Reporting preferences
            - Data filtering patterns
            - Response format preferences

            Return JSON array of suggested rules with confidence scores.
            """, ct);

        return ParseDiscoveredRules(analysis);
    }
}
```

### Admin Portal: Pending Rules Review

```typescript
// admin-portal/src/pages/PendingRules.tsx
export function PendingRulesPage() {
  const { tenantId } = useAuth();
  const { data: pendingRules } = useQuery(['pendingRules', tenantId],
    () => api.getPendingRules(tenantId));

  const approveMutation = useMutation(
    (ruleId: string) => api.approveRule(tenantId, ruleId)
  );

  return (
    <div>
      <h1>Learned Rules Pending Approval</h1>

      {pendingRules?.map(rule => (
        <RuleCard key={rule.id}>
          <RuleHeader>
            <Badge>{rule.ruleCategory}</Badge>
            <Confidence value={rule.confidence} />
          </RuleHeader>

          <RuleContent>
            <Label>Prompt Injection:</Label>
            <Code>{rule.promptInjection}</Code>

            <Label>Learned From:</Label>
            <ConversationLink sessionId={rule.sourceConversation} />
          </RuleContent>

          <RuleActions>
            <Button onClick={() => approveMutation.mutate(rule.id)}>
              Approve
            </Button>
            <Button variant="secondary" onClick={() => editRule(rule)}>
              Edit & Approve
            </Button>
            <Button variant="danger" onClick={() => rejectRule(rule.id)}>
              Reject
            </Button>
          </RuleActions>
        </RuleCard>
      ))}
    </div>
  );
}
```

### Updated Database Schema

> **Note:** See the "Database Provider Abstraction" section for full SQLite (default) and SQL Server schemas.

```sql
-- SQL Server syntax (for enterprise deployments)
-- For SQLite schema, see "Database Provider Abstraction" section

CREATE TABLE LearnedRules (
    Id INT IDENTITY PRIMARY KEY,
    TenantId INT NOT NULL,
    AgentType NVARCHAR(50),
    RuleCategory NVARCHAR(100),
    RuleKey NVARCHAR(200),
    RuleValue NVARCHAR(MAX),  -- JSON
    PromptInjection NVARCHAR(MAX),
    Confidence FLOAT,
    Status NVARCHAR(20) DEFAULT 'pending',  -- pending, approved, rejected
    SourceSessionId NVARCHAR(100),
    SourceConversation NVARCHAR(MAX),
    LearnedAt DATETIME2 DEFAULT GETUTCDATE(),
    ReviewedAt DATETIME2,
    ReviewedBy NVARCHAR(100),
    ReviewNotes NVARCHAR(MAX)
);

CREATE TABLE UserCorrections (
    Id INT IDENTITY PRIMARY KEY,
    TenantId INT NOT NULL,
    SessionId NVARCHAR(100),
    OriginalResponse NVARCHAR(MAX),
    UserCorrection NVARCHAR(MAX),
    ExtractedRule NVARCHAR(MAX),  -- JSON
    ProcessedAt DATETIME2 DEFAULT GETUTCDATE()
);
```

---

### Agent Integration

```csharp
public class PlannerNode : IGraphNode<WorkerAgentState>
{
    private readonly ITenantAwarePromptBuilder _promptBuilder;
    private readonly ILlmClient _llm;

    public async Task<NodeResult<WorkerAgentState>> ExecuteAsync(
        WorkerAgentState state,
        GraphContext ctx,
        CancellationToken ct)
    {
        // Build tenant-specific prompt with business rules
        var prompt = await _promptBuilder.BuildPromptAsync(
            state.TenantContext,
            agentType: "Analytics",
            promptSection: "planner",
            variables: new Dictionary<string, object>
            {
                ["TaskDescription"] = state.TaskDescription,
                ["PropertyId"] = state.PropertyId,
                ["ToolDefinitions"] = state.AvailableTools
            },
            ct);

        // LLM call with fully customized prompt
        var response = await _llm.GenerateAsync(prompt, ct);

        var todoList = ParseTodoList(response);
        return new NodeResult(state with { TodoList = todoList });
    }
}
```

### Site-Level Overrides (Optional)

For cases where individual sites within a tenant need different rules:

```csharp
public class SiteBusinessRule
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int SiteId { get; set; }
    public string AgentType { get; set; }
    public string RuleKey { get; set; }
    public JsonDocument RuleValue { get; set; }
    public string? PromptInjection { get; set; }
    public bool IsActive { get; set; }
}

// Site rules override tenant rules when present
public async Task<string> GetPromptInjectionsAsync(
    int tenantId,
    int siteId,
    string agentType,
    CancellationToken ct)
{
    // 1. Get tenant-level rules
    var tenantRules = await GetTenantRulesAsync(tenantId, agentType, ct);

    // 2. Get site-level overrides
    var siteRules = await GetSiteRulesAsync(tenantId, siteId, agentType, ct);

    // 3. Merge (site rules override tenant rules for same keys)
    var merged = MergeRules(tenantRules, siteRules);

    return FormatAsPromptInjections(merged);
}
```

### Admin API for Managing Rules

```csharp
[ApiController]
[Route("api/admin/business-rules")]
[Authorize(Policy = "TenantAdmin")]
public class BusinessRulesController : ControllerBase
{
    [HttpGet("{tenantId}")]
    public async Task<IActionResult> GetRules(int tenantId)
    {
        var rules = await _service.GetAllRulesAsync(tenantId);
        return Ok(rules);
    }

    [HttpPost("{tenantId}")]
    public async Task<IActionResult> CreateRule(int tenantId, [FromBody] CreateRuleRequest request)
    {
        var rule = await _service.CreateRuleAsync(tenantId, request);
        return Created($"/api/admin/business-rules/{tenantId}/{rule.Id}", rule);
    }

    [HttpPut("{tenantId}/{ruleId}")]
    public async Task<IActionResult> UpdateRule(int tenantId, int ruleId, [FromBody] UpdateRuleRequest request)
    {
        await _service.UpdateRuleAsync(tenantId, ruleId, request);

        // Invalidate cache
        _cache.Remove($"prompt_injections_{tenantId}_*");

        return NoContent();
    }

    [HttpPost("{tenantId}/prompt-overrides")]
    public async Task<IActionResult> CreatePromptOverride(int tenantId, [FromBody] CreatePromptOverrideRequest request)
    {
        var override_ = await _service.CreatePromptOverrideAsync(tenantId, request);
        return Created($"/api/admin/business-rules/{tenantId}/prompt-overrides/{override_.Id}", override_);
    }
}
```

### Updated Solution Structure

```
Diva/
├── src/
│   ├── Diva.Infrastructure/
│   │   ├── TenantRules/                      # NEW: Business rules
│   │   │   ├── ITenantBusinessRulesService.cs
│   │   │   ├── TenantBusinessRulesService.cs
│   │   │   ├── TenantBusinessRule.cs
│   │   │   ├── TenantPromptOverride.cs
│   │   │   └── SiteBusinessRule.cs
│   │   ├── Prompts/
│   │   │   ├── IPromptService.cs
│   │   │   ├── PromptService.cs
│   │   │   ├── ITenantAwarePromptBuilder.cs  # NEW
│   │   │   ├── TenantAwarePromptBuilder.cs   # NEW
│   │   │   └── FilePromptStore.cs
```

---

## Prompt Management

Centralized prompt versioning and management.

### Prompt Storage Structure

```
prompts/
├── supervisor/
│   ├── decomposer.v1.txt
│   ├── decomposer.v2.txt      # Latest
│   ├── integrator.v1.txt
│   └── integrator.v2.txt
├── analytics/
│   ├── planner.v1.txt
│   ├── planner.v2.txt
│   └── text-to-sql.v1.txt
├── Reservation/
│   ├── planner.v1.txt
│   └── booking-rules.v1.txt
└── shared/
    ├── security-constraints.txt
    └── output-format.txt
```

### Prompt Management Service

```csharp
public interface IPromptService
{
    Task<string> GetPromptAsync(string category, string name, string? version = null);
    Task<string> RenderPromptAsync(string category, string name, Dictionary<string, object> variables);
    Task<PromptMetadata> GetPromptMetadataAsync(string category, string name);
}

public class PromptService : IPromptService
{
    private readonly IPromptStore _store;  // File, DB, or LiteLLM Prompt Studio

    public async Task<string> GetPromptAsync(string category, string name, string? version = null)
    {
        // If no version specified, get latest
        version ??= await _store.GetLatestVersionAsync(category, name);

        return await _store.GetPromptContentAsync(category, name, version);
    }

    public async Task<string> RenderPromptAsync(
        string category,
        string name,
        Dictionary<string, object> variables)
    {
        var template = await GetPromptAsync(category, name);

        // Simple variable substitution
        foreach (var (key, value) in variables)
        {
            template = template.Replace($"{{{{{key}}}}}", value?.ToString() ?? "");
        }

        return template;
    }
}
```

### Prompt Templates

```
# prompts/analytics/planner.v2.txt

You are the internal planner for the Analytics Agent in the Diva enterprise platform.

## Security Context
{{SecurityConstraints}}

## Your Task
Given a task description, produce a JSON to-do list of MCP tool calls needed to complete the task.

## Available MCP Tools
{{ToolDefinitions}}

## Rules
1. Always include property_id={{PropertyId}} in every tool call
2. Mark items as parallel (depends_on: []) when they don't need prior results
3. Use "{{from_step_N}}" placeholders for dependent parameters
4. Prefer parallel execution — only add dependencies when genuinely needed
5. Always validate data schema first if the task involves ad-hoc queries
6. Maximum 10 items per to-do list
7. For error-prone operations, add a verification step

## Task Description
{{TaskDescription}}

## Output
Respond with ONLY the JSON to-do list. No explanation.
```

### LiteLLM Prompt Studio Integration

```csharp
// Using LiteLLM's Prompt Studio for version control
public class LiteLLMPromptStore : IPromptStore
{
    private readonly HttpClient _http;

    public async Task<string> GetPromptContentAsync(
        string category,
        string name,
        string version)
    {
        var response = await _http.GetAsync(
            $"/prompt/get?id={category}/{name}&version={version}");

        var prompt = await response.Content.ReadFromJsonAsync<LiteLLMPrompt>();
        return prompt.Content;
    }
}

// LiteLLM Prompt Studio API
// POST /prompt/new - Create new prompt
// GET /prompt/get?id=xxx&version=v1 - Get specific version
// GET /prompt/versions?id=xxx - List all versions
// POST /prompt/set-default - Set default version
```

---

## RunQuery Tool (Text-to-SQL)

Ad-hoc SQL query generation via LLM.

### Tool Definition

```csharp
[McpTool("RunQuery")]
public class RunQueryTool : IMcpTool
{
    private readonly ILlmClient _llm;
    private readonly IDivaDbContext _db;
    private readonly IPromptService _prompts;

    public async Task<ToolResult> ExecuteAsync(RunQueryParams parameters, CancellationToken ct)
    {
        // 1. Get schema context
        var schema = await GetRelevantSchemaAsync(parameters.Query, ct);

        // 2. Generate SQL via LLM
        var sqlPrompt = await _prompts.RenderPromptAsync("analytics", "text-to-sql", new()
        {
            ["Schema"] = schema,
            ["UserQuery"] = parameters.Query,
            ["PropertyId"] = parameters.PropertyId,
            ["DateContext"] = DateTime.Today.ToString("yyyy-MM-dd")
        });

        var sqlResponse = await _llm.GenerateAsync(sqlPrompt, ct);
        var sql = ExtractSqlFromResponse(sqlResponse);

        // 3. Validate SQL security
        ValidateSqlSecurity(sql, parameters.PropertyId);

        // 4. Execute query
        var results = await _db.Database
            .SqlQueryRaw<dynamic>(sql)
            .ToListAsync(ct);

        return new ToolResult
        {
            Success = true,
            Data = new
            {
                Query = sql,
                RowCount = results.Count,
                Results = results.Take(1000)  // Limit response size
            }
        };
    }

    private void ValidateSqlSecurity(string sql, int propertyId)
    {
        // Must contain TenantID filter
        if (!sql.Contains($"TenantID = {propertyId}") &&
            !sql.Contains($"TenantID={propertyId}"))
        {
            throw new SecurityException(
                "Generated SQL must filter by TenantID");
        }

        // No DML operations allowed
        var forbidden = new[] { "INSERT", "UPDATE", "DELETE", "DROP", "ALTER", "TRUNCATE" };
        if (forbidden.Any(f => sql.Contains(f, StringComparison.OrdinalIgnoreCase)))
        {
            throw new SecurityException(
                "DML operations are not allowed");
        }
    }
}
```

### Text-to-SQL Prompt

```
# prompts/analytics/text-to-sql.v1.txt

You are a SQL expert generating queries for a multi-tenant enterprise database.

## CRITICAL SECURITY REQUIREMENTS
- ALL queries MUST include: WHERE TenantID = {{PropertyId}}
- You can ONLY generate SELECT statements
- No INSERT, UPDATE, DELETE, DROP, ALTER, or TRUNCATE
- No access to system tables or stored procedures

## Database Schema
{{Schema}}

## Date Context
Today is {{DateContext}}. Use this for relative date references like "yesterday", "last week", etc.

## User Question
{{UserQuery}}

## Output
Generate a single SQL SELECT statement. Include only the SQL, no explanation.

```sql
SELECT ...
```
```

### Analytics MCP Tools (Updated)

```csharp
// Updated tool list including RunQuery
public class AnalyticsMcpServer
{
    [McpTool("GetDataSchema")]
    public Task<ToolResult> GetDataSchema(GetDataSchemaParams p);

    [McpTool("GetMetricBreakdown")]
    public Task<ToolResult> GetMetricBreakdown(GetMetricBreakdownParams p);

    [McpTool("GetYoY")]
    public Task<ToolResult> GetYoY(GetYoYParams p);

    [McpTool("GenSnapshot")]
    public Task<ToolResult> GenSnapshot(GenSnapshotParams p);

    [McpTool("GetData")]
    public Task<ToolResult> GetData(GetDataParams p);  // Pre-defined reports

    [McpTool("RunQuery")]
    public Task<ToolResult> RunQuery(RunQueryParams p);  // Ad-hoc Text-to-SQL
}
```

---

## Observability

Comprehensive logging, monitoring, and alerting.

### Observability Stack

```
┌─────────────────────────────────────────────────────────────────┐
│                    OBSERVABILITY STACK                           │
│                                                                  │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐              │
│  │   LOGGING   │  │   METRICS   │  │   TRACING   │              │
│  │             │  │             │  │             │              │
│  │ Serilog +   │  │ Prometheus  │  │ OpenTele-   │              │
│  │ Seq/ELK     │  │ + Grafana   │  │ metry       │              │
│  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘              │
│         │                │                │                      │
│         └────────────────┼────────────────┘                      │
│                          │                                       │
│                          ▼                                       │
│              ┌───────────────────────┐                          │
│              │   Azure Application   │                          │
│              │      Insights         │                          │
│              │   (or self-hosted)    │                          │
│              └───────────────────────┘                          │
│                          │                                       │
│                          ▼                                       │
│              ┌───────────────────────┐                          │
│              │       ALERTING        │                          │
│              │  PagerDuty / Slack    │                          │
│              └───────────────────────┘                          │
└─────────────────────────────────────────────────────────────────┘
```

### Structured Logging

```csharp
// Serilog configuration
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "Diva")
    .Enrich.WithProperty("Environment", Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"))
    .WriteTo.Console(new JsonFormatter())
    .WriteTo.Seq("http://seq:5341")
    .WriteTo.ApplicationInsights(TelemetryConfiguration.Active, TelemetryConverter.Traces)
    .CreateLogger();

// Agent execution logging
public class AgentLoggingMiddleware
{
    public async Task<T> ExecuteWithLoggingAsync<T>(
        string agentName,
        string taskDescription,
        TenantContext tenant,
        Func<Task<T>> action)
    {
        var correlationId = Guid.NewGuid().ToString();

        using var _ = LogContext.PushProperty("CorrelationId", correlationId);
        using var __ = LogContext.PushProperty("AgentName", agentName);
        using var ___ = LogContext.PushProperty("TenantId", tenant.TenantId);
        using var ____ = LogContext.PushProperty("SiteId", tenant.CurrentSiteId);

        var sw = Stopwatch.StartNew();

        _logger.Information(
            "Agent {AgentName} starting task: {TaskDescription}",
            agentName, taskDescription);

        try
        {
            var result = await action();

            sw.Stop();
            _logger.Information(
                "Agent {AgentName} completed in {ElapsedMs}ms",
                agentName, sw.ElapsedMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.Error(ex,
                "Agent {AgentName} failed after {ElapsedMs}ms: {ErrorMessage}",
                agentName, sw.ElapsedMilliseconds, ex.Message);
            throw;
        }
    }
}
```

### Metrics

```csharp
// Prometheus metrics
public static class AgentMetrics
{
    public static readonly Counter TasksTotal = Metrics.CreateCounter(
        "agent_tasks_total",
        "Total agent tasks executed",
        new CounterConfiguration
        {
            LabelNames = new[] { "agent", "tenant", "site", "status" }
        });

    public static readonly Histogram TaskDuration = Metrics.CreateHistogram(
        "agent_task_duration_seconds",
        "Agent task execution duration",
        new HistogramConfiguration
        {
            LabelNames = new[] { "agent", "tenant" },
            Buckets = new[] { 0.1, 0.5, 1, 2, 5, 10, 30, 60 }
        });

    public static readonly Gauge ActiveTasks = Metrics.CreateGauge(
        "agent_active_tasks",
        "Currently executing agent tasks",
        new GaugeConfiguration
        {
            LabelNames = new[] { "agent" }
        });

    public static readonly Counter LlmTokensUsed = Metrics.CreateCounter(
        "llm_tokens_total",
        "Total LLM tokens used",
        new CounterConfiguration
        {
            LabelNames = new[] { "tenant", "agent", "model" }
        });

    public static readonly Counter McpToolCalls = Metrics.CreateCounter(
        "mcp_tool_calls_total",
        "Total MCP tool invocations",
        new CounterConfiguration
        {
            LabelNames = new[] { "tool", "agent", "status" }
        });
}
```

### Distributed Tracing

```csharp
// OpenTelemetry configuration
services.AddOpenTelemetry()
    .WithTracing(builder =>
    {
        builder
            .SetResourceBuilder(ResourceBuilder.CreateDefault()
                .AddService("Diva"))
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddSqlClientInstrumentation()
            .AddSource("Diva.Agents")  // Custom agent spans
            .AddOtlpExporter(opts =>
            {
                opts.Endpoint = new Uri("http://otel-collector:4317");
            });
    });

// Creating spans for agent execution
public class TracingAgentDecorator : IAgentGraph
{
    private static readonly ActivitySource Source = new("Diva.Agents");

    public async Task<TState> ExecuteAsync<TState>(TState state, CancellationToken ct)
    {
        using var activity = Source.StartActivity(
            $"Agent.{_agentName}.Execute",
            ActivityKind.Internal);

        activity?.SetTag("tenant.id", state.TenantId);
        activity?.SetTag("site.id", state.SiteId);
        activity?.SetTag("task.description", state.TaskDescription);

        try
        {
            var result = await _inner.ExecuteAsync(state, ct);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return result;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}
```

### Health Checks

```csharp
services.AddHealthChecks()
    .AddSqlServer(
        configuration.GetConnectionString("TeiDb"),
        name: "database",
        tags: new[] { "db", "sql" })
    .AddUrlGroup(
        new Uri(configuration["LiteLLM:BaseUrl"] + "/health"),
        name: "litellm",
        tags: new[] { "llm" })
    .AddCheck<AgentRegistryHealthCheck>("agent-registry")
    .AddCheck<McpServerHealthCheck>("mcp-server");

// Kubernetes-ready endpoints
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false  // Just checks if app is running
});
```

---

## Database Provider Abstraction

Diva uses **SQLite as the default database** for easy setup and local development, with **optional SQL Server support** for enterprise deployments.

### Provider Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                     DATABASE PROVIDER ABSTRACTION                            │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  ┌──────────────────────────────────────────────────────────────────────┐   │
│  │                        IDatabaseProviderFactory                       │   │
│  │                                                                       │   │
│  │   CreateDbContext(TenantContext) → DivaDbContext                     │   │
│  │   ApplyMigrations()                                                  │   │
│  │   GetTenantIsolationStrategy() → IsolationStrategy                   │   │
│  └──────────────────────────────────────────────────────────────────────┘   │
│                            │                                                 │
│              ┌─────────────┴─────────────┐                                  │
│              │                           │                                  │
│              ▼                           ▼                                  │
│  ┌─────────────────────┐    ┌─────────────────────┐                        │
│  │  SQLiteProvider     │    │  SqlServerProvider  │                        │
│  │  (Default)          │    │  (Enterprise)       │                        │
│  │                     │    │                     │                        │
│  │  • Single file DB   │    │  • Connection pool  │                        │
│  │  • App-level RLS    │    │  • Native RLS       │                        │
│  │  • No server needed │    │  • High performance │                        │
│  │  • Quick setup      │    │  • Production-ready │                        │
│  └─────────────────────┘    └─────────────────────┘                        │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Database Options Configuration

```csharp
namespace Diva.Infrastructure.Data;

public class DatabaseOptions
{
    public string Provider { get; set; } = "SQLite";  // "SQLite" or "SqlServer"

    public SQLiteOptions SQLite { get; set; } = new();
    public SqlServerOptions SqlServer { get; set; } = new();
}

public class SQLiteOptions
{
    public string ConnectionString { get; set; } = "Data Source=diva.db";
    public string DataDirectory { get; set; } = "./data";  // For multi-file setup
}

public class SqlServerOptions
{
    public string ConnectionString { get; set; }
    public bool UseRls { get; set; } = true;  // Row-Level Security
    public bool UseConnectionPerTenant { get; set; } = false;  // Dedicated DBs
}
```

### Database Provider Factory

```csharp
namespace Diva.Infrastructure.Data;

public interface IDatabaseProviderFactory
{
    DivaDbContext CreateDbContext(TenantContext? tenant = null);
    Task ApplyMigrationsAsync(CancellationToken ct = default);
    IsolationStrategy GetIsolationStrategy();
}

public class DatabaseProviderFactory : IDatabaseProviderFactory
{
    private readonly DatabaseOptions _options;
    private readonly IServiceProvider _services;

    public DatabaseProviderFactory(
        IOptions<DatabaseOptions> options,
        IServiceProvider services)
    {
        _options = options.Value;
        _services = services;
    }

    public DivaDbContext CreateDbContext(TenantContext? tenant = null)
    {
        var optionsBuilder = new DbContextOptionsBuilder<DivaDbContext>();

        switch (_options.Provider.ToLowerInvariant())
        {
            case "sqlite":
                ConfigureSQLite(optionsBuilder);
                break;

            case "sqlserver":
                ConfigureSqlServer(optionsBuilder, tenant);
                break;

            default:
                throw new NotSupportedException(
                    $"Database provider '{_options.Provider}' is not supported. " +
                    "Use 'SQLite' or 'SqlServer'.");
        }

        return new DivaDbContext(optionsBuilder.Options, tenant);
    }

    private void ConfigureSQLite(DbContextOptionsBuilder builder)
    {
        builder.UseSqlite(_options.SQLite.ConnectionString);
    }

    private void ConfigureSqlServer(DbContextOptionsBuilder builder, TenantContext? tenant)
    {
        var connectionString = _options.SqlServer.ConnectionString;

        // Optional: per-tenant database
        if (_options.SqlServer.UseConnectionPerTenant && tenant != null)
        {
            connectionString = connectionString.Replace(
                "Database=Diva",
                $"Database=Diva_Tenant{tenant.TenantId}");
        }

        builder.UseSqlServer(connectionString);
    }

    public IsolationStrategy GetIsolationStrategy()
    {
        return _options.Provider.ToLowerInvariant() switch
        {
            "sqlite" => IsolationStrategy.ApplicationLevel,
            "sqlserver" when _options.SqlServer.UseRls => IsolationStrategy.RowLevelSecurity,
            "sqlserver" => IsolationStrategy.ApplicationLevel,
            _ => IsolationStrategy.ApplicationLevel
        };
    }
}

public enum IsolationStrategy
{
    ApplicationLevel,    // WHERE TenantId = @TenantId in queries
    RowLevelSecurity,    // SQL Server RLS policies
    DatabasePerTenant    // Separate databases
}
```

### DivaDbContext with Provider-Agnostic Design

```csharp
namespace Diva.Infrastructure.Data;

public class DivaDbContext : DbContext
{
    private readonly TenantContext? _tenant;
    private readonly IsolationStrategy _isolation;

    public DivaDbContext(
        DbContextOptions<DivaDbContext> options,
        TenantContext? tenant = null,
        IsolationStrategy isolation = IsolationStrategy.ApplicationLevel)
        : base(options)
    {
        _tenant = tenant;
        _isolation = isolation;
    }

    public DbSet<TenantEntity> Tenants { get; set; }
    public DbSet<TenantBusinessRuleEntity> BusinessRules { get; set; }
    public DbSet<TenantPromptOverrideEntity> PromptOverrides { get; set; }
    public DbSet<AgentSessionEntity> Sessions { get; set; }
    public DbSet<AgentDefinitionEntity> AgentDefinitions { get; set; }
    public DbSet<LearnedRuleEntity> LearnedRules { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply global tenant filter for application-level isolation
        if (_isolation == IsolationStrategy.ApplicationLevel)
        {
            modelBuilder.Entity<TenantBusinessRuleEntity>()
                .HasQueryFilter(e => _tenant == null || e.TenantId == _tenant.TenantId);

            modelBuilder.Entity<TenantPromptOverrideEntity>()
                .HasQueryFilter(e => _tenant == null || e.TenantId == _tenant.TenantId);

            modelBuilder.Entity<AgentSessionEntity>()
                .HasQueryFilter(e => _tenant == null || e.TenantId == _tenant.TenantId);

            modelBuilder.Entity<AgentDefinitionEntity>()
                .HasQueryFilter(e => _tenant == null || e.TenantId == _tenant.TenantId);

            modelBuilder.Entity<LearnedRuleEntity>()
                .HasQueryFilter(e => _tenant == null || e.TenantId == _tenant.TenantId);
        }

        // Provider-agnostic configurations
        ConfigureEntities(modelBuilder);
    }

    private void ConfigureEntities(ModelBuilder modelBuilder)
    {
        // Use TEXT for JSON columns (works for both SQLite and SqlServer)
        modelBuilder.Entity<TenantBusinessRuleEntity>()
            .Property(e => e.RuleValue)
            .HasColumnType("TEXT");

        modelBuilder.Entity<AgentDefinitionEntity>()
            .Property(e => e.Capabilities)
            .HasColumnType("TEXT");

        modelBuilder.Entity<AgentDefinitionEntity>()
            .Property(e => e.ToolBindings)
            .HasColumnType("TEXT");
    }

    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        // For SQL Server with RLS, set session context
        if (_isolation == IsolationStrategy.RowLevelSecurity && _tenant != null)
        {
            await Database.ExecuteSqlRawAsync(
                "EXEC sp_set_session_context @key=N'TenantId', @value={0}",
                _tenant.TenantId);
        }

        // Auto-set TenantId on new entities
        foreach (var entry in ChangeTracker.Entries<ITenantEntity>()
            .Where(e => e.State == EntityState.Added))
        {
            entry.Entity.TenantId = _tenant?.TenantId ?? 0;
        }

        return await base.SaveChangesAsync(ct);
    }
}
```

### Service Registration

```csharp
// In Program.cs or Startup.cs
public static IServiceCollection AddDivaDatabase(
    this IServiceCollection services,
    IConfiguration configuration)
{
    services.Configure<DatabaseOptions>(
        configuration.GetSection("Database"));

    services.AddSingleton<IDatabaseProviderFactory, DatabaseProviderFactory>();

    // Register DbContext with factory pattern
    services.AddScoped(sp =>
    {
        var factory = sp.GetRequiredService<IDatabaseProviderFactory>();
        var httpContext = sp.GetService<IHttpContextAccessor>()?.HttpContext;
        var tenant = httpContext?.Items["TenantContext"] as TenantContext;
        return factory.CreateDbContext(tenant);
    });

    return services;
}
```

### SQLite Schema (Default)

```sql
-- SQLite-compatible schema (no RLS, uses EF Core query filters)

CREATE TABLE Tenants (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    DisplayName TEXT,
    IsActive INTEGER DEFAULT 1,
    CreatedAt TEXT DEFAULT (datetime('now')),
    Settings TEXT  -- JSON
);

CREATE TABLE TenantBusinessRules (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    TenantId INTEGER NOT NULL,
    AgentType TEXT,
    RuleCategory TEXT NOT NULL,
    RuleKey TEXT NOT NULL,
    RuleValue TEXT,  -- JSON
    IsActive INTEGER DEFAULT 1,
    CreatedAt TEXT DEFAULT (datetime('now')),
    UpdatedAt TEXT,
    FOREIGN KEY (TenantId) REFERENCES Tenants(Id)
);

CREATE TABLE AgentDefinitions (
    Id TEXT PRIMARY KEY,  -- GUID as TEXT
    TenantId INTEGER NOT NULL,
    Name TEXT NOT NULL,
    DisplayName TEXT,
    Description TEXT,
    AgentType TEXT NOT NULL,
    SystemPrompt TEXT,
    PersonaName TEXT,
    Temperature REAL DEFAULT 0.7,
    MaxIterations INTEGER DEFAULT 10,
    Capabilities TEXT,  -- JSON array
    SupportedIntents TEXT,  -- JSON array
    ToolBindings TEXT,  -- JSON array
    RoutingRules TEXT,  -- JSON array
    IsEnabled INTEGER DEFAULT 1,
    Status TEXT DEFAULT 'Draft',
    Version INTEGER DEFAULT 1,
    CreatedAt TEXT DEFAULT (datetime('now')),
    UpdatedAt TEXT,
    CreatedBy TEXT,
    FOREIGN KEY (TenantId) REFERENCES Tenants(Id)
);

CREATE TABLE AgentSessions (
    Id TEXT PRIMARY KEY,  -- GUID as TEXT
    TenantId INTEGER NOT NULL,
    UserId TEXT NOT NULL,
    AgentType TEXT,
    StartedAt TEXT DEFAULT (datetime('now')),
    LastActivityAt TEXT,
    Status TEXT DEFAULT 'active',
    Metadata TEXT,  -- JSON
    FOREIGN KEY (TenantId) REFERENCES Tenants(Id)
);

CREATE TABLE LearnedRules (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    TenantId INTEGER NOT NULL,
    AgentType TEXT,
    RuleCategory TEXT,
    RuleKey TEXT,
    RuleValue TEXT,
    PromptInjection TEXT,
    Confidence REAL,
    Status TEXT DEFAULT 'pending',
    SourceSessionId TEXT,
    LearnedAt TEXT DEFAULT (datetime('now')),
    ReviewedAt TEXT,
    ReviewedBy TEXT,
    FOREIGN KEY (TenantId) REFERENCES Tenants(Id)
);

-- Indexes for performance
CREATE INDEX IX_BusinessRules_TenantId ON TenantBusinessRules(TenantId);
CREATE INDEX IX_AgentDefinitions_TenantId ON AgentDefinitions(TenantId);
CREATE INDEX IX_AgentDefinitions_Status ON AgentDefinitions(Status);
CREATE INDEX IX_Sessions_TenantId ON AgentSessions(TenantId);
CREATE INDEX IX_LearnedRules_TenantId ON LearnedRules(TenantId);
```

### SQL Server Schema (Enterprise)

```sql
-- SQL Server schema with Row-Level Security

-- Enable RLS
CREATE SECURITY POLICY TenantIsolationPolicy
ADD FILTER PREDICATE dbo.fn_TenantAccessPredicate(TenantId)
    ON dbo.TenantBusinessRules,
ADD FILTER PREDICATE dbo.fn_TenantAccessPredicate(TenantId)
    ON dbo.AgentDefinitions,
ADD FILTER PREDICATE dbo.fn_TenantAccessPredicate(TenantId)
    ON dbo.AgentSessions,
ADD FILTER PREDICATE dbo.fn_TenantAccessPredicate(TenantId)
    ON dbo.LearnedRules
WITH (STATE = ON);

-- RLS predicate function
CREATE FUNCTION dbo.fn_TenantAccessPredicate(@TenantId INT)
RETURNS TABLE
WITH SCHEMABINDING
AS RETURN SELECT 1 AS AccessResult
WHERE @TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS INT)
   OR SESSION_CONTEXT(N'TenantId') IS NULL;  -- Allow admin access
GO

-- Tables use NVARCHAR(MAX) for JSON, DATETIME2, UNIQUEIDENTIFIER
-- (Same structure as SQLite but with SQL Server types)
```

### Migration Strategy

```bash
# Generate migrations for both providers
dotnet ef migrations add InitialCreate --context DivaDbContext --output-dir Migrations/SQLite -- --provider SQLite
dotnet ef migrations add InitialCreate --context DivaDbContext --output-dir Migrations/SqlServer -- --provider SqlServer

# Apply migrations
dotnet ef database update -- --provider SQLite    # Default
dotnet ef database update -- --provider SqlServer # Enterprise
```

### Configuration Examples

**Development (SQLite - Default)**
```json
{
  "Database": {
    "Provider": "SQLite",
    "SQLite": {
      "ConnectionString": "Data Source=diva.db"
    }
  }
}
```

**Production (SQL Server)**
```json
{
  "Database": {
    "Provider": "SqlServer",
    "SqlServer": {
      "ConnectionString": "Server=prod-sql.database.windows.net;Database=Diva;...",
      "UseRls": true
    }
  }
}
```

**Multi-Tenant Dedicated Databases (SQL Server)**
```json
{
  "Database": {
    "Provider": "SqlServer",
    "SqlServer": {
      "ConnectionString": "Server=prod-sql;Database=Diva;...",
      "UseConnectionPerTenant": true
    }
  }
}
```

---

## Docker & Containerization

### Solution Dockerfile

```dockerfile
# Dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files
COPY *.sln .
COPY src/Diva.Core/*.csproj src/Diva.Core/
COPY src/Diva.Agents/*.csproj src/Diva.Agents/
COPY src/Diva.Tools/*.csproj src/Diva.Tools/
COPY src/Diva.Infrastructure/*.csproj src/Diva.Infrastructure/
COPY src/Diva.Host/*.csproj src/Diva.Host/

# Restore
RUN dotnet restore

# Copy source and build
COPY src/ src/
WORKDIR /src/src/Diva.Host
RUN dotnet publish -c Release -o /app/publish

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8080/health/live || exit 1

EXPOSE 8080
ENTRYPOINT ["dotnet", "Diva.Host.dll"]
```

### Docker Compose

```yaml
# docker-compose.yml (SQLite default - minimal setup)
version: '3.8'

services:
  # Main application (SQLite embedded - no external DB needed)
  diva-host:
    build:
      context: .
      dockerfile: Dockerfile
    ports:
      - "8080:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - Database__Provider=SQLite
      - Database__SQLite__ConnectionString=Data Source=/app/data/diva.db
      - LLM__Provider=Anthropic  # Direct provider (no LiteLLM)
      - LLM__ApiKey=${ANTHROPIC_API_KEY}
    volumes:
      - diva-data:/app/data  # Persist SQLite database
    networks:
      - diva-network
    deploy:
      resources:
        limits:
          cpus: '2'
          memory: 2G

  # Admin Portal
  admin-portal:
    build:
      context: ./admin-portal
      dockerfile: Dockerfile
    ports:
      - "3000:3000"
    environment:
      - REACT_APP_API_URL=http://diva-host:8080
    depends_on:
      - diva-host
    networks:
      - diva-network

networks:
  diva-network:
    driver: bridge

volumes:
  diva-data:
```

```yaml
# docker-compose.enterprise.yml (SQL Server + LiteLLM for production)
version: '3.8'

services:
  # Main application with SQL Server
  diva-host:
    build:
      context: .
      dockerfile: Dockerfile
    ports:
      - "8080:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - Database__Provider=SqlServer
      - Database__SqlServer__ConnectionString=${DB_CONNECTION_STRING}
      - Database__SqlServer__UseRls=true
      - LiteLLM__BaseUrl=http://litellm:4000
      - Seq__ServerUrl=http://seq:5341
    depends_on:
      - litellm
      - sqlserver
      - seq
    networks:
      - diva-network
    deploy:
      replicas: 2
      resources:
        limits:
          cpus: '2'
          memory: 4G

  # LiteLLM Proxy (optional - for multi-provider routing)
  litellm:
    image: ghcr.io/berriai/litellm:main-latest
    ports:
      - "4000:4000"
    environment:
      - LITELLM_MASTER_KEY=${LITELLM_MASTER_KEY}
      - DATABASE_URL=postgresql://postgres:postgres@litellm-db:5432/litellm
      - ANTHROPIC_API_KEY=${ANTHROPIC_API_KEY}
      - OPENAI_API_KEY=${OPENAI_API_KEY}
    volumes:
      - ./litellm_config.yaml:/app/config.yaml
    command: ["--config", "/app/config.yaml", "--port", "4000"]
    depends_on:
      - litellm-db
    networks:
      - diva-network
    profiles:
      - enterprise

  # LiteLLM Database
  litellm-db:
    image: postgres:16
    environment:
      - POSTGRES_USER=postgres
      - POSTGRES_PASSWORD=postgres
      - POSTGRES_DB=litellm
    volumes:
      - litellm-db-data:/var/lib/postgresql/data
    networks:
      - diva-network
    profiles:
      - enterprise

  # SQL Server (enterprise)
  sqlserver:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      - ACCEPT_EULA=Y
      - SA_PASSWORD=${SQL_SA_PASSWORD}
    ports:
      - "1433:1433"
    volumes:
      - sqlserver-data:/var/opt/mssql
    networks:
      - diva-network
    profiles:
      - enterprise

  # Seq (logging)
  seq:
    image: datalust/seq:latest
    ports:
      - "5341:5341"
      - "8081:80"
    environment:
      - ACCEPT_EULA=Y
    volumes:
      - seq-data:/data
    networks:
      - diva-network

  # Temporal (scheduling)
  temporal:
    image: temporalio/auto-setup:latest
    ports:
      - "7233:7233"
    environment:
      - DB=postgresql
      - DB_PORT=5432
      - POSTGRES_USER=postgres
      - POSTGRES_PWD=postgres
      - POSTGRES_SEEDS=temporal-db
    depends_on:
      - temporal-db
    networks:
      - diva-network

  temporal-db:
    image: postgres:16
    environment:
      - POSTGRES_USER=postgres
      - POSTGRES_PASSWORD=postgres
    volumes:
      - temporal-db-data:/var/lib/postgresql/data
    networks:
      - diva-network

  temporal-ui:
    image: temporalio/ui:latest
    ports:
      - "8082:8080"
    environment:
      - TEMPORAL_ADDRESS=temporal:7233
    networks:
      - diva-network

networks:
  diva-network:
    driver: bridge

volumes:
  sqlserver-data:
  litellm-db-data:
  seq-data:
  temporal-db-data:
```

### Kubernetes Deployment (Production)

```yaml
# k8s/deployment.yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: diva-host
  labels:
    app: tei
spec:
  replicas: 3
  selector:
    matchLabels:
      app: tei
  template:
    metadata:
      labels:
        app: tei
    spec:
      containers:
      - name: diva-host
        image: your-registry/diva-host:latest
        ports:
        - containerPort: 8080
        env:
        - name: ASPNETCORE_ENVIRONMENT
          value: "Production"
        - name: ConnectionStrings__TeiDb
          valueFrom:
            secretKeyRef:
              name: tei-secrets
              key: db-connection-string
        resources:
          requests:
            memory: "2Gi"
            cpu: "1000m"
          limits:
            memory: "4Gi"
            cpu: "2000m"
        livenessProbe:
          httpGet:
            path: /health/live
            port: 8080
          initialDelaySeconds: 10
          periodSeconds: 30
        readinessProbe:
          httpGet:
            path: /health/ready
            port: 8080
          initialDelaySeconds: 5
          periodSeconds: 10
```

---

## CI/CD Pipeline

### GitHub Actions

```yaml
# .github/workflows/ci-cd.yml
name: CI/CD Pipeline

on:
  push:
    branches: [main, develop]
  pull_request:
    branches: [main]

env:
  REGISTRY: ghcr.io
  IMAGE_NAME: ${{ github.repository }}

jobs:
  build-and-test:
    runs-on: ubuntu-latest
    steps:
    - uses: actions/checkout@v4

    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '8.0.x'

    - name: Restore dependencies
      run: dotnet restore

    - name: Build
      run: dotnet build --no-restore --configuration Release

    - name: Run unit tests
      run: dotnet test --no-build --configuration Release --verbosity normal --logger trx --results-directory TestResults

    - name: Publish test results
      uses: dorny/test-reporter@v1
      if: always()
      with:
        name: .NET Tests
        path: TestResults/*.trx
        reporter: dotnet-trx

  security-scan:
    runs-on: ubuntu-latest
    needs: build-and-test
    steps:
    - uses: actions/checkout@v4

    - name: Run Trivy vulnerability scanner
      uses: aquasecurity/trivy-action@master
      with:
        scan-type: 'fs'
        scan-ref: '.'
        format: 'sarif'
        output: 'trivy-results.sarif'

    - name: Upload Trivy scan results
      uses: github/codeql-action/upload-sarif@v3
      with:
        sarif_file: 'trivy-results.sarif'

  build-and-push-image:
    runs-on: ubuntu-latest
    needs: [build-and-test, security-scan]
    if: github.event_name == 'push' && github.ref == 'refs/heads/main'
    permissions:
      contents: read
      packages: write
    steps:
    - uses: actions/checkout@v4

    - name: Log in to Container Registry
      uses: docker/login-action@v3
      with:
        registry: ${{ env.REGISTRY }}
        username: ${{ github.actor }}
        password: ${{ secrets.GITHUB_TOKEN }}

    - name: Extract metadata
      id: meta
      uses: docker/metadata-action@v5
      with:
        images: ${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}
        tags: |
          type=sha,prefix=
          type=raw,value=latest

    - name: Build and push Docker image
      uses: docker/build-push-action@v5
      with:
        context: .
        push: true
        tags: ${{ steps.meta.outputs.tags }}
        labels: ${{ steps.meta.outputs.labels }}
        cache-from: type=gha
        cache-to: type=gha,mode=max

  deploy-staging:
    runs-on: ubuntu-latest
    needs: build-and-push-image
    environment: staging
    steps:
    - name: Deploy to Staging
      uses: azure/k8s-deploy@v4
      with:
        namespace: diva-staging
        manifests: |
          k8s/staging/
        images: |
          ${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}:${{ github.sha }}

  deploy-production:
    runs-on: ubuntu-latest
    needs: deploy-staging
    environment: production
    steps:
    - name: Deploy to Production
      uses: azure/k8s-deploy@v4
      with:
        namespace: diva-production
        manifests: |
          k8s/production/
        images: |
          ${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}:${{ github.sha }}
```

---

## Final Solution Structure (MAF-Based)

```
Diva/                                            # Open Source Agentic AI Platform
├── src/
│   ├── Diva.Core/                               # Domain models, OAuth context
│   │   ├── Models/
│   │   │   ├── TenantContext.cs                # OAuth-derived tenant info
│   │   │   ├── McpRequestContext.cs            # Headers for MCP tools
│   │   │   └── AgentRequest.cs
│   │   └── Configuration/
│   │       └── OAuthOptions.cs
│   │
│   ├── Diva.Agents/                             # MAF Agents (minimal custom)
│   │   ├── Workers/
│   │   │   └── BaseReActAgent.cs               # MAF ReAct wrapper
│   │   ├── Supervisor/
│   │   │   └── SupervisorAgent.cs              # MAF AgentGroupChat
│   │   └── Registry/
│   │
│   ├── Diva.TenantAdmin/                        # CUSTOM: Tenant config (KEY!)
│   │   ├── Services/
│   │   ├── Prompts/
│   │   │   ├── ITenantAwarePromptBuilder.cs
│   │   │   └── TenantAwarePromptBuilder.cs
│   │   └── Models/
│   │
│   ├── Diva.Tools/                              # CUSTOM: MCP Tool Infrastructure
│   │   ├── Core/
│   │   │   ├── McpToolBase.cs                  # OAuth + header injection
│   │   │   ├── McpHeaderPropagator.cs
│   │   │   └── TenantAwareMcpClient.cs
│   │   └── [DomainTools]/                      # Pluggable domain tools
│   │
│   ├── Diva.Infrastructure/
│   │   ├── Data/
│   │   ├── Auth/
│   │   │   ├── OAuthTokenValidator.cs
│   │   │   ├── TenantContextMiddleware.cs
│   │   │   └── HeaderPropagationHandler.cs
│   │   ├── LiteLLM/
│   │   └── Sessions/
│   │
│   └── Diva.Host/
│       ├── Controllers/
│       └── Hubs/
│
├── admin-portal/                               # CUSTOM: Admin UI (KEY!)
├── prompts/
├── tests/
├── k8s/
├── .github/
├── docker-compose.yml
└── Dockerfile
```

---

## Final Estimated Scope (MAF-Based)

| Phase | Files | Description | Custom vs MAF |
|-------|-------|-------------|---------------|
| 1. Setup | ~5 | Solution, packages, OAuth config | Config |
| 2. OAuth Integration | ~6 | Token validation, tenant extraction | **Custom** |
| 3. MCP Header Injection | ~5 | Token/header propagation to tools | **Custom** |
| 4. Tenant Admin | ~10 | Business rules, prompts, models | **Custom** |
| 5. MCP Tool Infrastructure | ~8 | Base classes, pluggable tools | **Custom** |
| 6. ReAct Agents | ~8 | Worker agents using MAF | 30% Custom |
| 7. Supervisor | ~5 | AgentGroupChat | 20% Custom |
| 8. Database | ~8 | DbContext, entities, RLS, learned rules | **Custom** |
| 9. **Dynamic Rule Learning** | ~10 | Rule extraction, session rules, patterns | **Custom** |
| 10. Admin Portal | ~10 | React UI + pending rules review | **Custom** |
| 11. **Dynamic Agent Registration** | ~12 | UI-managed agents, hot-reload, version control | **Custom** |
| 12. Host & API | ~6 | ASP.NET endpoints | **Custom** |
| **Total** | **~93** | **Enterprise-ready with learning & dynamic agents** | |

---

## References

- [Microsoft Agent Framework](https://azure.microsoft.com/en-us/blog/introducing-microsoft-agent-framework/)
- [MAF ReAct Agent Documentation](https://learn.microsoft.com/en-us/agent-framework/react/)
- [A2A Protocol Specification](https://github.com/a2aproject/A2A)
- [MCP Specification](https://modelcontextprotocol.io/specification/2025-11-25)
- [LiteLLM Documentation](https://docs.litellm.ai/)
- [OAuth 2.0 RFC 6749](https://tools.ietf.org/html/rfc6749)
- [OpenTelemetry .NET](https://opentelemetry.io/docs/instrumentation/net/)

---

## Dynamic Agent Registration via UI

**Diva supports dynamically adding new agents through the Admin UI and integrating them into the orchestrator workflow at runtime - no code deployment required.**

### Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                     DYNAMIC AGENT REGISTRATION FLOW                          │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  ┌──────────────────┐    ┌──────────────────┐    ┌──────────────────┐       │
│  │   Admin Portal   │───>│   Agent API      │───>│   Agent Store    │       │
│  │   (React UI)     │    │   (REST)         │    │   (Database)     │       │
│  └──────────────────┘    └──────────────────┘    └──────────────────┘       │
│           │                       │                       │                  │
│           │                       v                       │                  │
│           │              ┌──────────────────┐             │                  │
│           │              │  AgentRegistry   │<────────────┘                  │
│           │              │  (Runtime Cache) │                                │
│           │              └──────────────────┘                                │
│           │                       │                                          │
│           │                       v                                          │
│           │              ┌──────────────────┐                                │
│           └─────────────>│   Supervisor     │                                │
│             (Live View)  │   (Orchestrator) │                                │
│                          └──────────────────┘                                │
│                                   │                                          │
│                    ┌──────────────┼──────────────┐                          │
│                    v              v              v                          │
│             ┌──────────┐   ┌──────────┐   ┌──────────┐                      │
│             │ Agent A  │   │ Agent B  │   │ Agent N  │  ← Dynamically       │
│             │ (Static) │   │ (Dynamic)│   │ (Dynamic)│    Registered        │
│             └──────────┘   └──────────┘   └──────────┘                      │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Agent Definition Model

```csharp
namespace Diva.Core.Agents;

public class AgentDefinition
{
    public Guid Id { get; set; }
    public int TenantId { get; set; }

    // Basic Info
    public string Name { get; set; }              // "InventoryAgent"
    public string DisplayName { get; set; }       // "Inventory Management Agent"
    public string Description { get; set; }       // "Handles inventory queries..."
    public string AgentType { get; set; }         // "ReAct" | "Simple" | "Custom"

    // Capabilities (used by Supervisor for routing)
    public List<string> Capabilities { get; set; } // ["inventory-lookup", "stock-alerts"]
    public List<string> SupportedIntents { get; set; } // ["check_stock", "reorder"]

    // System Prompt & Behavior
    public string SystemPrompt { get; set; }       // Full system prompt
    public string PersonaName { get; set; }        // "Inventory Specialist"
    public double Temperature { get; set; }        // 0.7
    public int MaxIterations { get; set; }         // 10

    // Tool Configuration
    public List<AgentToolBinding> ToolBindings { get; set; }

    // Routing Rules
    public List<RoutingRule> RoutingRules { get; set; }

    // Lifecycle
    public bool IsEnabled { get; set; }
    public AgentStatus Status { get; set; }       // Draft, Published, Archived
    public int Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string CreatedBy { get; set; }
}

public class AgentToolBinding
{
    public string ToolServerId { get; set; }      // "inventory-mcp-server"
    public List<string> AllowedTools { get; set; } // ["get_stock", "update_quantity"]
    public Dictionary<string, object> DefaultParameters { get; set; }
}

public class RoutingRule
{
    public string IntentPattern { get; set; }     // "inventory|stock|warehouse"
    public int Priority { get; set; }             // 100 (higher = more priority)
    public Dictionary<string, string> Conditions { get; set; }
}

public enum AgentStatus
{
    Draft,      // Not active, can be tested
    Published,  // Active, routing enabled
    Archived    // Disabled, kept for history
}
```

### Dynamic Agent Registry with Hot-Reload

```csharp
namespace Diva.Agents.Registry;

public class DynamicAgentRegistry : IAgentRegistry, IDisposable
{
    private readonly ConcurrentDictionary<Guid, DynamicAgentInstance> _agents = new();
    private readonly IAgentDefinitionStore _store;
    private readonly IServiceProvider _services;
    private readonly IChangeNotifier _changeNotifier;
    private readonly ILogger<DynamicAgentRegistry> _logger;

    public DynamicAgentRegistry(
        IAgentDefinitionStore store,
        IServiceProvider services,
        IChangeNotifier changeNotifier,
        ILogger<DynamicAgentRegistry> logger)
    {
        _store = store;
        _services = services;
        _changeNotifier = changeNotifier;
        _logger = logger;

        // Subscribe to real-time changes from Admin UI
        _changeNotifier.OnAgentChanged += HandleAgentChangeAsync;
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        // Load all published agents on startup
        var agents = await _store.GetPublishedAgentsAsync(ct);
        foreach (var def in agents)
        {
            await RegisterOrUpdateAsync(def, ct);
        }
        _logger.LogInformation("Loaded {Count} dynamic agents", agents.Count);
    }

    // Called when agent is created/updated/deleted in Admin UI
    private async Task HandleAgentChangeAsync(AgentChangeEvent evt)
    {
        _logger.LogInformation("Agent change: {Type} for {AgentId}", evt.ChangeType, evt.AgentId);

        switch (evt.ChangeType)
        {
            case ChangeType.Created:
            case ChangeType.Updated:
                var definition = await _store.GetByIdAsync(evt.AgentId);
                if (definition?.Status == AgentStatus.Published)
                    await RegisterOrUpdateAsync(definition, CancellationToken.None);
                else
                    await UnregisterAsync(evt.AgentId);
                break;

            case ChangeType.Deleted:
            case ChangeType.Archived:
                await UnregisterAsync(evt.AgentId);
                break;
        }
    }

    public async Task RegisterOrUpdateAsync(AgentDefinition definition, CancellationToken ct)
    {
        // Create agent instance from definition
        var agent = CreateAgentFromDefinition(definition);

        // Register/update in runtime cache (atomic)
        _agents.AddOrUpdate(
            definition.Id,
            agent,
            (_, existing) =>
            {
                existing.Dispose();
                return agent;
            });

        _logger.LogInformation("Registered agent {Name} v{Version}", definition.Name, definition.Version);
    }

    private DynamicAgentInstance CreateAgentFromDefinition(AgentDefinition def)
    {
        return def.AgentType switch
        {
            "ReAct" => new DynamicReActAgent(def, _services),
            "Simple" => new DynamicSimpleAgent(def, _services),
            "Custom" => new DynamicCustomAgent(def, _services),
            _ => throw new NotSupportedException($"Agent type '{def.AgentType}' not supported")
        };
    }

    // Used by Supervisor for routing decisions
    public IReadOnlyList<AgentCapability> GetCapabilities(int? tenantId = null)
    {
        return _agents.Values
            .Where(a => a.IsEnabled)
            .Where(a => tenantId == null || a.TenantId == tenantId || a.TenantId == 0) // 0 = global
            .Select(a => a.GetCapability())
            .ToList();
    }

    public async Task<IAgent?> GetAgentAsync(Guid agentId)
    {
        return _agents.TryGetValue(agentId, out var agent) ? agent : null;
    }

    public async Task UnregisterAsync(Guid agentId)
    {
        if (_agents.TryRemove(agentId, out var agent))
        {
            agent.Dispose();
            _logger.LogInformation("Unregistered agent {AgentId}", agentId);
        }
    }

    public void Dispose()
    {
        _changeNotifier.OnAgentChanged -= HandleAgentChangeAsync;
        foreach (var agent in _agents.Values)
        {
            agent.Dispose();
        }
    }
}
```

### Dynamic ReAct Agent

```csharp
namespace Diva.Agents.Dynamic;

public class DynamicReActAgent : BaseReActAgent, IDisposable
{
    private readonly AgentDefinition _definition;
    private readonly IServiceProvider _services;

    public DynamicReActAgent(AgentDefinition definition, IServiceProvider services)
    {
        _definition = definition;
        _services = services;
    }

    public override string AgentId => _definition.Id.ToString();
    public override string Name => _definition.Name;
    public override string DisplayName => _definition.DisplayName;
    public int TenantId => _definition.TenantId;
    public bool IsEnabled => _definition.IsEnabled && _definition.Status == AgentStatus.Published;

    protected override async Task<string> GetSystemPromptAsync(TenantContext tenant, CancellationToken ct)
    {
        // Combine definition prompt with tenant-specific rules
        var promptBuilder = _services.GetRequiredService<ITenantAwarePromptBuilder>();

        return await promptBuilder.BuildPromptAsync(
            tenant,
            agentType: _definition.Name,
            basePrompt: _definition.SystemPrompt,
            ct: ct);
    }

    protected override async Task<IEnumerable<IMcpTool>> GetToolsAsync(TenantContext tenant, CancellationToken ct)
    {
        var mcpClientFactory = _services.GetRequiredService<IMcpClientFactory>();
        var tools = new List<IMcpTool>();

        foreach (var binding in _definition.ToolBindings)
        {
            var client = mcpClientFactory.CreateClient(binding.ToolServerId);
            var serverTools = await client.ListToolsAsync(ct);

            // Filter to only allowed tools
            var filteredTools = serverTools
                .Where(t => binding.AllowedTools.Contains(t.Name))
                .Select(t => new BoundMcpTool(t, binding.DefaultParameters));

            tools.AddRange(filteredTools);
        }

        return tools;
    }

    public AgentCapability GetCapability()
    {
        return new AgentCapability
        {
            AgentId = _definition.Id,
            Name = _definition.Name,
            DisplayName = _definition.DisplayName,
            Capabilities = _definition.Capabilities,
            SupportedIntents = _definition.SupportedIntents,
            RoutingRules = _definition.RoutingRules,
            TenantId = _definition.TenantId,
            Priority = _definition.RoutingRules.FirstOrDefault()?.Priority ?? 50
        };
    }

    public void Dispose()
    {
        // Cleanup resources if any
    }
}
```

### Supervisor Integration for Dynamic Agents

```csharp
namespace Diva.Agents.Supervisor;

public class DynamicSupervisor : ISupervisor
{
    private readonly IAgentRegistry _registry;
    private readonly IIntentClassifier _classifier;
    private readonly ITenantBusinessRulesService _rulesService;
    private readonly ILogger<DynamicSupervisor> _logger;

    public async Task<AgentResponse> RouteAsync(
        UserMessage message,
        TenantContext tenant,
        CancellationToken ct)
    {
        // Get all available agents for this tenant
        var availableAgents = _registry.GetCapabilities(tenant.TenantId);

        if (!availableAgents.Any())
        {
            return AgentResponse.Error("No agents available for this tenant");
        }

        // Classify user intent
        var intent = await _classifier.ClassifyAsync(message.Text, ct);

        // Find best matching agent
        var bestMatch = FindBestAgent(intent, availableAgents);

        if (bestMatch == null)
        {
            _logger.LogWarning("No agent matched intent {Intent}", intent.Primary);
            return AgentResponse.NoMatch("I couldn't find an appropriate agent to handle this request.");
        }

        _logger.LogInformation("Routing to agent {Agent} for intent {Intent}",
            bestMatch.Name, intent.Primary);

        // Get agent instance and execute
        var agent = await _registry.GetAgentAsync(bestMatch.AgentId);
        if (agent == null)
        {
            return AgentResponse.Error("Agent not available");
        }

        return await agent.ProcessAsync(message, tenant, ct);
    }

    private AgentCapability? FindBestAgent(Intent intent, IReadOnlyList<AgentCapability> agents)
    {
        return agents
            .Select(a => new { Agent = a, Score = CalculateMatchScore(a, intent) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Agent.Priority)
            .FirstOrDefault()?.Agent;
    }

    private double CalculateMatchScore(AgentCapability agent, Intent intent)
    {
        double score = 0;

        // Check capabilities match
        if (agent.Capabilities.Any(c => intent.Categories.Contains(c, StringComparer.OrdinalIgnoreCase)))
            score += 0.5;

        // Check intent patterns
        foreach (var rule in agent.RoutingRules)
        {
            if (Regex.IsMatch(intent.Primary, rule.IntentPattern, RegexOptions.IgnoreCase))
            {
                score += 0.3;
                break;
            }
        }

        // Check supported intents
        if (agent.SupportedIntents.Contains(intent.Primary, StringComparer.OrdinalIgnoreCase))
            score += 0.4;

        return score;
    }
}
```

### Admin Portal Agent Management UI

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  DIVA ADMIN PORTAL > Agents                                       [+ New]   │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │ Agent Name         │ Type   │ Status    │ Capabilities    │ Actions│    │
│  ├─────────────────────────────────────────────────────────────────────┤    │
│  │ AnalyticsAgent     │ ReAct  │ Published │ sql, reporting  │ [Edit] │    │
│  │ InventoryAgent     │ ReAct  │ Published │ inventory       │ [Edit] │    │
│  │ CustomerSupport    │ ReAct  │ Draft     │ tickets, faq    │ [Edit] │    │
│  │ WorkflowAgent      │ Simple │ Published │ approvals       │ [Edit] │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                                                              │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │ CREATE / EDIT AGENT                                                  │    │
│  ├─────────────────────────────────────────────────────────────────────┤    │
│  │                                                                      │    │
│  │  Basic Information                                                   │    │
│  │  ┌──────────────────────────────────────────────────────────────┐   │    │
│  │  │ Name:        [InventoryAgent________________]                │   │    │
│  │  │ Display:     [Inventory Management Agent____]                │   │    │
│  │  │ Type:        [ReAct          ▼]                              │   │    │
│  │  │ Description: [Handles inventory queries and stock management]│   │    │
│  │  └──────────────────────────────────────────────────────────────┘   │    │
│  │                                                                      │    │
│  │  System Prompt                                                       │    │
│  │  ┌──────────────────────────────────────────────────────────────┐   │    │
│  │  │ You are an Inventory Management specialist. Your role is     │   │    │
│  │  │ to help users with stock queries, reorder recommendations,   │   │    │
│  │  │ and warehouse management.                                     │   │    │
│  │  │                                                               │   │    │
│  │  │ Available variables: {{tenant_name}}, {{business_rules}}     │   │    │
│  │  │ ...                                                           │   │    │
│  │  └──────────────────────────────────────────────────────────────┘   │    │
│  │                                                                      │    │
│  │  Capabilities & Routing                                              │    │
│  │  ┌──────────────────────────────────────────────────────────────┐   │    │
│  │  │ Capabilities: [inventory] [stock] [warehouse] [+ Add]        │   │    │
│  │  │ Intent Patterns: [inventory|stock|reorder] [Edit Regex]      │   │    │
│  │  │ Priority: [100____]                                           │   │    │
│  │  └──────────────────────────────────────────────────────────────┘   │    │
│  │                                                                      │    │
│  │  Tool Bindings                                                       │    │
│  │  ┌──────────────────────────────────────────────────────────────┐   │    │
│  │  │ [✓] inventory-mcp-server                                     │   │    │
│  │  │     └── [✓] get_stock  [✓] update_quantity  [ ] delete_item │   │    │
│  │  │ [ ] analytics-mcp-server                                     │   │    │
│  │  │ [ ] notification-mcp-server                                  │   │    │
│  │  └──────────────────────────────────────────────────────────────┘   │    │
│  │                                                                      │    │
│  │  [Save as Draft]  [Test Agent]  [Publish]  [View Versions]           │    │
│  │                                                                      │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### REST API for Agent Management

```csharp
namespace Diva.Host.Controllers;

[ApiController]
[Route("api/admin/agents")]
[Authorize(Roles = "TenantAdmin")]
public class AgentAdminController : ControllerBase
{
    private readonly IAgentManagementService _agentService;
    private readonly IAgentRegistry _registry;

    [HttpGet]
    public async Task<ActionResult<List<AgentDefinitionDto>>> ListAgents(
        [FromQuery] AgentStatus? status = null)
    {
        var tenantId = User.GetTenantId();
        var agents = await _agentService.GetAgentsAsync(tenantId, status);
        return Ok(agents.Select(a => a.ToDto()));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<AgentDefinitionDto>> GetAgent(Guid id)
    {
        var agent = await _agentService.GetAgentAsync(id);
        if (agent == null) return NotFound();
        return Ok(agent.ToDto());
    }

    [HttpPost]
    public async Task<ActionResult<AgentDefinitionDto>> CreateAgent(
        [FromBody] CreateAgentRequest request)
    {
        var tenantId = User.GetTenantId();
        var agent = await _agentService.CreateAgentAsync(tenantId, request);
        return CreatedAtAction(nameof(GetAgent), new { id = agent.Id }, agent.ToDto());
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<AgentDefinitionDto>> UpdateAgent(
        Guid id,
        [FromBody] UpdateAgentRequest request)
    {
        var agent = await _agentService.UpdateAgentAsync(id, request);
        return Ok(agent.ToDto());
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAgent(Guid id)
    {
        await _agentService.DeleteAgentAsync(id);
        return NoContent();
    }

    [HttpPost("{id}/publish")]
    public async Task<ActionResult<AgentDefinitionDto>> PublishAgent(Guid id)
    {
        var agent = await _agentService.PublishAgentAsync(id);
        return Ok(agent.ToDto());
    }

    [HttpPost("{id}/archive")]
    public async Task<IActionResult> ArchiveAgent(Guid id)
    {
        await _agentService.ArchiveAgentAsync(id);
        return NoContent();
    }

    [HttpPost("{id}/test")]
    public async Task<ActionResult<AgentTestResult>> TestAgent(
        Guid id,
        [FromBody] AgentTestRequest request)
    {
        var result = await _agentService.TestAgentAsync(id, request.TestMessage);
        return Ok(result);
    }

    [HttpGet("{id}/versions")]
    public async Task<ActionResult<List<AgentVersionDto>>> GetVersions(Guid id)
    {
        var versions = await _agentService.GetVersionHistoryAsync(id);
        return Ok(versions);
    }

    [HttpPost("{id}/rollback/{version}")]
    public async Task<ActionResult<AgentDefinitionDto>> RollbackToVersion(
        Guid id,
        int version)
    {
        var agent = await _agentService.RollbackToVersionAsync(id, version);
        return Ok(agent.ToDto());
    }
}
```

### Database Schema for Dynamic Agents

> **Note:** See the "Database Provider Abstraction" section for full SQLite (default) schema.
> The schema below shows SQL Server syntax for enterprise deployments.

```sql
-- SQL Server syntax (enterprise)
-- For SQLite, see AgentDefinitions table in "Database Provider Abstraction" section

CREATE TABLE AgentDefinitions (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    TenantId INT NOT NULL,
    Name NVARCHAR(100) NOT NULL,
    DisplayName NVARCHAR(200),
    Description NVARCHAR(MAX),
    AgentType NVARCHAR(50) NOT NULL,
    SystemPrompt NVARCHAR(MAX),
    PersonaName NVARCHAR(100),
    Temperature FLOAT DEFAULT 0.7,
    MaxIterations INT DEFAULT 10,
    Capabilities NVARCHAR(MAX),       -- JSON
    SupportedIntents NVARCHAR(MAX),   -- JSON
    RoutingRules NVARCHAR(MAX),       -- JSON
    ToolBindings NVARCHAR(MAX),       -- JSON
    IsEnabled BIT DEFAULT 1,
    Status NVARCHAR(20) DEFAULT 'Draft',
    Version INT DEFAULT 1,
    CreatedAt DATETIME2 DEFAULT GETUTCDATE(),
    UpdatedAt DATETIME2,
    CreatedBy NVARCHAR(100),
    CONSTRAINT FK_AgentDefinitions_Tenant FOREIGN KEY (TenantId)
        REFERENCES Tenants(Id)
);

CREATE TABLE AgentDefinitionVersions (
    Id INT IDENTITY PRIMARY KEY,
    AgentId UNIQUEIDENTIFIER NOT NULL,
    Version INT NOT NULL,
    Definition NVARCHAR(MAX) NOT NULL,
    CreatedAt DATETIME2 DEFAULT GETUTCDATE(),
    CreatedBy NVARCHAR(100),
    ChangeNotes NVARCHAR(MAX),
    CONSTRAINT FK_AgentVersions_Agent FOREIGN KEY (AgentId)
        REFERENCES AgentDefinitions(Id)
);

CREATE INDEX IX_AgentDefinitions_TenantId ON AgentDefinitions(TenantId);
CREATE INDEX IX_AgentDefinitions_Status ON AgentDefinitions(Status);
```

### Key Features Summary

| Feature | Description |
|---------|-------------|
| **No-Code Agent Creation** | Create agents entirely through Admin UI |
| **System Prompt Editor** | Rich editor with variable support ({{tenant_name}}, {{business_rules}}) |
| **Tool Binding UI** | Select which MCP tools each agent can use |
| **Capability Tags** | Define what each agent can handle for routing |
| **Intent Routing** | Regex patterns for automatic intent-based routing |
| **Version Control** | Track agent definition versions with rollback |
| **Draft/Publish Workflow** | Test agents before activating |
| **Hot-Reload** | Changes apply immediately without restart |
| **Per-Tenant Agents** | Each tenant can have custom agents |
| **Agent Testing** | Test agent responses before publishing |

---

## Review Notes

### Key Decisions Made:
1. ✅ **Diva Namespace** - Enterprise-wide, domain-agnostic naming
2. ✅ **OAuth Integration** - Token-based tenant identification from main app
3. ✅ **MCP Header Injection** - OAuth token + custom headers propagated to tools
4. ✅ **TenantID (not PropertyID)** - Generalized multi-tenant terminology
5. ✅ **Using MAF for ReAct** - Saves ~40 files of custom orchestration code
6. ✅ **Custom Tenant Admin** - Per-tenant prompts and business rules
7. ✅ **Pluggable Domain Tools** - Domain-specific MCP tools register via DI
8. ✅ **Dynamic Rule Learning** - Agents learn business rules during iterations
9. ✅ **Dynamic Agent Registration** - Add/configure agents via UI, hot-reload, no code deployment
10. ✅ **Open Source Ready** - MIT licensed, generic examples, extension points documented
11. ✅ **SQLite Default** - Zero-config local dev, optional SQL Server for enterprise

### Architecture Highlights:
```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    DIVA ENTERPRISE ARCHITECTURE                              │
│                                                                              │
│  Main App (OAuth Provider)                                                  │
│       │                                                                     │
│       │ OAuth Token (Bearer)                                                │
│       ▼                                                                     │
│  Diva API Host                                                               │
│       │                                                                     │
│       ├── TenantContextMiddleware (extract TenantID from token)             │
│       │                                                                     │
│       ├── Agent invocation (MAF ReAct)                                      │
│       │       │                                                             │
│       │       └── TenantAwarePromptBuilder (inject business rules)          │
│       │                                                                     │
│       └── MCP Tool calls                                                    │
│               │                                                             │
│               └── TenantAwareMcpClient (inject OAuth + custom headers)      │
│                       │                                                     │
│                       └── Domain Tool Servers (receive full context)        │
│                               │                                             │
│                               └── Downstream APIs (via propagated headers)  │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Remaining Questions:
1. What OAuth claims should map to TenantID? (e.g., `tenant_id`, `org_id`, `client_id`)
2. Which custom headers should be propagated by default?
3. Should tool servers call downstream APIs synchronously or queue requests?
4. Admin Portal: React or Blazor?
5. Hosting: Azure Container Apps or AKS?

### Implementation Priority:
```
Phase 1: OAuth + Tenant Admin + Core Infrastructure (MVP)
         └── ~35 files

Phase 2: Domain Agent + MCP Tools
         └── ~20 files per domain

Phase 3: Admin Portal UI + Advanced Features
         └── ~15 files
```

---

## Getting Started

### Prerequisites

- .NET 8 SDK
- Node.js 18+ (for Admin Portal)
- Docker (optional, for containerized deployment)
- **No database server required** (SQLite is the default)
- SQL Server (optional, for enterprise deployments)

### Quick Start

```bash
# Clone the repository
git clone https://github.com/your-org/diva.git
cd diva

# Create environment file
cp .env.example .env
# Edit .env with your LLM API key (that's all you need!)

# Option 1: Start with Docker (simplest)
docker-compose up -d
# That's it! SQLite is embedded, no database to configure.

# Option 2: Run locally
cd src/Diva.Host
dotnet run
# SQLite database auto-created at ./diva.db

# Start Admin Portal (optional)
cd admin-portal
npm install
npm start
```

**Zero-config local development:** Just set your LLM API key and run. SQLite database is created automatically.

### Configuration

Create a `.env` file with the following:

```env
# Database (SQLite is default - no config needed for local dev)
DATABASE_PROVIDER=SQLite
# DATABASE_PROVIDER=SqlServer  # Uncomment for SQL Server
# DATABASE_CONNECTION_STRING=Server=localhost;Database=Diva;Trusted_Connection=True

# OAuth Configuration
OAUTH_AUTHORITY=https://your-oauth-provider.com
OAUTH_AUDIENCE=diva-api
OAUTH_TENANT_CLAIM=tenant_id

# LLM Configuration (choose one)
# Option 1: Direct provider
LLM_PROVIDER=Anthropic
LLM_API_KEY=your-api-key
LLM_MODEL=claude-sonnet-4-20250514

# Option 2: LiteLLM (optional)
USE_LITELLM=false
LITELLM_ENDPOINT=http://localhost:4000

# MCP Tool Servers (comma-separated)
MCP_SERVERS=analytics:http://localhost:5001,inventory:http://localhost:5002
```

---

## Extension Points

Diva is designed for extensibility. Here are the main extension points:

### 1. Custom Agents

Extend `BaseReActAgent` to create domain-specific agents:

```csharp
public class MyCustomAgent : BaseReActAgent
{
    public override string Name => "MyCustomAgent";

    protected override async Task<string> GetSystemPromptAsync(
        TenantContext tenant, CancellationToken ct)
    {
        // Return your agent's system prompt
        // Use _promptBuilder to include tenant-specific rules
    }

    protected override async Task<IEnumerable<IMcpTool>> GetToolsAsync(
        TenantContext tenant, CancellationToken ct)
    {
        // Return the MCP tools this agent can use
    }
}

// Register in DI
services.AddTransient<IAgent, MyCustomAgent>();
```

### 2. Custom MCP Tool Servers

Implement `IMcpToolServer` to add new tool servers:

```csharp
public class MyToolServer : IMcpToolServer
{
    public string ServerId => "my-tools";

    public async Task<ToolResult> InvokeToolAsync(
        string toolName,
        JsonElement parameters,
        McpRequestContext context,
        CancellationToken ct)
    {
        // context contains OAuth token and custom headers
        return toolName switch
        {
            "my_tool" => await HandleMyToolAsync(parameters, context, ct),
            _ => ToolResult.NotFound()
        };
    }
}

// Register in DI
services.AddTransient<IMcpToolServer, MyToolServer>();
```

### 3. Custom Authentication

Implement `ITenantContextProvider` for custom auth:

```csharp
public class MyTenantContextProvider : ITenantContextProvider
{
    public async Task<TenantContext> GetContextAsync(HttpContext http)
    {
        // Extract tenant info from your auth system
        var token = http.Request.Headers.Authorization;
        // Parse and validate...

        return new TenantContext
        {
            TenantId = extractedTenantId,
            UserId = extractedUserId,
            AccessToken = token,
            // ...
        };
    }
}
```

### 4. Custom Storage

Implement storage interfaces for different databases:

```csharp
public class MyTenantRulesStore : ITenantRulesStore
{
    // Implement for MongoDB, PostgreSQL, etc.
}
```

---

## Sample Implementations

The `samples/` directory contains example domain implementations:

### Analytics Domain
```
samples/Diva.Samples.Analytics/
├── AnalyticsAgent.cs           # ReAct agent for analytics queries
├── AnalyticsMcpServer.cs       # MCP tools: run_query, get_metrics
└── README.md                   # Setup instructions
```

### Customer Support Domain
```
samples/Diva.Samples.Support/
├── SupportAgent.cs             # ReAct agent for support tickets
├── SupportMcpServer.cs         # MCP tools: search_kb, create_ticket
└── README.md
```

### Workflow Automation Domain
```
samples/Diva.Samples.Workflow/
├── WorkflowAgent.cs            # Simple agent for approvals
├── WorkflowMcpServer.cs        # MCP tools: get_pending, approve, reject
└── README.md
```

---

## Contributing

We welcome contributions! Please see [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

### Development Setup

```bash
# Fork and clone
git clone https://github.com/your-username/diva.git

# Install dependencies
dotnet restore
cd admin-portal && npm install

# Run tests
dotnet test

# Run locally
docker-compose -f docker-compose.dev.yml up
```

### Pull Request Process

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Make your changes
4. Add/update tests
5. Run `dotnet format` and `npm run lint`
6. Commit with descriptive message
7. Push and create Pull Request

### Code Style

- Follow .NET naming conventions
- Use async/await consistently
- Add XML documentation for public APIs
- Write unit tests for new features

---

## License

Diva is released under the **MIT License**.

```
MIT License

Copyright (c) 2026 Diva Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

**Plan Version**: 4.0 (Diva Open Source Platform)
**Last Updated**: March 2026
**Status**: READY FOR IMPLEMENTATION
