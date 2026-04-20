namespace Diva.Core.Models;

/// <summary>
/// The outcome of running ResponseVerifier against an agent response.
/// Attached to every AgentResponse when Verification.Mode != "Off".
/// </summary>
public sealed class VerificationResult
{
    /// <summary>True if all factual claims are grounded in tool evidence.</summary>
    public bool IsVerified { get; init; }

    /// <summary>0.0 = high hallucination risk, 1.0 = fully verified.</summary>
    public float Confidence { get; init; }

    /// <summary>Verification mode that produced this result: Off | ToolGrounded | LlmVerifier | Strict</summary>
    public string Mode { get; init; } = "Off";

    /// <summary>Specific claims identified as ungrounded (LlmVerifier/Strict mode only).</summary>
    public List<string> UngroundedClaims { get; init; } = [];

    /// <summary>True if Strict mode replaced the response with a refusal due to low confidence.</summary>
    public bool WasBlocked { get; init; }

    /// <summary>One-sentence explanation from the LLM verifier (LlmVerifier/Strict mode only).</summary>
    public string? Reasoning { get; init; }
}
