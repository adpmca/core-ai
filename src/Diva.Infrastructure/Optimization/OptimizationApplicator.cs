using Diva.Core.Models;
using Diva.Core.Optimization;
using Diva.Infrastructure.Data;
using Diva.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Diva.Infrastructure.Optimization;

/// <summary>
/// Applies approved optimization suggestions to AgentDefinitionEntity.
/// Calls IAgentSetupAssistant.SavePromptVersionAsync for prompt changes.
/// </summary>
public sealed class OptimizationApplicator
{
    private readonly Diva.Core.Models.IAgentSetupAssistant _setupAssistant;
    private readonly IOptimizationRulePackAccessor _rulePackAccessor;
    private readonly IOptimizationLlmAnalyzer _llmAnalyzer;
    private readonly ILogger<OptimizationApplicator> _logger;

    private static readonly HashSet<string> ValidVerificationModes =
        new(StringComparer.Ordinal) { "Off", "ToolGrounded", "LlmVerifier", "Strict", "Auto" };

    public OptimizationApplicator(
        IAgentSetupAssistant setupAssistant,
        IOptimizationRulePackAccessor rulePackAccessor,
        IOptimizationLlmAnalyzer llmAnalyzer,
        ILogger<OptimizationApplicator> logger)
    {
        _setupAssistant   = setupAssistant;
        _rulePackAccessor = rulePackAccessor;
        _llmAnalyzer      = llmAnalyzer;
        _logger           = logger;
    }

    public async Task ApplyAsync(
        AgentOptimizationSuggestionEntity suggestion,
        AgentDefinitionEntity agentDef,
        DivaDbContext db,
        string applyMode,
        CancellationToken ct)
    {
        if (suggestion.Status != "Approved")
            throw new InvalidOperationException("Only Approved suggestions can be applied.");

        switch (suggestion.Type)
        {
            case nameof(SuggestionType.SystemPromptImprovement):
            case nameof(SuggestionType.ToolStrategyHint):
                await ApplyPromptAsync(suggestion, agentDef, db, ct);
                break;

            case nameof(SuggestionType.TemperatureAdjustment):
                if (double.TryParse(suggestion.SuggestedValue, out var temp))
                    agentDef.Temperature = Math.Clamp(temp, 0.0, 2.0);
                break;

            case nameof(SuggestionType.VerificationModeUpgrade):
                if (!ValidVerificationModes.Contains(suggestion.SuggestedValue))
                {
                    _logger.LogWarning(
                        "Skipping invalid VerificationMode suggestion '{Mode}' for agent {AgentId}",
                        suggestion.SuggestedValue, agentDef.Id);
                    break;
                }
                agentDef.VerificationMode = suggestion.SuggestedValue;
                break;

            case nameof(SuggestionType.MaxIterationsAdjustment):
                if (int.TryParse(suggestion.SuggestedValue, out var maxIter))
                    agentDef.MaxIterations = Math.Clamp(maxIter, 1, 50);
                break;

            case nameof(SuggestionType.MaxContinuationsAdjustment):
                if (int.TryParse(suggestion.SuggestedValue, out var maxCont))
                    agentDef.MaxContinuations = Math.Clamp(maxCont, 0, 10);
                break;

            case nameof(SuggestionType.ModelSwitch):
                agentDef.ModelId = suggestion.SuggestedValue;
                break;

            case nameof(SuggestionType.ContextWindowAdjustment):
                agentDef.ContextWindowJson = suggestion.SuggestedValue;
                break;

            case nameof(SuggestionType.RulePackSuggestion):
                if (int.TryParse(suggestion.SuggestedValue, out var packId))
                {
                    try { await _rulePackAccessor.EnablePackAsync(packId, agentDef.TenantId, ct); }
                    catch (KeyNotFoundException ex)
                    {
                        _logger.LogWarning(ex, "Rule pack {PackId} not found — skipping", packId);
                    }
                }
                else
                {
                    _logger.LogWarning("RulePackSuggestion has non-integer packId '{V}'", suggestion.SuggestedValue);
                }
                break;

            default:
                _logger.LogWarning("Unknown suggestion type {Type} — skipping apply", suggestion.Type);
                return;
        }

        suggestion.Status = "Applied";
        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Applied suggestion {Id} type={Type} to agent {AgentId}",
            suggestion.Id, suggestion.Type, agentDef.Id);
    }

    private async Task ApplyPromptAsync(
        AgentOptimizationSuggestionEntity suggestion,
        AgentDefinitionEntity agentDef,
        DivaDbContext db,
        CancellationToken ct)
    {
        var existing = agentDef.SystemPrompt ?? "";
        string newPrompt;

        try
        {
            newPrompt = await _llmAnalyzer.MergePromptAsync(
                existing, [suggestion.SuggestedValue], agentDef, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM prompt merge failed — falling back to append");
            newPrompt = string.IsNullOrWhiteSpace(existing)
                ? suggestion.SuggestedValue
                : existing + "\n\n" + suggestion.SuggestedValue;
        }

        agentDef.SystemPrompt = newPrompt;
        await db.SaveChangesAsync(ct);

        await _setupAssistant.SavePromptVersionAsync(
            agentDef.Id,
            agentDef.TenantId,
            newPrompt,
            source: "optimization",
            reason: suggestion.Reasoning,
            createdBy: "optimization",
            ct);
    }
}
