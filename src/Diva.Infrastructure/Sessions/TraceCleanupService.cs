using Diva.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Diva.Infrastructure.Sessions;

/// <summary>
/// Background service that periodically deletes trace sessions (and all child rows via cascade)
/// older than the configured <c>SessionTrace:RetentionDays</c>.
/// </summary>
public sealed class TraceCleanupService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<TraceCleanupService> _logger;
    private readonly TraceCleanupOptions _opts;

    public TraceCleanupService(
        IServiceProvider sp,
        ILogger<TraceCleanupService> logger,
        TraceCleanupOptions opts)
    {
        _sp = sp;
        _logger = logger;
        _opts = opts;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Yield(); // let host finish startup

        if (_opts.RetentionDays <= 0)
        {
            _logger.LogInformation("Session trace cleanup disabled (RetentionDays={Days})", _opts.RetentionDays);
            return;
        }

        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromHours(_opts.CleanupIntervalHours), ct); }
            catch (OperationCanceledException) { break; }

            try { await CleanupAsync(ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Session trace cleanup failed");
            }
        }
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-_opts.RetentionDays);

        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SessionTraceDbContext>();

        // Cascade deletes: TraceSessions → Turns, Iterations, ToolCalls, DelegationChain
        var deleted = await db.TraceSessions
            .Where(s => s.LastActivityAt < cutoff)
            .ExecuteDeleteAsync(ct);

        if (deleted > 0)
            _logger.LogInformation(
                "Session trace cleanup: deleted {Count} sessions older than {Cutoff:u}",
                deleted, cutoff);
    }
}

/// <summary>Options bound from <c>SessionTrace</c> config section.</summary>
public sealed class TraceCleanupOptions
{
    public int RetentionDays { get; set; } = 30;
    public int CleanupIntervalHours { get; set; } = 24;
}
