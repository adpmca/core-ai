using System.Collections.Concurrent;
using Diva.Core.Configuration;
using Diva.Core.Models;
using Diva.Core.Prompts;
using Diva.Infrastructure.Data;
using Diva.Infrastructure.Data.Entities;
using Diva.Infrastructure.LiteLLM;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Diva.Infrastructure.Scheduler;

/// <summary>
/// Background service that polls for due scheduled tasks and dispatches them through
/// the existing <see cref="AnthropicAgentRunner"/> execution path.
///
/// Queue-on-overlap: when a task fires while its previous run is still active, a "pending" DB
/// record is created. On completion of any run, the next pending run for that task is activated
/// and dispatched in the same poll call.
/// </summary>
public sealed class SchedulerHostedService : BackgroundService
{
    private readonly IScheduledTaskService _service;
    private readonly IDatabaseProviderFactory _db;
    private readonly AnthropicAgentRunner _runner;
    private readonly IOptions<TaskSchedulerOptions> _opts;
    private readonly ILogger<SchedulerHostedService> _logger;

    // taskId → runId: which scheduled tasks are currently executing
    private readonly ConcurrentDictionary<string, string> _runningTasks = new();

    // Concurrency gate
    private SemaphoreSlim _semaphore = null!;

    public SchedulerHostedService(
        IScheduledTaskService service,
        IDatabaseProviderFactory db,
        AnthropicAgentRunner runner,
        IOptions<TaskSchedulerOptions> opts,
        ILogger<SchedulerHostedService> logger)
    {
        _service = service;
        _db      = db;
        _runner  = runner;
        _opts    = opts;
        _logger  = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Yield immediately so StartAsync returns and the host can finish startup.
        await Task.Yield();

        var options = _opts.Value;
        if (!options.IsEnabled)
        {
            _logger.LogInformation("Task Scheduler is disabled via configuration.");
            return;
        }

        _semaphore = new SemaphoreSlim(options.MaxConcurrentRuns, options.MaxConcurrentRuns);

        _logger.LogInformation(
            "Task Scheduler started. PollInterval={Sec}s MaxConcurrent={Max} StuckRunTimeout={Timeout}min.",
            options.PollIntervalSeconds, options.MaxConcurrentRuns, options.StuckRunTimeoutMinutes);

        // Startup recovery: any run still "running" in the DB belongs to the previous process.
        // Mark them all failed now so their pending runs can proceed.
        Exception? startupRecoveryEx = null;
        int recoveredCount = 0;
        try { recoveredCount = await _service.RecoverStuckRunsAsync(DateTime.UtcNow, stoppingToken); }
        catch (Exception ex) { startupRecoveryEx = ex; }

        if (startupRecoveryEx is not null)
            _logger.LogError(startupRecoveryEx, "Startup stuck-run recovery failed.");
        else if (recoveredCount > 0)
            _logger.LogWarning("Startup recovery: marked {Count} stuck run(s) as failed.", recoveredCount);

        var pollInterval = TimeSpan.FromSeconds(options.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            Exception? pollError = null;
            try
            {
                await PollAndDispatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                pollError = ex;
            }

            if (pollError is not null)
                _logger.LogError(pollError, "Scheduler poll cycle failed.");

            await Task.Delay(pollInterval, stoppingToken)
                      .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        _logger.LogInformation("Task Scheduler stopping — waiting for {Count} active run(s).",
            _runningTasks.Count);

        // Give in-flight runs a grace period then stop
        while (_runningTasks.Count > 0)
            await Task.Delay(500, CancellationToken.None);

        _logger.LogInformation("Task Scheduler stopped.");
    }

    private async Task PollAndDispatchAsync(CancellationToken ct)
    {
        var now     = DateTime.UtcNow;
        var timeout = _opts.Value.StuckRunTimeoutMinutes;

        // Per-poll timeout recovery: catch runs that are hung in-process (not a restart).
        if (timeout > 0)
        {
            var cutoff = now.AddMinutes(-timeout);
            Exception? ex = null;
            int recovered = 0;
            try { recovered = await _service.RecoverStuckRunsAsync(cutoff, ct); }
            catch (Exception e) { ex = e; }

            if (ex is not null)
                _logger.LogError(ex, "Per-poll stuck-run recovery failed.");
            else if (recovered > 0)
                _logger.LogWarning("Timeout recovery: marked {Count} run(s) stuck for >{Timeout}min as failed.", recovered, timeout);
        }

        var dueTasks      = await _service.GetDueTasksAsync(now, ct);
        var dueGroupTasks = await _service.GetDueGroupTasksAsync(now, ct);

        if (dueTasks.Count > 0 || dueGroupTasks.Count > 0)
            _logger.LogDebug("Scheduler poll: {Count} tenant task(s), {GroupCount} group task(s).",
                dueTasks.Count, dueGroupTasks.Count);

        foreach (var scheduledTask in dueTasks)
            await TryDispatchDueTaskAsync(scheduledTask, now, ct);

        foreach (var groupTask in dueGroupTasks)
            await TryDispatchGroupTaskAsync(groupTask, now, ct);

        // Dispatch any pending runs created by TriggerNow that have no active running run
        await DispatchOrphanedPendingRunsAsync(ct);
    }

    private async Task DispatchOrphanedPendingRunsAsync(CancellationToken ct)
    {
        var orphaned = await _service.GetTasksWithOrphanedPendingRunsAsync(ct);
        foreach (var (task, _) in orphaned)
        {
            if (_runningTasks.ContainsKey(task.Id)) continue;

            ScheduledTaskRunEntity? activated = null;
            Exception? ex = null;
            try { activated = await _service.ActivateOldestPendingRunAsync(task.Id, ct); }
            catch (Exception e) { ex = e; }
            if (ex is not null) { _logger.LogError(ex, "ActivateOldestPendingRunAsync failed for orphaned run of task '{Id}'.", task.Id); continue; }
            if (activated is null) continue;

            _logger.LogInformation("Dispatching manually triggered run '{RunId}' for task '{TaskName}'.",
                activated.Id, task.Name);
            DispatchRun(task, activated, ct);
        }
    }

    private async Task TryDispatchDueTaskAsync(
        ScheduledTaskEntity scheduledTask, DateTime now, CancellationToken ct)
    {
        // BeginRunAsync creates the run record (status = running | pending | skipped)
        // and advances NextRunUtc so the task won't re-appear next poll.
        ScheduledTaskRunEntity? run = null;
        Exception? ex = null;
        try { run = await _service.BeginRunAsync(scheduledTask.Id, now, ct); }
        catch (Exception e) { ex = e; }
        if (ex is not null)
        {
            _logger.LogError(ex, "BeginRunAsync failed for task '{Id}'.", scheduledTask.Id);
            return;
        }

        if (run is null) return;

        if (run!.Status == "skipped")
        {
            _logger.LogWarning("Task '{Id}' run skipped — queue is full.", scheduledTask.Id);
            return;
        }

        if (run.Status == "pending")
        {
            _logger.LogDebug("Task '{Id}' run '{RunId}' queued (another run is active).",
                scheduledTask.Id, run.Id);
            return;
        }

        // Status == "running" → dispatch
        DispatchRun(scheduledTask, run, ct);
    }

    /// <summary>Fire-and-forget dispatch — tracked via _runningTasks.</summary>
    private void DispatchRun(
        ScheduledTaskEntity scheduledTask, ScheduledTaskRunEntity run, CancellationToken ct)
    {
        if (!_semaphore.Wait(0))
        {
            _logger.LogDebug("Rate limit reached — deferred run '{RunId}'.", run.Id);
            return;
        }

        _runningTasks[scheduledTask.Id] = run.Id;

        _ = Task.Run(async () =>
        {
            try
            {
                await ExecuteRunAsync(scheduledTask, run, ct);
            }
            finally
            {
                _runningTasks.TryRemove(scheduledTask.Id, out _);
                _semaphore.Release();

                // Promote and dispatch the oldest queued pending run (if any)
                await DispatchNextQueuedRunAsync(scheduledTask, ct);
            }
        }, CancellationToken.None);
    }

    private async Task DispatchNextQueuedRunAsync(ScheduledTaskEntity scheduledTask, CancellationToken ct)
    {
        // If the task is already running again (e.g., new due fire arrived simultaneously), skip
        if (_runningTasks.ContainsKey(scheduledTask.Id)) return;

        ScheduledTaskRunEntity? pending = null;
        Exception? ex = null;
        try { pending = await _service.ActivateOldestPendingRunAsync(scheduledTask.Id, ct); }
        catch (Exception e) { ex = e; }

        if (ex is not null) { _logger.LogError(ex, "ActivateOldestPendingRunAsync failed."); return; }
        if (pending is null) return;

        _logger.LogInformation("Dispatching queued run '{RunId}' for task '{TaskName}'.",
            pending.Id, scheduledTask.Name);
        DispatchRun(scheduledTask, pending, ct);
    }

    private async Task ExecuteRunAsync(
        ScheduledTaskEntity scheduledTask, ScheduledTaskRunEntity run, CancellationToken appCt)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation(
            "Executing scheduled run '{RunId}' — task '{TaskName}' (tenant {TenantId}).",
            run.Id, scheduledTask.Name, scheduledTask.TenantId);

        string? result  = null;
        string? error   = null;
        string? session = null;
        bool    success = false;

        try
        {
            using var db = _db.CreateDbContext();
            var agent = await db.AgentDefinitions.FindAsync([scheduledTask.AgentId], appCt);

            if (agent is null || !agent.IsEnabled)
            {
                error = agent is null
                    ? $"Agent '{scheduledTask.AgentId}' not found."
                    : "Agent is disabled.";
                _logger.LogWarning("Run '{RunId}': {Error}", run.Id, error);
                return;
            }

            var prompt  = BuildPrompt(scheduledTask, run.Id);
            var tenant  = TenantContext.System(scheduledTask.TenantId);
            var request = new AgentRequest
            {
                Query       = prompt,
                TriggerType = "scheduled",
                Metadata    = new Dictionary<string, object?>
                {
                    ["schedule_id"]       = scheduledTask.Id,
                    ["schedule_name"]     = scheduledTask.Name,
                    ["run_id"]            = run.Id,
                    ["scheduled_for_utc"] = run.ScheduledForUtc.ToString("O")
                }
            };

            var response = await _runner.RunAsync(agent, request, tenant, appCt);
            session = response.SessionId;
            result  = response.Content;
            success = true;

            _logger.LogInformation(
                "Run '{RunId}' completed in {Ms}ms.", run.Id, sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (appCt.IsCancellationRequested)
        {
            error = "Cancelled due to application shutdown.";
            _logger.LogWarning("Run '{RunId}' cancelled.", run.Id);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            _logger.LogError(ex, "Run '{RunId}' failed.", run.Id);
        }
        finally
        {
            Exception? ex = null;
            try
            {
                await _service.CompleteRunAsync(
                    run.Id, success, result, error, session,
                    sw.ElapsedMilliseconds, CancellationToken.None);
            }
            catch (Exception e) { ex = e; }

            if (ex is not null)
                _logger.LogError(ex, "CompleteRunAsync failed for run '{RunId}'.", run.Id);
        }
    }

    // ── Group task dispatch ───────────────────────────────────────────────────

    private async Task TryDispatchGroupTaskAsync(
        GroupScheduledTaskEntity groupTask, DateTime now, CancellationToken ct)
    {
        var members = groupTask.Group?.Members ?? [];

        foreach (var member in members)
        {
            var raceKey = $"{groupTask.Id}_{member.TenantId}";
            if (_runningTasks.ContainsKey(raceKey)) continue;

            AgentDefinitionEntity? agent = null;
            Exception? agentEx = null;
            try { agent = await _service.GetFirstEnabledAgentByTypeAsync(member.TenantId, groupTask.AgentType, ct); }
            catch (Exception e) { agentEx = e; }

            if (agentEx is not null)
            {
                _logger.LogError(agentEx, "GetFirstEnabledAgentByTypeAsync failed for group task '{Id}'.", groupTask.Id);
                continue;
            }

            if (agent is null)
            {
                _logger.LogDebug("No enabled '{AgentType}' agent for tenant {TenantId} — skipping group task '{Id}'.",
                    groupTask.AgentType, member.TenantId, groupTask.Id);
                continue;
            }

            GroupScheduledTaskRunEntity? run = null;
            Exception? runEx = null;
            try { run = await _service.BeginGroupRunAsync(groupTask.Id, member.TenantId, groupTask.GroupId, now, ct); }
            catch (Exception e) { runEx = e; }

            if (runEx is not null)
            {
                _logger.LogError(runEx, "BeginGroupRunAsync failed for group task '{Id}' tenant {TenantId}.",
                    groupTask.Id, member.TenantId);
                continue;
            }

            DispatchGroupRun(groupTask, agent, run!, raceKey, ct);
        }

        // Advance NextRunUtc after all members have been dispatched
        Exception? advEx = null;
        try { await _service.AdvanceGroupTaskNextRunAsync(groupTask.Id, ct); }
        catch (Exception e) { advEx = e; }
        if (advEx is not null)
            _logger.LogError(advEx, "AdvanceGroupTaskNextRunAsync failed for group task '{Id}'.", groupTask.Id);
    }

    private void DispatchGroupRun(
        GroupScheduledTaskEntity groupTask, AgentDefinitionEntity agent,
        GroupScheduledTaskRunEntity run, string raceKey, CancellationToken ct)
    {
        if (!_semaphore.Wait(0))
        {
            _logger.LogDebug("Rate limit reached — deferred group run '{RunId}'.", run.Id);
            return;
        }

        _runningTasks[raceKey] = run.Id;

        _ = Task.Run(async () =>
        {
            try { await ExecuteGroupRunAsync(groupTask, agent, run, ct); }
            finally
            {
                _runningTasks.TryRemove(raceKey, out _);
                _semaphore.Release();
            }
        }, CancellationToken.None);
    }

    private async Task ExecuteGroupRunAsync(
        GroupScheduledTaskEntity groupTask, AgentDefinitionEntity agent,
        GroupScheduledTaskRunEntity run, CancellationToken appCt)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation(
            "Executing group run '{RunId}' — task '{TaskName}' for tenant {TenantId}.",
            run.Id, groupTask.Name, run.TenantId);

        string? result  = null;
        string? error   = null;
        string? session = null;
        bool    success = false;

        try
        {
            var prompt  = BuildGroupPrompt(groupTask, run.Id);
            var tenant  = TenantContext.System(run.TenantId);
            var request = new AgentRequest
            {
                Query       = prompt,
                TriggerType = "group_scheduled",
                Metadata    = new Dictionary<string, object?>
                {
                    ["group_task_id"]     = groupTask.Id,
                    ["group_task_name"]   = groupTask.Name,
                    ["group_id"]          = groupTask.GroupId,
                    ["run_id"]            = run.Id,
                    ["scheduled_for_utc"] = run.ScheduledForUtc.ToString("O")
                }
            };

            var response = await _runner.RunAsync(agent, request, tenant, appCt);
            session = response.SessionId;
            result  = response.Content;
            success = true;

            _logger.LogInformation("Group run '{RunId}' completed in {Ms}ms.", run.Id, sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (appCt.IsCancellationRequested)
        {
            error = "Cancelled due to application shutdown.";
            _logger.LogWarning("Group run '{RunId}' cancelled.", run.Id);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            _logger.LogError(ex, "Group run '{RunId}' failed.", run.Id);
        }
        finally
        {
            Exception? ex = null;
            try
            {
                await _service.CompleteGroupRunAsync(
                    run.Id, success, result, error, session,
                    sw.ElapsedMilliseconds, CancellationToken.None);
            }
            catch (Exception e) { ex = e; }

            if (ex is not null)
                _logger.LogError(ex, "CompleteGroupRunAsync failed for run '{RunId}'.", run.Id);
        }
    }

    internal string BuildGroupPrompt(GroupScheduledTaskEntity task, string runId)
    {
        _logger.LogDebug(
            "Run '{RunId}': building group prompt. PayloadType={PayloadType} TemplateLength={Len} ParametersJson={Params}",
            runId, task.PayloadType, task.PromptText?.Length ?? 0, task.ParametersJson ?? "(none)");

        IReadOnlyDictionary<string, string>? customVars = null;
        if (task.PayloadType == "template")
        {
            customVars = PromptVariableResolver.ParseJson(task.ParametersJson, _logger);
            _logger.LogDebug("Run '{RunId}': parsed {Count} template variable(s).",
                runId, customVars?.Count ?? 0);
        }

        var resolved = PromptVariableResolver.Resolve(task.PromptText, customVars, _logger);
        _logger.LogDebug("Run '{RunId}': resolved prompt — {Preview}",
            runId, resolved.Length > 200 ? resolved[..200] + "…" : resolved);
        return resolved;
    }

    // ── Prompt building ───────────────────────────────────────────────────────

    internal string BuildPrompt(ScheduledTaskEntity task, string runId)
    {
        _logger.LogDebug(
            "Run '{RunId}': building prompt. PayloadType={PayloadType} TemplateLength={Len} ParametersJson={Params}",
            runId, task.PayloadType, task.PromptText?.Length ?? 0, task.ParametersJson ?? "(none)");

        IReadOnlyDictionary<string, string>? customVars = null;
        if (task.PayloadType == "template")
        {
            customVars = PromptVariableResolver.ParseJson(task.ParametersJson, _logger);
            _logger.LogDebug("Run '{RunId}': parsed {Count} template variable(s).",
                runId, customVars?.Count ?? 0);
        }

        var resolved = PromptVariableResolver.Resolve(task.PromptText, customVars, _logger);
        _logger.LogDebug("Run '{RunId}': resolved prompt — {Preview}",
            runId, resolved.Length > 200 ? resolved[..200] + "…" : resolved);
        return resolved;
    }
}
