using System;
using System.Collections.Generic;
using System.Threading;
using Dojo.Framework.Auth;
using Dojo.Framework.Content;
using Dojo.Framework.Inventory;
using Dojo.Framework.Net;
using Dojo.Framework.Session;
using Dojo.Framework.Startup;
using Dojo.Framework.UI;
using Dojo.Framework.Utilities;
using Dojo.Framework.World;
using Dojo.Game.Managers;
using UnityEngine;
using VContainer.Unity;

namespace Dojo.Game.Systems
{
    /// <summary>
    /// The app's opening move, driven by <see cref="RootLifetimeScope"/>. VContainer calls
    /// <see cref="Start"/> only after the container is fully built, so every dependency here is
    /// guaranteed constructed — which a MonoBehaviour's Start cannot promise.
    /// </summary>
    /// <remarks>
    /// Two things start here and they deliberately do not wait for each other. The Lobby loads
    /// immediately, because nothing blocks the Lobby; the startup preprocesses run behind it and
    /// report into <see cref="IAppReadiness"/>, which is what the Lobby's entry points gate on.
    /// A player therefore always gets a UI, even when content or the backend is unreachable — they
    /// get a UI and an explanation, rather than a black screen.
    /// </remarks>
    public sealed class AppStartup : IStartable, IDisposable
    {
        /// <summary>
        /// Readiness key for the server connection that says what this player is entitled to.
        /// Required: without it there is no way to know which content sets to fetch.
        /// </summary>
        public const string ConnectionPreprocess = "connection";

        /// <summary>Readiness key for the content download. Required: no content, no game scene.</summary>
        public const string ContentPreprocess = "content";

        /// <summary>
        /// Readiness key for WorkOS sign-in. Not required: with WorkOS unavailable the game carries
        /// on anonymously. With it available, startup waits here - on the saved session, or on the
        /// player's choice of a way in.
        /// </summary>
        public const string AuthPreprocess = "auth";

        /// <summary>
        /// Readiness key for the player's agent roster, read from hiro-ai-api with their WorkOS
        /// token. Required: the agents are the game.
        /// </summary>
        /// <remarks>
        /// Declared here because the Lobby's gate reads every required key through
        /// <c>IAppReadiness</c>, but marked by <c>CommunicationStartUp</c>, which is the entry
        /// point that actually performs the refresh. Required is the whole point of it: a player
        /// shown somebody else's agents, or none, would have no way to tell that from a working
        /// game, so the Lobby stays disabled and says why instead.
        /// </remarks>
        public const string RosterPreprocess = "roster";

        readonly ILoadingService loadingService;
        readonly ISessionService session;
        readonly IWorkOSAuthService workOSAuth;
        readonly SignInGate signInGate;

        /// <summary>Set once the Lobby has loaded: the sign-in cards live there, so nothing asks for them before.</summary>
        readonly AwaitableCompletionSource lobbyReady = new AwaitableCompletionSource();

        bool lobbyLoaded;

        /// <summary>
        /// The entitlement gate, as the concrete service rather than an interface.
        /// </summary>
        /// <remarks>
        /// Deliberate. <c>AdoptSession</c> is on neither <see cref="IEntitlements"/> nor
        /// <see cref="IEntitlementGrants"/>, so the one class permitted to seed the gate is the one
        /// class that has to name it — which makes that permission visible in this constructor
        /// instead of hidden behind an interface everything else holds.
        /// </remarks>
        readonly EntitlementService entitlements;

        readonly IContentService content;
        readonly IItemCatalog itemCatalog;
        readonly IAppReadiness readiness;
        readonly ICatalogPublisher catalogPublisher;
        readonly string lobbySceneName;
        readonly CancellationTokenSource cancellation = new CancellationTokenSource();

        /// <summary>
        /// The screen the player watches while all of this happens.
        /// </summary>
        /// <remarks>
        /// Startup is the one load nothing else can cover: there is no scene on screen yet, so
        /// without this the player waits on a black window. The run is begun before the first
        /// await and completed in a finally, so no failure path can leave it up.
        /// </remarks>
        readonly ILoadingScreen loadingScreen;

        /// <summary>
        /// Asked first, before anything that needs a connection: whether there is one.
        /// </summary>
        /// <remarks>
        /// Without it an offline start opened a WorkOS sign-in browser onto nothing, then failed
        /// the server, the content and the roster one after another, each with its own complaint.
        /// </remarks>
        readonly NetworkManager network;

