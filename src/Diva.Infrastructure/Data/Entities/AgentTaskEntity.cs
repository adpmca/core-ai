namespace Diva.Infrastructure.Data.Entities;

public sealed class AgentTaskEntity : ITenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public int TenantId { get; set; }
    public string AgentId { get; set; } = "";

    /// <summary>pending | working | completed | failed | canceled</summary>
    public string Status { get; set; } = "pending";

    public string? InputJson { get; set; }
    public string? OutputText { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? SessionId { get; set; }
}
