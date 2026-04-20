namespace Diva.Core.Models;

/// <summary>
/// Base interface for all agent lifecycle hooks.
/// Hooks are resolved per-request via DI or constructed from JSON config.
/// </summary>
public interface IAgentLifecycleHook
{
    /// <summary>Execution order. Lower = earlier. Default = 100.</summary>
    int Order => 100;
}

/// <summary>Called once when agent execution starts, before the ReAct loop.</summary>
public interface IOnInitHook : IAgentLifecycleHook
{
    Task OnInitAsync(AgentHookContext context, CancellationToken ct);
}

/// <summary>Called at the start of each ReAct iteration.</summary>
public interface IOnBeforeIterationHook : IAgentLifecycleHook
{
    Task OnBeforeIterationAsync(AgentHookContext context, int iteration, CancellationToken ct);
}

/// <summary>Called after LLM returns tool calls, before execution. Can filter/modify tool list.</summary>
public interface IOnToolFilterHook : IAgentLifecycleHook
{
    Task<List<UnifiedToolCallRef>> OnToolFilterAsync(
        AgentHookContext context, List<UnifiedToolCallRef> toolCalls, CancellationToken ct);
}

/// <summary>Called after each tool call completes.</summary>
public interface IOnAfterToolCallHook : IAgentLifecycleHook
{
    Task<string> OnAfterToolCallAsync(
        AgentHookContext context, string toolName, string toolOutput, bool isError, CancellationToken ct);
}

/// <summary>Called after the ReAct loop produces a final text response, before verification.</summary>
public interface IOnBeforeResponseHook : IAgentLifecycleHook
{
    Task<string> OnBeforeResponseAsync(AgentHookContext context, string responseText, CancellationToken ct);
}

/// <summary>Called after verification, before returning to caller. Last chance for side effects.</summary>
public interface IOnAfterResponseHook : IAgentLifecycleHook
{
    Task OnAfterResponseAsync(AgentHookContext context, AgentResponse response, CancellationToken ct);
}

/// <summary>
/// Called when a tool call or LLM call fails. Allows custom error recovery.
/// </summary>
public interface IOnErrorHook : IAgentLifecycleHook
{
    Task<ErrorRecoveryAction> OnErrorAsync(
        AgentHookContext context, string? toolName, Exception exception, CancellationToken ct);
}

/// <summary>Instructs the ReAct loop how to proceed after an error hook runs.</summary>
public enum ErrorRecoveryAction
{
    /// <summary>Log and continue the iteration loop normally (default runner behavior).</summary>
    Continue,
    /// <summary>Retry the same tool call once.</summary>
    Retry,
    /// <summary>Abort the ReAct loop immediately and return an error response.</summary>
    Abort,
}

/// <summary>Mutable context bag passed through all hooks for a single agent execution.</summary>
public sealed class AgentHookContext
{
    public AgentRequest Request { get; init; } = null!;
    public TenantContext Tenant { get; init; } = null!;
    public string AgentId { get; init; } = "";
    public string ArchetypeId { get; init; } = "";
    public string SessionId { get; set; } = "";

    /// <summary>Hook-specific state. Hooks can store/retrieve arbitrary data here.</summary>
    public Dictionary<string, object?> State { get; } = [];

    /// <summary>System prompt that will be sent to the LLM. Hooks can modify this.</summary>
    public string SystemPrompt { get; set; } = "";

    /// <summary>Custom variables resolved from archetype + agent config.</summary>
    public Dictionary<string, string> Variables { get; init; } = [];

    /// <summary>Accumulated tool evidence (read-only snapshot for hooks).</summary>
    public string ToolEvidence { get; set; } = "";

    /// <summary>Current iteration number (updated by runner before each hook call).</summary>
    public int CurrentIteration { get; set; }

    /// <summary>Number of consecutive tool failures (read from runner state).</summary>
    public int ConsecutiveFailures { get; set; }

