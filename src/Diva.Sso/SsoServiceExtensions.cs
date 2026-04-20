using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Diva.Sso;

public static class SsoServiceExtensions
{
    /// <summary>
    /// Registers ISsoTokenValidator (SsoTokenValidator) and its dependencies.
    /// Call in Program.cs: builder.Services.AddSsoValidation()
    /// </summary>
    public static IServiceCollection AddSsoValidation(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddHttpClient("sso-introspect");
        services.AddHttpClient("sso-auth");   // used by AuthController for code→token exchange
        services.AddSingleton<ISsoTokenValidator, SsoTokenValidator>();
        return services;
    }

    /// <summary>
    /// Registers SsoAwareHttpMessageHandler with a custom headers factory.
    /// The factory receives the current HttpContext and returns headers to inject.
    ///
    /// Example (Diva.Host):
    ///   services.AddSsoHttpPassthrough(ctx =>
    ///       McpRequestContext.FromTenant(ctx?.TryGetTenantContext()).ToHeaders());
    /// </summary>
    /// <summary>
    /// Note: IHttpContextAccessor must be registered separately by the consuming project
    /// (e.g., builder.Services.AddHttpContextAccessor() in Program.cs).
    /// </summary>
    public static IServiceCollection AddSsoHttpPassthrough(
        this IServiceCollection services,
        Func<HttpContext?, Dictionary<string, string>> headersFactory)
    {
        services.AddTransient(sp => new SsoAwareHttpMessageHandler(
            sp.GetRequiredService<IHttpContextAccessor>(),
            headersFactory));
        return services;
    }
}
