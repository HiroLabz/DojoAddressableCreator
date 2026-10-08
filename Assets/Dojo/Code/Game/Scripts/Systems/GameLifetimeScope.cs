using Dojo.Game.InGame.Agents;
using Dojo.Game.InGame.Controllers;
using Dojo.Game.InGame.Effects;
using Dojo.Game.Managers;
using Dojo.Game.Placement;
using Dojo.Game.UI;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Dojo.Game.Systems
{
    /// <summary>
    /// Scene scope for the game scenes. Without it nothing in the scene would be injected, so the
    /// UI and the GameManager would never receive the shared IEventHub and would not hear each
    /// other.
    /// </summary>
    /// <remarks>
    /// Shared by the scenes that hold a world and those that do not: the
    /// older scene has the agents and the managers but no placement system at all. So the scene is
    /// looked at once, here, and only what is actually present is declared — see
    /// <see cref="RegisterSceneServices"/> for why that search belongs in this class and nowhere
    /// else.
    /// </remarks>
    public sealed class GameLifetimeScope : LifetimeScope
    {
        [Header("World")]
        [Tooltip("How saved worlds are written and what layer loaded pieces join. Here rather than " +
                 "on a component, because the service that reads them is not one.")]
        [SerializeField] WorldSettings worldSettings = new WorldSettings();

        protected override void Configure(IContainerBuilder builder)
        {
            // Declared before the scene's own components, because the placement controller resolves
            // IGamePhase and a registration that arrived after it would not be there to find.
            //
            // Three faces of one instance, the same split the entitlement gate uses: IGamePhase for
            // the many things that only ask, AsSelf for the inventory drawer — the one panel
            // allowed to decide — and the applier, which is what turns a decision into halted
            // agents and locked furniture.
            builder.Register<GamePhaseService>(Lifetime.Singleton)
                .AsSelf()
                .As<IGamePhase>();

            builder.RegisterEntryPoint<GamePhaseApplier>();

            // The world's three plain objects are only useful if there is a world root to build
            // into, so they follow whether that root was found.
            var hasWorld = RegisterSceneServices(builder);

            if (hasWorld)
            {
                RegisterWorld(builder);
            }

            // Passed on because GameManager opens the starting world, and it can only be given the
            // service in a scene where that service was registered at all.
            InjectSpawnedComponents(builder, hasWorld);
        }

        /// <summary>
        /// Declares the scene's own single-instance components, and says whether the placement
        /// system is one of the things this scene has.
        /// </summary>
        /// <remarks>
        /// This is the composition root, and it is the one class in the project with any business
        /// searching the scene. It happens once, when the scope builds, and the container holds the
        /// results for the life of the scene. That is the whole difference from what was here
        /// before: every dependent used to run its own <c>FindAnyObjectByType</c> in its own
        /// <c>Start</c> — the same search over and over, each with its own chance to come back null
        /// and leave a component half-wired.
        /// <para>
        /// The search is written out rather than left to <c>RegisterComponentInHierarchy</c>, which
        /// does the identical thing but throws when a component is absent. Absent is a legitimate
        /// answer here: <c>Game</c> has no placement system, and a scope that threw on load would
        /// take that scene down with it.
        /// </para>
        /// <para>
        /// One of each is not an assumption, it is the design — one grid, one occupancy map, one
        /// world root. A second would be a bug.
        /// </para>
        /// </remarks>
        bool RegisterSceneServices(IContainerBuilder builder)
        {
            var world = Declare<WorldRoot>(builder);

            Declare<PlacementGrid>(builder);
            Declare<FurniturePlacementService>(builder);
            var placement = Declare<PlacementController>(builder);

            var owner = Declare<ManagerController>(builder);

            // Handed over rather than injected, because there is not always an owner. As a
            // constructor dependency it took the whole container down with it when the owner was
            // removed from the scene: nothing was registered, PlacementController's injector threw,
            // and the aborted build left GameManager, the file tray and the inventory all
            // uninjected and each complaining about it in turn.
            if (placement != null)
            {
                placement.Owner = owner;
                placement.BakesNavigation = worldSettings == null || worldSettings.Characters;
            }

            DeclareAreaPainter(builder, placement);

            // Declared for its injection alone: it needs the phase to stay quiet in Edit, and
            // nothing depends on it.
            Declare<ClickFeedback>(builder);

            return world != null;
        }

        /// <summary>
        /// Declares the agent area brush, adding it to the placement object when the scene has not
        /// authored one.
        /// </summary>
        /// <remarks>
        /// The one component here that is created rather than only found. It is new, it needs no
        /// settings, and it only works alongside the grid, the floor lookup and the highlight —
        /// which are all components of the object the <see cref="PlacementController"/> is on. So
        /// where it has to live is not a choice a scene gets to make wrongly, and a scene authored
        /// before the Agents tab existed gets a working brush rather than a warning.
        /// <para>
        /// Registered so <c>AgentDisplayUI</c> can be handed it. Without the registration the
        /// panel would be back to searching the scene for it, which is exactly what this scope
        /// exists to stop.
        /// </para>
        /// </remarks>
        static void DeclareAreaPainter(IContainerBuilder builder, PlacementController placement)
        {
            if (placement == null)
            {
                // No placement system in this scene — Game is like this — so there is no grid to
                // paint on and nothing to declare. The agents tab is not offered there.
                return;
            }

            var painter = placement.GetComponent<AreaPainter>();

            if (painter == null)
            {
                painter = placement.gameObject.AddComponent<AreaPainter>();
            }

            builder.RegisterComponent(painter);
        }

        /// <summary>
        /// Registers the one component of this type in this scene, if there is one.
        /// </summary>
        /// <remarks>
        /// <c>RegisterComponent</c> rather than a plain <c>RegisterInstance</c>: it queues the
        /// component for injection when the container finishes building, which is what lets a
        /// registered component both be a dependency and have dependencies of its own.
        /// </remarks>
        T Declare<T>(IContainerBuilder builder) where T : Component
        {
            var found = InThisScene<T>();

            if (found != null)
            {
                builder.RegisterComponent(found);
            }

            return found;
        }

        /// <summary>
        /// The first component of this type in this scope's own scene, inactive ones included.
        /// </summary>
        /// <remarks>
        /// Scoped to this scene rather than <c>FindAnyObjectByType</c>, which reaches across every
        /// loaded scene. The Bootstrapper and the Lobby are still loaded while a game scene comes
        /// up, and a scope has no business registering something out of somebody else's scene.
        /// </remarks>
        T InThisScene<T>() where T : Component
        {
            foreach (var root in gameObject.scene.GetRootGameObjects())
            {
                var found = root.GetComponentInChildren<T>(true);

                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// World saving, as three plain objects rather than a component.
        /// </summary>
        /// <remarks>
        /// None of these needs a GameObject: they orchestrate a file, a spawner and a set of
        /// navigation surfaces, and not one of them reads a transform or runs per frame. They were a
        /// MonoBehaviour only because a MonoBehaviour is where <c>[SerializeField]</c>s live and
        /// where a <c>Start</c> could go looking for collaborators — and both of those needs are
        /// answered here instead, by <see cref="worldSettings"/> and by the registrations above.
        /// <para>
        /// Registered, not resolved. Nothing saves or loads a world until the player presses a
        /// button, and until then there is no reason for any of it to exist.
        /// </para>
        /// </remarks>
        void RegisterWorld(IContainerBuilder builder)
        {
            builder.RegisterInstance(worldSettings ?? new WorldSettings());

            // The world's floors and which one is being looked at. Beside the world services
            // because every one of them builds into, saves from or places onto a floor.
            builder.Register<Storeys>(Lifetime.Singleton);

            // The elevators: one shaft with a stop on every floor, and the links the manager rides.
            builder.Register<ElevatorShafts>(Lifetime.Singleton);

            // The world's named areas and who uses which, and what keeps the agents in theirs.
            builder.Register<BlockAreas>(Lifetime.Singleton);
            builder.Register<AgentAreaBinding>(Lifetime.Singleton);

            builder.Register<WorldLibraryFile>(Lifetime.Singleton);
            builder.Register<NavigationBaker>(Lifetime.Singleton);
            builder.Register<WorldService>(Lifetime.Singleton);

            // Writes the selected world back whenever anything in it changes. Registered here
            // rather than on the root, because it is only meaningful where there is a world root
            // to save — the same condition this whole method already sits behind.
            builder.RegisterEntryPoint<WorldAutoSave>();

            // The picture the Lobby's continue card shows, taken when the inventory closes. As
            // itself too, because BACK takes one on the way out.
            builder.RegisterEntryPoint<WorldPictures>().AsSelf();

            // The agents' credentials file is NOT registered here. It moved to the root scope,
            // alongside the registry that owns it, because the roster has to outlive a scene: a
            // scene scope is disposed with its scene, and re-reading the file on every level change
            // would throw away a list the startup sync had already reconciled. Registering it here
            // as well would shadow the root's with a second instance and a second view of the same
            // file.

            // The manager's own file, kept apart from the agents'. Two registrations of one shape
            // rather than one shared instance: they are separate files, and a distinct type is
            // what lets each panel be handed its own without asking for it by name.
            builder.Register<ManagerRosterFile>(Lifetime.Singleton);

            // The one piece of agent building both the Place Agent button and a world load need.
            // Registered beside the world services rather than left on the panel, because a load
            // has to assemble agents too and the loader has no inspector to have been wired in.
            builder.Register<AgentAssembler>(Lifetime.Singleton);

            // Gives every character a walkable floor built for the size of its model. Beside the
            // assemblers because they are what brings a character to life, and that is when it is
            // first measured.
            builder.Register<ClearanceRegistry>(Lifetime.Singleton);

            // And the manager's, which is a separate class because bringing him to life is a
            // different job: a ManagerController and the player's clicks rather than an
            // AgentRoutine and a wander. Registered so a world load can assemble him too.
            builder.Register<ManagerAssembler>(Lifetime.Singleton);
        }

        /// <summary>
        /// Injects the components that could not be registered, because there may be more than one
        /// of them.
        /// </summary>
        /// <remarks>
        /// A registration resolves a single component, so a second call-agents group or a second
        /// file tray would silently go without. These are enumerated instead and injected in place,
        /// which asks nothing about how many there are.
        /// <para>
        /// The DialogManager the file tray receives is declared on the project root, so it resolves
        /// through this scope's parent even though the object itself was brought up back in the
        /// Lobby.
        /// </para>
        /// </remarks>
        /// <param name="hasWorld">
        /// Whether this scene had a world root, and so whether <c>WorldService</c> was registered.
        /// </param>
        static void InjectSpawnedComponents(IContainerBuilder builder, bool hasWorld)
        {
            builder.RegisterBuildCallback(container =>
            {
                foreach (var group in FindObjectsByType<CallAgentsGroup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    container.Inject(group);
                }

                foreach (var manager in FindObjectsByType<GameManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    container.Inject(manager);

                    // Assigned rather than injected, the same trade the placement owner makes just
                    // above. WorldService exists only in a scene with a world root, and asking for
                    // one that was never registered throws — which would abort this callback and
                    // leave the inventory and the file tray below uninjected too.
                    if (hasWorld)
                    {
                        manager.World = container.Resolve<WorldService>();
                    }
                }

                // The Agents and You tabs, put away while characters are off: there is nobody for
                // either to place.
                if (hasWorld && !container.Resolve<WorldSettings>().Characters)
                {
                    foreach (var screen in FindObjectsByType<InventoryScreen>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    {
                        screen.HideCharacterTabs();
                    }
                }

                foreach (var inventory in FindObjectsByType<InventoryUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    container.Inject(inventory);
                }

                // The drawer that replaced the one above. Inactive ones included, and it matters:
                // the screen shuts itself in Awake so the game does not open onto it, and an
                // enumeration that skipped inactive objects would leave it with no catalogue to
                // read the moment the player first opened it.
                foreach (var hud in FindObjectsByType<InventoryHud>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    container.Inject(hud);
                }

                // The screen those two sit on, which owns the phase: opening it is the edit
                // session. Left off this list it opens with no phase service, and the game stays
                // in Play with the inventory up — furniture that will not lift, and a camera that
                // goes on easing back onto the manager under a player arranging a room. Inactive
                // included, because the screen shuts itself in Awake.
                foreach (var screen in FindObjectsByType<InventoryScreen>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    container.Inject(screen);
                }

                // The Agents tab's panel. Switched off whenever another tab is up, so inactive ones
                // have to be included or the tab comes back with no roster to list.
                foreach (var agents in FindObjectsByType<InventoryAgentsHud>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    container.Inject(agents);
                }

                // The in-game canvas, which opens the save and load dialogs from its menu entries.
                // Skipped without a world root: WorldService is registered only in a scene that has
                // one, and resolving a registration that was never made throws — which would abort
                // this callback and leave everything after it uninjected.
                if (hasWorld)
                {
                    foreach (var canvas in FindObjectsByType<InGameCanvasUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    {
                        container.Inject(canvas);
                    }

                    // The floor stepper, which needs the world's floors and so only exists to be
                    // injected where there is a world root.
                    foreach (var floors in FindObjectsByType<FloorSwitcher>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    {
                        container.Inject(floors);
                    }

                    // The elevator's floor picker, for the same reason: it lists the world's floors
                    // and rides its elevators.
                    foreach (var lift in FindObjectsByType<ElevatorPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    {
                        container.Inject(lift);
                    }

                    // The areas' locators, which mark the world's areas on its floors.
                    foreach (var locators in FindObjectsByType<AreaLocators>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    {
                        container.Inject(locators);
                    }
                }

                foreach (var files in FindObjectsByType<FileSaveAndLoadUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    container.Inject(files);
                }

                // The controls guide at the top of the screen reads the phase to pick its line.
                foreach (var hint in FindObjectsByType<ControlsHint>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    container.Inject(hint);
                }

                // The first-time tour: it waits for the loading screen, reads the phase and the open
                // dialogs, and listens for pieces put down. Only with a world root: without a world
                // there is nothing to show the player round.
                if (hasWorld)
                {
                    foreach (var tour in FindObjectsByType<FtueTour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    {
                        container.Inject(tour);
                    }
                }

                // The pack-acquisition test harness. Listed like the rest rather than registered,
                // because it is scaffolding a scene is free not to have — and a registration that
                // resolved nothing would throw the whole callback, taking the panels above with it.
                foreach (var buy in FindObjectsByType<BuyStrawberryPopup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    container.Inject(buy);
                }

                // Inactive ones included, and it matters here more than anywhere else on this list:
                // the agents card is switched off in Awake so the drawer does not open onto it, and
                // an enumeration that skipped inactive objects would leave the panel uninjected
                // with no roster to show the moment it was raised.
                foreach (var agents in FindObjectsByType<AgentDisplayUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    container.Inject(agents);
                }

                // Inactive included for the same reason as the agents card: the manager's card is
                // down whenever its tab is not up, and an enumeration that skipped inactive
                // objects would leave the panel uninjected with nothing to show when it was raised.
                foreach (var you in FindObjectsByType<ManagerDisplayUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    container.Inject(you);
                }

                // The areas panel, down whenever its tab is not up, like the two cards above.
                foreach (var areas in FindObjectsByType<AreasDisplayUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    container.Inject(areas);
                }

                // The chat window, which is authored inactive and so must be found with inactive
                // objects included. Without this it never receives a backend and answers with the
                // echo stand-in, which looks exactly like the platform not being connected.
                foreach (var chat in FindObjectsByType<AgentChatUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    container.Inject(chat);
                }

                // The window that replaced the one above, and inactive for the same reason: it
                // switches itself off at the end of wiring so the game does not open onto a chat.
                // Left off this list it has no backend at all rather than a stand-in one, and says
                // there is nothing connected to answer — which reads as the platform being down.
                foreach (var window in FindObjectsByType<ChatAgentWindow>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    container.Inject(window);
                }
            });
        }
    }
}
