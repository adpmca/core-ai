namespace Diva.Core.Configuration;

public sealed class VerificationOptions
{
    public const string SectionName = "Verification";

    /// <summary>Off | ToolGrounded | LlmVerifier | Strict | Auto</summary>
    public string Mode { get; init; } = "ToolGrounded";

    /// <summary>Confidence below this value triggers a block in Strict mode.</summary>
    public float ConfidenceThreshold { get; init; } = 0.5f;

    /// <summary>Include the verifier's reasoning text in the API response.</summary>
    public bool IncludeReasoningInResponse { get; init; } = false;

    /// <summary>Model to use for LLM verification. Falls back to the request's effective model if null.</summary>
    public string? VerifierModel { get; init; }

    /// <summary>How many times to retry with a correction prompt when ungrounded claims are found. 0 = no retry.</summary>
    public int MaxVerificationRetries { get; init; } = 1;

    /// <summary>Max output tokens for the LLM verifier call.</summary>
    public int VerifierMaxTokens { get; init; } = 1024;
}
