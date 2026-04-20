namespace Diva.Infrastructure.Data.Entities;

public class TenantEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? LiteLLMTeamId { get; set; }
    public string? LiteLLMTeamKey { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<SiteEntity> Sites { get; set; } = [];
}

public class SiteEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? TimeZone { get; set; }
    public bool IsActive { get; set; } = true;

    public TenantEntity Tenant { get; set; } = null!;
}
