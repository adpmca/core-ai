namespace Diva.Infrastructure.Data.Entities;

public class AgentSessionEntity : ITenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public int TenantId { get; set; }
    public int SiteId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string? CurrentAgentType { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(24);
    public string Status { get; set; } = "active";        // "active" | "expired" | "closed"

    public List<AgentSessionMessageEntity> Messages { get; set; } = [];
}

public class AgentSessionMessageEntity
{
    public int Id { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;       // "user" | "assistant" | "system"
    public string Content { get; set; } = string.Empty;
    public string? MetadataJson { get; set; }              // JSON: agent name, tools used, etc.
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public AgentSessionEntity Session { get; set; } = null!;
}
