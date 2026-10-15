using System;
using System.Collections.Generic;
using Dojo.Framework.Events;
using Dojo.Framework.UI;
using Dojo.Game.Events;
using Dojo.Game.InGame.Agents;
using Dojo.Game.InGame.Controllers;
using Dojo.Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace Dojo.Game.UI
{
    /// <summary>
    /// The first-time tour: a card along the bottom of the game screen for each thing worth
    /// knowing, most of them finishing by themselves when the player does what they ask.
    /// </summary>
    /// <remarks>
    /// A canvas of its own rather than a popup, one step below the popup layer
    /// (<see cref="PopupManager.SortingOrder"/> - 2): above the game and its menus, and under
    /// anything a popup has to say. Brought in and taken out with LeanTween - the layer fades and
    /// the card slides - and the canvas switched off once it is out. That includes whenever a
    /// dialog or an agent chat is up, so it never sits over what a step has sent the player to.
    /// Between two cards only the card fades, so the new words never pop in over the old.
    /// <para>
    /// The card is scaled as a whole, so its text, buttons and icon grow together: larger than
    /// authored with a mouse, larger still on a touchscreen, and never wider than the screen.
    /// </para>
    /// <para>
    /// Runs as a state machine, one path for every step: see <see cref="FtueState"/> for the path
    /// and <see cref="FtueFlow.Next"/> for the rule. Each frame this reads the signals, asks
    /// <see cref="FtueFlow"/> where to go, and <see cref="Enter"/> does what arriving there means.
    /// Nothing else moves the tour but the two buttons.
    /// </para>
    /// <para>
    /// Each step's goal is read from the system that owns it - the camera rig, the chat windows,
    /// the menu, the game phase, the world-changed event and the world's save - so a step ends
    /// because the thing happened, not because the player said so. NEXT still works throughout.
    /// </para>
    /// <para>
    /// The spotlight is four dim panels around a hole rather than a mask: the hole is simply where
    /// nothing is drawn, so the button - or the agent - it frames can still be clicked through it.
    /// It follows its target every frame, because the target moves: the menu's entries slide up as
    /// it opens, and an agent walks about. An agent out of view drops the spotlight until they are
    /// back.
    /// </para>
    /// <para>
    /// Shown once per player, and resumed at the step it was left on: both are kept in PlayerPrefs,
    /// which CLEAR ALL wipes. Built by <c>Tools ▸ Dojo ▸ Build FTUE Overlay</c>, which fills in every
    /// reference below.
    /// </para>
    /// </remarks>
    public sealed class FtueTour : MonoBehaviour
    {
        const string DoneKey = "dojo.ftue.tour.done";
        const string StepKey = "dojo.ftue.tour.step";

        /// <summary>Seconds a finished step stays up before the next, so the player sees it land.</summary>
        const float CompletionBeat = 0.8f;

        /// <summary>Seconds after the loading screen goes before the first card, so the world is standing.</summary>
        const float SettleTime = 1f;

        [Header("Layer")]
        [SerializeField] Canvas canvas;
        [SerializeField] GraphicRaycaster raycaster;
        [SerializeField] RectTransform root;

        [Header("Spotlight")]
        [SerializeField] RectTransform dimTop;
        [SerializeField] RectTransform dimBottom;
        [SerializeField] RectTransform dimLeft;
        [SerializeField] RectTransform dimRight;
        [SerializeField] Image frame;
        [SerializeField] float holePadding = 12f;
        [SerializeField] float frameSpread = 16f;

        [Header("Card")]
        [SerializeField] RectTransform card;
        [SerializeField] Image cardAccent;
        [SerializeField] RectTransform notch;
        [SerializeField] Image iconPlate;
        [SerializeField] Image icon;
        [SerializeField] TMP_Text eyebrow;
        [SerializeField] TMP_Text title;
        [SerializeField] TMP_Text body;
        [SerializeField] Image[] dots = new Image[0];
        [SerializeField] Button secondary;
        [SerializeField] TMP_Text secondaryLabel;
        [SerializeField] Button primary;
        [SerializeField] TMP_Text primaryLabel;

        [Tooltip("The glow behind the primary button, dropped while the button is off.")]
        [SerializeField] Image primaryGlow;

        [SerializeField] float cardMargin = 40f;

        [Tooltip("Space between a spotlit target and the card beside it.")]
        [SerializeField] float cardGap = 56f;

        [Tooltip("In Edit the inventory drawer fills the left of the screen; a card with nothing to " +
                 "point at starts at least this far in, so it does not cover the drawer.")]
        [SerializeField] float editClearance = 680f;

        [Header("Size")]
        [Tooltip("The card's scale with a mouse. Everything on it - text, buttons, icon - grows together.")]
        [SerializeField] float cardScale = 1.3f;

        [Tooltip("The card's scale on a touchscreen, read at arm's length and pressed with a thumb.")]
        [SerializeField] float touchCardScale = 1.7f;

        [Header("Motion")]
        [SerializeField] float inSeconds = 0.35f;
        [SerializeField] float outSeconds = 0.2f;

        [Tooltip("How far the card slides up as it comes in.")]
        [SerializeField] float slideDistance = 60f;

        [Header("Icons")]
        [SerializeField] Sprite inventoryIcon;
        [SerializeField] Sprite cameraIcon;
        [SerializeField] Sprite agentsIcon;
        [SerializeField] Sprite menuIcon;
        [SerializeField] Sprite placeIcon;
        [SerializeField] Sprite saveIcon;
        [SerializeField] Sprite shopIcon;

        [Header("Colours")]
        [SerializeField] Color signal = new Color(0.184f, 0.561f, 1f, 1f);      // #2F8FFF
        [SerializeField] Color shop = new Color(0.91f, 0.384f, 0.478f, 1f);     // #E8627A, the shop entry's pink lifted
        [SerializeField] Color dotIdle = new Color(0.165f, 0.271f, 0.439f, 1f); // #2A4570

        ILoadingScreen loadingScreen;
        IGamePhase phase;
        IEventHub hub;
        IDialogManager dialogs;
        IDisposable committedSubscription;

        InGameCanvasUI menu;
        CameraRig rig;
        AgentChatUI agentChat;
        ChatAgentWindow chatWindow;
        InventoryScreen inventory;

        IReadOnlyList<FtueStep> steps;
        int step = -1;
        FtueState state = FtueState.Off;

        // What the card shows, which trails the step by the length of a swap: the old card fades out
        // before the new one is drawn.
        FtueStep drawn;
        int drawnIndex = -1;

        // The agent the Agent step's spotlight is on.
        Transform spotAgent;

        // The Agent step's staging: the camera on the agent, and the agent kept walking. Set up as
        // the step starts watching and taken down the moment it stops.
        MainCinemachineCamera viewCamera;
        AgentRoutine stagedAgent;
        bool staged;

        // The motion. Shown is where the layer is headed, not where it is: it turns false the moment
        // a card starts to go.
        CanvasGroup layerGroup;
        CanvasGroup cardGroup;
        bool shown;
        float slide;
        int layerTween = -1;
        int cardTween = -1;
        int slideTween = -1;

        // Unscaled clock times: when the world counts as settled (pushed back while the loading
        // screen is up), and when Completing's beat is over.
        float readyAt;
        float beatEndsAt;

        // LookAround's three halves, and the zoom it started from.
        bool panned;
        bool rotated;
        bool zoomed;
        float zoomAtStart;

        // Set by events rather than read each frame.
        bool pieceCommitted;
        bool worldSaved;

        /// <summary>True once the player has finished or skipped the tour.</summary>
        public static bool Done => PlayerPrefs.GetInt(DoneKey, 0) == 1;

        /// <summary>Forgets the tour entirely, so it starts again next time the game scene opens.</summary>
        public static void Replay()
        {
            PlayerPrefs.DeleteKey(DoneKey);
            PlayerPrefs.DeleteKey(StepKey);
            PlayerPrefs.Save();
        }

        /// <summary>Where the tour is.</summary>
        public FtueState State => state;

        /// <summary>The step the tour is on, whether or not its card is showing; null before the first and after the last.</summary>
        public FtueStep Current => steps != null && step >= 0 && step < steps.Count ? steps[step] : null;

        [Inject]
        public void Construct(ILoadingScreen screen, IGamePhase gamePhase, IEventHub eventHub, IDialogManager dialogManager)
        {
            loadingScreen = screen;
            phase = gamePhase;
            hub = eventHub;
            dialogs = dialogManager;
        }

        void Awake()
        {
            // Groups to fade: the whole layer on the way in and out, the card alone between cards.
            layerGroup = canvas != null ? canvas.GetComponent<CanvasGroup>() : null;
            if (layerGroup == null && canvas != null)
            {
                layerGroup = canvas.gameObject.AddComponent<CanvasGroup>();
            }

            cardGroup = card != null ? card.GetComponent<CanvasGroup>() : null;
            if (cardGroup == null && card != null)
            {
                cardGroup = card.gameObject.AddComponent<CanvasGroup>();
            }

            state = FtueState.Off;
            SetCardVisible(false, instant: true);

            if (secondary != null)
            {
                secondary.onClick.AddListener(OnSecondary);
            }

            if (primary != null)
            {
                primary.onClick.AddListener(OnPrimary);
            }
        }

        void Start()
        {
            menu = FindAnyObjectByType<InGameCanvasUI>();
            rig = FindAnyObjectByType<CameraRig>();
            viewCamera = FindAnyObjectByType<MainCinemachineCamera>();
            agentChat = FindAnyObjectByType<AgentChatUI>(FindObjectsInactive.Include);
            chatWindow = FindAnyObjectByType<ChatAgentWindow>(FindObjectsInactive.Include);
            inventory = FindAnyObjectByType<InventoryScreen>(FindObjectsInactive.Include);

            if (hub != null)
            {
                committedSubscription = hub.Subscribe<WorldChanged>(OnWorldChanged);
            }

            // The save dialog's save, not the world's: the world also saves itself on leaving Edit,
            // and that is not the player saving.
            if (menu != null)
            {
                menu.SavedFromDialog += OnWorldSaved;
            }

            if (!Done)
            {
                Enter(FtueState.Starting);
            }
        }

        void OnDestroy()
        {
            committedSubscription?.Dispose();

            if (menu != null)
            {
                menu.SavedFromDialog -= OnWorldSaved;
            }

            Cancel(ref layerTween);
            Cancel(ref cardTween);
            Cancel(ref slideTween);

            if (stagedAgent != null)
            {
                stagedAgent.StaysOnFeet = false;
            }
        }

        /// <summary>One move along the path per frame, at most: read the game, ask the flow, act.</summary>
        void Update()
        {
            if (state == FtueState.Off)
            {
                return;
            }

            var next = FtueFlow.Next(state, ReadSignals());

            if (next != state)
            {
                Enter(next);
            }
        }

        FtueSignals ReadSignals()
        {
            var current = Current;

            return new FtueSignals(
                worldReady: state == FtueState.Starting && WorldReady(),
                goalMet: current != null && GoalMet(current.Goal),
                blocked: InTheWay(current),
                beatOver: Time.unscaledTime >= beatEndsAt,
                nextBlocked: InTheWay(NextStep));
        }

        /// <summary>The card after this one; null on the last, since ending the tour needs nothing shut.</summary>
        FtueStep NextStep => steps != null && step + 1 < steps.Count ? steps[step + 1] : null;

        /// <summary>The loading screen has gone, and the world has had <see cref="SettleTime"/> to stand.</summary>
        bool WorldReady()
        {
            if (loadingScreen != null && loadingScreen.IsShowing)
            {
                readyAt = Time.unscaledTime + SettleTime;
                return false;
            }

            return Time.unscaledTime >= readyAt;
        }

        /// <summary>
        /// Whether something is in the way of <paramref name="card"/>: a dialog - the save dialog
        /// the save step sends the player to, the shop, anything - or an agent chat, which is how
        /// "Meet your agent" finishes; or the inventory, for a card that needs it shut. A card
        /// pointing at the menu behind any of them, and a dim over it, is only confusing.
        /// </summary>
        bool InTheWay(FtueStep card)
        {
            return (dialogs != null && dialogs.IsAnyOpen)
                   || ChatUp()
                   || (card != null && card.NeedsInventoryClosed && InventoryOpen());
        }

        /// <summary>The inventory drawer is out. It opens with the Edit phase, so that stands in if the screen is not found.</summary>
        bool InventoryOpen() => inventory != null ? inventory.IsOpen : phase != null && phase.IsEdit;

        /// <summary>Whether an agent chat is on screen: open and not minimised to its bar.</summary>
        bool ChatUp()
        {
            return (agentChat != null && agentChat.IsOpen)
                   || (chatWindow != null && chatWindow.IsOpen && !chatWindow.IsMinimised);
        }

        /// <summary>Moves to <paramref name="next"/> and does what arriving there means.</summary>
        void Enter(FtueState next)
        {
            var from = state;
            state = next;

            switch (next)
            {
                case FtueState.Off:
                    step = -1;
                    break;

                case FtueState.Starting:
                    step = -1;
                    readyAt = Time.unscaledTime + SettleTime;
                    break;

                case FtueState.Watching:
                    if (from == FtueState.SteppedAside)
                    {
                        ReopenEntries();
                    }

                    break;

                case FtueState.Completing:
                    beatEndsAt = Time.unscaledTime + CompletionBeat;

                    // Done, so a required step's NEXT comes on for the beat before the tour moves on.
                    SetPrimaryOn(true);
                    break;

                case FtueState.Advance:
                    // Passed straight through: on to Watching the next card, or Off after the last.
                    AdvanceStep();
                    return;
            }

            SetCardVisible(FtueFlow.CardVisible(state));
            Stage();
        }

        /// <summary>
        /// Sets up or takes down the Agent step's staging, from where the tour now is: up while that
        /// step is being watched, down the moment it is not - the agent clicked and the chat open,
        /// or the tour skipped.
        /// </summary>
        /// <remarks>
        /// Up: the camera blends onto the spotlit agent at the zoom the game opened at, and the agent
        /// keeps walking rather than sitting at a desk, where it is hard to spot. Down: the agent
        /// goes back to its routine and the camera back to the manager, who by then is on his way
        /// over to the agent that was clicked.
        /// </remarks>
        void Stage()
        {
            var wanted = Current != null && Current.Target == FtueTarget.Agent
                         && (state == FtueState.Watching || state == FtueState.SteppedAside);

            if (wanted == staged)
            {
                return;
            }

            staged = wanted;

            if (wanted)
            {
                stagedAgent = spotAgent != null ? spotAgent.GetComponent<AgentRoutine>() : null;

                if (stagedAgent != null)
                {
                    stagedAgent.StaysOnFeet = true;
                }

                if (viewCamera != null && spotAgent != null)
                {
                    viewCamera.FocusOn(spotAgent);

                    // After the hand-over, which adopts the camera's current zoom as the rig's.
                    if (rig != null)
                    {
                        rig.TargetZoom = rig.StartZoom;
                    }
                }

                return;
            }

            if (stagedAgent != null)
            {
                stagedAgent.StaysOnFeet = false;
                stagedAgent = null;
            }

            if (viewCamera != null)
            {
                viewCamera.ClearFocus();
            }
        }

        /// <summary>
        /// The next card. The first time, the steps are decided - now the world is standing, which
        /// ones this player can actually do - and the tour picks up where it was left.
        /// </summary>
        void AdvanceStep()
        {
            if (steps == null)
            {
                var hasAgents = FindAnyObjectByType<ManagerController>() != null
                                && FindAnyObjectByType<AgentRoutine>() != null;
                steps = FtueSteps.For(CameraRig.UsesTouch, hasAgents);
                EnsureDots(steps.Count);

                ShowStep(ResumeIndex());
                return;
            }

            ShowStep(step + 1);
        }

        /// <summary>
        /// Back from a dialog on a step that points at a menu entry - a save cancelled, say: the
        /// dialog put the menu away, so it comes up again for the spotlight to find.
        /// </summary>
        void ReopenEntries()
        {
            if (menu != null && Current != null && IsEntry(Current.Target) && menu.State != InGameMenuState.Entries)
            {
                menu.OpenEntries();
            }
        }

        /// <summary>A target that is one of the raised menu's entries, so the menu has to be up to point at it.</summary>
        static bool IsEntry(FtueTarget target)
            => target == FtueTarget.Inventory || target == FtueTarget.SaveWorld || target == FtueTarget.Shop;

        /// <summary>Where to pick the tour up: the step saved last time, if it is still in the list.</summary>
        int ResumeIndex()
        {
            var saved = PlayerPrefs.GetString(StepKey, string.Empty);

            for (var i = 0; i < steps.Count; i++)
            {
                if (steps[i].Goal.ToString() == saved)
                {
                    return i;
                }
            }

            return 0;
        }

        /// <summary>Moves the tour to step <paramref name="index"/> and starts Watching it. Past the last step, the tour ends.</summary>
        void ShowStep(int index)
        {
            if (steps == null || index < 0 || index >= steps.Count)
            {
                Finish(false);
                return;
            }

            step = index;
            var current = steps[index];

            PlayerPrefs.SetString(StepKey, current.Goal.ToString());
            PlayerPrefs.Save();

            ArmGoal(current.Goal);
            spotAgent = current.Target == FtueTarget.Agent ? PickAgent() : null;

            if (menu != null)
            {
                if (current.Target == FtueTarget.MenuButton)
                {
                    // The point of this step is the player opening it themselves.
                    menu.CloseEntries();
                }
                else if (IsEntry(current.Target))
                {
                    // An entry cannot be pointed at while it is tucked away.
                    menu.OpenEntries();
                }
                else if (current.Goal == FtueGoal.PlacePiece && (phase == null || !phase.IsEdit))
                {
                    // Pieces come out of the inventory, so it has to be open for this step - the
                    // player may have pressed NEXT on the inventory step rather than opening it.
                    // Through its own entry, which also puts the menu away.
                    menu.ShowInventory();
                }
            }

            if (shown)
            {
                // A card already up is swapped: the old one fades, the new one is drawn and fades in.
                SwapTo(index);
            }
            else
            {
                // Coming up from nothing: drawn now, and the whole layer brings it in below.
                Cancel(ref cardTween);
                if (cardGroup != null)
                {
                    cardGroup.alpha = 1f;
                }

                Draw(index);
            }

            Enter(FtueState.Watching);
        }

        /// <summary>Puts step <paramref name="index"/>'s words, colours and buttons on the card.</summary>
        void Draw(int index)
        {
            var current = steps[index];
            var accent = AccentOf(current);

            drawn = current;
            drawnIndex = index;

            title.text = current.Title;
            body.text = Highlight(current.Body, current.Accent == FtueAccent.Shop ? accent : Color.white);
            secondaryLabel.text = current.Secondary ?? string.Empty;
            secondary.gameObject.SetActive(!string.IsNullOrEmpty(current.Secondary));
            primaryLabel.text = current.Primary;

            // Completing already, if the goal was met while the old card was fading out.
            SetPrimaryOn(!current.Required || (state == FtueState.Completing && index == step));
            eyebrow.color = accent;
            DrawEyebrow();

            icon.sprite = IconOf(current.Icon);
            icon.color = current.Icon == FtueIcon.Shop ? Color.white : accent;
            iconPlate.color = new Color(accent.r, accent.g, accent.b, 0.35f);
            cardAccent.color = new Color(accent.r, accent.g, accent.b, current.Accent == FtueAccent.Shop ? 0.7f : 0.35f);
            frame.color = accent;

            for (var i = 0; i < dots.Length; i++)
            {
                if (dots[i] != null)
                {
                    dots[i].color = i == index ? accent : i < index ? new Color(accent.r, accent.g, accent.b, 0.6f) : dotIdle;
                }
            }

            Place();
        }

        /// <summary>The agent to shine the spotlight on: the first one standing in the world.</summary>
        static Transform PickAgent()
        {
            var agent = FindAnyObjectByType<AgentRoutine>();
            return agent != null ? agent.transform : null;
        }

        /// <summary>Clears whatever the previous step was waiting for.</summary>
        void ArmGoal(FtueGoal goal)
        {
            panned = rotated = zoomed = false;
            zoomAtStart = rig != null ? rig.TargetZoom : 0f;
            pieceCommitted = false;
            worldSaved = false;
        }

        /// <summary>Whether the step the tour is on has had its thing done.</summary>
        bool GoalMet(FtueGoal goal)
        {
            switch (goal)
            {
                case FtueGoal.LookAround:
                    if (rig != null)
                    {
                        var was = (panned, rotated, zoomed);
                        panned |= rig.IsDragging;
                        rotated |= rig.IsRotating;
                        zoomed |= Mathf.Abs(rig.TargetZoom - zoomAtStart) > 0.2f;

                        if (was != (panned, rotated, zoomed))
                        {
                            DrawEyebrow();
                        }
                    }

                    return panned && rotated && zoomed;

                case FtueGoal.MeetAgent:
                    return (agentChat != null && agentChat.IsOpen) || (chatWindow != null && chatWindow.IsOpen);

                case FtueGoal.OpenMenu:
                    return menu != null && menu.State == InGameMenuState.Entries;

                case FtueGoal.OpenInventory:
                    return phase != null && phase.IsEdit;

                case FtueGoal.PlacePiece:
                    return pieceCommitted;

                case FtueGoal.SaveWorld:
                    return worldSaved;

                default:
                    return false;
            }
        }

        void OnWorldChanged(WorldChanged change)
        {
            if (change.Reason == "committed")
            {
                pieceCommitted = true;
            }
        }

        void OnWorldSaved(string name) => worldSaved = true;

        /// <summary>"STEP 2 OF 8", and on the camera step which of its three are done.</summary>
        void DrawEyebrow()
        {
            // The card on screen, which during a swap is still the old one.
            if (drawn == null)
            {
                return;
            }

            var text = "STEP " + (drawnIndex + 1) + " OF " + steps.Count;

            if (drawn.Goal == FtueGoal.LookAround && drawn == Current)
            {
                text += "   ·   " + Tick("PAN", panned) + "  " + Tick("ROTATE", rotated) + "  " + Tick("ZOOM", zoomed);
            }

            eyebrow.text = text;
        }

        /// <summary>A word in the eyebrow: its step's colour once done, the idle dot colour until then.</summary>
        string Tick(string word, bool done)
            => done ? word : "<color=#" + ColorUtility.ToHtmlStringRGB(dotIdle) + ">" + word + "</color>";

        /// <summary>The card follows its target every frame it is on screen - on its way out too, so the slide shows.</summary>
        void LateUpdate()
        {
            if (canvas != null && canvas.enabled)
            {
                Place();
            }
        }

        /// <summary>Puts the card - and on a spotlit card the hole, the frame and the notch - where they belong this frame.</summary>
        void Place()
        {
            if (drawn == null)
            {
                return;
            }

            var size = root.rect.size;

            // Scaled as a whole, so text, buttons and icon grow together - but never wider than the
            // screen, which on a tablet's squarer shape the touch scale would be.
            var wanted = CameraRig.UsesTouch ? touchCardScale : cardScale;
            var scale = Mathf.Min(wanted, (size.x - 48f) / Mathf.Max(1f, card.rect.width));

            // Worked out every frame rather than once per card: an agent can walk out of view.
            var hole = default(Rect);
            var found = drawn.Target != FtueTarget.None && TryTargetRect(drawn.Target, out hole);
            SetSpotlight(found);

            // In Edit a card with nothing to point at sits right of the inventory drawer, shrunk as
            // far as it must to fit there: covering the drawer would hide what the step says to drag.
            var inEdit = !found && phase != null && phase.IsEdit;
            if (inEdit)
            {
                scale = Mathf.Min(scale, (size.x - editClearance - 24f) / Mathf.Max(1f, card.rect.width));
            }

            card.localScale = new Vector3(scale, scale, 1f);
            var cardSize = card.rect.size * scale;

            if (!found)
            {
                notch.gameObject.SetActive(false);

                var x = (size.x - cardSize.x) * 0.5f;

                if (inEdit)
                {
                    x = Mathf.Min(Mathf.Max(x, editClearance), size.x - cardSize.x - 24f);
                }

                card.anchoredPosition = new Vector2(x, cardMargin + slide);
                return;
            }

            hole.xMin -= holePadding;
            hole.yMin -= holePadding;
            hole.xMax += holePadding;
            hole.yMax += holePadding;

            Stretch(dimLeft, 0f, 0f, hole.xMin, size.y);
            Stretch(dimRight, hole.xMax, 0f, size.x - hole.xMax, size.y);
            Stretch(dimBottom, hole.xMin, 0f, hole.width, hole.yMin);
            Stretch(dimTop, hole.xMin, hole.yMax, hole.width, size.y - hole.yMax);
            Stretch(frame.rectTransform, hole.xMin - frameSpread, hole.yMin - frameSpread,
                hole.width + frameSpread * 2f, hole.height + frameSpread * 2f);

            if (drawn.Target == FtueTarget.Agent)
            {
                // An agent can be anywhere, and the card is too wide to sit beside them: below them
                // if there is room, above them if not, centred either way. No notch - it points
                // sideways, and the frame already says who.
                notch.gameObject.SetActive(false);

                var below = hole.yMin - frameSpread - cardGap - cardSize.y;
                var above = hole.yMax + frameSpread + cardGap;
                var y = below >= cardMargin ? cardMargin
                    : above + cardSize.y <= size.y - cardMargin ? above
                    : cardMargin;

                card.anchoredPosition = new Vector2((size.x - cardSize.x) * 0.5f, y + slide);
                return;
            }

            // Beside the target and level with its row, with the notch pointing straight at it.
            // Kept on screen: the TAB button sits near the bottom, so its card stays along the
            // bottom edge and the notch moves down to meet it.
            notch.gameObject.SetActive(true);

            // Never over the target: a card too wide for the room right of it is shrunk to fit,
            // rather than pushed back left across the button it is pointing at.
            var cardX = hole.xMax + cardGap;
            var room = size.x - cardX - 24f;
            if (cardSize.x > room)
            {
                scale = room / Mathf.Max(1f, card.rect.width);
                card.localScale = new Vector3(scale, scale, 1f);
                cardSize = card.rect.size * scale;
            }

            var cardY = Mathf.Clamp(hole.center.y - cardSize.y * 0.5f, cardMargin, size.y - cardSize.y - cardMargin);
            card.anchoredPosition = new Vector2(cardX, cardY + slide);

            // In the card's own units, which the scale makes smaller than the screen's.
            var notchY = Mathf.Clamp((hole.center.y - cardY) / scale, 28f, card.rect.height - 28f);
            notch.anchoredPosition = new Vector2(0f, notchY);
        }

        /// <summary>
        /// The target's rectangle in this canvas, measured from its bottom-left corner.
        /// </summary>
        /// <remarks>
        /// An entry on a menu the player has closed is not there to point at, so the spotlight goes
        /// back to the TAB button: the way to it.
        /// </remarks>
        bool TryTargetRect(FtueTarget target, out Rect rect)
        {
            rect = default;

            if (target == FtueTarget.Agent)
            {
                // Picked again if the one chosen has gone - the world was reloaded, say.
                if (spotAgent == null)
                {
                    spotAgent = PickAgent();
                }

                return TryWorldRect(spotAgent, out rect);
            }

            if (menu == null)
            {
                return false;
            }

            RectTransform rt;

            if (target == FtueTarget.MenuButton || menu.State != InGameMenuState.Entries)
            {
                rt = menu.ToggleRect;
            }
            else
            {
                rt = target == FtueTarget.Shop ? menu.ShopEntryRect
                    : target == FtueTarget.Inventory ? menu.InventoryEntryRect
                    : menu.SaveWorldEntryRect;
            }

            if (rt == null)
            {
                return false;
            }

            var targetCanvas = rt.GetComponentInParent<Canvas>();
            var view = targetCanvas == null || targetCanvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : targetCanvas.rootCanvas.worldCamera;

            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);

            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);

            foreach (var corner in corners)
            {
                var screen = RectTransformUtility.WorldToScreenPoint(view, corner);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out var local);
                var fromCorner = local - root.rect.min;
                min = Vector2.Min(min, fromCorner);
                max = Vector2.Max(max, fromCorner);
            }

            rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            return true;
        }

        /// <summary>
        /// Something in the world - an agent - as a rectangle in this canvas: the screen box around
        /// everything drawn for it. False when there is nothing drawn, or it is behind the camera or
        /// out of view.
        /// </summary>
        bool TryWorldRect(Transform target, out Rect rect)
        {
            rect = default;
            var view = Camera.main;

            if (target == null || view == null)
            {
                return false;
            }

            var renderers = target.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                return false;
            }

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            var c = bounds.center;
            var e = bounds.extents;

            for (var corner = 0; corner < 8; corner++)
            {
                var point = c + new Vector3(
                    (corner & 1) == 0 ? -e.x : e.x,
                    (corner & 2) == 0 ? -e.y : e.y,
                    (corner & 4) == 0 ? -e.z : e.z);

                var screen = view.WorldToScreenPoint(point);
                if (screen.z <= 0f)
                {
                    return false;
                }

                RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out var local);
                var fromCorner = local - root.rect.min;
                min = Vector2.Min(min, fromCorner);
                max = Vector2.Max(max, fromCorner);
            }

            rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);

            var screenRect = new Rect(Vector2.zero, root.rect.size);
            return rect.Overlaps(screenRect);
        }

        /// <summary>SKIP TOUR: straight to Off. On the shop card it is OPEN SHOP, which ends the tour too.</summary>
        void OnSecondary()
        {
            if (FtueFlow.CardVisible(state))
            {
                // The card on screen's meaning, which mid-swap is still the old card's.
                Finish(drawn != null && drawn.SecondaryOpensShop);
            }
        }

        /// <summary>
        /// NEXT: onward, the same as a finished beat - straight to the next card, or waiting while
        /// something is in its way. Guarded as well as switched off: a required step is done only
        /// once its goal is met, which is Completing.
        /// </summary>
        void OnPrimary()
        {
            if ((state == FtueState.Watching && !Current.Required) || state == FtueState.Completing)
            {
                Enter(FtueFlow.Onward(InTheWay(NextStep)));
            }
        }

        void SetPrimaryOn(bool on)
        {
            primary.interactable = on;

            if (primaryGlow != null)
            {
                primaryGlow.enabled = on;
            }
        }

        /// <summary>Ends the tour for good, and optionally opens the shop on the way out.</summary>
        void Finish(bool openShop)
        {
            PlayerPrefs.SetInt(DoneKey, 1);
            PlayerPrefs.DeleteKey(StepKey);
            PlayerPrefs.Save();

            Enter(FtueState.Off);

            if (menu == null)
            {
                return;
            }

            if (openShop)
            {
                menu.ShowShop();
            }
            else
            {
                menu.CloseEntries();
            }
        }

        /// <summary>
        /// Brings the card's layer in or takes it out. Only <see cref="Enter"/> decides which, from
        /// the state; <paramref name="instant"/> is for setting it up, before anything has shown.
        /// </summary>
        /// <remarks>
        /// In: the canvas on, the layer fading up and the card sliding up into place. Out: no more
        /// clicks at once, the layer fading and the card dropping a little, and the canvas off when
        /// it is gone. On unscaled time, like the popups, so a paused game does not freeze it.
        /// </remarks>
        void SetCardVisible(bool on, bool instant = false)
        {
            if (on == shown && !instant)
            {
                return;
            }

            shown = on;
            Cancel(ref layerTween);
            Cancel(ref slideTween);

            if (instant || !Application.isPlaying || layerGroup == null)
            {
                slide = 0f;

                if (layerGroup != null)
                {
                    layerGroup.alpha = on ? 1f : 0f;
                }

                SetLayerOn(on);
                return;
            }

            if (on)
            {
                SetLayerOn(true);
                layerTween = LeanTween.alphaCanvas(layerGroup, 1f, inSeconds)
                    .setEase(LeanTweenType.easeOutCubic)
                    .setIgnoreTimeScale(true)
                    .setOnComplete(() => layerTween = -1)
                    .id;
                Slide(-slideDistance, 0f, inSeconds, LeanTweenType.easeOutBack);
                return;
            }

            // Nothing on a card on its way out can be pressed.
            if (raycaster != null)
            {
                raycaster.enabled = false;
            }

            layerTween = LeanTween.alphaCanvas(layerGroup, 0f, outSeconds)
                .setEase(LeanTweenType.easeInCubic)
                .setIgnoreTimeScale(true)
                .setOnComplete(() =>
                {
                    layerTween = -1;
                    SetLayerOn(false);
                })
                .id;
            Slide(slide, -slideDistance * 0.5f, outSeconds, LeanTweenType.easeInCubic);
        }

        /// <summary>
        /// One card for another while the layer stays up: the old card fades and drops, step
        /// <paramref name="index"/> is drawn in its place, and it fades in rising.
        /// </summary>
        void SwapTo(int index)
        {
            Cancel(ref cardTween);

            if (cardGroup == null || !Application.isPlaying)
            {
                Draw(index);
                return;
            }

            cardTween = LeanTween.alphaCanvas(cardGroup, 0f, outSeconds)
                .setEase(LeanTweenType.easeInCubic)
                .setIgnoreTimeScale(true)
                .setOnComplete(() =>
                {
                    Draw(index);
                    cardTween = LeanTween.alphaCanvas(cardGroup, 1f, inSeconds)
                        .setEase(LeanTweenType.easeOutCubic)
                        .setIgnoreTimeScale(true)
                        .setOnComplete(() => cardTween = -1)
                        .id;
                    Slide(-slideDistance * 0.5f, 0f, inSeconds, LeanTweenType.easeOutCubic);
                })
                .id;
        }

        /// <summary>Moves the card's slide from <paramref name="from"/> to <paramref name="to"/>. <see cref="Place"/> adds it to the card's height.</summary>
        void Slide(float from, float to, float seconds, LeanTweenType ease)
        {
            Cancel(ref slideTween);
            slide = from;
            slideTween = LeanTween.value(gameObject, from, to, seconds)
                .setEase(ease)
                .setIgnoreTimeScale(true)
                .setOnUpdate((float value) => slide = value)
                .setOnComplete(() => slideTween = -1)
                .id;
        }

        void SetLayerOn(bool on)
        {
            if (canvas != null)
            {
                canvas.enabled = on;
            }

            if (raycaster != null)
            {
                raycaster.enabled = on;
            }
        }

        static void Cancel(ref int tween)
        {
            if (tween >= 0)
            {
                LeanTween.cancel(tween);
                tween = -1;
            }
        }

        /// <summary>One dot per step: the authored ones, and copies of the first to the left of them if there are more steps.</summary>
        void EnsureDots(int count)
        {
            if (dots.Length == 0 || dots[0] == null || dots.Length >= count)
            {
                return;
            }

            var all = new List<Image>(dots);
            var first = dots[0].rectTransform;
            var spacing = dots.Length > 1 && dots[1] != null
                ? dots[1].rectTransform.anchoredPosition.x - first.anchoredPosition.x
                : 16f;

            while (all.Count < count)
            {
                var copy = Instantiate(dots[0], first.parent);
                copy.name = "Dot" + (all.Count + 1);
                all.Insert(0, copy);
            }

            // Laid out again right to left from where the last authored dot sits.
            var last = dots[dots.Length - 1].rectTransform.anchoredPosition;
            for (var i = 0; i < all.Count; i++)
            {
                all[i].rectTransform.anchoredPosition = new Vector2(last.x - (all.Count - 1 - i) * spacing, last.y);
            }

            dots = all.ToArray();
        }

        void SetSpotlight(bool on)
        {
            dimTop.gameObject.SetActive(on);
            dimBottom.gameObject.SetActive(on);
            dimLeft.gameObject.SetActive(on);
            dimRight.gameObject.SetActive(on);
            frame.gameObject.SetActive(on);
        }

        Color AccentOf(FtueStep current) => current.Accent == FtueAccent.Shop ? shop : signal;

        Sprite IconOf(FtueIcon kind)
        {
            switch (kind)
            {
                case FtueIcon.Camera: return cameraIcon != null ? cameraIcon : inventoryIcon;
                case FtueIcon.Agents: return agentsIcon != null ? agentsIcon : inventoryIcon;
                case FtueIcon.Menu: return menuIcon;
                case FtueIcon.Place: return placeIcon != null ? placeIcon : inventoryIcon;
                case FtueIcon.Save: return saveIcon != null ? saveIcon : inventoryIcon;
                case FtueIcon.Shop: return shopIcon;
                default: return inventoryIcon;
            }
        }

        static void Stretch(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(Mathf.Max(0f, width), Mathf.Max(0f, height));
        }

        /// <summary>The body with its &lt;b&gt; words drawn in <paramref name="colour"/>.</summary>
        public static string Highlight(string text, Color colour)
        {
            var hex = "#" + ColorUtility.ToHtmlStringRGB(colour);
            return text.Replace("<b>", "<b><color=" + hex + ">").Replace("</b>", "</color></b>");
        }
    }
}
