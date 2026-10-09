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
    /// against the building, the way floor tiles butt up to each other.
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

            public Taken(Rect rect, string what)
            {
                Rect = rect;
                What = what;
            }
        }

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
        readonly IObjectResolver resolver;
        readonly IEventHub hub;

        public RoomPlacer(RoomCatalog catalog, WorldService world, Storeys storeys, BlockAreas blockAreas,
            PlacementGrid grid, IObjectResolver resolver, IEventHub hub)
        {
            this.catalog = catalog;
            this.world = world;
            this.storeys = storeys;
            this.blockAreas = blockAreas;
            this.grid = grid;
            this.resolver = resolver;
            this.hub = hub;
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
                foreach (Transform child in root)
                {
                    Bounds bounds;

                    if (TrySolidBounds(child.gameObject, out bounds))
                    {
                        taken.Add(new Taken(Rect.MinMaxRect(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z),
                            NameOf(child)));
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

                taken.Add(new Taken(Rect.MinMaxRect(min.x, min.y, max.x, max.y), "the " + area.Name + " area"));
            }

            return taken;
        }

        /// <summary>
        /// True when a footprint, already moved to where it would go, overlaps nothing taken.
        /// Otherwise <paramref name="blocker"/> names the first thing in the way.
        /// </summary>
        public static bool IsFree(Rect placed, List<Taken> taken, out string blocker)
        {
            var inner = Rect.MinMaxRect(placed.xMin + Tolerance, placed.yMin + Tolerance,
                placed.xMax - Tolerance, placed.yMax - Tolerance);

            foreach (var thing in taken)
            {
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

        /// <summary>Builds the room for real and says the world changed.</summary>
        public void Build(Plan plan, int floor, Vector2 offset, int steps)
        {
            world.AddRoom(plan.Save, floor, new Vector3(offset.x, 0f, offset.y), steps, plan.Entry.name);

            if (hub != null)
            {
                hub.Publish(new WorldChanged("room '" + plan.Entry.name + "' placed"));
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
