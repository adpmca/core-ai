using Diva.Infrastructure.Data.Entities;

namespace Diva.TenantAdmin.Services;

public interface ITenantManagementService
{
    Task<List<TenantEntity>> GetAllAsync(CancellationToken ct = default);
    Task<TenantEntity?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<TenantEntity> CreateAsync(CreateTenantDto dto, CancellationToken ct = default);
    Task<TenantEntity> UpdateAsync(int id, UpdateTenantDto dto, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}

public record CreateTenantDto(
    string Name,
    string? LiteLLMTeamId,
    string? LiteLLMTeamKey);

public record UpdateTenantDto(
    string Name,
    string? LiteLLMTeamId,
    string? LiteLLMTeamKey,
    bool IsActive);
