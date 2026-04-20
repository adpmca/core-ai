namespace Diva.Core.Configuration;

public sealed class AppBrandingOptions
{
    public const string SectionName = "AppBranding";

    public string ProductName { get; set; } = "Diva AI";

    /// <summary>Lowercase slug — localStorage key prefix and platform API key prefix. No spaces.</summary>
    public string Slug { get; set; } = "diva";

    public string ApiAudience { get; set; } = "diva-api";

    public string LocalIssuer { get; set; } = "diva-local";
}
