# Diva AI — Coding Conventions

> Applies to all contributors and AI coding assistants.
> Consistent conventions let every session pick up where the last left off.

---

## C# Conventions

### Naming

| Element | Convention | Example |
|---------|-----------|---------|
| Interface | `I` prefix | `ITenantAwarePromptBuilder` |
| Async method | `Async` suffix | `ExecuteAsync`, `GetRulesAsync` |
| Options/config class | `Options` suffix | `LlmOptions`, `DatabaseOptions` |
| DB entity | `Entity` suffix | `TenantBusinessRuleEntity` |
| Domain/service model | No suffix | `TenantBusinessRule`, `SuggestedRule` |
| DTO (API layer) | `Dto` suffix | `CreateRuleDto`, `AgentDefinitionDto` |
| Private fields | Underscore prefix | `_db`, `_logger`, `_cache` |
| Constants | PascalCase | `public const string SectionName = "LLM"` |
| Enum values | PascalCase | `RuleApprovalMode.RequireAdmin` |
| CancellationToken param | Always named `ct` | `CancellationToken ct` |

### Namespace Rules

Namespaces must exactly match the folder path relative to `src/`:

```
src/Diva.Infrastructure/Learning/RuleLearningService.cs
→ namespace Diva.Infrastructure.Learning;

src/Diva.Agents/Supervisor/Stages/DecomposeStage.cs
→ namespace Diva.Agents.Supervisor.Stages;

src/Diva.TenantAdmin/Prompts/TenantAwarePromptBuilder.cs
→ namespace Diva.TenantAdmin.Prompts;
```

### Class Structure Order

```csharp
public class ExampleService : IExampleService
{
    // 1. Constants
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    // 2. Private fields
    private readonly DivaDbContext _db;
    private readonly ILogger<ExampleService> _logger;

    // 3. Constructor
    public ExampleService(DivaDbContext db, ILogger<ExampleService> logger) { }

    // 4. Public interface methods (in same order as interface declaration)
    public async Task<List<X>> GetAsync(int tenantId, CancellationToken ct) { }

    // 5. Private helpers
    private string BuildCacheKey(int tenantId) => $"rules:{tenantId}";
}
```

### Method Signatures

```csharp
// Always: CancellationToken last, named ct
public Task<AgentResponse> ExecuteAsync(
    AgentRequest request,
    TenantContext tenant,
    CancellationToken ct);

// Always: explicit return types (no var for public API)
public async Task<List<TenantBusinessRule>> GetRulesAsync(
    int tenantId, string agentType, CancellationToken ct)

// Records for immutable value types
public record SubTask(string Description, string[] RequiredCapabilities, int SiteId, int TenantId);

// Sealed for leaf classes (services, middleware, etc.)
public sealed class TenantContextMiddleware { }
```

### Error Handling

```csharp
// Prefer specific exception types
throw new NotSupportedException($"Provider '{provider}' not supported.");
throw new UnauthorizedAccessException($"Access denied to site {siteId}");

// Catch only what you handle — never catch Exception blindly in business logic
try { ... }
catch (JsonException) { return []; }    // ← specific, with clear fallback

// Use CancellationToken, never swallow OperationCanceledException
```

### EF Core Rules

```csharp
// Entities MUST implement ITenantEntity — EF query filters depend on this
public class MyEntity : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; }   // drives HasQueryFilter
}

// Always pass CancellationToken to EF async methods
await _db.Rules.Where(...).ToListAsync(ct);
await _db.SaveChangesAsync(ct);

// Never use .Result or .Wait() on async EF calls
```

### Dependency Injection

```csharp
// Lifetime rules
// Singleton:  IAgentRegistry, ILlmClientFactory, PromptTemplateStore
// Scoped:     DivaDbContext, ITenantAwarePromptBuilder, ISessionService, IRuleLearningService
// Transient:  HeaderPropagationHandler

// Never resolve scoped services from singleton — use IServiceProvider + CreateScope()
public class DynamicAgentRegistry  // Singleton
{
    private readonly IServiceProvider _services;

    private async Task LoadFromDbAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DivaDbContext>();
        // ...
    }
}
```

