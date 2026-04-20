using Diva.Infrastructure.Data.Entities;

namespace Diva.TenantAdmin.Services;

/// <summary>
/// Pure static mapper: converts TenantBusinessRuleEntity → virtual HookRuleEntity instances
/// for evaluation by RulePackEngine. No DB calls, no side effects (T4 — adapter purity constraint).
/// </summary>
public static class BusinessRuleAdapter
{
    private const int VirtualHeaderOrderInPack = -1;
    private const int VirtualPackPriority = 95;
    private const int VirtualPackMaxEvaluationMs = 500;

    /// <summary>
    /// Maps a TenantBusinessRuleEntity to a virtual HookRuleEntity understood by RulePackEngine.
    /// Uses negative ID  (-(br.Id)) so the log flusher can identify it as a business rule and
    /// populate RuleExecutionLogEntity.BusinessRuleId instead of RuleId (Gap #5).
    /// For inject_prompt rules, wraps PromptInjection in "- " to preserve existing bullet format (Gap #1).
    /// </summary>
    public static HookRuleEntity ToVirtualHookRule(TenantBusinessRuleEntity br)
    {
        // For inject_prompt type, prefix with "- " to match the bullet list the old
        // GetPromptInjectionsAsync produced (Gap #1: prevent prompt formatting regression).
        var instruction = br.HookRuleType == "inject_prompt"
            ? $"- {br.PromptInjection}"
            : br.PromptInjection;

        return new HookRuleEntity
        {
            Id              = -(br.Id),   // negative = virtual; log flusher maps back to BusinessRuleId
            PackId          = br.RulePackId ?? 0,
            HookPoint       = br.HookPoint,
            RuleType        = br.HookRuleType,
            Instruction     = instruction,
            Pattern         = br.Pattern,
            Replacement     = br.Replacement,
            ToolName        = br.ToolName,
            OrderInPack     = br.OrderInPack,
            StopOnMatch     = br.StopOnMatch,
            MaxEvaluationMs = br.MaxEvaluationMs,
            IsEnabled       = br.IsActive,
            Pack            = null!,   // virtual — never persisted
        };
    }

    /// <summary>
    /// Wraps standalone business rules (no RulePackId) into a virtual ResolvedRulePack named
    /// "__business_rules" at Priority=95 so they evaluate between registered packs.
    /// For inject_prompt rules (OnInit hook point), inserts a synthetic "## Business Rules" header
    /// rule at OrderInPack=-1, preserving the section format of the previous prompt-builder output (Gap #1).
    /// </summary>
    public static ResolvedRulePack WrapAsVirtualPack(List<TenantBusinessRuleEntity> standaloneRules)
    {
        var rules = new List<HookRuleEntity>();

        // Add synthetic header only when there are inject_prompt rules at OnInit
        bool hasInjectPrompt = standaloneRules.Any(
            r => r.HookRuleType == "inject_prompt" && r.HookPoint == "OnInit");

        if (hasInjectPrompt)
        {
            rules.Add(new HookRuleEntity
            {
                Id          = 0,    // synthetic — not a real business rule ID
                PackId      = 0,
                HookPoint   = "OnInit",
                RuleType    = "inject_prompt",
                Instruction = "## Business Rules",
                OrderInPack = VirtualHeaderOrderInPack,
                IsEnabled   = true,
                Pack        = null!,
            });
        }

        var virtualRules = standaloneRules
            .OrderBy(br => br.OrderInPack)
            .ThenBy(br => br.Priority)
            .Select(ToVirtualHookRule)
            .ToList();

        rules.AddRange(virtualRules);

        return new ResolvedRulePack(
            0, "__business_rules", VirtualPackPriority, VirtualPackMaxEvaluationMs, rules);
    }

    /// <summary>
    /// Merges virtual business-rule entries into an existing pack's rules list.
    /// Native hook rules take precedence over business rules at equal OrderInPack (Gap #6 tie-breaking).
    /// Returns a new merged list; the original list is not mutated.
    /// </summary>
    public static List<HookRuleEntity> MergeIntoPackRules(
        List<HookRuleEntity> nativeRules,
        IEnumerable<TenantBusinessRuleEntity> linkedRules)
    {
        var virtualRules = linkedRules.Select(ToVirtualHookRule).ToList();

        return nativeRules
            .Concat(virtualRules)
            // Gap #6: stable sort — native (Id >= 0) before virtual (Id < 0) at equal OrderInPack
            .OrderBy(r => r.OrderInPack)
            .ThenBy(r => r.Id < 0 ? 1 : 0)
            .ToList();
    }
}
