using System.Collections.Generic;
using Dojo.Framework.Events;
using Dojo.Game.Events;
using UnityEngine;
using VContainer;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// What placing a room needs: its footprint at any turn, whether a spot is free, a ghost to
    /// carry on the cursor, and building it for real.
    /// </summary>
    /// <remarks>
    /// The gesture itself is <see cref="PlacementController"/>'s, the same one a floor tile or a
    /// wall goes through, so a room is picked up, turned and put down exactly like them. This
    /// class is the room-shaped half that the controller does not know about.
    /// <para>
    /// A room moves in whole grid cells and turns in quarter turns about its own origin, so every
    /// piece in it stays on the cells it was laid out on. Free means not overlapping any piece or
    /// named area already on the floor. Touching is allowed, so rooms can stand wall to wall and
    /// against the building, the way floor tiles butt up to each other - and a room's walls may
    /// hang a grid cell over the floor tiles, walls and areas beside it. Near the floor, a room is
    /// pulled onto its tiles' lattice, so its own floor meets the floor beside it edge to edge.
    /// </para>
    /// </remarks>
    public sealed class RoomPlacer
    {
        /// <summary>A room read and measured, ready to be carried.</summary>
        public sealed class Plan
        {
            public RoomEntry Entry;
            public WorldSave Save;

            /// <summary>The room's extent on the ground at no turn, relative to its own origin.</summary>
            public Rect Footprint;
        }

        /// <summary>
        /// Something already on the floor, and what to call it when it is in the way.
        /// </summary>
        public readonly struct Taken
        {
            public readonly Rect Rect;
            public readonly string What;

            /// <summary>How far a room's edge may reach over it, beyond <see cref="Tolerance"/>.</summary>
            public readonly float Slack;

            public Taken(Rect rect, string what, float slack = 0f)
            {
                Rect = rect;
                What = what;
                Slack = slack;
            }
        }

        /// <summary>
        /// A floor tile's middle and its size on the ground: a room's own relative to the room's
        /// origin, the world's where it stands.
        /// </summary>
        public readonly struct Tile
        {
            public readonly Vector2 Centre;
            public readonly Vector2 Size;

            public Tile(Vector2 centre, Vector2 size)
            {
                Centre = centre;
                Size = size;
            }
        }

        /// <summary>
        /// How far a room may reach over a floor tile, an area or a wall beside it: one grid cell.
        /// A room's walls stand just outside its own tiles, so laid tile to tile against the floor
        /// next to it, they hang over that floor's edge by half a wall.
        /// </summary>
        const float Overhang = 0.25f;

        /// <summary>How close two tiles' sizes must be for one to line up with the other.</summary>
        const float SameSize = 0.05f;

        /// <summary>How many tiles away the floor may be and still pull a room into line with it.</summary>
        const int AlignReach = 2;

        /// <summary>
        /// How far two things may overlap and still count as touching. Colliders are measured, not
        /// authored to the grid, so two flush edges can disagree by a few millimetres.
        /// </summary>
        const float Tolerance = 0.05f;

        /// <summary>
        /// What a room's edge reaches past its outermost piece centre or area cell: half a wall's
        /// thickness, so the room's own walls are inside its footprint and nothing more.
        /// </summary>
        const float Margin = 0.08f;

        /// <summary>Rooms move in whole grid cells, so their pieces stay on the cells they were laid on.</summary>
        const float Snap = 0.25f;

        readonly RoomCatalog catalog;
        readonly WorldService world;
        readonly Storeys storeys;
        readonly BlockAreas blockAreas;
        readonly PlacementGrid grid;
        readonly FurniturePlacementService placement;
        readonly IObjectResolver resolver;
        readonly IEventHub hub;

        public RoomPlacer(RoomCatalog catalog, WorldService world, Storeys storeys, BlockAreas blockAreas,
            PlacementGrid grid, FurniturePlacementService placement, IObjectResolver resolver, IEventHub hub)
        {
            this.catalog = catalog;
            this.world = world;
            this.storeys = storeys;
            this.blockAreas = blockAreas;
            this.grid = grid;
            this.placement = placement;
            this.resolver = resolver;
            this.hub = hub;
        }

        /// <summary>
        /// A room already in the world, picked up whole: everything that moves with it, and where
        /// it stood so it can be put back.
        /// </summary>
        public sealed class Move
        {
            public RoomContainer Room;
            public int Floor;

            /// <summary>The container's position when it was picked up.</summary>
            public Vector3 From;

            /// <summary>The room's name and extent, measured as it stands, relative to its container.</summary>
            public Plan Plan;

            /// <summary>The room's own pieces.</summary>
            public readonly List<FurniturePiece> Pieces = new List<FurniturePiece>();

            /// <summary>
            /// Things put on the room's furniture since it was placed, which stand under the floor
            /// rather than the room, and the parent each goes back to once the room is down.
            /// </summary>
            public readonly List<KeyValuePair<FurniturePiece, Transform>> Riders
                = new List<KeyValuePair<FurniturePiece, Transform>>();

            /// <summary>The named areas lying wholly inside the room, by id, with their cells as they were.</summary>
            public readonly List<KeyValuePair<int, List<Vector3>>> Areas = new List<KeyValuePair<int, List<Vector3>>>();
        }

        /// <summary>The room read and measured, or null with <paramref name="problem"/> saying why.</summary>
        public Plan Prepare(RoomEntry room, out string problem)
        {
            var save = room != null ? catalog.Read(room) : null;

            if (save == null)
            {
                problem = "That room's file could not be read.";
                return null;
            }

            Rect footprint;

            if (!TryFootprint(save, out footprint))
            {
                problem = "That room has nothing in it to place.";
                return null;
            }

            problem = null;
            return new Plan { Entry = room, Save = save, Footprint = footprint };
        }

        /// <summary>The plan's footprint after <paramref name="steps"/> quarter turns about its origin.</summary>
        public static Rect FootprintAt(Plan plan, int steps)
        {
            var turn = Quaternion.Euler(0f, PlacementGrid.Normalise(steps) * 90f, 0f);
            var r = plan.Footprint;
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);

            foreach (var corner in new[]
            {
                new Vector3(r.xMin, 0f, r.yMin), new Vector3(r.xMax, 0f, r.yMin),
                new Vector3(r.xMin, 0f, r.yMax), new Vector3(r.xMax, 0f, r.yMax),
            })
            {
                var turned = turn * corner;
                min = Vector2.Min(min, new Vector2(turned.x, turned.z));
                max = Vector2.Max(max, new Vector2(turned.x, turned.z));
            }

            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        /// <summary>A position rounded to the grid a room moves on.</summary>
        public static Vector2 Snapped(Vector2 value)
            => new Vector2(Mathf.Round(value.x / Snap) * Snap, Mathf.Round(value.y / Snap) * Snap);

        /// <summary>
        /// The ground already used on a floor: what stands on it, and its named areas. Read once
        /// when a room is picked up, since nothing else moves while it is carried.
        /// </summary>
        public List<Taken> TakenOn(int floor)
        {
            var taken = new List<Taken>();
            var root = storeys.Floor(floor);

            if (root != null)
            {
                // Piece by piece, rooms' pieces included: a room's container measured whole would
                // stretch across the gap to any piece moved out of it since.
                var pieces = new List<Transform>();
                Storeys.AddPiecesUnder(root, pieces);

                foreach (var child in pieces)
                {
                    Bounds bounds;

                    if (TrySolidBounds(child.gameObject, out bounds))
                    {
                        taken.Add(new Taken(Rect.MinMaxRect(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z),
                            NameOf(child), MayOverhang(child) ? Overhang : 0f));
                    }
                }
            }

            foreach (var area in blockAreas.OnFloor(floor))
            {
                if (area.Cells == null || area.Cells.Count == 0)
                {
                    continue;
                }

                var half = area.CellSize * 0.5f;
                var min = new Vector2(float.MaxValue, float.MaxValue);
                var max = new Vector2(float.MinValue, float.MinValue);

                foreach (var cell in area.Cells)
                {
                    Include(cell, half, ref min, ref max);
                }

                taken.Add(new Taken(Rect.MinMaxRect(min.x, min.y, max.x, max.y), "the " + area.Name + " area", Overhang));
            }

            return taken;
        }

        /// <summary>Floor tiles and walls: what a room's walls may stand over the edge of.</summary>
        static bool MayOverhang(Transform piece)
        {
            var furniture = piece.GetComponent<FurniturePiece>();

            if (furniture != null && furniture.IsFloor)
            {
                return true;
            }

            return NameOf(piece).IndexOf("wall", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// The floor tiles directly under <paramref name="root"/> - a room's container, or the ghost
        /// of one - relative to it. Read while it stands square to the world.
        /// </summary>
        public static List<Tile> TilesOf(Transform root)
        {
            var tiles = new List<Tile>();

            if (root == null)
            {
                return tiles;
            }

            Physics.SyncTransforms();

            var origin = new Vector2(root.position.x, root.position.z);

            foreach (Transform child in root)
            {
                var piece = child.GetComponent<FurniturePiece>();
                Bounds bounds;

                if (piece != null && piece.IsFloor && TrySolidBounds(child.gameObject, out bounds))
                {
                    tiles.Add(new Tile(new Vector2(bounds.center.x, bounds.center.z) - origin,
                        new Vector2(bounds.size.x, bounds.size.z)));
                }
            }

            return tiles;
        }

        /// <summary>The floor tiles on a floor, leaving out the room being moved.</summary>
        public List<Tile> FloorTilesOn(int floor, Move moving)
        {
            var tiles = new List<Tile>();
            var root = storeys.Floor(floor);

            if (root == null)
            {
                return tiles;
            }

            var pieces = new List<Transform>();
            Storeys.AddPiecesUnder(root, pieces);

            foreach (var child in pieces)
            {
                var piece = child.GetComponent<FurniturePiece>();
                Bounds bounds;

                if (piece == null || !piece.IsFloor || (moving != null && child.parent == moving.Room.transform))
                {
                    continue;
                }

                if (TrySolidBounds(child.gameObject, out bounds))
                {
                    tiles.Add(new Tile(new Vector2(bounds.center.x, bounds.center.z), new Vector2(bounds.size.x, bounds.size.z)));
                }
            }

            return tiles;
        }

        /// <summary>
        /// <paramref name="offset"/> moved the least it takes for the room's floor tiles to line up
        /// with the floor tiles near them - on the same lattice, edge to edge - as a tile laid by
        /// hand does. Unchanged when no tile of the same size is within a couple of tiles.
        /// </summary>
        public static Vector2 AlignToTiles(List<Tile> own, List<Tile> floor, Vector2 offset, int steps)
        {
            if (own == null || floor == null || own.Count == 0 || floor.Count == 0)
            {
                return offset;
            }

            steps = PlacementGrid.Normalise(steps);
            var turn = Quaternion.Euler(0f, steps * 90f, 0f);
            var odd = (steps & 1) == 1;
            var best = Vector2.zero;
            var bestLength = float.MaxValue;

            foreach (var tile in own)
            {
                var turned = turn * new Vector3(tile.Centre.x, 0f, tile.Centre.y);
                var at = new Vector2(turned.x + offset.x, turned.z + offset.y);
                var size = odd ? new Vector2(tile.Size.y, tile.Size.x) : tile.Size;

                foreach (var other in floor)
                {
                    if (Mathf.Abs(size.x - other.Size.x) > SameSize || Mathf.Abs(size.y - other.Size.y) > SameSize
                        || other.Size.x <= 0f || other.Size.y <= 0f)
                    {
                        continue;
                    }

                    var gap = at - other.Centre;
                    var kx = Mathf.Round(gap.x / other.Size.x);
                    var kz = Mathf.Round(gap.y / other.Size.y);

                    if (Mathf.Abs(kx) > AlignReach || Mathf.Abs(kz) > AlignReach)
                    {
                        continue;
                    }

                    var delta = new Vector2(other.Centre.x + kx * other.Size.x - at.x, other.Centre.y + kz * other.Size.y - at.y);

                    if (delta.sqrMagnitude < bestLength)
                    {
                        bestLength = delta.sqrMagnitude;
                        best = delta;
                    }
                }
            }

            return bestLength < float.MaxValue ? offset + best : offset;
        }

        /// <summary>
        /// The ground already used on a floor, leaving out the room being moved: its own pieces and
        /// the areas that go with it are not in its way.
        /// </summary>
        public List<Taken> TakenOn(int floor, Move moving)
        {
            var taken = TakenOn(floor);

            if (moving == null)
            {
                return taken;
            }

            // Measured again rather than filtered by name: two pieces of the same kind, one in the
            // room and one not, would share a name.
            var own = new List<Rect>();

            foreach (Transform child in moving.Room.transform)
            {
                Bounds bounds;

                if (TrySolidBounds(child.gameObject, out bounds))
                {
                    own.Add(Rect.MinMaxRect(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z));
                }
            }

            foreach (var pair in moving.Areas)
            {
                var area = blockAreas.Find(pair.Key);

                if (area != null)
                {
                    own.Add(AreaRect(area));
                }
            }

            taken.RemoveAll(thing => own.Exists(rect => rect == thing.Rect));

            return taken;
        }

        static Rect AreaRect(BlockArea area)
        {
            var half = area.CellSize * 0.5f;
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);

            foreach (var cell in area.Cells)
            {
                Include(cell, half, ref min, ref max);
            }

            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        /// <summary>
        /// Picks up a placed room whole: its pieces give up their cells and stop obstructing, and
        /// anything standing on them is carried along. Null, with nothing lifted, when the room is
        /// empty or one of its pieces is in use.
        /// </summary>
        /// <remarks>
        /// The room's areas are the ones lying wholly inside it, worked out from where things stand
        /// rather than remembered, so a room loaded from a save takes its areas along as surely as
        /// one placed a moment ago.
        /// </remarks>
        public Move Lift(RoomContainer room, out string problem)
        {
            problem = null;

            if (room == null)
            {
                problem = "There is no room there.";
                return null;
            }

            var move = new Move
            {
                Room = room,
                Floor = Storeys.StoreyOf(room.transform),
                From = room.transform.position,
            };

            var origin = new Vector2(move.From.x, move.From.z);
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            var any = false;

            foreach (Transform child in room.transform)
            {
                var piece = child.GetComponent<FurniturePiece>();

                if (piece == null)
                {
                    continue;
                }

                if (!piece.CanLift)
                {
                    problem = "'" + room.RoomName + "' can't be moved while " + NameOf(child) + " is in use.";
                    return null;
                }

                move.Pieces.Add(piece);

                Bounds bounds;

                if (TrySolidBounds(child.gameObject, out bounds))
                {
                    min = Vector2.Min(min, new Vector2(bounds.min.x, bounds.min.z));
                    max = Vector2.Max(max, new Vector2(bounds.max.x, bounds.max.z));
                    any = true;
                }
            }

            if (!any)
            {
                problem = "'" + room.RoomName + "' has nothing in it to move.";
                return null;
            }

            // The areas inside what the pieces cover. Measured before they are added, so an area
            // painted across the room's edge is left where it is rather than dragged along.
            var covered = Rect.MinMaxRect(min.x - Tolerance, min.y - Tolerance, max.x + Tolerance, max.y + Tolerance);

            foreach (var area in blockAreas.OnFloor(move.Floor))
            {
                if (area.Cells == null || area.Cells.Count == 0)
                {
                    continue;
                }

                var inside = true;

                foreach (var cell in area.Cells)
                {
                    if (!covered.Contains(new Vector2(cell.x, cell.z)))
                    {
                        inside = false;
                        break;
                    }
                }

                if (!inside)
                {
                    continue;
                }

                move.Areas.Add(new KeyValuePair<int, List<Vector3>>(area.Id, new List<Vector3>(area.Cells)));

                var rect = AreaRect(area);
                min = Vector2.Min(min, rect.min);
                max = Vector2.Max(max, rect.max);
            }

            move.Plan = new Plan
            {
                Entry = new RoomEntry { name = room.RoomName, address = room.Address },
                Footprint = Rect.MinMaxRect(min.x - origin.x, min.y - origin.y, max.x - origin.x, max.y - origin.y),
            };

            // What has been put on the room's furniture since, standing under the floor: carried in
            // the room for the move, and handed back afterwards.
            var own = new HashSet<FurniturePiece>(move.Pieces);

            foreach (var prop in Object.FindObjectsByType<FurniturePiece>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (prop.SupportedBy == null || !own.Contains(prop.SupportedBy) || own.Contains(prop))
                {
                    continue;
                }

                move.Riders.Add(new KeyValuePair<FurniturePiece, Transform>(prop, prop.transform.parent));
                prop.transform.SetParent(room.transform, true);
            }

            foreach (var piece in move.Pieces)
            {
                placement.Release(piece);
                piece.Lift();
            }

            foreach (var pair in move.Riders)
            {
                placement.Release(pair.Key);
                pair.Key.Lift();
            }

            return move;
        }

        /// <summary>
        /// Stands the room being moved at <paramref name="offset"/>, turned <paramref name="steps"/>
        /// quarter turns about its own origin. Only how it looks: nothing is claimed until
        /// <see cref="PutDown"/>.
        /// </summary>
        public static void Carry(Move move, Vector2 offset, int steps)
        {
            move.Room.transform.SetPositionAndRotation(
                new Vector3(offset.x, move.From.y, offset.y),
                Quaternion.Euler(0f, PlacementGrid.Normalise(steps) * 90f, 0f));
        }

        /// <summary>
        /// Puts the room being moved down at <paramref name="offset"/>, turned
        /// <paramref name="steps"/> quarter turns about its origin. Put back where it came from by
        /// passing its own position and no turn, with <paramref name="announce"/> false.
        /// </summary>
        /// <remarks>
        /// The container is left square to the world, as every room's is: the turn is handed to
        /// each piece as its own, so a save writes what is on screen and a load builds it again.
        /// Every piece is registered where it lands rather than claimed - it was checked as a room.
        /// </remarks>
        public void PutDown(Move move, Vector2 offset, int steps, bool announce)
        {
            steps = PlacementGrid.Normalise(steps);
            Carry(move, offset, steps);

            var room = move.Room.transform;
            var to = room.position;
            var turn = room.rotation;

            // The turn baked into the pieces, and the container squared up again.
            var children = new List<Transform>();
            var positions = new List<Vector3>();
            var rotations = new List<Quaternion>();

            foreach (Transform child in room)
            {
                children.Add(child);
                positions.Add(child.position);
                rotations.Add(child.rotation);
            }

            room.rotation = Quaternion.identity;

            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];
                var piece = child.GetComponent<FurniturePiece>();

                child.position = positions[i];

                if (piece != null)
                {
                    piece.SetRotationSteps(piece.RotationSteps + steps);
                }
                else
                {
                    child.rotation = rotations[i];
                }
            }

            // Riders back under what they stood under before, already turned with the room.
            foreach (var pair in move.Riders)
            {
                if (pair.Key != null)
                {
                    pair.Key.transform.SetParent(pair.Value != null ? pair.Value : room.parent, true);
                }
            }

            foreach (var pair in move.Areas)
            {
                var cells = new List<Vector3>(pair.Value.Count);

                foreach (var cell in pair.Value)
                {
                    var turned = turn * new Vector3(cell.x - move.From.x, 0f, cell.z - move.From.z);
                    cells.Add(new Vector3(to.x + turned.x, cell.y, to.z + turned.z));
                }

                blockAreas.Repaint(pair.Key, cells);
            }

            // As a load does: everything standing before the floor list, then the cells held and
            // what rests on what.
            Physics.SyncTransforms();
            placement.RefreshFloors();

            foreach (var piece in move.Pieces)
            {
                if (piece != null)
                {
                    piece.Settle();
                    placement.Register(piece);
                }
            }

            foreach (var pair in move.Riders)
            {
                if (pair.Key != null)
                {
                    pair.Key.Settle();
                    placement.Register(pair.Key);
                }
            }

            placement.ResolveSupports();
            storeys.Refresh();

            if (announce && hub != null)
            {
                hub.Publish(new WorldChanged("room '" + move.Room.RoomName + "' moved"));
            }
        }

        /// <summary>
        /// Throws a room that has been lifted away for good: its pieces, what stands on its
        /// furniture, the areas that went with it, and its container. Says the world changed.
        /// </summary>
        /// <remarks>
        /// Only a room already lifted with <see cref="Lift"/>, so its pieces have given up their
        /// cells; switched off before they are destroyed, because Destroy waits for the end of the
        /// frame and the floor list is rebuilt from every active piece straight after.
        /// </remarks>
        public void Delete(Move move)
        {
            if (move == null || move.Room == null)
            {
                return;
            }

            var name = move.Room.RoomName;

            foreach (var pair in move.Areas)
            {
                blockAreas.Delete(pair.Key);
            }

            // Off its floor as well as switched off: Destroy waits for the end of the frame, and a
            // save in the meantime - the autosave the change below sets off - reads every child of
            // a floor, switched off or not, and would write the room back.
            var container = move.Room.gameObject;
            container.SetActive(false);
            container.transform.SetParent(null, true);
            Object.Destroy(container);

            // Riders were carried in the room, so they went with it; any that were not go too.
            foreach (var pair in move.Riders)
            {
                if (pair.Key != null && !pair.Key.transform.IsChildOf(container.transform))
                {
                    pair.Key.gameObject.SetActive(false);
                    pair.Key.transform.SetParent(null, true);
                    Object.Destroy(pair.Key.gameObject);
                }
            }

            Physics.SyncTransforms();
            placement.RefreshFloors();
            placement.ResolveSupports();
            storeys.Refresh();

            if (hub != null)
            {
                hub.Publish(new WorldChanged("room '" + name + "' deleted"));
            }
        }

        /// <summary>
        /// True when a footprint, already moved to where it would go, overlaps nothing taken.
        /// Otherwise <paramref name="blocker"/> names the first thing in the way.
        /// </summary>
        public static bool IsFree(Rect placed, List<Taken> taken, out string blocker)
        {
            foreach (var thing in taken)
            {
                var allowed = Tolerance + thing.Slack;
                var inner = Rect.MinMaxRect(placed.xMin + allowed, placed.yMin + allowed,
                    placed.xMax - allowed, placed.yMax - allowed);

                if (inner.Overlaps(thing.Rect))
                {
                    blocker = thing.What;
                    return false;
                }
            }

            blocker = null;
            return true;
        }

        /// <summary>
        /// The ground a piece stands on, from its colliders that are switched on and solid.
        /// </summary>
        /// <remarks>
        /// Not <see cref="FurniturePlacementService.TryColliderBounds"/>: that one takes every
        /// collider, and a switched-off collider reports a zero-sized box at the world origin, which
        /// would stretch the piece's footprint all the way back to the middle of the map.
        /// </remarks>
        static bool TrySolidBounds(GameObject root, out Bounds bounds)
        {
            bounds = new Bounds();
            var found = false;

            foreach (var collider in root.GetComponentsInChildren<Collider>(false))
            {
                if (!collider.enabled || collider.isTrigger)
                {
                    continue;
                }

                if (!found)
                {
                    bounds = collider.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }

            return found;
        }

        /// <summary>What a piece is called in a "blocked by" line: its pack name without the pack.</summary>
        static string NameOf(Transform piece)
        {
            var placed = piece.GetComponent<PlacedPiece>();
            var name = placed != null && !string.IsNullOrEmpty(placed.Resource) ? placed.Resource : piece.name;
            var slash = name.LastIndexOf('/');

            return slash >= 0 ? name.Substring(slash + 1) : name;
        }

        /// <summary>
        /// The room as it will look, built from the same prefabs through the same spawner as the
        /// real thing, on <paramref name="layer"/> and obstructing nothing. Its root is the room's
        /// origin: move and turn the root, and the room goes with it.
        /// </summary>
        /// <remarks>
        /// Not under any floor, so a save never mistakes the ghost for pieces of the world.
        /// </remarks>
        public Transform BuildGhost(Plan plan, int layer)
        {
            var root = new GameObject("RoomGhost (" + plan.Entry.name + ")").transform;

            foreach (var floor in plan.Save.floors)
            {
                if (floor.index != 0)
                {
                    continue;
                }

                foreach (var saved in floor.pieces)
                {
                    GameObject prefab;

                    if (string.IsNullOrEmpty(saved.resource) || !catalog.TryPrefab(saved.resource, out prefab))
                    {
                        continue;
                    }

                    var piece = FurnitureSpawner.Spawn(prefab, grid, saved.position, layer, int.MaxValue, root, resolver);

                    if (piece == null)
                    {
                        continue;
                    }

                    piece.SetRotationSteps(saved.rotationSteps);
                    piece.transform.localScale = saved.scale;
                    piece.Lift();
                }
            }

            return root;
        }

        /// <summary>
        /// Builds the room for real and says the world changed. Then, as the game does, asks which
        /// department the room's new areas belong to - only those the room file gave none, so a room
        /// that already carries its department (or is a conference room) is not asked about.
        /// </summary>
        public void Build(Plan plan, int floor, Vector2 offset, int steps)
        {
            var before = new HashSet<int>();
            foreach (var area in blockAreas.All)
            {
                before.Add(area.Id);
            }

            world.AddRoom(plan.Save, floor, new Vector3(offset.x, 0f, offset.y), steps, plan.Entry.name, plan.Entry.address);

            if (hub != null)
            {
                hub.Publish(new WorldChanged("room '" + plan.Entry.name + "' placed"));
            }

            var unassigned = new List<int>();
            foreach (var area in blockAreas.All)
            {
                if (!before.Contains(area.Id) && string.IsNullOrEmpty(area.Department) && !area.ConferenceRoom)
                {
                    unassigned.Add(area.Id);
                }
            }

            Dojo.Game.UI.AreaDepartmentPrompt departments;
            if (unassigned.Count > 0 && resolver != null && resolver.TryResolve(out departments))
            {
                departments.Ask("the " + plan.Entry.name, unassigned);
            }
        }

        /// <summary>
        /// The room's extent on the ground, relative to its own origin: every piece and every area
        /// cell on its floor, and a little over for the thickness of its walls.
        /// </summary>
        static bool TryFootprint(WorldSave room, out Rect footprint)
        {
            var any = false;
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);

            foreach (var floor in room.floors)
            {
                if (floor.index != 0)
                {
                    continue;
                }

                foreach (var piece in floor.pieces)
                {
                    Include(piece.position, 0f, ref min, ref max);
                    any = true;
                }

                foreach (var area in floor.blockAreas)
                {
                    if (area == null || area.area == null)
                    {
                        continue;
                    }

                    foreach (var point in area.area)
                    {
                        Include(point, area.areaCellSize * 0.5f, ref min, ref max);
                        any = true;
                    }
                }
            }

            footprint = any
                ? Rect.MinMaxRect(min.x - Margin, min.y - Margin, max.x + Margin, max.y + Margin)
                : default;

            return any;
        }

        static void Include(Vector3 point, float halfCell, ref Vector2 min, ref Vector2 max)
        {
            min = Vector2.Min(min, new Vector2(point.x - halfCell, point.z - halfCell));
            max = Vector2.Max(max, new Vector2(point.x + halfCell, point.z + halfCell));
        }
    }
}
