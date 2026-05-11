namespace Diva.Core.Optimization;

public sealed record RulePackSummary
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public bool IsEnabled { get; init; }
    public string? AppliesToJson { get; init; }
    public List<RuleSummary> Rules { get; init; } = [];
}

public sealed record RuleSummary
{
    public int Id { get; init; }
    public string RuleType { get; init; } = "";
    public string? Instruction { get; init; }
}

public interface IOptimizationRulePackAccessor
{
    Task<List<RulePackSummary>> GetPackSummariesAsync(int tenantId, CancellationToken ct);
    Task EnablePackAsync(int packId, int tenantId, CancellationToken ct);
}
