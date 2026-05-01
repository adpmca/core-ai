using System.IdentityModel.Tokens.Jwt;
using System.Text;
using DivaFsMcpServer.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace DivaFsMcpServer.Tests.Auth;

public sealed class StandaloneTokenServiceTests
{
    private static StandaloneTokenService Build(string signingKey = "test-signing-key-must-be-32-chars!!", int expiryMinutes = 60) =>
        new(Options.Create(new StandaloneJwtOptions
        {
            SigningKey = signingKey,
            Issuer = "diva-fs-mcp",
            Audience = "diva-fs-mcp-clients",
            TokenExpiryMinutes = expiryMinutes
        }));

    [Fact]
    public void IsEnabled_ReturnsTrue_WhenSigningKeySet()
    {
        var svc = Build();
        Assert.True(svc.IsEnabled);
    }

    [Fact]
    public void IsEnabled_ReturnsFalse_WhenSigningKeyEmpty()
    {
        var svc = Build(signingKey: "");
        Assert.False(svc.IsEnabled);
    }

    [Fact]
    public void IssueToken_ReturnsNonEmptyToken()
    {
        var token = Build().IssueToken();
        Assert.False(string.IsNullOrWhiteSpace(token.AccessToken));
        Assert.True(token.ExpiresIn > 0);
    }

    [Fact]
    public void IssueToken_ExpiresInMatchesConfig()
    {
        var token = Build(expiryMinutes: 30).IssueToken();
        Assert.InRange(token.ExpiresIn, 1799, 1800); // 30 min ±1s
    }

    [Fact]
    public void ValidateToken_AcceptsFreshToken()
    {
        var svc = Build();
        var token = svc.IssueToken().AccessToken;
        Assert.True(svc.ValidateToken(token));
    }

    [Fact]
    public void ValidateToken_ReturnsFalse_WhenDisabled()
    {
        var svc = Build(signingKey: "");
        Assert.False(svc.ValidateToken("any-token"));
    }

    [Fact]
    public void ValidateToken_ReturnsFalse_WhenSignedWithDifferentKey()
    {
        var svc1 = Build("key-one-must-be-at-least-32-chars!!");
        var svc2 = Build("key-two-must-be-at-least-32-chars!!");
        var token = svc1.IssueToken().AccessToken;
        Assert.False(svc2.ValidateToken(token));
    }

    [Fact]
    public void ValidateToken_ReturnsFalse_ForTamperedToken()
    {
        var svc = Build();
        var token = svc.IssueToken().AccessToken;
        var tampered = token[..^4] + "XXXX";
        Assert.False(svc.ValidateToken(tampered));
    }

    [Fact]
    public void ValidateToken_ReturnsFalse_ForExpiredToken()
    {
        const string key = "test-signing-key-must-be-32-chars!!";
        var svc = Build(key);

        // Manually craft a token that expired 5 minutes ago (beyond the 1-minute ClockSkew)
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var expiredToken = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: "diva-fs-mcp",
            audience: "diva-fs-mcp-clients",
            notBefore: DateTime.UtcNow.AddMinutes(-10),
            expires: DateTime.UtcNow.AddMinutes(-5),
            signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256)));

        Assert.False(svc.ValidateToken(expiredToken));
    }
}
