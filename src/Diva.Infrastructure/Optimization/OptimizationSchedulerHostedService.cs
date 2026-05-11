using Diva.Core.Configuration;
using Diva.Core.Models;
using Diva.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Diva.Infrastructure.Optimization;

public sealed class OptimizationSchedulerHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAgentOptimizationService _optimizationService;
    private readonly AgentOptions _opts;
    private readonly ILogger<OptimizationSchedulerHostedService> _logger;

    public OptimizationSchedulerHostedService(
        IServiceScopeFactory scopeFactory,
        IAgentOptimizationService optimizationService,
        IOptions<AgentOptions> opts,
        ILogger<OptimizationSchedulerHostedService> logger)
    {
        _scopeFactory        = scopeFactory;
        _optimizationService = optimizationService;
        _opts                = opts.Value;
        _logger              = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Optimization scheduler started (poll interval: {Interval}s)",
            _opts.Optimization.SchedulerPollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollSchedulesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Optimization scheduler poll failed");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(_opts.Optimization.SchedulerPollIntervalSeconds),
                stoppingToken);
        }
    }

    internal async Task PollSchedulesAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        // Use TenantId=0 to read all tenants' configs (bypasses tenant filter)
        var db = scope.ServiceProvider.GetRequiredService<DivaDbContext>();

        var now = DateTime.UtcNow;
        var dueConfigs = await db.OptimizationConfigs
            .Where(c => c.IsEnabled
                     && c.ScheduleType != "manual"
                     && c.NextRunAt != null
                     && c.NextRunAt <= now)
            .ToListAsync(ct);

        foreach (var config in dueConfigs)
        {
            try
            {
                var request = new TriggerOptimizationRequest
                {
                    From = now.AddDays(-30),
                    To   = now
                };

                await _optimizationService.StartRunAsync(
                    config.AgentId, config.TenantId, request, "scheduled", ct);

                config.LastScheduledRunAt = now;
                config.NextRunAt = AgentOptimizationService.ComputeNextRunAt(new OptimizationScheduleConfig
                {
                    ScheduleType   = config.ScheduleType,
                    RunAtTime      = config.RunAtTime,
                    RunOnDayOfWeek = config.RunOnDayOfWeek,
                    Timezone       = config.Timezone,
                    IsEnabled      = config.IsEnabled
                });

                await db.SaveChangesAsync(ct);
                _logger.LogInformation(
                    "Scheduled optimization run started for agent {AgentId} tenant {TenantId}; next at {Next}",
                    config.AgentId, config.TenantId, config.NextRunAt);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("already in progress"))
            {
                _logger.LogInformation(
                    "Skipping scheduled run for agent {AgentId} — already in progress", config.AgentId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Scheduled optimization failed for agent {AgentId}", config.AgentId);
            }
        }
    }
}
