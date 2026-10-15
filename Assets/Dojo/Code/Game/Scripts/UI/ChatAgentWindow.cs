using System;
using System.Collections.Generic;
using System.Threading;
using Dojo.Framework.UI;
using Dojo.Hiro;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace Dojo.Game.UI
{
    /// <summary>
    /// The full-screen chat window: a title bar, two conversation pills, the agent's readout, the
    /// transcript, and somewhere to type.
    /// </summary>
    /// <remarks>
    /// Every part comes from <c>ChatAgentWindow.prefab</c> and a missing reference is reported
    /// rather than built around, the same bargain <see cref="LoadWorldDialog"/> makes. A chat window
    /// that half-assembled itself would be one where the send button might not be the send button.
    /// <para>
    /// The two transcripts are kept apart and only one is ever on screen. Switching pills swaps
    /// which list is drawn and which <see cref="ChatContext"/> a message is sent with, so the
    /// companion never sees what was said to the agent and the agent's thread is not disturbed by
    /// asking the companion something in the middle of it.
    /// </para>
    /// <para>
    /// A reply in flight is cancelled when the window closes or the pill changes. Without that, an
    /// answer to a question asked of the agent would arrive into whichever transcript happened to be
    /// showing when it landed.
    /// </para>
    /// </remarks>
    public sealed class ChatAgentWindow : MonoBehaviour, IChatAgentWindow
    {
        /// <summary>One line of a transcript, kept so the list survives a pill switch.</summary>
        sealed class Line
        {
            public string Text;
            public bool FromPlayer;

            /// <summary>
            /// Set when this line is a question with options rather than prose.
            /// </summary>
            /// <remarks>
            /// Held here because this list is the only persistence the transcript has — a redraw
            /// rebuilds every row from it, so a question stored anywhere else would vanish the
            /// moment the player switched pills and came back. Cleared when an option is picked,
            /// which is what turns the card into an ordinary player bubble.
            /// </remarks>
            public ChatQuestion Question;
        }

        /// <summary>One side of the window: who is answering, and what has been said.</summary>
        sealed class Thread
        {
            public ChatContext Context;
            public readonly List<Line> Lines = new List<Line>();
        }

        [Header("Parts")]
        [Tooltip("The whole window. Switched off while it is not on screen.")]
        [SerializeField] GameObject root;


        [Header("Title bar")]
        [SerializeField] TMP_Text titleLabel;
        [SerializeField] TMP_Text titleSubtitle;
        [SerializeField] TMP_Text titleAvatarLabel;
        [SerializeField] Image titleAvatar;

        [Tooltip("Closes the window.")]
        [SerializeField] Button closeButton;

        [Tooltip("Shrinks the window to its title bar, and restores it.")]
        [SerializeField] Button minimiseButton;

        [Tooltip("Everything below the title bar. Switched off while minimised, which is what " +
                 "leaves the title bar sitting on its own.")]
        [SerializeField] GameObject body;

        [Tooltip("Height of the window while minimised — the title bar and nothing else.")]
        [SerializeField] float minimisedHeight = 76f;

        [Tooltip("Width of the window while minimised. Narrower than the window, because a title " +
                 "bar at full width reads as a broken window rather than a parked one.")]
        [SerializeField] float minimisedWidth = 560f;

        [Tooltip("Moves the window and keeps it on screen. Defaults to the one on the window.")]
        [SerializeField] ChatWindowDrag drag;

        [Tooltip("Swells the window to fill the screen, and puts it back.")]
        [SerializeField] Button maximiseButton;

        [Tooltip("The icon on the maximise button. Swapped, because the button does two jobs.")]
        [SerializeField] Image maximiseIcon;

        [Tooltip("Shown while the window is at its authored size: pressing it fills the screen.")]
        [SerializeField] Sprite maximiseSprite;

        [Tooltip("Shown at either other size: pressing it returns the window to its authored size.")]
        [SerializeField] Sprite restoreSprite;

        [Tooltip("How far a maximised window stops short of the screen edge.")]
        [SerializeField] float maximisedInset = 24f;

        [Tooltip("Draw order against the other nested canvases on the game canvas. Above the " +
                 "inventory, which sits at 10.")]
        [SerializeField] int sortingOrder = 20;

        [Header("Pills")]
        [Tooltip("Show the Hiro Assistant pill beside the agent's. Off: the window only holds the " +
                 "conversation with the agent it was opened for. The assistant's code stays, to be " +
                 "used elsewhere.")]
        [SerializeField] bool showHiroAssistant;

        [Tooltip("Selects the agent's conversation.")]
        [SerializeField] Button agentPill;

        [SerializeField] TMP_Text agentPillName;
        [SerializeField] TMP_Text agentPillRole;
        [SerializeField] TMP_Text agentPillAvatarLabel;
        [SerializeField] Image agentPillAvatar;
        [SerializeField] GameObject agentPillSelected;

        [Tooltip("Selects the companion's conversation.")]
        [SerializeField] Button companionPill;

        [SerializeField] TMP_Text companionPillName;
        [SerializeField] TMP_Text companionPillRole;
        [SerializeField] TMP_Text companionPillAvatarLabel;
        [SerializeField] Image companionPillAvatar;
        [SerializeField] GameObject companionPillSelected;

        [Header("Greeting")]
        [Tooltip("Shown until the conversation has a line in it.")]
        [SerializeField] GameObject greetingBlock;

        [SerializeField] TMP_Text greetingLabel;
        [SerializeField] TMP_Text greetingAvatarLabel;
        [SerializeField] Image greetingAvatar;

        [SerializeField] string greetingFormat = "Hi, I'm {0}.\nHow can I help you today?";

        [Header("Readout")]
        [Tooltip("The four tiles. Hidden on the companion, which has no floor state to report.")]
        [SerializeField] GameObject statsBlock;

        [SerializeField] TMP_Text healthValue;
        [SerializeField] TMP_Text tokensValue;
        [SerializeField] TMP_Text readinessValue;
        [SerializeField] TMP_Text plansValue;

        [Header("Transcript")]
        [Tooltip("Parent the bubbles are created under.")]
        [SerializeField] RectTransform messages;

        [SerializeField] ScrollRect scroller;

        [Tooltip("One row per line the player sent. The blue one.")]
        [SerializeField] ChatBubble playerBubble;

        [Tooltip("One row per line that came back. The dark one.")]
        [SerializeField] ChatBubble agentBubble;

        [Tooltip("One row per question Hiro asks back, with its options as buttons.")]
        [SerializeField] ChatQuestionCard questionCard;

        [Header("Input")]
        [SerializeField] TMP_InputField input;
        [SerializeField] Button sendButton;

        [Tooltip("How many lines the input grows to as the text wraps, before it scrolls instead.")]
        [SerializeField, Min(1)] int maxInputLines = 5;

        [Tooltip("Shown while a reply is being waited on.")]
        [SerializeField] GameObject busyBlock;

        [SerializeField] TMP_Text busyLabel;

        [Tooltip("Cycles the dots on the busy caption. Optional: without one the caption is " +
                 "written once and sits still, which is what it did before.")]
        [SerializeField] EllipsisTextEffect busyDots;

        [Header("History")]
        [Tooltip("How many lines of one conversation to keep when it is reopened. Zero keeps " +
                 "everything, which is fine for a play session and unbounded for a long one.")]
        [SerializeField] int maxRetainedLines = 200;

        [Header("Body layout")]
        [Tooltip("Where the first block under the pills starts, measured down from the top of " +
                 "the window.")]
        [SerializeField] float contentTop = 160f;

        [Tooltip("Gap between the greeting and the readout.")]
        [SerializeField] float blockGap = 10f;

        [Tooltip("Gap between the last block that is showing and the top of the transcript.")]
        [SerializeField] float transcriptGap = 16f;


        /// <summary>
        /// Every agent the player has talked to this session, by whatever identifies them.
        /// </summary>
        /// <remarks>
        /// Kept so walking away and coming back resumes the conversation rather than starting a
        /// new one. It holds the thread id as well as the bubbles, and the id is the half that
        /// matters most: without it the platform is asked to begin a fresh conversation, so the
        /// agent would have forgotten everything even while the old messages were still on screen.
        /// </remarks>
        readonly Dictionary<string, Thread> agentThreads = new Dictionary<string, Thread>(StringComparer.Ordinal);

        /// <summary>The agent side currently on show. Replaced on each <see cref="Open"/>.</summary>
        Thread agent = new Thread();

        /// <summary>
        /// The companion side. One conversation for the whole session, because the companion is
        /// the facility itself rather than somebody the player walked up to.
        /// </summary>
        readonly Thread companion = new Thread();
        // One pool per speaker. A row's side and plate are authored into its prefab, so a row can
        // be reused for another line by the same speaker but never for the other one — and keeping
        // them apart is what lets a redraw reorder rows instead of destroying and rebuilding them.
        readonly List<ChatBubble> playerRows = new List<ChatBubble>();
        readonly List<ChatBubble> agentRows = new List<ChatBubble>();
        readonly List<ChatQuestionCard> questionRows = new List<ChatQuestionCard>();

        // Since the 2026-09-29 swap (see ApplyPills and BackendFor) the left pill, agentPill, holds
        // the Hiro Assistant and the right pill, companionPill, holds the agent. Named here so the
        // code that hides the assistant reads by what the player sees.
        const ChatConversation HiroAssistantChat = ChatConversation.Agent;
        const ChatConversation AgentChat = ChatConversation.Companion;

        // Where the two pills sit as authored, so the agent's can take the first slot while the
        // assistant's is hidden and go back if it is shown again.
        Vector2 firstPillSlot, secondPillSlot;
        bool pillSlotsCaptured;

        IChatBackend backend;
        ICompanionChatBackend companionBackend;
        Action closed;
        CancellationTokenSource pending;
        bool ready;

        // The authored geometry, captured once so maximising is reversible without a second set of
        // numbers to keep in step with the prefab.
        RectTransform windowRect;
        Vector2 restoreAnchorMin, restoreAnchorMax, restorePivot, restorePosition, restoreSize;
        bool restoreCaptured;

        /// <inheritdoc />
        public ChatWindowSize Size { get; private set; } = ChatWindowSize.Normal;

        /// <inheritdoc />
        public bool IsMaximised => Size == ChatWindowSize.Maximised;

        /// <inheritdoc />
        public bool IsMinimised => Size == ChatWindowSize.Minimised;

        /// <inheritdoc />
        public bool IsOpen => root != null && root.activeSelf;

        /// <inheritdoc />
        public ChatConversation Showing { get; private set; } = ChatConversation.Agent;

        /// <summary>
        /// Both correspondents, because the window holds two conversations and they are not with
        /// the same party.
        /// </summary>
        /// <remarks>
        /// The companion's name is read from its own backend rather than the agent's. It used to
        /// come from the agent backend — harmless while both pills reached the same service, and
        /// wrong the moment they stopped.
        /// </remarks>
        [Inject]
        public void Construct(IChatBackend chatBackend, ICompanionChatBackend companionChatBackend)
        {
            backend = chatBackend;
            companionBackend = companionChatBackend;
        }

        /// <summary>Whichever backend answers for a conversation.</summary>
        /// <remarks>
        /// Follows the swapped labels (2026-09-29, see <see cref="ApplyPills"/>): the left pill reads
        /// Hiro Assistant and asks the Hiro assistant (the builder endpoint); the right pill reads
        /// as the agent and asks the agent (<c>/v1/chat/completions</c>).
        /// </remarks>
        IChatBackend BackendFor(ChatConversation which)
            => which == ChatConversation.Companion ? backend : companionBackend;

        void Awake()
        {
            EnsureWired();
        }

        void EnsureWired()
        {
            if (ready)
            {
                return;
            }

            ready = true;

            if (root == null || messages == null || playerBubble == null || agentBubble == null
                || input == null || sendButton == null || closeButton == null)
            {
                Debug.LogError(
                    $"{nameof(ChatAgentWindow)} on '{name}' is missing authored parts — it needs at " +
                    "least root, messages, playerBubble, agentBubble, input, sendButton and " +
                    "closeButton. Assign them on ChatAgentWindow.prefab.",
                    this);
                return;
            }

            closeButton.onClick.AddListener(Dismiss);
            sendButton.onClick.AddListener(Send);
            input.onSubmit.AddListener(OnSubmit);

            // Wraps and grows upward instead of scrolling sideways. The input is its own
            // background here, so it is what grows; the transcript gives up its bottom edge and the
            // busy line rides up above it.
            ChatInputAutoGrow.Attach(
                input,
                input.transform as RectTransform,
                maxInputLines,
                scroller,
                scroller != null ? scroller.transform as RectTransform : null,
                busyBlock != null ? busyBlock.transform as RectTransform : null);

            ApplySorting();
            CaptureWindow();

            if (drag == null && root != null)
            {
                drag = root.GetComponent<ChatWindowDrag>();
            }

            if (maximiseButton != null)
            {
                maximiseButton.onClick.AddListener(Maximise);
            }

            if (minimiseButton != null)
            {
                minimiseButton.onClick.AddListener(Minimise);
            }

            if (agentPill != null)
            {
                agentPill.onClick.AddListener(ShowAgent);
            }

            if (companionPill != null)
            {
                companionPill.onClick.AddListener(ShowCompanion);
            }

            root.SetActive(false);
        }

        void OnDestroy()
        {
            if (closeButton != null) { closeButton.onClick.RemoveListener(Dismiss); }
            if (sendButton != null) { sendButton.onClick.RemoveListener(Send); }
            if (input != null) { input.onSubmit.RemoveListener(OnSubmit); }
            if (agentPill != null) { agentPill.onClick.RemoveListener(ShowAgent); }
            if (companionPill != null) { companionPill.onClick.RemoveListener(ShowCompanion); }
            if (maximiseButton != null) { maximiseButton.onClick.RemoveListener(Maximise); }
            if (minimiseButton != null) { minimiseButton.onClick.RemoveListener(Minimise); }

            Cancel();
        }

        /// <inheritdoc />
        public void Open(ChatContext context, Action onClosed = null)
        {
            EnsureWired();

            if (root == null)
            {
                return;
            }

            if (IsOpen)
            {
                Close();
            }

            closed = onClosed;

            // Resumed rather than started. Walking up to the same agent twice is one conversation
            // as far as the player is concerned, so neither the transcript nor the thread id is
            // thrown away — see <see cref="Remember"/>.
            agent = Remember(context);

            // The companion is the facility itself, so it is described by the backend rather than
            // by a roster entry. No platform agent id: the player is talking to the service, not to
            // something it hosts.
            string facility = companionBackend != null && !string.IsNullOrWhiteSpace(companionBackend.DisplayName)
                ? companionBackend.DisplayName
                : "Companion";

            // Carries the agent's platform id although the companion is not that agent. It is what
            // the facility is being asked about, not who is answering — which is how the companion
            // can say something useful about the one the player is standing next to instead of only
            // about the platform in general.
            //
            // Its own thread id is carried across rather than cleared: the companion is one
            // continuous conversation for the session, and only the agent it is being asked about
            // changes as the player walks around.
            companion.Context = new ChatContext(
                facility, "COMPANION", context.PlatformAgentId, companion.Context.ThreadId);

            Trim(companion);

            ApplyTitle(context);
            ApplyPills(facility);

            // Opened on the agent, because the agent is what the player clicked. Since the swap,
            // that is the right pill's conversation.
            Select(AgentChat);
            Raise();
        }

        /// <summary>
        /// The conversation already held with this agent, or a new one the first time.
        /// </summary>
        /// <remarks>
        /// Keyed on the platform agent id, falling back to the name for a roster entry that has
        /// no platform agent behind it. The name is a weaker key — two agents could share one —
        /// but an agent with no id has no thread to continue either, so the worst it can do is
        /// show one local agent the bubbles of another with the same name.
        /// <para>
        /// The name and department are refreshed from the roster on every visit, because those
        /// can be edited between two conversations. The thread id is deliberately <em>not</em>:
        /// that is the conversation the platform is still holding, and overwriting it with the
        /// caller's null is exactly what used to make the agent forget.
        /// </para>
        /// </remarks>
        Thread Remember(ChatContext context)
        {
            string key = !string.IsNullOrWhiteSpace(context.PlatformAgentId)
                ? context.PlatformAgentId
                : (context.AgentName ?? string.Empty);

            Thread held;

            if (!agentThreads.TryGetValue(key, out held))
            {
                held = new Thread { Context = context };
                agentThreads[key] = held;
                return held;
            }

            held.Context = new ChatContext(
                context.AgentName, context.WorkId, context.PlatformAgentId, held.Context.ThreadId);

            Trim(held);

            return held;
        }

        /// <summary>
        /// Drops the oldest lines of a conversation that has outgrown the cap.
        /// </summary>
        /// <remarks>
        /// Applied when a conversation is reopened rather than on every line, because that is the
        /// point at which a session's worth of history is about to be drawn again. A single very
        /// long sitting is left alone: it is bounded by the player's patience, and trimming
        /// underneath somebody mid-conversation would make their own messages disappear.
        /// </remarks>
        void Trim(Thread thread)
        {
            if (maxRetainedLines <= 0 || thread.Lines.Count <= maxRetainedLines)
            {
                return;
            }

            thread.Lines.RemoveRange(0, thread.Lines.Count - maxRetainedLines);
        }

        /// <summary>Shows the agent's side of the window.</summary>
        public void ShowAgent()
        {
            Select(ChatConversation.Agent);
        }

        /// <summary>Shows the companion's side of the window.</summary>
        public void ShowCompanion()
        {
            Select(ChatConversation.Companion);
        }

        void Select(ChatConversation which)
        {
            // With its pill hidden the assistant's conversation cannot be reached, by a click or by
            // a caller asking for it.
            if (!showHiroAssistant && which == HiroAssistantChat)
            {
                which = AgentChat;
            }

            // A reply still coming belongs to the conversation it was asked in. Letting it land in
            // the other one is how an agent ends up appearing to answer a question about itself.
            Cancel();

            Showing = which;

            if (agentPillSelected != null)
            {
                agentPillSelected.SetActive(which == ChatConversation.Agent);
            }

            if (companionPillSelected != null)
            {
                companionPillSelected.SetActive(which == ChatConversation.Companion);
            }

            // The readout describes an agent standing on the floor. The facility has no health, no
            // readiness and no plans, so the block goes rather than showing four empty tiles.
            if (statsBlock != null)
            {
                statsBlock.SetActive(which == ChatConversation.Agent);
            }

            ApplyGreeting();
            Redraw();
            SetBusy(false, null);
        }

        /// <summary>
        /// Packs the greeting, the readout and the transcript up under the pills, so a block that
        /// is not showing leaves no hole behind it.
        /// </summary>
        /// <remarks>
        /// The body is anchored rather than laid out — every block is positioned against the top
        /// of the window — which is why hiding one used to leave its space reserved. A vertical
        /// layout group would do this by itself, but the body also holds the pills, the input, the
        /// send button and the busy line, all anchored to their own corners; putting a layout
        /// group over that would mean restructuring the authored window rather than moving two
        /// rects.
        /// <para>
        /// The arithmetic reproduces the authored positions exactly in the case they were authored
        /// for — greeting and readout both showing puts the transcript back at its authored top —
        /// so this compacts the other three cases without redesigning the one that was right.
        /// </para>
        /// </remarks>
        void ReflowBody()
        {
            var transcript = scroller != null ? scroller.transform as RectTransform : null;

            if (transcript == null)
            {
                return;
            }

            var greeting = greetingBlock != null ? greetingBlock.transform as RectTransform : null;
            var stats = statsBlock != null ? statsBlock.transform as RectTransform : null;

            // Distance down from the top of the body, following the blocks that are showing.
            float y = contentTop;

            if (greeting != null && greeting.gameObject.activeSelf)
            {
                SetTop(greeting, y);
                y += greeting.rect.height + blockGap;
            }

            if (stats != null && stats.gameObject.activeSelf)
            {
                SetTop(stats, y);
                y += stats.rect.height + blockGap;
            }

            // The trailing block gap comes back off, because what follows is the transcript and it
            // sits at its own distance rather than at a block's.
            float top = y - blockGap + transcriptGap;

            transcript.offsetMax = new Vector2(transcript.offsetMax.x, -top);
        }

        /// <summary>Moves a top-anchored block so its top edge sits <paramref name="top"/> down.</summary>
        static void SetTop(RectTransform rect, float top)
        {
            float height = rect.rect.height;

            rect.offsetMax = new Vector2(rect.offsetMax.x, -top);
            rect.offsetMin = new Vector2(rect.offsetMin.x, -(top + height));
        }

        /// <summary>The conversation on screen.</summary>
        /// <remarks>
        /// Follows the swap the same way <see cref="BackendFor"/> does: the agent's pill holds the
        /// per-agent thread from <see cref="Remember"/>, and the Hiro Assistant's pill holds the one
        /// session-wide thread. Before 2026-10-07 this still followed the old pill names, so every
        /// agent the player talked to shared the assistant's single thread — one thread id, one
        /// transcript.
        /// </remarks>
        Thread Current => Showing == AgentChat ? agent : companion;

        void OnSubmit(string text)
        {
            Send();
        }

        void Send()
        {
            if (input == null)
            {
                return;
            }

            string text = input.text;

            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            input.SetTextWithoutNotify(string.Empty);
            input.ActivateInputField();

            var thread = Current;
            thread.Lines.Add(new Line { Text = text.Trim(), FromPlayer = true });

            ApplyGreeting();
            Redraw();

            _ = AnswerAsync(thread, Showing, text.Trim());
        }

        /// <summary>
        /// Asks the backend, and writes the answer into the conversation it was asked in.
        /// </summary>
        /// <remarks>
        /// The conversation is captured rather than read back from <see cref="Showing"/>, so an
        /// answer that arrives after the player has switched pills still goes to the right place —
        /// and is only drawn when that place is the one on screen.
        /// </remarks>
        async Awaitable AnswerAsync(Thread thread, ChatConversation which, string message)
        {
            var answering = BackendFor(which);

            if (answering == null)
            {
                thread.Lines.Add(new Line { Text = "There is nothing connected to answer.", FromPlayer = false });
                Redraw();
                return;
            }

            Cancel();
            pending = new CancellationTokenSource();
            var token = pending.Token;

            SetBusy(true, "Thinking…");

            var line = new Line { Text = string.Empty, FromPlayer = false };
            thread.Lines.Add(line);

            try
            {
                var reply = await answering.ReplyAsync(
                    thread.Context,
                    message,
                    piece =>
                    {
                        line.Text += piece;
                        if (Showing == which) { Redraw(); }
                    },
                    activity => SetBusy(true, activity),
                    () =>
                    {
                        // The attempt failed and another is coming. What was streamed describes the
                        // failed one, so it goes rather than being left half-written on screen.
                        line.Text = string.Empty;
                        if (Showing == which) { Redraw(); }
                    },
                    token);

                if (token.IsCancellationRequested)
                {
                    return;
                }

                if (!reply.Ok)
                {
                    line.Text = reply.Error;
                }
                else
                {
                    if (!string.IsNullOrEmpty(reply.Text))
                    {
                        line.Text = reply.Text;
                    }

                    // A question replaces the line rather than joining it. The stream that carries
                    // one has no prose at all, so the placeholder is still empty here and would
                    // otherwise be drawn as a blank bubble above the card.
                    if (reply.Question != null)
                    {
                        line.Question = reply.Question;
                        line.Text = string.Empty;
                    }

                    if (!string.IsNullOrEmpty(reply.ThreadId))
                    {
                        thread.Context = thread.Context.WithThread(reply.ThreadId);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                thread.Lines.Remove(line);
            }
            finally
            {
                SetBusy(false, null);

                if (Showing == which)
                {
                    Redraw();
                }
            }
        }

        /// <summary>
        /// An option was clicked. The card becomes the player's message and is sent as one.
        /// </summary>
        /// <remarks>
        /// The line is rewritten in place rather than removed and re-added, so it keeps its
        /// position in the transcript: the question was asked there, and the answer belongs in the
        /// same slot rather than jumping to the end.
        /// <para>
        /// Nothing special is sent. The platform carries no option ids and expects the option's own
        /// text as the next message on the same thread, which is exactly what typing it would do —
        /// so this goes down the ordinary answer path and needs no transport of its own.
        /// </para>
        /// </remarks>
        void OnOptionChosen(Thread thread, ChatConversation which, Line line, string option)
        {
            if (thread == null || line == null || string.IsNullOrEmpty(option))
            {
                return;
            }

            // Guards a second click arriving between the card locking itself and the redraw.
            if (line.Question == null)
            {
                return;
            }

            line.Question = null;
            line.Text = option;
            line.FromPlayer = true;

            if (Showing == which)
            {
                Redraw();
            }

            _ = AnswerAsync(thread, which, option);
        }

        void Cancel()
        {
            if (pending == null)
            {
                return;
            }

            pending.Cancel();
            pending.Dispose();
            pending = null;
        }

        void SetBusy(bool busy, string what)
        {
            if (busyBlock != null)
            {
                busyBlock.SetActive(busy);
            }

            // The animator owns the caption while it runs, dots and all, so the plain write below
            // is only for a window that has no animator wired. Doing both would have the label
            // written twice a frame and the dots never appear.
            if (busyDots != null)
            {
                if (busy)
                {
                    busyDots.Play(what);
                }
                else
                {
                    busyDots.Stop();
                }
            }
            else if (busyLabel != null && busy && !string.IsNullOrEmpty(what))
            {
                busyLabel.text = what;
            }

            if (sendButton != null)
            {
                sendButton.interactable = !busy;
            }
        }

        /// <summary>Draws the conversation showing, reusing rows rather than rebuilding them.</summary>
        /// <remarks>
        /// Rows are taken from the pool for their own speaker and then put in order by sibling
        /// index, which is the order the vertical layout reads. Nothing is destroyed: a destroy is
        /// deferred to the end of the frame, so a row removed here would still be in the list the
        /// layout measured, and the transcript would jump.
        /// </remarks>
        void Redraw()
        {
            if (messages == null || playerBubble == null || agentBubble == null)
            {
                return;
            }

            var lines = Current.Lines;
            var thread = Current;
            var which = Showing;
            float width = RowWidth();
            int players = 0, agents = 0, cards = 0;

            for (int i = 0; i < lines.Count; i++)
            {
                var entry = lines[i];

                // A question is its own kind of row and never shares a pool with the bubbles: the
                // three prefabs differ in side, plate and contents, so a row can only ever be
                // reused for another line of the same kind.
                if (entry.Question != null)
                {
                    if (questionCard == null)
                    {
                        continue;
                    }

                    while (questionRows.Count <= cards)
                    {
                        questionRows.Add(Instantiate(questionCard, messages, false));
                    }

                    var card = questionRows[cards++];
                    card.gameObject.SetActive(true);
                    card.transform.SetSiblingIndex(i);

                    var answered = entry;
                    card.Bind(entry.Question, width, option => OnOptionChosen(thread, which, answered, option));
                    continue;
                }

                bool mine = entry.FromPlayer;
                var rows = mine ? playerRows : agentRows;
                int index = mine ? players++ : agents++;

                while (rows.Count <= index)
                {
                    rows.Add(Instantiate(mine ? playerBubble : agentBubble, messages, false));
                }

                var row = rows[index];
                row.gameObject.SetActive(true);

                // Ascending, so each row lands before the ones still to be placed. Rows switched
                // off below keep their slots, and the layout skips them.
                row.transform.SetSiblingIndex(i);
                row.Bind(entry.Text, width);
            }

            for (int i = players; i < playerRows.Count; i++) { playerRows[i].gameObject.SetActive(false); }
            for (int i = agents; i < agentRows.Count; i++) { agentRows[i].gameObject.SetActive(false); }
            for (int i = cards; i < questionRows.Count; i++) { questionRows[i].gameObject.SetActive(false); }

            if (scroller != null)
            {
                // Rebuilt before scrolling, or the scroll is measured against the layout as it was
                // one line ago and stops just short of what was added.
                LayoutRebuilder.ForceRebuildLayoutImmediate(messages);
                scroller.verticalNormalizedPosition = 0f;
            }
        }

        /// <summary>
        /// How wide a row has to fit into, which is what a bubble takes its share of.
        /// </summary>
        /// <remarks>
        /// Read from the viewport rather than from the content. The content is the thing the layout
        /// is about to resize, so asking it returns whatever the last transcript needed — which at
        /// the first line of a conversation is nothing at all.
        /// </remarks>
        float RowWidth()
        {
            var against = scroller != null && scroller.viewport != null ? scroller.viewport : messages;

            if (against == null)
            {
                return 0f;
            }

            float width = against.rect.width;

            // Nothing has been laid out yet on the first line of a conversation, and a window that
            // has just been resized reports the width it had a moment ago. One forced update costs
            // less than a frame of bubbles measured against a rect that is not there yet.
            if (width <= 1f)
            {
                Canvas.ForceUpdateCanvases();
                width = against.rect.width;
            }

            return width;
        }

        void ApplyTitle(ChatContext context)
        {
            if (titleLabel != null)
            {
                titleLabel.text = context.AgentName;
            }

            if (titleSubtitle != null)
            {
                titleSubtitle.text = (context.WorkId ?? string.Empty).ToUpperInvariant();
            }

            string initials = InitialsOf(context.AgentName);

            if (titleAvatarLabel != null)
            {
                titleAvatarLabel.text = initials;
            }

            if (titleAvatar != null)
            {
                titleAvatar.color = TintFor(context.AgentName);
            }
        }

        /// <remarks>
        /// Swapped on 2026-09-29 at the user's request: the left pill (<c>agentPill</c>) reads as Hiro
        /// Assistant and the right pill (<c>companionPill</c>) reads as the agent. The endpoint
        /// (<see cref="BackendFor"/>) and the thread (<see cref="Current"/>) follow the labels, so the
        /// left pill is the assistant's one session-wide conversation on the builder endpoint, and the
        /// right pill is this agent's own conversation on <c>/v1/chat/completions</c>. See
        /// <see cref="PillName"/> for the greeting.
        /// </remarks>
        void ApplyPills(string facility)
        {
            string agentName = agent.Context.AgentName;

            if (agentPillName != null) { agentPillName.text = facility; }
            if (agentPillRole != null) { agentPillRole.text = "COMPANION"; }
            if (agentPillAvatarLabel != null) { agentPillAvatarLabel.text = CompanionBadge(facility); }
            if (agentPillAvatar != null) { agentPillAvatar.color = TintFor(facility); }

            if (companionPillName != null) { companionPillName.text = agentName; }
            if (companionPillRole != null) { companionPillRole.text = "AGENT"; }
            if (companionPillAvatarLabel != null) { companionPillAvatarLabel.text = InitialsOf(agentName); }
            if (companionPillAvatar != null) { companionPillAvatar.color = TintFor(agentName); }

            LayOutPills();
        }

        /// <summary>
        /// Shows or hides the Hiro Assistant pill. Hidden, the agent's pill moves into the first slot
        /// so the row does not start with a gap.
        /// </summary>
        void LayOutPills()
        {
            if (agentPill == null || companionPill == null)
            {
                return;
            }

            var assistantRect = (RectTransform)agentPill.transform;
            var agentRect = (RectTransform)companionPill.transform;

            if (!pillSlotsCaptured)
            {
                firstPillSlot = assistantRect.anchoredPosition;
                secondPillSlot = agentRect.anchoredPosition;
                pillSlotsCaptured = true;
            }

            agentPill.gameObject.SetActive(showHiroAssistant);
            agentRect.anchoredPosition = showHiroAssistant ? secondPillSlot : firstPillSlot;
        }

        /// <summary>The name on the pill of the conversation showing, for its greeting.</summary>
        string PillName => Current.Context.AgentName;

        /// <summary>
        /// The greeting stands in for an empty transcript, so it goes the moment there is one line.
        /// </summary>
        void ApplyGreeting()
        {
            bool empty = Current.Lines.Count == 0;

            if (greetingBlock != null)
            {
                greetingBlock.SetActive(empty);
            }

            // Whatever is left has to close the gap the greeting left behind.
            ReflowBody();

            if (!empty)
            {
                return;
            }

            string who = PillName;

            if (greetingLabel != null)
            {
                greetingLabel.text = string.Format(greetingFormat, who);
            }

            if (greetingAvatarLabel != null)
            {
                greetingAvatarLabel.text = Showing == ChatConversation.Agent ? CompanionBadge(who) : InitialsOf(who);
            }

            if (greetingAvatar != null)
            {
                greetingAvatar.color = TintFor(who);
            }
        }

        /// <summary>
        /// Sets the four tiles. Left public because what an agent's health and readiness are is not
        /// this window's to work out — it shows what it is told.
        /// </summary>
        public void SetReadout(string health, string tokens, string readiness, string plans)
        {
            if (healthValue != null) { healthValue.text = health; }
            if (tokensValue != null) { tokensValue.text = tokens; }
            if (readinessValue != null) { readinessValue.text = readiness; }
            if (plansValue != null) { plansValue.text = plans; }
        }

        /// <summary>
        /// Puts the window above the other panels on the game canvas.
        /// </summary>
        /// <remarks>
        /// Set here rather than on the prefab because <c>overrideSorting</c> cannot be stored there:
        /// inside the prefab this canvas is the root one, and Unity forces the flag off for a root
        /// canvas. It only becomes a nested canvas once the window is a child of the game canvas,
        /// which is the first moment the setting means anything.
        /// <para>
        /// Applied again from <see cref="Raise"/>, once the window is switched on. A canvas that is
        /// off still reports itself as a root one, so the call from <see cref="Awake"/> found the
        /// window switched off and set nothing. The window then drew at the game canvas's order 0,
        /// under the area labels (5) and the in-game screens (10) — seen 2026-10-07 as the
        /// locator labels bleeding through the chat.
        /// </para>
        /// </remarks>
        void ApplySorting()
        {
            if (root == null)
            {
                return;
            }

            var canvas = root.GetComponent<Canvas>();

            if (canvas == null || canvas.isRootCanvas)
            {
                return;
            }

            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;
        }

        /// <summary>
        /// Remembers the window as it was authored, so maximising can be undone exactly.
        /// </summary>
        /// <remarks>
        /// Read from the prefab rather than written down as a second pair of numbers here. The
        /// window's size is a thing that gets adjusted against a design, and a restore size kept in
        /// code would quietly stop matching the first time it was.
        /// </remarks>
        void CaptureWindow()
        {
            if (restoreCaptured || root == null)
            {
                return;
            }

            windowRect = root.transform as RectTransform;

            if (windowRect == null)
            {
                return;
            }

            restoreAnchorMin = windowRect.anchorMin;
            restoreAnchorMax = windowRect.anchorMax;
            restorePivot = windowRect.pivot;
            restorePosition = windowRect.anchoredPosition;
            restoreSize = windowRect.sizeDelta;
            restoreCaptured = true;
        }

        /// <summary>
        /// What the minimise button does: shrink to the title bar, from whatever size it is at.
        /// </summary>
        /// <remarks>
        /// Straight to minimised from full screen as well, rather than stepping down through normal.
        /// The button is disabled once the window is minimised, so it only ever means one thing and
        /// a step it does not offer to take would be a press that appeared to do the wrong job.
        /// </remarks>
        public void Minimise()
        {
            SetSize(ChatWindowSize.Minimised);
        }

        /// <summary>
        /// What the maximise button does: fill the screen from the authored size, or return to the
        /// authored size from either of the others.
        /// </summary>
        /// <remarks>
        /// One button doing two jobs, which is why it carries two icons. Normal is the only size it
        /// grows from; from minimised and from maximised alike it restores, because at both of those
        /// the thing the player wants back is the window they were given.
        /// <para>
        /// A maximised window used to have no way back to normal at all — the button was disabled
        /// for being "already maximised", which read as finished rather than as one-way, and left
        /// minimise-then-maximise as the only route.
        /// </para>
        /// </remarks>
        public void Maximise()
        {
            SetSize(Size == ChatWindowSize.Normal ? ChatWindowSize.Maximised : ChatWindowSize.Normal);
        }

        /// <inheritdoc />
        public void SetMinimised(bool minimised)
        {
            SetSize(minimised ? ChatWindowSize.Minimised : ChatWindowSize.Normal);
        }

        /// <inheritdoc />
        public void SetMaximised(bool maximised)
        {
            SetSize(maximised ? ChatWindowSize.Maximised : ChatWindowSize.Normal);
        }

        /// <summary>
        /// Puts the window into one of its three sizes.
        /// </summary>
        /// <remarks>
        /// One state rather than two booleans, because two allowed a fourth combination that means
        /// nothing — minimised and maximised at once. Reached by minimising and then maximising, it
        /// left the window stretched to the screen with its body switched off, and the geometry was
        /// applied twice over so the window bled off the edges.
        /// <para>
        /// The whole geometry is written on every change rather than patched from the last one, so
        /// any state is reachable from any other without an order of operations to get right.
        /// </para>
        /// </remarks>
        public void SetSize(ChatWindowSize size)
        {
            CaptureWindow();

            if (windowRect == null)
            {
                return;
            }

            Size = size;

            if (body != null)
            {
                body.SetActive(size != ChatWindowSize.Minimised);
            }

            if (size == ChatWindowSize.Maximised)
            {
                windowRect.anchorMin = Vector2.zero;
                windowRect.anchorMax = Vector2.one;
                windowRect.pivot = new Vector2(0.5f, 0.5f);
                windowRect.offsetMin = new Vector2(maximisedInset, maximisedInset);
                windowRect.offsetMax = new Vector2(-maximisedInset, -maximisedInset);
            }
            else
            {
                windowRect.anchorMin = restoreAnchorMin;
                windowRect.anchorMax = restoreAnchorMax;
                windowRect.pivot = restorePivot;

                if (size == ChatWindowSize.Minimised)
                {
                    windowRect.sizeDelta = new Vector2(minimisedWidth, minimisedHeight);
                    DockBottomRight();
                }
                else
                {
                    windowRect.sizeDelta = restoreSize;
                    windowRect.anchoredPosition = restorePosition;
                }
            }

            // A maximised window is the screen, so there is nothing to drag it to.
            if (drag != null)
            {
                drag.CanDrag = size != ChatWindowSize.Maximised;

                // The size just changed underneath whatever position it had, so a window parked
                // against an edge would now be hanging over it.
                if (size != ChatWindowSize.Maximised)
                {
                    drag.ClampIntoView();
                }
            }

            ApplyButtons();

            // The transcript's width has changed underneath the bubbles, so what was laid out for
            // the old width has to be measured again or long lines keep the shape they had.
            if (size != ChatWindowSize.Minimised && messages != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(messages);

                if (scroller != null)
                {
                    scroller.verticalNormalizedPosition = 0f;
                }
            }
        }

        /// <summary>
        /// Parks a minimised window in the bottom-right corner.
        /// </summary>
        /// <remarks>
        /// Docked on every minimise rather than left where the window happened to be. A title bar
        /// abandoned in the middle of the floor is in the way of the thing the player minimised it
        /// to get at; the corner is the one place it is reliably out of.
        /// <para>
        /// The corner is worked out from the drag clamp rather than from its own numbers, so the
        /// parked position and the furthest a drag may go are the same edge by construction.
        /// </para>
        /// </remarks>
        void DockBottomRight()
        {
            if (drag == null)
            {
                return;
            }

            // The rect has just been resized and the layout has not caught up, so the clamp would
            // measure the old size.
            LayoutRebuilder.ForceRebuildLayoutImmediate(windowRect);

            Vector2 limit = drag.Limit();
            windowRect.anchoredPosition = new Vector2(limit.x, -limit.y);
        }

        /// <summary>
        /// Says what each control would do from here, and withholds the one that would do nothing.
        /// </summary>
        /// <remarks>
        /// Minimise means one thing and is simply not offered once the window is already a title
        /// bar. Maximise means two, so it is always offered and its icon says which — the square
        /// while there is screen left to fill, the stepped-back pair once the only way is back.
        /// <para>
        /// Both icons are drawn rather than typed. The title bar's other two controls are font
        /// glyphs, but there is no restore character in JetBrains Mono to match them with, and a
        /// button whose two states came from different places would not have looked like one button.
        /// </para>
        /// </remarks>
        void ApplyButtons()
        {
            if (minimiseButton != null)
            {
                minimiseButton.interactable = Size != ChatWindowSize.Minimised;
            }

            if (maximiseButton != null)
            {
                maximiseButton.interactable = true;
            }

            if (maximiseIcon != null)
            {
                var wanted = Size == ChatWindowSize.Normal ? maximiseSprite : restoreSprite;

                if (wanted != null)
                {
                    maximiseIcon.sprite = wanted;
                }
            }
        }

        void Dismiss()
        {
            Close();
        }

        /// <summary>Puts the window on screen, at whatever size it was last left.</summary>
        /// <remarks>
        /// No scrim and no captured backdrop, which is the whole difference from a dialog. Nothing
        /// behind this is blocked, so nothing behind it should be dimmed or blurred either — doing
        /// so would make the window look modal while behaving otherwise.
        /// </remarks>
        void Raise()
        {
            if (root != null)
            {
                root.SetActive(true);
            }

            // Only now is the window's canvas nested under the game canvas: see ApplySorting.
            ApplySorting();

            // A window reopened while minimised would come back as a title bar and look broken.
            SetSize(ChatWindowSize.Normal);

            // Focus is taken after the object is on, because a field on a switched-off object
            // cannot take it.
            if (input != null)
            {
                input.Select();
                input.ActivateInputField();
            }
        }

        /// <inheritdoc />
        public void Close()
        {
            Cancel();

            if (root != null)
            {
                root.SetActive(false);
            }

            var callback = closed;
            closed = null;

            if (callback != null)
            {
                callback();
            }
        }

        /// <summary>
        /// The companion's badge: one letter, so Hiro Assistant is H. Agents keep two initials.
        /// </summary>
        public static string CompanionBadge(string facility)
        {
            var name = (facility ?? string.Empty).Trim();
            return name.Length == 0 ? "-" : char.ToUpperInvariant(name[0]).ToString();
        }

        /// <summary>The first letter of each of the first two words, so "Lead qualification" is LQ.</summary>
        static string InitialsOf(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "-";
            }

            var words = name.Split(new[] { ' ', '\t', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);

            if (words.Length >= 2)
            {
                return char.ToUpperInvariant(words[0][0]).ToString() + char.ToUpperInvariant(words[1][0]);
            }

            string word = words[0];
            return word.Length >= 2 ? word.Substring(0, 2).ToUpperInvariant() : word.ToUpperInvariant();
        }

        /// <summary>
        /// A colour for a name, so the same agent is the same colour every time it is shown.
        /// </summary>
        /// <remarks>
        /// Derived from the name rather than from a roster id, because the companion has no id and
        /// still needs a colour of its own.
        /// </remarks>
        static Color TintFor(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return new Color(0.35f, 0.45f, 0.65f, 1f);
            }

            int hash = 17;
            for (int i = 0; i < name.Length; i++)
            {
                hash = hash * 31 + char.ToUpperInvariant(name[i]);
            }

            float hue = Mathf.Abs(hash % 360) / 360f;
            return Color.HSVToRGB(hue, 0.45f, 0.85f);
        }
    }
}
