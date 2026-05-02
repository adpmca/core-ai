using Microsoft.Extensions.Options;

namespace Diva.Tools.FileSystem;

/// <summary>
/// Singleton bounded semaphore that caps concurrent bash script executions across all clients.
/// MaxConcurrentScripts=0 disables the cap (unlimited).
/// </summary>
public sealed class ScriptThrottle : IDisposable
{
    private readonly SemaphoreSlim? _semaphore;
    private readonly int _max;

    public ScriptThrottle(IOptions<FileSystemOptions> opts)
    {
        _max = opts.Value.MaxConcurrentScripts;
        if (_max > 0)
            _semaphore = new SemaphoreSlim(_max, _max);
    }

    public IDisposable Acquire(int timeoutMs = 5000)
    {
        if (_semaphore is null)
            return NullReleaser.Instance;

        if (!_semaphore.Wait(timeoutMs))
            throw new InvalidOperationException(
                $"Script queue full — {_max} scripts already running. Try again shortly.");

        return new SemaphoreReleaser(_semaphore);
    }

    public void Dispose() => _semaphore?.Dispose();

    private sealed class SemaphoreReleaser(SemaphoreSlim sem) : IDisposable
    {
        public void Dispose() => sem.Release();
    }

    private sealed class NullReleaser : IDisposable
    {
        public static readonly NullReleaser Instance = new();
        public void Dispose() { }
    }
}
