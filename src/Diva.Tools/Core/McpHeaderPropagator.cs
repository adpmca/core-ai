using Diva.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Diva.Tools.Core;

/// <summary>
/// Builds the standard MCP header set from the current request's TenantContext.
/// Used by TenantAwareMcpClient to inject tenant identity into every MCP tool call.
/// </summary>
public sealed class McpHeaderPropagator
{
    private readonly IHttpContextAccessor _httpContext;

    public McpHeaderPropagator(IHttpContextAccessor httpContext)
    {
        _httpContext = httpContext;
    }

    public TenantContext GetTenantContext() =>
        _httpContext.HttpContext?.Items["TenantContext"] as TenantContext
        ?? TenantContext.System(tenantId: 1);

    public Dictionary<string, string> GetHeaders()
    {
        var tenant = GetTenantContext();
        return McpRequestContext.FromTenant(tenant).ToHeaders();
    }
}
