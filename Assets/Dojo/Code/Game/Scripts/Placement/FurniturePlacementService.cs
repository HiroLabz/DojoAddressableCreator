using System.Collections.Generic;
using Dojo.Game.InGame.Agents;
using Dojo.Game.InGame.Controllers;
using UnityEngine;

namespace Dojo.Game.Placement
{
    /// <summary>Why a placement was refused, so the ghost can say which rule it broke.</summary>
    public enum PlacementRejection
    {
        /// <summary>The placement is good.</summary>
        None,

        /// <summary>Part of the footprint hangs off every placeable floor.</summary>
        OutOfBounds,

        /// <summary>Another piece already holds one of the cells.</summary>
        CellsTaken,

        /// <summary>Somebody is standing in the footprint.</summary>
        AgentInTheWay,

        /// <summary>
        /// A prop has nothing solid under all of it - hanging off the edge of the desk, or over a
        /// gap between two pieces.
        /// </summary>
        Unsupported,

        /// <summary>
        /// An elevator with nowhere to go: no other floor has ground under the whole of this spot,
        /// so there would be no other stop to ride to.
        /// </summary>
        NoOtherFloor,

        /// <summary>
        /// An elevator whose stop on another floor would land on something already standing there.
        /// </summary>
        BlockedOnAnotherFloor,
    }

    /// <summary>
    /// Owns which cells are taken and decides whether a piece may go somewhere. Cells are claimed
    /// on commit and released on lift, so a piece in mid-drag holds nothing.
    /// </summary>
    /// <remarks>
    /// Placement bounds are the union of every floor in the scene, not one area's slab. The
    /// <c>setfloor</c> objects are a grouping for agents' roaming, not a fence for furniture, so a
    /// chair picked up in one area may be put down in any other or in the shared space between
    /// them. The test is on X and Z only, matching <c>AgentRoutine.IsHome</c>: the NavMesh sits
    /// slightly above a slab's own bounds, so including height would reject everything.
    /// </remarks>
    [RequireComponent(typeof(PlacementGrid))]
    public sealed class FurniturePlacementService : MonoBehaviour
    {
        [Tooltip("Floors furniture may stand on. Left empty, every renderer whose name matches one " +
                 "of the names below is collected on startup.")]
        [SerializeField] Renderer[] placeableFloors;

        [Tooltip("Object names treated as floor when the list above is empty. The area slabs are " +
                 "only a grouping, so every floor in the scene counts and a piece from one area " +
                 "may be put down in any other.")]
        [SerializeField] string[] floorNames = { "setfloor", "floor", "Floor" };

        [Tooltip("How close an agent's centre must be to a cell's centre to count as standing in " +
                 "it. Roughly an agent radius plus the half cell it could be standing over.")]
        [SerializeField] float agentClearance = 0.45f;

        [Tooltip("Refuse a drop when somebody is standing in the cells. Belongs to the authored " +
                 "scene, where agents live on fixed area floors and the player only moves the " +
                 "furniture around them. Leave it off in a world the player builds: there, agents " +
                 "are put down by hand and this refuses that outright.")]
        [SerializeField] bool refuseUnderOccupants;

        [Tooltip("Largest footprint, in cells, that will be accepted. A piece bigger than this is " +
                 "refused with a warning rather than claiming the map: an oversized footprint " +
                 "means the piece is scaled oddly or is not really furniture, and silently " +
                 "reserving thousands of cells is far harder to notice than a warning.")]
        [SerializeField] int maxFootprintCells = 600;

        [Tooltip("How far apart the surface heights under a prop's cells may be and still count as " +
                 "one flat surface, in metres. This is what stops a mug settling half on a desk " +
                 "and half in mid-air.")]
        [SerializeField] float supportTolerance = 0.05f;

        [Tooltip("How far a thing's underside may be from a piece's top and still count as standing " +
                 "on it, in metres. Deliberately looser than the tolerance above: that one judges " +
                 "a placement the game itself made, while this one judges scenery a person placed " +
                 "by eye, and authored props are routinely a few centimetres sunk into the surface " +
                 "they sit on.")]
        [SerializeField] float standingTolerance = 0.15f;

        [Tooltip("How high above a piece to look for things standing on it, in metres. Only needs " +
                 "to be tall enough to catch the bottom of whatever is up there - what actually " +
                 "decides is whether its underside meets the piece's top.")]
        [SerializeField] float standingProbeHeight = 2f;

        readonly Dictionary<Vector3Int, FurniturePiece> occupancy = new Dictionary<Vector3Int, FurniturePiece>();
        readonly List<Vector3Int> scratch = new List<Vector3Int>();
        readonly List<Vector3Int> stale = new List<Vector3Int>();
        readonly List<Bounds> floors = new List<Bounds>();

        PlacementGrid placementGrid;

        /// <summary>The grid these cells belong to.</summary>
        public PlacementGrid Grid => placementGrid != null
            ? placementGrid
            : (placementGrid = GetComponent<PlacementGrid>());

