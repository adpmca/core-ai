using Diva.Core.Configuration;
using Diva.Core.Models;
using Diva.Infrastructure.Data;
using Diva.Infrastructure.Data.Entities;
using Diva.Infrastructure.Optimization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Diva.Agents.Tests.Optimization;

/// <summary>
/// Tests for AgentOptimizationService.ComputeNextRunAt (static) and
/// OptimizationSchedulerHostedService schedule-polling behavior.
/// </summary>
public class OptimizationSchedulerTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<DivaDbContext> _opts;
    private readonly ServiceProvider _provider;

    public OptimizationSchedulerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _opts = new DbContextOptionsBuilder<DivaDbContext>()
            .UseSqlite(_connection)
            .Options;
        using (var db = new DivaDbContext(_opts))
            db.Database.EnsureCreated();

        var services = new ServiceCollection();
        services.AddDbContext<DivaDbContext>(o => o.UseSqlite(_connection));
        _provider = services.BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // ── ComputeNextRunAt (pure unit tests) ────────────────────────────────────

    [Fact]
    public void ComputeNextRunAt_ManualSchedule_ReturnsNull()
    {
        var config = new OptimizationScheduleConfig { ScheduleType = "manual", IsEnabled = true };
        var result = AgentOptimizationService.ComputeNextRunAt(config);
        Assert.Null(result);
    }

    [Fact]
    public void ComputeNextRunAt_Disabled_ReturnsNull()
    {
        var config = new OptimizationScheduleConfig
            { ScheduleType = "daily", RunAtTime = "09:00", IsEnabled = false };
        var result = AgentOptimizationService.ComputeNextRunAt(config);
        Assert.Null(result);
    }

    [Fact]
    public void ComputeNextRunAt_DailyWithRunAtTime_ReturnsNextOccurrence()
    {
        var config = new OptimizationScheduleConfig
        {
            ScheduleType = "daily",
            RunAtTime    = "23:59",
            Timezone     = "UTC",
            IsEnabled    = true
        };
        var result = AgentOptimizationService.ComputeNextRunAt(config);
        Assert.NotNull(result);
        Assert.True(result!.Value > DateTime.UtcNow);
    }

    [Fact]
    public void ComputeNextRunAt_WeeklyWithDayOfWeek_ReturnsFutureDateOnTargetDay()
    {
        var config = new OptimizationScheduleConfig
        {
            ScheduleType   = "weekly",
            RunAtTime      = "09:00",
            RunOnDayOfWeek = (int)DayOfWeek.Friday,
            Timezone       = "UTC",
            IsEnabled      = true
        };
        var result = AgentOptimizationService.ComputeNextRunAt(config);
        Assert.NotNull(result);
        Assert.Equal(DayOfWeek.Friday, result!.Value.DayOfWeek);
        Assert.True(result.Value > DateTime.UtcNow);
    }

    [Fact]
    public void ComputeNextRunAt_MissingRunAtTime_ReturnsNull()
    {
        var config = new OptimizationScheduleConfig
            { ScheduleType = "daily", RunAtTime = null, IsEnabled = true };
        var result = AgentOptimizationService.ComputeNextRunAt(config);
        Assert.Null(result);
    }

    // ── Scheduler poll behavior ───────────────────────────────────────────────

    private OptimizationSchedulerHostedService BuildScheduler(IAgentOptimizationService optService)
        => new(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            optService,
            Options.Create(new AgentOptions
            {
                Optimization = new OptimizationOptions { SchedulerPollIntervalSeconds = 300 }
            }),
            NullLogger<OptimizationSchedulerHostedService>.Instance);

    [Fact]
    public async Task Scheduler_DueConfig_TriggersStartRunAsync()
    {
        await using (var db = new DivaDbContext(_opts))
        {
            db.OptimizationConfigs.Add(new AgentOptimizationConfigEntity
            {
                TenantId     = 1,
                AgentId      = "sched-agent",
                ScheduleType = "daily",
                RunAtTime    = "09:00",
                Timezone     = "UTC",
                IsEnabled    = true,
                NextRunAt    = DateTime.UtcNow.AddHours(-1)
            });
            await db.SaveChangesAsync();
        }

        var optService = Substitute.For<IAgentOptimizationService>();
        optService.StartRunAsync(
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TriggerOptimizationRequest>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(42);

        var scheduler = BuildScheduler(optService);
        await scheduler.PollSchedulesAsync(CancellationToken.None);

        await optService.Received().StartRunAsync(
            "sched-agent", 1, Arg.Any<TriggerOptimizationRequest>(), "scheduled", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Scheduler_DisabledConfig_DoesNotTrigger()
    {
        await using (var db = new DivaDbContext(_opts))
        {
            db.OptimizationConfigs.Add(new AgentOptimizationConfigEntity
            {
                TenantId     = 5,
                AgentId      = "disabled-agent",
                ScheduleType = "daily",
                RunAtTime    = "09:00",
                Timezone     = "UTC",
                IsEnabled    = false,
                NextRunAt    = DateTime.UtcNow.AddHours(-1)
            });
            await db.SaveChangesAsync();
        }

        var optService = Substitute.For<IAgentOptimizationService>();
        var scheduler  = BuildScheduler(optService);
        await scheduler.PollSchedulesAsync(CancellationToken.None);

        await optService.DidNotReceive().StartRunAsync(
            "disabled-agent", Arg.Any<int>(), Arg.Any<TriggerOptimizationRequest>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Scheduler_AlreadyRunningAgent_LogsAndDoesNotUpdateNextRunAt()
    {
        await using (var db = new DivaDbContext(_opts))
        {
            db.OptimizationConfigs.Add(new AgentOptimizationConfigEntity
            {
                TenantId     = 1,
                AgentId      = "busy-agent",
                ScheduleType = "daily",
                RunAtTime    = "09:00",
                Timezone     = "UTC",
                IsEnabled    = true,
                NextRunAt    = DateTime.UtcNow.AddHours(-1)
            });
            await db.SaveChangesAsync();
        }

        var optService = Substitute.For<IAgentOptimizationService>();
        optService.StartRunAsync(
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TriggerOptimizationRequest>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("An optimization run is already in progress for this agent."));

        var scheduler = BuildScheduler(optService);
        await scheduler.PollSchedulesAsync(CancellationToken.None);

        await using var verify = new DivaDbContext(_opts);
        var config = await verify.OptimizationConfigs
            .FirstAsync(c => c.AgentId == "busy-agent");

        Assert.Null(config.LastScheduledRunAt);
    }
}
