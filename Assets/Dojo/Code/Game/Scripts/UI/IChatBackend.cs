using System;
using System.Threading;
using Dojo.Hiro;
using UnityEngine;

namespace Dojo.Game.UI
{
    /// <summary>Who is being spoken to, and where the conversation has got to.</summary>
    /// <remarks>
    /// A context object rather than a growing parameter list. A backend needs different things
    /// depending on what it is: the stand-in wants the name to answer in, a platform backend wants
    /// the agent's id and the thread to continue. Passing all of it lets a new backend use what it
    /// needs without every caller changing shape again.
    /// </remarks>
    public readonly struct ChatContext
    {
        /// <summary>Name shown in the window, from the credentials file.</summary>
        public readonly string AgentName;

        /// <summary>Their department.</summary>
        public readonly string WorkId;

        /// <summary>
        /// The agent's id on the Hiro platform, or null when they are local only.
        /// </summary>
        /// <remarks>
        /// Null is a real state and not an error: a roster entry with no platform agent behind it
        /// still shows, still paints an area and still gets placed. It simply has nobody to answer,
        /// and a backend that needs one says so rather than inventing a reply.
        /// </remarks>
        public readonly string PlatformAgentId;

        /// <summary>The conversation so far, or null to start one.</summary>
        public readonly string ThreadId;

        public ChatContext(string agentName, string workId, string platformAgentId, string threadId)
        {
            AgentName = agentName;
            WorkId = workId;
            PlatformAgentId = platformAgentId;
            ThreadId = threadId;
        }

        public ChatContext WithThread(string threadId)
            => new ChatContext(AgentName, WorkId, PlatformAgentId, threadId);
    }

    /// <summary>What a backend said back.</summary>
    public readonly struct ChatReply
    {
        /// <summary>The answer, or null when there is none.</summary>
        public readonly string Text;

        /// <summary>The conversation to continue next time, when the backend keeps one.</summary>
        public readonly string ThreadId;

        /// <summary>Why there is no answer, worded for the player. Null on success.</summary>
        public readonly string Error;

        /// <summary>
        /// What the backend wants to know before it can answer, or null on an ordinary reply.
        /// </summary>
        /// <remarks>
        /// A reply carrying this has <see cref="Text"/> null and is still a success. The window
        /// draws it as a card of buttons rather than a bubble, and clicking one sends that
        /// option's text as the next message — which is all the platform needs to continue.
        /// </remarks>
        public readonly ChatQuestion Question;

        public ChatReply(string text, string threadId, string error, ChatQuestion question = null)
        {
            Text = text;
            ThreadId = threadId;
            Error = error;
            Question = question;
        }

        public bool Ok => Error == null;

        public static ChatReply Say(string text, string threadId = null)
            => new ChatReply(text, threadId, null);

        public static ChatReply Ask(ChatQuestion question, string threadId = null)
            => new ChatReply(null, threadId, null, question);

        public static ChatReply Failed(string why) => new ChatReply(null, null, why);
    }

    /// <summary>
    /// Whatever answers the player when they send a message to an agent.
    /// </summary>
    /// <remarks>
    /// One method, and an interface for it rather than a call sitting inside the panel, because the
    /// reply is the one part of a chat certain to be replaced. Today it is
    /// <see cref="EchoChatBackend"/> or the platform; tomorrow it might be a scripted routine or a
    /// lookup against the agent's department. None of those change the panel.
    /// <para>
    /// <b>Asynchronous, and it has to be.</b> This began as a synchronous <c>string Reply(...)</c>,
    /// which was right for a canned answer and impossible for a real one: a measured exchange with
    /// the platform took roughly eight seconds of thinking before the first word arrived, and a
    /// synchronous call would have frozen the game solid for all of it.
    /// </para>
    /// <para>
    /// <paramref name="onToken"/> is what makes that wait bearable and what keeps the interface
    /// honest about streaming. The answer arrives in pieces and the caller is told about each one
    /// as it lands, so the window fills in while the agent is still talking. A backend with nothing
    /// to stream simply never calls it and returns the finished text.
    /// </para>
    /// </remarks>
    public interface IChatBackend
    {
        /// <summary>
        /// What to call this facility in front of the player, e.g. "HiroAI".
        /// </summary>
        /// <remarks>
        /// The companion in the chat window is not a roster entry — it is whichever AI is currently
        /// answering, and that changes when the backend does. Asking the backend its own name is
        /// what keeps the pill honest through that swap; a name held anywhere else would have to be
        /// remembered and edited, and would eventually be introducing the wrong service.
        /// </remarks>
        string DisplayName { get; }

