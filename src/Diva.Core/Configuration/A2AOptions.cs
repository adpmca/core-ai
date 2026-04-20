namespace Diva.Core.Configuration;

public sealed class A2AOptions
{
    public const string SectionName = "A2A";

    /// <summary>Enable A2A endpoints (AgentCard, /tasks/send, /tasks/{id}).</summary>
    public bool Enabled { get; init; } = false;

    /// <summary>Seconds before a running task is automatically failed.</summary>
    public int TaskTimeoutSeconds { get; init; } = 300;

    /// <summary>Base URL for AgentCard generation (auto-detected if null).</summary>
    public string? BaseUrl { get; init; }

    /// <summary>Max A2A delegation depth to prevent infinite loops. Default 5.</summary>
    public int MaxDelegationDepth { get; init; } = 5;

    /// <summary>Max concurrent A2A tasks allowed. 0 = unlimited. Default 10.</summary>
    public int MaxConcurrentTasks { get; init; } = 10;

    /// <summary>Days to retain completed/failed/canceled tasks before cleanup. Default 7.</summary>
    public int TaskRetentionDays { get; init; } = 7;

    /// <summary>A2A endpoint rate limit per tenant per minute. Default 10.</summary>
    public int RateLimitPerMinute { get; init; } = 10;
}