        /// <summary>How many cells are currently claimed. Diagnostics only.</summary>
        public int ClaimedCellCount => occupancy.Count;

        /// <summary>
        /// Largest footprint that will be accepted, in cells. Read by
        /// <see cref="FurnitureSpawner"/> so a piece measured from the inventory is held to the
        /// same limit as one authored into the scene.
        /// </summary>
        public int MaxFootprintCells => maxFootprintCells;

        /// <summary>How high above a cell the search for a surface starts, and how far it reaches.</summary>
        /// <remarks>
        /// Generous enough to clear anything in the pack and cheap enough not to matter: a prop is
        /// small by definition, so this runs over a handful of cells rather than a footprint.
        /// </remarks>
        const float SupportProbeTop = 20f;

        const float SupportProbeDistance = 40f;

        void Awake()
        {
            placementGrid = GetComponent<PlacementGrid>();
            RefreshFloors();
        }

        /// <summary>
        /// Sizes and registers every piece standing in the scene. Done here rather than in each
        /// piece's own Start so the obstacle size comes from the grid's cell size — a piece has no
        /// business knowing the grid, and this way changing the cell size re-sizes everything.
        /// </summary>
        /// <remarks>
        /// Only pieces that are actually switched on. A disabled one is not in the world: it draws
        /// nothing, collides with nothing and obstructs nothing, so letting it hold ground means
        /// reserving cells that look completely empty and refusing everything the player tries to
        /// put there. That is not hypothetical — switching off an authored environment to build a
        /// new one in its place left forty-one invisible pieces holding a thousand cells around the
        /// origin, and every drop over them came back CellsTaken with nothing on screen to explain
        /// it. The floor list next door has always ignored inactive objects; this now agrees.
        /// </remarks>
        void Start()
        {
            var cell = Grid.CellSize;
            var registered = 0;

            foreach (var piece in FindObjectsByType<FurniturePiece>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                // A piece parented into a Canvas is a UI preview model, not scenery. Its transform
                // lives in UI space, so registering it would reserve cells far outside the world.
                if (piece.GetComponentInParent<Canvas>() != null)
                {
                    Debug.LogWarning(
                        "[Placement] Ignoring FurniturePiece on '" + piece.name + "': it is inside a Canvas, "
                        + "so it is a UI model rather than furniture in the world.", piece);
                    continue;
                }

                piece.SizeTo(cell);
                WarnIfStaticallyBatched(piece);

                if (Register(piece))
                {
                    registered++;
                }
            }

            // Again now that everything is awake, so a floor authored into the scene counts as
            // ground even though Awake ran before it had been found.
            RefreshFloors();

            var stacked = ResolveSupports();

            Debug.Log("[Placement] registered " + registered + " pieces holding " + occupancy.Count
                + " cells, on a " + cell + "m grid across " + floors.Count + " floors"
                + (stacked > 0 ? ", " + stacked + " of them standing on another piece" : "") + ".", this);
        }

        /// <summary>
        /// Works out what each piece in the scene is standing on, and records it.
        /// </summary>
        /// <remarks>
        /// A piece only learns what holds it up when it is committed through a placement gesture,
        /// and nothing in a scene has been through one. Without this a piece authored on top of a
        /// desk is invisible as such: it holds ground-floor cells it is not really on, it carves
        /// navigation it should not, and the desk leaves it hanging in the air when it is moved or
        /// thrown away — every one of which works correctly for the same piece if the player put it
        /// there instead. Establishing it here means authored and placed pieces are the same thing
        /// from the first frame.
        /// <para>
        /// Runs after every piece is registered, because the probe reads their colliders, and their
        /// transforms are synced first because this project keeps
        /// <see cref="Physics.autoSyncTransforms"/> off.
        /// </para>
        /// </remarks>
        public int ResolveSupports()
        {
            Physics.SyncTransforms();

            var stacked = 0;

            foreach (var piece in FindObjectsByType<FurniturePiece>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (piece.GetComponentInParent<Canvas>() != null)
                {
                    continue;   // a UI model, ignored on the way in and ignored here too
                }

                float top;
                FurniturePiece support;
                bool anySurface;
                TrySupportUnder(piece, piece.OriginCell, piece.RotatedFootprint, out top, out support, out anySurface);

                if (support == null)
                {
                    continue;   // standing on the floor, which is what a piece is assumed to do
                }

                piece.SetSupportedBy(support);

                // Its cells belong on the level above now, so the claim is redone.
                Register(piece);
                stacked++;
            }

            return stacked;
        }

