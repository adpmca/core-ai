using Diva.Core.Models;
using Diva.Infrastructure.Verification;
using Microsoft.Extensions.Logging;

namespace Diva.Agents.Supervisor.Stages;

/// <summary>
/// Verifies the integrated response against tool evidence.
///
/// Multi-agent path (G3 fix): consolidates pre-computed AgentResponse.Verification entries
/// (set per-agent by the agent runner) rather than re-running ToolGrounded on the combined
/// evidence pool, which would give false grounding confidence across agents.
///
/// Single-agent path: unchanged — calls ResponseVerifier exactly as before.
/// In Strict mode, replaces low-confidence responses with a refusal message.
/// </summary>
public sealed class VerifyStage : ISupervisorPipelineStage
{
    private readonly ResponseVerifier _verifier;
    private readonly ILogger<VerifyStage> _logger;

    public VerifyStage(ResponseVerifier verifier, ILogger<VerifyStage> logger)
    {
        _verifier = verifier;
        _logger   = logger;
    }

    public async Task<SupervisorState> ExecuteAsync(SupervisorState state, CancellationToken ct)
    {
        var successful = state.WorkerResults.Where(r => r.Success).ToList();

        if (successful.Count > 1)
        {
            var perAgent = successful
                .Select(r => r.Verification)
                .Where(v => v is not null)
                .Cast<VerificationResult>()
                .ToList();

            if (perAgent.Count > 0)
            {
                state.Verification = new VerificationResult
                {
                    IsVerified       = perAgent.All(v => v.IsVerified),
                    Confidence       = perAgent.Min(v => v.Confidence),
                    Mode             = "MultiAgent",
                    UngroundedClaims = [.. perAgent.SelectMany(v => v.UngroundedClaims ?? [])],
                    WasBlocked       = false  // blocking handled per-agent inside agent runners
                };

                _logger.LogInformation(
                    "Multi-agent verification: confidence={Conf:F2} verified={Ok}",
                    state.Verification.Confidence, state.Verification.IsVerified);

                return state;
            }
        }

        // Single-agent or no per-agent verifications available — existing path unchanged
        var allTools     = state.WorkerResults.SelectMany(r => r.ToolsUsed).ToList();
        var verification = await _verifier.VerifyAsync(
            state.IntegratedResult, allTools, state.ToolEvidence, ct);

        _logger.LogInformation(
            "Verification: mode={Mode} confidence={Conf:F2} verified={Ok} blocked={Blocked}",
            verification.Mode, verification.Confidence, verification.IsVerified, verification.WasBlocked);

        if (verification.WasBlocked)
        {
            state.IntegratedResult =
                "I was unable to verify the accuracy of this response with sufficient confidence. " +
                "Please refine your question or consult the source data directly.";
        }

        state.Verification = verification;
        return state;
    }
}
