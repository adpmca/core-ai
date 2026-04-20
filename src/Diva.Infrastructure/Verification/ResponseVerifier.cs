using System.Text.Json;
using System.Text.RegularExpressions;
using Anthropic.SDK.Messaging;
using Diva.Core.Configuration;
using Diva.Core.Models;
using Diva.Infrastructure.LiteLLM;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Diva.Infrastructure.Verification;

/// <summary>
/// Verifies agent responses against the tool evidence collected during the ReAct loop.
///
/// Modes:
///   Off           — no check, always returns IsVerified=true
///   ToolGrounded  — heuristic: flags factual claims if no tools were called (zero LLM cost)
///   LlmVerifier   — second-pass LLM call that cross-checks claims against tool evidence
///   Strict        — same as LlmVerifier but blocks the response if confidence is below threshold
/// </summary>
public sealed class ResponseVerifier
{
    private readonly VerificationOptions _opts;
    private readonly LlmOptions _llm;
    private readonly IAnthropicProvider _anthropic;
    private readonly IOpenAiProvider _openAi;
    private readonly ILogger<ResponseVerifier> _logger;

    // Matches numbers that look like factual data: prices, stats, percentages, counts
    private static readonly Regex FactualClaimPattern =
        new(@"[\$\£\€]?\d[\d,\.]*\s*(%|transactions?|units?|pts?|points?|\b)?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public ResponseVerifier(
        IOptions<VerificationOptions> opts,
        IOptions<LlmOptions> llm,
        IAnthropicProvider anthropic,
        IOpenAiProvider openAi,
        ILogger<ResponseVerifier> logger)
    {
        _opts      = opts.Value;
        _llm       = llm.Value;
        _anthropic = anthropic;
        _openAi    = openAi;
        _logger    = logger;
    }

    public async Task<VerificationResult> VerifyAsync(
        string responseText,
        IReadOnlyList<string> toolsUsed,
        string toolEvidence,
        CancellationToken ct,
        string? modelId = null,
        string? modeOverride = null)
    {
        // Per-agent override takes priority over global config
        var effectiveMode = !string.IsNullOrWhiteSpace(modeOverride) ? modeOverride : _opts.Mode;

        _logger.LogDebug("Verifying response (mode={Mode}, tools={Count}, evidence={Len})",
            effectiveMode, toolsUsed.Count, toolEvidence.Length);

        return effectiveMode switch
        {
            "Off"          => Skipped(),
            "ToolGrounded" => ToolGroundedCheck(responseText, toolsUsed),
            "LlmVerifier"  => await LlmVerifyAsync(responseText, toolEvidence, block: false, ct, modelId),
            "Strict"       => await LlmVerifyAsync(responseText, toolEvidence, block: true, ct, modelId),
            "Auto"         => await AutoVerifyAsync(responseText, toolsUsed, toolEvidence, ct, modelId),
            _              => Skipped()
        };
    }

    private async Task<VerificationResult> AutoVerifyAsync(
        string responseText,
        IReadOnlyList<string> toolsUsed,
        string toolEvidence,
        CancellationToken ct,
        string? modelId)
    {
        // Short/trivial response — nothing meaningful to verify
        if (string.IsNullOrWhiteSpace(responseText) || responseText.Length < 80)
            return Skipped();

        // Tools were called AND produced evidence → trust the grounding heuristic (zero extra LLM cost).
        // Set Mode=LlmVerifier or Mode=Strict explicitly to enable cross-checking of claims vs evidence.
        if (toolsUsed.Count > 0 && !string.IsNullOrWhiteSpace(toolEvidence))
            return ToolGroundedCheck(responseText, toolsUsed);

        // Tools were called but produced no evidence (unusual) → cheap heuristic
        if (toolsUsed.Count > 0)
            return ToolGroundedCheck(responseText, toolsUsed);

        // No tools: flag only if response looks like it contains factual data
        if (ContainsFactualClaims(responseText))
            return ToolGroundedCheck(responseText, toolsUsed);

        // Purely conversational — skip verification entirely
        return Skipped();
    }

