using Anthropic.SDK.Messaging;
using Diva.Core.Configuration;
using Diva.Infrastructure.Context;
using Diva.Infrastructure.Sessions;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace Diva.Agents.Tests.Helpers;

/// <summary>Shared helpers for tests involving IContextWindowManager.</summary>
internal static class ContextWindowTestHelpers
{
    /// <summary>Returns a no-op IContextWindowManager mock: history and messages pass through unchanged.</summary>
    public static IContextWindowManager NoOpCtx()
    {
        var ctx = Substitute.For<IContextWindowManager>();

        ctx.CompactHistoryAsync(
                Arg.Any<List<ConversationTurn>>(),
                Arg.Any<string?>(),
                Arg.Any<ContextWindowOverrideOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<(List<ConversationTurn>, string?)>(
                (ci.ArgAt<List<ConversationTurn>>(0), null)));

        ctx.MaybeCompactAnthropicMessages(
                Arg.Any<List<Message>>(),
                Arg.Any<string>(),
                Arg.Any<ContextWindowOverrideOptions?>())
            .Returns(ci => ci.ArgAt<List<Message>>(0));

        ctx.MaybeCompactChatMessages(
                Arg.Any<List<ChatMessage>>(),
                Arg.Any<ContextWindowOverrideOptions?>())
            .Returns(ci => ci.ArgAt<List<ChatMessage>>(0));

        return ctx;
    }
}
