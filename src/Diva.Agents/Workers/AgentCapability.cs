namespace Diva.Agents.Workers;

/// <summary>
/// Describes what a worker agent is capable of, used for capability-based routing.
/// </summary>
public sealed record AgentCapability
{
    public string AgentId { get; init; } = "";
    public string AgentType { get; init; } = "";
    public string Description { get; init; } = "";
    public string[] Capabilities { get; init; } = [];
    public string[] SupportedTools { get; init; } = [];

    /// <summary>Higher priority = preferred when multiple agents match.</summary>
    public int Priority { get; init; } = 10;
}
