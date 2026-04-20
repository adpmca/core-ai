namespace Diva.Infrastructure.Data.Entities;

/// <summary>
/// Append-only history of an agent's system prompt.
/// Each change (manual edit, assistant suggestion, restore) appends a new row with an incremented Version.
/// Restoring a version creates a new row with Source="restore" — prior history is never deleted.
/// </summary>
public class AgentPromptHistoryEntity : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string AgentId { get; set; } = string.Empty;
    public int Version { get; set; }
    public string SystemPrompt { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = "system";
    /// <summary>"manual" | "assistant_create" | "assistant_refine" | "restore"</summary>
    public string Source { get; set; } = "manual";
    public string? Reason { get; set; }
}

/// <summary>
/// Append-only history of a rule pack's rules stored as a JSON snapshot.
/// Each change appends a new row with an incremented Version.
/// </summary>
public class RulePackHistoryEntity : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int PackId { get; set; }
    public int Version { get; set; }
    public string RulesJson { get; set; } = "[]";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = "system";
    /// <summary>"manual" | "assistant_create" | "assistant_refine" | "restore"</summary>
    public string Source { get; set; } = "manual";
    public string? Reason { get; set; }
}
