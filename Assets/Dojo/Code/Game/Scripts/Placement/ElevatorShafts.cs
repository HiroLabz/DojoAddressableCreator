using System;
using System.Collections.Generic;
using Dojo.Framework.Content;
using Dojo.Framework.Events;
using Dojo.Game.Events;
using Unity.AI.Navigation;
using UnityEngine;
using VContainer;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// The world's elevators: one shaft placed once, with a stop on every floor at the same spot,
    /// and the links that let the manager ride between the stops.
    /// </summary>
    /// <remarks>
    /// <b>One shaft, many stops.</b> The player puts an elevator down on one floor; this puts a
    /// matching stop on every other floor that has ground under the whole spot, and keeps them
    /// together from then on. A floor with no tiles there gets no stop - nothing floats - and one
    /// with something standing there refuses the elevator outright (<see cref="CheckSpot"/>), as
    /// does a spot no other floor has ground under: an elevator with nowhere to go. Moving any stop
    /// moves the shaft, deleting any stop deletes it, and a floor that gains ground under it later
    /// gets its stop. A shaft is saved once, on the world (<see cref="WorldSave.connectors"/>),
    /// never as pieces on floors - its stops are rebuilt from it.
    /// <para>
    /// <b>A stop opens only when its floor has tiles at the doors.</b> Each pair of open stops one
    /// above the other is joined by a NavMesh link in the Elevator area, which only the manager
    /// walks (<see cref="NavAreas"/>), so agents never ride. <see cref="ElevatorRider"/> turns
    /// arriving on a link into a ride.
    /// </para>
    /// <para>
    /// Changes are picked up from <see cref="WorldChanged"/> a frame late, on purpose: a delete
    /// destroys at the end of the frame, and a stop still standing would read as not deleted.
    /// </para>
    /// </remarks>
    public sealed class ElevatorShafts : IDisposable
    {
        /// <summary>The connector kind an elevator is saved as.</summary>
        public const string Kind = "elevator";

        /// <summary>How far in front of the doors a stop is walked to, in metres.</summary>
        const float FrontClearance = 0.6f;

        /// <summary>How far a stair's ends reach beyond its own steps, for walking on and off.</summary>
        const float StairClearance = 0.5f;

        readonly Storeys storeys;
        readonly FurniturePlacementService placement;
        readonly PlacementGrid grid;
        readonly IContentService content;
        readonly IObjectResolver resolver;
        readonly WorldSettings settings;
        readonly WorldRoot world;
        readonly IDisposable subscription;

        readonly List<Shaft> shafts = new List<Shaft>();
        readonly List<Vector3Int> cells = new List<Vector3Int>();
        readonly Func<FurniturePiece, Vector3Int, Vector2Int, PlacementRejection> rule;

        bool building;
        bool pending;

        sealed class Shaft
        {
            public string address;
            public Vector3 position;
            public int rotationSteps;

            /// <summary>The cells a stop covers as it is turned, taken from the first one built.</summary>
            public Vector2Int footprint;

            public readonly Dictionary<int, FurniturePiece> stops = new Dictionary<int, FurniturePiece>();
        }

        /// <summary>
        /// The floor the last refused elevator was blocked on, from 0, or -1. For the message that
        /// says where to clear.
        /// </summary>
        public int LastBlockedFloor { get; private set; } = -1;

        public ElevatorShafts(
            Storeys storeys,
            FurniturePlacementService placement,
            PlacementGrid grid,
            IContentService content,
            IObjectResolver resolver,
            WorldSettings settings,
            WorldRoot world,
            IEventHub hub)
        {
            this.storeys = storeys;
            this.placement = placement;
            this.grid = grid;
            this.content = content;
            this.resolver = resolver;
            this.settings = settings;
            this.world = world;

            storeys.Changed += Reconcile;

            if (hub != null)
            {
                subscription = hub.Subscribe<WorldChanged>(_ => ReconcileNextFrame());
            }

            // Asked while an elevator is being dragged, so the footprint goes red over a spot it
            // cannot have - the same red as overlapping a desk.
            rule = CheckSpot;
            placement.AddRule(rule);
        }

        public void Dispose()
        {
            storeys.Changed -= Reconcile;

            if (placement != null)
            {
                placement.RemoveRule(rule);
            }

            if (subscription != null)
            {
                subscription.Dispose();
            }
        }

        // ── Where an elevator may go ─────────────────────────────────────────────────────────

        /// <summary>
        /// Whether an elevator may stand at <paramref name="origin"/>, given the other floors.
        /// </summary>
        /// <remarks>
        /// Floor by floor, every floor but its own: one with no ground under the whole spot simply
        /// gets no stop; one with ground but something standing there - furniture, a wall, a person
        /// - refuses it; and if no other floor has ground there at all, there is nowhere to ride to,
        /// which refuses it too. Its own floor's ground and cells are the ordinary placement rules.
        /// </remarks>
        PlacementRejection CheckSpot(FurniturePiece piece, Vector3Int origin, Vector2Int footprint)
        {
            if (!IsStop(piece))
            {
                return PlacementRejection.None;
            }

            LastBlockedFloor = -1;

            var own = PlacementGrid.StoreyOfKey(origin.z);
            var shaft = ShaftOf(piece);
            var exits = 0;

            for (var floor = 0; floor < storeys.Count; floor++)
            {
                if (floor == own)
                {
                    continue;
                }

                var there = new Vector3Int(origin.x, origin.y, PlacementGrid.KeyFor(floor, PlacementGrid.FloorLevel));

                if (!placement.IsGroundUnder(there, footprint))
                {
                    continue;
                }

                if (Blocked(there, footprint, shaft))
                {
                    LastBlockedFloor = floor;
                    return PlacementRejection.BlockedOnAnotherFloor;
                }

                exits++;
            }

            return exits > 0 ? PlacementRejection.None : PlacementRejection.NoOtherFloor;
        }

        /// <summary>
        /// Whether anything stands in the block at <paramref name="origin"/>, other than this
        /// shaft's own stops - which a stop being moved a cell or two will overlap.
        /// </summary>
        bool Blocked(Vector3Int origin, Vector2Int footprint, Shaft shaft)
        {
            grid.CellsFor(origin, footprint, cells);

            foreach (var cell in cells)
            {
                var holder = placement.HolderOf(cell);

                if (holder != null && (shaft == null || !shaft.stops.ContainsValue(holder)))
                {
                    return true;
                }
            }

            return placement.AnyoneIn(origin, footprint);
        }

        Shaft ShaftOf(FurniturePiece stop)
        {
            foreach (var shaft in shafts)
            {
                if (shaft.stops.ContainsValue(stop))
                {
                    return shaft;
                }
            }

            return null;
        }

        /// <summary>The block a shaft's stop would cover on a floor.</summary>
        Vector3Int OriginOn(Shaft shaft, int floor)
        {
            var at = new Vector3(shaft.position.x, storeys.BaseOf(floor), shaft.position.z);
            return grid.OriginFor(at, shaft.footprint, PlacementGrid.FloorLevel);
        }

        // ── A world being built ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Forgets every shaft and stops reacting until <see cref="EndBuild"/>: the world is being
        /// taken down and put up again, and every half-built state in between is not a change.
        /// </summary>
        public void BeginBuild()
        {
            building = true;
            shafts.Clear();
        }

        /// <summary>The saved shafts, with a stop built on every floor. Call between Begin and End.</summary>
        public int Restore(List<WorldSave.SavedConnector> connectors)
        {
            if (connectors == null)
            {
                return 0;
            }

            var restored = 0;

            foreach (var saved in connectors)
            {
                if (saved == null || saved.kind != Kind || string.IsNullOrEmpty(saved.address))
                {
                    continue;
                }

                var shaft = new Shaft
                {
                    address = saved.address,
                    position = new Vector3(saved.x, 0f, saved.z),
                    rotationSteps = saved.rotationSteps,
                };

                // A stop only where the floor has ground under the whole spot. The first one built
                // says how many cells a stop covers, which is what the others are checked against.
                for (var floor = 0; floor < storeys.Count; floor++)
                {
                    var stop = SpawnStop(shaft, floor);
                    if (stop == null)
                    {
                        continue;
                    }

                    shaft.footprint = stop.RotatedFootprint;

                    if (!placement.IsGroundUnder(OriginOn(shaft, floor), shaft.footprint))
                    {
                        placement.Release(stop);
                        UnityEngine.Object.DestroyImmediate(stop.gameObject);
                        continue;
                    }

                    shaft.stops[floor] = stop;
                }

                if (shaft.stops.Count == 0)
                {
                    Debug.LogWarning("[Floors] A saved elevator at (" + saved.x + ", " + saved.z + ") has no "
                        + "floor with ground under it any more, so it was left out.");
                    continue;
                }

                shafts.Add(shaft);
                restored++;
            }

            return restored;
        }

        /// <summary>Back to reacting, with the links laid for what was built.</summary>
        public void EndBuild()
        {
            building = false;
            RefreshLinks();
        }

        // ── Saving ────────────────────────────────────────────────────────────────────────────

        /// <summary>Whether a piece is one of an elevator's stops, which are saved as a shaft instead.</summary>
        public static bool IsStop(Component piece)
            => piece != null && piece.GetComponentInChildren<ElevatorPiece>(true) != null;

        /// <summary>Every shaft, as the world saves it.</summary>
        public List<WorldSave.SavedConnector> Describe()
        {
            Reconcile();

            var described = new List<WorldSave.SavedConnector>();

            foreach (var shaft in shafts)
            {
                described.Add(new WorldSave.SavedConnector
                {
                    kind = Kind,
                    address = shaft.address,
                    x = shaft.position.x,
                    z = shaft.position.z,
                    rotationSteps = shaft.rotationSteps,
                });
            }

            return described;
        }

        // ── Where floors are reached ─────────────────────────────────────────────────────────

        /// <summary>
        /// Where the manager can step onto <paramref name="floor"/> from another: in front of each
        /// elevator stop on it, and at each end of a staircase that starts or finishes on it.
        /// </summary>
        public List<Vector3> ArrivalPoints(int floor)
        {
            var points = new List<Vector3>();

            foreach (var shaft in shafts)
            {
                FurniturePiece stop;
                if (shaft.stops.TryGetValue(floor, out stop) && stop != null)
                {
                    points.Add(DoorFront(stop, floor));
                }
            }

            foreach (var child in storeys.Placed())
            {
                var stairs = child.GetComponent<FurniturePiece>();
                if (stairs == null || !stairs.IsWalkway)
                {
                    continue;
                }

                var on = Storeys.StoreyOf(stairs.transform);
                var half = stairs.Footprint.y * grid.CellSize * 0.5f + StairClearance;
                var turn = stairs.transform.rotation;

                if (on == floor)
                {
                    // The foot: on this floor, at the stairs' -Z end.
                    points.Add(stairs.transform.position + turn * new Vector3(0f, 0f, -half));
                }
                else if (on + 1 == floor)
                {
                    // The head: one floor up, at the +Z end.
                    points.Add(stairs.transform.position + turn * new Vector3(0f, 0f, half) + Vector3.up * storeys.Spacing);
                }
            }

            return points;
        }

        // ── Keeping the shafts together ──────────────────────────────────────────────────────

        async void ReconcileNextFrame()
        {
            if (pending)
            {
                return;
            }

            pending = true;

            try
            {
                await Awaitable.NextFrameAsync();
            }
            finally
            {
                pending = false;
            }

            Reconcile();
        }

        /// <summary>
        /// Brings every shaft in line with what is standing: deleted ones go, moved ones take every
        /// stop with them, new ones from the inventory become shafts, and every floor has its stop.
        /// </summary>
        void Reconcile()
        {
            if (building || world == null)
            {
                return;
            }

            var changed = false;

            // Deleted: any stop gone takes the whole shaft with it.
            for (var i = shafts.Count - 1; i >= 0; i--)
            {
                var shaft = shafts[i];
                var lost = false;

                foreach (var stop in shaft.stops.Values)
                {
                    if (stop == null)
                    {
                        lost = true;
                        break;
                    }
                }

                if (!lost)
                {
                    continue;
                }

                foreach (var stop in shaft.stops.Values)
                {
                    if (stop != null)
                    {
                        placement.Release(stop);
                        UnityEngine.Object.Destroy(stop.gameObject);
                    }
                }

                shafts.RemoveAt(i);
                changed = true;
            }

            // Moved: the stop that no longer agrees with its shaft is the one the player moved. The
            // others are taken down rather than carried along, and the fill below builds them again
            // at the new spot - only on the floors that have ground there, which need not be the
            // floors that had it at the old one.
            foreach (var shaft in shafts)
            {
                FurniturePiece moved = null;
                var movedFloor = -1;

                foreach (var pair in shaft.stops)
                {
                    if (pair.Value != null && !pair.Value.IsLifted && !Agrees(pair.Value, shaft))
                    {
                        moved = pair.Value;
                        movedFloor = pair.Key;
                        break;
                    }
                }

                if (moved == null)
                {
                    continue;
                }

                shaft.position = moved.transform.position;
                shaft.rotationSteps = moved.RotationSteps;
                shaft.footprint = moved.RotatedFootprint;

                foreach (var other in shaft.stops)
                {
                    if (other.Value != null && other.Value != moved)
                    {
                        placement.Release(other.Value);
                        UnityEngine.Object.Destroy(other.Value.gameObject);
                    }
                }

                shaft.stops.Clear();
                shaft.stops[movedFloor] = moved;
                changed = true;
            }

            // New: an elevator out of the inventory that no shaft knows yet.
            foreach (var child in storeys.Placed())
            {
                var piece = child.GetComponent<FurniturePiece>();

                if (piece == null || piece.IsLifted || !IsStop(piece) || Owned(piece))
                {
                    continue;
                }

                var placed = piece.GetComponent<PlacedPiece>();
                var shaft = new Shaft
                {
                    address = placed != null ? placed.Resource : string.Empty,
                    position = piece.transform.position,
                    rotationSteps = piece.RotationSteps,
                    footprint = piece.RotatedFootprint,
                };

                shaft.stops[Storeys.StoreyOf(piece.transform)] = piece;
                shafts.Add(shaft);
                changed = true;
            }

            // A stop on every floor with ground under the whole spot and nothing standing there; none
            // where the ground has gone, or on a floor that no longer exists.
            for (var i = shafts.Count - 1; i >= 0; i--)
            {
                var shaft = shafts[i];
                var gone = new List<int>();

                foreach (var pair in shaft.stops)
                {
                    if (pair.Key >= storeys.Count
                        || !placement.IsGroundUnder(OriginOn(shaft, pair.Key), shaft.footprint))
                    {
                        gone.Add(pair.Key);
                    }
                }

                foreach (var floor in gone)
                {
                    var stop = shaft.stops[floor];
                    if (stop != null)
                    {
                        placement.Release(stop);
                        UnityEngine.Object.Destroy(stop.gameObject);
                    }

                    shaft.stops.Remove(floor);
                    changed = true;
                }

                for (var floor = 0; floor < storeys.Count; floor++)
                {
                    FurniturePiece stop;
                    if (shaft.stops.TryGetValue(floor, out stop) && stop != null)
                    {
                        continue;
                    }

                    var origin = OriginOn(shaft, floor);

                    if (!placement.IsGroundUnder(origin, shaft.footprint) || Blocked(origin, shaft.footprint, shaft))
                    {
                        continue;
                    }

                    stop = SpawnStop(shaft, floor);
                    if (stop != null)
                    {
                        shaft.stops[floor] = stop;
                        changed = true;
                    }
                }

                // Every stop gone with its ground: nothing is left of the shaft to keep.
                if (shaft.stops.Count == 0)
                {
                    shafts.RemoveAt(i);
                }
            }

            if (changed)
            {
                storeys.Refresh();
            }

            RefreshLinks();
        }

        bool Owned(FurniturePiece piece)
        {
            foreach (var shaft in shafts)
            {
                foreach (var stop in shaft.stops.Values)
                {
                    if (stop == piece)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        static bool Agrees(FurniturePiece stop, Shaft shaft)
        {
            var at = stop.transform.position;
            var dx = at.x - shaft.position.x;
            var dz = at.z - shaft.position.z;

            return dx * dx + dz * dz < 0.0001f && stop.RotationSteps == PlacementGrid.Normalise(shaft.rotationSteps);
        }

        /// <summary>A new stop for one floor, built through the spawner like everything else.</summary>
        FurniturePiece SpawnStop(Shaft shaft, int floor)
        {
            GameObject prefab;
            if (content == null || !content.TryGet(shaft.address, out prefab) || prefab == null)
            {
                Debug.LogWarning("[Floors] The elevator '" + shaft.address + "' is not in the content, so "
                    + Storeys.LabelFor(floor) + " has no stop.");
                return null;
            }

            var layer = LayerMask.NameToLayer(settings != null ? settings.PlacedLayerName : "Default");

            var stop = FurnitureSpawner.Spawn(
                prefab, grid, Ground(shaft, floor), Mathf.Max(0, layer), placement.MaxFootprintCells,
                storeys.Floor(floor), resolver);

            if (stop == null)
            {
                return null;
            }

            var placed = stop.GetComponent<PlacedPiece>();
            if (placed != null)
            {
                placed.Set(string.Empty, shaft.address);
            }

            stop.SetRotationSteps(shaft.rotationSteps);
            stop.SizeTo(grid.CellSize);
            stop.Settle();
            placement.Register(stop);

            return stop;
        }

        /// <summary>Where a stop stands on a floor: the shaft's spot, on that floor's tiles if any.</summary>
        Vector3 Ground(Shaft shaft, int floor)
        {
            var ground = new Vector3(shaft.position.x, storeys.BaseOf(floor), shaft.position.z);

            float top;
            if (placement.TryFloorTopAt(ground, out top))
            {
                ground.y = top;
            }

            return ground;
        }

        // ── Links ────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The spot in front of a stop's doors, on its floor: where a ride starts and ends. The doors
        /// are on the model's -X face.
        /// </summary>
        public Vector3 DoorFront(FurniturePiece stop, int floor)
        {
            var half = stop.Footprint.x * grid.CellSize * 0.5f;
            var front = stop.transform.position + stop.transform.rotation * new Vector3(-(half + FrontClearance), 0f, 0f);

            front.y = storeys.BaseOf(floor);

            float top;
            if (placement.TryFloorTopAt(front, out top))
            {
                front.y = top;
            }

            return front;
        }

        /// <summary>
        /// Where to stand on <paramref name="floor"/> to ride the shaft <paramref name="stop"/> is
        /// part of: in front of its doors there. False when the shaft has no stop on that floor, or
        /// one whose doors open onto no tiles - it does not stop there.
        /// </summary>
        public bool TryDoorOn(FurniturePiece stop, int floor, out Vector3 front)
        {
            front = Vector3.zero;

            // A stop deleted since it was clicked compares equal to the null a shaft's lost stop is
            // too, and would find a shaft that is not its own.
            if (stop == null)
            {
                return false;
            }

            var shaft = ShaftOf(stop);
            FurniturePiece there;

            if (shaft == null || !shaft.stops.TryGetValue(floor, out there) || there == null || !IsOpen(there, floor))
            {
                return false;
            }

            front = DoorFront(there, floor);
            return true;
        }

        /// <summary>
        /// The elevator stop on <paramref name="floor"/> that <paramref name="position"/> is within
        /// <paramref name="reach"/> of, across the floor - at its doors or against any side. The
        /// nearest when there are several; null when none is that close.
        /// </summary>
        public FurniturePiece StopTouching(Vector3 position, int floor, float reach)
        {
            FurniturePiece nearest = null;
            var best = float.MaxValue;

            foreach (var shaft in shafts)
            {
                FurniturePiece stop;
                if (!shaft.stops.TryGetValue(floor, out stop) || stop == null)
                {
                    continue;
                }

                var cells = stop.RotatedFootprint;
                var half = new Vector2(cells.x, cells.y) * (grid.CellSize * 0.5f);
                var gap = FlatGap(position, stop.transform.position, half);

                if (gap <= reach && gap < best)
                {
                    nearest = stop;
                    best = gap;
                }
            }

            return nearest;
        }

        /// <summary>
        /// How far a point is from the outside of a block standing on the floor, across the floor:
        /// zero inside it or on its edge. <paramref name="half"/> is half its size along X and Z.
        /// </summary>
        public static float FlatGap(Vector3 point, Vector3 centre, Vector2 half)
        {
            var dx = Mathf.Max(Mathf.Abs(point.x - centre.x) - half.x, 0f);
            var dz = Mathf.Max(Mathf.Abs(point.z - centre.z) - half.y, 0f);

            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>A stop is open when there are tiles to step out onto in front of its doors.</summary>
        bool IsOpen(FurniturePiece stop, int floor)
        {
            float top;
            return placement.TryFloorTopAt(DoorFront(stop, floor), out top);
        }

        /// <summary>
        /// Joins each open stop to the next open one above it, for every size of walker the world
        /// has a walking surface for.
        /// </summary>
        /// <remarks>
        /// One link per surface rather than per character, because the manager is fitted to whichever
        /// surface suits his model and may change. Rebuilt whole every time: there are only ever a
        /// handful, and a link left pointing at a stop that has moved would be worse than none.
        /// </remarks>
        public void RefreshLinks()
        {
            var surfaces = world.Surfaces;
            var area = NavAreas.Elevator;

            foreach (var shaft in shafts)
            {
                foreach (var stop in shaft.stops.Values)
                {
                    if (stop == null)
                    {
                        continue;
                    }

                    foreach (var old in stop.GetComponentsInChildren<ElevatorLink>(true))
                    {
                        UnityEngine.Object.Destroy(old.gameObject);
                    }
                }

                FurniturePiece below = null;
                var belowFloor = -1;

                for (var floor = 0; floor < storeys.Count; floor++)
                {
                    FurniturePiece stop;
                    if (!shaft.stops.TryGetValue(floor, out stop) || stop == null || !IsOpen(stop, floor))
                    {
                        continue;
                    }

                    if (below != null)
                    {
                        Link(below, belowFloor, stop, floor, surfaces, area);
                    }

                    below = stop;
                    belowFloor = floor;
                }
            }
        }

        void Link(FurniturePiece lower, int lowerFloor, FurniturePiece upper, int upperFloor,
            NavMeshSurface[] surfaces, int area)
        {
            var holder = new GameObject("ElevatorLink " + Storeys.LabelFor(lowerFloor) + "-" + Storeys.LabelFor(upperFloor));
            holder.SetActive(false);
            holder.transform.SetParent(lower.transform, false);

            // In world space: the link's ends are absolute, whatever the stop's own turn.
            holder.transform.position = Vector3.zero;
            holder.transform.rotation = Quaternion.identity;
            holder.transform.localScale = Vector3.one;

            holder.AddComponent<ElevatorLink>().Join(lower, lowerFloor, upper, upperFloor);

            var start = DoorFront(lower, lowerFloor);
            var end = DoorFront(upper, upperFloor);

            if (surfaces != null)
            {
                foreach (var surface in surfaces)
                {
                    if (surface == null)
                    {
                        continue;
                    }

                    var link = holder.AddComponent<NavMeshLink>();
                    link.agentTypeID = surface.agentTypeID;
                    link.startPoint = start;
                    link.endPoint = end;
                    link.width = 0f;
                    link.bidirectional = true;
                    link.area = area;
                }
            }

            holder.SetActive(true);
        }
    }
}
