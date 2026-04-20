namespace Diva.Core.Configuration;

/// <summary>
/// Read-only archetype lookup — placed in Diva.Core so both Infrastructure and Agents can use it.
/// Implementation lives in Diva.Agents.Archetypes.ArchetypeRegistry.
/// </summary>
public interface IArchetypeRegistry
{
    IReadOnlyList<AgentArchetype> GetAll();
    AgentArchetype? GetById(string archetypeId);
    void Register(AgentArchetype archetype);
}
