using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace DivaFsMcpServer.Auth;

public sealed class StandaloneTokenService(IOptions<StandaloneJwtOptions> opts)
{
    public bool IsEnabled => !string.IsNullOrEmpty(opts.Value.SigningKey);

    public TokenResponse IssueToken()
    {
        var o = opts.Value;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.SigningKey));
        var expires = DateTime.UtcNow.AddMinutes(o.TokenExpiryMinutes);

        var jwt = new JwtSecurityToken(
            issuer: o.Issuer,
            audience: o.Audience,
            notBefore: DateTime.UtcNow,
            expires: expires,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new(new JwtSecurityTokenHandler().WriteToken(jwt),
                   (int)(expires - DateTime.UtcNow).TotalSeconds);
    }

    public bool ValidateToken(string token)
    {
        if (!IsEnabled) return false;
        var o = opts.Value;
        try
        {
            new JwtSecurityTokenHandler { MapInboundClaims = false }
                .ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.SigningKey)),
                    ValidateIssuer = true,
                    ValidIssuer = o.Issuer,
                    ValidateAudience = true,
                    ValidAudience = o.Audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1)
                }, out _);
            return true;
        }
        catch { return false; }
    }

    public sealed record TokenResponse(string AccessToken, int ExpiresIn);
}
