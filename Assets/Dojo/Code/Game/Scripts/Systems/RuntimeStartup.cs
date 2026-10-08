using System;
using System.Threading;
using Dojo.Framework.Content;
using Dojo.Framework.Inventory;
using Dojo.Framework.Session;
using UnityEngine;
using VContainer.Unity;

namespace Dojo.Game.Systems
{
    /// <summary>
    /// The offline startup of the Runtime scene: load the packs, read the catalogue, then build the
    /// game scene's scope.
    /// </summary>
    /// <remarks>
    /// What <c>AppStartup</c> does in the game between sign-in and the Lobby, with neither: the
    /// simulated session names the packs (<see cref="ContentSettings.ContentKeys"/>, "default"
    /// unless changed), the entitlements adopt them, Addressables makes them resident, and the
    /// catalogue is read from the manifests that arrived. Only then is the game scope built and
    /// the scene switched on, so the inventory, the placement and the default world all find
    /// their content already there.
    /// </remarks>
    public sealed class RuntimeStartup : IStartable, IDisposable
    {
        readonly ISessionService session;
        readonly EntitlementService entitlements;
        readonly IContentService content;
        readonly IItemCatalog itemCatalog;
        readonly LifetimeScope gameScope;
        readonly RuntimeLifetimeScope root;
        readonly CancellationTokenSource cancellation = new CancellationTokenSource();

        public RuntimeStartup(
            ISessionService session,
            EntitlementService entitlements,
            IContentService content,
            IItemCatalog itemCatalog,
            GameLifetimeScope gameScope,
            RuntimeLifetimeScope root)
        {
            this.session = session;
            this.entitlements = entitlements;
            this.content = content;
            this.itemCatalog = itemCatalog;
            this.gameScope = gameScope;
            this.root = root;
        }

        public void Start()
        {
            _ = RunAsync();
        }

        async Awaitable RunAsync()
        {
            try
            {
                var result = await session.ConnectAsync(cancellation.Token);

                if (!result.Connected)
                {
                    Debug.LogError("[Runtime] The offline session could not start: " + result.Error);
                    return;
                }

                entitlements.AdoptSession(result);

                await content.InitializeAsync(cancellation.Token);
                await content.PreloadAsync(entitlements.Keys, cancellation.Token);
                Debug.Log("[Runtime] Content resident; reading the catalogue.");

                itemCatalog.Refresh();

                Debug.Log("[Runtime] Loaded " + string.Join(", ", entitlements.Keys) + ": "
                    + itemCatalog.All.Count + " item(s) in the catalogue.");
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ContentException exception)
            {
                Debug.LogError("[Runtime] The content could not be loaded, so the game is not started: "
                    + exception.Message + " Build the packs with Tools > Dojo > Addressable Generator.");
                return;
            }
            catch (Exception exception)
            {
                // Logged here because nothing else will: this runs as a discarded Awaitable, so an
                // exception left to escape disappears and the scene simply never comes up.
                Debug.LogException(exception);
                Debug.LogError("[Runtime] Startup failed before the game could start; see the exception above.");
                return;
            }

            if (gameScope == null)
            {
                Debug.LogError("[Runtime] There is no GameLifetimeScope in the scene to start.");
                return;
            }

            try
            {
                if (gameScope.Container == null)
                {
                    gameScope.Build();
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Debug.LogError("[Runtime] The game scope could not be built; the scene is left switched off.");
                return;
            }

            Debug.Log("[Runtime] Game scope built; switching the scene on.");

            // Last, so the scene wakes into a built container: GameManager's Start opens the world
            // and checks its injection, and both need the scope above to exist first.
            root.ReleaseScene();
        }

        public void Dispose()
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }
    }
}
