using System.Text.RegularExpressions;

namespace Diva.Infrastructure.LiteLLM;

/// <summary>
/// Pure static helpers for detecting and parsing ReAct plans from LLM text output.
/// </summary>
internal static class ReActPlanParser
{
    private static readonly Regex PlanStepPattern =
        new(@"(?m)^\d+\.\s+.+", RegexOptions.Compiled);

    /// <summary>
    /// Extracts numbered plan steps (e.g. "1. Do X") from raw LLM text.
    /// Returns an empty array when no steps are found.
    /// </summary>
    internal static string[] ParsePlanSteps(string text) =>
        PlanStepPattern.Matches(text)
                       .Select(m => m.Value.Trim())
                       .ToArray();

    /// <summary>
    /// Returns true when the text qualifies as a plan emission:
    /// first iteration, plan not yet emitted, and at least 2 numbered steps.
    /// </summary>
    internal static bool IsPlanEmission(string text, bool isFirstIteration, bool planAlreadyEmitted) =>
        isFirstIteration && !planAlreadyEmitted && ParsePlanSteps(text).Length >= 2;
}
