using Anthropic.SDK.Messaging;
using Diva.Agents.Tests.Helpers;
using Diva.Core.Configuration;
using Diva.Infrastructure.Context;
using Microsoft.Extensions.Options;
using Diva.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Diva.Infrastructure.LiteLLM;
using Diva.Infrastructure.Learning;
using Diva.Infrastructure.Sessions;
using Diva.Infrastructure.Verification;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Diva.Agents.Tests;

/// <summary>
/// Unit tests for tool calling optimisations:
///   Gap 3 — TruncateResult helper
///   Gap 4 — LLM retry with exponential back-off
/// </summary>
public class ToolOptimizationTests : IAsyncDisposable
{
    // ── Gap 3 — TruncateResult ────────────────────────────────────────────────

    [Fact]
    public void TruncateResult_OutputUnderLimit_ReturnsUnchanged()
    {
        var result = ReActToolHelper.TruncateResult("hello", 8000);
        Assert.Equal("hello", result);
    }

    [Fact]
    public void TruncateResult_OutputAtExactLimit_ReturnsUnchanged()
    {
        var input  = new string('x', 8000);
        var result = ReActToolHelper.TruncateResult(input, 8000);
        Assert.Equal(input, result);
    }

    [Fact]
    public void TruncateResult_OutputOverLimit_TruncatesWithMarker()
    {
        var input  = new string('x', 9000);
        var result = ReActToolHelper.TruncateResult(input, 8000);

        Assert.Equal(8000, result.IndexOf("\n[truncated"));
        Assert.StartsWith(new string('x', 8000), result);
        Assert.Contains("9000 chars total", result);
    }

    [Fact]
    public void TruncateResult_TruncatedOutputContainsRerequeryHint()
    {
        var input  = new string('x', 9000);
        var result = ReActToolHelper.TruncateResult(input, 8000);
        Assert.Contains("Re-query with narrower parameters", result);
    }

    // ── Gap 4 — Retry ─────────────────────────────────────────────────────────

    // Shared SQLite connection + runner factory for retry tests
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<DivaDbContext> _dbOptions;

    public ToolOptimizationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _dbOptions = new DbContextOptionsBuilder<DivaDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var db = new DivaDbContext(_dbOptions);
        db.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    private AnthropicAgentRunner BuildRunner(
        IAnthropicProvider anthropic,
        AgentOptions? agentOpts = null)
        => BuildRunnerWith(anthropic, Substitute.For<IAnthropicProvider>(),
               new VerificationOptions { Mode = "Off" }, agentOpts);

    private AnthropicAgentRunner BuildRunnerWith(
        IAnthropicProvider runnerAnthropic,
        IAnthropicProvider verifierAnthropic,
        VerificationOptions verificationOpts,
        AgentOptions? agentOpts = null)
    {
        var factory  = new RetryTestDbFactory(_dbOptions);
        var sessions = new AgentSessionService(factory, NullLogger<AgentSessionService>.Instance);
        var verifier = new ResponseVerifier(
            AgentTestFixtures.Opts(verificationOpts),
            AgentTestFixtures.AnthropicLlm(),
            verifierAnthropic,
            Substitute.For<IOpenAiProvider>(),
            NullLogger<ResponseVerifier>.Instance);
        var ruleLearner = Substitute.For<IRuleLearningService>();
        ruleLearner.ExtractRulesFromConversationAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([]);
        var cache = new McpClientCache();

        return new AnthropicAgentRunner(
            AgentTestFixtures.AnthropicLlm(),
            AgentTestFixtures.Opts(agentOpts ?? new AgentOptions { EnableResponseStreaming = false, Retry = new LlmRetryOptions { MaxRetries = 3, BaseDelayMs = 1 } }),
            AgentTestFixtures.Opts(verificationOpts),
            sessions,
            verifier,
            ruleLearner,
            cache,
            runnerAnthropic,
            Substitute.For<IOpenAiProvider>(),
            ContextWindowTestHelpers.NoOpCtx(),
            Substitute.For<IHttpContextAccessor>(),
            new ToolExecutor(NullLogger<ToolExecutor>.Instance, Options.Create(new AgentOptions())),
            NullLogger<AnthropicAgentRunner>.Instance);
    }

