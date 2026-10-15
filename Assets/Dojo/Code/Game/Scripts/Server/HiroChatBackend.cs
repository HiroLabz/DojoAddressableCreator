using System;
using System.Threading;
using Dojo.Game.UI;
using Dojo.Hiro;
using UnityEngine;

namespace Dojo.Game.Server
{
    /// <summary>
    /// Answers the player with a real reply from the Hiro platform.
    /// </summary>
    /// <remarks>
    /// Thin on purpose. It is an adapter between the game's chat seam
    /// (<see cref="IChatBackend"/>, which knows about a panel and a player) and the platform's
    /// (<see cref="IAgentChat"/>, which knows about agent ids and threads) — and that is all it is.
    /// The streaming, the SSE parsing and the retryable failures are all
    /// <see cref="HiroAgentChat"/>'s, over the generated SDK's transport.
    /// <para>
    /// Which is what makes a socket transport later a change to neither this class nor the panel.
    /// It depends on <c>IAgentChat</c>, not on anything that mentions HTTP.
    /// </para>
    /// </remarks>
    public sealed class HiroChatBackend : IChatBackend
    {
        /// <summary>What the companion pill reads while this is the facility answering.</summary>
        public string DisplayName => "HiroAI";

        readonly IAgentChat chat;

        public HiroChatBackend(IAgentChat chat)
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

            if (string.IsNullOrEmpty(context.PlatformAgentId))
            {
                // A roster entry with no platform agent behind it. Said plainly, with the fix,
                // because the alternative is a chat window that looks broken.
                return ChatReply.Failed(
                    (string.IsNullOrEmpty(context.AgentName) ? "This agent" : context.AgentName)
                    + " is not linked to an agent on Hiro, so there is nobody to ask. Start the "
                    + "game with an API key set and the startup sync will link them.");
            }

            var turn = await chat.AskAsync(
                context.PlatformAgentId,
                message,
                context.ThreadId,
                onToken,
                onActivity,
                onAttemptFailed,
                token);

            return turn.Ok
                ? ChatReply.Say(turn.Answer, turn.ThreadId)
                : ChatReply.Failed(turn.Error);
        }
    }
}
