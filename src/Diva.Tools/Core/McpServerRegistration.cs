using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Diva.Tools.Core;

/// <summary>Marker interface for MCP tool types managed by Diva's registration helper.</summary>
public interface IDivaMcpToolType { }

public static class DivaMcpServerExtensions
{
    /// <summary>
    /// Registers <typeparamref name="T"/> as Scoped in DI and adds its tools to the MCP server.
    /// Scoped lifetime is required because tool handlers use IHttpContextAccessor (request-scoped).
    /// </summary>
    public static IMcpServerBuilder WithDivaMcpTools<T>(this IMcpServerBuilder builder)
        where T : class, IDivaMcpToolType
    {
        builder.Services.AddScoped<T>();
        return builder.WithTools<T>();
    }
}
