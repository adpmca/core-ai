using Diva.Agents.Workers;

namespace Diva.Agents.Registry;

/// <summary>
/// Scores and ranks candidate agents against a set of required capabilities.
/// Extracted from DynamicAgentRegistry to be shared with ScopedAgentRegistry (Phase 19)
/// without duplication. Stateless — safe as Singleton.
/// </summary>
public interface ICapabilityScoringService
{
    /// <summary>
    /// Returns the best-matching agent from <paramref name="candidates"/>.
    /// When <paramref name="requiredCapabilities"/> is empty, returns the highest-priority agent.
    /// Returns null only when <paramref name="candidates"/> is empty.
    /// </summary>
    IWorkerAgent? FindBestMatch(IReadOnlyList<IWorkerAgent> candidates, string[] requiredCapabilities);
}