        /// <summary>
        /// Gathers the ground furniture may stand on: every piece marked as a floor, plus the
        /// scene's own painted-in slabs.
        /// </summary>
        /// <remarks>
        /// The painted-in slabs are found by name rather than by layer, because they predate the
        /// placement layers and every other system identifies them this way too. Floors laid down
        /// by the player are found by their component instead, which is the only thing that can
        /// work for a piece that did not exist when the scene was authored.
        /// <para>
        /// Call this again whenever a floor is laid, moved or taken away — the list is a snapshot
        /// of bounds, and a stale one puts furniture out of bounds over ground that plainly exists.
        /// </para>
        /// </remarks>
        public void RefreshFloors()
        {
            floors.Clear();

            // Ground the player has laid. Measured from renderers rather than colliders, and
            // deliberately: this project runs with Physics.autoSyncTransforms off, so a collider
            // still reports the position it had at the last fixed step. A floor is asked about the
            // instant it is put down, and a stale rectangle puts furniture out of bounds over
            // ground that is plainly there. Renderer bounds follow the transform immediately, and
            // it is what the painted-in slabs below have always been measured by anyway.
            foreach (var piece in FindObjectsByType<FurniturePiece>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                Bounds slab;
                if (piece.IsFloor && TryRendererBounds(piece.gameObject, out slab))
                {
                    floors.Add(slab);
                }
            }

            if (placeableFloors != null && placeableFloors.Length > 0)
            {
                foreach (var floor in placeableFloors)
                {
                    if (floor != null)
                    {
                        floors.Add(floor.bounds);
                    }
                }

                return;
            }

            foreach (var found in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (IsNamedFloor(found.gameObject.name))
                {
                    floors.Add(found.bounds);
                }
            }
        }

        /// <summary>How many patches of ground furniture may currently stand on.</summary>
        public int FloorCount => floors.Count;

        /// <summary>
        /// Complains if a placeable piece has been merged into a static batch. Unity welds
        /// batching-static meshes into one shared buffer at load, so moving the transform moves
        /// nothing on screen: the piece silently appears frozen while every log and every cell
        /// claim says it moved. Worth a loud warning, because nothing else about it looks wrong.
        /// </summary>
        void WarnIfStaticallyBatched(FurniturePiece piece)
        {
            foreach (var renderer in piece.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.isPartOfStaticBatch)
                {
                    continue;
                }

                Debug.LogError(
                    "[Placement] '" + piece.name + "' is in a static batch, so dragging it will move its "
                    + "transform but not its visible mesh. Clear the Static flags on it and its children.",
                    piece);
                return;
            }
        }

