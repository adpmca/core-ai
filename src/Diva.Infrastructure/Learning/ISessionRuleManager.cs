namespace Diva.Infrastructure.Learning;

public interface ISessionRuleManager
{
    Task AddRuleAsync(string sessionId, SuggestedRule rule, CancellationToken ct);
    Task<List<SuggestedRule>> GetSessionRulesAsync(string sessionId, CancellationToken ct);
    Task ClearSessionRulesAsync(string sessionId, CancellationToken ct);
}
