namespace Diva.Core.Configuration;

/// <summary>
/// Per-agent model switching config stored as JSON in AgentDefinitionEntity.ModelSwitchingJson.
///
/// Use the LlmConfigId fields (preferred) to reference a full TenantLlmConfigEntity or
/// GroupLlmConfigEntity — these carry provider, API key, and endpoint and are resolved via
/// ILlmConfigResolver, supporting cross-provider and cross-endpoint switches.
///
/// Use the Model string fields (shorthand) for same-provider, same-endpoint model-only switches.
/// Model ID strings should be present in the agent's ResolvedLlmConfig.AvailableModels list.
/// When both are set for the same phase, LlmConfigId takes precedence.
/// </summary>
public sealed class ModelSwitchingOptions
{
    // ── Full config switch (any provider / endpoint) ──────────────────────────
    /// <summary>LlmConfigId for tool-calling iterations (cheaper/faster model).</summary>
    public int? ToolIterationLlmConfigId { get; set; }

    /// <summary>LlmConfigId for the final response iteration (quality model).</summary>
    public int? FinalResponseLlmConfigId { get; set; }

    /// <summary>LlmConfigId for adaptive re-planning calls (short structural prompt).</summary>
    public int? ReplanLlmConfigId { get; set; }

    /// <summary>LlmConfigId to escalate to after repeated consecutive failures.</summary>
    public int? UpgradeOnFailuresLlmConfigId { get; set; }

    // ── Same-provider model-only switch (convenience shorthand) ───────────────
    /// <summary>Model ID for tool-calling iterations.</summary>
    public string? ToolIterationModel { get; set; }

    /// <summary>Model ID for the final response iteration.</summary>
    public string? FinalResponseModel { get; set; }

    /// <summary>Model ID for adaptive re-planning calls.</summary>
    public string? ReplanModel { get; set; }

    /// <summary>Model ID to escalate to after repeated consecutive failures.</summary>
    public string? UpgradeOnFailuresModel { get; set; }

    /// <summary>Consecutive failure count before upgrading model. Default 2.</summary>
    public int UpgradeAfterFailures { get; set; } = 2;

    /// <summary>
    /// If true (default), a failed API call on a switched model restores the original
    /// model/strategy for the remainder of the session.
    /// Set false to let normal OnError hook recovery handle the failure instead.
    /// </summary>
    public bool FallbackToOriginalOnError { get; set; } = true;
}