    // ── Modes ─────────────────────────────────────────────────────────────────

    private static VerificationResult Skipped() =>
        new() { IsVerified = true, Confidence = 1f, Mode = "Off" };

    private VerificationResult ToolGroundedCheck(string response, IReadOnlyList<string> toolsUsed)
    {
        // No tools were called but the response contains factual-looking data → flag
        if (toolsUsed.Count == 0 && ContainsFactualClaims(response))
        {
            _logger.LogWarning("Unverified response: factual claims present but no tools were called");
            return new VerificationResult
            {
                IsVerified       = false,
                Confidence       = 0.4f,
                Mode             = "ToolGrounded",
                UngroundedClaims = ["Response contains factual claims but no tools were called to support them"],
                Reasoning        = "No tool evidence available to verify factual assertions"
            };
        }

        // Tools were called — we trust the grounding at this level
        return new VerificationResult
        {
            IsVerified = true,
            Confidence = toolsUsed.Count > 0 ? 0.85f : 0.95f,
            Mode       = "ToolGrounded"
        };
    }

    private async Task<VerificationResult> LlmVerifyAsync(
        string response, string evidence, bool block, CancellationToken ct, string? modelId = null)
    {
        // Skip LLM verification for trivial responses — no factual content to check
        if (string.IsNullOrWhiteSpace(response) || response.Length < 80)
            return new VerificationResult { IsVerified = true, Confidence = 1f, Mode = block ? "Strict" : "LlmVerifier" };

        try
        {
            var raw    = await CallLlmAsync(response, evidence, ct, modelId);
            var parsed = ParseVerifierResponse(raw);

            var shouldBlock = block && parsed.Confidence < _opts.ConfidenceThreshold;

            if (shouldBlock)
                _logger.LogWarning("Strict mode blocking response — confidence={Conf:F2}", parsed.Confidence);

            return new VerificationResult
            {
                IsVerified       = parsed.IsVerified,
                Confidence       = parsed.Confidence,
                Mode             = block ? "Strict" : "LlmVerifier",
                UngroundedClaims = parsed.UngroundedClaims,
                WasBlocked       = shouldBlock,
                Reasoning        = _opts.IncludeReasoningInResponse ? parsed.Reasoning : null
            };
        }
        catch (Exception ex)
        {
            // In Strict mode, a failed verifier means we cannot confirm accuracy — block the response.
            // In LlmVerifier mode, fail-open: allow through with low confidence so delivery is not disrupted.
            _logger.LogError(ex, "LLM verification call failed (mode={Mode}) — {Action}",
                block ? "Strict" : "LlmVerifier",
                block ? "blocking response" : "allowing response through with low confidence");
            return new VerificationResult
            {
                IsVerified = !block,
                WasBlocked = block,
                Confidence = 0.5f,
                Mode       = block ? "Strict" : "LlmVerifier",
                Reasoning  = block ? "Verification service unavailable — response blocked in Strict mode" : null,
            };
        }
    }

    // ── LLM call (provider split — same pattern as AnthropicAgentRunner) ──────

    private async Task<string> CallLlmAsync(string response, string evidence, CancellationToken ct, string? modelId = null)
    {
        var prompt = BuildPrompt(response, evidence);
        var opts   = _llm.DirectProvider;
        var model  = !string.IsNullOrWhiteSpace(_opts.VerifierModel) ? _opts.VerifierModel
                   : !string.IsNullOrWhiteSpace(modelId)             ? modelId
                   : opts.Model;

        if (opts.Provider.Equals("Anthropic", StringComparison.OrdinalIgnoreCase))
            return await CallAnthropicAsync(prompt, model, ct);

        return await CallOpenAiCompatibleAsync(prompt, model, ct);
    }

