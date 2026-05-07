namespace Diva.Agents.Supervisor.Decompose;

/// <summary>
/// Selects the highest-priority IDecompositionStrategy whose CanHandle() returns true.
/// Strategies are injected via IEnumerable&lt;IDecompositionStrategy&gt; and sorted by Priority
/// descending at construction time. SingleTaskStrategy (Priority=0) always matches,
/// so the selector never throws in a correctly configured DI container.
/// </summary>
public sealed class DecompositionStrategySelector
{
    private readonly IReadOnlyList<IDecompositionStrategy> _strategies;

    public DecompositionStrategySelector(IEnumerable<IDecompositionStrategy> strategies)
    {
        _strategies = [.. strategies.OrderByDescending(s => s.Priority)];
    }

    public IDecompositionStrategy Select(SupervisorState state)
    {
        foreach (var strategy in _strategies)
            if (strategy.CanHandle(state))
                return strategy;

        throw new InvalidOperationException(
            "No IDecompositionStrategy could handle the current state. " +
            "Ensure SingleTaskStrategy is registered as IDecompositionStrategy.");
    }
}
