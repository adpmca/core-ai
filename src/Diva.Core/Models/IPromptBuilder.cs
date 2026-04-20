namespace Diva.Core.Models;

/// <summary>
/// Builds a system prompt for an agent, injecting tenant-specific business rules.
/// Defined in Diva.Core so Diva.Infrastructure.AnthropicAgentRunner can depend on it
/// without a circular reference to Diva.TenantAdmin.
///
/// Implemented by TenantAwarePromptBuilder in Diva.TenantAdmin.
/// Returns <paramref name="baseSystemPrompt"/> unchanged when no implementation is registered.
/// </summary>
public interface IPromptBuilder
{
    /// <summary>
    /// Returns the full system prompt augmented with tenant business rules and session rules.
    /// Variable placeholders (e.g. {{current_date}}, {{company_name}}) in the assembled prompt
    /// are resolved before returning.
    /// </summary>
    Task<string> BuildAsync(
        string baseSystemPrompt,
        string agentType,
        TenantContext tenant,
        CancellationToken ct,
        string? customVariablesJson = null,
        string? agentId = null);

    /// <summary>
    /// Returns the system prompt split into a stable part and a volatile part for Anthropic
    /// prompt caching.
    /// <list type="bullet">
    ///   <item><description><b>StaticPart</b> — base prompt + group/tenant overrides + group rules.
    ///     Identical for every session of the same agent+tenant configuration.
    ///     Mark with <c>cache_control: ephemeral</c> (BP1) for cross-session cache hits.</description></item>
    ///   <item><description><b>DynamicPart</b> — session rules only. Changes per session.
    ///     Send without cache_control so the cache key stays stable.</description></item>
    /// </list>
    /// Default implementation calls <see cref="BuildAsync"/> and places the entire result in
    /// <c>StaticPart</c> with an empty <c>DynamicPart</c>. This is correct (the full prompt is
    /// still cached as BP1) but less granular than a proper split.
    /// Override in <c>TenantAwarePromptBuilder</c> for a true static/dynamic split.
    /// <para>
    /// <b>NSubstitute note:</b> NSubstitute does not call default interface implementations.
    /// Test mocks must explicitly set up this method to return a non-null tuple.
    /// </para>
    /// </summary>
    virtual async Task<(string StaticPart, string DynamicPart)> BuildPartsAsync(
        string baseSystemPrompt,
        string agentType,
        TenantContext tenant,
        CancellationToken ct,
        string? customVariablesJson = null,
        string? agentId = null)
    {
        var combined = await BuildAsync(baseSystemPrompt, agentType, tenant, ct,
                                        customVariablesJson, agentId);
        return (combined, string.Empty);
    }
}
