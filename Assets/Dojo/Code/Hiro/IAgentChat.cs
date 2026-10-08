using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Dojo.Framework.Net;
using UnityEngine;

namespace Dojo.Hiro
{
    /// <summary>
    /// Asking an agent something and getting an answer back.
    /// </summary>
    /// <remarks>
    /// The interface a socket transport will satisfy unchanged, which is the whole reason it is
    /// shaped like this. <paramref name="onToken"/> is a push: the answer arrives in pieces and the
    /// caller is told about each one. Server-sent events over HTTP happen to deliver it that way
    /// today, and a socket would deliver it the same way tomorrow — so nothing above this line has
    /// to know which is in use.
    /// <para>
    /// A request/response signature returning only the finished string would have been simpler and
    /// wrong: it would have thrown away the streaming the API already offers, and swapping the
    /// transport later would then have been a change to every caller.
    /// </para>
    /// </remarks>
    public interface IAgentChat
    {
        /// <summary>
        /// Sends <paramref name="message"/> to an agent and returns the whole answer.
        /// </summary>
        /// <param name="agentId">The platform's agent id, not a local roster id.</param>
        /// <param name="threadId">
        /// The conversation so far, or null to start one. The reply carries the id to pass next
        /// time — see <see cref="ChatTurn.ThreadId"/>.
        /// <para>
        /// Kept in the signature although the REST implementation has no use for it: that endpoint
        /// remembers a conversation itself, so the id passes straight through. A transport that
        /// needs to be told where a conversation got to can still be given one without every caller
        /// changing shape.
        /// </para>
        /// </param>
        /// <param name="onToken">
        /// Called on the main thread for each piece of the answer as it arrives. Optional: a caller
        /// that only wants the finished text can pass null and read the return value.
        /// </param>
        /// <param name="onActivity">
        /// Called on the main thread with a short phrase describing what the agent is doing right
        /// now — "Prepared the response", "Writing the answer". Optional, and an implementation
        /// with nothing to report simply never calls it: a completions stream carries the answer
        /// and no commentary on how it is going.
        /// </param>
        /// <param name="onAttemptFailed">
        /// Called on the main thread when one attempt failed and another is about to start, so a
        /// caller that has already shown something can throw it away.
        /// </param>
        /// <remarks>
        /// The retries are in here rather than in the caller because what is worth retrying is a
        /// judgement about this platform, not about chat: an <c>error</c> event mid-stream is the
        /// transient the platform actually produces, and a 401 is not worth a second go. A UI that
        /// owned the loop would have to know all of that, and every later caller would need its
        /// own copy of it.
        /// <para>
        /// A failed attempt may already have pushed tokens or a progress phrase, which is why
        /// <paramref name="onAttemptFailed"/> exists: streaming stays live, and anything drawn for
        /// an attempt that then failed is discarded rather than left to run into the next one's
        /// text. Nothing about a failure is worded for the player until every attempt is spent.
        /// </para>
        /// </remarks>
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
    /// A question put back to the player, with the answers it will accept.
    /// </summary>
    /// <remarks>
    /// Hiro can stop mid-turn and ask something rather than answer — a <c>needs_input</c> event
    /// whose payload carries a prompt and a flat list of options. There are no identifiers of any
    /// kind on it: no question id, no option id, no type, no multi-select flag. An answer is the
    /// option's own text sent as the next message on the same thread, and the ordering of the
    /// conversation is the only thing correlating one to the other.
    /// <para>
    /// The payload also carries a <c>questions</c> array of further prompts. They are deliberately
    /// dropped: answering the first was enough to send Hiro straight to a proposal, and advancing
    /// through the rest locally would ask questions a server that had already moved on was no
    /// longer waiting for. If it still wants them it sends another <c>needs_input</c>.
    /// </para>
    /// </remarks>
    public sealed class ChatQuestion
    {
        public ChatQuestion(string prompt, IReadOnlyList<string> options)
        {
            Prompt = prompt;
            Options = options ?? Array.Empty<string>();
        }

        /// <summary>What is being asked.</summary>
        public string Prompt { get; }

        /// <summary>
        /// The offered answers, in order. Never null, and its length varies run to run — four or
        /// five in practice — so anything drawing these builds from the list rather than to a
        /// fixed count.
        /// </summary>
        public IReadOnlyList<string> Options { get; }
    }

    /// <summary>The outcome of one exchange.</summary>
    public readonly struct ChatTurn
    {
        /// <summary>The finished answer, or null when the exchange failed.</summary>
        public readonly string Answer;

        /// <summary>
        /// The conversation this turn belongs to. Pass it back to continue rather than start over.
        /// </summary>
        public readonly string ThreadId;

        /// <summary>Why there is no answer, in a form worth showing. Null on success.</summary>
        public readonly string Error;

        /// <summary>
        /// What Hiro wants to know before it can answer, or null on an ordinary reply.
        /// </summary>
        /// <remarks>
        /// A turn carrying this has <see cref="Answer"/> null and is still a <em>success</em>.
        /// Treating an unanswered turn as a failure is the bug this field exists to end.
        /// </remarks>
        public readonly ChatQuestion Question;

        public ChatTurn(string answer, string threadId, string error, ChatQuestion question = null)
        {
            Answer = answer;
            ThreadId = threadId;
            Error = error;
            Question = question;
        }

        public bool Ok => Error == null;

        /// <summary>True when there is something to put in the transcript, of either kind.</summary>
        public bool HasContent => !string.IsNullOrEmpty(Answer) || Question != null;

        public static ChatTurn Failed(string why) => new ChatTurn(null, null, why);

        public static ChatTurn Asked(ChatQuestion question, string threadId)
            => new ChatTurn(null, threadId, null, question);
    }

    /// <summary>
    /// The agent chat for a build with no Hiro backend: every question is answered with a failure that
    /// says so, rather than a request that could never be sent.
    /// </summary>
    public sealed class OfflineAgentChat : IAgentChat
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
