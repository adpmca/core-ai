using Diva.Core.Models;

namespace Diva.Infrastructure.Optimization;

public interface ISessionAnalyzer
{
    Task<SessionAnalysisReport> AnalyzeSessionAsync(
        string sessionId, string agentId, int tenantId, CancellationToken ct);

    Task<SessionAnalysisReport> AnalyzeAggregateAsync(
        string agentId, int tenantId, DateTime from, DateTime to, CancellationToken ct);
}
