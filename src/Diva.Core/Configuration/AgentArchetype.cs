namespace Diva.Core.Configuration;

/// <summary>
/// Defines an agent archetype — a reusable behavioral template with
/// pre-configured hooks, tools, and defaults.
/// </summary>
public sealed class AgentArchetype
{
    /// <summary>Unique archetype ID (e.g. "rag", "code-analyst", "data-analyst").</summary>
    public string Id { get; init; } = "";

    /// <summary>Human-readable name shown in Agent Builder UI.</summary>
    public string DisplayName { get; init; } = "";

    /// <summary>Description of what this archetype does.</summary>
    public string Description { get; init; } = "";

    /// <summary>Icon identifier for the UI (e.g. "database", "code", "search", "brain").</summary>
    public string Icon { get; init; } = "bot";

    /// <summary>Category for grouping in the archetype gallery.</summary>
    public string Category { get; init; } = "General";

    /// <summary>Default system prompt template. Supports {{variable}} placeholders.</summary>
    public string SystemPromptTemplate { get; init; } = "";

    /// <summary>Default capabilities assigned to agents created from this archetype.</summary>
    public string[] DefaultCapabilities { get; init; } = [];

    /// <summary>Suggested MCP tool server names.</summary>
    public string[] SuggestedTools { get; init; } = [];

    /// <summary>Default hook configuration (JSON key → hook class name or inline config).</summary>
    public Dictionary<string, string> DefaultHooks { get; init; } = [];

    /// <summary>Default temperature.</summary>
    public double DefaultTemperature { get; init; } = 0.7;

    /// <summary>Default max iterations.</summary>
    public int DefaultMaxIterations { get; init; } = 10;

    /// <summary>Default verification mode.</summary>
    public string? DefaultVerificationMode { get; init; }

    /// <summary>Recommended pipeline stage overrides.</summary>
    public Dictionary<string, bool>? PipelineStageDefaults { get; init; }

    /// <summary>Default execution mode for agents created from this archetype.</summary>
    public AgentExecutionMode DefaultExecutionMode { get; init; } = AgentExecutionMode.Full;
}

/// <summary>
/// Controls what an agent is allowed to do at runtime.
/// Enforced at the runner's tool-loading choke point (same location as ToolFilterJson).
/// </summary>
public enum AgentExecutionMode
{
    /// <summary>Full ReAct loop — LLM can plan, call tools, iterate, verify (default).</summary>
    Full,

    /// <summary>Chat only — all tools removed before LLM sees them. LLM can only reason and respond.</summary>
    ChatOnly,

    /// <summary>Read-only — only tools with ToolAccessLevel.ReadOnly are loaded.</summary>
    ReadOnly,

    /// <summary>Supervised — tool calls require human approval via SignalR before execution (future).</summary>
    Supervised,
}

/// <summary>
/// Classifies an MCP tool binding's access level for ExecutionMode enforcement.
/// Tagged per-binding in Agent Builder UI. Auto-suggested from binding name.
/// </summary>
public enum ToolAccessLevel
{
    /// <summary>Search, lookup, list, get, read — safe in ReadOnly mode.</summary>
    ReadOnly,

    /// <summary>Create, update, execute — blocked in ReadOnly mode (default).</summary>
    ReadWrite,

    /// <summary>Delete, drop, purge — blocked in ReadOnly mode.</summary>
    Destructive,
}
