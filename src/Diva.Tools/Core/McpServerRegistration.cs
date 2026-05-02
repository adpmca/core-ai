using Diva.Tools.FileSystem;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Diva.Tools.Core;

/// <summary>Marker interface for MCP tool types managed by Diva's registration helper.</summary>
public interface IDivaMcpToolType { }

public static class DivaMcpServerExtensions
{
    /// <summary>
    /// Registers <typeparamref name="T"/> as Scoped in DI and adds its tools to the MCP server.
    /// Also registers concurrency-safety singletons (FileWriteLock, ScriptThrottle).
    /// </summary>
    public static IMcpServerBuilder WithDivaMcpTools<T>(this IMcpServerBuilder builder)
        where T : class, IDivaMcpToolType
    {
        builder.Services.AddSingleton<FileWriteLock>();
        builder.Services.AddSingleton<ScriptThrottle>();
        builder.Services.AddScoped<T>();
        return builder.WithTools<T>();
    }
}
