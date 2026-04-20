namespace Diva.Core.Models;

/// <summary>Tenant-available LLM configuration. Injected by LlmConfigContextEnricher.</summary>
public sealed record SetupLlmConfigDto(int Id, string Provider, string Model, string Label);

/// <summary>
/// Mutable context provided to <see cref="IAgentSetupAssistant"/> for prompt and rule pack suggestion.
/// <see cref="ISetupAssistantContextEnricher"/> implementations may mutate this class before the LLM call.
/// </summary>
public sealed class AgentSetupContext
{
    public int TenantId { get; set; }
    public string AgentName { get; set; } = string.Empty;
    public string AgentDescription { get; set; } = string.Empty;
    public string? ArchetypeId { get; set; }
    public string[] ToolNames { get; set; } = [];
    public string? AdditionalContext { get; set; }

    /// <summary>"create" or "refine". Refine mode passes existing content for targeted changes.</summary>
    public string Mode { get; set; } = "create";

    /// <summary>Existing system prompt for refine mode.</summary>
    public string? CurrentSystemPrompt { get; set; }

    /// <summary>Existing rule packs as JSON for refine mode.</summary>
    public string? CurrentRulePacksJson { get; set; }

    /// <summary>Populated by LlmConfigContextEnricher from the tenant's named LLM configs.</summary>
    public List<SetupLlmConfigDto> AvailableLlmConfigs { get; set; } = [];

    /// <summary>Saved agent ID — enables AgentToolsContextEnricher to discover MCP tools and delegates.</summary>
    public string? AgentId { get; set; }

    /// <summary>Delegate agent IDs sent by frontend; resolved to full info by AgentToolsContextEnricher.</summary>
    public string[] DelegateAgentIds { get; set; } = [];

    /// <summary>Populated by AgentToolsContextEnricher — actual MCP tool names + descriptions.</summary>
    public List<McpToolDetail> McpTools { get; set; } = [];

    /// <summary>Populated by AgentToolsContextEnricher — resolved delegate agent details.</summary>
    public List<DelegateAgentDetail> DelegateAgents { get; set; } = [];
}

/// <summary>MCP tool name and description discovered from a connected MCP server.</summary>
public sealed record McpToolDetail(string Name, string? Description);

/// <summary>Delegate sub-agent info for prompt generation context.</summary>
public sealed record DelegateAgentDetail(string Id, string Name, string? Description, string[]? Capabilities);

/// <summary>AI-generated system prompt suggestion.</summary>
public sealed class PromptSuggestionDto
{
    public string SystemPrompt { get; init; } = string.Empty;
    public string Rationale { get; init; } = string.Empty;
}

/// <summary>A single hook rule within a suggested rule pack.</summary>
public sealed class SuggestedHookRuleDto
{
    public string HookPoint { get; init; } = string.Empty;
    public string RuleType { get; init; } = string.Empty;
    public string? Pattern { get; init; }
    public string? Instruction { get; init; }
    public string? Replacement { get; init; }
    public string? ToolName { get; init; }
    public int Order { get; init; }
    public bool StopOnMatch { get; init; }
    /// <summary>Preferred LlmConfigId for model_switch rules (stored in HookRuleEntity.ToolName as int).</summary>
    public int? LlmConfigId { get; init; }
    /// <summary>Fallback model override string — only when LlmConfigId is unavailable.</summary>
    public string? ModelOverride { get; init; }
}

/// <summary>A complete suggested rule pack, including edit intent for refine mode.</summary>
public sealed class SuggestedRulePackDto
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Rationale { get; init; } = string.Empty;
    /// <summary>Edit intent for refine mode: "add" | "update" | "delete" | "keep".</summary>
    public string Operation { get; init; } = "add";
    /// <summary>Existing pack ID for update/delete/keep operations in refine mode.</summary>
    public int? ExistingPackId { get; init; }
    public List<SuggestedHookRuleDto> Rules { get; init; } = [];
}

/// <summary>Request to generate a regex pattern from natural-language intent and example strings.</summary>
public sealed class RegexSuggestionRequestDto
{
    public string IntentDescription { get; set; } = string.Empty;
    public string[] SampleMatches { get; set; } = [];
    public string[] SampleNonMatches { get; set; } = [];
    public string? RuleType { get; set; }
    public string? HookPoint { get; set; }
}

/// <summary>AI-generated and server-validated regex pattern.</summary>
public sealed class RegexSuggestionDto
{
    public string Pattern { get; init; } = string.Empty;
    public string Explanation { get; init; } = string.Empty;
    public string? Flags { get; init; }
    public List<string> Warnings { get; init; } = [];
    public List<string> PreviewMatches { get; init; } = [];
    public List<string> PreviewNonMatches { get; init; } = [];
}

// ── History DTOs ─────────────────────────────────────────────────────────────

public sealed class AgentPromptHistoryEntryDto
{
    public int Version { get; init; }
    public string SystemPrompt { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; }
    public string CreatedBy { get; init; } = string.Empty;
    /// <summary>"manual" | "assistant_create" | "assistant_refine" | "restore"</summary>
    public string Source { get; init; } = "manual";
    public string? Reason { get; init; }
}

public sealed class RulePackHistoryEntryDto
{
    public int Version { get; init; }
    public string RulesJson { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; }
    public string CreatedBy { get; init; } = string.Empty;
    /// <summary>"manual" | "assistant_create" | "assistant_refine" | "restore"</summary>
    public string Source { get; init; } = "manual";
    public string? Reason { get; init; }
}

public sealed class RestorePromptVersionRequestDto
{
    public string? Reason { get; set; }
}

public sealed class RestoreRulePackVersionRequestDto
{
    public string? Reason { get; set; }
}

// ── Interfaces (referenced by assistants) ────────────────────────────────────

/// <summary>Service for AI-assisted agent setup: system prompt and rule pack suggestion.</summary>
public interface IAgentSetupAssistant
{
    Task<PromptSuggestionDto> SuggestSystemPromptAsync(AgentSetupContext ctx, CancellationToken ct);
    Task<IReadOnlyList<SuggestedRulePackDto>> SuggestRulePacksAsync(AgentSetupContext ctx, CancellationToken ct);
    Task<RegexSuggestionDto> SuggestRegexAsync(RegexSuggestionRequestDto request, int tenantId, CancellationToken ct);

    // History
    Task<IReadOnlyList<AgentPromptHistoryEntryDto>> GetAgentPromptHistoryAsync(string agentId, int tenantId, CancellationToken ct);
    Task SavePromptVersionAsync(string agentId, int tenantId, string systemPrompt, string source, string? reason, string? createdBy, CancellationToken ct);
    Task<AgentPromptHistoryEntryDto?> RestorePromptVersionAsync(string agentId, int tenantId, int version, string? reason, string? restoredBy, CancellationToken ct);

    Task<IReadOnlyList<RulePackHistoryEntryDto>> GetRulePackHistoryAsync(int packId, int tenantId, CancellationToken ct);
    Task SaveRulePackVersionAsync(int packId, int tenantId, string rulesJson, string source, string? reason, string? createdBy, CancellationToken ct);
    Task<RulePackHistoryEntryDto?> RestoreRulePackVersionAsync(int packId, int tenantId, int version, string? reason, string? restoredBy, CancellationToken ct);
}

/// <summary>
/// Enriches <see cref="AgentSetupContext"/> before the LLM call in the Agent Setup Assistant.
/// Implementations are resolved from DI in registration order.
/// </summary>
public interface ISetupAssistantContextEnricher
{
    ValueTask EnrichAsync(AgentSetupContext ctx, CancellationToken ct);
}
