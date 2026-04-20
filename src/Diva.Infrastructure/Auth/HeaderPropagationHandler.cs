using Diva.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Diva.Infrastructure.Auth;

/// <summary>
/// DelegatingHandler that injects tenant headers into all outbound HttpClient requests.
/// Register as a typed client handler for LiteLLMClient and MCP tool HTTP clients.
/// </summary>
public sealed class HeaderPropagationHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HeaderPropagationHandler(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var tenant = _httpContextAccessor.HttpContext?.TryGetTenantContext();
        if (tenant is not null)
        {
            var mcpContext = McpRequestContext.FromTenant(tenant);
            foreach (var (key, value) in mcpContext.ToHeaders())
            {
                request.Headers.TryAddWithoutValidation(key, value);
            }
        }
        return base.SendAsync(request, ct);
    }
}