    private async Task<string> CallAnthropicAsync(string prompt, string model, CancellationToken ct)
    {
        var parameters = new MessageParameters
        {
            Model     = model,
            MaxTokens = _opts.VerifierMaxTokens,
            System    = [new SystemMessage("You are a JSON-only fact-checking assistant. Respond ONLY with valid JSON.")],
            Messages  = [new Message
            {
                Role    = RoleType.User,
                Content = [new Anthropic.SDK.Messaging.TextContent { Text = prompt }]
            }]
        };

        var msg = await _anthropic.GetClaudeMessageAsync(parameters, ct);
        return msg.Content.OfType<Anthropic.SDK.Messaging.TextContent>().FirstOrDefault()?.Text ?? "{}";
    }

    private async Task<string> CallOpenAiCompatibleAsync(string prompt, string model, CancellationToken ct)
    {
        var chatClient = _openAi.CreateChatClient(model);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are a JSON-only fact-checking assistant. Respond ONLY with valid JSON."),
            new(ChatRole.User, prompt)
        };
        var result = await chatClient.GetResponseAsync(messages, new ChatOptions { MaxOutputTokens = _opts.VerifierMaxTokens }, ct);
        return result.Text ?? "{}";
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string BuildPrompt(string response, string evidence)
    {
        var evidenceSection = string.IsNullOrEmpty(evidence)
            ? "(no tools were called — no evidence available)"
            : evidence;

        return
            "TOOL EVIDENCE (ground truth — data returned by real tool calls):\n" +
            "---\n" +
            evidenceSection + "\n" +
            "---\n\n" +
            "AGENT RESPONSE (to verify):\n" +
            "---\n" +
            response + "\n" +
            "---\n\n" +
            "Identify factual claims in the agent response that are NOT supported by the tool evidence\n" +
            "and cannot be logically inferred from it. Focus on specific numbers, dates, names, prices,\n" +
            "statistics, or events asserted without evidence. If no tools were called and the response\n" +
            "contains specific facts, treat all such facts as ungrounded.\n\n" +
            "Respond ONLY with this JSON (no markdown, no explanation):\n" +
            "{\"confidence\": 0.0, \"is_verified\": false, \"ungrounded_claims\": [], \"reasoning\": \"\"}";
    }

    private static VerificationResult ParseVerifierResponse(string raw)
    {
        // Strip any markdown fences the LLM may have added
        var json = Regex.Replace(raw.Trim(), @"^```json?\s*|\s*```$", "", RegexOptions.Multiline).Trim();

        // Handle empty or invalid response
        if (string.IsNullOrEmpty(json) || json == "{}")
            return new VerificationResult { IsVerified = true, Confidence = 0.5f };

        // Extract just the JSON object in case the model appended explanation text
        var firstBrace = json.IndexOf('{');
        var lastBrace  = json.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
            json = json[firstBrace..(lastBrace + 1)];

        using var doc  = JsonDocument.Parse(json);
        var root       = doc.RootElement;

        var confidence = root.TryGetProperty("confidence",   out var conf) ? (float)conf.GetDouble() : 0.5f;
        var isVerified = root.TryGetProperty("is_verified",  out var iv)   && iv.GetBoolean();
        var reasoning  = root.TryGetProperty("reasoning",    out var r)    ? r.GetString() : null;

        var claims = new List<string>();
        if (root.TryGetProperty("ungrounded_claims", out var uc) && uc.ValueKind == JsonValueKind.Array)
            foreach (var item in uc.EnumerateArray())
                if (item.GetString() is { } s && !string.IsNullOrEmpty(s))
                    claims.Add(s);

        return new VerificationResult
        {
            IsVerified       = isVerified,
            Confidence       = confidence,
            UngroundedClaims = claims,
            Reasoning        = reasoning
        };
    }

    private static bool ContainsFactualClaims(string text) =>
        FactualClaimPattern.IsMatch(text);
}
