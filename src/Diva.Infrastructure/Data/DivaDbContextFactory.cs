using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Diva.Infrastructure.Data;

/// <summary>
/// Design-time factory used by dotnet-ef migrations.
/// Uses SQLite with a local diva.db file.
/// </summary>
public sealed class DivaDbContextFactory : IDesignTimeDbContextFactory<DivaDbContext>
{
    public DivaDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<DivaDbContext>()
            .UseSqlite("Data Source=diva.db")
            .Options;
        return new DivaDbContext(options, currentTenantId: 0);
    }
}
