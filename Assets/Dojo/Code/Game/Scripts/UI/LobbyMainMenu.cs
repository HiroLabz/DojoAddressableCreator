using System;
using System.Collections.Generic;
using Dojo.Framework.Content;
using Dojo.Framework.Net;
using Dojo.Framework.Session;
using Dojo.Framework.Startup;
using Dojo.Framework.UI;
using Dojo.Framework.Utilities;
using Dojo.Framework.World;
using Dojo.Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace Dojo.Game
{
    /// <summary>
    /// Drives the Lobby's main menu screen: owns every control and every label whose text is not
    /// fixed, and decides which parts of the screen are showing.
    /// </summary>
    /// <remarks>
    /// This component deliberately does not load scenes. <see cref="Dojo.Framework.Components.ToLoad"/>
    /// already does that, and it also gates on <c>IAppReadiness</c> so a player cannot enter a world
    /// before its content has downloaded. Duplicating that here would give us two rules about when
    /// the game may be entered, and one of them would eventually be wrong. Navigation buttons raise
    /// an event instead; attach <c>ToLoad</c> alongside for the ones that change scene.
    /// <para>
    /// Labels that never change — the eyebrow tag, the tagline, the button captions — are not
    /// referenced here. Only what something else has to fill in is serialised, so the field list
    /// stays an accurate inventory of what this screen needs from outside.
    /// </para>
    /// <para>
    /// Whether the screen shows at all is <c>LobbyScreens</c>' decision: it comes up once the
    /// player is signed in. The SESSION panel and the build badge fill themselves - see
    /// <see cref="SessionPanel"/>.
    /// </para>
    /// </remarks>
    public sealed class LobbyMainMenu : MonoBehaviour
    {
        /// <summary>Alpha the menu sits at while startup preprocesses are still running.</summary>
        const float BusyAlpha = 0.30f;

        /// <summary>Seconds the menu takes to fade up once the app is ready.</summary>
        const float ReadyFadeSeconds = 1f;

        [Header("Screen")]
        [Tooltip("Dimmed and made non-interactive until every required startup preprocess has " +
                 "succeeded. Defaults to the CanvasGroup on this object.")]
        [SerializeField] CanvasGroup canvasGroup;

        [Header("Controls")]
        [Tooltip("Resumes the world named on the continue card. Hidden with the card when there is no save.")]
        [SerializeField] Button resumeButton;

        [Tooltip("Starts an empty world.")]
        [SerializeField] Button newWorldButton;

        [Tooltip("Opens the saved-world list.")]
        [SerializeField] Button loadWorldButton;

        [Tooltip("Opens the content pack screen.")]
        [SerializeField] Button contentPacksButton;

        [Tooltip("Opens settings.")]
        [SerializeField] Button settingsButton;

        [Tooltip("Quits the application.")]
        [SerializeField] Button quitButton;

        [Tooltip("Recent Worlds. Pressing one of its rows opens that world.")]
        [SerializeField] UI.RecentWorldsPanel recentWorlds;

        [Header("Continue card")]
        [Tooltip("The whole card. Hidden when there is no world to resume.")]
        [SerializeField] GameObject continueCard;

        [Tooltip("Name of the world the card offers to resume.")]
        [SerializeField] TextMeshProUGUI continueWorldName;

        [Tooltip("Piece and agent counts plus the save time, on two lines.")]
        [SerializeField] TextMeshProUGUI continueWorldMeta;

        [Tooltip("The picture in the card's box: the world as the player last left it, or the default " +
                 "world's own. Switched off, leaving the empty box, when there is none.")]
        [SerializeField] RawImage continuePicture;

        [Header("Pack chips")]
        [Tooltip("The bottom-right pack strip. Told what to show from here; builds its own chips.")]
        [SerializeField] PackComponent packChips;

        [Header("Navigation")]
        [Tooltip("Scene entered when a world is chosen from the load dialog. Must match the name " +
                 "the ToLoad components on Resume and New World use.")]
        [SerializeField] string gameSceneName = "Game";

        /// <summary>Raised when the player asks to resume the world on the continue card.</summary>
        public event Action ResumeRequested;

        /// <summary>Raised when the player asks for a new, empty world.</summary>
        public event Action NewWorldRequested;

        /// <summary>Raised when the player asks to open the saved-world list.</summary>
        public event Action LoadWorldRequested;

        /// <summary>Raised when the player asks to open the content pack screen.</summary>
        public event Action ContentPacksRequested;

        /// <summary>Raised when the player asks to open settings.</summary>
        public event Action SettingsRequested;

        /// <summary>Raised when the player asks to quit.</summary>
        public event Action QuitRequested;

        /// <summary>
        /// Whether the player has any saved world at all. Separate from the continue card: a player
        /// can have saves without a current world, and could otherwise be shown a Load World button
        /// that opens an empty list.
        /// </summary>
        bool hasSavedWorlds;

        /// <summary>
        /// What <see cref="SetNavigationInteractable"/> last asked for. Held so that re-enabling
        /// navigation after a load cannot quietly revive Load World for a player with no saves.
        /// </summary>
        bool navigationInteractable = true;

        IAppReadiness readiness;
        IEntitlements entitlements;
        IWorldSource worlds;
        IWorldSelection selection;
        WorldOpenIntent worldIntent;
        IDialogManager dialogs;
        ILoadingService loading;
        ILoadingScreen loadingScreen;

        /// <summary>Where the default world is read from, for a player with none of their own.</summary>
        IContentService content;

        /// <summary>Content address of the default world's picture, beside the default world itself.</summary>
        const string DefaultWorldPicture = "default/World/default_world_picture";

        /// <summary>Where the worlds' pictures are read from: the game's own folder, unless a test says otherwise.</summary>
        /// <remarks>
        /// Made on first use: Unity will not hand out <see cref="Application.persistentDataPath"/>
        /// while a component is being constructed.
        /// </remarks>
        public Placement.WorldPictureFile Pictures
        {
            get => pictureFile ?? (pictureFile = Placement.WorldPictureFile.InPersistentData());
            set => pictureFile = value;
        }

        Placement.WorldPictureFile pictureFile;

        /// <summary>
        /// The picture read from disk, which this owns and destroys. Null while the card shows the
        /// default world's, which belongs to the content.
        /// </summary>
        Texture2D ownedPicture;

        /// <summary>
        /// What the continue card calls the starter world, for a player who has no world yet - a
        /// new install. A saved world is shown by its own name.
        /// </summary>
        const string YourWorld = "Your World";

        /// <summary>
        /// Supplied by <c>LobbyLifetimeScope</c> once the container is built.
        /// </summary>
        /// <remarks>
        /// <see cref="IEntitlements"/> rather than <c>IEntitlementGrants</c>: this screen asks what
        /// the player holds, it never hands them anything.
        /// </remarks>
        [Inject]
        public void Construct(
            IAppReadiness readiness,
            IEntitlements entitlements,
            IWorldSource worlds,
            IWorldSelection selection,
            WorldOpenIntent worldIntent,
            IDialogManager dialogs,
            ILoadingService loading,
            ILoadingScreen loadingScreen,
            IContentService content)
        {
            this.readiness = readiness;
            this.entitlements = entitlements;
            this.worlds = worlds;
            this.selection = selection;
            this.worldIntent = worldIntent;
            this.dialogs = dialogs;
            this.loading = loading;
            this.loadingScreen = loadingScreen;
            this.content = content;
        }

        void Awake()
        {
            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
            }

            // Dimmed from the first frame. The Lobby is shown the moment Bootstrapper hands over,
            // while content and the session are still coming up behind it, so lighting the menu
            // first and dimming it in Start would flash a usable-looking menu that is not usable.
            ApplyBusyState(false);

            Bind(resumeButton, () => ResumeRequested?.Invoke());
            Bind(newWorldButton, OnNewWorldClicked);
            Bind(loadWorldButton, OnLoadWorldClicked);
            Bind(contentPacksButton, () => ContentPacksRequested?.Invoke());
            Bind(settingsButton, () => SettingsRequested?.Invoke());
            Bind(quitButton, () => QuitRequested?.Invoke());

            // Nothing has supplied world data yet, so the screen starts in the state that is true
            // for a brand new player: no card to resume, and nothing to load. Anything else would
            // briefly offer the player a world that may not exist.
            ShowNoContinueWorld();
            SetSavedWorlds(0);

            ApplyFirstTimeState();
        }

        void Start()
        {
            if (readiness != null)
            {
                readiness.Changed += ApplyReadiness;
            }

            if (entitlements != null)
            {
                entitlements.Changed += OnEntitlementsChanged;
            }

            if (worlds != null)
            {
                worlds.Changed += OnWorldsChanged;
            }

            // The same way in as the load dialog's: recorded as current, then entered.
            if (recentWorlds != null)
            {
                recentWorlds.WorldChosen += OnWorldChosen;
            }

            // Applied once on top of subscribing, rather than waiting for the next event. A
            // preprocess that finished before this scene loaded has already raised its only
            // Changed, and a menu that waited for another would stay dimmed forever. The same
            // applies to the entitlement set adopted at sign-in.
            ApplyReadiness();
            ApplyEntitlements();
            ApplyWorlds();
        }

        void OnDestroy()
        {
            if (readiness != null)
            {
                readiness.Changed -= ApplyReadiness;
            }

            if (entitlements != null)
            {
                entitlements.Changed -= OnEntitlementsChanged;
            }

            if (worlds != null)
            {
                worlds.Changed -= OnWorldsChanged;
            }

            if (recentWorlds != null)
            {
                recentWorlds.WorldChosen -= OnWorldChosen;
            }

            // A fade still running when the scene unloads would tick against a destroyed
            // CanvasGroup on its next frame.
            LeanTween.cancel(gameObject);

            ShowPicture(null, false);
        }

        void OnEntitlementsChanged(EntitlementsChanged change)
        {
            ApplyEntitlements();
        }

        void OnWorldsChanged()
        {
            ApplyWorlds();
        }

        /// <summary>
        /// Lights the menu only once every required startup preprocess has succeeded. An uninjected
        /// component is treated as ready rather than dead: the Lobby opened on its own, outside the
        /// Bootstrapper path, should still be usable instead of a permanently greyed-out screen.
        /// </summary>
        void ApplyReadiness()
        {
            ApplyBusyState(readiness == null || readiness.AllRequiredSucceeded);

            // The same signal that lights the menu is the one that means the world cache has been
            // filled, so the continue card is re-read here rather than on a timer.
            ApplyWorlds();
        }

        /// <summary>
        /// Moves the menu between its dimmed startup state and its usable one.
        /// </summary>
        /// <remarks>
        /// Only the fade up is animated. Dimming happens at <c>Awake</c>, before a single frame has
        /// been drawn, so there is nothing to animate from; and a menu going un-ready mid-session is
        /// a fault, which should read as an abrupt change rather than a graceful one.
        /// <para>
        /// <c>interactable</c> flips immediately rather than on the tween's completion. The gate is
        /// readiness, not the animation — a player who clicks the instant content finishes
        /// downloading should not be turned away for another second because a fade is still running.
        /// </para>
        /// </remarks>
        void ApplyBusyState(bool ready)
        {
            if (canvasGroup == null)
            {
                return;
            }

            canvasGroup.interactable = ready;

            // Any in-flight fade is stale the moment readiness changes; leaving it running would
            // let it finish and overwrite whatever was just set.
            LeanTween.cancel(gameObject);

            if (!ready)
            {
                canvasGroup.alpha = BusyAlpha;
                return;
            }

            if (Mathf.Approximately(canvasGroup.alpha, 1f))
            {
                return;
            }

            LeanTween.alphaCanvas(canvasGroup, 1f, ReadyFadeSeconds)
                     .setEase(LeanTweenType.easeOutQuad)
                     .setIgnoreTimeScale(true);
        }

        /// <summary>
        /// Opens the saved-world list, and enters the chosen world.
        /// </summary>
        /// <remarks>
        /// The dialog is a view: it is handed the worlds and two callbacks and decides nothing. The
        /// order on confirm matters — the selection is recorded <em>before</em> the scene load
        /// starts, because the game scene reads the current world in its own <c>Start</c> and a
        /// selection written afterwards would arrive too late to be the one it opens.
        /// </remarks>
        void OnLoadWorldClicked()
        {
            LoadWorldRequested?.Invoke();

            if (dialogs == null || worlds == null || !worlds.IsReady)
            {
                return;
            }

            var dialog = dialogs.OpenDialog<ILoadWorldDialog>();

            if (dialog == null)
            {
                Debug.LogError(
                    $"{nameof(LobbyMainMenu)} could not open the load-world dialog. Assign " +
                    "LoadWorldDialog.prefab to DialogPrefabs on RootLifetimeScope.",
                    this);
                return;
            }

            dialog.Choose(BuildWorldList(), OnWorldChosen);
        }

        /// <summary>
        /// Describes every saved world for the dialog.
        /// </summary>
        /// <remarks>
        /// Each world is loaded to be counted, which is the only way to know its pieces and agents:
        /// the source reports names, not sizes. Cheap enough here because the reads are against the
        /// local cache the subscription keeps current, and a player has a handful of worlds — but
        /// it is the reason this is built on demand rather than held.
        /// </remarks>
        List<WorldChoice> BuildWorldList()
        {
            var list = new List<WorldChoice>();
            List<string> names = worlds.Names();

            if (names == null)
            {
                return list;
            }

            for (int i = 0; i < names.Count; i++)
            {
                WorldSnapshot snapshot = worlds.Load(names[i]);

                if (snapshot == null)
                {
                    // Named but no longer loadable. Leaving it out beats offering a row that
                    // cannot open.
                    continue;
                }

                list.Add(new WorldChoice(
                    names[i],
                    snapshot.PieceCount(),
                    snapshot.AgentCount()));
            }

            return list;
        }

        void OnWorldChosen(string worldName)
        {
            if (string.IsNullOrEmpty(worldName))
            {
                return;
            }

            // Recorded first, so the game scene finds it already current. The intent carries the
            // same name as a belt-and-braces for the case where the backend refuses the write —
            // the player still gets the world they asked for, it simply is not remembered.
            if (selection != null)
            {
                selection.SetCurrent(worldName);
            }

            if (worldIntent != null)
            {
                worldIntent.RequestWorld(worldName);
            }

            EnterGameScene(worldName);
        }

        /// <summary>
        /// Starts the load into the game scene.
        /// </summary>
        /// <remarks>
        /// The same two guards <c>ToLoad</c> applies, and for the same reasons: LoadingService
        /// throws on a second concurrent load, and entering a game scene before the required
        /// preprocesses have succeeded is what the readiness gate exists to prevent. This path
        /// exists because the load is triggered by a dialog rather than by a button that could
        /// carry a <c>ToLoad</c> of its own.
        /// </remarks>
        void EnterGameScene(string worldName = null)
        {
            if (loading == null || string.IsNullOrEmpty(gameSceneName))
            {
                return;
            }

            if (loading.IsLoading)
            {
                return;
            }

            if (readiness != null && !readiness.AllRequiredSucceeded)
            {
                return;
            }

            _ = EnterGameSceneAsync(worldName);
        }

        /// <summary>
        /// Loads the game scene behind the loading screen.
        /// </summary>
        /// <remarks>
        /// The world's own name is the title, which is what the design asks for — the player is
        /// told which floor is being built, not merely that something is loading.
        /// <para>
        /// One step, because one step is all this can honestly drive. The scene load reports real
        /// progress; building the world out of the save happens in the game scene, after this has
        /// handed over, and a second step here would be a bar moving on a guess. Wiring that up
        /// means the game scene taking <see cref="ILoadingScreen.Current"/> and holding it until
        /// the floor is actually placed.
        /// </para>
        /// </remarks>
        async Awaitable EnterGameSceneAsync(string worldName)
        {
            LoadingRun run = null;

            if (loadingScreen != null)
            {
                bool named = !string.IsNullOrEmpty(worldName);

                run = loadingScreen.Begin(new LoadingRequest(
                    named ? worldName.ToUpperInvariant() : "NEW FLOOR",
                    named ? "Building the world from your last save." : "Clearing a floor for you.",
                    new[] { new LoadingStepPlan("LOADING SCENE", "Scene ready") },
                    cancellable: false,
                    tip: "One button, bottom-left. Tab opens Load, Save, Inventory and Shop — and "
                        + "the same key closes them."));

                run.Advance("LOADING SCENE");
            }

            Action<SceneLoadProgress> onProgress = null;

            if (run != null)
            {
                // The service already throttles to whole percents, so this is not the per-frame
                // firehose it looks like.
                onProgress = progress =>
                {
                    if (run.Current != null)
                    {
                        run.Current.Report(progress.Normalized);
                    }
                };

                loading.ProgressChanged += onProgress;
            }

            try
            {
                await loading.LoadAsync(SceneLoadRequest.Single(gameSceneName));
            }
            catch (OperationCanceledException)
            {
                // Shutting down mid-load. Nothing to report.
            }
            catch (Exception exception)
            {
                // Awaited now rather than fired and forgotten, so a failure here would otherwise
                // vanish into an unobserved task and leave the screen up for ever.
                Debug.LogException(exception, this);
            }
            finally
            {
                if (onProgress != null)
                {
                    loading.ProgressChanged -= onProgress;
                }

                if (run != null)
                {
                    run.Complete();
                }
            }
        }

        /// <summary>
        /// Marks the next game scene as a blank floor, then lets the click carry on.
        /// </summary>
        /// <remarks>
        /// The scene load itself is <c>ToLoad</c>'s job, on the same button. This runs first — both
        /// are <c>onClick</c> listeners and this one is registered in <c>Awake</c> while ToLoad
        /// registers in <c>Start</c> — but the ordering is not load-bearing: <c>LoadAsync</c> is
        /// asynchronous, so the flag is set well before the new scene's objects wake.
        /// </remarks>
        void OnNewWorldClicked()
        {
            if (worldIntent != null)
            {
                worldIntent.RequestBlank();
            }

            NewWorldRequested?.Invoke();
        }

        static void Bind(Button button, Action handler)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.AddListener(() => handler());
        }

        /// <summary>
        /// Shows the continue card for a world the player can resume.
        /// </summary>
        /// <param name="worldName">Display name of the world.</param>
        /// <param name="pieces">How many placed pieces it contains.</param>
        /// <param name="agents">How many agents it contains.</param>
        /// <param name="savedAt">Save time, already formatted for display (e.g. "today 14:02").</param>
        public void ShowContinueWorld(string worldName, int pieces, int agents, string savedAt)
        {
            SetActive(continueCard, true);

            // By its own name. "Your World" is only for a player with no world yet - see
            // ShowDefaultWorld.
            SetText(continueWorldName, worldName);

            // The save time is optional because WorldSnapshot does not carry one. Rather than print
            // "saved " followed by nothing, the line is simply left off.
            string meta = Count(pieces, "piece") + "  ·  " + Count(agents, "agent");
            if (!string.IsNullOrEmpty(savedAt))
            {
                meta += "\nsaved " + savedAt;
            }

            SetText(continueWorldMeta, meta);

            // The world as the player last left it. None yet - a save from before pictures, or one
            // that will not read - leaves the box empty.
            ShowPicture(Pictures.Read(worldName), true);
        }

        /// <summary>"1 agent", "2 agents".</summary>
        static string Count(int count, string thing) => count + " " + thing + (count == 1 ? string.Empty : "s");

        /// <summary>Hides the continue card, for a player with no world in progress.</summary>
        public void ShowNoContinueWorld()
        {
            SetActive(continueCard, false);
            ShowPicture(null, false);
        }

        /// <summary>
        /// Puts a picture in the card's box, or empties it, destroying the last one if it was ours.
        /// </summary>
        void ShowPicture(Texture2D picture, bool owned)
        {
            if (ownedPicture != null && ownedPicture != picture)
            {
                Destroy(ownedPicture);
            }

            ownedPicture = owned ? picture : null;

            if (continuePicture == null)
            {
                return;
            }

            continuePicture.texture = picture;
            continuePicture.enabled = picture != null;
        }

        /// <summary>
        /// The continue card for a player with no saved world: START opens the default world from
        /// the content pack, fitted to their own agents and manager.
        /// </summary>
        /// <remarks>
        /// Only the piece count is shown. The template's agent count is its places, not this
        /// player's agents, and saying "2 agents" to somebody with none would be wrong. With no
        /// default world in the content the card still shows, and the game falls back to asking.
        /// </remarks>
        void ShowDefaultWorld()
        {
            var template = Placement.DefaultWorld.Read(content);

            SetActive(continueCard, true);
            SetText(continueWorldName, YourWorld);
            SetText(continueWorldMeta, template != null
                ? Count(template.PieceCount(), "piece")
                : string.Empty);

            Texture2D picture = null;
            if (content != null)
            {
                content.TryGet(DefaultWorldPicture, out picture);
            }

            ShowPicture(picture, false);
        }

        /// <summary>
        /// Tells the screen how many saved worlds exist, which decides whether Load World can be
        /// pressed.
        /// </summary>
        /// <remarks>
        /// Load World is hidden whatever the count (2026-09-29): Recent Worlds lists every world and
        /// opens the one pressed. New World is gated only by the first-time tour - see
        /// <see cref="ApplyFirstTimeState"/>.
        /// </remarks>
        /// <param name="count">How many saved worlds the player has.</param>
        public void SetSavedWorlds(int count)
        {
            hasSavedWorlds = count > 0;
            ApplyLoadWorldState();
        }

        /// <summary>
        /// Hides New World until the player has finished or skipped the first-time tour, so a new
        /// player's one way in is START, which the tour follows.
        /// </summary>
        /// <remarks>
        /// Read once, in <c>Awake</c>. The tour runs in the game scene, and the Lobby is loaded
        /// afresh when the player comes back from it, so there is nothing to listen for here.
        /// </remarks>
        void ApplyFirstTimeState()
        {
            bool touring = !UI.FtueTour.Done;

            if (newWorldButton != null)
            {
                SetActive(newWorldButton.gameObject, !touring);
            }
        }

        void ApplyLoadWorldState()
        {
            // Hidden, whatever is saved: Recent Worlds is how a world is picked, and a button that
            // is never on only asks why. Kept, wired, rather than removed, so bringing it back is
            // showing it here.
            if (loadWorldButton != null)
            {
                SetActive(loadWorldButton.gameObject, false);
            }
        }

        /// <summary>
        /// Reads the player's saved worlds and sets both world-dependent parts of the screen: the
        /// continue card, and whether Load World can be pressed.
        /// </summary>
        /// <remarks>
        /// Driven off <see cref="IAppReadiness"/> rather than polling. <see cref="IWorldSource"/> is
        /// a synchronous cache filled by a subscription, and the connection preprocess that fills it
        /// is the same one the readiness gate waits on — so by the time the menu is allowed to light
        /// up, the cache has its first snapshot.
        /// <para>
        /// <c>IsReady</c> false means "do not know", not "no worlds", which is why it hides the card
        /// rather than reporting an empty library. Telling a player with a saved world that they
        /// have none is worse than telling them nothing.
        /// </para>
        /// </remarks>
        void ApplyWorlds()
        {
            if (worlds == null || !worlds.IsReady)
            {
                // No database answer: the local file is the honest fallback, the same one the Game
                // scene opens from when the database is not ready. Hiding a world that is sitting
                // on disk tells the player it is gone.
                ApplyLocalWorlds();
                return;
            }

            List<string> names = worlds.Names();
            SetSavedWorlds(names == null ? 0 : names.Count);

            string current = worlds.Current();
            if (string.IsNullOrEmpty(current))
            {
                ShowDefaultWorld();
                return;
            }

            WorldSnapshot snapshot = worlds.Load(current);
            if (snapshot == null)
            {
                // Named as current but no longer loadable — deleted from under us, or a name the
                // store no longer recognises. The game falls back to the default world for it, so
                // that is what the card offers.
                ShowDefaultWorld();
                return;
            }

            ShowContinueWorld(
                current,
                snapshot.PieceCount(),
                snapshot.AgentCount(),
                null);
        }

        /// <summary>The continue card, from the local world file.</summary>
        void ApplyLocalWorlds()
        {
            Placement.WorldLibrary library;

            try
            {
                library = new Placement.WorldLibraryFile(null).Read();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Lobby] Could not read the local world file: " + exception.Message);
                ShowDefaultWorld();
                SetSavedWorlds(0);
                return;
            }

            // Load World stays off: its dialog lists worlds from the database only, so enabling it
            // here would give the player a button that does nothing.
            SetSavedWorlds(0);

            var current = library.CurrentName();
            var save = string.IsNullOrEmpty(current) ? null : library.Find(current);

            if (save == null)
            {
                ShowDefaultWorld();
                return;
            }

            ShowContinueWorld(
                save.name,
                save.PieceCount(),
                save.AgentCount(),
                SavedAt(save.savedUtc));
        }

        /// <summary>A save time for the card, or null when there is none to show.</summary>
        static string SavedAt(string savedUtc)
        {
            if (string.IsNullOrEmpty(savedUtc)
                || !DateTime.TryParse(savedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var utc))
            {
                return null;
            }

            var local = utc.ToLocalTime();

            return (local.Date == DateTime.Today ? "today " : local.ToString("d MMM ")) + local.ToString("HH:mm");
        }

        /// <summary>
        /// Pushes the player's content keys to the pack strip, or empties it while the entitlement
        /// list is still the local seed rather than the session's answer.
        /// </summary>
        void ApplyEntitlements()
        {
            if (packChips == null)
            {
                return;
            }

            if (entitlements == null || !entitlements.IsReady)
            {
                packChips.Clear();
                return;
            }

            packChips.Show(entitlements.Keys);
        }

        /// <summary>
        /// Enables or disables every navigation control at once, for use while a load is running.
        /// Quit stays available: a player who cannot leave a hung screen has no way out.
        /// </summary>
        public void SetNavigationInteractable(bool interactable)
        {
            navigationInteractable = interactable;

            SetInteractable(resumeButton, interactable);
            SetInteractable(newWorldButton, interactable);
            SetInteractable(contentPacksButton, interactable);
            SetInteractable(settingsButton, interactable);

            // Not a plain assignment: Load World also depends on whether the player has any saves,
            // so re-enabling navigation must not revive a button that should stay dead.
            ApplyLoadWorldState();
        }

        static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
            {
                target.SetActive(active);
            }
        }

        static void SetText(TextMeshProUGUI label, string value)
        {
            if (label != null)
            {
                label.text = value;
            }
        }

        static void SetInteractable(Button button, bool interactable)
        {
            if (button != null)
            {
                button.interactable = interactable;
            }
        }
    }
}
