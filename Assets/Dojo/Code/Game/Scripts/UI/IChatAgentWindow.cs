using System;

namespace Dojo.Game.UI
{
    /// <summary>
    /// The chat window: the agent the player clicked, and the AI facility answering.
    /// </summary>
    /// <remarks>
    /// A window rather than a dialog, and the minimise button is the reason. <c>IDialog</c> is for
    /// "a question that owns the screen until it is answered", and says outright that toggling one
    /// would be meaningless. A chat the player can shrink to its title bar and leave up while they
    /// carry on arranging the floor is the opposite of that: it is a panel opened and closed at
    /// will, which is what <c>IInGamePopup</c> describes and what this is.
    /// <para>
    /// The practical difference is the scrim. A modal earns one because nothing behind it may be
    /// touched; this one has none, and does not blur what is behind it either — blurring a floor the
    /// player is still using would make the window feel modal while behaving otherwise.
    /// </para>
    /// <para>
    /// It holds exactly two conversations and cannot hold more: the agent, and the companion. The
    /// companion is not a roster entry — it is whichever facility is currently answering, named by
    /// <see cref="IChatBackend.DisplayName"/>, so swapping the backend renames the pill and nothing
    /// else has to be told.
    /// </para>
    /// </remarks>
    public interface IChatAgentWindow
    {
        /// <summary>Whether the window is on screen, in any of its three sizes.</summary>
        bool IsOpen { get; }

        /// <summary>Which of the two conversations is showing.</summary>
        ChatConversation Showing { get; }

        /// <summary>Which of the three sizes the window is at.</summary>
        ChatWindowSize Size { get; }

        /// <summary>Whether the window is shrunk to its title bar.</summary>
        bool IsMinimised { get; }

        /// <summary>Whether the window is filling the screen.</summary>
        bool IsMaximised { get; }

        /// <summary>
        /// Opens the window on an agent.
        /// </summary>
        /// <remarks>
        /// The agent's conversation is the one shown, because the player clicked the agent and not
        /// the companion. The companion's is built alongside it and waits behind its pill.
        /// </remarks>
        /// <param name="agent">Who was clicked, and the thread to continue if there is one.</param>
        /// <param name="onClosed">Called once the window is closed, if supplied.</param>
        void Open(ChatContext agent, Action onClosed = null);

        /// <summary>Takes the window off screen. The transcripts are dropped with it.</summary>
        void Close();

        /// <summary>Puts the window into one of its three sizes.</summary>
        void SetSize(ChatWindowSize size);

        /// <summary>Shrinks the window to its title bar, or restores it.</summary>
        void SetMinimised(bool minimised);

        /// <summary>Fills the screen, or returns the window to its authored size.</summary>
        void SetMaximised(bool maximised);
    }

    /// <summary>How big the chat window currently is.</summary>
    /// <remarks>
    /// One state rather than a minimised flag and a maximised flag. Two booleans allow a fourth
    /// combination that means nothing — both at once — and that combination was reachable: minimise
    /// then maximise left the window stretched across the screen with its body switched off.
    /// </remarks>
    public enum ChatWindowSize
    {
        /// <summary>The authored size. The only size the window grows from.</summary>
        Normal,

        /// <summary>The title bar alone. Minimise is withheld; maximise restores.</summary>
        Minimised,

        /// <summary>Filling the screen. Minimise shrinks it; maximise restores.</summary>
        Maximised,
    }

    /// <summary>Which pill under the title bar is selected.</summary>
    public enum ChatConversation
    {
        /// <summary>The agent the player clicked.</summary>
        Agent,

        /// <summary>The AI facility itself.</summary>
        Companion,
    }
}
