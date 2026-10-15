using System;
using System.Threading;
using Dojo.Game.UI;
using Dojo.Hiro;
using UnityEngine;

namespace Dojo.Game.Server
{
    /// <summary>
    /// Answers the player as the AI facility itself, rather than as one of its agents.
    /// </summary>
    /// <remarks>
    /// The companion side of the chat window. Thin, like <see cref="HiroChatBackend"/> beside it,
    /// and for the same reason: it is an adapter between the game's chat seam and the platform's,
    /// and the only thing it decides is which of the two platform surfaces a message belongs to.
    /// <para>
    /// The agent the player is standing next to is passed along as context. That is what lets the
    /// companion answer "what is wrong with this one's setup?" rather than only "what is Hiro?" —
    /// it is told what is open, never asked to speak for it.
    /// </para>
    /// <para>
    /// No guard on a missing agent id, unlike the agent backend. A companion with nobody selected
    /// is an ordinary state and it still has plenty to say.
    /// </para>
    /// </remarks>
    public sealed class HiroCompanionBackend : ICompanionChatBackend
    {
        /// <summary>
        /// What the companion pill reads.
        /// </summary>
        /// <remarks>
        /// The facility's name, not the surface's. It is Hiro's builder assistant behind this, but
        /// the pill answers "which AI is answering me" — and when a second facility is plugged in,
        /// the pill should rename itself to that one rather than to another job title.
        /// </remarks>
        public string DisplayName => "Hiro Assistant";

        readonly IAgentBuilderChat chat;

        public HiroCompanionBackend(IAgentBuilderChat chat)
        {
            this.chat = chat;
        }

        public async Awaitable<ChatReply> ReplyAsync(
            ChatContext context,
            string message,
            Action<string> onToken,
            Action<string> onActivity,
            Action onAttemptFailed,
            CancellationToken token)
        {
            if (chat == null)
            {
                return ChatReply.Failed("No connection to Hiro was set up.");
            }

            var turn = await chat.AskAsync(
                context.PlatformAgentId,
                message,
                context.ThreadId,
                onToken,
                onActivity,
                onAttemptFailed,
                token);

            if (!turn.Ok)
            {
                return ChatReply.Failed(turn.Error);
            }

            // A turn that asks rather than answers is a success with no prose in it. Passing it on
            // as Say(null) would put an empty bubble in the transcript and lose the options.
            return turn.Question != null
                ? ChatReply.Ask(turn.Question, turn.ThreadId)
                : ChatReply.Say(turn.Answer, turn.ThreadId);
        }
    }
}
