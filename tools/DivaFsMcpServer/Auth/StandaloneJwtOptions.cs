namespace DivaFsMcpServer.Auth;

public sealed class StandaloneJwtOptions
{
    public const string SectionName = "Jwt";
    public string SigningKey { get; set; } = "";
    public string Issuer { get; set; } = "diva-fs-mcp";
    public string Audience { get; set; } = "diva-fs-mcp-clients";
    public int TokenExpiryMinutes { get; set; } = 60;
}
