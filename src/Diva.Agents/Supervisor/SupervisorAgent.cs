using Diva.Core.Models;
using Diva.Infrastructure.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Diva.Agents.Supervisor;

/// <summary>
/// Orchestrates the supervisor pipeline: decompose → match → dispatch → monitor → integrate → verify → deliver.
/// Session history is loaded before the pipeline runs and saved after it completes.
/// </summary>
public sealed class SupervisorAgent : ISupervisorAgent
{
    private readonly IEnumerable<ISupervisorPipelineStage> _stages;
    private readonly AgentSessionService _sessions;
    private readonly ILogger<SupervisorAgent> _logger;
    private readonly IServiceScopeFactory? _scopeFactory;

    public SupervisorAgent(
        IEnumerable<ISupervisorPipelineStage> stages,
        AgentSessionService sessions,
        ILogger<SupervisorAgent> logger,
        IServiceScopeFactory? scopeFactory = null)
    {
        _stages       = stages;
        _sessions     = sessions;
        _logger       = logger;
        _scopeFactory = scopeFactory;
    }

    public async Task<AgentResponse> InvokeAsync(
        AgentRequest request,
        TenantContext tenant,
        CancellationToken ct)
    {
        // ── Load or create session ────────────────────────────────────────────
        var (sessionId, history) = await _sessions.GetOrCreateAsync(
            request.SessionId, "supervisor", tenant, ct);

        _logger.LogInformation("Supervisor invoked: session={SessionId}, tenant={TenantId}, query={Query}",
            sessionId, tenant.TenantId, request.Query);

        // ── Session trace setup ───────────────────────────────────────────────
        await using var traceScope = _scopeFactory?.CreateAsyncScope();
        var traceWriter = traceScope?.ServiceProvider.GetService<SessionTraceWriter>();
        if (traceWriter is not null)
            await traceWriter.EnsureSessionAsync(
                sessionId, request.ParentSessionId, tenant,
                "supervisor", "Supervisor", isSupervisor: true, ct);

        // ── Build initial pipeline state ──────────────────────────────────────
        var state = new SupervisorState
        {
            Request                = request,
            TenantContext          = tenant,
            SessionId              = sessionId,
            SessionHistory         = history,
            SupervisorInstructions = request.Instructions
        };

        // ── Run pipeline stages ───────────────────────────────────────────────
        foreach (var stage in _stages)
        {
            var stageName = stage.GetType().Name;
            _logger.LogDebug("Running stage: {Stage}", stageName);
            try
            {
                state = await stage.ExecuteAsync(state, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pipeline stage {Stage} failed", stageName);
                state.Status       = SupervisorStatus.Failed;
                state.ErrorMessage = $"Stage {stageName} failed: {ex.Message}";
            }

            if (state.Status == SupervisorStatus.Failed)
                break;
        }

        var content = state.Status == SupervisorStatus.Failed
            ? state.ErrorMessage ?? "Supervisor pipeline failed."
            : state.IntegratedResult;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var finalStatus = state.Status == SupervisorStatus.Completed ? "completed" : "failed";

        // ── Persist turn ──────────────────────────────────────────────────────
        if (!string.IsNullOrEmpty(content))
        {
            var turnNumber = await _sessions.SaveTurnAsync(sessionId, request.Query, content, ct);

            // Flush supervisor trace — one "iteration" per pipeline run
            if (traceWriter is not null)
            {
                var totalIterations = state.WorkerResults.Sum(r => r.ToolsUsed.Count > 0 ? 1 : 1);
                var totalToolCalls = state.WorkerResults.Sum(r => r.ToolsUsed.Count);
                await traceWriter.FlushTurnAsync(
                    sessionId, turnNumber, request.Query, content,
                    (long)state.WorkerResults.DefaultIfEmpty().Max(r => r?.ExecutionTime.TotalMilliseconds ?? 0),
                    agentId: "supervisor", modelId: "", provider: "supervisor", ct);
            }
        }

        if (traceWriter is not null)
            await traceWriter.CompleteSessionAsync(sessionId, finalStatus, ct);

        return new AgentResponse
        {
            Success      = state.Status == SupervisorStatus.Completed,
            Content      = content ?? "",
            SessionId    = sessionId,
            ErrorMessage = state.ErrorMessage,
            AgentName    = "supervisor",
            ToolsUsed    = state.WorkerResults.SelectMany(r => r.ToolsUsed).Distinct().ToList(),
            ExecutionTime = state.WorkerResults.Count > 0
                ? state.WorkerResults.Max(r => r.ExecutionTime)
                : TimeSpan.Zero
        };
    }
}
