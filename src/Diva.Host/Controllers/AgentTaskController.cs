using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Diva.Agents.Registry;
using Diva.Core.Configuration;
using Diva.Core.Models;
using Diva.Infrastructure.Auth;
using Diva.Infrastructure.Data;
using Diva.Infrastructure.Data.Entities;
using Diva.Infrastructure.LiteLLM;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Diva.Host.Controllers;

[ApiController]
[Route("tasks")]
[EnableRateLimiting("a2a")]
public class AgentTaskController : ControllerBase
{
    private readonly IDatabaseProviderFactory _db;
    private readonly IAgentRunner _runner;
    private readonly IAgentRegistry _registry;
    private readonly A2AOptions _a2aOptions;
    private readonly ILogger<AgentTaskController> _logger;

    // In-memory CTS tracking for running tasks (Gap #23)
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> _runningTasks = new();

    private static readonly JsonSerializerOptions _sseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public AgentTaskController(
        IDatabaseProviderFactory db,
        IAgentRunner runner,
        IAgentRegistry registry,
        IOptions<A2AOptions> a2aOptions,
        ILogger<AgentTaskController> logger)
    {
        _db = db;
        _runner = runner;
        _registry = registry;
        _a2aOptions = a2aOptions.Value;
        _logger = logger;
    }

    /// <summary>POST /tasks/send — accepts A2A task, dispatches via IAgentRunner, streams SSE.</summary>
    [HttpPost("send")]
    public async Task SendTask([FromQuery] string agentId, [FromBody] A2ATaskRequest body, CancellationToken ct)
    {
        if (!_a2aOptions.Enabled)
        {
            Response.StatusCode = 404;
            return;
        }

        // Concurrent task limit enforcement
        if (_a2aOptions.MaxConcurrentTasks > 0 && _runningTasks.Count >= _a2aOptions.MaxConcurrentTasks)
        {
            Response.StatusCode = 429;
            await Response.WriteAsJsonAsync(new { error = $"Too many concurrent tasks (max: {_a2aOptions.MaxConcurrentTasks})" }, ct);
            return;
        }

        // Delegation depth protection (Gap #24)
        var depthHeader = Request.Headers["X-A2A-Depth"].FirstOrDefault();
        var depth = int.TryParse(depthHeader, out var d) ? d : 0;
        if (depth >= _a2aOptions.MaxDelegationDepth)
        {
            Response.StatusCode = 400;
            await Response.WriteAsJsonAsync(new { error = $"A2A delegation depth exceeded (max: {_a2aOptions.MaxDelegationDepth})" }, ct);
            return;
        }

        var tenant = HttpContext.TryGetTenantContext() ?? TenantContext.System(1);
        using var db = _db.CreateDbContext(tenant);

        var definition = await db.AgentDefinitions.FindAsync([agentId], ct);
        if (definition is null || !definition.IsEnabled)
        {
            Response.StatusCode = 404;
            return;
        }

        // Create task record
        var taskId = Guid.NewGuid().ToString();
        var taskEntity = new AgentTaskEntity
        {
            Id = taskId,
            TenantId = tenant.TenantId,
            AgentId = agentId,
            Status = "working",
            InputJson = body.Query,
            CreatedAt = DateTime.UtcNow,
        };
        db.AgentTasks.Add(taskEntity);
        await db.SaveChangesAsync(ct);

        // Create linked CTS for cancellation support
        using var taskCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _runningTasks[taskId] = taskCts;

        Response.Headers["Content-Type"] = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";
        // Expose task ID before streaming so A2AAgentClient can emit a2a_delegation_start
        Response.Headers["X-A2A-Task-Id"] = taskId;
        Response.Headers.Append("Access-Control-Expose-Headers", "X-A2A-Task-Id");

        var request = new AgentRequest
        {
            Query = body.Query ?? "",
            SessionId = body.SessionId,
            Metadata = new Dictionary<string, object?> { ["a2a_depth"] = depth },
        };

        try
        {
            await foreach (var chunk in _runner.InvokeStreamAsync(definition, request, tenant, taskCts.Token))
            {
                var json = JsonSerializer.Serialize(chunk, _sseOptions);
                await Response.WriteAsync($"data: {json}\n\n", taskCts.Token);
                await Response.Body.FlushAsync(taskCts.Token);

                if (chunk.Type == "final_response")
                    taskEntity.OutputText = chunk.Content;
            }

            taskEntity.Status = "completed";
            taskEntity.CompletedAt = DateTime.UtcNow;
        }
        catch (OperationCanceledException)
        {
            taskEntity.Status = "canceled";
            taskEntity.CompletedAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A2A task {TaskId} failed", taskId);
            taskEntity.Status = "failed";
            taskEntity.ErrorMessage = ex.Message;
            taskEntity.CompletedAt = DateTime.UtcNow;
        }
        finally
        {
            _runningTasks.TryRemove(taskId, out _);
            using var saveDb = _db.CreateDbContext(tenant);
            saveDb.AgentTasks.Update(taskEntity);
            await saveDb.SaveChangesAsync(CancellationToken.None);
        }
    }

    /// <summary>GET /tasks/{taskId} — returns task status.</summary>
    [HttpGet("{taskId}")]
    public async Task<IActionResult> GetTask(string taskId, CancellationToken ct)
    {
        if (!_a2aOptions.Enabled)
            return NotFound(new { error = "A2A is not enabled" });

        var tenant = HttpContext.TryGetTenantContext() ?? TenantContext.System(1);
        using var db = _db.CreateDbContext(tenant);
        var task = await db.AgentTasks.FindAsync([taskId], ct);
        if (task is null) return NotFound();

        return Ok(new
        {
            id = task.Id,
            agentId = task.AgentId,
            status = task.Status,
            output = task.OutputText,
            error = task.ErrorMessage,
            createdAt = task.CreatedAt,
            completedAt = task.CompletedAt,
        });
    }

    /// <summary>DELETE /tasks/{taskId} — cancels a running task (Gap #23).</summary>
    [HttpDelete("{taskId}")]
    public async Task<IActionResult> CancelTask(string taskId, CancellationToken ct)
    {
        if (!_a2aOptions.Enabled)
            return NotFound(new { error = "A2A is not enabled" });

        if (_runningTasks.TryGetValue(taskId, out var cts))
        {
            await cts.CancelAsync();
            _runningTasks.TryRemove(taskId, out _);
        }

        var tenant = HttpContext.TryGetTenantContext() ?? TenantContext.System(1);
        using var db = _db.CreateDbContext(tenant);
        var task = await db.AgentTasks.FindAsync([taskId], ct);
        if (task is null) return NotFound();

        if (task.Status is "pending" or "working")
        {
            task.Status = "canceled";
            task.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return Ok(new { id = task.Id, status = task.Status });
    }
}

public record A2ATaskRequest(string? Query, string? SessionId = null);
