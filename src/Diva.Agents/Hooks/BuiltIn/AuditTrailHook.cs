namespace Diva.Agents.Hooks.BuiltIn;

using Diva.Core.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// Writes a structured audit log entry after every agent response.
/// Captures: tenant, agent, session, user, iteration count, tool evidence summary,
/// verification status, and response length.
/// Logs at Information level — Serilog sinks (Seq, file, SIEM) pick it up automatically.
/// </summary>
public sealed class AuditTrailHook(ILogger<AuditTrailHook> logger) : IOnAfterResponseHook
{
    public int Order => 200; // Run last — after all other after-response hooks

    public Task OnAfterResponseAsync(
        AgentHookContext context, AgentResponse response, CancellationToken ct)
    {
        logger.LogInformation(
            "AgentAudit: TenantId={TenantId} AgentId={AgentId} SessionId={SessionId} " +
            "UserId={UserId} Archetype={Archetype} Iterations={Iterations} " +
            "ResponseLength={ResponseLength} HasToolEvidence={HasToolEvidence} " +
            "Verified={Verified} VerificationMode={VerificationMode}",
            context.Tenant.TenantId,
            context.AgentId,
            context.SessionId,
            context.Tenant.UserId,
            context.ArchetypeId,
            context.CurrentIteration,
            response.Content?.Length ?? 0,
            !string.IsNullOrWhiteSpace(context.ToolEvidence),
            response.Verification?.IsVerified,
            response.Verification?.Mode ?? "none");

        return Task.CompletedTask;
    }
}
