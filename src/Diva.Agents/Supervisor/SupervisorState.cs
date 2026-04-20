using Diva.Agents.Workers;
using Diva.Core.Models;
using Diva.Infrastructure.Sessions;

namespace Diva.Agents.Supervisor;

/// <summary>
/// Mutable state bag passed through the supervisor pipeline stages.
/// </summary>
public sealed class SupervisorState
{
    public string RequestId { get; init; } = Guid.NewGuid().ToString();
    public AgentRequest Request { get; init; } = null!;
    public TenantContext TenantContext { get; init; } = null!;

    // Session (loaded by SupervisorAgent before pipeline starts)
    public string SessionId { get; set; } = "";
    public List<ConversationTurn> SessionHistory { get; set; } = [];

    // Set by DecomposeStage
    public List<SubTask> SubTasks { get; set; } = [];

    // Set by CapabilityMatchStage
    public List<(SubTask Task, IWorkerAgent Agent)> DispatchPlan { get; set; } = [];

    // Set by DispatchStage / MonitorStage
    public List<AgentResponse> WorkerResults { get; set; } = [];

    // Set by IntegrateStage
    public string IntegratedResult { get; set; } = "";

    // Accumulated by DispatchStage from WorkerResults (passed to VerifyStage)
    public string ToolEvidence { get; set; } = "";

    // Set by VerifyStage (Phase 13)
    public VerificationResult? Verification { get; set; }

    // Set by DeliverStage
    public bool DeliveryComplete { get; set; }

    public SupervisorStatus Status { get; set; } = SupervisorStatus.Running;
    public string? ErrorMessage { get; set; }

    /// <summary>Instructions from the caller/API to propagate to all sub-tasks and worker agents.</summary>
    public string? SupervisorInstructions { get; set; }
}

/// <summary>A unit of work to dispatch to a single worker agent.</summary>
public sealed record SubTask(
    string Description,
    string[] RequiredCapabilities,
    int SiteId,
    int TenantId,
    string? Instructions = null);

public enum SupervisorStatus { Running, Completed, Failed }
