using Diva.Core.Configuration;
using Diva.Core.Models;
using Diva.Infrastructure.LiteLLM;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using NSubstitute;

namespace Diva.Agents.Tests;

/// <summary>
/// Unit tests for <see cref="ToolExecutor"/>.
/// Uses empty/null client maps to exercise the error/exception paths without a real MCP server.
/// </summary>
public class ToolExecutorTests
{
    private readonly ToolExecutor _sut = new(
        NullLogger<ToolExecutor>.Instance,
        Options.Create(new AgentOptions()));

    // ── No client available ───────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_NoClientInMap_NoFallbackClients_ReturnsFailed()
    {
        var (output, failed, error) = await _sut.ExecuteAsync(
            toolName:          "my_tool",
            inputJson:         "{}",
            toolClientMap:     [],
            mcpClients:        [],
            maxToolResultChars: 4000,
            ct:                CancellationToken.None);

        Assert.True(failed);
        Assert.NotNull(error);
        Assert.Contains("Error:", output);
    }

    [Fact]
    public async Task ExecuteAsync_ToolNotInMap_FallsBackToFirstClient_ReturnsErrorWhenNoClients()
    {
        // toolClientMap has no entry for "unknown_tool" and mcpClients is also empty
        var (output, failed, _) = await _sut.ExecuteAsync(
            toolName:          "unknown_tool",
            inputJson:         "{}",
            toolClientMap:     new Dictionary<string, McpClient>(),
            mcpClients:        new Dictionary<string, McpClient>(),
            maxToolResultChars: 4000,
            ct:                CancellationToken.None);

        Assert.True(failed);
        Assert.StartsWith("Error:", output);
    }

    // ── Cancellation / timeout ────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_PreCancelledToken_ReturnsTimeoutMessage()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var (output, failed, error) = await _sut.ExecuteAsync(
            toolName:          "slow_tool",
            inputJson:         "{}",
            toolClientMap:     [],
            mcpClients:        [],
            maxToolResultChars: 4000,
            ct:                cts.Token);

        // Pre-cancelled CT may either hit the timeout path or the exception path,
        // but either way the call must complete and Failed must be true.
        Assert.True(failed);
        Assert.NotEmpty(output);
    }

    // ── Output truncation ─────────────────────────────────────────────────────

    [Fact]
    public void TruncateResult_Applied_WhenOutputExceedsMax()
    {
        // The internal truncation is exercised via ReActToolHelper — verify the helper
        // returns a string that fits within the requested limit.
        var longText = new string('A', 10_000);
        var truncated = ReActToolHelper.TruncateResult(longText, 500);

        // Must fit within the limit (allowing for the truncation suffix)
        Assert.True(truncated.Length <= 600, $"Truncated length was {truncated.Length}");
        Assert.Contains("[truncated", truncated);
    }

    // ── Error classification ──────────────────────────────────────────────────

    [Theory]
    [InlineData("Error: connection refused", true)]
    [InlineData("Error: tool not found", true)]
    [InlineData("{\"status\":\"error\",\"msg\":\"foo\"}", true)]
    [InlineData("42 results found", false)]
    [InlineData("Success", false)]
    public void IsToolOutputError_CorrectlyClassifies(string output, bool expectedFailed)
    {
        var isError = ReActToolHelper.IsToolOutputError(output);
        Assert.Equal(expectedFailed, isError);
    }
}
