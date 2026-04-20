namespace Diva.Agents.Archetypes;

using System.Collections.Concurrent;
using Diva.Core.Configuration;
public sealed class ArchetypeRegistry : IArchetypeRegistry
{
    private readonly ConcurrentDictionary<string, AgentArchetype> _archetypes = new(StringComparer.OrdinalIgnoreCase);

    public ArchetypeRegistry()
    {
        foreach (var (id, archetype) in BuiltInArchetypes.All)
            _archetypes[id] = archetype;
    }

    public IReadOnlyList<AgentArchetype> GetAll() => [.. _archetypes.Values];

    public AgentArchetype? GetById(string archetypeId) =>
        _archetypes.GetValueOrDefault(archetypeId);

    public void Register(AgentArchetype archetype) =>
        _archetypes[archetype.Id] = archetype;
}
