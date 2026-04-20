using Diva.Core.Prompts;
using Microsoft.Extensions.Logging.Abstractions;

namespace Diva.TenantAdmin.Tests;

public class PromptVariableResolverTests
{
    // ── Resolve — fast path ──────────────────────────────────────────────────

    [Fact]
    public void Resolve_NoPlaceholders_ReturnsSameString()
    {
        var result = PromptVariableResolver.Resolve("Hello world.", null);
        Assert.Equal("Hello world.", result);
    }

    [Fact]
    public void Resolve_EmptyString_ReturnsEmpty()
    {
        var result = PromptVariableResolver.Resolve("", null);
        Assert.Equal("", result);
    }

    // ── Resolve — built-in date/time variables ───────────────────────────────

    [Fact]
    public void Resolve_CurrentDate_ReplacedWithIsoDate()
    {
        var before = DateTime.UtcNow;
        var result = PromptVariableResolver.Resolve("Today is {{current_date}}.", null);
        var after  = DateTime.UtcNow;

        Assert.DoesNotContain("{{current_date}}", result);
        // Must be parseable as a date between before and after
        Assert.True(DateTime.TryParse(result.Replace("Today is ", "").TrimEnd('.'), out var parsed));
        Assert.InRange(parsed.Date, before.Date, after.Date);
    }

    [Fact]
    public void Resolve_CurrentTime_ReplacedWithUtcTime()
    {
        var result = PromptVariableResolver.Resolve("Now: {{current_time}}", null);

        Assert.DoesNotContain("{{current_time}}", result);
        Assert.Contains("UTC", result);
    }

    [Fact]
    public void Resolve_CurrentDatetime_ContainsDateAndUtc()
    {
        var before = DateTime.UtcNow;
        var result = PromptVariableResolver.Resolve("{{current_datetime}}", null);
        var after  = DateTime.UtcNow;

        Assert.DoesNotContain("{{current_datetime}}", result);
        Assert.Contains("UTC", result);
        // Should contain today's date portion
        Assert.Contains(before.ToString("yyyy-MM-dd"), result);
    }

    [Fact]
    public void Resolve_BuiltInsAreCaseInsensitive()
    {
        var result = PromptVariableResolver.Resolve("{{CURRENT_DATE}}", null);
        Assert.DoesNotContain("{{CURRENT_DATE}}", result);
    }

    [Fact]
    public void Resolve_MultipleBuiltInOccurrences_AllReplaced()
    {
        var result = PromptVariableResolver.Resolve("{{current_date}} and {{current_date}}", null);
        Assert.DoesNotContain("{{current_date}}", result);
        // Both occurrences replaced — resulting string has two date segments
        var segments = result.Split(" and ");
        Assert.Equal(2, segments.Length);
        Assert.Equal(segments[0], segments[1]); // same timestamp within one Resolve call
    }

    // ── Resolve — custom variables ───────────────────────────────────────────

    [Fact]
    public void Resolve_CustomVariable_IsReplaced()
    {
        var vars   = new Dictionary<string, string> { ["company_name"] = "Acme Corp" };
        var result = PromptVariableResolver.Resolve("You work for {{company_name}}.", vars);

        Assert.Equal("You work for Acme Corp.", result);
    }

    [Fact]
    public void Resolve_CustomVariableOverridesBuiltIn()
    {
        // User overrides current_date with a fixed value
        var vars   = new Dictionary<string, string> { ["current_date"] = "2099-01-01" };
        var result = PromptVariableResolver.Resolve("Date: {{current_date}}", vars);

        Assert.Equal("Date: 2099-01-01", result);
    }

    [Fact]
    public void Resolve_MixedBuiltInAndCustom_BothReplaced()
    {
        var vars   = new Dictionary<string, string> { ["tenant"] = "Globex" };
        var result = PromptVariableResolver.Resolve("{{tenant}} — {{current_date}}", vars);

        Assert.StartsWith("Globex — ", result);
        Assert.DoesNotContain("{{", result);
    }

    [Fact]
    public void Resolve_CustomVariablesAreCaseInsensitive()
    {
        // ParseJson wraps results in OrdinalIgnoreCase — simulate that here
        var vars   = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Company_Name"] = "Initech" };
        var result = PromptVariableResolver.Resolve("{{company_name}}", vars);

        Assert.Equal("Initech", result);
    }

    // ── Resolve — unknown variables ──────────────────────────────────────────

    [Fact]
    public void Resolve_UnknownVariable_LeftAsIs()
    {
        var result = PromptVariableResolver.Resolve("Hello {{unknown_var}}.", null);
        Assert.Equal("Hello {{unknown_var}}.", result);
    }

    [Fact]
    public void Resolve_UnknownVariable_LogsDebugAndContinues()
    {
        // Should not throw even with a logger wired up
        var result = PromptVariableResolver.Resolve(
            "{{missing}}", null, NullLogger.Instance);
        Assert.Equal("{{missing}}", result);
    }

    // ── ParseJson ────────────────────────────────────────────────────────────

    [Fact]
    public void ParseJson_Null_ReturnsNull()
        => Assert.Null(PromptVariableResolver.ParseJson(null));

    [Fact]
    public void ParseJson_Empty_ReturnsNull()
        => Assert.Null(PromptVariableResolver.ParseJson(""));

    [Fact]
    public void ParseJson_Whitespace_ReturnsNull()
        => Assert.Null(PromptVariableResolver.ParseJson("   "));

    [Fact]
    public void ParseJson_EmptyObject_ReturnsNull()
        => Assert.Null(PromptVariableResolver.ParseJson("{}"));

    [Fact]
    public void ParseJson_ValidJson_ReturnsDictionary()
    {
        var dict = PromptVariableResolver.ParseJson("""{"company_name":"Acme","region":"EU"}""");

        Assert.NotNull(dict);
        Assert.Equal("Acme", dict["company_name"]);
        Assert.Equal("EU",   dict["region"]);
    }

    [Fact]
    public void ParseJson_ValidJson_KeysAreCaseInsensitive()
    {
        var dict = PromptVariableResolver.ParseJson("""{"Company":"Duff"}""");

        Assert.NotNull(dict);
        Assert.Equal("Duff", dict["company"]);
    }

    [Fact]
    public void ParseJson_InvalidJson_ReturnsNull()
        => Assert.Null(PromptVariableResolver.ParseJson("{not valid json}",
            NullLogger.Instance));
}
