using System.Collections.Generic;
using Dojo.Content;
using Dojo.Framework;
using Dojo.Framework.Auth;
using Dojo.Framework.Content;
using Dojo.Framework.Inventory;
using Dojo.Framework.Net;
using Dojo.Framework.Session;
using Dojo.Framework.UI;
using Dojo.Framework.World;
using Dojo.Game.InGame.Agents;
using Dojo.Game.Managers;
using Dojo.Game.Server;
using Dojo.Hiro;
using Dojo.Inventory;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Dojo.Game.Systems
{
    /// <summary>
    /// The root scope of the Runtime scene: the game's own root, offline.
    /// </summary>
    /// <remarks>
    /// dojo-game-app's <c>RootLifetimeScope</c> with the server taken out. There is no sign-in, no
    /// SpacetimeDB and no Hiro API here, so every service that would talk to one is registered as
    /// its offline stand-in, and the content comes from this project's own Addressables groups —
    /// the packs built from <c>Assets/Process</c>.
    /// <para>
    /// Everything the game scene asks the root for is still registered, so the scene's
    /// <see cref="GameLifetimeScope"/> resolves exactly as it does in the game. That scope is built
    /// by <see cref="RuntimeStartup"/> once the content is resident, not on its own Awake: the
    /// inventory reads the catalogue as it is built, and an empty catalogue is an empty inventory.
    /// </para>
    /// <para>
    /// The rest of the scene is held inactive until then. In the game the scene loads after the
    /// content does, so its components inject before their Start and GameManager opens the world
    /// from content already there; holding them here gives them the same order in one scene.
    /// </para>
    /// </remarks>
    [DefaultExecutionOrder(-10000)]
    public sealed class RuntimeLifetimeScope : LifetimeScope
    {
        [Tooltip("Optional authored dialog prefabs. A dialog with nothing here builds a plain " +
                 "version of itself in code.")]
        [SerializeField] DialogPrefabs dialogPrefabs = new DialogPrefabs();

        [Tooltip("The popups in Prefabs/UI/Popups, for code that shows one.")]
        [SerializeField] PopupPrefabs popupPrefabs = new PopupPrefabs();

        [Header("Content")]
        [Tooltip("Which packs to load. Leave Remote Root empty to load from this project's own " +
                 "Addressables groups.")]
        [SerializeField] ContentSettings contentSettings = new ContentSettings();

        [Header("Loading")]
        [Tooltip("Optional. Without it the loading screen builds itself with no parts.")]
        [SerializeField] LoadingScreen loadingScreen;

        [Header("Game")]
        [Tooltip("The game scene's scope, built once the content has loaded. Its Auto Run should " +
                 "be off. Found in the scene when left empty.")]
        [SerializeField] GameLifetimeScope gameScope;

        /// <summary>The scene's root objects switched off by <see cref="HoldScene"/>.</summary>
        readonly List<GameObject> held = new List<GameObject>();

        protected override void Awake()
        {
            if (gameScope == null)
            {
                gameScope = FindAnyObjectByType<GameLifetimeScope>(FindObjectsInactive.Include);
            }

            HoldScene();
            base.Awake();
        }

        /// <summary>
        /// Switches off every other root of the scene before any of them wakes, leaving only this
        /// scope and the game scope's own object.
        /// </summary>
        void HoldScene()
        {
            foreach (var root in gameObject.scene.GetRootGameObjects())
            {
                if (root == gameObject || !root.activeSelf
                    || (gameScope != null && root == gameScope.gameObject))
                {
                    continue;
                }

                root.SetActive(false);
                held.Add(root);
            }
        }

        /// <summary>
        /// Switches the held roots back on. Called once the game scope is built, so they wake
        /// already injected.
        /// </summary>
        public void ReleaseScene()
        {
            foreach (var root in held)
            {
                if (root != null)
                {
                    root.SetActive(true);
                }
            }

            held.Clear();
        }

        protected override void Configure(IContainerBuilder builder)
        {

            new FrameworkInstaller().Install(builder);

            RegisterManagers(builder);
            RegisterLoadingScreen(builder);
            RegisterDialogs(builder);
            RegisterContent(builder);
            RegisterOffline(builder);

            // Registered although nothing here signs in: the Lobby's types take it, and a scene that
            // still carries one should resolve rather than throw.
            builder.Register<SignInGate>(Lifetime.Singleton);

            builder.RegisterEntryPoint<RuntimeStartup>()
                .WithParameter(gameScope)
                .WithParameter(this);
        }

        void RegisterContent(IContainerBuilder builder)
        {
            builder.RegisterInstance(contentSettings ?? new ContentSettings());
            builder.Register<AddressablesContentService>(Lifetime.Singleton).As<IContentService>();
            builder.Register<ItemCatalog>(Lifetime.Singleton).As<IItemCatalog>();
            builder.Register<WorldOpenIntent>(Lifetime.Singleton);
            builder.Register<EntitlementService>(Lifetime.Singleton)
                .AsSelf()
                .As<IEntitlements>()
                .As<IEntitlementGrants>();
            builder.Register<SimulatedSessionService>(Lifetime.Singleton).As<ISessionService>();
        }

        /// <summary>
        /// Every server-facing service, as the stand-in that never sends a request.
        /// </summary>
        static void RegisterOffline(IContainerBuilder builder)
        {
            // Backend: the same stand-ins the game registers when SpacetimeDB is switched off.
            builder.RegisterInstance(new SpacetimeSettings());
            builder.Register<NullCatalogPublisher>(Lifetime.Singleton).As<ICatalogPublisher>();
            builder.Register<NullPackGranter>(Lifetime.Singleton).As<IPackGranter>();
            builder.Register<NullOwnedPackSource>(Lifetime.Singleton).As<IOwnedPackSource>();
            builder.Register<NullWorldPublisher>(Lifetime.Singleton).As<IWorldPublisher>();
            builder.Register<NullWorldSource>(Lifetime.Singleton).As<IWorldSource>();
            builder.Register<NullWorldSelection>(Lifetime.Singleton).As<IWorldSelection>();
            builder.Register<NullRosterPublisher>(Lifetime.Singleton).As<IRosterPublisher>();
            builder.Register<NullWorkOSAuthService>(Lifetime.Singleton).As<IWorkOSAuthService>();

            // Hiro: the settings are still registered for the types that read them, but the
            // directory and both chats are the offline ones, so nothing is ever sent.
            builder.RegisterInstance(new HiroApiSettings()).AsSelf();
            builder.Register<HiroWorkOSApiSettings>(Lifetime.Singleton).As<IApiSettings>();
            builder.Register<OfflineAgentDirectory>(Lifetime.Singleton).As<IAgentDirectory>();
            builder.Register<OfflineAgentChat>(Lifetime.Singleton).As<IAgentChat>();
            builder.Register<OfflineBuilderChat>(Lifetime.Singleton).As<IAgentBuilderChat>();

            // The roster comes from the content pack's credentials file alone.
            builder.Register<AgentRosterFile>(Lifetime.Singleton);
            builder.Register<ManagerRosterFile>(Lifetime.Singleton);
            builder.Register<AgentRegistry>(Lifetime.Singleton).As<IAgentRegistry>();

            builder.Register<HiroChatBackend>(Lifetime.Singleton).As<Dojo.Game.UI.IChatBackend>();
            builder.Register<HiroCompanionBackend>(Lifetime.Singleton)
                .As<Dojo.Game.UI.ICompanionChatBackend>();
        }

        void RegisterLoadingScreen(IContainerBuilder builder)
        {
            if (loadingScreen != null)
            {
                builder.RegisterComponentInNewPrefab(loadingScreen, Lifetime.Singleton)
                    .DontDestroyOnLoad()
                    .As<ILoadingScreen>();
                return;
            }

            builder.RegisterComponentOnNewGameObject<LoadingScreen>(Lifetime.Singleton, nameof(LoadingScreen))
                .DontDestroyOnLoad()
                .As<ILoadingScreen>();
        }

        static void RegisterManagers(IContainerBuilder builder)
        {
            builder.RegisterComponentOnNewGameObject<DialogManager>(Lifetime.Singleton, nameof(DialogManager))
                .DontDestroyOnLoad()
                .As<IDialogManager>();

            builder.RegisterComponentOnNewGameObject<PanelManager>(Lifetime.Singleton, nameof(PanelManager))
                .DontDestroyOnLoad()
                .AsSelf();
        }

        void RegisterDialogs(IContainerBuilder builder)
        {
            var prefabs = dialogPrefabs ?? new DialogPrefabs();

            RegisterDialog<ConfirmDialog, IConfirmDialog>(builder, prefabs.Confirm);
            RegisterDialog<PromptDialog, IPromptDialog>(builder, prefabs.Prompt);
            RegisterDialog<ChooserDialog, IChooserDialog>(builder, prefabs.Chooser);
            RegisterDialog<SaveAsDialog, ISaveAsDialog>(builder, prefabs.SaveAs);
            RegisterDialog<LoadWorldDialog, ILoadWorldDialog>(builder, prefabs.LoadWorld);
            RegisterDialog<SaveWorldDialog, ISaveWorldDialog>(builder, prefabs.SaveWorld);
            RegisterDialog<ShopDialog, IShopDialog>(builder, prefabs.Shop);

            builder.RegisterInstance(popupPrefabs ?? new PopupPrefabs());
        }

        static void RegisterDialog<TDialog, TInterface>(IContainerBuilder builder, TDialog prefab)
            where TDialog : MonoBehaviour, TInterface
            where TInterface : class
        {
            if (prefab != null)
            {
                builder.RegisterComponentInNewPrefab(prefab, Lifetime.Transient)
                    .As<TInterface>();
            }
            else
            {
                builder.RegisterComponentOnNewGameObject<TDialog>(Lifetime.Transient, typeof(TDialog).Name)
                    .As<TInterface>();
            }

            builder.RegisterFactory<TInterface>(
                resolver => () => resolver.Resolve<TInterface>(), Lifetime.Singleton);
        }
    }
}
