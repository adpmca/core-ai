using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace DivaFsMcpServer.Tests.Auth;

public sealed class AuthEndpointTests : IDisposable
{
    private const string MasterKey = "test-master-key";
    private const string SigningKey = "test-signing-key-must-be-32-chars!!";

    private readonly WebApplicationFactory<Program> _factory;

    public AuthEndpointTests()
    {
        // Force HTTP mode so the auth middleware and /auth/token endpoint are registered
        Environment.SetEnvironmentVariable("DIVA_FS_MCP_PORT", "test");

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["FileSystem:StandaloneApiKey"] = MasterKey,
                    ["FileSystem:AllowedBasePaths:0"] = Path.GetTempPath(),
                    ["Jwt:SigningKey"] = SigningKey,
                    ["Jwt:TokenExpiryMinutes"] = "60"
                })));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("DIVA_FS_MCP_PORT", null);
        _factory.Dispose();
    }

    [Fact]
    public async Task PostAuthToken_WithCorrectKey_Returns200AndToken()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/auth/token", new { apiKey = MasterKey });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<TokenBody>();
        Assert.False(string.IsNullOrWhiteSpace(body?.AccessToken));
        Assert.Equal("Bearer", body?.TokenType);
        Assert.True(body?.ExpiresIn > 0);
    }

    [Fact]
    public async Task PostAuthToken_WithWrongKey_Returns401()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/auth/token", new { apiKey = "wrong-key" });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task PostAuthToken_WhenJwtDisabled_Returns501()
    {
        Environment.SetEnvironmentVariable("DIVA_FS_MCP_PORT", "test");
        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["FileSystem:StandaloneApiKey"] = MasterKey,
                    ["FileSystem:AllowedBasePaths:0"] = Path.GetTempPath(),
                    ["Jwt:SigningKey"] = "" // JWT disabled
                })));

        var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/auth/token", new { apiKey = MasterKey });
        Assert.Equal(HttpStatusCode.NotImplemented, resp.StatusCode);
        factory.Dispose();
    }

    [Fact]
    public async Task McpEndpoint_WithValidJwt_PassesAuth()
    {
        var client = _factory.CreateClient();

        // Get a JWT
        var tokenResp = await client.PostAsJsonAsync("/auth/token", new { apiKey = MasterKey });
        var body = await tokenResp.Content.ReadFromJsonAsync<TokenBody>();

        // Call /mcp with JWT — should NOT return 401 (MCP may return 4xx for other reasons)
        var req = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        req.Headers.Authorization = new("Bearer", body!.AccessToken);
        req.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        var mcpResp = await client.SendAsync(req);

        Assert.NotEqual(HttpStatusCode.Unauthorized, mcpResp.StatusCode);
    }

    [Fact]
    public async Task McpEndpoint_WithNoToken_Returns401()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsync("/mcp",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    private sealed record TokenBody(
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string AccessToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("expires_in")] int ExpiresIn,
        [property: System.Text.Json.Serialization.JsonPropertyName("token_type")] string TokenType);
}
