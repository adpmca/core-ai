namespace Diva.Infrastructure.Data.Entities;

public sealed class AgentOptimizationRunEntity : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string AgentId { get; set; } = "";
    public string? SessionId { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string Status { get; set; } = "running";
    public string TriggerSource { get; set; } = "manual";
    public int SessionsAnalyzed { get; set; }
    public int TurnsAnalyzed { get; set; }
    public string? ReportJson { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public List<AgentOptimizationSuggestionEntity> Suggestions { get; set; } = [];
}

public sealed class AgentOptimizationSuggestionEntity : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int RunId { get; set; }
    public string AgentId { get; set; } = "";
    public string Type { get; set; } = "";
    public string FieldName { get; set; } = "";
    public string? CurrentValue { get; set; }
    public string SuggestedValue { get; set; } = "";
    public float Confidence { get; set; }
    public string Reasoning { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public string? ReviewedBy { get; set; }
    public string? ReviewNotes { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public AgentOptimizationRunEntity? Run { get; set; }
}

public sealed class AgentOptimizationConfigEntity : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string AgentId { get; set; } = "";
    public string ScheduleType { get; set; } = "manual";
    public string? RunAtTime { get; set; }
    public int? RunOnDayOfWeek { get; set; }
    public string Timezone { get; set; } = "UTC";
    public bool IsEnabled { get; set; } = true;
    public DateTime? NextRunAt { get; set; }
    public DateTime? LastScheduledRunAt { get; set; }
}
