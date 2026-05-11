using Diva.Core.Optimization;

namespace Diva.TenantAdmin.Services;

public sealed class OptimizationRulePackAccessor : IOptimizationRulePackAccessor
{
    private readonly IRulePackService _packs;

    public OptimizationRulePackAccessor(IRulePackService packs) => _packs = packs;

    public async Task<List<RulePackSummary>> GetPackSummariesAsync(int tenantId, CancellationToken ct)
    {
        var packs = await _packs.GetPacksAsync(tenantId, ct);
        return packs.Select(p => new RulePackSummary
        {
            Id            = p.Id,
            Name          = p.Name,
            Description   = p.Description,
            IsEnabled     = p.IsEnabled,
            AppliesToJson = p.AppliesToJson,
            Rules         = p.Rules.Select(r => new RuleSummary
            {
                Id          = r.Id,
                RuleType    = r.RuleType,
                Instruction = r.Instruction
            }).ToList()
        }).ToList();
    }

    public async Task EnablePackAsync(int packId, int tenantId, CancellationToken ct)
    {
        var pack = await _packs.GetPackWithRulesAsync(tenantId, packId, ct)
            ?? throw new KeyNotFoundException($"Rule pack {packId} not found for tenant {tenantId}");

        await _packs.UpdatePackAsync(tenantId, packId, new UpdateRulePackDto(
            Name:               pack.Name,
            Description:        pack.Description,
            Version:            pack.Version,
            Priority:           pack.Priority,
            IsEnabled:          true,
            IsMandatory:        pack.IsMandatory,
            AppliesToJson:      pack.AppliesToJson,
            ActivationCondition: pack.ActivationCondition,
            MaxEvaluationMs:    pack.MaxEvaluationMs), ct);
    }
}