        /// <summary>The popup every startup failure is told in.</summary>
        readonly PopupPrefabs popups;

        /// <summary>Whether a database is meant to be there at all.</summary>
        readonly SpacetimeSettings spacetime;

        /// <summary>
        /// The database's saved worlds. Read only for whether its first snapshot arrived, which is
        /// the one sign that the database answered.
        /// </summary>
        readonly IWorldSource worlds;

        /// <summary>The startup run, from <see cref="Start"/> until the Lobby is up and ready.</summary>
        LoadingRun run;

        /// <summary>How far the online half has got. See <see cref="Stage"/>.</summary>
        Stage stage;

        /// <summary>
        /// How many startup failures in a row, so a second one can say "still".
        /// </summary>
        int failures;

        /// <summary>
        /// The parts of the online half, in order, so a Retry picks up at the one that failed.
        /// </summary>
        /// <remarks>
        /// Not from the top: once the server has answered, <c>AdoptSession</c> has been called, and
        /// it refuses a second call.
        /// </remarks>
        enum Stage
        {
            SignIn,
            Connect,
            Content,
        }

        public AppStartup(
            ILoadingService loadingService,
            ILoadingScreen loadingScreen,
            ISessionService session,
            EntitlementService entitlements,
            IContentService content,
            IItemCatalog itemCatalog,
            IAppReadiness readiness,
            ICatalogPublisher catalogPublisher,
            IWorkOSAuthService workOSAuth,
            SignInGate signInGate,
            NetworkManager network,
            PopupPrefabs popups,
            SpacetimeSettings spacetime,
            IWorldSource worlds,
            string lobbySceneName)
        {
            this.loadingService = loadingService;
            this.loadingScreen = loadingScreen;
            this.session = session;
            this.entitlements = entitlements;
            this.content = content;
            this.itemCatalog = itemCatalog;
            this.readiness = readiness;
            this.catalogPublisher = catalogPublisher;
            this.workOSAuth = workOSAuth;
            this.signInGate = signInGate;
            this.network = network;
            this.popups = popups;
            this.spacetime = spacetime;
            this.worlds = worlds;
            this.lobbySceneName = lobbySceneName;
        }

        public void Start()
        {
            // Declared before anything runs, so the gate is shut from the first frame. A ToLoad
            // button that appeared before this line would briefly be enabled with no content behind
            // it, and a player is quite fast enough to click it.
            readiness.Declare(ConnectionPreprocess, required: true);
            readiness.Declare(ContentPreprocess, required: true);
            readiness.Declare(AuthPreprocess, required: false);
            readiness.Declare(RosterPreprocess, required: true);

            // Up before either task starts, so the very first frame is the loading screen rather
            // than an empty window.
            run = loadingScreen.Begin(StartupRequest());

            _ = EnterLobbyAsync();
            _ = RunPreprocessesAsync();
        }

        /// <summary>What the loading screen shows for a startup run, first time or after Retry.</summary>
        /// <remarks>
        /// Weights, not an equal share each: the content preload is far and away the longest, and
        /// a bar that gives it the same share as reading the catalogue appears to hang for most of
        /// a startup. The connection check is the lightest of all - it is one request.
        /// </remarks>
        static LoadingRequest StartupRequest()
        {
            return new LoadingRequest(
                "HIROLABZ STUDIO",
                "Getting your studio ready.",
                new[]
                {
                    new LoadingStepPlan("CHECKING CONNECTION", "Online", 0.5f),
                    new LoadingStepPlan("CONNECTING", "Connected", 1f),
                    new LoadingStepPlan("FETCHING CONTENT", "Content ready", 6f),
                    new LoadingStepPlan("READING CATALOGUE", "Catalogue read", 1f),
                    new LoadingStepPlan("ENTERING LOBBY", "Lobby ready", 1f),
                },
                cancellable: false,
                tip: "One button, bottom-left. Tab opens Load, Save, Inventory and Shop — and the "
                    + "same key closes them.");
        }

