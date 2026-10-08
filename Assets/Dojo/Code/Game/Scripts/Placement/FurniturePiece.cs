using Dojo.Game.Components;
using Dojo.Game.InGame.Agents;
using UnityEngine;
using UnityEngine.AI;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// Marks an object the player can pick up and put down, and owns the
    /// <see cref="NavMeshObstacle"/> that makes pathfinding notice where it now is.
    /// </summary>
    /// <remarks>
    /// The obstacle is sized from the authored footprint rather than from the mesh, so what blocks
    /// navigation is exactly what the grid reserves. Sizing it from renderer bounds instead would
    /// let an L-shaped desk block its own empty corner.
    /// <para>
    /// A piece must also be left out of the NavMesh bake. The scene paints furniture Not Walkable
    /// through <c>NavMeshModifier</c> components on the group roots, and baked data never heals —
    /// so a baked-in piece that moves at runtime would leave a permanent hole where it used to
    /// stand. The runtime carve has to be a piece's only contribution to navigation.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(NavMeshObstacle))]
    public sealed class FurniturePiece : MonoBehaviour
    {
        /// <summary>Whether a piece may be stood on top of other furniture.</summary>
        public enum Stacking
        {
            /// <summary>
            /// Decided by size: it may stand on anything it fits on. The default, and what every
            /// piece that authors nothing gets, so a new model works without being classified.
            /// </summary>
            Auto,

            /// <summary>
            /// May stand on furniture whatever the size rule says. For something the measurement
            /// reads as too big but that plainly belongs on a surface.
            /// </summary>
            Stackable,

            /// <summary>
            /// Never stands on furniture, however small. For the things size alone gets wrong: a
            /// chair, a bin, a yoga ball, anything wall or ceiling mounted.
            /// </summary>
            NonStackable,
        }

        [Header("Footprint")]
        [Tooltip("Size in grid cells at zero rotation: X across the grid, Y along it. Authored per " +
                 "piece rather than measured, so the carve matches the silhouette.")]
        [SerializeField] Vector2Int footprint = new Vector2Int(2, 4);

        [Tooltip("Height of the carve volume. This only has to clear the tallest agent that must be " +
                 "stopped — the Manager is 1.2 — not the height of the model.")]
        [SerializeField] float blockHeight = 1.2f;

        [Header("Behaviour")]
        [Tooltip("Cut a hole in the NavMesh so paths route around this piece. Leave this on for " +
                 "anything an actor must not walk through. Turning it off does not make a piece " +
                 "passable politely: the obstacle then only steers moving agents, and route " +
                 "planning ignores it, so agents path straight at the piece and shove through it.")]
        [SerializeField] bool canCarve = true;

        [Tooltip("Refuse to be picked up while somebody is using this piece, so a seated agent is " +
                 "never carried off with the furniture.")]
        [SerializeField] bool blockWhenOccupied = true;

        [Tooltip("Ground rather than something standing on it. A floor holds no grid cells, never " +
                 "obstructs navigation, counts as somewhere furniture may be put down, and makes " +
                 "the navigation mesh rebuild when it is placed. Set from a FloorPiece marker on " +
                 "the art prefab.")]
        [SerializeField] bool isFloor;

        [Tooltip("Whether this may be stood on top of other furniture. Auto decides by size - it " +
                 "may stand on anything its footprint fits inside - which is right for most things " +
                 "and needs no authoring. Override it for the pieces size alone gets wrong: a " +
                 "chair fits on a desk and has no business up there.")]
        [SerializeField] Stacking stacking = Stacking.Auto;

        NavMeshObstacle obstacle;
        Chair chair;
        int rotationSteps;

        /// <summary>Authored size in cells, before rotation.</summary>
        public Vector2Int Footprint => footprint;

        /// <summary>Quarter turns applied, 0-3.</summary>
        public int RotationSteps => rotationSteps;

        /// <summary>Size in cells as the piece currently sits.</summary>
        public Vector2Int RotatedFootprint => PlacementGrid.Rotated(footprint, rotationSteps);

        /// <summary>Whether this piece carves at all. Authored, not decided at runtime.</summary>
        public bool CanCarve => canCarve;

        /// <summary>How this piece decides whether it may sit on furniture.</summary>
        public Stacking StackingRule => stacking;

        /// <summary>True for ground: a slab that is walked on rather than stood on.</summary>
        public bool IsFloor => isFloor;

        /// <summary>
        /// True for something walked across rather than round - a staircase. It holds its cells,
        /// so nothing is put on it, but it never carves: it is baked into the walking surface instead.
        /// </summary>
        public bool IsWalkway { get; private set; }

        /// <summary>Marks a piece as a walkway, for one built from a prefab carrying a <see cref="StairsPiece"/>.</summary>
        public void SetWalkway(bool walkway)
        {
            IsWalkway = walkway;
            ApplyObstacle();
        }

        /// <summary>
        /// True for somebody rather than something: the owner or an agent, put down through the
        /// furniture path and stamped with who they are.
        /// </summary>
        /// <remarks>
        /// A person comes out of the drawer as a piece so that placing one feels like placing
        /// anything else, and stops being furniture the moment they land. They walk away from
        /// where they were put, so they hold no cells, carve nothing and hold nothing up — each of
        /// which, left to the furniture rules, turned the spot they had left into floor nothing
        /// could be built on.
        /// <para>
        /// Read from the stamp rather than the walking parts, because the stamp is on this object
        /// and the parts are on the art below it: this is asked for every hit of every probe of a
        /// drag, and a component on the same object is the cheap question.
        /// </para>
        /// </remarks>
        public bool IsPerson => GetComponent<PlacedManager>() != null || GetComponent<PlacedAgent>() != null;

        /// <summary>
        /// The piece this one is standing on, or null when it stands on the floor. Only ever set
        /// for a prop; it is what lets a desk gather its clutter when it is picked up.
        /// </summary>
        public FurniturePiece SupportedBy { get; private set; }

        /// <summary>True while the piece is being dragged and is not obstructing anything.</summary>
        public bool IsLifted { get; private set; }

        /// <summary>Lowest-corner cell of the block this piece currently holds.</summary>
        public Vector3Int OriginCell { get; private set; }

        /// <summary>
        /// Where the piece stood before it was lifted, so a cancelled drag can put it back exactly
        /// — position, cell and rotation. Restoring position alone would leave a piece turned.
        /// </summary>
        public Vector3 RestorePosition { get; private set; }

        /// <summary>Quarter turns the piece had before it was lifted.</summary>
        public int RestoreRotationSteps { get; private set; }

        /// <summary>
        /// False when the piece is in use and set to refuse it. Placement asks before picking
        /// anything up, so an occupied chair simply does not respond to a long press.
        /// </summary>
        public bool CanLift
        {
            get
            {
                if (!blockWhenOccupied)
                {
                    return true;
                }

                return Seat() == null || !Seat().IsOccupied;
            }
        }

        void Awake()
        {
            obstacle = GetComponent<NavMeshObstacle>();
            ApplyObstacle();
        }

        /// <summary>
        /// Sizes the obstacle to the footprint. Needs the cell size, which lives on the grid, so
        /// placement calls this once the piece has been handed its grid.
        /// </summary>
        /// <remarks>
        /// The size uses the <em>authored</em> footprint, not the rotated one: the transform
        /// carries the rotation, so the box turns with it and swapping the size here as well would
        /// cancel the rotation out.
        /// </remarks>
        public void SizeTo(float cellSize)
        {
            var box = GetComponent<NavMeshObstacle>();
            if (box == null)
            {
                return;
            }

            box.shape = NavMeshObstacleShape.Box;
            box.size = new Vector3(footprint.x * cellSize, blockHeight, footprint.y * cellSize);

            // Centred on the box's own height rather than the transform origin, so the volume sits
            // on the floor instead of half sunk into it.
            box.center = new Vector3(0f, blockHeight * 0.5f, 0f);
        }

        /// <summary>
        /// Turns the piece by whole quarter turns. Rotation is applied to the transform rather than
        /// to the obstacle, which keeps the footprint an integer swap.
        /// </summary>
        public void SetRotationSteps(int steps)
        {
            rotationSteps = PlacementGrid.Normalise(steps);
            transform.localRotation = Quaternion.Euler(0f, rotationSteps * 90f, 0f);
        }

        /// <summary>
        /// Sets how many cells the piece covers, for a piece built at runtime rather than authored.
        /// </summary>
        /// <remarks>
        /// Scenery pulled out of the inventory has no authored footprint — the art prefabs carry no
        /// <see cref="FurniturePiece"/> at all — so <see cref="FurnitureSpawner"/> measures one and
        /// hands it over here. Call it before <see cref="SizeTo"/>, which is what pushes the size
        /// onto the obstacle.
        /// </remarks>
        public void SetFootprint(Vector2Int cells)
        {
            footprint = new Vector2Int(Mathf.Max(1, cells.x), Mathf.Max(1, cells.y));
        }

        /// <summary>
        /// Marks a piece as ground, for one built at runtime from a prefab carrying a
        /// <see cref="FloorPiece"/>.
        /// </summary>
        public void SetFloor(bool floor)
        {
            isFloor = floor;
            ApplyObstacle();
        }

        /// <summary>
        /// Whether this piece may stand on <paramref name="support"/>.
        /// </summary>
        /// <remarks>
        /// Stackability is a relationship, not a property: whether a thing can go on top of another
        /// depends on both of them. So the default answer comes from comparing the two footprints —
        /// this one has to fit inside the other and be strictly smaller, which is what lets a
        /// monitor go on a desk while refusing a desk on a desk. No threshold, no per-model
        /// authoring, and a new model behaves sensibly the moment it is imported.
        /// <para>
        /// Only two levels exist. A support already standing on something is refused, so clutter
        /// cannot be piled up indefinitely.
        /// </para>
        /// </remarks>
        public bool CanStandOn(FurniturePiece support)
        {
            if (support == null || support == this || stacking == Stacking.NonStackable)
            {
                return false;
            }

            // Never on a person, however the sizes compare. The box fitted round a character is
            // wide enough for a chair to "fit" on, which is how a chair dragged over the spot the
            // owner had walked away from was lifted 1.9m into the air and refused.
            if (support.IsPerson)
            {
                return false;
            }

            // The ground is not something you stand *on top of* in this sense — it is where things
            // stand by default. A floor laid over another floor would be resting on it, which is
            // not a thing the two-level model has any use for.
            if (isFloor || support.IsFloor)
            {
                return false;
            }

            // Two levels only: a desk may hold a monitor, and the monitor may hold nothing.
            if (support.SupportedBy != null)
            {
                return false;
            }

            if (stacking == Stacking.Stackable)
            {
                return true;
            }

            var mine = RotatedFootprint;
            var theirs = support.RotatedFootprint;

            return mine.x <= theirs.x
                && mine.y <= theirs.y
                && mine.x * mine.y < theirs.x * theirs.y;
        }

        /// <summary>
        /// Records what this piece is standing on, so the support can collect it when it moves.
        /// Null means the floor.
        /// </summary>
        public void SetSupportedBy(FurniturePiece support)
        {
            SupportedBy = support == this ? null : support;

            // What it stands on decides whether it obstructs anything, so the obstacle is pushed
            // again here rather than waiting for the next lift or settle.
            ApplyObstacle();
        }

        /// <summary>Records the cell the placement service has given this piece.</summary>
        public void SetPlacedAt(Vector3Int origin)
        {
            OriginCell = origin;
        }

        /// <summary>
        /// Stops the piece obstructing anything so it can be moved freely, and remembers where it
        /// came from so a cancelled drag can undo the lift exactly.
        /// </summary>
        /// <remarks>
        /// Carving is disabled outright rather than left to <c>carveOnlyStationary</c>: a piece
        /// following the cursor is never stationary, but one that pauses mid-drag would otherwise
        /// settle and carve a hole where it merely hovers.
        /// </remarks>
        public void Lift()
        {
            RestorePosition = transform.position;
            RestoreRotationSteps = rotationSteps;

            IsLifted = true;
            ApplyObstacle();
        }

        /// <summary>Puts the piece back to work, obstructing wherever it now sits.</summary>
        public void Settle()
        {
            IsLifted = false;
            ApplyObstacle();
        }

        /// <summary>
        /// Brings the piece back to where the somebody inside it is now standing. Returns false,
        /// and moves nothing, when nothing inside it walks.
        /// </summary>
        /// <remarks>
        /// The spawner wraps the art in this container, and a person's <see cref="NavMeshAgent"/>
        /// rides on the art — so walking moves the art and leaves the container, with its collider,
        /// where the person was put down. Everything that asks "where is this piece" reads the
        /// container: picking it up, standing things on it, saving it. So after a walk all of them
        /// answered with the spot the person had left.
        /// <para>
        /// Only across the floor. The height is the container's own, as placement left it, and the
        /// art is put back exactly where it stood, so nothing on screen moves.
        /// </para>
        /// </remarks>
        public bool GatherToWalker()
        {
            var walker = GetComponentInChildren<NavMeshAgent>(true);

            if (walker == null || walker.transform == transform)
            {
                return false;
            }

            var body = walker.transform;
            var standing = body.position;
            var shift = new Vector3(standing.x - transform.position.x, 0f, standing.z - transform.position.z);

            if (shift.sqrMagnitude < 1e-6f)
            {
                return false;
            }

            transform.position += shift;
            body.position = standing;
            return true;
        }

        /// <summary>
        /// Pushes the current state onto the obstacle. Only a settled piece that is allowed to
        /// carve obstructs anything; everything else is invisible to navigation.
        /// </summary>
        /// <remarks>
        /// A settled piece always obstructs; only a piece in hand does not. Carving is what makes
        /// route planning respect it — an enabled but non-carving obstacle merely nudges agents
        /// that are already moving, so they plan straight through the piece and then shoulder
        /// their way across it. Anything an actor must not walk through therefore has to carve.
        /// </remarks>
        void ApplyObstacle()
        {
            var box = obstacle != null ? obstacle : GetComponent<NavMeshObstacle>();
            if (box == null)
            {
                return;
            }

            obstacle = box;

            // A piece being dragged obstructs nothing: it is not in the world yet, and carving
            // under the cursor would cut holes wherever it happened to hover. Nor does anything
            // standing on furniture: the piece underneath already blocks that ground, and a second
            // carve on top of it would only shove agents away from the desk they mean to sit at.
            // And never the ground itself: an obstacle on a floor would carve a hole through the
            // very surface it is supposed to be providing.
            // Nor a person: somebody carrying a carve stands in a hole of their own making and
            // drops off the NavMesh. The assemblers switch it off on activation, but a person
            // picked up and put down again in Edit settles here without passing through them.
            // Nor a walkway: a staircase carving itself would cut the very steps it is baked in as.
            var settled = !IsLifted && SupportedBy == null && !isFloor && !IsPerson && !IsWalkway;
            box.enabled = settled;
            box.carving = canCarve && settled;

            // Stationary-only carving keeps the expensive rebuild on the drop rather than on every
            // frame of a drag, which is what makes dragging affordable at all.
            box.carveOnlyStationary = true;
        }

        /// <summary>The chair on this piece, if it is one. Cached because a lift asks every frame.</summary>
        Chair Seat() => chair != null ? chair : (chair = GetComponent<Chair>());
    }
}
