using Diva.TenantAdmin.Services;

namespace Diva.TenantAdmin.Tests;

/// <summary>
/// Tests for RulePackRuleCompatibility — validates the hook × rule-type matrix and AsMarkdownTable output.
/// </summary>
public class RulePackCompatibilityMatrixTests
{
    // ── Basic matrix structure ─────────────────────────────────────────────────

    [Fact]
    public void AllHookPoints_HaveAtLeastOneRuleType()
    {
        foreach (var (hookPoint, rules) in RulePackRuleCompatibility.Allowed)
        {
            Assert.True(rules.Count > 0, $"Hook point '{hookPoint}' has no valid rule types.");
        }
    }

    [Fact]
    public void AllowedMatrix_HasAtLeastSevenHookPoints()
    {
        // Regression guard — matrix must not shrink below known set
        Assert.True(RulePackRuleCompatibility.Allowed.Count >= 7,
            "Compatibility matrix has fewer hook points than expected.");
    }

    // ── Known-valid combinations ───────────────────────────────────────────────

    [Theory]
    [InlineData("OnInit",             "inject_prompt")]
    [InlineData("OnInit",             "tool_require")]
    [InlineData("OnBeforeIteration",  "model_switch")]
    [InlineData("OnBeforeIteration",  "inject_prompt")]
    [InlineData("OnAfterToolCall",    "regex_redact")]
    [InlineData("OnAfterToolCall",    "append_text")]
    [InlineData("OnBeforeResponse",   "block_pattern")]
    [InlineData("OnAfterResponse",    "format_response")]
    [InlineData("OnError",            "tool_require")]
    public void KnownValidCombination_IsPermitted(string hookPoint, string ruleType)
    {
        Assert.True(
            RulePackRuleCompatibility.IsValid(hookPoint, ruleType),
            $"Expected '{hookPoint}' + '{ruleType}' to be valid, but was rejected.");
    }

    // ── Known-invalid combinations ─────────────────────────────────────────────

    [Theory]
    [InlineData("OnToolFilter",       "inject_prompt")]   // inject_prompt not valid here
    [InlineData("OnToolFilter",       "model_switch")]    // model_switch unknown to OnToolFilter
    [InlineData("OnInit",             "regex_redact")]    // regex_redact not on OnInit
    [InlineData("OnAfterResponse",    "tool_require")]    // tool_require not on OnAfterResponse
    [InlineData("OnError",            "format_response")] // format_response not on OnError
    public void KnownInvalidCombination_IsRejected(string hookPoint, string ruleType)
    {
        Assert.False(
            RulePackRuleCompatibility.IsValid(hookPoint, ruleType),
            $"Expected '{hookPoint}' + '{ruleType}' to be invalid, but was accepted.");
    }

    // ── Edge cases ────────────────────────────────────────────────────────────

    [Fact]
    public void IsValid_NullHookPoint_ReturnsFalse()
    {
        Assert.False(RulePackRuleCompatibility.IsValid(null!, "inject_prompt"));
    }

    [Fact]
    public void IsValid_NullRuleType_ReturnsFalse()
    {
        Assert.False(RulePackRuleCompatibility.IsValid("OnInit", null!));
    }

    [Fact]
    public void IsValid_EmptyStrings_ReturnsFalse()
    {
        Assert.False(RulePackRuleCompatibility.IsValid("", ""));
    }

    [Fact]
    public void IsValid_IsCaseInsensitive()
    {
        // Dictionary uses OrdinalIgnoreCase — hookPoint lookup is case-insensitive
        Assert.True(RulePackRuleCompatibility.IsValid("oninit", "inject_prompt"));
        // Rule type values are lowercase; case-insensitive hookPoint + exact ruleType
        Assert.True(RulePackRuleCompatibility.IsValid("ONINIT", "inject_prompt"));
    }

    [Fact]
    public void ValidateOrThrow_ValidCombination_DoesNotThrow()
    {
        // Should not throw
        RulePackRuleCompatibility.ValidateOrThrow("OnInit", "inject_prompt");
    }

    [Fact]
    public void ValidateOrThrow_InvalidCombination_ThrowsInvalidOperation()
    {
        Assert.Throws<InvalidOperationException>(
            () => RulePackRuleCompatibility.ValidateOrThrow("OnInit", "regex_redact"));
    }

    [Fact]
    public void ValidateOrThrow_UnknownHookPoint_ThrowsInvalidOperation()
    {
        Assert.Throws<InvalidOperationException>(
            () => RulePackRuleCompatibility.ValidateOrThrow("NonExistentHook", "inject_prompt"));
    }

    // ── AsMarkdownTable ────────────────────────────────────────────────────────

    [Fact]
    public void AsMarkdownTable_IsNotEmpty()
    {
        var table = RulePackRuleCompatibility.AsMarkdownTable();
        Assert.False(string.IsNullOrWhiteSpace(table));
    }

    [Fact]
    public void AsMarkdownTable_ContainsAllHookPoints()
    {
        var table = RulePackRuleCompatibility.AsMarkdownTable();
        foreach (var hookPoint in RulePackRuleCompatibility.Allowed.Keys)
        {
            Assert.Contains(hookPoint, table);
        }
    }

    [Fact]
    public void AsMarkdownTable_ContainsAllRuleTypes()
    {
        var allRuleTypes = RulePackRuleCompatibility.Allowed.Values
            .SelectMany(s => s)
            .Distinct();

        var table = RulePackRuleCompatibility.AsMarkdownTable();
        foreach (var ruleType in allRuleTypes)
        {
            Assert.Contains(ruleType, table);
        }
    }

    [Fact]
    public void AsMarkdownTable_HasMarkdownTableStructure()
    {
        var table = RulePackRuleCompatibility.AsMarkdownTable();
        var lines = table.Split('\n');

        // Must have at least: header row, separator row, one data row
        Assert.True(lines.Length >= 3, "Table must have at least 3 lines.");

        // Header row starts with '|'
        Assert.StartsWith("|", lines[0]);

        // Separator row contains '---'
        Assert.Contains("---", lines[1]);
    }

    [Fact]
    public void AsMarkdownTable_CheckmarkAppearsForKnownValidCombination()
    {
        var table = RulePackRuleCompatibility.AsMarkdownTable();
        // Should contain checkmarks for valid combos
        Assert.Contains("✓", table);
    }

    [Fact]
    public void AsMarkdownTable_IsStable_CalledTwice()
    {
        // Regression guard: pure function, same result every call
        var first = RulePackRuleCompatibility.AsMarkdownTable();
        var second = RulePackRuleCompatibility.AsMarkdownTable();
        Assert.Equal(first, second);
    }
}
