namespace Diva.Core.Configuration;

public sealed class VerificationOptions
{
    public const string SectionName = "Verification";

    /// <summary>Off | ToolGrounded | LlmVerifier | Strict | Auto</summary>
    public string Mode { get; init; } = "ToolGrounded";

    /// <summary>Confidence below this value triggers a block in Strict mode.</summary>
    public float ConfidenceThreshold { get; init; } = 0.5f;

    /// <summary>
    /// Auto mode only. When the cheap tool-grounded heuristic returns confidence below this value
    /// AND tool evidence is available, Auto escalates to a (non-blocking) LLM cross-check instead of
    /// accepting the heuristic verdict. Keeps the common case zero-cost while catching low-confidence
    /// responses (e.g. action/delivery claims tools can't directly prove). Set to 0 to disable escalation.
    /// </summary>
    public float AutoEscalateThreshold { get; init; } = 0.7f;

    /// <summary>Include the verifier's reasoning text in the API response.</summary>
    public bool IncludeReasoningInResponse { get; init; } = false;

    /// <summary>Model to use for LLM verification. Falls back to the request's effective model if null.</summary>
    public string? VerifierModel { get; init; }

    /// <summary>How many times to retry with a correction prompt when ungrounded claims are found. 0 = no retry.</summary>
    public int MaxVerificationRetries { get; init; } = 1;

    /// <summary>Max output tokens for the LLM verifier call.</summary>
    public int VerifierMaxTokens { get; init; } = 1024;

    /// <summary>
    /// Max characters of tool evidence sent into the LLM verifier prompt. Deep tool runs can
    /// accumulate tens of thousands of tokens of evidence; capping it bounds verifier input cost
    /// without materially hurting grounding accuracy. 0 disables the cap.
    /// </summary>
    public int MaxVerifierEvidenceChars { get; init; } = 4000;
}
