using System.Collections.Concurrent;

namespace Diva.Tools.FileSystem;

/// <summary>
/// Singleton per-path exclusive lock for write operations.
/// Prevents two concurrent requests from writing to the same file simultaneously.
/// </summary>
public sealed class FileWriteLock
{
    private readonly ConcurrentDictionary<string, object> _gates =
        new(StringComparer.OrdinalIgnoreCase);

    public IDisposable Acquire(string canonicalPath)
    {
        var gate = _gates.GetOrAdd(canonicalPath, _ => new object());
        Monitor.Enter(gate);
        return new MonitorReleaser(gate);
    }

    private sealed class MonitorReleaser(object gate) : IDisposable
    {
        public void Dispose() => Monitor.Exit(gate);
    }
}
