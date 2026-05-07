namespace Diva.Core.Models;

/// <summary>
/// Resolved LLM provider context for supervisor-level calls (decompose, synthesis).
/// Set by OrchestratorAgent (Phase 19) from the coordinator agent's resolved LlmConfig.
/// Null on SupervisorState = use global platform defaults from IOptions&lt;LlmOptions&gt;.
/// API keys are never stored here — resolved by the provider at call time from the vault.
/// </summary>
public sealed record SupervisorLlmOverride(
    string Provider,          // "Anthropic" | "OpenAI"
    string Model,
    string? Endpoint = null); // non-null for LiteLLM proxy or LM Studio
