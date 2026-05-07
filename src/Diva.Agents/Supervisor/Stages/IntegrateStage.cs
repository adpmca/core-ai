using Diva.Infrastructure.Synthesis;
using Microsoft.Extensions.Logging;

namespace Diva.Agents.Supervisor.Stages;

/// <summary>
/// Combines worker results into a single integrated response.
/// Single agent: passes result through directly (no LLM call).
/// Multiple agents: synthesizes via IResponseSynthesizer using the coordinator's
/// provider+model from state.LlmOverride, falling back to platform defaults.
/// Partial failures are surfaced in the result rather than silently dropped (G6 fix).
/// </summary>
public sealed class IntegrateStage : ISupervisorPipelineStage
{
    private readonly IResponseSynthesizer _synthesizer;
    private readonly ILogger<IntegrateStage> _logger;

    public IntegrateStage(IResponseSynthesizer synthesizer, ILogger<IntegrateStage> logger)
    {
        _synthesizer = synthesizer;
        _logger      = logger;
    }

    public async Task<SupervisorState> ExecuteAsync(SupervisorState state, CancellationToken ct)
    {
        var successful = state.WorkerResults.Where(r => r.Success).ToList();
        var failed     = state.WorkerResults.Where(r => !r.Success).ToList();

        state.IntegratedResult = successful.Count switch
        {
            0 => state.ErrorMessage ?? "No results.",
            _ => await _synthesizer.SynthesizeAsync(
                    state.Request.Query, successful, state.LlmOverride, ct)
        };

        // Surface partial failures so the user knows part of their request was not completed
        if (failed.Count > 0 && successful.Count > 0)
        {
            var names = string.Join(", ", failed.Select(r => r.AgentName ?? "unknown"));
            state.IntegratedResult +=
                $"\n\n> **Note:** {failed.Count} sub-task(s) could not be completed ({names}).";
        }

        _logger.LogDebug("Integrated {Success} succeeded + {Failed} failed into {Length} chars",
            successful.Count, failed.Count, state.IntegratedResult.Length);

        return state;
    }
}
