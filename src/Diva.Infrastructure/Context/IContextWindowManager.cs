using Anthropic.SDK.Messaging;
using Diva.Core.Configuration;
using Diva.Infrastructure.Sessions;
using Microsoft.Extensions.AI;

namespace Diva.Infrastructure.Context;

public interface IContextWindowManager
{
    /// <summary>
    /// Point B: sliding window on cross-run history.
    /// Uses LLM summarisation when a model is available (SummarizerModel config or session model),
    /// falls back to rule-based summarisation otherwise.
    /// </summary>
    Task<(List<ConversationTurn> Turns, string? Summary)> CompactHistoryAsync(
        List<ConversationTurn> history,
        string? sessionModel = null,
        ContextWindowOverrideOptions? agentOverride = null,
        CancellationToken ct = default);

    /// <summary>Point A: in-run compaction for Anthropic SDK messages (rule-based, synchronous).</summary>
    List<Message> MaybeCompactAnthropicMessages(
        List<Message> messages,
        string systemPrompt,
        ContextWindowOverrideOptions? agentOverride = null);

    /// <summary>Point A: in-run compaction for ME.AI ChatMessages (rule-based, synchronous).</summary>
    List<ChatMessage> MaybeCompactChatMessages(
        List<ChatMessage> messages,
        ContextWindowOverrideOptions? agentOverride = null);
}
