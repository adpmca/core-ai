namespace Diva.Agents.Hooks.BuiltIn;

using System.Text.RegularExpressions;
using Diva.Core.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// Scans the agent's response for common PII patterns and redacts them before returning.
/// Detects: SSN, credit card numbers, US phone numbers, email addresses.
///
/// Agent custom variables:
///   "pii_mode"       — "redact" (default) replaces patterns in-line; "block" withholds entire response.
///   "pii_skip_types" — Comma-separated list of types to leave untouched.
///                      Supported values: email, phone, ssn, cc
///                      Example: "email" or "email,phone"
/// </summary>
public sealed partial class PiiRedactionHook : IOnBeforeResponseHook
{
    private readonly ILogger<PiiRedactionHook> _logger;

    public PiiRedactionHook(ILogger<PiiRedactionHook> logger)
    {
        _logger = logger;
    }

    public int Order => 10; // Run early — before disclaimer/citation hooks

    public Task<string> OnBeforeResponseAsync(
        AgentHookContext context, string responseText, CancellationToken ct)
    {
        var mode = context.Variables.GetValueOrDefault("pii_mode", "redact");
        var skipRaw = context.Variables.GetValueOrDefault("pii_skip_types", "");
        var skip = skipRaw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToLowerInvariant())
            .ToHashSet();

        _logger.LogDebug(
            "PiiRedactionHook running — mode={Mode} skip=[{Skip}] agent={AgentId}",
            mode, skipRaw, context.AgentId);

        var matches = new List<string>();

        // SSN: 123-45-6789 or 123 45 6789
        if (!skip.Contains("ssn") && SsnPattern().IsMatch(responseText))
            matches.Add("SSN");

        // Credit card: 4 groups of 4 digits (Visa/MC/Amex patterns)
        if (!skip.Contains("cc") && CreditCardPattern().IsMatch(responseText))
            matches.Add("Credit Card");

        // US phone: (555) 123-4567 or 555-123-4567 or +1-555-123-4567
        if (!skip.Contains("phone") && PhonePattern().IsMatch(responseText))
            matches.Add("Phone Number");

        // Email
        if (!skip.Contains("email") && EmailPattern().IsMatch(responseText))
            matches.Add("Email Address");

        if (matches.Count == 0)
        {
            _logger.LogDebug("PiiRedactionHook: no PII detected — response unchanged");
            return Task.FromResult(responseText);
        }

        _logger.LogWarning(
            "PiiRedactionHook: detected [{Types}] mode={Mode} skip=[{Skip}] agent={AgentId}",
            string.Join(", ", matches), mode, skipRaw, context.AgentId);

        if (mode == "block")
        {
            return Task.FromResult(
                $"⚠️ **Response blocked**: Detected potential PII ({string.Join(", ", matches)}). " +
                "The original response has been withheld to protect sensitive information.");
        }

        // Redact mode — replace patterns with [REDACTED]
        var redacted = responseText;
        if (!skip.Contains("ssn"))   redacted = SsnPattern().Replace(redacted, "[SSN REDACTED]");
        if (!skip.Contains("cc"))    redacted = CreditCardPattern().Replace(redacted, "[CC REDACTED]");
        if (!skip.Contains("phone")) redacted = PhonePattern().Replace(redacted, "[PHONE REDACTED]");
        if (!skip.Contains("email")) redacted = EmailPattern().Replace(redacted, "[EMAIL REDACTED]");

        redacted += $"\n\n> ⚠️ **PII detected and redacted**: {string.Join(", ", matches)}.";

        return Task.FromResult(redacted);
    }

    [GeneratedRegex(@"\b\d{3}[-\s]\d{2}[-\s]\d{4}\b")]
    private static partial Regex SsnPattern();

    [GeneratedRegex(@"\b(?:\d{4}[-\s]?){3}\d{4}\b")]
    private static partial Regex CreditCardPattern();

    [GeneratedRegex(@"(?:\+?1[-.\s]?)?\(?\d{3}\)?[-.\s]?\d{3}[-.\s]?\d{4}\b")]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b")]
    private static partial Regex EmailPattern();
}
