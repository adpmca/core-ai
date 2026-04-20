namespace Diva.Core.Configuration;

public sealed class LocalAuthOptions
{
    public const string SectionName = "LocalAuth";
    public string SigningKey { get; set; } = "change-me-in-production-must-be-32-chars!!";
    public int TokenExpiryHours { get; set; } = 8;
}
