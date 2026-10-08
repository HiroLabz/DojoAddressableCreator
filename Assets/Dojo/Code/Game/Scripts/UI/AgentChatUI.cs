using System;
using System.Collections.Generic;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace Dojo.Game.UI
{
    /// <summary>
    /// A chat window for talking to one agent: a title bar that can be closed, the agent's
    /// greeting, the conversation so far, and a box to type into.
    /// </summary>
    /// <remarks>
    /// The parts that have a fixed layout — the header, the greeting block, the input row — are
    /// authored in the scene and referenced here, the same way <see cref="AgentDisplayUI"/> uses
    /// its card. The message bubbles are built in code, because there is nothing to author: how
    /// many there are is decided by the conversation, and a bubble is a rounded rect with a label
    /// in it.
    /// <para>
    /// Not an <c>InGamePopup</c>. That base parks a panel off screen and slides it back to an
    /// authored closed position, which is right for a drawer anchored to an edge; a chat window
    /// sits in the middle of the screen and belongs on top of whatever raised it, so it simply
    /// switches on and off.
    /// </para>
    /// <para>
    /// The reply comes from <see cref="IChatBackend"/> and nothing here knows what produces it —
    /// see that interface for why the seam is there rather than a call to something concrete.
    /// </para>
    /// </remarks>
    public sealed class AgentChatUI : MonoBehaviour
    {
        [Header("Window")]
        [Tooltip("The whole window. Switched on by Open and off by Close. Left empty this object " +
                 "is used, which is where the component is expected to live.")]
        [SerializeField] GameObject window;

        [Tooltip("Title bar text. Takes the agent's name plus 'Agent'.")]
        [SerializeField] TMP_Text titleLabel;

        [Tooltip("Closes the window.")]
        [SerializeField] Button closeButton;

        [Tooltip("Grows the window to fill the screen, and puts it back.")]
        [SerializeField] Button expandButton;

        [Tooltip("Title bar wording. {0} is the agent's name, {1} their department.")]
        [SerializeField] string titleFormat = "{0} - {1}";

        [Header("Greeting")]
        [Tooltip("The round avatar's letter. Takes the first letter of the agent's name.")]
        [SerializeField] TMP_Text avatarLabel;

        [Tooltip("The avatar circle itself, tinted per agent so two of them are told apart.")]
        [SerializeField] Image avatar;

        [Tooltip("The opening line under the avatar.")]
        [SerializeField] TMP_Text greetingLabel;

        [Tooltip("Wording of that line. {0} is the agent's name.")]
        [SerializeField] string greetingFormat = "Hi! I'm {0}. How can I help you today?";

        [Tooltip("The greeting block, hidden once the conversation starts so the messages have the " +
                 "whole window.")]
        [SerializeField] GameObject greetingBlock;

        /// <summary>Gap between the top of the message area and the greeting block.</summary>
        const float GreetingTopMargin = 8f;

        /// <summary>
        /// How tall the greeting block is made, in pixels.
        /// </summary>
        /// <remarks>
        /// Enough for the authored avatar and greeting plus the four stat rows under them. It was
        /// 200 in the scene, sized for the greeting alone and centred in the window.
        /// </remarks>
        const float GreetingBlockHeight = 300f;

        /// <summary>Inset each side of the greeting block, matching the authored greeting label.</summary>
        const float GreetingSideInset = 20f;

        /// <summary>
        /// Where the stat rows begin, measured down from the top of the greeting block.
        /// </summary>
        /// <remarks>
        /// Clears the authored greeting above it: the avatar sits at -10 and is 66 tall, and the
        /// greeting label at -92 and is 70 tall, so the greeting ends at -162.
        /// </remarks>
        const float StatsTop = 174f;

        /// <summary>Width reserved for the stat names, so the values line up in a column.</summary>
        const float StatNameWidth = 78f;

        /// <summary>
        /// Shown in a stat row until there is a real value for it.
        /// </summary>
        /// <remarks>
        /// A plain hyphen rather than an em dash, which would read better and render worse: the
        /// fallback font's atlas has no glyph for a good many characters outside ASCII, and one
        /// missing from it is drawn as an empty box. The warning sign this window used to prefix
        /// its errors with was exactly that.
        /// </remarks>
        const string StatPlaceholder = "-";

        // Built once by BuildGreetingStats, which also lifts the greeting to make room for them.
        bool statsBuilt;

        TMP_Text healthValue;
        TMP_Text tokensValue;
        TMP_Text readinessValue;
        TMP_Text plansValue;

        [Header("Conversation")]
        [Tooltip("Parent the message bubbles are built under. The object carrying the " +
                 "VerticalLayoutGroup inside the scroll view.")]
        [SerializeField] RectTransform messages;

        [Tooltip("The scroll view holding the messages, so a new one can be scrolled to.")]
        [SerializeField] ScrollRect scroller;

        [Header("Input")]
        [Tooltip("Where the player types.")]
        [SerializeField] TMP_InputField input;

        [Tooltip("Sends what is typed. Enter does the same thing.")]
        [SerializeField] Button sendButton;

        [Tooltip("The bar the input sits in, which is what grows as the text wraps. Left empty " +
                 "the input's parent is used, which is the footer in the authored window.")]
        [SerializeField] RectTransform inputRow;

        [Tooltip("How many lines the input grows to as the text wraps, before it scrolls instead.")]
        [SerializeField, Min(1)] int maxInputLines = 5;

        [Header("Placement")]
        [Tooltip("Where the window sits relative to the point it was opened at, in canvas units. " +
                 "Up and to the right by default, so it does not cover the two who are talking.")]
        [SerializeField] Vector2 pointOffset = new Vector2(340f, 150f);

        [Tooltip("How close the window may come to the edge of the screen, in canvas units.")]
        [SerializeField] float screenMargin = 16f;

        [Tooltip("Inset from the screen edges while the window is expanded, in canvas units.")]
        [SerializeField] float expandedMargin = 60f;

        [Header("Look")]
        [Tooltip("Bubble behind what the player said.")]
        [SerializeField] Color playerBubble = new Color(0.22f, 0.35f, 0.62f, 1f);

        [Tooltip("Bubble behind what the agent said.")]
        [SerializeField] Color agentBubble = new Color(0.15f, 0.15f, 0.19f, 1f);

        [SerializeField] Color bubbleText = new Color(0.93f, 0.93f, 0.96f, 1f);

        [Tooltip("Point size inside a bubble.")]
        [SerializeField] float bubbleFontSize = 15f;

        [Tooltip("How much of the window's width one bubble may take.")]
        [SerializeField, Range(0.4f, 1f)] float bubbleMaxWidth = 0.78f;

        readonly List<GameObject> bubbles = new List<GameObject>();

        IChatBackend backend;

        // The conversation the platform is keeping for us, handed back with each reply. Null until
        // the first answer, which is what tells the server to start a new one.
        string threadId;

        // The agent's id on Hiro, or null for a local-only entry. Carried so the backend can reach
        // the right agent without the panel having to know what a roster is.
        string platformAgentId;

        // Cancels a reply in flight when the window closes or a new agent is opened. Without it a
        // late answer would arrive into a conversation that had already moved on.
        CancellationTokenSource inFlight;

        // True while an answer is being waited for, so a second Send cannot start a second one.
        bool awaitingReply;

        // The windowed rect, remembered while expanded so it can be put back exactly. Anchors and
        // pivot go with the numbers: expanding changes them to a stretch, and restoring only the
        // size would leave the window stretched at its old dimensions.
        bool expanded;
        Vector2 restoreAnchorMin;
        Vector2 restoreAnchorMax;
        Vector2 restorePivot;
        Vector2 restoreSize;
        Vector2 restorePosition;

        /// <summary>Who the window is currently talking to.</summary>
        public string AgentName { get; private set; }

        /// <summary>Their department, handed to the backend with each message.</summary>
        public string WorkId { get; private set; }

        /// <summary>True while the window is up.</summary>
        public bool IsOpen => Window.activeSelf;

        /// <summary>
        /// Raised as the window closes, carrying whoever it was talking to.
        /// </summary>
        /// <remarks>
        /// The conversation is not the only thing that ends with the window. Whatever opened it
        /// has its own tidying to do — the manager stops following the agent he walked over to, and
        /// the agent is let go to get back to its routine — and this is how it hears about it
        /// without the window having to know either of them exists.
        /// </remarks>
        public event Action<string> Closed;

        /// <summary>
        /// The window, resolved on demand.
        /// </summary>
        /// <remarks>
        /// Not read straight out of the field, because <see cref="Close"/> can be reached before
        /// <c>Awake</c> has filled it in — the same ordering trap the agents card fell into, where
        /// a panel on one object is driven by a component on another.
        /// </remarks>
        GameObject Window => window != null ? window : gameObject;

        /// <summary>
        /// Receives whatever answers the player.
        /// </summary>
        /// <remarks>
        /// Injected rather than looked up, and it has to be: the window is authored in the scene,
        /// so nothing was ever handing it a backend and it fell through to
        /// <see cref="EchoChatBackend"/> every time. That stand-in is honest about being one — it
        /// repeats the message back — which is exactly what the chat was doing on screen while a
        /// perfectly good <c>HiroChatBackend</c> sat registered in the container, unused.
        /// <para>
        /// The container hands over whichever <see cref="IChatBackend"/> is registered, so this
        /// class still knows nothing about HTTP, agents or the platform.
        /// </para>
        /// </remarks>
        [Inject]
        public void Construct(IChatBackend replies)
        {
            Use(replies);
        }

        /// <summary>
        /// Replaces what answers the player. Called by whatever owns the conversation; until it is,
        /// the echo stand-in answers.
        /// </summary>
        public void Use(IChatBackend replies)
        {
            if (replies != null)
            {
                backend = replies;
            }
        }

        void Awake()
        {
            if (window == null)
            {
                window = gameObject;
            }

            if (closeButton != null)
            {
                closeButton.onClick.AddListener(Close);
            }

            if (sendButton != null)
            {
                sendButton.onClick.AddListener(Send);
            }

            if (expandButton != null)
            {
                expandButton.onClick.AddListener(ToggleExpanded);
            }

            if (input != null)
            {
                // Enter sends. onSubmit rather than watching for the key, so it follows whatever
                // the platform considers submit and does not fire while the field is unfocused.
                input.onSubmit.AddListener(_ => Send());

                // Wraps and grows upward instead of scrolling sideways. The footer carries the
                // background here and the input fills it, so the footer is what grows; the
                // conversation gives up its bottom edge to make room.
                ChatInputAutoGrow.Attach(
                    input,
                    inputRow != null ? inputRow : input.transform.parent as RectTransform,
                    maxInputLines,
                    scroller,
                    scroller != null ? scroller.transform as RectTransform : null);
            }

            BuildGreetingStats();

            // Deliberately does not hide the window. It is authored inactive, so this Awake does
            // not run at scene load — it runs the first time something activates it, which is
            // Open. Unity runs Awake synchronously inside SetActive(true), so hiding here would
            // fire in the middle of the call that was raising the window and take it back down.
        }

        /// <summary>
        /// Lifts the greeting to the top of the window and builds the stat rows underneath it.
        /// </summary>
        /// <remarks>
        /// Built in code rather than authored, the same trade the message bubbles make: four
        /// label-and-value rows are nothing to author, and a prefab for them would be a second
        /// place the colours and sizes have to be kept in step with the bubbles beside them.
        /// <para>
        /// It lives inside <c>greetingBlock</c> on purpose. That block is hidden the moment the
        /// first message lands and put back by <see cref="Clear"/>, so the stats appear exactly
        /// where they are wanted — on the opening screen — and take none of the room once there is
        /// a conversation to read.
        /// </para>
        /// <para>
        /// Guarded so it only ever builds once. <c>Awake</c> runs when the window is first
        /// activated rather than at scene load, and nothing stops something calling it again.
        /// </para>
        /// </remarks>
        void BuildGreetingStats()
        {
            if (statsBuilt || greetingBlock == null)
            {
                return;
            }

            statsBuilt = true;

            LiftGreeting();

            var column = new GameObject("Stats", typeof(RectTransform));
            column.transform.SetParent(greetingBlock.transform, false);

            // Stretched across the block and pinned below the greeting, so the rows keep their
            // place whatever width the window is dragged to.
            var rect = (RectTransform)column.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -StatsTop);
            rect.sizeDelta = new Vector2(-GreetingSideInset * 2f, 0f);

            var layout = column.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 4f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            // The column grows to fit however many rows there are rather than being given a
            // height, so adding a fifth stat later needs no numbers changed.
            column.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            healthValue = AddStatRow(column.transform, "Health:");
            tokensValue = AddStatRow(column.transform, "Tokens:");
            readinessValue = AddStatRow(column.transform, "Readiness:");
            plansValue = AddStatRow(column.transform, "Plans:");
        }

        /// <summary>
        /// Moves the greeting block to the top of the window from the middle.
        /// </summary>
        /// <remarks>
        /// The block was authored centred in the message area, which put the avatar and the
        /// greeting halfway down and left the whole lower half of an empty chat blank. The stats
        /// go into that space, so the greeting moves up to make room for them.
        /// <para>
        /// Done here rather than in the scene because the two belong together: the rows below are
        /// built in code and positioned relative to this block, so a height set in the inspector
        /// and a row offset set in code would be two halves of one layout that nothing keeps in
        /// step. The avatar and the greeting label are left exactly as authored — both are already
        /// anchored to the top of this block, so lifting the block lifts them with it.
        /// </para>
        /// </remarks>
        void LiftGreeting()
        {
            var rect = greetingBlock.transform as RectTransform;

            if (rect == null)
            {
                return;
            }

            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -GreetingTopMargin);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, GreetingBlockHeight);
        }

        /// <summary>
        /// One "Label: value" row.
        /// </summary>
        /// <returns>The value label, so a caller can fill it in later.</returns>
        /// <remarks>
        /// The name is dimmed and the value is not, which is what lets the eye run down the values
        /// rather than reading eight words per line. Both are one row so they share a baseline —
        /// two separate columns would drift apart the moment one value wrapped.
        /// </remarks>
        TMP_Text AddStatRow(Transform parent, string label)
        {
            var row = new GameObject(label.TrimEnd(':'), typeof(RectTransform));
            row.transform.SetParent(parent, false);

            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            row.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var dim = bubbleText;
            dim.a *= 0.6f;

            var name = NewStatLabel(row.transform, "Name", label, dim);
            name.alignment = TextAlignmentOptions.TopLeft;

            // A fixed width for the names, so every value starts at the same x and the column
            // reads as a column. Without it each value sits hard against a name of a different
            // length and the numbers stagger.
            var nameWidth = name.gameObject.AddComponent<LayoutElement>();
            nameWidth.preferredWidth = StatNameWidth;
            nameWidth.flexibleWidth = 0f;

            var value = NewStatLabel(row.transform, "Value", StatPlaceholder, bubbleText);
            value.alignment = TextAlignmentOptions.TopLeft;

            var fill = value.gameObject.AddComponent<LayoutElement>();
            fill.flexibleWidth = 1f;

            return value;
        }

        /// <summary>A text object styled to sit alongside the message bubbles.</summary>
        TMP_Text NewStatLabel(Transform parent, string objectName, string text, Color colour)
        {
            var label = new GameObject(objectName, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            label.transform.SetParent(parent, false);
            label.text = text;
            label.fontSize = bubbleFontSize - 2f;
            label.color = colour;

            // Off, unlike the bubbles. Nothing here is authored by a person and none of it wants
            // markup, so a value that happened to contain a "<" should show it rather than have it
            // swallowed as a tag.
            label.richText = false;

            return label;
        }

        /// <summary>
        /// Fills in the stat rows. Anything left null keeps whatever is already shown.
        /// </summary>
        /// <remarks>
        /// Public and string-typed because the panel is not the thing that should decide how a
        /// token count or a readiness score is worded — that belongs with whatever fetched it.
        /// Passing null rather than an empty string is how a caller updates one row without
        /// blanking the other three, which matters when the four come from four different calls
        /// that land at different times.
        /// </remarks>
        public void SetStats(
            string health = null,
            string tokens = null,
            string readiness = null,
            string plans = null)
        {
            Fill(healthValue, health);
            Fill(tokensValue, tokens);
            Fill(readinessValue, readiness);
            Fill(plansValue, plans);
        }

        /// <summary>Puts text in one row, or leaves it alone when there is nothing to say.</summary>
        static void Fill(TMP_Text label, string text)
        {
            if (label != null && text != null)
            {
                label.text = string.IsNullOrEmpty(text) ? StatPlaceholder : text;
            }
        }

        /// <summary>Puts every stat back to the placeholder, for a window opening on a new agent.</summary>
        void ResetStats()
        {
            Fill(healthValue, StatPlaceholder);
            Fill(tokensValue, StatPlaceholder);
            Fill(readinessValue, StatPlaceholder);
            Fill(plansValue, StatPlaceholder);
        }

        /// <summary>
        /// Opens the window on a fresh conversation with <paramref name="agentName"/>.
        /// </summary>
        /// <remarks>
        /// The window goes up first, before anything that could decide there is nothing to show —
        /// a window that silently fails to appear is indistinguishable from a broken button.
        /// </remarks>
        public void Open(string agentName, string workId = null, Color? tint = null,
            string hiroAgentId = null)
        {
            Window.SetActive(true);

            // A reply for the previous agent must not land in this one's window.
            Abandon();

            platformAgentId = hiroAgentId;
            threadId = null;

            AgentName = string.IsNullOrEmpty(agentName) ? "Agent" : agentName;
            WorkId = workId;

            if (titleLabel != null)
            {
                // No department is a real state - an agent authored into a scene rather than
                // placed from the roster has none - and "Agent1 - " with nothing after the dash
                // looks like a bug rather than an absence, so the dash goes with it.
                titleLabel.text = string.IsNullOrEmpty(WorkId)
                    ? AgentName
                    : string.Format(titleFormat, AgentName, WorkId);
            }

            if (avatarLabel != null)
            {
                avatarLabel.text = AgentName.Substring(0, 1).ToUpperInvariant();
            }

            if (avatar != null && tint.HasValue)
            {
                avatar.color = tint.Value;
            }

            if (greetingLabel != null)
            {
                greetingLabel.text = string.Format(greetingFormat, AgentName);
            }

            // Blanked rather than left standing. The window is reused for whichever agent the
            // manager met last, and one agent's token count under another's name is worse than no
            // number at all.
            ResetStats();

            Clear();

            if (input != null)
            {
                input.text = string.Empty;
                input.ActivateInputField();
            }
        }

        /// <summary>
        /// Opens the window beside a point in the world — where the manager met the agent.
        /// </summary>
        public void OpenAt(Vector3 worldPoint, string agentName, string workId = null,
            Color? tint = null, string hiroAgentId = null)
        {
            Open(agentName, workId, tint, hiroAgentId);
            PlaceNear(worldPoint);
        }

        /// <summary>
        /// Fills the screen with the window, or puts it back where it was.
        /// </summary>
        /// <remarks>
        /// The windowed rect is remembered rather than recomputed, because there is nothing to
        /// recompute it from: it was placed beside a meeting that has since been walked away from,
        /// and a window that jumped somewhere else on collapse would be worse than one that did
        /// not expand at all. Anchors and pivot are saved with the numbers — expanding replaces
        /// them with a stretch, and restoring only the size would leave the window stretched at
        /// its old dimensions.
        /// </remarks>
        public void ToggleExpanded() => SetExpanded(!expanded);

        /// <summary>Expands or restores the window, doing nothing if it is already that way.</summary>
        public void SetExpanded(bool wanted)
        {
            var rect = Window.transform as RectTransform;

            if (rect == null || wanted == expanded)
            {
                return;
            }

            if (wanted)
            {
                restoreAnchorMin = rect.anchorMin;
                restoreAnchorMax = rect.anchorMax;
                restorePivot = rect.pivot;
                restoreSize = rect.sizeDelta;
                restorePosition = rect.anchoredPosition;

                // Stretched to the parent with an inset, so it follows the screen at any
                // resolution rather than being sized to one.
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.offsetMin = new Vector2(expandedMargin, expandedMargin);
                rect.offsetMax = new Vector2(-expandedMargin, -expandedMargin);
            }
            else
            {
                rect.anchorMin = restoreAnchorMin;
                rect.anchorMax = restoreAnchorMax;
                rect.pivot = restorePivot;
                rect.sizeDelta = restoreSize;
                rect.anchoredPosition = restorePosition;
            }

            expanded = wanted;

            Recap();
        }

        /// <summary>
        /// Re-measures the bubbles already in the conversation against the window's new width.
        /// </summary>
        /// <remarks>
        /// A bubble's width cap is worked out once, when it is built, from the width the message
        /// list had then. Expanding the window doubles that width — so without this the old
        /// bubbles keep their old narrow wrapping while new ones use the whole room, which reads as
        /// the conversation having two different layouts in it.
        /// </remarks>
        void Recap()
        {
            if (messages == null)
            {
                return;
            }

            // The list's own width is what the cap is a fraction of, and it has only just changed.
            LayoutRebuilder.ForceRebuildLayoutImmediate(messages);

            foreach (var row in bubbles)
            {
                if (row == null)
                {
                    continue;
                }

                var label = row.GetComponentInChildren<TMP_Text>(true);
                var cap = row.GetComponentInChildren<LayoutElement>(true);

                if (label != null && cap != null)
                {
                    cap.preferredWidth = CapFor(label, label.text);
                }
            }

            ScrollToEnd();
        }

        /// <summary>
        /// How wide a bubble holding <paramref name="text"/> should ask to be.
        /// </summary>
        /// <remarks>
        /// A cap, not a width. Setting the preferred width to the maximum makes every bubble that
        /// wide — "hi" got one across the whole window — because a <see cref="LayoutElement"/>
        /// states a preference rather than a limit. So the text is measured against the cap first
        /// and the smaller of the two is asked for: short messages hug their words, long ones wrap.
        /// </remarks>
        float CapFor(TMP_Text label, string text)
        {
            const float sidePadding = 28f;   // the 14 either side set on the bubble's layout group

            var room = messages.rect.width * bubbleMaxWidth;
            var wanted = label.GetPreferredValues(text, room - sidePadding, 0f).x + sidePadding;

            return Mathf.Min(wanted, room);
        }

        /// <summary>
        /// Moves the window beside <paramref name="worldPoint"/>, kept wholly on screen.
        /// </summary>
        /// <remarks>
        /// Two steps, and the second is the one that matters. The world point is projected into the
        /// canvas and offset so the window sits beside the pair rather than on top of them — but a
        /// collision near an edge would put most of the window past it, and on an isometric camera
        /// that happens the moment the manager walks to the far side of the room. So the result is
        /// clamped to the canvas by the window's own half-size: it slides along the edge instead of
        /// hanging off it, which is what keeps the whole thing reachable.
        /// <para>
        /// Clamping needs the anchors centred, because that is what makes an anchored position and
        /// a point in the canvas's own space the same number. Set here rather than trusted, since a
        /// window dragged around in the inspector could easily have been re-anchored.
        /// </para>
        /// </remarks>
        public void PlaceNear(Vector3 worldPoint)
        {
            // A window filling the screen has no "beside" to be moved to, and repositioning it
            // would silently undo the stretch. A later meeting keeps the expanded view.
            if (expanded)
            {
                return;
            }

            var rect = Window.transform as RectTransform;
            var canvas = Window.GetComponentInParent<Canvas>();

            if (rect == null || canvas == null)
            {
                return;
            }

            var area = canvas.transform as RectTransform;

            if (area == null)
            {
                return;
            }

            // Overlay canvases take a null camera; every other mode wants the one rendering it.
            var uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : canvas.worldCamera;

            var view = Camera.main != null ? Camera.main : uiCamera;

            if (view == null)
            {
                return;
            }

            var screen = RectTransformUtility.WorldToScreenPoint(view, worldPoint);

            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(area, screen, uiCamera, out local))
            {
                return;
            }

            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            // The margin is taken off the screen rather than added to the window, so a window that
            // only just fits still lands inside instead of being treated as too big.
            var inside = area.rect;
            inside.xMin += screenMargin;
            inside.xMax -= screenMargin;
            inside.yMin += screenMargin;
            inside.yMax -= screenMargin;

            rect.anchoredPosition = Clamp(local + pointOffset, rect.rect.size, inside);
        }

        /// <summary>
        /// <paramref name="wanted"/> pulled far enough inside <paramref name="area"/> that a window
        /// of <paramref name="size"/> fits entirely within it.
        /// </summary>
        /// <remarks>
        /// A window larger than the screen is centred rather than clamped. Clamp would be handed a
        /// minimum above its maximum and would return whichever it checked last, which is a
        /// position off screen in one direction or the other — the one case where the guard meant
        /// to keep the window visible would be what pushed it out of sight.
        /// </remarks>
        static Vector2 Clamp(Vector2 wanted, Vector2 size, Rect area)
        {
            var half = size * 0.5f;

            var minX = area.xMin + half.x;
            var maxX = area.xMax - half.x;
            var minY = area.yMin + half.y;
            var maxY = area.yMax - half.y;

            return new Vector2(
                minX > maxX ? area.center.x : Mathf.Clamp(wanted.x, minX, maxX),
                minY > maxY ? area.center.y : Mathf.Clamp(wanted.y, minY, maxY));
        }

        /// <summary>Puts the window away. The conversation is not kept.</summary>
        public void Close()
        {
            var was = AgentName;

            // Put back to a window before it goes, so the next meeting opens beside the two of
            // them rather than filling the screen because of a choice made a conversation ago.
            SetExpanded(false);

            Clear();
            Window.SetActive(false);

            var handler = Closed;
            if (handler != null)
            {
                handler(was);
            }
        }

        /// <summary>
        /// Sends whatever is typed, and appends the reply.
        /// </summary>
        /// <remarks>
        /// The field is cleared and refocused before the reply is asked for, so a backend that
        /// takes a moment cannot leave the player's own words sitting in the box looking unsent.
        /// </remarks>
        public void Send()
        {
            if (input == null || awaitingReply)
            {
                // Already waiting. A second send would interleave two answers into one
                // conversation, and the agent has not finished the first.
                return;
            }

            var said = input.text;

            if (string.IsNullOrEmpty(said) || string.IsNullOrEmpty(said.Trim()))
            {
                return;   // nothing typed; an empty bubble is worse than no bubble
            }

            input.text = string.Empty;
            input.ActivateInputField();

            Say(said, true);

            _ = AskAsync(said);
        }

        /// <summary>
        /// Puts the question, then grows the answer as it arrives.
        /// </summary>
        /// <remarks>
        /// An empty bubble is opened before the first word and filled in by
        /// <paramref name="onToken"/>, so the window shows the agent talking rather than sitting
        /// blank for the several seconds a real reply takes. The finished text from the backend
        /// then replaces what was streamed: the tokens are for showing progress, and the server is
        /// the authority on what it actually said.
        /// <para>
        /// Not awaited by <see cref="Send"/>, because a button handler cannot be. The guard against
        /// a second send is <c>awaitingReply</c> rather than the returned task.
        /// </para>
        /// </remarks>
        async Awaitable AskAsync(string said)
        {
            var replies = backend ?? (backend = new EchoChatBackend());

            inFlight = new CancellationTokenSource();
            var token = inFlight.Token;

            awaitingReply = true;
            SetSendEnabled(false);

            GameObject growing = null;
            var streamed = new System.Text.StringBuilder();

            try
            {
                var context = new ChatContext(AgentName, WorkId, platformAgentId, threadId);

                var reply = await replies.ReplyAsync(context, said, piece =>
                {
                    streamed.Append(piece);

                    if (growing == null)
                    {
                        growing = Say(streamed.ToString(), false);
                    }
                    else
                    {
                        Retext(growing, streamed.ToString());
                    }
                }, doing =>
                {
                    // What the agent is up to, shown in the bubble until the first word of the real
                    // answer replaces it. Only while nothing has streamed yet: once tokens are
                    // arriving the answer itself is the better progress indicator, and overwriting
                    // it with "Prepared the response" would delete text the player is reading.
                    if (streamed.Length > 0)
                    {
                        return;
                    }

                    var note = "<i>" + doing + "…</i>";

                    if (growing == null)
                    {
                        growing = Say(note, false);
                    }
                    else
                    {
                        Retext(growing, note);
                    }
                }, () =>
                {
                    // One attempt failed and another is starting. Everything it drew goes with it:
                    // the bubble, and the streamed text behind it. Without the reset the next
                    // attempt's first token would be appended to a half-written answer from the
                    // failed one, and the player would watch two replies grow into each other.
                    if (growing != null)
                    {
                        Discard(growing);
                        growing = null;
                    }

                    streamed.Clear();
                }, token);

                if (reply.Ok)
                {
                    threadId = reply.ThreadId ?? threadId;

                    if (growing == null)
                    {
                        Say(reply.Text, false);
                    }
                    else
                    {
                        Retext(growing, reply.Text);
                    }
                }
                else
                {
                    // No warning glyph. U+26A0 is not in the fallback font's atlas, so it rendered
                    // as a tofu box in front of every message it was meant to decorate.
                    var complaint = reply.Error;

                    if (growing == null)
                    {
                        Say(complaint, false);
                    }
                    else
                    {
                        Retext(growing, complaint);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // The window closed or moved to another agent. Whatever was half-written goes with
                // it; there is nobody left to show it to.
            }
            catch (Exception error)
            {
                // A backend is allowed to be broken without taking the panel down with it. The
                // player is told something went wrong, and the console gets the detail.
                Debug.LogError("[Chat] " + AgentName + " backend threw: " + error, this);

                if (growing == null)
                {
                    Say("Something went wrong reaching " + AgentName + ".", false);
                }
            }
            finally
            {
                awaitingReply = false;
                SetSendEnabled(true);
            }
        }

        /// <summary>Replaces the text in a bubble already on screen, and re-measures it.</summary>
        /// <remarks>
        /// The width cap was worked out from the text the bubble was built with, so growing the
        /// text without recomputing it leaves a streamed answer wrapping at the width of its first
        /// word.
        /// </remarks>
        void Retext(GameObject row, string text)
        {
            if (row == null || string.IsNullOrEmpty(text))
            {
                return;
            }

            var label = row.GetComponentInChildren<TMP_Text>(true);
            var cap = row.GetComponentInChildren<LayoutElement>(true);

            if (label == null)
            {
                return;
            }

            label.text = text;

            if (cap != null)
            {
                cap.preferredWidth = CapFor(label, text);
            }

            ScrollToEnd();
        }

        /// <summary>
        /// Greys the send button while an answer is on its way.
        /// </summary>
        /// <remarks>
        /// The guard is <c>awaitingReply</c>; this only shows it. Both are wanted: the flag is what
        /// makes a second send impossible, and the button is what stops the player trying.
        /// </remarks>
        void SetSendEnabled(bool on)
        {
            if (sendButton != null)
            {
                sendButton.interactable = on;
            }
        }

        /// <summary>Drops a reply in flight, so a late answer cannot land in the wrong window.</summary>
        void Abandon()
        {
            if (inFlight != null)
            {
                inFlight.Cancel();
                inFlight.Dispose();
                inFlight = null;
            }

            awaitingReply = false;
            SetSendEnabled(true);
        }

        /// <summary>
        /// Appends one message bubble.
        /// </summary>
        /// <remarks>
        /// Built in code rather than cloned from a prefab. A bubble is a rounded rect and a label,
        /// and a prefab for it would be a second thing to keep in step with these colours — the
        /// same reasoning the agent tiles are built by hand.
        /// <para>
        /// The greeting goes as soon as the first message lands, so the conversation has the whole
        /// window rather than being squeezed under an introduction nobody needs twice.
        /// </para>
        /// </remarks>
        /// <returns>
        /// The row that was added, so a streamed answer can keep growing the same bubble instead
        /// of appending one per token. Null when nothing was added.
        /// </returns>
        public GameObject Say(string text, bool fromPlayer)
        {
            if (messages == null || string.IsNullOrEmpty(text))
            {
                return null;
            }

            if (greetingBlock != null && greetingBlock.activeSelf)
            {
                greetingBlock.SetActive(false);
            }

            // A row spanning the width, so the bubble inside it can sit left or right without the
            // layout group having to know which.
            var row = new GameObject(fromPlayer ? "Said" : "Replied", typeof(RectTransform));
            row.transform.SetParent(messages, false);

            var rowGroup = row.AddComponent<HorizontalLayoutGroup>();
            rowGroup.childAlignment = fromPlayer ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
            rowGroup.childForceExpandWidth = false;
            rowGroup.childForceExpandHeight = false;
            row.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Escaped here rather than trusted. Rich text is on for the italic progress note, and
            // a player who types "<b>" would otherwise see it interpreted instead of shown - or,
            // with an unclosed tag, lose the rest of the message entirely.
            if (fromPlayer)
            {
                text = text.Replace("<", "<noparse><</noparse>");
            }

            var bubble = new GameObject("Bubble", typeof(RectTransform));
            bubble.transform.SetParent(row.transform, false);

            var background = bubble.AddComponent<Image>();
            background.sprite = ChatSprites.Rounded;
            background.type = Image.Type.Sliced;
            background.color = fromPlayer ? playerBubble : agentBubble;

            var padding = bubble.AddComponent<HorizontalLayoutGroup>();
            padding.padding = new RectOffset(14, 14, 9, 9);
            padding.childForceExpandWidth = false;
            padding.childForceExpandHeight = false;

            var fitter = bubble.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var cap = bubble.AddComponent<LayoutElement>();
            cap.flexibleWidth = 0f;

            var label = new GameObject("Text", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            label.transform.SetParent(bubble.transform, false);
            label.text = text;
            label.fontSize = bubbleFontSize;
            label.color = bubbleText;
            label.alignment = TextAlignmentOptions.TopLeft;
            // Rich text on, so the greyed "…thinking" placeholder can be italic. The player's own
            // words are escaped before they ever reach a bubble - see Say - so typing < or > still
            // shows the character rather than swallowing the line as a tag.
            label.richText = true;

            cap.preferredWidth = CapFor(label, text);

            bubbles.Add(row);

            ScrollToEnd();

            return row;
        }

        /// <summary>
        /// Puts the view at the newest message.
        /// </summary>
        /// <remarks>
        /// After a layout rebuild, not before. The scroll position is a fraction of a content
        /// height that the bubble just added has changed, and reading it in the same frame the
        /// bubble was built lands on the second-to-last message.
        /// </remarks>
        void ScrollToEnd()
        {
            if (scroller == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(messages);
            scroller.verticalNormalizedPosition = 0f;
        }

        /// <summary>Takes the conversation away and puts the greeting back.</summary>
        /// <summary>
        /// Takes one bubble back out of the conversation, as though it had never been said.
        /// </summary>
        /// <remarks>
        /// For an answer that was being streamed when its attempt failed. Removed from
        /// <see cref="bubbles"/> as well as destroyed, because that list is what a width change
        /// re-measures and what <see cref="Clear"/> walks — a destroyed object left in it is a null
        /// every one of those has to keep stepping over.
        /// <para>
        /// Unparented before being destroyed for the same reason <see cref="Clear"/> does it:
        /// <c>Destroy</c> is deferred to the end of the frame, and until then the layout group
        /// keeps arranging a bubble that is on its way out.
        /// </para>
        /// </remarks>
        void Discard(GameObject row)
        {
            if (row == null)
            {
                return;
            }

            bubbles.Remove(row);

            if (Application.isPlaying)
            {
                row.transform.SetParent(null, false);
                Destroy(row);
            }
            else
            {
                DestroyImmediate(row);
            }
        }

        void Clear()
        {
            foreach (var bubble in bubbles)
            {
                if (bubble == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    // Unparented before being destroyed: Destroy is deferred to the end of the
                    // frame, so without this the layout group spends a frame arranging the old
                    // bubbles alongside the new ones and the list visibly jumps.
                    bubble.transform.SetParent(null, false);
                    Destroy(bubble);
                }
                else
                {
                    DestroyImmediate(bubble);
                }
            }

            bubbles.Clear();

            if (greetingBlock != null)
            {
                greetingBlock.SetActive(true);
            }
        }
    }
}