        /// <remarks>
        /// Holds the screen for as long as the scene is streaming. This runs alongside the
        /// preprocesses rather than after them, so the two finish in no fixed order — the hold is
        /// what stops whichever finishes first from pulling the screen out from under the other.
        /// <para>
        /// A Lobby that will not load is told in the popup, because the Lobby is where every other
        /// failure is explained. It is marked loaded only once it has, so sign-in waits for a Retry
        /// rather than for a card that is not there.
        /// </para>
        /// </remarks>
        async Awaitable EnterLobbyAsync()
        {
            IDisposable hold = run == null || run.IsComplete ? null : run.Hold();

            try
            {
                await loadingService.LoadAsync(
                    SceneLoadRequest.Single(lobbySceneName),
                    cancellation.Token);

                lobbyLoaded = true;
                lobbyReady.TrySetResult();
            }
            catch (OperationCanceledException)
            {
                // Application is shutting down mid-load; nothing to do.
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                ShowStartupFailed(() => _ = EnterLobbyAsync());
            }
            finally
            {
                if (hold != null)
                {
                    hold.Dispose();
                }
            }
        }

        /// <summary>
        /// Runs the required preprocesses in order, behind the Lobby - or, with no connection, none
        /// of them.
        /// </summary>
        /// <remarks>
        /// The connection check goes first of all, because the first thing after it is sign-in,
        /// and everything after that needs the internet too. Offline, nothing is
        /// tried: <see cref="GoOffline"/> leaves the Lobby locked and offers a Retry.
        /// <para>
        /// Failures are caught rather than left to fault the task: an unobserved exception on a
        /// fire-and-forget call would be swallowed by the runtime, and the player would see a Lobby
        /// whose buttons never enable and no reason why.
        /// </para>
        /// </remarks>
        async Awaitable RunPreprocessesAsync()
        {
            try
            {
                Step("CHECKING CONNECTION");
                var connection = await network.FirstAnswerAsync(cancellation.Token);

                if (connection != ConnectionState.Online)
                {
                    GoOffline();
                    return;
                }

                await RunOnlineAsync();
            }
            catch (OperationCanceledException)
            {
                // Shutting down mid-download. Not a failure worth reporting to anyone.
            }
            catch (Exception exception)
            {
                FailUnexpectedly(exception);
            }
            finally
            {
                // Every path out of here, including the offline one and both catches. A startup
                // that fails still has to give the player their screen back - a failed or offline
                // start leaves the Lobby up with its buttons disabled and a reason to read, which is
                // useless underneath a loading screen that never goes away.
                if (run != null)
                {
                    run.Complete();
                }
            }
        }

        /// <summary>
        /// No connection: the Lobby comes up locked, and a popup offers a Retry.
        /// </summary>
        /// <remarks>
        /// Only the connection step is marked failed, which is what locks the Lobby and gives its
        /// buttons a reason. Sign-in, content and the roster are left pending - nothing of them was
        /// tried, so readiness stands at zero. Pending rather than failed matters for the roster:
        /// <c>CommunicationStartUp</c> sets off to fetch agents the moment content settles either
        /// way, and a failed content step would send it out with no connection to raise a second,
        /// different complaint on top of this one. Pending, it simply waits, and carries on by
        /// itself once a Retry has brought the content in.
        /// </remarks>
        void GoOffline()
        {
            readiness.MarkFailed(ConnectionPreprocess, NoConnection);

            Debug.LogWarning("[Startup] No internet connection, so there is no sign-in, no server and "
                + "no content. The Lobby stays locked until Retry finds a connection.");

            ShowOffline(still: false);
        }

        const string NoConnection = "No internet connection.";

        /// <summary>
        /// Says there is no connection, from the Affirmation popup prefab: one button, RETRY, in
        /// red, since it is an error. Every popup comes from a prefab; none is built in code.
        /// </summary>
        /// <param name="still">A Retry found no connection either: says so.</param>
        void ShowOffline(bool still)
        {
            if (popups == null || popups.Affirmation == null)
            {
                Debug.LogWarning("[Startup] No Affirmation popup prefab on the RootLifetimeScope, so "
                    + "being offline is shown only as the Lobby's locked buttons.");
                return;
            }

            var layer = PopupManager.Instance;

            if (layer == null || ShuttingDown)
            {
                return;   // Play has stopped: nobody to tell
            }

            var popup = layer.Show(popups.Affirmation)
                .Tint(MessagePopup.Tone.Alert)
                .Present(
                    still ? "Still no connection" : "No internet connection",
                    still
                        ? "Check that you are online, then try again."
                        : "Dojo needs a connection to sign you in and load your studio. Check your "
                            + "network, then try again.")
                .Caption(MessagePopup.Choice.Primary, "RETRY");

            popup.Chosen += choice => _ = RetryAsync();
        }

