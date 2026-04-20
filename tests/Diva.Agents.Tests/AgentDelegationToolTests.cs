using Diva.Infrastructure.LiteLLM;
using Microsoft.Extensions.AI;

namespace Diva.Agents.Tests;

public class AgentDelegationToolTests
{
    [Fact]
    public void Constructor_SetsNameWithPrefix()
    {
        var tool = new AgentDelegationTool("42", "WeatherBot", "Checks weather", ["weather"]);

        Assert.StartsWith("call_agent_", tool.Name);
        Assert.Contains("WeatherBot", tool.Name);
        Assert.Contains("42", tool.Name);
    }

    [Fact]
    public void Constructor_SanitizesSpecialCharacters()
    {
        var tool = new AgentDelegationTool("1", "My Agent (v2)", null, null);

        Assert.Equal("call_agent_MyAgentv2_1", tool.Name);
    }

    [Fact]
    public void Description_IncludesAgentName()
    {
        var tool = new AgentDelegationTool("1", "Helper", "A helpful assistant", null);

        Assert.Contains("Helper", tool.Description);
        Assert.Contains("A helpful assistant", tool.Description);
    }

    [Fact]
    public void Description_IncludesCapabilities()
    {
        var tool = new AgentDelegationTool("1", "Bot", null, ["search", "email"]);

        Assert.Contains("search", tool.Description);
        Assert.Contains("email", tool.Description);
    }

    [Fact]
    public void Description_TruncatesLongDescription()
    {
        var longDesc = new string('a', 300);
        var tool = new AgentDelegationTool("1", "Bot", longDesc, null);

        // Should be truncated to ~200 chars + ellipsis + prefix text
        Assert.True(tool.Description.Length < 300);
    }

    [Fact]
    public void JsonSchema_HasQueryAndContextProperties()
    {
        var tool = new AgentDelegationTool("1", "Bot", null, null);
        var schemaText = tool.JsonSchema.GetRawText();

        Assert.Contains("\"query\"", schemaText);
        Assert.Contains("\"context\"", schemaText);
        Assert.Contains("\"required\"", schemaText);
    }

    [Fact]
    public void AgentId_ReturnsConstructorValue()
    {
        var tool = new AgentDelegationTool("99", "Bot", null, null);

        Assert.Equal("99", tool.AgentId);
    }

    [Fact]
    public void IsAgentDelegationTool_MatchesPrefix()
    {
        Assert.True(AgentDelegationTool.IsAgentDelegationTool("call_agent_Bot_1"));
        Assert.False(AgentDelegationTool.IsAgentDelegationTool("some_other_tool"));
        Assert.False(AgentDelegationTool.IsAgentDelegationTool(""));
    }

    [Fact]
    public async Task InvokeCoreAsync_ThrowsInvalidOperation()
    {
        var tool = new AgentDelegationTool("1", "Bot", null, null);

        // InvokeCoreAsync is protected — invoke via the public InvokeAsync entrypoint
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => tool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>())).AsTask());
    }
}
