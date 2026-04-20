using System.Security.Claims;

namespace Diva.Infrastructure.Auth;

public interface IOAuthTokenValidator
{
    Task<ClaimsPrincipal?> ValidateTokenAsync(string token, CancellationToken ct);
}
