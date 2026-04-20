namespace Diva.TenantAdmin.Services;

/// <summary>
/// Canonical compatibility matrix between Rule Pack hook points and rule types.
/// Used by backend validation and mirrored by the admin UI.
/// </summary>
public static class RulePackRuleCompatibility
{
    private static readonly Dictionary<string, HashSet<string>> Matrix =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["OnInit"] =
            [
                "inject_prompt",
                "tool_require",
                "format_response",
                "tool_transform",
            ],
            ["OnBeforeIteration"] =
            [
                "inject_prompt",
                "tool_require",
                "format_response",
                "tool_transform",
                "format_enforce",
                "model_switch",
            ],
            ["OnToolFilter"] =
            [
                "block_pattern",
                "tool_transform",
            ],
            ["OnAfterToolCall"] =
            [
                "regex_redact",
                "append_text",
                "block_pattern",
                "require_keyword",
                "format_enforce",
            ],
            ["OnBeforeResponse"] =
            [
                "regex_redact",
                "append_text",
                "block_pattern",
                "require_keyword",
                "format_enforce",
                "format_response",
            ],
            ["OnAfterResponse"] =
            [
                "append_text",
                "require_keyword",
                "format_response",
                "format_enforce",
            ],
            ["OnError"] =
            [
                "block_pattern",
                "tool_require",
            ],
        };

    public static IReadOnlyDictionary<string, HashSet<string>> Allowed => Matrix;

    public static bool IsValid(string hookPoint, string ruleType)
    {
        if (string.IsNullOrWhiteSpace(hookPoint) || string.IsNullOrWhiteSpace(ruleType))
            return false;

        return Matrix.TryGetValue(hookPoint, out var ruleTypes)
               && ruleTypes.Contains(ruleType);
    }

    public static void ValidateOrThrow(string hookPoint, string ruleType)
    {
        if (IsValid(hookPoint, ruleType))
            return;

        var validHookPoints = string.Join(", ", Matrix.Keys.OrderBy(k => k));
        var validRuleTypes = Matrix.TryGetValue(hookPoint, out var types)
            ? string.Join(", ", types.OrderBy(t => t))
            : "(unknown hook point)";

        throw new InvalidOperationException(
            $"Unsupported rule configuration: hookPoint='{hookPoint}', ruleType='{ruleType}'. " +
            $"Valid hook points: {validHookPoints}. " +
            $"Valid rule types for '{hookPoint}': {validRuleTypes}.");
    }

    /// <summary>
    /// Returns a Markdown table of the full hook × rule-type compatibility matrix.
    /// Injected into LLM prompts by AgentSetupAssistant so the model always sees the live matrix.
    /// </summary>
    /// <summary>
    /// Validates a business rule's HookPoint + HookRuleType combination using the same matrix.
    /// Returns (valid=true, allowedTypes) on success; (valid=false, allowedTypes) when invalid.
    /// </summary>
    public static (bool Valid, string[] AllowedTypes) ValidateBusinessRule(string hookPoint, string hookRuleType)
    {
        var allowed = Matrix.TryGetValue(hookPoint, out var types)
            ? types.OrderBy(t => t).ToArray()
            : [];

        var valid = allowed.Length > 0
            && allowed.Contains(hookRuleType, StringComparer.OrdinalIgnoreCase);

        return (valid, allowed);
    }

    public static string AsMarkdownTable()
    {
        var allRuleTypes = Matrix.Values
            .SelectMany(s => s)
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        var hookPoints = Matrix.Keys.OrderBy(k => k).ToList();

        var header = "| Hook Point | " + string.Join(" | ", allRuleTypes) + " |";
        var separator = "|---|" + string.Join("|", allRuleTypes.Select(_ => "---")) + "|";

        var rows = hookPoints.Select(hp =>
        {
            var cells = allRuleTypes.Select(rt =>
                Matrix.TryGetValue(hp, out var allowed) && allowed.Contains(rt) ? "✓" : "");
            return $"| {hp} | " + string.Join(" | ", cells) + " |";
        });

        return string.Join("\n", new[] { header, separator }.Concat(rows));
    }
}
