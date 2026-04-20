using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Diva.Sso;

/// <summary>
/// DelegatingHandler that dynamically injects SSO headers into outbound HTTP requests.
/// Safe to use from singletons — IHttpContextAccessor uses AsyncLocal internally.
///
/// The headersFactory delegate is provided by the consuming project:
///   Diva.Host: ctx => McpRequestContext.FromTenant(ctx.TryGetTenantContext()).ToHeaders()
///   MCP server: ctx => ctx?.User?.ToSsoHeaders() ?? []
///
/// This keeps Diva.Sso free of any Diva.Core / TenantContext dependencies.
/// </summary>
public sealed class SsoAwareHttpMessageHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly Func<HttpContext?, Dictionary<string, string>> _headersFactory;
    private readonly ILogger? _logger;

    public SsoAwareHttpMessageHandler(
        IHttpContextAccessor httpContextAccessor,
        Func<HttpContext?, Dictionary<string, string>> headersFactory,
        ILogger? logger = null)
        : base(new HttpClientHandler())
    {
        _httpContextAccessor = httpContextAccessor;
        _headersFactory      = headersFactory;
        _logger              = logger;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var ctx     = _httpContextAccessor.HttpContext;
        var headers = _headersFactory(ctx);

        foreach (var (key, value) in headers)
            request.Headers.TryAddWithoutValidation(key, value);

        if (_logger is not null && _logger.IsEnabled(LogLevel.Debug))
        {
            var hasAuth = headers.ContainsKey("Authorization");
            var authHint = hasAuth
                ? $"present ({Mask(headers["Authorization"])})"
                : "MISSING";

            _logger.LogDebug(
                "SsoAwareHttpMessageHandler: {Method} {Uri} | HttpContext={CtxAvailable} | Authorization={AuthHint} | extra-headers={ExtraCount}",
                request.Method,
                request.RequestUri,
                ctx is not null,
                authHint,
                headers.Count - (hasAuth ? 1 : 0));
        }

        return base.SendAsync(request, ct);
    }

    /// Masks the token value — shows scheme + first 6 chars + "…" to confirm presence without leaking the full token.
    private static string Mask(string headerValue)
    {
        // e.g. "Bearer eyJhbGci…" → "Bearer eyJhbG…"
        var parts = headerValue.Split(' ', 2);
        if (parts.Length == 2 && parts[1].Length > 6)
            return $"{parts[0]} {parts[1][..6]}…";
        return parts[0] + " [short]";
    }
}
