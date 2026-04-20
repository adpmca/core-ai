namespace Diva.Infrastructure.Data.Entities;

public class TenantBusinessRuleEntity : ITenantEntity
{
    public int Id { get; set; }
    /// <summary>Stable external identifier returned in API responses. Never changes after creation.</summary>
    public string Guid { get; set; } = System.Guid.NewGuid().ToString();
    public int TenantId { get; set; }
    public string AgentType { get; set; } = "*";           // "*" = all agents
    /// <summary>
    /// When set, this rule applies only to the agent with this ID.
    /// When null, applies to all agents matching AgentType (existing behavior).
    /// Soft FK — no cascade constraint, same pattern as AgentType string.
    /// </summary>
    public string? AgentId { get; set; }
    public string RuleCategory { get; set; } = string.Empty;
    public string RuleKey { get; set; } = string.Empty;
    public string? RuleValueJson { get; set; }             // JSON for structured values
    public string? PromptInjection { get; set; }           // text injected into agent prompt
    public bool IsActive { get; set; } = true;
    public int Priority { get; set; } = 100;               // lower = applied first
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // ── Hook pipeline integration fields ──────────────────────────────────────
    /// <summary>Optional FK to a rule pack. null = standalone (evaluated in virtual "__business_rules" pack).</summary>
    public int? RulePackId { get; set; }

    /// <summary>Hook point where this rule fires. Default "OnInit" matches existing prompt-injection behaviour.</summary>
    public string HookPoint { get; set; } = "OnInit";

    /// <summary>Rule type as understood by RulePackEngine. Default "inject_prompt" preserves existing behaviour.</summary>
    public string HookRuleType { get; set; } = "inject_prompt";

    /// <summary>Regex or keyword pattern (block_pattern, regex_redact, require_keyword, format_enforce, tool_require, tool_transform).</summary>
    public string? Pattern { get; set; }

    /// <summary>Replacement text (regex_redact, tool_transform).</summary>
    public string? Replacement { get; set; }

    /// <summary>Tool name (tool_require, tool_transform, model_switch).</summary>
    public string? ToolName { get; set; }

    /// <summary>Execution order within the assigned pack. Lower = runs first.</summary>
    public int OrderInPack { get; set; } = 0;

    /// <summary>If true, stop evaluating remaining rules in this pack when this rule matches.</summary>
    public bool StopOnMatch { get; set; } = false;

    /// <summary>Per-rule evaluation timeout (ms).</summary>
    public int MaxEvaluationMs { get; set; } = 100;

    /// <summary>When set, this rule was activated from a group rule template. Soft ref — no FK constraint.</summary>
    public int? SourceGroupRuleId { get; set; }

    // Navigation
    public HookRulePackEntity? RulePack { get; set; }
}

public class TenantPromptOverrideEntity : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string AgentType { get; set; } = "*";
    public string Section { get; set; } = string.Empty;   // matches prompt template section
    public string CustomText { get; set; } = string.Empty;
    public string MergeMode { get; set; } = "Append";     // "Append" | "Prepend" | "Replace"
    public bool IsActive { get; set; } = true;
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Non-null when this override was activated from a group prompt template.</summary>
    public int? SourceGroupOverrideId { get; set; }
    /// <summary>When non-null, this override applies only to the specific agent with this ID (takes priority over archetype-only overrides).</summary>
    public string? AgentId { get; set; }
}
