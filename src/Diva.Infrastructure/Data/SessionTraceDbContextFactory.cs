using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Diva.Infrastructure.Data;

/// <summary>
/// Design-time factory for SessionTraceDbContext.
/// Required by "dotnet ef migrations add" when using a separate context in the same assembly.
/// Usage:
///   dotnet ef migrations add InitialTraceSchema \
///     --project src/Diva.Infrastructure \
///     --startup-project src/Diva.Host \
///     --context SessionTraceDbContext \
///     -- --provider SQLite
/// </summary>
public sealed class SessionTraceDbContextFactory : IDesignTimeDbContextFactory<SessionTraceDbContext>
{
    public SessionTraceDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SessionTraceDbContext>();
        optionsBuilder.UseSqlite("Data Source=sessions-trace.db");
        return new SessionTraceDbContext(optionsBuilder.Options);
    }
}
