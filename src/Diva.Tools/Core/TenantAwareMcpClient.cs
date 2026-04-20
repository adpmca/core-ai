using Diva.Core.Models;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Diva.Tools.Core;

/// <summary>
/// Wraps MCP tool invocations with automatic tenant header injection.
/// Every tool call includes X-Tenant-ID, X-Site-ID, X-Correlation-ID, and Authorization.
/// </summary>
public sealed class TenantAwareMcpClient
{
    private readonly McpHeaderPropagator _propagator;
    private readonly ILogger<TenantAwareMcpClient> _logger;

    public TenantAwareMcpClient(
        McpHeaderPropagator propagator,
        ILogger<TenantAwareMcpClient> logger)
    {
        _propagator = propagator;
        _logger     = logger;
    }

    /// <summary>
    /// Calls an MCP tool on an already-established client, logging tenant context.
    /// Headers are propagated via the MCP client's transport (configured at client creation time).
    /// </summary>
    public async Task<CallToolResult> CallToolAsync(
        McpClient client,
        string toolName,
        Dictionary<string, object?> parameters,
        CancellationToken ct)
    {
        var headers = _propagator.GetHeaders();

        _logger.LogDebug(
            "MCP call: tool={Tool} tenant={TenantId} site={SiteId} correlation={CorrelationId}",
            toolName,
            headers.GetValueOrDefault("X-Tenant-ID", "?"),
            headers.GetValueOrDefault("X-Site-ID", "?"),
            headers.GetValueOrDefault("X-Correlation-ID", "?"));

        return await client.CallToolAsync(toolName, parameters, cancellationToken: ct);
    }

    /// <summary>
    /// Creates an HTTP-transport MCP client with tenant headers pre-configured.
    /// </summary>
    public async Task<McpClient> CreateHttpClientAsync(string endpoint, CancellationToken ct)
    {
        var headers = _propagator.GetHeaders();

        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint       = new Uri(endpoint),
            AdditionalHeaders = headers
        });

        return await McpClient.CreateAsync(transport, cancellationToken: ct);
    }
}
