namespace Diva.Infrastructure.Data.Entities;

/// <summary>
/// Named, versioned bundle of hook rules applied as a unit.
/// Packs scope to a tenant (direct) or group (shared across group tenants).
/// Platform-wide starter packs have TenantId=0, GroupId=null.
/// </summary>
public class HookRulePackEntity : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int? GroupId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Version { get; set; } = "1.0";

    /// <summary>Pack execution order relative to other packs. Lower = runs first.</summary>
    public int Priority { get; set; } = 100;

    public bool IsEnabled { get; set; } = true;

    /// <summary>Mandatory packs cannot be disabled by tenant admins.</summary>
    public bool IsMandatory { get; set; }

    /// <summary>
    /// JSON array of archetype/agent-type names this pack applies to.
    /// null = applies to all agents.
    /// Example: ["data-analyst","rag"]
    /// </summary>
    public string? AppliesToJson { get; set; }

    /// <summary>
    /// Conditional activation expression. null = always active.
    /// Prefix-based: "regex:pattern", "tool:tool-name", "archetype:name"
    /// </summary>
    public string? ActivationCondition { get; set; }

    /// <summary>FK to parent pack for inheritance. null = root pack.</summary>
    public int? ParentPackId { get; set; }

    /// <summary>Maximum total evaluation time for all rules in this pack (ms).</summary>
    public int MaxEvaluationMs { get; set; } = 500;

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedAt { get; set; }

    // Navigation
    public HookRulePackEntity? ParentPack { get; set; }
    public List<HookRulePackEntity> ChildPacks { get; set; } = [];
    public List<HookRuleEntity> Rules { get; set; } = [];
    public TenantGroupEntity? Group { get; set; }
    /// <summary>Business rules directly assigned to this pack. Read-only in most contexts; mutate via TenantBusinessRulesService.</summary>
    public List<TenantBusinessRuleEntity> LinkedBusinessRules { get; set; } = [];
}

/// <summary>
/// Individual rule inside a Rule Pack.
/// Rules execute in OrderInPack sequence within their parent pack.
/// </summary>
public class HookRuleEntity
{
    public int Id { get; set; }
    public int PackId { get; set; }

    /// <summary>Hook point where this rule fires: "OnInit", "OnBeforeResponse", etc.</summary>
    public string HookPoint { get; set; } = "OnBeforeResponse";

    /// <summary>
    /// Rule type: inject_prompt, tool_require, format_response, regex_redact,
    /// append_text, block_pattern, require_keyword, format_enforce, tool_transform
    /// </summary>
    public string RuleType { get; set; } = string.Empty;

    /// <summary>Regex or keyword pattern (used by regex_redact, block_pattern, require_keyword, tool_require).</summary>
    public string? Pattern { get; set; }

    /// <summary>Instruction text (used by inject_prompt, format_response, format_enforce, append_text).</summary>
    public string? Instruction { get; set; }

    /// <summary>Replacement text (used by regex_redact).</summary>
    public string? Replacement { get; set; }

    /// <summary>Tool name (used by tool_require, tool_transform).</summary>
    public string? ToolName { get; set; }

    /// <summary>
    /// For model_switch at OnBeforeIteration: which text Pattern is matched against.
    /// "query" (default) = user's original query. "response" = previous iteration response.
    /// </summary>
    public string MatchTarget { get; set; } = "query";

    /// <summary>Execution order within the pack. Lower = runs first.</summary>
    public int OrderInPack { get; set; } = 1;

    /// <summary>If true, stop evaluating remaining rules in this pack when this rule matches.</summary>
    public bool StopOnMatch { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>FK to parent pack rule this overrides (for inherited packs). null = original rule.</summary>
    public int? OverridesParentRuleId { get; set; }

    /// <summary>Per-rule evaluation timeout (ms).</summary>
    public int MaxEvaluationMs { get; set; } = 100;

    // Navigation
    public HookRulePackEntity Pack { get; set; } = null!;
    public HookRuleEntity? OverridesParentRule { get; set; }
}

/// <summary>
/// Tracks which rules fired, what action they took, and how long they took.
/// Written in batches for observability dashboards.
/// </summary>
public class RuleExecutionLogEntity
{
    public long Id { get; set; }
    public int PackId { get; set; }
    public int RuleId { get; set; }
    public string? AgentId { get; set; }
    public int TenantId { get; set; }

    /// <summary>Whether the rule's condition matched.</summary>
    public bool Triggered { get; set; }

    /// <summary>What the rule did: "modified", "blocked", "skipped", "appended", "injected"</summary>
    public string Action { get; set; } = "skipped";

    public int ElapsedMs { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    /// <summary>Set when the evaluated rule originated from a TenantBusinessRuleEntity (virtual rule, Id < 0).</summary>
    public int? BusinessRuleId { get; set; }
}
