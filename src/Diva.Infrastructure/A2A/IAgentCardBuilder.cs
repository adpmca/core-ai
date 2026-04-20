namespace Diva.Infrastructure.A2A;

using Diva.Infrastructure.Data.Entities;

/// <summary>Interface for AgentCard generation — mockable in controller tests.</summary>
public interface IAgentCardBuilder
{
    object BuildCard(AgentDefinitionEntity agent, string baseUrl);
}
