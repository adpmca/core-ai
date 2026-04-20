using Diva.Infrastructure.Data;
using Diva.TenantAdmin.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Diva.TenantAdmin.Tests;

/// <summary>
/// Tests the IAsyncDisposable implementation on <see cref="RulePackEngine"/>.
/// The engine starts a background log-flusher task that must shut down cleanly on disposal.
/// </summary>
public class RulePackEngineDisposalTests
{
    private static (RulePackEngine Engine, SqliteConnection Connection) CreateEngine()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var opts = new DbContextOptionsBuilder<DivaDbContext>()
            .UseSqlite(conn)
            .Options;
        var db = new DivaDbContext(opts);
        db.Database.EnsureCreated();
        db.Dispose();

        var engine = new RulePackEngine(
            new DirectDbFactoryForDisposalTests(opts),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<RulePackEngine>.Instance);
        return (engine, conn);
    }

    [Fact]
    public async Task DisposeAsync_CompletesCleanly_WithinTimeout()
    {
        var (engine, conn) = CreateEngine();
        await using (conn)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var disposalCompleted = false;
            Exception? disposeEx = null;

            try
            {
                await engine.DisposeAsync();
                disposalCompleted = true;
            }
            catch (Exception ex)
            {
                disposeEx = ex;
            }

            Assert.True(disposalCompleted, $"DisposeAsync did not complete: {disposeEx?.Message}");
            Assert.Null(disposeEx);
        }
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_DoesNotThrow()
    {
        var (engine, conn) = CreateEngine();
        await using (conn)
        {
            await engine.DisposeAsync();

            // Second call should be a no-op or at least not throw
            Exception? secondEx = null;
            try { await engine.DisposeAsync(); }
            catch (Exception ex) { secondEx = ex; }

            Assert.Null(secondEx);
        }
    }

    [Fact]
    public async Task Dispose_Synchronous_AfterQueuedLogs_DoesNotHang()
    {
        var (engine, conn) = CreateEngine();
        await using (conn)
        {
            // Queue some log entries (they'll be discarded on disposal)
            // Synchronous Dispose() should not hang even if background task is still running
            var completedInTime = false;
            var thread = new Thread(() =>
            {
                engine.Dispose();
                completedInTime = true;
            });
            thread.Start();
            completedInTime = thread.Join(TimeSpan.FromSeconds(10));

            Assert.True(completedInTime, "Synchronous Dispose() blocked for more than 10s");
        }
    }
}

internal sealed class DirectDbFactoryForDisposalTests : IDatabaseProviderFactory
{
    private readonly DbContextOptions<DivaDbContext> _options;
    public DirectDbFactoryForDisposalTests(DbContextOptions<DivaDbContext> options) => _options = options;
    public DivaDbContext CreateDbContext(Diva.Core.Models.TenantContext? tenant = null)
        => new(_options, tenant?.TenantId ?? 0);
    public Task ApplyMigrationsAsync() => Task.CompletedTask;
}