---

## Project Boundaries

| Layer | May depend on | Must NOT depend on |
|-------|--------------|-------------------|
| `Diva.Core` | Nothing | Everything |
| `Diva.Infrastructure` | `Diva.Core` | `Diva.Agents`, `Diva.Tools`, `Diva.Host` |
| `Diva.Tools` | `Diva.Core`, `Diva.Infrastructure` | `Diva.Agents`, `Diva.Host` |
| `Diva.TenantAdmin` | `Diva.Core`, `Diva.Infrastructure` | `Diva.Agents`, `Diva.Host` |
| `Diva.Agents` | `Diva.Core`, `Diva.Infrastructure`, `Diva.Tools`, `Diva.TenantAdmin` | `Diva.Host` |
| `Diva.Host` | Everything | — |

---

## File Placement Rules

| Thing | Where |
|-------|-------|
| Domain interface (`IFooService`) | Same folder as implementation |
| Entity classes | `Diva.Infrastructure/Data/Entities/` |
| EF migrations | `Diva.Infrastructure/Migrations/{Provider}/` |
| Prompt templates | `prompts/{agent-type}/` as `.txt` files |
| appsettings overrides | `src/Diva.Host/appsettings.{Environment}.json` |
| TypeScript types | `admin-portal/src/types.ts` for shared types; co-locate component-specific types |
| API client calls | `admin-portal/src/api/adminApi.ts` only |

---

## Semantic Kernel Specifics

```csharp
// Suppress SK experimental API warnings at file level (not project level)
#pragma warning disable SKEXP0110

// Kernel is scoped (per-request, per-tenant) — never singleton
builder.Services.AddScoped<Kernel>(sp => factory.CreateKernel(tenant));

// Always use FunctionChoiceBehavior.Auto for ReAct tool calling
agent.ExecutionSettings = new OpenAIPromptExecutionSettings
{
    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
};

// AgentGroupChat settings always set MaximumIterations
new AgentGroupChatSettings
{
    TerminationStrategy = new TaskCompletionTerminationStrategy { MaximumIterations = 10 }
}
```

---

## React / TypeScript Conventions

### File & Component Naming

```
BusinessRulesPage.tsx    ← PascalCase, named export
AgentToggle.tsx          ← PascalCase component
adminApi.ts              ← camelCase for non-component modules
types.ts                 ← shared type definitions
```

### Component Structure

```tsx
// Named export (not default export)
export function BusinessRulesPage() {
  // 1. Hooks (query, mutation, form, state)
  const { tenantId } = useAuth();
  const { data: rules, refetch } = useQuery({ ... });

  // 2. Derived state / handlers

  // 3. JSX
  return ( ... );
}
```

### Data Fetching

```tsx
// Always TanStack Query — never useEffect + fetch
const { data, isLoading, error } = useQuery({
  queryKey: ['rules', tenantId],
  queryFn: () => api.getRules(tenantId),
});

// Mutations always invalidate or refetch on success
const mutation = useMutation({
  mutationFn: (rule: CreateRuleDto) => api.createRule(tenantId, rule),
  onSuccess: () => refetch(),
});
```

### Type Safety

```tsx
// Always type API responses — no `any`
async function apiCall<T>(path: string, options?: RequestInit): Promise<T>

// Use Zod schemas for form validation
const schema = z.object({
  name: z.string().min(1),
  temperature: z.number().min(0).max(1),
});
```

---

## Git Conventions

```
feat: add dynamic rule learning service
fix: correct tenant isolation in DivaDbContext query filter
refactor: extract prompt merge logic into TenantAwarePromptBuilder
docs: update phase-08 agent registry section
test: add integration tests for SessionService
chore: update SK package to 1.x
```

- Commits reference the phase where relevant: `feat(phase-11): add LlmRuleExtractor`
- One logical change per commit
- Never commit `.env` or files matching `.env.*` (except `.env.example`)
