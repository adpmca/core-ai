using Diva.Agents.Workers;

namespace Diva.Agents.Registry;

/// <summary>
/// Full agent registry: read operations from IReadableAgentRegistry plus Register() for
/// static (code-defined) agent registration at startup. Pipeline stages and read-only
/// consumers should depend on IReadableAgentRegistry instead.
/// </summary>
public interface IAgentRegistry : IReadableAgentRegistry
{
    /// <summary>Register a static (code-defined) agent.</summary>
    void Register(IWorkerAgent agent);
}
