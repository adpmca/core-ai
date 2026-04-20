using Diva.Core.Configuration;
using Diva.Core.Models;
using Diva.Infrastructure.Data.Entities;
using Microsoft.Extensions.Options;

namespace Diva.Agents.Tests.Helpers;

internal static class AgentTestFixtures
{
    public static AgentDefinitionEntity BasicAgent(string id = "agent-1") => new()
    {
        Id          = id,
        Name        = "TestAgent",
        DisplayName = "Test Agent",
        AgentType   = "generic",
        SystemPrompt = "You are a test agent.",
        ToolBindings = "[]"
    };

    public static TenantContext BasicTenant(int id = 1) => new()
    {
        TenantId   = id,
        TenantName = "TestTenant",
        UserId     = "user-1"
    };

    public static AgentRequest BasicRequest(string query = "hi") => new()
    {
        Query     = query,
        SessionId = null
    };

    public static IOptions<T> Opts<T>(T value) where T : class
        => Options.Create(value);

    public static IOptions<VerificationOptions> OffVerification()
        => Opts(new VerificationOptions { Mode = "Off" });

    public static IOptions<VerificationOptions> AutoVerification()
        => Opts(new VerificationOptions { Mode = "Auto" });

    public static IOptions<LlmOptions> AnthropicLlm(string model = "claude-sonnet-4-20250514")
        => Opts(new LlmOptions
        {
            DirectProvider = new DirectProviderOptions
            {
                Provider = "Anthropic",
                ApiKey   = "test-key",
                Model    = model
            }
        });

    public static IOptions<LlmOptions> OpenAiLlm(string model = "gpt-4.1")
        => Opts(new LlmOptions
        {
            DirectProvider = new DirectProviderOptions
            {
                Provider = "OpenAI",
                ApiKey   = "test-key",
                Model    = model
            }
        });
}
