using Diva.Core.Models;

namespace Diva.Infrastructure.Synthesis;

/// <summary>
/// Synthesizes multiple agent results into a single coherent response.
/// When count == 1, returns the single result directly (no LLM call).
/// Uses state.LlmOverride provider+model when set; falls back to global defaults.
/// </summary>
public interface IResponseSynthesizer
{
    Task<string> SynthesizeAsync(
        string originalQuery,
        IReadOnlyList<AgentResponse> results,
        SupervisorLlmOverride? llmOverride,
        CancellationToken ct);
}
