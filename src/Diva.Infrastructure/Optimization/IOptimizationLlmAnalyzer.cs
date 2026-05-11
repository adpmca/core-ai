using Diva.Core.Models;
using Diva.Infrastructure.Data.Entities;

namespace Diva.Infrastructure.Optimization;

public interface IOptimizationLlmAnalyzer
{
    Task<List<OptimizationSuggestionDto>> AnalyzeAsync(
        SessionAnalysisReport report,
        AgentDefinitionEntity agentDefinition,
        string? userContext,
        CancellationToken ct);

    Task<string> MergePromptAsync(
        string currentPrompt,
        IReadOnlyList<string> suggestedChanges,
        AgentDefinitionEntity agentDef,
        CancellationToken ct);

    /// <summary>
    /// Applies a free-form user instruction to the current system prompt and returns
    /// the improved version. Unlike MergePromptAsync (which integrates LLM-generated
    /// optimization suggestions), this is driven entirely by the admin's own words.
    /// </summary>
    Task<string> QuickImprovePromptAsync(
        string currentPrompt,
        string instruction,
        AgentDefinitionEntity agentDef,
        CancellationToken ct);
}
