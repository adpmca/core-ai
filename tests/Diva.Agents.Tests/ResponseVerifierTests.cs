using Anthropic.SDK.Messaging;
using Diva.Agents.Tests.Helpers;
using Diva.Core.Configuration;
using Diva.Infrastructure.LiteLLM;
using Diva.Infrastructure.Verification;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Diva.Agents.Tests;

public class ResponseVerifierTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static ResponseVerifier BuildVerifier(
        string mode = "Off",
        IAnthropicProvider? anthropic = null,
        IOpenAiProvider? openAi = null)
    {
        return new ResponseVerifier(
            AgentTestFixtures.Opts(new VerificationOptions { Mode = mode }),
            AgentTestFixtures.AnthropicLlm(),
            anthropic ?? Substitute.For<IAnthropicProvider>(),
            openAi    ?? Substitute.For<IOpenAiProvider>(),
            NullLogger<ResponseVerifier>.Instance);
    }

    private static ResponseVerifier BuildVerifierWithOverride(
        string globalMode,
        IAnthropicProvider? anthropic = null)
    {
        return new ResponseVerifier(
            AgentTestFixtures.Opts(new VerificationOptions { Mode = globalMode }),
            AgentTestFixtures.AnthropicLlm(),
            anthropic ?? Substitute.For<IAnthropicProvider>(),
            Substitute.For<IOpenAiProvider>(),
            NullLogger<ResponseVerifier>.Instance);
    }

    // ── Off mode ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Off_AlwaysSkips_ReturnsVerifiedTrue()
    {
        var verifier = BuildVerifier("Off");

        var result = await verifier.VerifyAsync(
            "The Sensex closed at 62,150 today.",
            ["GetMarketData"],
            "evidence here",
            CancellationToken.None);

        Assert.True(result.IsVerified);
        Assert.Equal("Off", result.Mode);
        Assert.Equal(1f, result.Confidence);
    }

    // ── ToolGrounded mode ────────────────────────────────────────────────────

    [Fact]
    public async Task ToolGrounded_NoToolsCalled_WithFactualNumbers_ReturnsFlagged()
    {
        var verifier = BuildVerifier("ToolGrounded");

        var result = await verifier.VerifyAsync(
            "Revenue was $24,500 last month with 1,250 transactions.",
            toolsUsed: [],
            toolEvidence: "",
            CancellationToken.None);

        Assert.False(result.IsVerified);
        Assert.Equal("ToolGrounded", result.Mode);
        Assert.True(result.Confidence < 0.5f);
        Assert.NotEmpty(result.UngroundedClaims);
    }

    [Fact]
    public async Task ToolGrounded_NoToolsCalled_PurelyConversational_Passes()
    {
        var verifier = BuildVerifier("ToolGrounded");

        var result = await verifier.VerifyAsync(
            "Sure, I can help you with that! Let me know what you need.",
            toolsUsed: [],
            toolEvidence: "",
            CancellationToken.None);

        Assert.True(result.IsVerified);
        Assert.Equal("ToolGrounded", result.Mode);
    }

    [Fact]
    public async Task ToolGrounded_ToolsWereCalled_AlwaysPasses()
    {
        var verifier = BuildVerifier("ToolGrounded");

        var result = await verifier.VerifyAsync(
            "Revenue was $24,500 last month.",
            toolsUsed: ["GetMetrics"],
            toolEvidence: "[Tool: GetMetrics]\n{\"total\": 24500}",
            CancellationToken.None);

        Assert.True(result.IsVerified);
        Assert.Equal("ToolGrounded", result.Mode);
        Assert.True(result.Confidence >= 0.8f);
    }

    // ── Auto mode ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Auto_ShortResponse_Skips()
    {
        var verifier = BuildVerifier("Auto");

        var result = await verifier.VerifyAsync(
            "Got it!",   // < 80 chars
            toolsUsed: [],
            toolEvidence: "",
            CancellationToken.None);

        Assert.True(result.IsVerified);
        Assert.Equal("Off", result.Mode);
    }

    [Fact]
    public async Task Auto_NoToolsNoFacts_Skips()
    {
        var verifier = BuildVerifier("Auto");

        var result = await verifier.VerifyAsync(
            "Sure, I can help you with that! Please describe what you'd like to do and I'll guide you through it step by step.",
            toolsUsed: [],
            toolEvidence: "",
            CancellationToken.None);

        Assert.True(result.IsVerified);
        Assert.Equal("Off", result.Mode);
    }

    [Fact]
    public async Task Auto_NoToolsWithFacts_RunsToolGrounded()
    {
        var verifier = BuildVerifier("Auto");

        var result = await verifier.VerifyAsync(
            "The stock is currently priced at $142.75, up 3.2% from yesterday's close of $138.30.",
            toolsUsed: [],
            toolEvidence: "",
            CancellationToken.None);

        Assert.Equal("ToolGrounded", result.Mode);
        Assert.False(result.IsVerified);
    }

    [Fact]
    public async Task Auto_ToolsCalledWithEvidence_RunsToolGrounded()
    {
        // Auto mode now uses ToolGrounded heuristic (zero extra LLM cost) when tools+evidence present.
        // Use LlmVerifier or Strict explicitly for cross-checking claims vs evidence.
        var anthropic = Substitute.For<IAnthropicProvider>();

        var verifier = BuildVerifier("Auto", anthropic: anthropic);

        var result = await verifier.VerifyAsync(
            "Total revenue was $24,500 last month, with 1,250 transactions recorded and a strong 7.9% growth rate.",
            toolsUsed: ["GetMetrics"],
            toolEvidence: "[Tool: GetMetrics]\n{\"revenue\": 24500, \"transactions\": 1250, \"growth\": 0.079}",
            CancellationToken.None);

        Assert.Equal("ToolGrounded", result.Mode);
        Assert.True(result.IsVerified);
        // No extra LLM call — heuristic is zero-cost
        await anthropic.DidNotReceive().GetClaudeMessageAsync(Arg.Any<MessageParameters>(), Arg.Any<CancellationToken>());
    }

    // ── modeOverride ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ModeOverride_PerAgentOverridesGlobal_Off_SkipsEvenForFactualContent()
    {
        // Global = LlmVerifier, per-agent override = Off
        var anthropic = Substitute.For<IAnthropicProvider>();
        var verifier  = BuildVerifierWithOverride("LlmVerifier", anthropic);

        var result = await verifier.VerifyAsync(
            "Revenue was $24,500 last month with 1,250 transactions.",
            toolsUsed: ["GetMetrics"],
            toolEvidence: "[Tool: GetMetrics]\n{\"total\": 24500}",
            CancellationToken.None,
            modelId: null,
            modeOverride: "Off");   // per-agent override

        Assert.True(result.IsVerified);
        Assert.Equal("Off", result.Mode);
        // LLM should never be called
        await anthropic.DidNotReceive().GetClaudeMessageAsync(Arg.Any<MessageParameters>(), Arg.Any<CancellationToken>());
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static MessageResponse MakeAnthropicResponse(string text)
    {
        return new MessageResponse
        {
            Content    = [new Anthropic.SDK.Messaging.TextContent { Text = text }],
            StopReason = "end_turn",
            Model      = "claude-sonnet-4-20250514"
        };
    }
}
