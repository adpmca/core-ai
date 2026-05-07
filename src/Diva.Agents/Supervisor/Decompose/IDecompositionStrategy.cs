namespace Diva.Agents.Supervisor.Decompose;

/// <summary>
/// Transforms a SupervisorState into one or more SubTasks.
/// Implementations must be Singleton-safe (no scoped dependencies).
/// New strategies are registered in DI and discovered automatically by DecompositionStrategySelector.
/// </summary>
public interface IDecompositionStrategy
{
    /// <summary>
    /// Priority used by DecompositionStrategySelector. Higher value = selected first.
    /// SingleTaskStrategy = 0 (fallback). LlmDecompositionStrategy = 10.
    /// </summary>
    int Priority { get; }

    /// <summary>Returns true when this strategy can handle the given state.</summary>
    bool CanHandle(SupervisorState state);

    /// <summary>Decomposes the request into sub-tasks. Never returns an empty list.</summary>
    Task<List<SubTask>> DecomposeAsync(SupervisorState state, CancellationToken ct);
}
