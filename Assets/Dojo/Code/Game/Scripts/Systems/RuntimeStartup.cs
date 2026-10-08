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
    /// The offline startup of the Runtime scene: name the packs, read the catalogue, then build the
    /// game scene's scope, which opens the world.
    /// </summary>
    /// <remarks>
    /// No session and no server: the packs are <see cref="ContentSettings.ContentKeys"/>
    /// ("default" unless changed), adopted as they are. Preloading them only indexes their
    /// addresses - each asset loads the first time something asks for it - so the scene comes up
    /// within a few frames and the world builds from just the prefabs it uses.
    /// </remarks>
    public sealed class RuntimeStartup : IStartable, IDisposable
    {
        readonly ContentSettings contentSettings;
        readonly EntitlementService entitlements;
        readonly IContentService content;
        readonly IItemCatalog itemCatalog;
        readonly LifetimeScope gameScope;
        readonly RuntimeLifetimeScope root;
        readonly CancellationTokenSource cancellation = new CancellationTokenSource();

        const string OfflinePlayerId = "plr_offline";

        public RuntimeStartup(
            ContentSettings contentSettings,
            EntitlementService entitlements,
            IContentService content,
            IItemCatalog itemCatalog,
            GameLifetimeScope gameScope,
            RuntimeLifetimeScope root)
        {
            this.contentSettings = contentSettings;
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
                entitlements.AdoptSession(SessionResult.Success(OfflinePlayerId, contentSettings.ContentKeys));

                await content.InitializeAsync(cancellation.Token);
                await content.PreloadAsync(entitlements.Keys, cancellation.Token);

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
