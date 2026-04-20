using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace Diva.Infrastructure.Learning;

/// <summary>
/// Stores business rules that the user chose to apply only for the current session.
/// Uses distributed cache (in-memory default, Redis for production).
/// Rules expire automatically after 24 hours of inactivity.
/// </summary>
public sealed class SessionRuleManager : ISessionRuleManager
{
    private readonly IDistributedCache _cache;
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    public SessionRuleManager(IDistributedCache cache) => _cache = cache;

    public async Task AddRuleAsync(string sessionId, SuggestedRule rule, CancellationToken ct)
    {
        var rules = await GetSessionRulesAsync(sessionId, ct);
        rules.Add(rule);
        await _cache.SetAsync(
            Key(sessionId),
            JsonSerializer.SerializeToUtf8Bytes(rules),
            new DistributedCacheEntryOptions { SlidingExpiration = Ttl },
            ct);
    }

    public async Task<List<SuggestedRule>> GetSessionRulesAsync(string sessionId, CancellationToken ct)
    {
        var data = await _cache.GetAsync(Key(sessionId), ct);
        return data is null
            ? []
            : JsonSerializer.Deserialize<List<SuggestedRule>>(data) ?? [];
    }

    public Task ClearSessionRulesAsync(string sessionId, CancellationToken ct)
        => _cache.RemoveAsync(Key(sessionId), ct);

    private static string Key(string sessionId) => $"session_rules:{sessionId}";
}
