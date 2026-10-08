using System;
using System.Collections.Generic;
using System.Threading;
using Dojo.Framework.Auth;
using Dojo.Framework.World;
using Dojo.Framework.Startup;
using Dojo.Framework.UI;
using Dojo.Game.InGame.Agents;
using Dojo.Game.Managers;
using Dojo.Game.Systems;
using Dojo.Hiro;
using UnityEngine;
using VContainer.Unity;

namespace Dojo.Game.Server
{
    /// <summary>
    /// Opens the line to the Hiro platform when the app starts, and brings the local agent roster
    /// in line with what it finds there.
    /// </summary>
    /// <remarks>
    /// A VContainer entry point on the root scope, so it runs once the container is fully built —
    /// which a MonoBehaviour's <c>Start</c> cannot promise. Modelled on <c>AppStartup</c>, which
    /// does the same thing for scene loading.
    /// <para>
    /// It owns the <em>reads</em>: listing what the platform has, and writing the result into the
    /// roster. It deliberately owns nothing about chatting — that is <see cref="IAgentChat"/>,
    /// reached by the panel that needs it — because a startup class that also brokered
    /// conversations would be the thing every future transport had to be threaded through.
    /// </para>
    /// <para>
    /// <b>Never blocks the game.</b> The sync is started and not waited on: an unreachable API, a
    /// missing key or an expired one all end with a log and a roster that still works locally. The
    /// alternative — a game that will not start because a web service is down — is a much worse
    /// failure than a chat that says it cannot connect.
    /// </para>
    /// </remarks>
    public sealed class CommunicationStartUp : IStartable, IDisposable
    {
        readonly IAgentRegistry registry;
        readonly AgentRosterFile roster;
        readonly IAppReadiness readiness;
        readonly IRosterPublisher rosterPublisher;
        readonly ManagerRosterFile managerRoster;

        /// <summary>
        /// How the player is told the roster could not be read.
        /// </summary>
        /// <remarks>
        /// Resolvable here because the DialogManager is registered on the root scope, not the
        /// Lobby's — so this entry point can raise a dialog without waiting for a scene, and
        /// without a back-reference from startup code into scene UI.
        /// </remarks>
        readonly IDialogManager dialogs;

        /// <summary>Who is signed in, so a refusal can name the account the platform does not know.</summary>
        readonly IWorkOSAuthService workOS;

        /// <summary>The popup the failure is told in. Without it, the plain confirm dialog stands in.</summary>
        readonly PopupPrefabs popups;

        /// <summary>Where REGISTER NOW sends a player whose account Hiro does not know.</summary>
        readonly HiroApiSettings hiro;

        readonly CancellationTokenSource cancellation = new CancellationTokenSource();

        public CommunicationStartUp(
            IAgentRegistry registry,
            AgentRosterFile roster,
            IAppReadiness readiness,
            IDialogManager dialogs,
            IRosterPublisher rosterPublisher,
            ManagerRosterFile managerRoster,
            IWorkOSAuthService workOS,
            PopupPrefabs popups,
            HiroApiSettings hiro)
        {
            this.registry = registry;
            this.roster = roster;
            this.readiness = readiness;
            this.dialogs = dialogs;
            this.rosterPublisher = rosterPublisher;
            this.managerRoster = managerRoster;
            this.workOS = workOS;
            this.popups = popups;
            this.hiro = hiro;
        }

        public void Start()
        {
            // Fire and forget, on purpose. See the remarks: nothing about starting the game waits
            // for a web service.
            _ = SyncAsync();
        }

        /// <summary>
        /// Blocks this sync — and only this sync — until the content preprocess has settled.
        /// </summary>
        /// <remarks>
        /// The local roster is downloaded content now, not a Resources asset, so reading it before
        /// the catalogue arrives finds nothing. Both this and <c>AppStartup</c> are entry points and
        /// VContainer does not order them, so without this wait the backup read races the download
        /// and loses — which is exactly the "No credentials at content address" error it produced.
        /// <para>
        /// Waiting here rather than making the game wait: the Lobby is already up throughout, and a
        /// failed content download resolves this immediately rather than hanging, so the promise
        /// that nothing blocks the game still holds.
        /// </para>
        /// </remarks>
        async Awaitable WaitForContentAsync()
        {
            if (readiness == null)
            {
                return;
            }

            while (true)
            {
                var state = readiness.StateOf(AppStartup.ContentPreprocess);
                if (state == PreprocessState.Succeeded || state == PreprocessState.Failed)
                {
                    return;
                }

                await Awaitable.NextFrameAsync(cancellation.Token);
            }
        }

        async Awaitable SyncAsync()
        {
            try
            {
                await WaitForContentAsync();

                await AttemptAsync();
            }
            catch (OperationCanceledException)
            {
                // Shutting down mid-request. Nothing to report and nobody left to report it to.
            }
            catch (Exception error)
            {
                // A startup entry point that throws takes the container's whole build callback
                // with it. Caught here so a surprise from the network layer costs the sync alone.
                Debug.LogError("[Hiro] Startup sync failed: " + error);
                Fail(error.Message);
                Finish();
            }
        }

