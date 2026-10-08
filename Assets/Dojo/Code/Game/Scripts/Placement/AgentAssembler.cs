using System.Collections.Generic;
using Dojo.Game.InGame.Agents;
using Dojo.Game.Systems;
using UnityEngine;
using UnityEngine.AI;
using VContainer;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// Builds an agent into the world: the model, the credentials stamped on it, and the switch
    /// from a piece being carried to somebody walking around.
    /// </summary>
    /// <remarks>
    /// Exists because two callers need the identical thing and used to have their own copy of half
    /// of it. <c>AgentDisplayUI</c> assembles one when the player presses Place Agent, and
    /// <see cref="WorldService"/> assembles every one in a saved world when it is loaded. The
    /// second is what made a shared class necessary: a loaded agent that skipped any of these
    /// steps would stand in the right spot and never move.
    /// <para>
    /// An ordinary object rather than a component. It reads no transform of its own and runs
    /// nothing per frame — it is handed the collaborators it needs and does one job on demand,
    /// which is the same reason <see cref="WorldService"/> is not a component either.
    /// </para>
    /// </remarks>
    public sealed class AgentAssembler
    {
        readonly FurniturePlacementService placement;
        readonly PlacementGrid grid;
        readonly WorldRoot world;

        // An agent lives on a floor: it is built under the floor at the height it is put down at.
        readonly Storeys storeys;

        readonly WorldSettings settings;

        // The scene's container. Agents are built through it rather than through
        // Object.Instantiate, so a component on the model that expects injection gets it.
        readonly IObjectResolver resolver;

        // Asked at activation, because the phase is applied to whoever exists when it changes and
        // an agent put down mid-Edit arrives after that.
        readonly IGamePhase phase;

        // Measures the model and gives the agent a walkable floor to its size as it comes to life.
        readonly ClearanceRegistry clearance;

        // The world's named areas: an agent walks in the one it has been given.
        readonly BlockAreas blockAreas;

        /// <summary>
        /// The cells an agent with no area of its own is given: its whole floor, at this size.
        /// Coarser than a painted cell, since it only has to say "on this floor's tiles".
        /// </summary>
        const float WholeFloorCell = 0.5f;

        public AgentAssembler(
            FurniturePlacementService placement,
            PlacementGrid grid,
            WorldRoot world,
            Storeys storeys,
            WorldSettings settings,
            IObjectResolver resolver,
            IGamePhase phase,
            ClearanceRegistry clearance,
            BlockAreas blockAreas)
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
        }

        /// <summary>
        /// The model every agent wears, from the scene's world settings, or a capsule when none is
        /// authored.
        /// </summary>
        /// <remarks>
        /// One authored field rather than one per caller. It used to live on the agents panel,
        /// which meant a world loaded from disk had no way to reach it — the loader is not a
        /// component and has no inspector to have been wired in.
        /// </remarks>
        public GameObject Prefab
            => settings != null && settings.AgentPrefab != null ? settings.AgentPrefab : Placeholder();

        /// <summary>
        /// Builds an agent on the cursor's behalf: the model at <paramref name="position"/>, with
        /// the credentials recorded on it and its own driving switched off.
        /// </summary>
        /// <remarks>
        /// Deliberately stops short of bringing them to life. A piece coming out of the drawer is
        /// still on the cursor and has not been committed, so a live NavMeshAgent would spend the
        /// drag steering against the pointer. <see cref="Activate"/> is the other half.
        /// </remarks>
        public FurniturePiece Assemble(AgentCredential credential, Vector3 position, int layer)
        {
            if (credential == null)
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

            return piece;
        }

        /// <summary>
        /// Records which credentials an agent is, so an area painted later can find them.
        /// </summary>
        /// <remarks>
        /// Stamped on the piece rather than left on the prefab, so a second agent built from the
        /// same model does not overwrite the first one's identity.
        /// </remarks>
        public void Stamp(FurniturePiece piece, AgentCredential credential)
        {
            if (piece == null || credential == null)
            {
                return;
            }

            var placed = piece.GetComponent<PlacedAgent>() ?? piece.gameObject.AddComponent<PlacedAgent>();
            placed.Set(credential.id, credential.name, credential.workId);

            piece.name = credential.name;
        }

        /// <summary>Stops an agent driving itself while it is being placed.</summary>
        /// <remarks>
        /// Both looked for in children, because the spawner only leaves components on the root for
        /// a prefab carrying its own <see cref="FurniturePiece"/>; anything else is wrapped and the
        /// agent's components ride on the art underneath.
        /// <para>
        /// The routine goes first, so nothing issues a destination in the frame between the two.
        /// </para>
        /// </remarks>
        public static void Quieten(FurniturePiece piece)
        {
            if (piece == null)
            {
                return;
            }

            var routine = piece.GetComponentInChildren<AgentRoutine>(true);
            if (routine != null)
            {
                routine.enabled = false;
            }

            var nav = piece.GetComponentInChildren<NavMeshAgent>(true);
            if (nav != null)
            {
                nav.enabled = false;
            }
        }

        /// <summary>
        /// Turns a settled piece into a walking agent, and confines them to their area.
        /// </summary>
        /// <remarks>
        /// Three things have to be undone, because an agent came down the furniture path to get the
        /// same feel as everything else and furniture is not what it is.
        /// <list type="bullet">
        /// <item>The <see cref="NavMeshObstacle"/> every <c>FurniturePiece</c> carries is switched
        /// off. It exists so a desk carves a hole in the walkable space, and an agent carrying one
        /// carves that hole directly under itself — it would drop off the NavMesh the instant it
        /// was put down and never take a step.</item>
        /// <item>The grid cells it claimed are released. A desk keeps its cells because it stays
        /// where it was put; an agent walks away from them, and cells reserved by somebody who has
        /// left is floor nothing can ever be built on again.</item>
        /// <item>The <see cref="NavMeshAgent"/> and the <see cref="AgentRoutine"/> are enabled,
        /// the routine before the area is handed over so the wander that
        /// <c>ConfineTo</c> starts is issued by a component already running.</item>
        /// </list>
        /// </remarks>
        public void Activate(FurniturePiece piece, AgentCredential credential)
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
                // Onto the floor built for its size before it is switched on, so the first thing
                // it stands on is the floor it will walk.
                if (clearance != null)
                {
                    clearance.Fit(nav);
                }

                nav.enabled = true;
            }

            // Any way between floors the manager can use, an agent can: the stairs, and the
            // elevator with a rider of its own to make each trip a ride.
            if (nav != null)
            {
                var rider = nav.GetComponent<InGame.Controllers.ElevatorRider>()
                    ?? nav.gameObject.AddComponent<InGame.Controllers.ElevatorRider>();
                rider.Floors = storeys;
            }

            var routine = piece.GetComponentInChildren<AgentRoutine>(true);
            if (routine != null)
            {
                routine.FloorOf = storeys.StoreyAt;
                routine.AllowAreas((1 << NavAreas.Stairs) | (1 << NavAreas.Elevator));
                routine.enabled = true;
            }

            // The cells themselves, not the rectangle around them. A rectangle takes in floor the
            // player never painted, and in a world with every floor baked Walkable that rectangle
            // was the only thing holding an agent in — so agents strolled wherever it reached.
            if (routine != null && credential != null)
            {
                Confine(routine, credential, Storeys.StoreyOf(piece.transform));
            }

            // Last, after ConfineTo has started the wander, so an agent put down while the room is
            // being arranged stands still like everybody else instead of strolling off.
            if (routine != null && phase != null)
            {
                GamePhaseApplier.ApplyTo(routine, phase.Current);
            }
        }

        /// <summary>
        /// Confines every placed copy of one set of credentials to their area.
        /// </summary>
        /// <remarks>
        /// Every copy, not one: the area belongs to the credentials rather than to a particular
        /// model, and a player may well have put two of the same agent down. Each is given the
        /// painted cells themselves — see <c>AgentRoutine.ConfineTo</c> for why a rectangle around
        /// them is not good enough.
        /// <para>
        /// The routine is found from the agent and <see cref="PlacedAgent"/> searched for
        /// <em>upwards</em>, because the two are not on the same object — the spawner parents the
        /// art under a container carrying the placement components, so the routine rides on the art
        /// while the stamp is on the container above it.
        /// </para>
        /// </remarks>
        public int Reconfine(AgentCredential credential)
        {
            if (credential == null)
            {
                return 0;
            }

            var confined = 0;

            foreach (var routine in Object.FindObjectsByType<AgentRoutine>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var placed = routine.GetComponentInParent<PlacedAgent>();

                if (placed != null && placed.CredentialId == credential.id)
                {
                    Confine(routine, credential, Storeys.StoreyOf(routine.transform));
                    confined++;
                }
            }

            return confined;
        }

        /// <summary>
        /// Keeps one agent to the block area it has been given, or with none, to the floor it is on.
        /// </summary>
        /// <remarks>
        /// The area may be on another floor: the agent walks there, by the stairs or the elevator,
        /// because its way back in is searched for over every floor it is allowed to use. Without
        /// an area it is its whole floor, and only its floor - a floor with no tiles yet leaves it
        /// where it is.
        /// </remarks>
        void Confine(AgentRoutine routine, AgentCredential credential, int floor)
        {
            var area = blockAreas != null
                ? blockAreas.AreaFor(Dojo.Framework.World.SnapshotRole.Agent, credential.id)
                : null;

            if (area != null && area.Cells.Count > 0)
            {
                routine.ConfineTo(area.Cells, area.CellSize);
                return;
            }

            var cells = FloorCells(floor);

            if (cells.Count > 0)
            {
                routine.ConfineTo(cells, WholeFloorCell);
            }
        }

        /// <summary>
        /// A cell over every floor tile on one floor, for an agent whose area is the whole floor.
        /// </summary>
        /// <remarks>
        /// Lined up with the cells the routine keys by, so every point on a tile falls in a cell
        /// that was made. Stairs are left out: they join floors, and an agent wandering its own
        /// floor has no business choosing to stand on them.
        /// </remarks>
        List<Vector3> FloorCells(int floor)
        {
            var cells = new List<Vector3>();
            var seen = new HashSet<Vector2Int>();

            foreach (var piece in storeys.Floor(floor).GetComponentsInChildren<FurniturePiece>(true))
            {
                if (piece == null || !piece.IsFloor || piece.IsWalkway || Storeys.StoreyOf(piece.transform) != floor)
                {
                    continue;
                }

                Bounds bounds;
                if (!TryBounds(piece, out bounds))
                {
                    continue;
                }

                var fromX = Mathf.FloorToInt(bounds.min.x / WholeFloorCell);
                var toX = Mathf.FloorToInt(bounds.max.x / WholeFloorCell);
                var fromZ = Mathf.FloorToInt(bounds.min.z / WholeFloorCell);
                var toZ = Mathf.FloorToInt(bounds.max.z / WholeFloorCell);

                for (var x = fromX; x <= toX; x++)
                {
                    for (var z = fromZ; z <= toZ; z++)
                    {
                        if (seen.Add(new Vector2Int(x, z)))
                        {
                            cells.Add(new Vector3((x + 0.5f) * WholeFloorCell, bounds.max.y, (z + 0.5f) * WholeFloorCell));
                        }
                    }
                }
            }

            return cells;
        }

        static bool TryBounds(FurniturePiece piece, out Bounds bounds)
        {
            bounds = default(Bounds);
            var any = false;

            foreach (var drawn in piece.GetComponentsInChildren<Renderer>(true))
            {
                if (!any)
                {
                    bounds = drawn.bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(drawn.bounds);
                }
            }

            return any;
        }

        static GameObject placeholder;

        /// <summary>
        /// A capsule with everything an agent needs, built once and kept for the session.
        /// </summary>
        /// <remarks>
        /// Not an asset, because a placeholder in the project would outlive the need for it. Parked
        /// far below the world rather than merely switched off: the spawner measures a prefab's
        /// renderer bounds to work out its footprint, and a renderer only reports bounds while it is
        /// enabled — so it has to be a live object that simply cannot be seen.
        /// </remarks>
        static GameObject Placeholder()
        {
            if (placeholder != null)
            {
                return placeholder;
            }

            placeholder = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            placeholder.name = "AgentPlaceholder";
            placeholder.hideFlags = HideFlags.DontSave;
            placeholder.transform.localScale = new Vector3(0.56f, 0.85f, 0.56f);
            placeholder.transform.position = new Vector3(0f, -1000f, 0f);

            var nav = placeholder.AddComponent<NavMeshAgent>();
            nav.radius = 0.28f;
            nav.height = 1.7f;
            nav.speed = 1.6f;
            nav.angularSpeed = 720f;
            nav.acceleration = 12f;
            nav.stoppingDistance = 0.1f;
            nav.enabled = false;

            placeholder.AddComponent<AgentRoutine>().enabled = false;

            Debug.LogWarning("[Agents] No agent prefab is set on the scene's world settings, so "
                + "agents will be placed as capsules. Assign one under World on the scene's "
                + "LifetimeScope.");

            return placeholder;
        }
    }
}
