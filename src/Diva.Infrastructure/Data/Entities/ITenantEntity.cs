namespace Diva.Infrastructure.Data.Entities;

/// <summary>
/// All DB entities must implement this. Drives EF query filters for tenant isolation.
/// </summary>
public interface ITenantEntity
{
    int TenantId { get; set; }
}