    /// <summary>
    /// Set to true when the previous LLM response was cut off due to max_tokens.
    /// OnBeforeIteration hooks can read this to inject conciseness instructions
    /// into <see cref="SystemPrompt"/> before the next call.
    /// </summary>
    public bool WasTruncated { get; set; }

    // ── Per-iteration model switching ─────────────────────────────────────────

    /// <summary>
    /// Full LlmConfig switch resolved by the runner via ILlmConfigResolver.
    /// Supports cross-provider and cross-endpoint switches.
    /// Takes precedence over <see cref="ModelOverride"/> when both are set.
    /// Set by OnBeforeIteration hooks; cleared by the runner after applying.
    /// </summary>
    public int? LlmConfigIdOverride { get; set; }

    /// <summary>
    /// Same-provider, same-endpoint model-only switch.
    /// Ignored when <see cref="LlmConfigIdOverride"/> is set.
    /// Set by OnBeforeIteration hooks; cleared by the runner after applying.
    /// </summary>
    public string? ModelOverride { get; set; }

    /// <summary>
    /// Optional max_tokens for <see cref="ModelOverride"/>.
    /// Ignored for <see cref="LlmConfigIdOverride"/> (the resolved config provides it).
    /// </summary>
    public int? MaxTokensOverride { get; set; }

    /// <summary>
    /// Optional API key for <see cref="ModelOverride"/> same-provider switches.
    /// Use <see cref="LlmConfigIdOverride"/> instead for cross-provider switches.
    /// </summary>
    public string? ApiKeyOverride { get; set; }

    // ── Runner-internal iteration state (written/read by runner; hooks may observe) ──

    /// <summary>True when this is the last iteration (no further tool calls expected).</summary>
    public bool IsFinalIteration { get; set; }

    /// <summary>True when the previous iteration contained at least one tool call.</summary>
    public bool LastHadToolCalls { get; set; }

    /// <summary>
    /// Text output from the most recent iteration that produced tool calls.
    /// Set by the runner after each non-final iteration. Empty on iteration 1.
    /// OnBeforeIteration hooks can read this to trigger model switches based on
    /// what the agent said in the previous step.
    /// </summary>
    public string LastIterationResponse { get; set; } = "";

    // ── Hook → runner communication (hooks SET; runner reads then clears) ─────

    /// <summary>
    /// Human-readable reason for why a model switch was requested.
    /// Set by hooks before returning from <c>OnBeforeIterationAsync</c>.
    /// Cleared by the runner after emitting the <c>model_switch</c> chunk.
    /// </summary>
    public string? ModelSwitchReason { get; set; }

    /// <summary>
    /// LlmConfigId for the replan LLM call after consecutive tool failures.
    /// Takes precedence over <see cref="ReplanModel"/>. Set by hooks; cleared by runner.
    /// </summary>
    public int? ReplanConfigId { get; set; }

    /// <summary>
    /// Model name for the replan LLM call after consecutive tool failures.
    /// Used only when <see cref="ReplanConfigId"/> is null. Set by hooks; cleared by runner.
    /// </summary>
    public string? ReplanModel { get; set; }

    // ── Rule-pack hook results (written by rule-pack hooks; read by runner for SSE chunks) ──

    /// <summary>Number of rule-pack rules that fired in the last hook call.</summary>
    public int? RulePackLastTriggeredCount { get; set; }

    /// <summary>Names of rules that fired in the last hook call.</summary>
    public string[]? RulePackLastTriggeredRules { get; set; }

    /// <summary>Number of tool calls filtered by rule-pack policy in the last hook call.</summary>
    public int? RulePackLastFilteredCount { get; set; }

    /// <summary>Error-recovery action recommended by rule-pack policy in the last hook call.</summary>
    public string? RulePackLastErrorAction { get; set; }

    /// <summary>True when rule-pack policy blocked the response in the last hook call.</summary>
    public bool? RulePackLastBlocked { get; set; }
}

/// <summary>Lightweight reference to a tool call (for filter hooks).</summary>
public sealed class UnifiedToolCallRef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string InputJson { get; init; } = "";
    public bool Filtered { get; set; }
}
