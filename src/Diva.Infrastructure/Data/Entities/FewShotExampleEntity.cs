namespace Diva.Infrastructure.Data.Entities;

public sealed class FewShotExampleEntity : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string AgentId { get; set; } = "";
    public string? SourceSessionId { get; set; }
    public int? SourceTurnNumber { get; set; }
    public string UserMessage { get; set; } = "";
    public string AssistantMessage { get; set; } = "";
    public string? Description { get; set; }
    public int SortOrder { get; set; } = 0;
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
}