        bool IsNamedFloor(string name)
        {
            if (floorNames == null)
            {
                return false;
            }

            foreach (var candidate in floorNames)
            {
                if (name == candidate)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Records where a piece already stands without asking whether it is a legal spot, so
        /// pieces authored into the scene hold their cells from the first frame. A piece authored
        /// overlapping another keeps its place rather than being silently shoved aside.
        /// </summary>
        public bool Register(FurniturePiece piece)
        {
            if (piece == null)
            {
                return false;
            }

            var footprint = piece.RotatedFootprint;

            // Ground is exempt from the cap. It exists to stop a mis-scaled model reserving half
            // the map, and ground reserves nothing at all — a floor slab is legitimately larger
            // than any piece of furniture has business being.
            if (!piece.IsFloor && footprint.x * footprint.y > maxFootprintCells)
            {
                Debug.LogWarning(
                    "[Placement] Ignoring FurniturePiece on '" + piece.name + "': footprint " + footprint
                    + " is " + (footprint.x * footprint.y) + " cells, over the " + maxFootprintCells
                    + " allowed. Check the object's scale.", piece);
                return false;
            }

            var origin = Grid.OriginFor(piece.transform.position, footprint, LevelOf(piece));
            Claim(piece, origin, footprint);
            return true;
        }

        /// <summary>
        /// Which level a piece holds its cells on: the ground, unless it is standing on something.
        /// </summary>
        public static int LevelOf(FurniturePiece piece)
            => piece != null && piece.SupportedBy != null
                ? PlacementGrid.SurfaceLevel
                : PlacementGrid.FloorLevel;

        /// <summary>
        /// Finds the flat surface a prop would stand on across its whole footprint: the top of a
        /// piece of furniture, or the floor.
        /// </summary>
        /// <remarks>
        /// Every cell is probed, not just the middle, and they all have to agree to within
        /// <see cref="supportTolerance"/> - that is the test that refuses a mug hanging off the
        /// edge of a desk, or bridging the gap between two of them.
        /// <para>
        /// What counts as a surface is deliberately narrow: a piece of furniture that is not itself
        /// a prop, or a floor. Walls and agents are hit by the probe and ignored, so a mug cannot be
        /// stood on somebody's head, and clutter cannot be stacked on clutter.
        /// </para>
        /// </remarks>
        /// <param name="support">The piece holding it up, or null when that is the floor.</param>
        /// <param name="anySurface">
        /// Whether anything at all was found under any part of it. When this is true,
        /// <paramref name="top"/> and <paramref name="support"/> describe the highest surface met
        /// even if the answer is a refusal — so a caller can keep drawing the piece there rather
        /// than dropping it to the floor and lifting it again every time the cursor crosses a rim.
        /// </param>
        /// <returns>True only when the whole footprint rests on one flat surface.</returns>
        public bool TrySupportUnder(
            FurniturePiece piece,
            Vector3Int origin,
            Vector2Int rotatedFootprint,
            out float top,
            out FurniturePiece support,
            out bool anySurface)
        {
            top = 0f;
            support = null;
            anySurface = false;

            Grid.CellsFor(origin, rotatedFootprint, scratch);

            var lowest = float.MaxValue;
            var highest = float.MinValue;
            var everyCell = true;

            foreach (var cell in scratch)
            {
                float cellTop;
                FurniturePiece cellSupport;

                if (!TrySurfaceAt(Grid.CellCentre(cell), piece, out cellTop, out cellSupport))
                {
                    everyCell = false;   // nothing under this part of it
                    continue;
                }

                if (!anySurface || cellTop > highest)
                {
                    highest = cellTop;
                    support = cellSupport;
                }

                if (!anySurface || cellTop < lowest)
                {
                    lowest = cellTop;
                }

                anySurface = true;
            }

            if (!anySurface)
            {
                return false;
            }

            top = highest;

            // Straddling an edge: part of it would float.
            return everyCell && highest - lowest <= supportTolerance;
        }

        /// <summary>
        /// Where a ray from the cursor meets something a piece could stand on: the top of a piece
        /// of furniture, or the floor. The nearest such surface wins.
        /// </summary>
        /// <remarks>
        /// This is what a prop is positioned by, and it exists because the alternative does not
        /// work for one. Everything else is placed by casting the cursor onto a horizontal plane,
        /// but a plane has to be at <em>some</em> height, and on this isometric camera a plane at
        /// desk height and one at floor height put the same pixel over 1.6m apart. Taking the
        /// height from whatever the piece is over, and the piece's position from that height, is a
        /// loop — it oscillates every frame the cursor is near a table edge. A ray closes the loop:
        /// the geometry under the cursor answers directly, and the answer does not depend on where
        /// the piece currently is.
        /// <para>
        /// Only surfaces the piece in hand may actually stand on are considered — it must fit, and
        /// they must be on the floor themselves — which is what makes a ray usable here at all. A
        /// ray otherwise meets the piece being dragged, or one of the hundred desktop props on the
        /// default layer, long before it meets anything worth standing on. A piece with nowhere to
        /// climb simply gets no hit and is placed on the floor as it always was.
        /// </para>
        /// </remarks>
        /// <param name="storey">
        /// The floor being looked at. A surface on any other floor is ignored: the floors above are
        /// hidden but still there, and the ray meets them first.
        /// </param>
        public bool TryCursorSurface(
            Ray ray,
            float maxDistance,
            FurniturePiece ignore,
            int storey,
            out Vector3 point,
            out FurniturePiece support)
        {
            point = Vector3.zero;
            support = null;

            var hits = Physics.RaycastAll(ray, maxDistance, ~0, QueryTriggerInteraction.Ignore);
            var nearest = float.MaxValue;
            var found = false;

            for (var i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];

                if (Grid.StoreyAt(hit.point.y) != storey)
                {
                    continue;
                }

                var piece = hit.transform.GetComponentInParent<FurniturePiece>();

                if (piece != null && !piece.IsFloor)
                {
                    if (ignore == null || !ignore.CanStandOn(piece))
                    {
                        continue;
                    }
                }
                else if (piece == null && !IsNamedFloor(hit.transform.gameObject.name))
                {
                    continue;
                }

                if (hit.distance >= nearest)
                {
                    continue;
                }

                nearest = hit.distance;
                point = hit.point;
                support = piece != null && piece.IsFloor ? null : piece;
                found = true;
            }

            return found;
        }

        /// <summary>
        /// The highest thing a prop could stand on at one point, on the floor that point's height
        /// names.
        /// </summary>
        /// <remarks>
        /// The probe still drops from high above, but only surfaces on this floor count: anything
        /// higher is the floor above, or something standing on it, and "the highest surface" would
        /// otherwise always be up there.
        /// </remarks>
        bool TrySurfaceAt(Vector3 point, FurniturePiece ignore, out float top, out FurniturePiece support)
        {
            top = 0f;
            support = null;
            var found = false;
            var storey = Grid.StoreyAt(point.y);

            // The floor first, and from its own bounds rather than a collider: the slabs are found
            // by name everywhere else in this class and need not have colliders at all.
            float floorTop;
            if (TryFloorTopAt(point, out floorTop))
            {
                top = floorTop;
                found = true;
            }

            var from = new Vector3(point.x, Grid.StoreyBase(storey) + SupportProbeTop, point.z);
            var hits = Physics.RaycastAll(
                from, Vector3.down, SupportProbeDistance, ~0, QueryTriggerInteraction.Ignore);

            for (var i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];

                if (Grid.StoreyAt(hit.point.y) != storey)
                {
                    continue;   // another floor, or something on one
                }

                var piece = hit.transform.GetComponentInParent<FurniturePiece>();

                if (piece != null && !piece.IsFloor)
                {
                    // Only somewhere this particular piece is allowed to stand: it has to fit, and
                    // the support has to be on the floor itself. That is what keeps a sofa off a
                    // coffee table while letting a monitor onto a desk.
                    if (ignore == null || !ignore.CanStandOn(piece))
                    {
                        continue;
                    }
                }
                else if (piece == null && !IsNamedFloor(hit.transform.gameObject.name))
                {
                    continue;   // a wall, an agent, scenery - nothing to stand anything on
                }

                var surface = hit.point.y;
                if (!found || surface > top)
                {
                    top = surface;

                    // A floor supplies a height but is not a support: something standing on one is
                    // on the ground, at ground level, exactly as it is on a painted-in slab.
                    support = piece != null && piece.IsFloor ? null : piece;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>
        /// Everything resting on top of <paramref name="piece"/>, furniture or not.
        /// </summary>
        /// <remarks>
        /// Answered by collision rather than by bookkeeping, because most of what stands on a desk
        /// in an authored scene has no bookkeeping to consult: the office set's monitors, keyboards
        /// and mice are plain scenery in a different branch of the hierarchy entirely, with no
        /// <see cref="FurniturePiece"/> and no link of any kind to the desk under them. Touching
        /// something is the only evidence there is.
        /// <para>
        /// What comes back is the whole object, not the mesh that happened to be hit — the search
        /// climbs from each collider to the highest ancestor still standing within the piece's
        /// own outline, so a monitor returns the PC it belongs to rather than just its screen, and
        /// stops short of the shared container holding every PC in the area.
        /// </para>
        /// <para>
        /// People are never included, whatever they are standing on or in.
        /// </para>
        /// </remarks>
        public void CollectStandingOn(FurniturePiece piece, List<GameObject> into)
        {
            into.Clear();

            if (piece == null)
            {
                return;
            }

            Bounds outline;
            if (!TryColliderBounds(piece.gameObject, out outline))
            {
                return;
            }

            var top = outline.max.y;
            var half = standingProbeHeight * 0.5f;

            var hits = Physics.OverlapBox(
                new Vector3(outline.center.x, top + half, outline.center.z),
                new Vector3(outline.extents.x, half, outline.extents.z),
                Quaternion.identity,
                ~0,
                QueryTriggerInteraction.Ignore);

            for (var i = 0; i < hits.Length; i++)
            {
                var standing = StandingRoot(hits[i].transform, piece, outline, top);

                if (standing != null && !into.Contains(standing))
                {
                    into.Add(standing);
                }
            }
        }

        /// <summary>
        /// The whole object a hit collider belongs to, if it is really standing on this piece.
        /// Null when it is the piece itself, a person, the floor, or merely passing overhead.
        /// </summary>
        GameObject StandingRoot(Transform hit, FurniturePiece piece, Bounds outline, float top)
        {
            // Never a person, whatever they are standing on, near or inside.
            if (hit.GetComponentInParent<AgentRoutine>() != null
                || hit.GetComponentInParent<ManagerController>() != null)
            {
                return null;
            }

            if (IsNamedFloor(hit.gameObject.name))
            {
                return null;
            }

            // The piece itself, or something already riding on it and going with it anyway.
            if (hit.IsChildOf(piece.transform))
            {
                return null;
            }

            var standing = hit;
            var onPiece = hit.GetComponentInParent<FurniturePiece>();

            if (onPiece != null)
            {
                standing = onPiece.transform;
            }
            else
            {
                // Loose scenery: climb while the whole of what is above still stands within this
                // piece's outline. That reaches the PC and stops below the container of every PC.
                while (standing.parent != null)
                {
                    Bounds parentBounds;
                    if (!TryRendererBounds(standing.parent.gameObject, out parentBounds))
                    {
                        break;
                    }

                    if (!WithinOutline(parentBounds, outline))
                    {
                        break;
                    }

                    standing = standing.parent;
                }
            }

            // Standing on it, rather than floating over it or built into it.
            Bounds resting;
            if (!TryRendererBounds(standing.gameObject, out resting))
            {
                return null;
            }

            return Mathf.Abs(resting.min.y - top) <= standingTolerance ? standing.gameObject : null;
        }

        static bool WithinOutline(Bounds candidate, Bounds outline)
            => candidate.min.x >= outline.min.x - 0.05f
                && candidate.max.x <= outline.max.x + 0.05f
                && candidate.min.z >= outline.min.z - 0.05f
                && candidate.max.z <= outline.max.z + 0.05f;

        /// <summary>
        /// The space a piece's colliders take up. This is the physical object, not the drawn one:
        /// what things rest on, and what a probe can hit.
        /// </summary>
        public static bool TryColliderBounds(GameObject root, out Bounds bounds)
        {
            bounds = new Bounds();
            var found = false;

            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
            {
                if (!found) { bounds = collider.bounds; found = true; }
                else { bounds.Encapsulate(collider.bounds); }
            }

            return found;
        }

        static bool TryRendererBounds(GameObject root, out Bounds bounds)
        {
            bounds = new Bounds();
            var found = false;

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer) { continue; }
                if (!found) { bounds = renderer.bounds; found = true; }
                else { bounds.Encapsulate(renderer.bounds); }
            }

            return found;
        }

        /// <summary>
        /// Nudges a floor so its edge meets a floor already laid, if one is close enough.
        /// </summary>
        /// <remarks>
        /// Ground cannot tile on the grid alone, and no amount of care with the grid will fix it.
        /// The office pack's slabs are 4.89m across on a 0.25m grid — 19.56 cells — so their
        /// footprint rounds up to 20 and the grid puts consecutive slabs 5m apart, leaving an 11cm
        /// seam every time. Edges have to be matched to each other rather than to the grid.
        /// <para>
        /// Each axis is settled separately, against three candidates per neighbour: butted against
        /// its far edge, butted against its near edge, and lined up with its middle. The last is
        /// what lets a row of slabs stay in line rather than drifting a few centimetres sideways as
        /// it grows. Only floors already alongside are considered — a slab across the room has no
        /// business pulling this one towards it.
        /// </para>
        /// </remarks>
        /// <param name="neighbours">Filled with the floors that were snapped to, for highlighting.</param>
        public bool TrySnapFloor(
            FurniturePiece floor,
            Vector3 desiredCentre,
            float snapDistance,
            out Vector3 snappedCentre,
            List<FurniturePiece> neighbours)
        {
            snappedCentre = desiredCentre;

            if (neighbours != null)
            {
                neighbours.Clear();
            }

            Bounds own;
            if (floor == null || !floor.IsFloor || snapDistance <= 0f
                || !TryRendererBounds(floor.gameObject, out own))
            {
                return false;
            }

            var half = own.extents;
            var storey = Grid.StoreyAt(own.max.y);

            var bestX = float.MaxValue;
            var bestZ = float.MaxValue;
            var atX = desiredCentre.x;
            var atZ = desiredCentre.z;
            FurniturePiece byX = null;
            FurniturePiece byZ = null;

            foreach (var other in FindObjectsByType<FurniturePiece>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (other == floor || !other.IsFloor)
                {
                    continue;
                }

                Bounds slab;
                if (!TryRendererBounds(other.gameObject, out slab))
                {
                    continue;
                }

                // A tile on another floor is not alongside, however close it looks from above.
                if (Grid.StoreyAt(slab.max.y) != storey)
                {
                    continue;
                }

                // Alongside, rather than anywhere in the room.
                var reachesX = desiredCentre.x + half.x >= slab.min.x - snapDistance
                    && desiredCentre.x - half.x <= slab.max.x + snapDistance;
                var reachesZ = desiredCentre.z + half.z >= slab.min.z - snapDistance
                    && desiredCentre.z - half.z <= slab.max.z + snapDistance;

                if (!reachesX || !reachesZ)
                {
                    continue;
                }

                Closest(slab.max.x + half.x, desiredCentre.x, snapDistance, other, ref bestX, ref atX, ref byX);
                Closest(slab.min.x - half.x, desiredCentre.x, snapDistance, other, ref bestX, ref atX, ref byX);
                Closest(slab.center.x, desiredCentre.x, snapDistance, other, ref bestX, ref atX, ref byX);

                Closest(slab.max.z + half.z, desiredCentre.z, snapDistance, other, ref bestZ, ref atZ, ref byZ);
                Closest(slab.min.z - half.z, desiredCentre.z, snapDistance, other, ref bestZ, ref atZ, ref byZ);
                Closest(slab.center.z, desiredCentre.z, snapDistance, other, ref bestZ, ref atZ, ref byZ);
            }

            if (byX == null && byZ == null)
            {
                return false;
            }

            snappedCentre = new Vector3(
                byX != null ? atX : desiredCentre.x,
                desiredCentre.y,
                byZ != null ? atZ : desiredCentre.z);

            if (neighbours != null)
            {
                if (byX != null)
                {
                    neighbours.Add(byX);
                }

                if (byZ != null && !neighbours.Contains(byZ))
                {
                    neighbours.Add(byZ);
                }
            }

            return true;
        }

        /// <summary>Keeps the nearest candidate for one axis, if it is near enough to count.</summary>
        static void Closest(
            float candidate,
            float desired,
            float limit,
            FurniturePiece other,
            ref float best,
            ref float chosen,
            ref FurniturePiece by)
        {
            var distance = Mathf.Abs(candidate - desired);

            if (distance > limit || distance >= best)
            {
                return;
            }

            best = distance;
            chosen = candidate;
            by = other;
        }

        /// <summary>Gives up every cell a piece holds, so it can be dragged over its own footprint.</summary>
        public void Release(FurniturePiece piece)
        {
            if (piece == null)
            {
                return;
            }

            stale.Clear();

            foreach (var pair in occupancy)
            {
                if (pair.Value == piece)
                {
                    stale.Add(pair.Key);
                }
            }

            foreach (var cell in stale)
            {
                occupancy.Remove(cell);
            }
        }

        /// <summary>
        /// Whether <paramref name="piece"/> may occupy the block at <paramref name="origin"/>.
        /// Cells held by the piece itself never count against it, so a piece can always settle back
        /// where it came from.
        /// </summary>
        public PlacementRejection Validate(FurniturePiece piece, Vector3Int origin, Vector2Int rotatedFootprint)
        {
            // Ground goes wherever it is put. It holds no cells, so nothing can be in its way; it
            // is not standing on anything, so it cannot be out of bounds; and laying it under
            // somebody's feet is how a room gets built, not a mistake.
            if (piece != null && piece.IsFloor)
            {
                return PlacementRejection.None;
            }

            Grid.CellsFor(origin, rotatedFootprint, scratch);

            foreach (var cell in scratch)
            {
                if (!IsOnAFloor(Grid.CellCentre(cell)))
                {
                    return PlacementRejection.OutOfBounds;
                }

                FurniturePiece holder;
                if (occupancy.TryGetValue(cell, out holder) && holder != null && holder != piece)
                {
                    return PlacementRejection.CellsTaken;
                }
            }

            // Off by default, because it only makes sense in the authored scene. There, agents
            // stand on fixed area floors and the player rearranges the furniture around them, so
            // refusing to carve under somebody is a kindness. In a world the player builds, agents
            // are something they put down by hand — and a rule that refuses to place a thing where
            // that thing now is refuses it everywhere.
            //
            // Only on the ground even when it is on: somebody standing beside a desk is no reason
            // to refuse a mug on top of it. Left until last either way, because it walks every
            // agent in the scene while the tests above are lookups.
            if (refuseUnderOccupants
                && PlacementGrid.LevelOfKey(origin.z) == PlacementGrid.FloorLevel
                && AnyoneStandingIn(scratch, piece))
            {
                return PlacementRejection.AgentInTheWay;
            }

            // Rules that reach beyond this floor - an elevator's stops on the others. Last, because
            // they are the most expensive and only matter once the spot itself is fine.
            foreach (var rule in rules)
            {
                var refused = rule(piece, origin, rotatedFootprint);

                if (refused != PlacementRejection.None)
                {
                    return refused;
                }
            }

            return PlacementRejection.None;
        }

        readonly List<System.Func<FurniturePiece, Vector3Int, Vector2Int, PlacementRejection>> rules =
            new List<System.Func<FurniturePiece, Vector3Int, Vector2Int, PlacementRejection>>();

        /// <summary>
        /// Adds a rule <see cref="Validate"/> also asks, once a spot is fine on its own floor. For
        /// pieces whose placement depends on other floors, like an elevator's.
        /// </summary>
        public void AddRule(System.Func<FurniturePiece, Vector3Int, Vector2Int, PlacementRejection> rule)
        {
            if (rule != null && !rules.Contains(rule))
            {
                rules.Add(rule);
            }
        }

        public void RemoveRule(System.Func<FurniturePiece, Vector3Int, Vector2Int, PlacementRejection> rule)
        {
            rules.Remove(rule);
        }

        /// <summary>
        /// Whether every cell of the block at <paramref name="origin"/> is over a floor tile on the
        /// floor its Z names.
        /// </summary>
        public bool IsGroundUnder(Vector3Int origin, Vector2Int rotatedFootprint)
        {
            Grid.CellsFor(origin, rotatedFootprint, scratch);

            foreach (var cell in scratch)
            {
                if (!IsOnAFloor(Grid.CellCentre(cell)))
                {
                    return false;
                }
            }

            return scratch.Count > 0;
        }

        /// <summary>Whether an agent or the manager is standing in the block at <paramref name="origin"/>.</summary>
        public bool AnyoneIn(Vector3Int origin, Vector2Int rotatedFootprint)
        {
            Grid.CellsFor(origin, rotatedFootprint, scratch);
            return AnyoneStandingIn(scratch, null);
        }

        /// <summary>
        /// Claims the block for <paramref name="piece"/> if it is legal, releasing whatever it held
        /// before. Returns false and changes nothing when the spot is refused.
        /// </summary>
        public bool TryClaim(FurniturePiece piece, Vector3Int origin, Vector2Int rotatedFootprint)
        {
            if (Validate(piece, origin, rotatedFootprint) != PlacementRejection.None)
            {
                return false;
            }

            Claim(piece, origin, rotatedFootprint);
            return true;
        }

        /// <summary>Which piece holds a cell, or null.</summary>
        public FurniturePiece HolderOf(Vector3Int cell)
        {
            FurniturePiece piece;
            return occupancy.TryGetValue(cell, out piece) ? piece : null;
        }

        /// <summary>
        /// Records the cells a piece holds. The one place occupancy is ever written, which is why
        /// the rule about ground lives here rather than in either of the two ways in.
        /// </summary>
        /// <remarks>
        /// Ground holds no cells. Reserving the whole slab would leave nowhere to put anything on
        /// it, which is the exact opposite of what a floor is for — it is where furniture goes, not
        /// something competing with it for room. The outline is still recorded so logs and the
        /// grid maths have something to report; being somewhere furniture may stand is handled by
        /// the floor list instead.
        /// </remarks>
        void Claim(FurniturePiece piece, Vector3Int origin, Vector2Int rotatedFootprint)
        {
            Release(piece);
            piece.SetPlacedAt(origin);

            // Nor does a person. They walk away from the cells they were put down on, and cells
            // held by somebody who has left are floor nothing can be built on again. The
            // assemblers release them on activation, but a person moved in Edit is committed here
            // and never goes back through an assembler.
            if (piece.IsFloor || piece.IsPerson)
            {
                return;
            }

            Grid.CellsFor(origin, rotatedFootprint, scratch);

            foreach (var cell in scratch)
            {
                occupancy[cell] = piece;
            }
        }

        /// <summary>
        /// Top surface height of the floor under <paramref name="point"/>, or false if there is
        /// none. The highest match wins, so an area slab beats the world floor it rests on.
        /// </summary>
        /// <remarks>
        /// Furniture cannot supply this: pieces are authored sunk into the slab, so a chair's own Y
        /// is below the surface it appears to stand on. Anything drawn flat on the ground has to
        /// ask the floor.
        /// <para>
        /// Only tiles on the floor <paramref name="point"/>'s height names are considered. With
        /// floors stacked, "the highest tile here" is otherwise the floor above.
        /// </para>
        /// </remarks>
        public bool TryFloorTopAt(Vector3 point, out float top)
        {
            top = 0f;
            var found = false;
            var storey = Grid.StoreyAt(point.y);

            foreach (var floor in floors)
            {
                if (point.x < floor.min.x || point.x > floor.max.x
                    || point.z < floor.min.z || point.z > floor.max.z
                    || Grid.StoreyAt(floor.max.y) != storey)
                {
                    continue;
                }

                if (!found || floor.max.y > top)
                {
                    top = floor.max.y;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>
        /// XZ footprint test, matching how an agent decides it is on its home floor - against the
        /// tiles of the floor <paramref name="point"/>'s height names only.
        /// </summary>
        bool IsOnAFloor(Vector3 point)
        {
            var storey = Grid.StoreyAt(point.y);

            foreach (var floor in floors)
            {
                if (point.x >= floor.min.x && point.x <= floor.max.x
                    && point.z >= floor.min.z && point.z <= floor.max.z
                    && Grid.StoreyAt(floor.max.y) == storey)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True when an agent or the manager is standing in any of these cells. Carving under
        /// somebody drops them off the NavMesh, so the drop is refused here rather than left to the
        /// recovery code that would otherwise have to catch it.
        /// </summary>
        /// <param name="ignore">
        /// The piece being placed. Anybody riding on it is skipped — see the remarks.
        /// </param>
        /// <remarks>
        /// The exemption is the same one the cell test above makes with <c>holder != piece</c>, and
        /// for the same reason: a piece cannot be in its own way.
        /// <para>
        /// Without it an agent could never be put down at all. A placed agent carries an
        /// <see cref="AgentRoutine"/>, and a disabled component on an active object is still
        /// returned by <c>FindObjectsByType</c> — so the agent on the cursor was found standing in
        /// the very cells it was asking for, and every spot on every floor came back
        /// <c>AgentInTheWay</c>. It was refusing to place itself.
        /// </para>
        /// </remarks>
        bool AnyoneStandingIn(List<Vector3Int> cells, FurniturePiece ignore)
        {
            foreach (var agent in FindObjectsByType<AgentRoutine>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (Rides(agent.transform, ignore))
                {
                    continue;
                }

                if (IsStandingIn(cells, agent.transform.position))
                {
                    return true;
                }
            }

            foreach (var manager in FindObjectsByType<ManagerController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (Rides(manager.transform, ignore))
                {
                    continue;
                }

                if (IsStandingIn(cells, manager.transform.position))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True when <paramref name="who"/> is the piece being placed, or part of it.
        /// </summary>
        /// <remarks>
        /// <c>IsChildOf</c> counts a transform as a child of itself, which is wanted: the spawner
        /// wraps a prefab it was handed, so an agent's own components can end up either on the
        /// piece root or on the art below it depending on whether the prefab carried its own
        /// <see cref="FurniturePiece"/>. Both are the same object as far as this question goes.
        /// </remarks>
        static bool Rides(Transform who, FurniturePiece piece)
            => piece != null && who != null && who.IsChildOf(piece.transform);

        bool IsStandingIn(List<Vector3Int> cells, Vector3 position)
        {
            // Somebody on another floor is not in the way, however close they are from above.
            if (cells.Count > 0 && Grid.StoreyAt(position.y) != PlacementGrid.StoreyOfKey(cells[0].z))
            {
                return false;
            }

            foreach (var cell in cells)
            {
                var centre = Grid.CellCentre(cell);
                var dx = position.x - centre.x;
                var dz = position.z - centre.z;

                if (dx * dx + dz * dz <= agentClearance * agentClearance)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
