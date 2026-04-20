using System.Text.RegularExpressions;
using Diva.Infrastructure.Data.Entities;

namespace Diva.TenantAdmin.Services;

/// <summary>
/// Analyzes Rule Pack rules for potential conflicts and redundancies.
/// Runs at save-time to warn admins about problematic rule combinations.
/// </summary>
public sealed class RulePackConflictAnalyzer
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Analyze a single pack's rules for internal conflicts.
    /// </summary>
    public List<ConflictWarning> AnalyzePack(HookRulePackEntity pack)
    {
        var warnings = new List<ConflictWarning>();
        var rules = pack.Rules.Where(r => r.IsEnabled).OrderBy(r => r.OrderInPack).ToList();

        AnalyzeInjectBlockConflicts(rules, warnings);
        AnalyzeDuplicateFormats(rules, warnings);
        AnalyzeRedundantRedactions(rules, warnings);
        AnalyzeKeywordAppendConflicts(rules, warnings);
        AnalyzeRegexComplexity(rules, warnings);

        return warnings;
    }

    /// <summary>
    /// Analyze conflicts across multiple packs (cross-pack conflicts).
    /// </summary>
    public List<ConflictWarning> AnalyzeCrossPack(List<HookRulePackEntity> packs)
    {
        var warnings = new List<ConflictWarning>();
        var allRules = packs
            .Where(p => p.IsEnabled)
            .OrderBy(p => p.Priority)
            .SelectMany(p => p.Rules.Where(r => r.IsEnabled).Select(r => (Pack: p, Rule: r)))
            .ToList();

        // Check for inject_prompt keywords overlapping with block_pattern
        var injectRules = allRules.Where(x => x.Rule.RuleType == "inject_prompt").ToList();
        var blockRules = allRules.Where(x => x.Rule.RuleType == "block_pattern").ToList();

        foreach (var inject in injectRules)
        {
            if (string.IsNullOrWhiteSpace(inject.Rule.Instruction)) continue;

            foreach (var block in blockRules)
            {
                if (string.IsNullOrWhiteSpace(block.Rule.Pattern)) continue;

                try
                {
                    var regex = new Regex(block.Rule.Pattern, RegexOptions.IgnoreCase, RegexTimeout);
                    if (regex.IsMatch(inject.Rule.Instruction))
                    {
                        warnings.Add(new ConflictWarning(
                            ConflictSeverity.Warning,
                            $"Pack '{inject.Pack.Name}' injects text that would be blocked by pack '{block.Pack.Name}' (block_pattern rule #{RuleLabel(block.Rule.Id)} matches inject_prompt rule #{RuleLabel(inject.Rule.Id)}))"));
                    }
                }
                catch { /* skip if regex is invalid */ }
            }
        }

        // Check for multiple format_response rules across packs (only last one wins)
        var formatRules = allRules.Where(x => x.Rule.RuleType == "format_response").ToList();
        if (formatRules.Count > 1)
        {
            warnings.Add(new ConflictWarning(
                ConflictSeverity.Warning,
                $"Multiple format_response rules across {formatRules.Select(f => f.Pack.Name).Distinct().Count()} packs — only the last pack's format instruction takes full effect. Consider consolidating into one pack."));
        }

        // Check for conflicting tool_require targeting same trigger pattern
        var toolRequireRules = allRules.Where(x => x.Rule.RuleType == "tool_require").ToList();
        var groupedByPattern = toolRequireRules
            .Where(x => !string.IsNullOrWhiteSpace(x.Rule.Pattern))
            .GroupBy(x => x.Rule.Pattern, StringComparer.OrdinalIgnoreCase);
        foreach (var group in groupedByPattern.Where(g => g.Count() > 1))
        {
            var tools = group.Select(g => g.Rule.ToolName).Distinct().ToList();
            if (tools.Count > 1)
            {
                warnings.Add(new ConflictWarning(
                    ConflictSeverity.Info,
                    $"Multiple tool_require rules with same trigger pattern require different tools ({string.Join(", ", tools)}) — both will be injected."));
            }
        }

        return warnings;
    }

    // ── Internal analyzers ────────────────────────────────────────────────────

    private static void AnalyzeInjectBlockConflicts(List<HookRuleEntity> rules, List<ConflictWarning> warnings)
    {
        var injects = rules.Where(r => r.RuleType == "inject_prompt").ToList();
        var blocks = rules.Where(r => r.RuleType == "block_pattern").ToList();

        foreach (var inject in injects)
        {
            if (string.IsNullOrWhiteSpace(inject.Instruction)) continue;

            foreach (var block in blocks)
            {
                if (string.IsNullOrWhiteSpace(block.Pattern)) continue;

                try
                {
                    var regex = new Regex(block.Pattern, RegexOptions.IgnoreCase, RegexTimeout);
                    if (regex.IsMatch(inject.Instruction))
                    {
                        warnings.Add(new ConflictWarning(
                            ConflictSeverity.Warning,
                            $"inject_prompt (rule #{inject.OrderInPack}) contains text that matches block_pattern (rule #{block.OrderInPack}) — the response may be blocked after the LLM follows the injected instruction."));
                    }
                }
                catch { /* skip invalid regex */ }
            }
        }
    }

    private static void AnalyzeDuplicateFormats(List<HookRuleEntity> rules, List<ConflictWarning> warnings)
    {
        var formatRules = rules.Where(r => r.RuleType is "format_response" or "format_enforce").ToList();
        if (formatRules.Count > 1)
        {
            warnings.Add(new ConflictWarning(
                ConflictSeverity.Warning,
                $"Multiple format rules ({formatRules.Count}) in the same pack — the LLM may receive conflicting format instructions. Consider keeping only one."));
        }
    }

    private static void AnalyzeRedundantRedactions(List<HookRuleEntity> rules, List<ConflictWarning> warnings)
    {
        var redactRules = rules.Where(r => r.RuleType == "regex_redact" && !string.IsNullOrWhiteSpace(r.Pattern)).ToList();
        for (var i = 0; i < redactRules.Count; i++)
        {
            for (var j = i + 1; j < redactRules.Count; j++)
            {
                if (string.Equals(redactRules[i].Pattern, redactRules[j].Pattern, StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add(new ConflictWarning(
                        ConflictSeverity.Info,
                        $"Duplicate regex_redact patterns (rules #{redactRules[i].OrderInPack} and #{redactRules[j].OrderInPack}) — redundant, not harmful."));
                }
            }
        }
    }

    private static void AnalyzeKeywordAppendConflicts(List<HookRuleEntity> rules, List<ConflictWarning> warnings)
    {
        var keywords = rules.Where(r => r.RuleType == "require_keyword").ToList();
        var blocks = rules.Where(r => r.RuleType == "block_pattern").ToList();

        foreach (var kw in keywords)
        {
            if (string.IsNullOrWhiteSpace(kw.Pattern)) continue;

            foreach (var block in blocks)
            {
                if (string.IsNullOrWhiteSpace(block.Pattern)) continue;

                try
                {
                    var regex = new Regex(block.Pattern, RegexOptions.IgnoreCase, RegexTimeout);
                    if (regex.IsMatch(kw.Pattern))
                    {
                        warnings.Add(new ConflictWarning(
                            ConflictSeverity.Error,
                            $"require_keyword '{kw.Pattern}' (rule #{kw.OrderInPack}) would be blocked by block_pattern (rule #{block.OrderInPack}) — the appended keyword will trigger the block."));
                    }
                }
                catch { /* skip invalid regex */ }
            }
        }
    }

    private static void AnalyzeRegexComplexity(List<HookRuleEntity> rules, List<ConflictWarning> warnings)
    {
        foreach (var rule in rules.Where(r => !string.IsNullOrWhiteSpace(r.Pattern)))
        {
            var pattern = rule.Pattern!;

            // Check for common ReDoS patterns: nested quantifiers
            if (Regex.IsMatch(pattern, @"\([^)]*[+*][^)]*\)[+*]"))
            {
                warnings.Add(new ConflictWarning(
                    ConflictSeverity.Error,
                    $"Rule #{rule.OrderInPack} ({rule.RuleType}) has a potentially dangerous regex with nested quantifiers — risk of ReDoS. Simplify the pattern."));
            }

            // Check for very long patterns (>500 chars)
            if (pattern.Length > 500)
            {
                warnings.Add(new ConflictWarning(
                    ConflictSeverity.Warning,
                    $"Rule #{rule.OrderInPack} ({rule.RuleType}) has a very long regex pattern ({pattern.Length} chars) — may impact performance."));
            }
        }
    }

    /// <summary>
    /// Returns a human-readable label for a rule ID.
    /// Negative IDs are virtual business rules (Id &lt; 0 → BusinessRule #{abs(id)}).
    /// </summary>
    private static string RuleLabel(int id) =>
        id < 0 ? $"BusinessRule #{Math.Abs(id)}" : id.ToString();
}

public enum ConflictSeverity { Info, Warning, Error }

public record ConflictWarning(ConflictSeverity Severity, string Message);
