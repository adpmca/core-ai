using Diva.Agents.Workers;

namespace Diva.Agents.Registry;

/// <summary>
/// Default scoring: capability intersection count (primary) then Priority (tie-break).
/// Falls back to highest-priority agent when no capability overlaps.
/// </summary>
public sealed class CapabilityScoringService : ICapabilityScoringService
{
    public IWorkerAgent? FindBestMatch(IReadOnlyList<IWorkerAgent> candidates, string[] requiredCapabilities)
    {
        if (candidates.Count == 0) return null;

        if (requiredCapabilities.Length == 0)
            return candidates.OrderByDescending(a => a.GetCapability().Priority).First();

        var best = candidates
            .Select(a => (Agent: a, Score: a.GetCapability().Capabilities
                .Intersect(requiredCapabilities, StringComparer.OrdinalIgnoreCase)
                .Count()))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Agent.GetCapability().Priority)
            .Select(x => x.Agent)
            .FirstOrDefault();

        return best ?? candidates.OrderByDescending(a => a.GetCapability().Priority).First();
    }
}
