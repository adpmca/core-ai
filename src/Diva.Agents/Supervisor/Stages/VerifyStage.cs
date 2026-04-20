using Diva.Infrastructure.Verification;
using Microsoft.Extensions.Logging;

namespace Diva.Agents.Supervisor.Stages;

/// <summary>
/// Verifies the integrated response against the tool evidence accumulated by DispatchStage.
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
        var allTools = state.WorkerResults.SelectMany(r => r.ToolsUsed).ToList();

        var verification = await _verifier.VerifyAsync(
            state.IntegratedResult,
            allTools,
            state.ToolEvidence,
            ct);

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
