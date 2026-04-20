using Microsoft.Extensions.Logging;

namespace Diva.Infrastructure.Learning;

/// <summary>
/// Processes explicit user corrections to agent responses and saves them as learned rules.
/// Called when a user says "That's wrong, actually X should be Y" after an agent response.
/// </summary>
public sealed class FeedbackLearningService
{
    private readonly LlmRuleExtractor _extractor;
    private readonly IRuleLearningService _learning;
    private readonly ILogger<FeedbackLearningService> _logger;

    public FeedbackLearningService(
        LlmRuleExtractor extractor,
        IRuleLearningService learning,
        ILogger<FeedbackLearningService> logger)
    {
        _extractor = extractor;
        _learning  = learning;
        _logger    = logger;
    }

    /// <summary>
    /// Analyses a user correction and, if a business rule is implied, saves it for admin review.
    /// </summary>
    public async Task ProcessCorrectionAsync(
        int tenantId,
        string sessionId,
        string originalResponse,
        string userCorrection,
        CancellationToken ct)
    {
        // Build a small transcript from the correction context
        var transcript =
            $"Agent said: {originalResponse}\n" +
            $"User corrected: {userCorrection}";

        var rules = await _extractor.ExtractAsync(transcript, sessionId, ct);

        foreach (var rule in rules.Where(r => r.Confidence >= 0.7f))
        {
            _logger.LogInformation(
                "Correction implies business rule: {Key} (confidence={Conf:F2})", rule.RuleKey, rule.Confidence);

            await _learning.SaveLearnedRuleAsync(
                tenantId,
                new SuggestedRule
                {
                    AgentType       = rule.AgentType,
                    RuleCategory    = "learned_from_correction",
                    RuleKey         = rule.RuleKey,
                    PromptInjection = rule.PromptInjection,
                    Confidence      = rule.Confidence,
                    SourceSessionId = rule.SourceSessionId
                },
                RuleApprovalMode.RequireAdmin,
                ct);
        }
    }
}
