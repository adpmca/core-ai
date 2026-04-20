using Diva.Core.Configuration;
using Diva.Core.Models;
using Diva.Infrastructure.LiteLLM;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Diva.Agents.Tests;

public class AgentToolExecutorTests
{
    private readonly IAgentDelegationResolver _resolver = Substitute.For<IAgentDelegationResolver>();
    private readonly A2AOptions _a2aOpts = new() { MaxDelegationDepth = 3 };
    private readonly AgentOptions _agentOpts = new() { ToolTimeoutSeconds = 30, SubAgentTimeoutSeconds = 120 };
    private readonly AgentToolExecutor _sut;
    private readonly TenantContext _tenant = TenantContext.System(1);

    public AgentToolExecutorTests()
    {
        _sut = new AgentToolExecutor(
            _resolver,
            Options.Create(_a2aOpts),
            Options.Create(_agentOpts),
            NullLogger<AgentToolExecutor>.Instance);
    }

    private static AgentDelegationTool MakeTool(string id = "42", string name = "Helper") =>
        new(id, name, "A helper agent", ["general"]);

    // ── Depth guard ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_AtMaxDepth_ReturnsDepthLimitError()
    {
        var tool = MakeTool();
        var (output, failed, _) = await _sut.ExecuteAsync(
            tool, """{"query":"hi"}""", _tenant, currentDepth: 3, 4000, CancellationToken.None);

        Assert.True(failed);
        Assert.Contains("depth limit", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_AboveMaxDepth_ReturnsDepthLimitError()
    {
        var tool = MakeTool();
        var (output, failed, _) = await _sut.ExecuteAsync(
            tool, """{"query":"hi"}""", _tenant, currentDepth: 10, 4000, CancellationToken.None);

        Assert.True(failed);
        Assert.Contains("depth limit", output, StringComparison.OrdinalIgnoreCase);
    }

    // ── Input parsing ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_MissingQuery_ReturnsError()
    {
        var tool = MakeTool();
        var (output, failed, _) = await _sut.ExecuteAsync(
            tool, """{"context":"only context"}""", _tenant, 0, 4000, CancellationToken.None);

        Assert.True(failed);
        Assert.Contains("query", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_MalformedJson_ReturnsError()
    {
        var tool = MakeTool();
        var (output, failed, error) = await _sut.ExecuteAsync(
            tool, "not json at all", _tenant, 0, 4000, CancellationToken.None);

        Assert.True(failed);
        Assert.NotNull(error);
        Assert.Contains("Invalid input JSON", output);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyQuery_ReturnsError()
    {
        var tool = MakeTool();
        var (output, failed, _) = await _sut.ExecuteAsync(
            tool, """{"query":""}""", _tenant, 0, 4000, CancellationToken.None);

        Assert.True(failed);
        Assert.Contains("query", output, StringComparison.OrdinalIgnoreCase);
    }

    // ── Successful delegation ─────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_SuccessfulDelegation_ReturnsContent()
    {
        var tool = MakeTool();
        _resolver.ExecuteAgentAsync("42", Arg.Any<AgentRequest>(), _tenant, Arg.Any<CancellationToken>())
            .Returns(new AgentResponse { Content = "The weather is sunny.", Success = true });

        var (output, failed, _) = await _sut.ExecuteAsync(
            tool, """{"query":"What is the weather?"}""", _tenant, 0, 4000, CancellationToken.None);

        Assert.False(failed);
        Assert.Contains("sunny", output);
    }

    [Fact]
    public async Task ExecuteAsync_WithContext_AppendsToQuery()
    {
        var tool = MakeTool();
        AgentRequest? captured = null;
        _resolver.ExecuteAgentAsync("42", Arg.Do<AgentRequest>(r => captured = r), _tenant, Arg.Any<CancellationToken>())
            .Returns(new AgentResponse { Content = "Done", Success = true });

        await _sut.ExecuteAsync(
            tool, """{"query":"lookup","context":"for tenant XYZ"}""", _tenant, 0, 4000, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Contains("tenant XYZ", captured!.Query);
    }

    [Fact]
    public async Task ExecuteAsync_DepthPropagated_InMetadata()
    {
        var tool = MakeTool();
        AgentRequest? captured = null;
        _resolver.ExecuteAgentAsync("42", Arg.Do<AgentRequest>(r => captured = r), _tenant, Arg.Any<CancellationToken>())
            .Returns(new AgentResponse { Content = "ok", Success = true });

        await _sut.ExecuteAsync(
            tool, """{"query":"test"}""", _tenant, currentDepth: 1, 4000, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(2, captured!.Metadata!["a2a_local_depth"]);
    }

    // ── Agent failure ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_AgentThrows_ReturnsFailed()
    {
        var tool = MakeTool();
        _resolver.ExecuteAgentAsync("42", Arg.Any<AgentRequest>(), _tenant, Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Agent crashed"));

        var (output, failed, error) = await _sut.ExecuteAsync(
            tool, """{"query":"go"}""", _tenant, 0, 4000, CancellationToken.None);

        Assert.True(failed);
        Assert.NotNull(error);
        Assert.Contains("Agent crashed", output);
    }

    [Fact]
    public async Task ExecuteAsync_AgentReturnsFailed_MarksOutputAsFailed()
    {
        var tool = MakeTool();
        _resolver.ExecuteAgentAsync("42", Arg.Any<AgentRequest>(), _tenant, Arg.Any<CancellationToken>())
            .Returns(new AgentResponse { Content = "Something went wrong", Success = false });

        var (output, failed, _) = await _sut.ExecuteAsync(
            tool, """{"query":"run"}""", _tenant, 0, 4000, CancellationToken.None);

        Assert.True(failed);
        Assert.Contains("Something went wrong", output);
    }

    // ── ForwardSsoToMcp propagation ───────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ForwardSsoTrue_PropagatedToRequest()
    {
        var tool = MakeTool();
        AgentRequest? captured = null;
        _resolver.ExecuteAgentAsync("42", Arg.Do<AgentRequest>(r => captured = r), _tenant, Arg.Any<CancellationToken>())
            .Returns(new AgentResponse { Content = "ok", Success = true });

        await _sut.ExecuteAsync(
            tool, """{"query":"test"}""", _tenant, currentDepth: 0, 4000, CancellationToken.None, forwardSsoToMcp: true);

        Assert.NotNull(captured);
        Assert.True(captured!.ForwardSsoToMcp);
    }

    [Fact]
    public async Task ExecuteAsync_ForwardSsoDefault_IsFalseOnRequest()
    {
        var tool = MakeTool();
        AgentRequest? captured = null;
        _resolver.ExecuteAgentAsync("42", Arg.Do<AgentRequest>(r => captured = r), _tenant, Arg.Any<CancellationToken>())
            .Returns(new AgentResponse { Content = "ok", Success = true });

        await _sut.ExecuteAsync(
            tool, """{"query":"test"}""", _tenant, currentDepth: 0, 4000, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.False(captured!.ForwardSsoToMcp);
    }

    // ── Truncation ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_LongResponse_IsTruncated()
    {
        var tool = MakeTool();
        var longContent = new string('x', 5000);
        _resolver.ExecuteAgentAsync("42", Arg.Any<AgentRequest>(), _tenant, Arg.Any<CancellationToken>())
            .Returns(new AgentResponse { Content = longContent, Success = true });

        var (output, _, _) = await _sut.ExecuteAsync(
            tool, """{"query":"big"}""", _tenant, 0, maxToolResultChars: 100, CancellationToken.None);

        Assert.True(output.Length <= 200); // some overhead from truncation marker
    }

    // ── Timeout source ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_UsesSubAgentTimeout_NotToolTimeout()
    {
        // ToolTimeoutSeconds = 1 s (would fail), SubAgentTimeoutSeconds = 10 s (should succeed)
        var opts = new AgentOptions { ToolTimeoutSeconds = 1, SubAgentTimeoutSeconds = 10 };
        var sut = new AgentToolExecutor(
            _resolver,
            Options.Create(_a2aOpts),
            Options.Create(opts),
            NullLogger<AgentToolExecutor>.Instance);
        var tool = MakeTool();

        _resolver.ExecuteAgentAsync("42", Arg.Any<AgentRequest>(), _tenant, Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await Task.Delay(2000, call.Arg<CancellationToken>());
                return new AgentResponse { Content = "Done after 2s", Success = true };
            });

        var (output, failed, _) = await sut.ExecuteAsync(
            tool, """{"query":"slow task"}""", _tenant, 0, 4000, CancellationToken.None);

        // Should succeed — 2s < SubAgentTimeoutSeconds (10s).
        // Would fail if ToolTimeoutSeconds (1s) were used instead.
        Assert.False(failed);
        Assert.Contains("Done after 2s", output);
    }
}
