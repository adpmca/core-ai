using Diva.Tools.FileSystem;
using DivaFsMcpServer.Auth;
using Microsoft.Extensions.Options;

namespace DivaFsMcpServer;

public sealed class StandaloneAuthMiddleware(
    IOptions<FileSystemOptions> fsOpts,
    StandaloneTokenService tokenService,
    ILogger<StandaloneAuthMiddleware> logger) : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        // Token endpoint is unauthenticated
        if (context.Request.Path.StartsWithSegments("/auth"))
        {
            await next(context);
            return;
        }

        var staticKey = fsOpts.Value.StandaloneApiKey;

        // No auth configured → allow (trusted network)
        if (string.IsNullOrEmpty(staticKey) && !tokenService.IsEnabled)
        {
            await next(context);
            return;
        }

        if (IsAuthenticated(context, staticKey))
        {
            await next(context);
            return;
        }

        logger.LogWarning("Auth rejected {Method} {Path} from {RemoteIp}",
            context.Request.Method, context.Request.Path,
            context.Connection.RemoteIpAddress);
        context.Response.StatusCode = 401;
        await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
    }

    private bool IsAuthenticated(HttpContext context, string? staticKey)
    {
        var bearer = context.Request.Headers.Authorization.ToString();
        var apiKey = context.Request.Headers["X-Api-Key"].ToString();

        // X-Api-Key: always checked against static key
        if (!string.IsNullOrEmpty(staticKey) && apiKey == staticKey)
            return true;

        if (bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = bearer[7..];
            // Valid JWT (returns false when JWT not enabled)
            if (tokenService.ValidateToken(token)) return true;
            // Static key sent as Bearer header (backward compat)
            if (!string.IsNullOrEmpty(staticKey) && token == staticKey) return true;
        }

        return false;
    }
}