        /// <summary>
        /// Answers <paramref name="message"/>.
        /// </summary>
        /// <param name="onToken">
        /// Called on the main thread for each piece of the answer as it arrives. May be null.
        /// </param>
        /// <param name="onActivity">
        /// Called on the main thread with a short phrase describing what the agent is doing while
        /// the player waits. Whether there is anything to report depends on the backend: the
        /// platform sends it, a canned stand-in has nothing to say. May be null.
        /// </param>
        /// <param name="onAttemptFailed">
        /// Called on the main thread when a backend that retries has had an attempt fail and is
        /// about to make another, so anything already streamed for the failed one can be thrown
        /// away. A backend that does not retry never calls it. May be null.
        /// </param>
        /// <remarks>
        /// A failed attempt is silent by design: the caller is told to discard what it drew, not
        /// what went wrong. Only the final <see cref="ChatReply"/> carries an
        /// <see cref="ChatReply.Error"/> worded for the player, so a chat that recovers on its
        /// second try never flashes a complaint the player then has to ignore.
        /// </remarks>
        Awaitable<ChatReply> ReplyAsync(
            ChatContext context,
            string message,
            Action<string> onToken,
            Action<string> onActivity,
            Action onAttemptFailed,
            CancellationToken token);
    }

    /// <summary>
    /// The backend behind the companion pill, as opposed to the one behind an agent.
    /// </summary>
    /// <remarks>
    /// Nothing but a name. Both sides of the window are <see cref="IChatBackend"/>s and neither
    /// needs anything the other lacks, but a container cannot hand out two of the same interface
    /// without being told which is which — and naming the distinction here is better than naming it
    /// in a registration nobody reads.
    /// <para>
    /// It is also the honest shape of the thing: the window really does hold two correspondents,
    /// and the one bug this whole seam exists to prevent was them being the same one.
    /// </para>
    /// </remarks>
    public interface ICompanionChatBackend : IChatBackend
    {
    }

    /// <summary>
    /// The stand-in reply: acknowledges what was said and hands back something to look at.
    /// </summary>
    /// <remarks>
    /// Exists so the panel can be finished, seen and judged with no platform behind it — and so a
    /// scene with no API key still demonstrates the chat rather than refusing to open. It is honest
    /// about being a placeholder rather than pretending to understand, because a canned answer that
    /// sounds real is the kind of thing that survives into a demo.
    /// </remarks>
    public sealed class EchoChatBackend : IChatBackend
    {
        /// <remarks>
        /// Named for what it is. A stand-in that introduced itself as the real service would make a
        /// session with no backend behind it indistinguishable from one that is working.
        /// </remarks>
        public string DisplayName => "Offline";

        public async Awaitable<ChatReply> ReplyAsync(
            ChatContext context,
            string message,
            Action<string> onToken,
            Action<string> onActivity,
            Action onAttemptFailed,
            CancellationToken token)
        {
            // onAttemptFailed is never called: a canned answer cannot fail transiently, so there
            // is never a discarded attempt to tell anybody about.
            if (string.IsNullOrEmpty(message))
            {
                return ChatReply.Failed("Nothing to answer.");
            }

            // One frame, so the panel exercises the same asynchronous path a real backend takes.
            // Without it a bug that only shows when a reply arrives late would never appear here.
            await Awaitable.NextFrameAsync(token);

            var who = string.IsNullOrEmpty(context.AgentName) ? "I" : context.AgentName;
            var job = string.IsNullOrEmpty(context.WorkId) ? "this" : context.WorkId;

            return ChatReply.Say(who + " here. Nothing is wired up behind me yet, so all I can do "
                + "is repeat it back: “" + message.Trim() + "”. When there is a " + job
                + " brain to ask, it answers here.");
        }
    }
}
