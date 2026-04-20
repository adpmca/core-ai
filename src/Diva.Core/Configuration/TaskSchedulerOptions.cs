namespace Diva.Core.Configuration;

public sealed class TaskSchedulerOptions
{
    public const string SectionName = "TaskScheduler";

    /// <summary>Master switch — set false to disable all scheduled execution without removing schedules.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>How often the polling loop checks for due tasks (seconds).</summary>
    public int PollIntervalSeconds { get; set; } = 30;

    /// <summary>Maximum tasks executing concurrently per host instance.</summary>
    public int MaxConcurrentRuns { get; set; } = 5;

    /// <summary>Maximum pending (queued) runs allowed per task before new due-fires are silently dropped.</summary>
    public int MaxQueuedRunsPerTask { get; set; } = 10;

    /// <summary>Maximum characters of agent response stored per run for history.</summary>
    public int MaxResponseStorageChars { get; set; } = 4000;

    /// <summary>
    /// A run that has been in "running" status for longer than this many minutes is
    /// considered stuck and will be automatically marked as failed so queued pending
    /// runs can proceed. Default 60 minutes. Set 0 to disable timeout recovery (startup
    /// recovery on service restart still applies).
    /// </summary>
    public int StuckRunTimeoutMinutes { get; set; } = 60;
}
