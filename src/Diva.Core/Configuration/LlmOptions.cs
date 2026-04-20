namespace Diva.Core.Configuration;

public sealed class LlmOptions
{
    public const string SectionName = "LLM";

    public DirectProviderOptions DirectProvider { get; set; } = new();
    public List<string> AvailableModels { get; set; } = [];

    /// <summary>
    /// Timeout in seconds for LLM HTTP calls. The .NET default is 100 s which is
    /// too short for large outputs or slow models. Default 600 s (10 min).
    /// </summary>
    public int HttpTimeoutSeconds { get; set; } = 600;
}

public sealed class DirectProviderOptions
{
    /// <summary>"Anthropic" | "OpenAI" | "Azure"
    /// To use LiteLLM: set Provider="OpenAI", Endpoint="http://litellm:4000/", ApiKey=&lt;master key&gt;, Model=&lt;alias&gt;
    /// </summary>
    public string Provider { get; set; } = "Anthropic";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "claude-sonnet-4-20250514";
    public string? Endpoint { get; set; }           // Azure OpenAI or LiteLLM proxy endpoint
    public string? DeploymentName { get; set; }     // Azure OpenAI deployment
}
