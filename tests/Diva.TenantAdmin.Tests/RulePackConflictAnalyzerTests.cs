using Diva.Infrastructure.Data.Entities;
using Diva.TenantAdmin.Services;

namespace Diva.TenantAdmin.Tests;

/// <summary>
/// Tests for RulePackConflictAnalyzer — internal and cross-pack conflict detection.
/// Pure unit tests (no DB needed — analyzer works on entity objects directly).
/// </summary>
public class RulePackConflictAnalyzerTests
{
    private readonly RulePackConflictAnalyzer _analyzer = new();

    // ── Helper ────────────────────────────────────────────────────────────────

    private static HookRulePackEntity Pack(string name, params HookRuleEntity[] rules)
    {
        var pack = new HookRulePackEntity
        {
            Id = 1, TenantId = 1, Name = name, Version = "1.0",
            Priority = 100, IsEnabled = true, MaxEvaluationMs = 500
        };
        foreach (var r in rules) pack.Rules.Add(r);
        return pack;
    }

    private static HookRuleEntity Rule(string ruleType, int order,
        string? pattern = null, string? instruction = null, string? replacement = null,
        string? toolName = null) =>
        new()
        {
            Id = order,
            HookPoint = ruleType is "inject_prompt" or "tool_require" or "format_response" or "tool_transform"
                ? "OnInit" : "OnBeforeResponse",
            RuleType = ruleType,
            Pattern = pattern,
            Instruction = instruction,
            Replacement = replacement,
            ToolName = toolName,
            OrderInPack = order,
            IsEnabled = true,
            MaxEvaluationMs = 100,
        };

    // ── Internal conflicts ────────────────────────────────────────────────────

    [Fact]
    public void AnalyzePack_InjectBlockConflict_ReturnsWarning()
    {
        var pack = Pack("test",
            Rule("inject_prompt", 1, instruction: "Always mention revenue targets"),
            Rule("block_pattern", 2, pattern: "(?i)revenue"));

        var warnings = _analyzer.AnalyzePack(pack);

        Assert.Contains(warnings, w => w.Severity == ConflictSeverity.Warning && w.Message.Contains("inject_prompt"));
    }

    [Fact]
    public void AnalyzePack_NoConflicts_ReturnsEmpty()
    {
        var pack = Pack("clean",
            Rule("regex_redact", 1, pattern: @"\b\d{9}\b", replacement: "[SSN]"),
            Rule("append_text", 2, instruction: "Disclaimer"));

        var warnings = _analyzer.AnalyzePack(pack);

        Assert.Empty(warnings);
    }

    [Fact]
    public void AnalyzePack_DuplicateFormatRules_ReturnsWarning()
    {
        var pack = Pack("formats",
            Rule("format_response", 1, instruction: "Use tables"),
            Rule("format_enforce", 2, pattern: @"^\|"));

        var warnings = _analyzer.AnalyzePack(pack);

        Assert.Contains(warnings, w => w.Severity == ConflictSeverity.Warning && w.Message.Contains("format"));
    }

    [Fact]
    public void AnalyzePack_RedundantRedactions_ReturnsInfoWarning()
    {
        var pack = Pack("dupes",
            Rule("regex_redact", 1, pattern: @"\b\d{9}\b", replacement: "[SSN]"),
            Rule("regex_redact", 2, pattern: @"\b\d{9}\b", replacement: "[REDACTED]"));

        var warnings = _analyzer.AnalyzePack(pack);

        Assert.Contains(warnings, w => w.Severity == ConflictSeverity.Info && w.Message.Contains("Duplicate"));
    }

    [Fact]
    public void AnalyzePack_KeywordBlockConflict_ReturnsError()
    {
        var pack = Pack("kw-block",
            Rule("require_keyword", 1, pattern: "compliance"),
            Rule("block_pattern", 2, pattern: "(?i)compliance"));

        var warnings = _analyzer.AnalyzePack(pack);

        Assert.Contains(warnings, w => w.Severity == ConflictSeverity.Error && w.Message.Contains("require_keyword"));
    }

