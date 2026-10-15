using Dojo.Game.InGame.Agents;
using Dojo.Game.InGame.Controllers;
using Dojo.Game.Systems;
using UnityEngine;
using UnityEngine.AI;
using VContainer;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// Builds the manager into the world: the model, the credentials stamped on it, and the switch
    /// from a piece being carried to somebody the player drives.
    /// </summary>
    /// <remarks>
    /// The manager's counterpart of <see cref="AgentAssembler"/> and, like it, exists because two
    /// callers need the identical thing: <c>ManagerDisplayUI</c> when the player presses Place
    /// Manager, and <see cref="WorldService"/> when a saved world is loaded. The second is what
    /// made it necessary — the loader is not a component and cannot reach a panel's private
    /// methods, so leaving the steps in the UI meant a loaded manager was never assembled at all.
    /// <para>
    /// Kept apart from the agent assembler rather than folded into it. What "bring to life" means
    /// differs: an agent gets an <c>AgentRoutine</c> switched on and is confined to its painted
    /// cells, while the manager gets a <c>ManagerController</c> and goes where the player clicks.
    /// The two share only the parts that belong to placement, and those are on
    /// <see cref="FurniturePiece"/> already.
    /// </para>
    /// </remarks>
    public sealed class ManagerAssembler
    {
        readonly FurniturePlacementService placement;
        readonly PlacementGrid grid;
        readonly WorldRoot world;

        // The manager stands on a floor: he is built under the floor at the height he is put down at.
        readonly Storeys storeys;

        readonly WorldSettings settings;

        // The scene's container. The manager is built through it rather than through
        // Object.Instantiate, because ManagerController expects an IEventHub and spent its whole
        // life logging that nothing would hear it when it did not get one.
        readonly IObjectResolver resolver;

        // Asked at activation, because the phase is applied to whoever exists when it changes and
        // a manager put down mid-Edit arrives after that.
        readonly IGamePhase phase;

        // Measures his model and gives him a walkable floor to its size as he comes to life.
        readonly ClearanceRegistry clearance;

        // The world's named areas: he walks back to his when left standing.
        readonly BlockAreas blockAreas;

        // The elevators: clicking one sends him to its doors, and reaching one offers its floors.
        readonly ElevatorShafts elevators;

        public ManagerAssembler(
            FurniturePlacementService placement,
            PlacementGrid grid,
            WorldRoot world,
            Storeys storeys,
            WorldSettings settings,
            IObjectResolver resolver,
            IGamePhase phase,
            ClearanceRegistry clearance,
            BlockAreas blockAreas,
            ElevatorShafts elevators)
        {
            this.placement = placement;
            this.grid = grid;
            this.world = world;
            this.storeys = storeys;
            this.settings = settings;
            this.resolver = resolver;
            this.phase = phase;
            this.clearance = clearance;
            this.blockAreas = blockAreas;
            this.elevators = elevators;
        }

        /// <summary>
        /// The manager's model, or null when the scene has none authored.
        /// </summary>
        /// <remarks>
        /// No capsule fallback, unlike the agents. An agent placed as a placeholder still wanders
        /// and still demonstrates the tab; the manager is the player, and quietly giving them a
        /// stand-in body would be a worse answer than saying the prefab is missing.
        /// </remarks>
        public GameObject Prefab => settings != null ? settings.ManagerPrefab : null;

        /// <summary>
        /// The manager brought to life most recently, or null when there is none - for anything
        /// sending him somewhere that is not a click, like "Go to 3F".
        /// </summary>
        public ManagerController Active { get; private set; }

        /// <summary>
        /// Builds the manager at <paramref name="position"/> with their credentials recorded and
        /// their own driving switched off.
        /// </summary>
        /// <remarks>
        /// Stops short of handing them to the player — see <see cref="Activate"/>. A piece coming
        /// out of the drawer is still on the cursor, and a live <see cref="NavMeshAgent"/> steers
        /// its own transform: because the manager's sits on the art <em>inside</em> the placement
        /// container, one left running walks the model away from the container while the container
        /// goes on following the cursor. That looked exactly like the model not following the mouse.
        /// </remarks>
        public FurniturePiece Assemble(AgentCredential credential, Vector3 position, int layer)
        {
            if (credential == null || Prefab == null)
            {
                return null;
            }

            var piece = FurnitureSpawner.Spawn(
                Prefab, grid, position, layer, placement.MaxFootprintCells, storeys.FloorAt(position.y), resolver);

            if (piece == null)
            {
                return null;   // the spawner has already said why
            }

            Stamp(piece, credential);
            Quieten(piece);

            // So his clicks land on the floor being looked at, not on a hidden one above it; and so
            // he knows the elevators, to walk up to one and to be offered its floors there.
            foreach (var controller in piece.GetComponentsInChildren<ManagerController>(true))
            {
                controller.Floors = storeys;
                controller.Elevators = elevators;
            }

            // And so he rides the elevator rather than gliding through its wall: on the same
            // object as his walking, which stops crossing links by itself once this is there.
            var walker = piece.GetComponentInChildren<NavMeshAgent>(true);
            if (walker != null)
            {
                var rider = walker.GetComponent<ElevatorRider>() ?? walker.gameObject.AddComponent<ElevatorRider>();
                rider.Floors = storeys;

                // And so, left standing a while, he walks back to his area - or about his floor.
                var idle = walker.GetComponent<ManagerIdle>() ?? walker.gameObject.AddComponent<ManagerIdle>();
                idle.Areas = blockAreas;
                idle.Floors = storeys;
                idle.Phase = phase;
                idle.CredentialId = credential.id;
            }

            return piece;
        }

        /// <summary>Records which credentials this manager is, so a save can find them again.</summary>
        public void Stamp(FurniturePiece piece, AgentCredential credential)
        {
            if (piece == null || credential == null)
            {
                return;
            }

            var placed = piece.GetComponent<PlacedManager>() ?? piece.gameObject.AddComponent<PlacedManager>();
            placed.Set(credential.id, credential.name, credential.workId);

            piece.name = credential.name;
        }

        /// <summary>Stops the manager driving himself while he is being placed.</summary>
        /// <remarks>
        /// Both looked for in children, because the spawner wraps whatever prefab it is handed and
        /// the manager's own components ride on the art underneath the container.
        /// <para>
        /// The controller goes first, so nothing issues a destination in the frame between the two.
        /// It also has to go down because it acts on pointer-down: left live, the click meant to
        /// put the manager down would also dispatch him to walk somewhere.
        /// </para>
        /// </remarks>
        public static void Quieten(FurniturePiece piece)
        {
            if (piece == null)
            {
                return;
            }

            var controller = piece.GetComponentInChildren<ManagerController>(true);
            if (controller != null)
            {
                controller.enabled = false;
            }

            var nav = piece.GetComponentInChildren<NavMeshAgent>(true);
            if (nav != null)
            {
                nav.enabled = false;
            }
        }

        /// <summary>
        /// Turns a settled piece into the manager the player drives.
        /// </summary>
        /// <remarks>
        /// The same two undoings an agent needs, for the same reasons: the
        /// <see cref="NavMeshObstacle"/> every <c>FurniturePiece</c> carries would carve the
        /// walkable space out from under him, and the grid cells he claimed have to go back because
        /// he walks away from them.
        /// <para>
        /// He is not confined to his area: he goes wherever the player sends him. His area is only
        /// where he walks back to when left standing - see <see cref="ManagerIdle"/>.
        /// </para>
        /// </remarks>
        public void Activate(FurniturePiece piece)
        {
            if (piece == null)
            {
                return;
            }

            if (placement != null)
            {
                placement.Release(piece);
            }

            var obstacle = piece.GetComponent<NavMeshObstacle>();
            if (obstacle != null)
            {
                obstacle.enabled = false;
            }

            var nav = piece.GetComponentInChildren<NavMeshAgent>(true);
            if (nav != null)
            {
                // Onto the floor built for his size before he is switched on, so the first thing
                // he stands on is the floor he will walk.
                if (clearance != null)
                {
                    clearance.Fit(nav);
                }

                nav.enabled = true;
            }

            var controller = piece.GetComponentInChildren<ManagerController>(true);
            if (controller != null)
            {
                controller.enabled = true;
                Active = controller;

                // Into whatever phase is in force now. Switching the controller on hands him the
                // player's clicks, and a manager placed while the room is being arranged is
                // otherwise the one person in it who is not frozen.
                if (phase != null)
                {
                    GamePhaseApplier.ApplyTo(controller, phase.Current);
                }
            }
            else
            {
                Debug.LogWarning("[Manager] '" + piece.name + "' has no ManagerController on it, so "
                    + "clicking the floor will not move him. Add one to the manager prefab.", piece);
            }
        }
    }
}
