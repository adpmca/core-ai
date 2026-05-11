namespace Diva.Core.Models;

public enum SuggestionType
{
    SystemPromptImprovement,
    TemperatureAdjustment,
    VerificationModeUpgrade,
    MaxIterationsAdjustment,
    MaxContinuationsAdjustment,
    ToolStrategyHint,
    ModelSwitch,
    ContextWindowAdjustment,
    FewShotExampleAdded,
    RulePackSuggestion   // SuggestedValue = packId (int as string); FieldName = pack name
}

public enum SuggestionStatus { Pending, Approved, Rejected, Applied }

public sealed record TurnDimensionScores
{
    public float Faithfulness { get; init; }
    public float Completeness { get; init; }
    public float ToolEfficiency { get; init; }
    public float Coherence { get; init; }
}

public sealed record SessionAnalysisReport
{
    public string AgentId { get; init; } = "";
    public string? SessionId { get; init; }
    public int TotalSessions { get; init; }
    public int TotalTurns { get; init; }
    public int ScoredTurns { get; init; }
    public double? AvgFaithfulness { get; init; }
    public double? AvgCompleteness { get; init; }
    public double? AvgToolEfficiency { get; init; }
    public double? AvgCoherence { get; init; }
    public double VerificationFailureRate { get; init; }
    public double CorrectionRetryRate { get; init; }
    public double MaxIterationsHitRate { get; init; }
    public double ToolErrorRate { get; init; }
    public double AverageIterationsPerTurn { get; init; }
    public double AverageInputTokensPerTurn { get; init; }
    public List<string> FrequentToolErrors { get; init; } = [];
    public List<string> SampleTurnContent { get; init; } = [];
}

public sealed record OptimizationSuggestionDto
{
    public int Id { get; init; }
    public int RunId { get; init; }
    public string AgentId { get; init; } = "";
    public string Type { get; init; } = "";
    public string FieldName { get; init; } = "";
    public string? CurrentValue { get; init; }
    public string SuggestedValue { get; init; } = "";
    public float Confidence { get; init; }
    public string Reasoning { get; init; } = "";
    public string Status { get; init; } = "Pending";
    public string? ReviewedBy { get; init; }
    public string? ReviewNotes { get; init; }
    public DateTime? ReviewedAt { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record OptimizationRunSummary
{
    public int Id { get; init; }
    public string AgentId { get; init; } = "";
    public string? SessionId { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string Status { get; init; } = "";
    public string TriggerSource { get; init; } = "";
    public int SessionsAnalyzed { get; init; }
    public int TurnsAnalyzed { get; init; }
    public int SuggestionCount { get; init; }
}

public sealed record OptimizationRunDetail : OptimizationRunSummary
{
    public SessionAnalysisReport? Report { get; init; }
    public List<OptimizationSuggestionDto> Suggestions { get; init; } = [];
    public string? ErrorMessage { get; init; }
}

public sealed record TriggerOptimizationRequest
{
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
    public string? SessionId { get; init; }
    public string? UserContext { get; init; }
}

public sealed record MergePromptRequest
{
    public int[] SuggestionIds { get; init; } = [];
}

public sealed record ApplyMergedRequest
{
    public string MergedPrompt { get; init; } = "";
    public int[] SuggestionIds { get; init; } = [];
}

public sealed record ReviewSuggestionRequest
{
    public string? Notes { get; init; }
}

public sealed record ApplySuggestionRequest
{
    /// <summary>"append" | "prepend" | "replace" — only relevant for SystemPromptImprovement</summary>
    public string ApplyMode { get; init; } = "append";
}

public sealed record OptimizationScheduleConfig
{
    public string ScheduleType { get; init; } = "manual";
    public string? RunAtTime { get; init; }
    public int? RunOnDayOfWeek { get; init; }
    public string Timezone { get; init; } = "UTC";
    public bool IsEnabled { get; init; } = true;
    public DateTime? NextRunAt { get; init; }
    public DateTime? LastScheduledRunAt { get; init; }
}

public sealed record FewShotExampleDto
{
    public int Id { get; init; }
    public string AgentId { get; init; } = "";
    public string? SourceSessionId { get; init; }
    public int? SourceTurnNumber { get; init; }
    public string UserMessage { get; init; } = "";
    public string AssistantMessage { get; init; } = "";
    public string? Description { get; init; }
    public int SortOrder { get; init; }
    public bool IsEnabled { get; init; } = true;
    public DateTime CreatedAt { get; init; }
    public string? CreatedBy { get; init; }
}

public sealed record ReorderFewShotExamplesRequest
{
    public int[] OrderedIds { get; init; } = [];
}

public sealed record MarkTurnAsExampleRequest
{
    public string? Description { get; init; }
}
