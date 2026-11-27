using System.Collections.Generic;
using Dojo.Framework.Events;
using Dojo.Game.Events;
using UnityEngine;
using UnityEngine.Rendering;
using VContainer;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// What placing a floor layout needs: its footprint at any turn, a ghost to carry on the
    /// cursor with the pieces that would be skipped hidden, and laying it for real.
    /// </summary>
    /// <remarks>
    /// <see cref="RoomPlacer"/> for floors (ClickUp 14ze67xgf0v, AC 6 and 7). A layout is laid on
    /// open ground only: where any of its floor tiles would lie on a floor tile already there, the
    /// footprint turns red and the click is refused (Jacob, 26 Nov). On open ground it is never
    /// refused: what is already there stays exactly as it is, a piece of the layout whose spot is
    /// taken is left out, and the rest is laid. The gesture is <see cref="PlacementController"/>'s,
    /// as a room's is. The measuring - what the floor has taken, its tiles' lattice - is the room
    /// placer's own, used through it.
    /// </remarks>
    public sealed class FloorLayoutPlacer
    {
        /// <summary>A layout read and measured, ready to be carried.</summary>
        public sealed class Plan
        {
            public FloorLayoutEntry Entry;
            public WorldSave Save;

            /// <summary>The layout's extent on the ground at no turn, relative to its own origin.</summary>
            public Rect Footprint;
        }

        /// <summary>What an edge reaches past the outermost piece centre: half a wall's thickness.</summary>
        const float Margin = 0.08f;

        /// <summary>How far two tiles may overlap and still count as touching, as the room placer allows.</summary>
        const float Tolerance = 0.05f;

        readonly FloorLayoutCatalog catalog;
        readonly WorldService world;
        readonly RoomPlacer rooms;
        readonly BlockAreas blockAreas;
        readonly PlacementGrid grid;
        readonly IObjectResolver resolver;
        readonly IEventHub hub;

        public FloorLayoutPlacer(FloorLayoutCatalog catalog, WorldService world, RoomPlacer rooms, BlockAreas blockAreas,
            PlacementGrid grid, IObjectResolver resolver, IEventHub hub)
        {
            this.catalog = catalog;
            this.world = world;
            this.rooms = rooms;
            this.blockAreas = blockAreas;
            this.grid = grid;
            this.resolver = resolver;
            this.hub = hub;
        }

        /// <summary>The layout read and measured, or null with <paramref name="problem"/> saying why.</summary>
        public Plan Prepare(FloorLayoutEntry layout, out string problem)
        {
            var save = layout != null && catalog != null ? catalog.Read(layout) : null;

            if (save == null)
            {
                problem = "That layout's file could not be read.";
                return null;
            }

            Rect footprint;

            if (!TryFootprint(save, out footprint))
            {
                problem = "That layout has nothing in it to place.";
                return null;
            }

            problem = null;
            return new Plan { Entry = layout, Save = save, Footprint = footprint };
        }

        /// <summary>The footprint after <paramref name="steps"/> quarter turns about its origin.</summary>
        public static Rect FootprintAt(Rect footprint, int steps)
        {
            var turn = Quaternion.Euler(0f, PlacementGrid.Normalise(steps) * 90f, 0f);
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);

            foreach (var corner in new[]
            {
                new Vector3(footprint.xMin, 0f, footprint.yMin), new Vector3(footprint.xMax, 0f, footprint.yMin),
                new Vector3(footprint.xMin, 0f, footprint.yMax), new Vector3(footprint.xMax, 0f, footprint.yMax),
            })
            {
                var turned = turn * corner;
                min = Vector2.Min(min, new Vector2(turned.x, turned.z));
                max = Vector2.Max(max, new Vector2(turned.x, turned.z));
            }

            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        /// <summary>The ground already used on a floor, as the room placer measures it.</summary>
        public List<RoomPlacer.Taken> TakenOn(int floor) => rooms.TakenOn(floor);

        /// <summary>The floor tiles on a floor, for lining the layout's own up with them.</summary>
        public List<RoomPlacer.Tile> FloorTilesOn(int floor) => rooms.FloorTilesOn(floor, null);

        /// <summary>
        /// True when any of the layout's floor tiles, turned <paramref name="steps"/> quarter turns
        /// and moved to <paramref name="offset"/>, lies on a floor tile already there. Touching edge
        /// to edge is not lying on. A floor plan is laid on open ground only.
        /// </summary>
        public static bool OverlapsFloor(List<RoomPlacer.Tile> own, List<RoomPlacer.Tile> floor, Vector2 offset, int steps)
        {
            if (own == null || floor == null || own.Count == 0 || floor.Count == 0)
            {
                return false;
            }

            steps = PlacementGrid.Normalise(steps);
            var turn = Quaternion.Euler(0f, steps * 90f, 0f);
            var odd = (steps & 1) == 1;

            foreach (var tile in own)
            {
                var turned = turn * new Vector3(tile.Centre.x, 0f, tile.Centre.y);
                var centre = new Vector2(turned.x + offset.x, turned.z + offset.y);
                var size = odd ? new Vector2(tile.Size.y, tile.Size.x) : tile.Size;
                var inner = new Rect(centre.x - size.x * 0.5f + Tolerance, centre.y - size.y * 0.5f + Tolerance,
                    size.x - 2f * Tolerance, size.y - 2f * Tolerance);

                foreach (var other in floor)
                {
                    var rect = new Rect(other.Centre.x - other.Size.x * 0.5f, other.Centre.y - other.Size.y * 0.5f,
                        other.Size.x, other.Size.y);

                    if (inner.Overlaps(rect))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// True when a piece of the plan standing on <paramref name="placed"/> would lie on
        /// something already there that it must not take over: a standing wall, furniture, or any
        /// other piece, measured to the touching tolerance alone. A floor tile holds no cells and
        /// a named area is not a piece, so a wall may reach over their edge as a room's walls do;
        /// over a standing wall or furniture it may not, by even the room rule's slack - laid
        /// there, it would claim their cells, and deleting it later would free them under the
        /// piece that was there first (Bob's review of #33).
        /// </summary>
        public static bool Overlaps(Rect placed, List<RoomPlacer.Taken> taken, out string blocker)
        {
            blocker = null;

            if (taken == null)
            {
                return false;
            }

            foreach (var thing in taken)
            {
                var reachedOver = thing.Ground || IsArea(thing);
                var allowed = RoomPlacer.Tolerance + (reachedOver ? thing.Slack : 0f);
                var inner = Rect.MinMaxRect(placed.xMin + allowed, placed.yMin + allowed,
                    placed.xMax - allowed, placed.yMax - allowed);

                if (inner.Overlaps(thing.Rect))
                {
                    blocker = thing.What;
                    return true;
                }
            }

            return false;
        }

        static bool IsArea(RoomPlacer.Taken thing)
            => !string.IsNullOrEmpty(thing.What) && thing.What.EndsWith(" area", System.StringComparison.Ordinal);

        /// <summary>
        /// True when an area cell at <paramref name="centre"/> would lie on something the layout
        /// must leave alone: a named area, or furniture. Floor tiles and walls are not in an area's
        /// way, as they are not in a room's.
        /// </summary>
        public static bool CellIsTaken(Vector2 centre, List<RoomPlacer.Taken> taken)
        {
            if (taken == null)
            {
                return false;
            }

            foreach (var thing in taken)
            {
                if (!thing.Rect.Contains(centre))
                {
                    continue;
                }

                // A floor tile or a wall may be reached over (it has slack) and is not an area.
                var isArea = !string.IsNullOrEmpty(thing.What) && thing.What.EndsWith(" area", System.StringComparison.Ordinal);

                if (isArea || thing.Slack <= 0f)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Green while the plan may be laid where it is, red where it may not: the whole ghost reads as "being placed".</summary>
        static readonly Color GhostValid = new Color(0.35f, 1f, 0.55f, 0.6f);
        static readonly Color GhostInvalid = new Color(1f, 0.33f, 0.33f, 0.6f);
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static Material ghostMaterial;

        /// <summary>
        /// One translucent unlit material every piece of the ghost wears, so the plan in hand is lit
        /// as a whole rather than looking like furniture already standing (Jacob, 26 Nov). Made the
        /// way <see cref="PlacementHighlight"/> makes its panel's.
        /// </summary>
        static Material GhostMaterial()
        {
            if (ghostMaterial != null)
            {
                return ghostMaterial;
            }

            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var mat = new Material(shader) { name = "FloorLayoutGhost" };
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.SetColor(BaseColorId, GhostValid);
            ghostMaterial = mat;
            return mat;
        }

        /// <summary>Colours the ghost green where the plan may be laid, red where it may not.</summary>
        public static void Tint(bool fits)
        {
            GhostMaterial().SetColor(BaseColorId, fits ? GhostValid : GhostInvalid);
        }

        /// <summary>
        /// The layout as it will look, built from the same prefabs through the same spawner as the
        /// real thing, on <paramref name="layer"/> and obstructing nothing, every piece in the
        /// ghost's translucent colour. Its root is the layout's origin. Not under any floor, so a
        /// save never mistakes the ghost for pieces of the world.
        /// </summary>
        public Transform BuildGhost(Plan plan, int layer)
        {
            var root = new GameObject("FloorLayoutGhost (" + plan.Entry.name + ")").transform;
            var ghost = GhostMaterial();

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

                    foreach (var renderer in piece.GetComponentsInChildren<Renderer>(true))
                    {
                        if (renderer is ParticleSystemRenderer || renderer is LineRenderer)
                        {
                            continue;
                        }

                        var materials = renderer.sharedMaterials;
                        for (var i = 0; i < materials.Length; i++)
                        {
                            materials[i] = ghost;
                        }

                        renderer.sharedMaterials = materials;
                    }
                }
            }

            return root;
        }

        /// <summary>
        /// How many of the ghost's pieces stand on a taken spot where it is now, each of them
        /// hidden so the ghost shows exactly what would be laid; the others shown again.
        /// </summary>
        public static int Clashing(Transform ghost, List<RoomPlacer.Taken> taken)
        {
            if (ghost == null)
            {
                return 0;
            }

            Physics.SyncTransforms();
            var clashing = 0;

            foreach (Transform child in ghost)
            {
                Bounds bounds;
                string blocker;
                var clash = RoomPlacer.TrySolidBounds(child.gameObject, out bounds)
                    && Overlaps(Rect.MinMaxRect(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z), taken, out blocker);

                foreach (var renderer in child.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.enabled = !clash;
                }

                if (clash)
                {
                    clashing++;
                }
            }

            return clashing;
        }

        /// <summary>
        /// Lays the layout for real, skipping the taken spots, and says the world changed. Then, as
        /// a placed room does, asks which department its new areas belong to - only those the file
        /// gave none.
        /// </summary>
        public WorldService.LayoutResult Build(Plan plan, int floor, Vector2 offset, int steps, List<RoomPlacer.Taken> taken)
        {
            var before = new HashSet<int>();
            foreach (var area in blockAreas.All)
            {
                before.Add(area.Id);
            }

            var result = world.AddLayout(plan.Save, floor, new Vector3(offset.x, 0f, offset.y), steps, plan.Entry.name, taken);

            if (hub != null)
            {
                hub.Publish(new WorldChanged("floor layout '" + plan.Entry.name + "' laid"));
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
                var names = new List<string>();
                foreach (var id in unassigned)
                {
                    var area = blockAreas.Find(id);
                    names.Add(area != null ? area.Name : string.Empty);
                }

                departments.Ask(RoomPlacer.DepartmentTitle(plan.Entry.name, names), unassigned);
            }

            return result;
        }

        /// <summary>
        /// The layout's extent on the ground, relative to its own origin: every piece and every
        /// area cell on its floor, and a little over for the thickness of its walls.
        /// </summary>
        static bool TryFootprint(WorldSave layout, out Rect footprint)
        {
            var any = false;
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);

            foreach (var floor in layout.floors)
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