    [Fact]
    public async Task RunAsync_TransientError_RetriesAndSucceeds()
    {
        var anthropic = Substitute.For<IAnthropicProvider>();
        anthropic.GetClaudeMessageAsync(Arg.Any<MessageParameters>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromException<MessageResponse>(new Exception("503 Service Unavailable")),
                _ => Task.FromException<MessageResponse>(new Exception("503 Service Unavailable")),
                _ => Task.FromResult(MakeTextResponse("Success after retries")));

        var runner = BuildRunner(anthropic);

        var result = await runner.RunAsync(
            AgentTestFixtures.BasicAgent(),
            AgentTestFixtures.BasicRequest("hi"),
            AgentTestFixtures.BasicTenant(),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("Success after retries", result.Content);
        await anthropic.Received(3).GetClaudeMessageAsync(Arg.Any<MessageParameters>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_NonTransientError_DoesNotRetry()
    {
        var anthropic = Substitute.For<IAnthropicProvider>();
        anthropic.GetClaudeMessageAsync(Arg.Any<MessageParameters>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("400 Bad Request — invalid model"));

        var runner = BuildRunner(anthropic);

        var result = await runner.RunAsync(
            AgentTestFixtures.BasicAgent(),
            AgentTestFixtures.BasicRequest("hi"),
            AgentTestFixtures.BasicTenant(),
            CancellationToken.None);

        // Non-transient error should not be retried — returns failure
        Assert.False(result.Success);
        await anthropic.Received(1).GetClaudeMessageAsync(Arg.Any<MessageParameters>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ExceedsMaxRetries_Throws()
    {
        var anthropic = Substitute.For<IAnthropicProvider>();
        anthropic.GetClaudeMessageAsync(Arg.Any<MessageParameters>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("429 Too Many Requests"));

        var runner = BuildRunner(anthropic,
            new AgentOptions { EnableResponseStreaming = false, Retry = new LlmRetryOptions { MaxRetries = 2, BaseDelayMs = 1 } });

        var result = await runner.RunAsync(
            AgentTestFixtures.BasicAgent(),
            AgentTestFixtures.BasicRequest("hi"),
            AgentTestFixtures.BasicTenant(),
            CancellationToken.None);

        // After 1 initial + 2 retries = 3 total calls, returns failure
        Assert.False(result.Success);
        await anthropic.Received(3).GetClaudeMessageAsync(Arg.Any<MessageParameters>(), Arg.Any<CancellationToken>());
    }

    // ── Correction loop scope ─────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_LlmVerifierFalsePositive_DoesNotTriggerCorrectionIteration()
    {
        // Verifier LLM returns is_verified: false (false positive)
        var verifierAnthropic = Substitute.For<IAnthropicProvider>();
        verifierAnthropic.GetClaudeMessageAsync(Arg.Any<MessageParameters>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(MakeTextResponse(
                """{"confidence": 0.3, "is_verified": false, "ungrounded_claims": ["unverified claim"], "reasoning": "no evidence"}""")));

        // Runner LLM returns a response long enough to trigger LlmVerifier (>80 chars)
        var runnerAnthropic = Substitute.For<IAnthropicProvider>();
        runnerAnthropic.GetClaudeMessageAsync(Arg.Any<MessageParameters>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(MakeTextResponse(
                "The revenue for Q1 was $1,234,567 and total transactions reached 42,000 this quarter.")));

        var runner = BuildRunnerWith(
            runnerAnthropic,
            verifierAnthropic,
            new VerificationOptions { Mode = "LlmVerifier", MaxVerificationRetries = 1 });

        var result = await runner.RunAsync(
            AgentTestFixtures.BasicAgent(),
            AgentTestFixtures.BasicRequest("what is the revenue?"),
            AgentTestFixtures.BasicTenant(),
            CancellationToken.None);

        Assert.True(result.Success);
        // LlmVerifier is informational — false-positive must NOT trigger a correction iteration
        await runnerAnthropic.Received(1).GetClaudeMessageAsync(
            Arg.Any<MessageParameters>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ToolGroundedNoTools_TriggersOneCorrection()
    {
        // Runner LLM: first call returns factual claim (triggers ToolGrounded failure),
        // second call (correction iteration) returns a safe response
        var runnerAnthropic = Substitute.For<IAnthropicProvider>();
        runnerAnthropic.GetClaudeMessageAsync(Arg.Any<MessageParameters>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromResult(MakeTextResponse(
                    "Today's temperature is 25 degrees and there are 7 sunny hours forecast.")),
                _ => Task.FromResult(MakeTextResponse(
                    "I cannot provide that information without access to a weather tool.")));

        var runner = BuildRunnerWith(
            runnerAnthropic,
            Substitute.For<IAnthropicProvider>(),
            new VerificationOptions { Mode = "ToolGrounded", MaxVerificationRetries = 1 });

        var result = await runner.RunAsync(
            AgentTestFixtures.BasicAgent(),
            AgentTestFixtures.BasicRequest("what is the weather?"),
            AgentTestFixtures.BasicTenant(),
            CancellationToken.None);

        // ToolGrounded detects ungrounded factual claims → correction triggers once → 2 LLM calls
        await runnerAnthropic.Received(2).GetClaudeMessageAsync(
            Arg.Any<MessageParameters>(), Arg.Any<CancellationToken>());
    }

    // ── DeduplicateCalls ──────────────────────────────────────────────────────

    private record Call(string Name, string InputJson);

    [Fact]
    public void DeduplicateCalls_DuplicatePair_GroupedIntoOne()
    {
        var calls = new List<Call>
        {
            new("get_weather", "{\"city\":\"London\"}"),
            new("get_weather", "{\"city\":\"London\"}"),
            new("get_flights", "{\"from\":\"LHR\"}"),
        };

        var groups = ReActToolHelper.DeduplicateCalls(calls, c => (c.Name, c.InputJson));

        Assert.Equal(2, groups.Count);
        Assert.Equal(2, groups[0].Originals.Count);
        Assert.Single(groups[1].Originals);
        Assert.Equal("get_weather", groups[0].Name);
        Assert.Equal("get_flights", groups[1].Name);
    }

    [Fact]
    public void DeduplicateCalls_AllUnique_NoGroupsMerged()
    {
        var calls = new List<Call>
        {
            new("tool_a", "{}"),
            new("tool_b", "{}"),
        };

        var groups = ReActToolHelper.DeduplicateCalls(calls, c => (c.Name, c.InputJson));

        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.Single(g.Originals));
    }

    [Fact]
    public void DeduplicateCalls_PreservesOrderOfFirstOccurrence()
    {
        var calls = new List<Call>
        {
            new("z_tool", "{}"),
            new("a_tool", "{}"),
            new("z_tool", "{}"),
        };

        var groups = ReActToolHelper.DeduplicateCalls(calls, c => (c.Name, c.InputJson));

        Assert.Equal(2, groups.Count);
        Assert.Equal("z_tool", groups[0].Name);
        Assert.Equal("a_tool", groups[1].Name);
    }

    // ── IsToolOutputError ─────────────────────────────────────────────────────

    [Fact]
    public void IsToolOutputError_ExceptionText_ReturnsTrue()
        => Assert.True(ReActToolHelper.IsToolOutputError("Error: connection refused"));

    [Fact]
    public void IsToolOutputError_JsonStatusError_ReturnsTrue()
        => Assert.True(ReActToolHelper.IsToolOutputError("{\"status\":\"error\",\"error\":\"Incorrect syntax near AND.\"}"));

    [Fact]
    public void IsToolOutputError_JsonStatusErrorWithSpace_ReturnsTrue()
        => Assert.True(ReActToolHelper.IsToolOutputError("{\"status\": \"error\",\"error\":\"something\"}"));

    [Fact]
    public void IsToolOutputError_SuccessJson_ReturnsFalse()
        => Assert.False(ReActToolHelper.IsToolOutputError("{\"status\":\"success\",\"data\":[]}"));

    [Fact]
    public void IsToolOutputError_NormalText_ReturnsFalse()
        => Assert.False(ReActToolHelper.IsToolOutputError("Revenue: $42,000"));

    // ── BuildContinuationContext ──────────────────────────────────────────────

    [Fact]
    public void BuildContinuationContext_NoEvidence_ContainsWindowAndIterationCount()
    {
        var result = ReActToolHelper.BuildContinuationContext(1, 10, []);
        Assert.Contains("window 2", result);
        Assert.Contains("10 iterations", result);
    }

    [Fact]
    public void BuildContinuationContext_WithEvidence_ContainsEvidenceText()
    {
        var evidence = new List<string> { "[Tool: get_data]\nrevenue: $42,000" };
        var result = ReActToolHelper.BuildContinuationContext(1, 10, evidence);
        Assert.Contains("revenue: $42,000", result);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static MessageResponse MakeTextResponse(string text) => new()
    {
        Content    = [new Anthropic.SDK.Messaging.TextContent { Text = text }],
        StopReason = "end_turn",
        Model      = "claude-sonnet-4-20250514"
    };
}

internal sealed class RetryTestDbFactory : IDatabaseProviderFactory
{
    private readonly DbContextOptions<DivaDbContext> _options;
    public RetryTestDbFactory(DbContextOptions<DivaDbContext> options) => _options = options;
    public DivaDbContext CreateDbContext(Diva.Core.Models.TenantContext? tenant = null)
        => new(_options, tenant?.TenantId ?? 0);
    public Task ApplyMigrationsAsync() => Task.CompletedTask;
}
