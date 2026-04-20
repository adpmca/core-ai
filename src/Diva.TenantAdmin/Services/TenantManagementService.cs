using Diva.Infrastructure.Data;
using Diva.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Diva.TenantAdmin.Services;

/// <summary>
/// Platform-level tenant CRUD. TenantEntity has no EF query filter (it is the root entity),
/// so no tenant scoping is needed — all queries see all tenants.
/// Singleton-safe: uses IDatabaseProviderFactory per call.
/// </summary>
public sealed class TenantManagementService : ITenantManagementService
{
    private readonly IDatabaseProviderFactory _db;

    public TenantManagementService(IDatabaseProviderFactory db) => _db = db;

    public async Task<List<TenantEntity>> GetAllAsync(CancellationToken ct = default)
    {
        using var db = _db.CreateDbContext();
        return await db.Tenants
            .AsNoTracking()
            .Include(t => t.Sites)
            .OrderBy(t => t.Name)
            .ToListAsync(ct);
    }

    public async Task<TenantEntity?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        using var db = _db.CreateDbContext();
        return await db.Tenants
            .AsNoTracking()
            .Include(t => t.Sites)
            .FirstOrDefaultAsync(t => t.Id == id, ct);
    }

    public async Task<TenantEntity> CreateAsync(CreateTenantDto dto, CancellationToken ct = default)
    {
        using var db = _db.CreateDbContext();
        var entity = new TenantEntity
        {
            Name           = dto.Name,
            LiteLLMTeamId  = dto.LiteLLMTeamId,
            LiteLLMTeamKey = dto.LiteLLMTeamKey,
            IsActive       = true,
            CreatedAt      = DateTime.UtcNow,
        };
        db.Tenants.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<TenantEntity> UpdateAsync(int id, UpdateTenantDto dto, CancellationToken ct = default)
    {
        using var db = _db.CreateDbContext();
        var entity = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new KeyNotFoundException($"Tenant {id} not found");

        entity.Name           = dto.Name;
        entity.LiteLLMTeamId  = dto.LiteLLMTeamId;
        entity.LiteLLMTeamKey = dto.LiteLLMTeamKey;
        entity.IsActive       = dto.IsActive;

        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        using var db = _db.CreateDbContext();
        var entity = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (entity is null) return;
        db.Tenants.Remove(entity);
        await db.SaveChangesAsync(ct);
    }
}