    [Fact]
    public void AnalyzePack_ReDoSPattern_ReturnsError()
    {
        var pack = Pack("redos",
            Rule("regex_redact", 1, pattern: @"(a+)+b"));

        var warnings = _analyzer.AnalyzePack(pack);

        Assert.Contains(warnings, w => w.Severity == ConflictSeverity.Error && w.Message.Contains("ReDoS"));
    }

    [Fact]
    public void AnalyzePack_LongPattern_ReturnsWarning()
    {
        var longPattern = new string('a', 501);
        var pack = Pack("long",
            Rule("regex_redact", 1, pattern: longPattern));

        var warnings = _analyzer.AnalyzePack(pack);

        Assert.Contains(warnings, w => w.Severity == ConflictSeverity.Warning && w.Message.Contains("long"));
    }

    // ── Cross-pack conflicts ──────────────────────────────────────────────────

    [Fact]
    public void AnalyzeCrossPack_InjectBlockAcrossPacks_ReturnsWarning()
    {
        var inject = Pack("Inject Pack",
            Rule("inject_prompt", 1, instruction: "Always provide revenue data"));
        inject.Id = 1;

        var block = Pack("Block Pack",
            Rule("block_pattern", 1, pattern: "(?i)revenue"));
        block.Id = 2;

        var warnings = _analyzer.AnalyzeCrossPack([inject, block]);

        Assert.Contains(warnings, w => w.Severity == ConflictSeverity.Warning && w.Message.Contains("blocked"));
    }

    [Fact]
    public void AnalyzeCrossPack_MultipleFormatRulesAcrossPacks_ReturnsWarning()
    {
        var pack1 = Pack("Format-1", Rule("format_response", 1, instruction: "Use JSON"));
        pack1.Id = 1;

        var pack2 = Pack("Format-2", Rule("format_response", 1, instruction: "Use tables"));
        pack2.Id = 2;

        var warnings = _analyzer.AnalyzeCrossPack([pack1, pack2]);

        Assert.Contains(warnings, w => w.Message.Contains("format_response"));
    }

    [Fact]
    public void AnalyzeCrossPack_ConflictingToolRequire_ReturnsInfo()
    {
        var pack1 = Pack("Tool1", Rule("tool_require", 1, pattern: "(?i)weather", toolName: "weather_v1"));
        pack1.Id = 1;

        var pack2 = Pack("Tool2", Rule("tool_require", 1, pattern: "(?i)weather", toolName: "weather_v2"));
        pack2.Id = 2;

        var warnings = _analyzer.AnalyzeCrossPack([pack1, pack2]);

        Assert.Contains(warnings, w => w.Severity == ConflictSeverity.Info && w.Message.Contains("tool_require"));
    }

    [Fact]
    public void AnalyzeCrossPack_NoConflicts_ReturnsEmpty()
    {
        var pack1 = Pack("Redact", Rule("regex_redact", 1, pattern: @"\d{9}", replacement: "[X]"));
        pack1.Id = 1;

        var pack2 = Pack("Append", Rule("append_text", 1, instruction: "Disclaimer"));
        pack2.Id = 2;

        var warnings = _analyzer.AnalyzeCrossPack([pack1, pack2]);

        Assert.Empty(warnings);
    }

    [Fact]
    public void AnalyzeCrossPack_DisabledPacksAreExcluded()
    {
        var enabled = Pack("Enabled", Rule("format_response", 1, instruction: "Use JSON"));
        enabled.Id = 1;

        var disabled = Pack("Disabled", Rule("format_response", 1, instruction: "Use tables"));
        disabled.Id = 2;
        disabled.IsEnabled = false;

        var warnings = _analyzer.AnalyzeCrossPack([enabled, disabled]);

        // Only one enabled pack with format_response — no "multiple format" warning
        Assert.DoesNotContain(warnings, w => w.Message.Contains("format_response"));
    }
}
