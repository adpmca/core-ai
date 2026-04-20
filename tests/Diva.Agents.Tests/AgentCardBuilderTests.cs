using Diva.Agents.Tests.Helpers;
using Diva.Core.Configuration;
using Diva.Core.Models;
using Diva.Infrastructure.A2A;
using Diva.Infrastructure.Data.Entities;
using NSubstitute;

namespace Diva.Agents.Tests;

/// <summary>
/// Unit tests for <see cref="AgentCardBuilder"/>.
/// </summary>
public class AgentCardBuilderTests
{
    private readonly IArchetypeRegistry _archetypes = Substitute.For<IArchetypeRegistry>();
    private readonly AgentCardBuilder _sut;

    public AgentCardBuilderTests()
    {
        var opts = AgentTestFixtures.Opts(new A2AOptions { BaseUrl = "https://diva.example.com" });
        _sut = new AgentCardBuilder(_archetypes, opts);
    }

    [Fact]
    public void BuildCard_ReturnsRequiredFields()
    {
        var agent = new AgentDefinitionEntity
        {
            Id = "weather-1",
            Name = "weather",
            DisplayName = "Weather Agent",
            Description = "Checks weather data",
            AgentType = "weather",
            Capabilities = """["weather","forecast"]""",
            Version = 2,
        };
        _archetypes.GetById(Arg.Any<string>()).Returns((AgentArchetype?)null);

        dynamic card = _sut.BuildCard(agent, "https://fallback.example.com");

        Assert.Equal("Weather Agent", (string)card.name);
        Assert.Equal("Checks weather data", (string)card.description);
        Assert.Contains("weather-1", (string)card.url);
        Assert.Equal("2", (string)card.version);
        Assert.True((bool)card.capabilities.streaming);
    }

    [Fact]
    public void BuildCard_UsesArchetypeCapabilities_WhenAgentCapabilitiesEmpty()
    {
        var agent = new AgentDefinitionEntity
        {
            Id = "agent-2",
            Name = "general",
            DisplayName = "General",
            Description = "General purpose",
            AgentType = "general",
            ArchetypeId = "general",
            Capabilities = null,
        };
        _archetypes.GetById("general").Returns(new AgentArchetype
        {
            Id = "general",
            DisplayName = "General",
            DefaultCapabilities = ["chat", "qa"],
        });

        dynamic card = _sut.BuildCard(agent, "https://fallback.example.com");

        Assert.Equal(2, ((object[])card.skills).Length);
    }

    [Fact]
    public void BuildCard_UsesBaseUrlFromOptions_OverFallback()
    {
        var agent = new AgentDefinitionEntity
        {
            Id = "agent-3",
            Name = "test",
            DisplayName = "Test",
            Description = "Test agent",
            AgentType = "generic",
        };
        _archetypes.GetById(Arg.Any<string>()).Returns((AgentArchetype?)null);

        dynamic card = _sut.BuildCard(agent, "https://fallback.example.com");

        Assert.StartsWith("https://diva.example.com", (string)card.url);
    }

    [Fact]
    public void BuildCard_UsesFallbackUrl_WhenBaseUrlNull()
    {
        var opts = AgentTestFixtures.Opts(new A2AOptions { BaseUrl = null });
        var sut = new AgentCardBuilder(_archetypes, opts);
        var agent = new AgentDefinitionEntity
        {
            Id = "agent-4",
            Name = "test",
            DisplayName = "Test",
            Description = "Test",
            AgentType = "generic",
        };
        _archetypes.GetById(Arg.Any<string>()).Returns((AgentArchetype?)null);

        dynamic card = sut.BuildCard(agent, "https://fallback.example.com");

        Assert.StartsWith("https://fallback.example.com", (string)card.url);
    }

    [Fact]
    public void BuildCard_UsesName_WhenDisplayNameEmpty()
    {
        var agent = new AgentDefinitionEntity
        {
            Id = "agent-5",
            Name = "my-agent",
            DisplayName = "",
            Description = "No display name",
            AgentType = "generic",
        };
        _archetypes.GetById(Arg.Any<string>()).Returns((AgentArchetype?)null);

        dynamic card = _sut.BuildCard(agent, "https://fallback.example.com");

        Assert.Equal("my-agent", (string)card.name);
    }

    [Fact]
    public void BuildCard_IncludesBearerAuthScheme()
    {
        var agent = new AgentDefinitionEntity
        {
            Id = "agent-6",
            Name = "test",
            DisplayName = "Test",
            Description = "Test",
            AgentType = "generic",
        };
        _archetypes.GetById(Arg.Any<string>()).Returns((AgentArchetype?)null);

        dynamic card = _sut.BuildCard(agent, "https://x.com");

        string[] schemes = ((object[])card.authentication.schemes).Cast<string>().ToArray();
        Assert.Contains("Bearer", schemes);
    }
}