        /// <summary>
        /// Sends the reconciled roster to the backend, so saved worlds can describe their agents.
        /// </summary>
        /// <remarks>
        /// Only after a successful refresh. A failed one leaves the registry empty on purpose — the
        /// platform decides who exists — and publishing that would tell the backend the player has
        /// no agents. Advisory, like every backend publish: a failure costs a log line, not the game.
        /// </remarks>
        async Awaitable PublishRosterAsync()
        {
            if (rosterPublisher == null || cancellation.IsCancellationRequested)
            {
                return;
            }

            try
            {
                var managers = managerRoster?.Read();
                var published = RosterPublication.Create(registry.Agents, managers?.agents);
                await rosterPublisher.PublishAsync(published, cancellation.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                Debug.LogWarning("[Hiro] Could not publish the reconciled roster: " + error.Message);
            }
        }

        /// <summary>
        /// One go at reading the roster, and the verdict that follows from it.
        /// </summary>
        /// <remarks>
        /// Shared by the startup run and the dialog's Retry so the two cannot drift — a retry that
        /// marked readiness differently from the first attempt would be a gate that opens on the
        /// second try for reasons nobody can see.
        /// <para>
        /// There is no key check before the call any more. It used to test the developer API key,
        /// which is no longer the credential the roster uses, and a missing WorkOS token now comes
        /// back through the same failure path as a refused one — with a reason attached.
        /// </para>
        /// </remarks>
        async Awaitable AttemptAsync()
        {
            if (readiness != null)
            {
                readiness.MarkRunning(AppStartup.RosterPreprocess);
            }

            // The registry does the fetching, the reconciling and the area-preserving. This class
            // decides only that it happens at startup, and reports what came of it.
            await registry.RefreshAsync(cancellation.Token);

            if (registry.Source == AgentSource.Platform)
            {
                Debug.Log("[Hiro] Agents are live from the platform: " + Describe());
                PersistCatalogue();

                if (readiness != null)
                {
                    readiness.MarkSucceeded(AppStartup.RosterPreprocess);
                }

                await PublishRosterAsync();
            }
            else
            {
                Fail(registry.LastError, registry.LastStatus);
            }

            Finish();
        }

        /// <summary>
        /// Shuts the gate and tells the player why.
        /// </summary>
        /// <remarks>
        /// The gate is the important half: <c>RosterPreprocess</c> is required, and LobbyMainMenu
        /// already drives <c>canvasGroup.interactable</c> off <c>AllRequiredSucceeded</c>, so
        /// marking it failed is what greys the Lobby out. The dialog only explains what the grey
        /// already means — which is why a missing DialogManager costs the explanation and not the
        /// gate.
        /// <para>
        /// Deliberately not falling back to the local roster. It is the previous tenant's agent
        /// list, and showing it would be indistinguishable from a working game.
        /// </para>
        /// </remarks>
        void Fail(string reason, long status = 0)
        {
            var explanation = string.IsNullOrEmpty(reason)
                ? "The agent roster could not be read."
                : reason;

            Debug.LogWarning("[Hiro] The agent roster could not be read, so the Lobby stays shut: "
                + explanation);

            var unlinked = status == Unlinked;
            var email = workOS == null ? string.Empty : workOS.PlayerEmail;

            if (readiness != null)
            {
                readiness.MarkFailed(AppStartup.RosterPreprocess, unlinked ? NotLinked(email) : explanation);
            }

            if (unlinked)
            {
                // QUIT or REGISTER NOW, which opens Hiro Labs in the browser. That is the whole flow.
                Ask("Account not linked to Hiro", NotLinked(email), "REGISTER NOW", OpenHiroLabs);
                return;
            }

            Ask("Could not load your agents", explanation, "RETRY", Retry);
        }

        /// <summary>REGISTER NOW: Hiro Labs, in the system browser.</summary>
        void OpenHiroLabs()
        {
            var url = hiro == null ? string.Empty : hiro.RegistrationUrl;

            if (string.IsNullOrEmpty(url))
            {
                Debug.LogWarning("[Hiro] REGISTER NOW has nowhere to go: HiroApiSettings' Registration Url is empty.");
                return;
            }

            Application.OpenURL(url);
        }

        /// <summary>
        /// Tells the player in the Confirmation popup, tinted red: <paramref name="primary"/> on the
        /// right, QUIT on the left.
        /// </summary>
        /// <remarks>
        /// The plain confirm dialog stands in only when the popup prefab is missing, so a missing
        /// asset costs the look and not the question.
        /// </remarks>
        void Ask(string title, string body, string primary, Action onPrimary)
        {
            if (popups != null && popups.Confirmation != null)
            {
                // Asked after Play stopped - the roster answered late: nobody to ask.
                var layer = PopupManager.Instance;

                if (layer == null)
                {
                    return;
                }

                // The Confirmation's buttons, in red: both questions asked here follow an error.
                var popup = layer.Show(popups.Confirmation)
                    .Tint(MessagePopup.Tone.Alert)
                    .Present(title, body)
                    .Caption(MessagePopup.Choice.Cancel, "QUIT")
                    .Caption(MessagePopup.Choice.Primary, primary);

                popup.Chosen += choice =>
                {
                    if (choice == MessagePopup.Choice.Primary)
                    {
                        onPrimary();
                    }
                    else
                    {
                        Quit();
                    }
                };

                return;
            }

            if (dialogs != null)
            {
                dialogs.Confirm(title + ".\n\n" + body, onPrimary, Quit);
            }
        }

        /// <summary>
        /// What the platform answers for a signed-in account that no Hiro user is linked to.
        /// </summary>
        /// <remarks>
        /// Linking is an admin step on Hiro's side; the platform never creates the account itself
        /// (<c>server/README.md</c>). It answers the same for a token it cannot read, by design, but
        /// a token straight from sign-in is readable, so in practice this is the unlinked account.
        /// </remarks>
        const long Unlinked = 401;

        /// <summary>The line that says which account Hiro does not know.</summary>
        public static string NotLinked(string email)
        {
            return (string.IsNullOrEmpty(email) ? "This account" : email) + " isn't linked to a Hiro account.";
        }

        /// <remarks>
        /// Fire-and-forget because <c>Confirm</c> hands back an <c>Action</c>. The await and both
        /// catches live in <see cref="RetryAsync"/> rather than here, so a failed retry cannot
        /// become an unobserved task exception the runtime swallows.
        /// </remarks>
        void Retry()
        {
            _ = RetryAsync();
        }

        async Awaitable RetryAsync()
        {
            try
            {
                await AttemptAsync();
            }
            catch (OperationCanceledException)
            {
                // Shutting down mid-retry.
            }
            catch (Exception error)
            {
                Debug.LogError("[Hiro] Roster retry failed: " + error);
                Fail(error.Message);
            }
        }

        /// <remarks>
        /// <c>Application.Quit</c> does nothing in the editor, so the editor gets the equivalent
        /// that does — otherwise Quit reads as a broken button on the one machine it is tested on.
        /// </remarks>
        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>
        /// Writes the reconciled roster back into the shipped catalogue, in the editor only.
        /// </summary>
        /// <remarks>
        /// The rules are <see cref="AgentRosterSync"/>'s, kept apart from this class so they can be
        /// read without wading through startup plumbing. The one thing decided here is <em>where</em>
        /// the result is written, and that differs by platform in a way nothing can paper over.
        /// <list type="bullet">
        /// <item><b>In the editor</b> the catalogue under <c>Resources</c> is written, so the sync
        /// lands in a real file that goes into source control. That is the useful case: a developer
        /// runs the game once and the roster now carries the platform ids.</item>
        /// <item><b>In a build</b> that file is read-only — it is packed into the player — so the
        /// result goes to the overlay in the persistent data folder instead, which is the same
        /// mechanism painted areas already use.</item>
        /// </list>
        /// </remarks>
        void PersistCatalogue()
        {
#if UNITY_EDITOR
            WriteCatalogue();
#endif
        }

#if UNITY_EDITOR
        /// <summary>
        /// Writes the reconciled roster back over the shipped catalogue.
        /// </summary>
        /// <remarks>
        /// Editor only, and by necessity rather than preference: this is exactly the write a build
        /// cannot do — more so now that the catalogue is downloaded content, which is read-only at
        /// runtime by definition. Going through <c>AssetDatabase</c> rather than <c>File</c> alone
        /// is what makes the editor notice — an asset changed behind its back is served from the
        /// old import until something asks for a refresh.
        /// <para>
        /// The roster's <c>CatalogueResource</c> is a content address, and addresses are the path
        /// under the content root without the extension — so the project path is that root, the
        /// address, and <c>.json</c> back on the end.
        /// </para>
        /// </remarks>
        void WriteCatalogue()
        {
            var path = "Assets/Dojo/Content/" + roster.CatalogueResource + ".json";

            var all = new AgentRoster { version = AgentRoster.CurrentVersion };

            foreach (var agent in registry.Agents)
            {
                if (agent != null)
                {
                    all.Put(agent.Copy());
                }
            }

            try
            {
                System.IO.File.WriteAllText(path, JsonUtility.ToJson(all, true));
                UnityEditor.AssetDatabase.ImportAsset(path);

                Debug.Log("[Hiro] Wrote the reconciled roster to " + path
                    + " — commit it so the platform ids travel with the project.");
            }
            catch (System.IO.IOException error)
            {
                Debug.LogWarning("[Hiro] Could not write " + path + ": " + error.Message
                    + ". The reconciled roster is in memory for this session only.");
            }
        }
#endif

        void Finish()
        {
            // Every credential, under one prefix, once the roster has settled. Filter the console
            // by AgentCredentialLog.Prefix to read the whole thing in one query.
            AgentCredentialLog.Dump(registry.Agents, registry.Source);

        }

        string Describe()
        {
            var names = new List<string>();

            foreach (var agent in registry.Agents)
            {
                if (agent == null)
                {
                    continue;
                }

                names.Add(agent.name + (agent.HasPlatformAgent ? string.Empty : " (local only)"));
            }

            return string.Join("; ", names.ToArray());
        }

        public void Dispose()
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }
    }
}