        /// <summary>
        /// The popup's Retry: check again, and if the connection is back, carry on from sign-in.
        /// </summary>
        /// <remarks>
        /// The popup closes with the press, as every popup does; the loading screen says the
        /// connection is being checked. Still offline, the popup comes back and says so. Online,
        /// the whole online half runs again under that loading screen, exactly as it would have at
        /// startup - the same code, so a retried start cannot end up different from a first one.
        /// </remarks>
        async Awaitable RetryAsync()
        {
            run = loadingScreen.Begin(StartupRequest());
            Step("CHECKING CONNECTION");

            ConnectionState connection;

            try
            {
                connection = await network.CheckNowAsync(cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (connection != ConnectionState.Online)
            {
                if (run != null && !run.IsComplete)
                {
                    run.Complete();
                }

                ShowOffline(still: true);
                return;
            }

            try
            {
                await RunOnlineAsync();
            }
            catch (OperationCanceledException)
            {
                // Shutting down. Nothing to report.
            }
            catch (Exception exception)
            {
                FailUnexpectedly(exception);
            }
            finally
            {
                if (run != null)
                {
                    run.Complete();
                }
            }
        }

        /// <summary>
        /// Everything that needs a connection, in order: sign-in, the server, the content and the
        /// catalogue. The same whether it runs at startup or after a Retry.
        /// </summary>
        /// <remarks>
        /// Sign-in goes first, ahead of even content, so the connection preprocess has a token to
        /// use the moment it runs. Content is next and on its own await, because everything after
        /// it needs the catalogue resolvable. The SpacetimeDB session and connection chain —
        /// ARCH-001 steps 3 and 4 — go after the content block, as a second required preprocess.
        /// A failure the steps expect is told in the popup where it happens; anything else throws,
        /// and the callers report it.
        /// </remarks>
        async Awaitable RunOnlineAsync()
        {
            stage = Stage.SignIn;

            // ── 0. Sign in ──────────────────────────────────────────────────────────────────
            // The saved session first, which never opens a browser. Only with none does the player
            // see the Sign in card, and only their button press opens the browser.
            readiness.MarkRunning(AuthPreprocess);

            var token = await workOSAuth.GetTokenAsync(cancellation.Token, interactive: false);

            if (workOSAuth.IsAvailable && string.IsNullOrEmpty(token))
            {
                await SignInWithPlayerAsync();
            }
            else
            {
                // Signed in already - or WorkOS unavailable (not configured, or no backend), which
                // carries on anonymously as before. Either way the menu is the way in.
                signInGate.MarkSignedIn();
            }

            readiness.MarkSucceeded(AuthPreprocess);

            await ConnectAndFetchAsync();

            // ARCH-001 steps 3-4 chain here: session, then connection, as a second required
            // preprocess. Content is already down by this point, which is the ordering that lets
            // the bridge resolve an item_def row straight to an asset.
        }

        /// <summary>
        /// Connects to the server and the database, adopts what the server says the player has,
        /// then fetches it.
        /// </summary>
        /// <remarks>
        /// Apart from sign-in so a Retry can start here. A failure stops before
        /// <c>AdoptSession</c>, which is what leaves the Retry free to call it.
        /// <para>
        /// Only the connection is marked failed. Content stays pending, as it does offline, so
        /// <c>CommunicationStartUp</c> waits for it instead of reading the roster on a connection
        /// that is not there and raising a second popup over this one.
        /// </para>
        /// </remarks>
        async Awaitable ConnectAndFetchAsync()
        {
            stage = Stage.Connect;

            // ── 1. Connect to the server ────────────────────────────────────────────────────
            readiness.MarkRunning(ConnectionPreprocess);
            Step("CONNECTING");
            var result = await session.ConnectAsync(cancellation.Token);

            var error = !result.Connected
                ? (string.IsNullOrEmpty(result.Error) ? NoServer : result.Error)
                : DatabaseUnreachable() ? NoDatabase : null;

            if (error != null)
            {
                // No connection means no entitlements, and downloading a guessed set would be worse
                // than downloading nothing: the player would enter a world furnished with things
                // they may not own. Stop here, keep the gate shut, and say so.
                readiness.MarkFailed(ConnectionPreprocess, error);

                Debug.LogWarning("[Startup] " + error + " The Lobby stays locked until Retry gets through.");

                ShowStartupFailed(() => _ = ResumeAsync());
                return;
            }

            readiness.MarkSucceeded(ConnectionPreprocess);

            // ── 2-3. Use the server's answer, and keep it ───────────────────────────────────
            // Handed to the entitlement service rather than written back to the settings object.
            // The prefab's list is only what to believe before anyone has been identified; from
            // here on the server's answer is the one that counts, and it lives in one place that
            // nothing else can write. This is the only call site of AdoptSession, and the service
            // refuses a second one.
            entitlements.AdoptSession(result);

            // ── 4-6. Download the content, read it, tell the backend ────────────────────────
            await FetchContentAsync();
        }

        const string NoServer = "Could not connect to the server.";
        const string NoDatabase = "Could not reach the database.";

        /// <summary>
        /// The database is switched on, but its first snapshot never arrived.
        /// </summary>
        /// <remarks>
        /// Asked here because nothing else would tell. The session's owned-pack read is what opens
        /// the database connection, and it answers an empty list when the host is down - so the
        /// session still connected, the player got the starter pack only, and the Lobby quietly
        /// fell back to the world file on disk. Switched off is not a failure, and is not asked.
        /// </remarks>
        bool DatabaseUnreachable()
        {
            return spacetime != null && spacetime.Enabled && (worlds == null || !worlds.IsReady);
        }

        /// <summary>
        /// Downloads the content the server said this player has, then reads its catalogue. A
        /// download that fails tells the player in a popup that offers a Retry.
        /// </summary>
        /// <remarks>
        /// Apart from <see cref="RunOnlineAsync"/> so the Retry can run it alone. Running the whole
        /// online half again is not an option here: sign-in and the server have already answered,
        /// and <c>AdoptSession</c> refuses a second call. The preload skips sets that are already
        /// resident, so a Retry fetches only what is still missing.
        /// <para>
        /// Only <see cref="ContentException"/> is caught: it is the one failure the content service
        /// promises, and it covers both a catalogue that will not load and a pack that will not
        /// download. Anything else is a bug, and goes on to the callers' catch as before.
        /// </para>
        /// </remarks>
        async Awaitable FetchContentAsync()
        {
            stage = Stage.Content;

            // ── 4. Load the addressables the server said this player has ───────────────────
            readiness.MarkRunning(ContentPreprocess);

            // Two jobs rather than one step reported twice: initialising Addressables and pulling
            // the packs down are separate waits with very different lengths, and the step's own
            // number is their weighted mean.
            LoadingStep fetching = Step("FETCHING CONTENT");
            LoadingJob init = fetching == null ? null : fetching.AddJob("addressables", 1f);
            LoadingJob packs = fetching == null ? null : fetching.AddJob("packs", 5f);

            try
            {
                await content.InitializeAsync(cancellation.Token);

                if (init != null)
                {
                    init.Complete();
                }

                if (run != null)
                {
                    run.SetDetail(Describe(entitlements.Keys));
                }

                await content.PreloadAsync(entitlements.Keys, cancellation.Token);
            }
            catch (ContentException exception)
            {
                // The content service has already logged the URL and the Addressables error.
                readiness.MarkFailed(ContentPreprocess, exception.Message);

                Debug.LogWarning("[Startup] The content could not be downloaded, so the Lobby stays "
                    + "locked until Retry brings it in.");

                ShowStartupFailed(() => _ = ResumeAsync());
                return;
            }

            if (packs != null)
            {
                packs.Complete();
            }

            // ── 5. Read what arrived ──────────────────────────────────────────────────────
            Step("READING CATALOGUE");
            // The catalogue is built from manifests the preload just made resident, so it is
            // refreshed here rather than on a timer or lazily on first use. A pack bought
            // mid-session takes the same two lines: preload it, then refresh.
            itemCatalog.Refresh();

            readiness.MarkSucceeded(ContentPreprocess);
            Step("ENTERING LOBBY");

            // Through: the next failure, if there is one, is a first one again.
            failures = 0;

            // ── 6. Tell the backend what arrived ──────────────────────────────────────────
            // After the gate opens, not before, and not awaited into it. The manifest ships inside
            // the pack, so this client is the first thing in the system that knows what the pack
            // contains — but nothing the player does depends on the server having been told. A
            // publish that fails leaves a working game and a server slightly behind.
            _ = PublishBackendStateAsync();
        }

        /// <summary>
        /// Tells the player startup failed: QUIT on the left, RETRY on the right.
        /// </summary>
        /// <remarks>
        /// One popup for every startup failure - server, database, content, the Lobby itself, or
        /// anything unexpected - and the same Confirmation popup and buttons as the roster's "Could
        /// not load your agents", so every failure looks and behaves alike. A second failure in a
        /// row says "still", so a Retry that changed nothing does not look as though nothing
        /// happened. With the prefab missing there is no popup, only the locked Lobby and its reason.
        /// </remarks>
        void ShowStartupFailed(Action retry)
        {
            failures++;

            if (popups == null || popups.Confirmation == null)
            {
                Debug.LogWarning("[Startup] No Confirmation popup prefab on the RootLifetimeScope, so "
                    + "the startup failure is shown only as the Lobby's locked buttons.");
                return;
            }

            var again = failures > 1;
            var layer = PopupManager.Instance;

            if (layer == null || ShuttingDown)
            {
                return;   // Play has stopped: nobody to tell
            }

            // The Confirmation's buttons, in red: an error.
            var popup = layer.Show(popups.Confirmation)
                .Tint(MessagePopup.Tone.Alert)
                .Present(
                    again ? "Still having trouble" : "Something went wrong",
                    again ? StillFailedBody : FailedBody)
                .Caption(MessagePopup.Choice.Cancel, "QUIT")
                .Caption(MessagePopup.Choice.Primary, "RETRY");

            popup.Chosen += choice =>
            {
                if (choice == MessagePopup.Choice.Primary)
                {
                    retry();
                }
                else
                {
                    Quit();
                }
            };
        }

        // Universal on purpose: the player cannot act on which server, database or pack failed.
        // The detail is in the log.
        const string FailedBody =
            "Sorry, we couldn't load the game. Please check your connection and try again.";

        const string StillFailedBody =
            "We still couldn't load the game. Please wait a moment and try again.";

        /// <summary>
        /// An error none of the steps expected. Logged in full, and told in the same popup.
        /// </summary>
        /// <remarks>
        /// The preprocess marked failed is the one that was running, rather than always content as
        /// before: an error during sign-in or the connection reported as a failed download sent
        /// the reader to the wrong place, and set the roster off on a connection that was not there.
        /// </remarks>
        void FailUnexpectedly(Exception exception)
        {
            // Shutting down: whatever broke on the way out is not something to tell the player.
            if (ShuttingDown)
            {
                Debug.LogWarning("[Startup] A step failed while shutting down, so nothing is shown: " + exception.Message);
                return;
            }

            Debug.LogException(exception);

            var preprocess = stage == Stage.Content ? ContentPreprocess
                : stage == Stage.Connect ? ConnectionPreprocess
                : AuthPreprocess;

            readiness.MarkFailed(preprocess, exception.Message);
            ShowStartupFailed(() => _ = ResumeAsync());
        }

        /// <summary>
        /// The popup's Retry: the online half again from the stage that failed, under a fresh
        /// loading screen.
        /// </summary>
        /// <remarks>
        /// The startup screen again, moved straight past the steps that have already answered, so
        /// the bar picks up where the failure left it. A failure on the way shows the popup again.
        /// </remarks>
        async Awaitable ResumeAsync()
        {
            run = loadingScreen.Begin(StartupRequest());
            Step("CHECKING CONNECTION");   // answered already

            if (stage == Stage.Content)
            {
                Step("CONNECTING");        // connected already: straight on to the content
            }

            try
            {
                switch (stage)
                {
                    case Stage.SignIn:
                        await RunOnlineAsync();
                        break;

                    case Stage.Connect:
                        await ConnectAndFetchAsync();
                        break;

                    default:
                        await FetchContentAsync();
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                // Shutting down. Nothing to report.
            }
            catch (Exception exception)
            {
                FailUnexpectedly(exception);
            }
            finally
            {
                if (run != null)
                {
                    run.Complete();
                }
            }
        }

        /// <remarks>
        /// <c>Application.Quit</c> does nothing in the editor, so the editor stops Play instead -
        /// otherwise QUIT reads as a broken button on the one machine it is tested on.
        /// </remarks>
        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        const string SignInCancelled = "Sign-in cancelled.";
        const string SignInUnfinished = "Sign-in didn't finish. Try again.";

        /// <summary>
        /// No saved session: hands the screen to the Lobby's Sign in card and waits for the player
        /// to pick a way in. Returns once they are signed in.
        /// </summary>
        /// <remarks>
        /// The loading screen goes away first, or the card underneath could not be pressed. Each
        /// attempt has a loading screen of its own that Esc cancels, since the browser may be closed
        /// without finishing; a cancelled or unfinished attempt puts the card back up with the reason.
        /// Once signed in, the usual startup loading screen comes back for the rest - the same one
        /// a Retry uses.
        /// </remarks>
        async Awaitable SignInWithPlayerAsync()
        {
            if (!lobbyLoaded)
            {
                await lobbyReady.Awaitable;
            }

            var message = string.Empty;
            var choice = default(SignInChoice);
            var retry = false;   // RETRY on the popup: the same way in again, without the card

            while (true)
            {
                if (run != null && !run.IsComplete)
                {
                    run.Complete();
                }

                if (!retry)
                {
                    choice = await signInGate.WaitForChoiceAsync(message, cancellation.Token);
                }

                retry = false;
                run = loadingScreen.Begin(SignInRequest());

                string token;
                var attemptRun = run;
                var playerCancel = attemptRun != null ? attemptRun.CancellationToken : CancellationToken.None;

                using (var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token, playerCancel))
                {
                    // No browser tells Dojo it was closed. The player coming back to Dojo with the
                    // sign-in still open is the sign it was, so that is when the error shows.
                    MessagePopup unfinished = null;
                    var away = false;
                    var ended = false;

                    void GiveUp()
                    {
                        if (attemptRun != null)
                        {
                            attemptRun.Cancel();   // as Esc does
                        }
                        else
                        {
                            attempt.Cancel();
                        }
                    }

                    void OnFocusChanged(bool focused)
                    {
                        if (!focused)
                        {
                            away = true;   // gone to the browser
                            return;
                        }

                        if (away)
                        {
                            away = false;
                            _ = ShowIfStillOpenAsync();
                        }
                    }

                    async Awaitable ShowIfStillOpenAsync()
                    {
                        // A sign-in finished in the browser reaches Dojo before the player does,
                        // but its last step can still be running: give it a moment first.
                        try
                        {
                            await Awaitable.WaitForSecondsAsync(ReturnGraceSeconds, cancellation.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }

                        if (ended || unfinished != null)
                        {
                            return;
                        }

                        unfinished = ShowSignInUnfinished(
                            () =>
                            {
                                retry = true;
                                GiveUp();
                            },
                            GiveUp);

                        if (unfinished != null)
                        {
                            unfinished.Hidden += _ => unfinished = null;
                        }
                    }

                    Application.focusChanged += OnFocusChanged;

                    try
                    {
                        token = await workOSAuth.SignInAsync(choice.Route, choice.Email, attempt.Token);
                    }
                    finally
                    {
                        ended = true;
                        Application.focusChanged -= OnFocusChanged;

                        if (unfinished != null && unfinished.IsOpen)
                        {
                            unfinished.Hide();   // the attempt ended: nothing left to ask
                        }
                    }
                }

                cancellation.Token.ThrowIfCancellationRequested();

                if (!string.IsNullOrEmpty(token))
                {
                    signInGate.MarkSignedIn();

                    if (run != null && !run.IsComplete)
                    {
                        run.Complete();
                    }

                    run = loadingScreen.Begin(StartupRequest());
                    Step("CHECKING CONNECTION");   // already answered: straight on to connecting
                    return;
                }

                message = run != null && run.IsCancelled ? SignInCancelled : SignInUnfinished;
            }
        }

        /// <summary>What the loading screen shows while the browser is open.</summary>
        static LoadingRequest SignInRequest()
        {
            return new LoadingRequest(
                "SIGNING IN",
                "Finish signing in in your browser. Dojo carries on by itself once you have.",
                new[] { new LoadingStepPlan("WAITING FOR THE BROWSER", "Signed in", 1f) },
                cancellable: true,
                tip: "Closed the browser by mistake? Press Esc, then choose a way in again.");
        }

        /// <summary>How long a player back from the browser waits before the error shows.</summary>
        const float ReturnGraceSeconds = 1.5f;

        /// <summary>
        /// Tells a player who came back from the browser without signing in that it did not finish,
        /// from the Confirmation popup prefab in red, since it is an error: CANCEL or RETRY.
        /// </summary>
        /// <param name="retry">Ends this attempt and opens the browser again, the same way in.</param>
        /// <param name="cancel">Ends this attempt and puts the Sign in card back up.</param>
        /// <returns>The popup, or null when there is none to show.</returns>
        MessagePopup ShowSignInUnfinished(Action retry, Action cancel)
        {
            if (popups == null || popups.Confirmation == null)
            {
                Debug.LogWarning("[Startup] No Confirmation popup prefab on the RootLifetimeScope, so "
                    + "coming back from the browser without signing in shows nothing.");
                return null;
            }

            var layer = PopupManager.Instance;

            if (layer == null || ShuttingDown)
            {
                return null;   // Play has stopped: nobody to tell
            }

            var popup = layer.Show(popups.Confirmation)
                .Tint(MessagePopup.Tone.Alert)
                .Present(
                    "Sign-in didn't finish",
                    "The browser was closed before you signed in. Try again, or cancel to choose "
                        + "another way in.")
                .Caption(MessagePopup.Choice.Cancel, "CANCEL")
                .Caption(MessagePopup.Choice.Primary, "RETRY");

            popup.Chosen += choice =>
            {
                if (choice == MessagePopup.Choice.Primary)
                {
                    retry();
                }
                else
                {
                    cancel();
                }
            };

            return popup;
        }

        /// <summary>
        /// Moves the loading screen on to its next step, if there is a screen.
        /// </summary>
        /// <remarks>
        /// Null-tolerant so the startup sequence reads the same whether or not a screen is up. The
        /// label is passed rather than relied on from the plan so the wording lives beside the work
        /// it describes — a step renamed here cannot drift from the step it labels.
        /// </remarks>
        LoadingStep Step(string label)
        {
            return run == null ? null : run.Advance(label);
        }

        /// <summary>The line under the bar: what the player is actually being given.</summary>
        static string Describe(IReadOnlyList<string> keys)
        {
            int count = keys == null ? 0 : keys.Count;

            return count == 1 ? "1 pack" : count + " packs";
        }

        /// <summary>
        /// Registers the downloaded packs with the backend, if there is one.
        /// </summary>
        /// <remarks>
        /// Deliberately not a readiness preprocess. Gating the Lobby on a catalogue upload would
        /// shut the game for a reason that does not affect the player, and the publisher's own
        /// no-backend implementation makes "there is no server" the ordinary case rather than a
        /// failure.
        /// </remarks>
        async Awaitable PublishBackendStateAsync()
        {
            try
            {
                await catalogPublisher.PublishAsync(itemCatalog.All, cancellation.Token);

                // CommunicationStartUp publishes the reconciled registry after its SDK fetch.
            }
            catch (OperationCanceledException)
            {
                // Shutting down. Nothing to report.
            }
            catch (Exception exception)
            {
                // Swallowed on purpose: see the remarks above. Logged so it is still findable.
                Debug.LogWarning("[Startup] backend state could not be published: " + exception.Message);
            }
        }

        /// <summary>Whether the app is shutting down - Play stopped, in the editor.</summary>
        bool ShuttingDown => cancellation.IsCancellationRequested;

        /// <remarks>
        /// Cancelled, not disposed. Steps still in flight - a sign-in the browser was waiting on -
        /// finish after this, and read the token on their way out. Disposed, reading it threw
        /// ObjectDisposedException, which was taken for an unexpected failure and showed the
        /// "couldn't load the game" popup after Play had stopped. Cancelled, they see an ordinary
        /// cancellation and stop quietly. Nothing here holds anything that needs disposing.
        /// </remarks>
        public void Dispose()
        {
            cancellation.Cancel();
        }
    }
}
