using Diva.Core.Configuration;
using Diva.Infrastructure.LiteLLM;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Diva.Agents.Tests;

public class AgentToolProviderTests
{
    private readonly IAgentDelegationResolver _resolver = Substitute.For<IAgentDelegationResolver>();

    private readonly AgentToolProvider _sut;

    public AgentToolProviderTests()
    {
        _sut = new AgentToolProvider(_resolver, NullLogger<AgentToolProvider>.Instance);
    }

    [Fact]
    public async Task BuildAgentToolsAsync_ValidIds_ReturnsTools()
    {
        _resolver.GetAgentInfoAsync("1", 10, Arg.Any<CancellationToken>())
            .Returns(new DelegateAgentInfo("1", "WeatherBot", "Checks weather", ["weather"]));
        _resolver.GetAgentInfoAsync("2", 10, Arg.Any<CancellationToken>())
            .Returns(new DelegateAgentInfo("2", "EmailBot", "Sends emails", ["email"]));

        var tools = await _sut.BuildAgentToolsAsync("[\"1\",\"2\"]", 10, "99", CancellationToken.None);

        Assert.Equal(2, tools.Count);
        Assert.StartsWith("call_agent_", tools[0].Name);
        Assert.Contains("WeatherBot", tools[0].Description);
        Assert.Contains("EmailBot", tools[1].Description);
    }

    [Fact]
    public async Task BuildAgentToolsAsync_SkipsSelfDelegation()
    {
        _resolver.GetAgentInfoAsync("5", 1, Arg.Any<CancellationToken>())
            .Returns(new DelegateAgentInfo("5", "Self", null, null));

        var tools = await _sut.BuildAgentToolsAsync("[\"5\"]", 1, "5", CancellationToken.None);

        Assert.Empty(tools);
    }

    [Fact]
    public async Task BuildAgentToolsAsync_SkipsMissingAgents()
    {
        _resolver.GetAgentInfoAsync("999", 1, Arg.Any<CancellationToken>())
            .Returns((DelegateAgentInfo?)null);

        var tools = await _sut.BuildAgentToolsAsync("[\"999\"]", 1, "0", CancellationToken.None);

        Assert.Empty(tools);
    }

    [Fact]
    public async Task BuildAgentToolsAsync_InvalidJson_ReturnsEmpty()
    {
        var tools = await _sut.BuildAgentToolsAsync("not-json", 1, "0", CancellationToken.None);

        Assert.Empty(tools);
    }

    [Fact]
    public async Task BuildAgentToolsAsync_EmptyArray_ReturnsEmpty()
    {
        var tools = await _sut.BuildAgentToolsAsync("[]", 1, "0", CancellationToken.None);

        Assert.Empty(tools);
    }

    [Fact]
    public async Task BuildAgentToolsAsync_NullIds_ReturnsEmpty()
    {
        var tools = await _sut.BuildAgentToolsAsync("null", 1, "0", CancellationToken.None);

        Assert.Empty(tools);
    }
}
