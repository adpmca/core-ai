using Diva.Core.Configuration;
using Diva.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Diva.Infrastructure.A2A;

/// <summary>
/// Background service that periodically deletes completed/failed/canceled A2A tasks
/// older than <see cref="A2AOptions.TaskRetentionDays"/>.
/// </summary>
public sealed class AgentTaskCleanupService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private static readonly string[] TerminalStatuses = ["completed", "failed", "canceled"];

    private readonly IServiceProvider _sp;
    private readonly ILogger<AgentTaskCleanupService> _logger;
    private readonly A2AOptions _opts;

    public AgentTaskCleanupService(
        IServiceProvider sp,
        ILogger<AgentTaskCleanupService> logger,
        IOptions<A2AOptions> opts)
    {
        _sp = sp;
        _logger = logger;
        _opts = opts.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Yield immediately so StartAsync returns and the host can finish startup.
        await Task.Yield();

        if (!_opts.Enabled || _opts.TaskRetentionDays <= 0)
        {
            _logger.LogInformation("A2A task cleanup disabled (Enabled={Enabled}, RetentionDays={Days})",
                _opts.Enabled, _opts.TaskRetentionDays);
            return;
        }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await CleanupAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "A2A task cleanup failed");
            }
        }
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-_opts.TaskRetentionDays);

        using var scope = _sp.CreateScope();
        // Use TenantId=0 to bypass query filters (system context)
        var factory = scope.ServiceProvider.GetRequiredService<IDatabaseProviderFactory>();
        using var db = factory.CreateDbContext(null);

        var deleted = await db.AgentTasks
            .Where(t => TerminalStatuses.Contains(t.Status) && t.CreatedAt < cutoff)
            .ExecuteDeleteAsync(ct);

        if (deleted > 0)
            _logger.LogInformation("A2A task cleanup: deleted {Count} expired tasks (cutoff={Cutoff:u})",
                deleted, cutoff);
    }
}
