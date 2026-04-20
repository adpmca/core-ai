namespace Diva.Infrastructure.A2A;

using System.Text.Json;
using Diva.Core.Configuration;
using Diva.Infrastructure.Data.Entities;
using Microsoft.Extensions.Options;

public sealed class AgentCardBuilder : IAgentCardBuilder
{
    private readonly IArchetypeRegistry _archetypes;
    private readonly A2AOptions _a2aOptions;

    public AgentCardBuilder(IArchetypeRegistry archetypes, IOptions<A2AOptions> a2aOptions)
    {
        _archetypes = archetypes;
        _a2aOptions = a2aOptions.Value;
    }

    public object BuildCard(AgentDefinitionEntity agent, string baseUrl)
    {
        var archetype = _archetypes.GetById(agent.ArchetypeId ?? "general");
        var capabilities = string.IsNullOrEmpty(agent.Capabilities)
            ? archetype?.DefaultCapabilities ?? []
            : JsonSerializer.Deserialize<string[]>(agent.Capabilities) ?? [];

        var url = _a2aOptions.BaseUrl ?? baseUrl;

        return new
        {
            name = agent.DisplayName.Length > 0 ? agent.DisplayName : agent.Name,
            description = agent.Description,
            url = $"{url}/tasks/send?agentId={agent.Id}",
            version = agent.Version.ToString(),
            capabilities = new
            {
                streaming = true,
                pushNotifications = false,
            },
            skills = capabilities.Select(c => new
            {
                id = c,
                name = c,
                description = $"Capability: {c}",
            }).ToArray(),
            authentication = new
            {
                schemes = new[] { "Bearer" },
            },
            defaultInputModes = new[] { "text" },
            defaultOutputModes = new[] { "text" },
        };
    }
}
