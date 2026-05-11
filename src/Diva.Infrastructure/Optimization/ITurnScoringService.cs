namespace Diva.Infrastructure.Optimization;

public interface ITurnScoringService
{
    Task ScoreTurnAsync(
        string sessionId,
        int turnNumber,
        string agentId,
        string userMessage,
        string assistantResponse,
        string toolEvidence,
        CancellationToken ct);
}
