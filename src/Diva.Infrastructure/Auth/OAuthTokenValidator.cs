using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Diva.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Diva.Infrastructure.Auth;

public sealed class OAuthTokenValidator : IOAuthTokenValidator
{
    private readonly OAuthOptions _options;
    private readonly ILogger<OAuthTokenValidator> _logger;
    private readonly ConfigurationManager<OpenIdConnectConfiguration> _configManager;
    private readonly JwtSecurityTokenHandler _handler = new();

    public OAuthTokenValidator(IOptions<OAuthOptions> options, ILogger<OAuthTokenValidator> logger)
    {
        _options = options.Value;
        _logger  = logger;

        // Fetch JWKS from the authority's discovery document
        var metadataAddress = $"{_options.Authority.TrimEnd('/')}/.well-known/openid-configuration";
        _configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            metadataAddress,
            new OpenIdConnectConfigurationRetriever());
    }

    public async Task<ClaimsPrincipal?> ValidateTokenAsync(string token, CancellationToken ct)
    {
        try
        {
            var config = await _configManager.GetConfigurationAsync(ct);

            var parameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys        = config.SigningKeys,
                ValidateIssuer           = _options.ValidateIssuer,
                ValidIssuer              = _options.Authority,
                ValidateAudience         = _options.ValidateAudience,
                ValidAudience            = _options.Audience,
                ValidateLifetime         = true,
                ClockSkew                = TimeSpan.FromSeconds(30)
            };

            var principal = _handler.ValidateToken(token, parameters, out _);
            return principal;
        }
        catch (SecurityTokenExpiredException ex)
        {
            _logger.LogWarning("Token expired: {Message}", ex.Message);
            return null;
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning("Token validation failed: {Message}", ex.Message);
            return null;
        }
    }
}
