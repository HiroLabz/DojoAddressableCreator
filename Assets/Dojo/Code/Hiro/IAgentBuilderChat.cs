using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Dojo.Framework.Net;
using UnityEngine;

namespace Dojo.Hiro
{
    /// <summary>
    /// Asking Hiro's own assistant something, rather than asking one of the agents.
    /// </summary>
    /// <remarks>
    /// A separate interface from <see cref="IAgentChat"/> although the signatures match, because
    /// they are two different correspondents and the distinction is the entire point. One reaches
    /// the agent standing on the floor; this one reaches the service that builds agents. They were
    /// the same call once, and the result was a game in which every agent answered as the builder.
    /// <para>
    /// The agent id is optional here and means something different: not "who is speaking" but "what
    /// is open". Given one, the assistant can look the agent up and answer about it; given none, it
    /// still answers about the platform.
    /// </para>
    /// </remarks>
    public interface IAgentBuilderChat
    {
        /// <summary>
        /// Sends <paramref name="message"/> to Hiro's assistant and returns the whole answer.
        /// </summary>
        /// <param name="agentId">
        /// The agent the player is looking at, so the assistant can speak about it. Optional: with
        /// nothing here it answers about the platform and the workspace instead.
        /// </param>
        Awaitable<ChatTurn> AskAsync(
            string agentId,
            string message,
            string threadId,
            Action<string> onToken,
            Action<string> onActivity,
            Action onAttemptFailed,
            CancellationToken token);
    }

    /// <summary>
    /// Hiro's assistant for a build with no Hiro backend: every question is answered with a failure that
    /// says so, rather than a request that could never be sent.
    /// </summary>
    public sealed class OfflineBuilderChat : IAgentBuilderChat
    {
        public Awaitable<ChatTurn> AskAsync(
            string agentId,
            string message,
            string threadId,
            Action<string> onToken,
            Action<string> onActivity,
            Action onAttemptFailed,
            CancellationToken token)
        {
            var source = new AwaitableCompletionSource<ChatTurn>();
            source.SetResult(ChatTurn.Failed("There is no Hiro backend in this build."));
            return source.Awaitable;
        }
    }
}
