namespace Diva.Infrastructure.Data.Entities;

public class LearnedRuleEntity : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string? AgentType { get; set; }
    public string? RuleCategory { get; set; }
    public string? RuleKey { get; set; }
    public string? PromptInjection { get; set; }
    public double Confidence { get; set; }
    public string Status { get; set; } = "pending";        // "pending" | "approved" | "rejected"
    public string? SourceSessionId { get; set; }
    public string? ReviewedBy { get; set; }
    public string? ReviewNotes { get; set; }
    public DateTime LearnedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
}
