using Diva.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Diva.Infrastructure.Data;

/// <summary>
/// Dedicated EF context for the session trace database (sessions-trace.db).
/// Completely independent of DivaDbContext — no tenant query filters, separate connection string.
/// Enables Claude Code to query sessions-trace.db directly for investigation given a session ID.
/// </summary>
public class SessionTraceDbContext : DbContext
{
    public SessionTraceDbContext(DbContextOptions<SessionTraceDbContext> options)
        : base(options)
    {
    }

    public DbSet<TraceSessionEntity> TraceSessions => Set<TraceSessionEntity>();
    public DbSet<TraceSessionTurnEntity> TraceSessionTurns => Set<TraceSessionTurnEntity>();
    public DbSet<TraceIterationEntity> TraceIterations => Set<TraceIterationEntity>();
    public DbSet<TraceToolCallEntity> TraceToolCalls => Set<TraceToolCallEntity>();
    public DbSet<TraceDelegationChainEntity> TraceDelegationChain => Set<TraceDelegationChainEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── TraceSessions ──────────────────────────────────────────────────────
        modelBuilder.Entity<TraceSessionEntity>(e =>
        {
            e.HasKey(x => x.SessionId);
            e.HasIndex(x => new { x.TenantId, x.CreatedAt });
            e.HasIndex(x => x.ParentSessionId);
        });

        // ── TraceSessionTurns ─────────────────────────────────────────────────
        modelBuilder.Entity<TraceSessionTurnEntity>(e =>
        {
            e.HasIndex(x => new { x.SessionId, x.TurnNumber });
            e.HasOne(x => x.Session)
             .WithMany(s => s.Turns)
             .HasForeignKey(x => x.SessionId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── TraceIterations ───────────────────────────────────────────────────
        modelBuilder.Entity<TraceIterationEntity>(e =>
        {
            e.HasIndex(x => x.SessionId);
            e.HasIndex(x => new { x.TurnId, x.IterationNumber });
            e.HasOne(x => x.Turn)
             .WithMany(t => t.Iterations)
             .HasForeignKey(x => x.TurnId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── TraceToolCalls ────────────────────────────────────────────────────
        modelBuilder.Entity<TraceToolCallEntity>(e =>
        {
            e.HasIndex(x => x.SessionId);
            e.HasIndex(x => x.IterationId);
            e.HasIndex(x => x.LinkedA2ATaskId);
            e.HasOne(x => x.Iteration)
             .WithMany(i => i.ToolCalls)
             .HasForeignKey(x => x.IterationId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── TraceDelegationChain ──────────────────────────────────────────────
        modelBuilder.Entity<TraceDelegationChainEntity>(e =>
        {
            e.HasIndex(x => x.CallerSessionId);
            e.HasIndex(x => x.ChildA2ATaskId);
            e.HasIndex(x => x.ChildSessionId);
        });
    }
}
