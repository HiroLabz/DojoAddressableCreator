using System.Collections.Generic;
using UnityEngine;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// The scene's placement grid. Converts between world space and cell coordinates, and turns an
    /// authored footprint into the set of cells it covers at a given rotation.
    /// </summary>
    /// <remarks>
    /// Wraps a <see cref="Grid"/> set to <see cref="GridLayout.CellSwizzle.XZY"/>, which lays the
    /// cells flat on the ground plane: cell X and Y map to world X and Z. The grid's cell height is
    /// zero, so no height ever comes from here — callers keep whatever Y they already had, which is
    /// the floor the piece is standing on.
    /// <para>
    /// <b>Floors.</b> A world's floors are stacked <see cref="FloorSpacing"/> apart, and which one a
    /// point is on is its height divided by that. A cell coordinate's Z carries the floor as well as
    /// the level, so a desk on floor 2 and the one under it on floor 1 never claim the same cell —
    /// see <see cref="KeyFor"/>.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(Grid))]
    public sealed class PlacementGrid : MonoBehaviour
    {
        /// <summary>Cells on the ground. Where all real furniture stands.</summary>
        /// <remarks>
        /// A cell coordinate's Z is the level it sits at, not a height. The grid's own cell height
        /// is zero, so Z contributes nothing to world position — it exists purely to keep the
        /// things standing on a desk from competing for the same cells as the desk itself.
        /// </remarks>
        public const int FloorLevel = 0;

        /// <summary>Cells on top of a piece of furniture. Where clutter goes.</summary>
        public const int SurfaceLevel = 1;

        /// <summary>How many levels each floor has: the ground, and the tops of things.</summary>
        const int LevelsPerStorey = 2;

        /// <summary>
        /// How far below a floor's own height a point may be and still count as on that floor.
        /// </summary>
        /// <remarks>
        /// Floor tiles are authored a few centimetres into the ground and the NavMesh sits slightly
        /// below a surface, so "on floor 2" has to start a little under floor 2. The rest of the
        /// band reaches up to just under the next floor, which takes in the top of any furniture.
        /// </remarks>
        const float StoreyMargin = 0.25f;

        [Tooltip("Height between one floor and the next, in metres. Floors are stacked this far " +
                 "apart. Saved positions are relative to their floor, so changing this moves floors " +
                 "without breaking anyone's worlds.")]
        [SerializeField] float floorSpacing = 3f;

        Grid grid;

        void Awake()
        {
            grid = GetComponent<Grid>();
        }

        /// <summary>Width of one cell in world units. Cells are square.</summary>
        public float CellSize => Resolved().cellSize.x;

        /// <summary>Height between one floor and the next, in metres.</summary>
        public float FloorSpacing => floorSpacing > 0.5f ? floorSpacing : 3f;

        /// <summary>World height of a floor's own level: the grid's plane, plus one spacing per floor.</summary>
        public float StoreyBase(int storey)
            => Resolved().transform.position.y + Mathf.Max(0, storey) * FloorSpacing;

        /// <summary>Which floor a world height is on, from 0. Never negative.</summary>
        public int StoreyAt(float worldY)
        {
            var above = worldY - Resolved().transform.position.y + StoreyMargin;

            return Mathf.Max(0, Mathf.FloorToInt(above / FloorSpacing));
        }

        /// <summary>
        /// A cell coordinate's Z for one level of one floor.
        /// </summary>
        /// <remarks>
        /// Both are packed into the one integer rather than widening the key, because every caller
        /// already carries Z through untouched — the cells of a block all copy their origin's — so a
        /// floor added here reaches every claim without a single caller having to know about it.
        /// </remarks>
        public static int KeyFor(int storey, int level) => Mathf.Max(0, storey) * LevelsPerStorey + level;

        /// <summary>The level a cell's Z names: <see cref="FloorLevel"/> or <see cref="SurfaceLevel"/>.</summary>
        public static int LevelOfKey(int z) => ((z % LevelsPerStorey) + LevelsPerStorey) % LevelsPerStorey;

        /// <summary>The floor a cell's Z names, from 0.</summary>
        public static int StoreyOfKey(int z) => Mathf.Max(0, z) / LevelsPerStorey;

        /// <summary>Which cell a world point falls in. Y is ignored, so Z comes back as 0.</summary>
        public Vector3Int WorldToCell(Vector3 world) => Resolved().WorldToCell(world);

        /// <summary>
        /// Centre of one cell, at the height of the floor its Z names. Only X and Z come from the
        /// grid — its cell height is zero — so Y is that floor's own level rather than any surface.
        /// </summary>
        /// <remarks>
        /// At the floor's height rather than the grid's plane so a probe or a floor lookup made from
        /// it asks about the right floor without the caller having to put it there.
        /// </remarks>
        public Vector3 CellCentre(Vector3Int cell)
        {
            var centre = Resolved().GetCellCenterWorld(new Vector3Int(cell.x, cell.y, 0));
            centre.y = StoreyBase(StoreyOfKey(cell.z));

            return centre;
        }

        /// <summary>
        /// Footprint as it sits after <paramref name="steps"/> quarter turns. A quarter turn swaps
        /// width for depth, which is the whole reason rotation is restricted to 90 degrees: the
        /// footprint stays an integer pair instead of becoming an approximation.
        /// </summary>
        public static Vector2Int Rotated(Vector2Int footprint, int steps)
            => (Normalise(steps) & 1) == 0 ? footprint : new Vector2Int(footprint.y, footprint.x);

        /// <summary>Quarter turns folded into 0-3, so callers may pass any integer.</summary>
        public static int Normalise(int steps) => ((steps % 4) + 4) % 4;

        /// <summary>
        /// Lowest-corner cell of the block that centres <paramref name="rotatedFootprint"/> as
        /// close to <paramref name="world"/> as the grid allows.
        /// </summary>
        /// <remarks>
        /// The corner is rounded rather than floored so an even-sized footprint settles on a cell
        /// boundary and an odd one on a cell centre. Flooring instead would bias every even piece
        /// half a cell towards the origin.
        /// <para>
        /// The floor comes from <paramref name="world"/>'s height, so every caller - a piece's own
        /// position, the cursor, a painted cell - lands on the floor it is actually on.
        /// </para>
        /// </remarks>
        public Vector3Int OriginFor(Vector3 world, Vector2Int rotatedFootprint, int level = FloorLevel)
        {
            var cell = CellSize;
            var local = Resolved().transform.InverseTransformPoint(world);
            var minX = local.x - rotatedFootprint.x * cell * 0.5f;
            var minZ = local.z - rotatedFootprint.y * cell * 0.5f;

            return new Vector3Int(
                Mathf.RoundToInt(minX / cell),
                Mathf.RoundToInt(minZ / cell),
                KeyFor(StoreyAt(world.y), level));
        }

        /// <summary>
        /// World centre of the block starting at <paramref name="origin"/>. This is where the piece
        /// itself goes; the returned Y is its floor's own level, so callers substitute the surface
        /// height.
        /// </summary>
        public Vector3 CentreOf(Vector3Int origin, Vector2Int rotatedFootprint)
        {
            var cell = CellSize;
            var localMin = new Vector3(origin.x * cell, 0f, origin.y * cell);
            var localCentre = localMin
                + new Vector3(rotatedFootprint.x * cell * 0.5f, 0f, rotatedFootprint.y * cell * 0.5f);

            var centre = Resolved().transform.TransformPoint(localCentre);
            centre.y = StoreyBase(StoreyOfKey(origin.z));

            return centre;
        }

        /// <summary>
        /// Fills <paramref name="into"/> with every cell the block covers. Reuses the caller's list
        /// so dragging does not allocate a new one every frame.
        /// </summary>
        /// <remarks>
        /// The origin's Z carries the level through to every cell, so a mug on a desk and the desk
        /// under it produce two sets of keys that never collide.
        /// </remarks>
        public void CellsFor(Vector3Int origin, Vector2Int rotatedFootprint, List<Vector3Int> into)
        {
            into.Clear();

            for (var x = 0; x < rotatedFootprint.x; x++)
            {
                for (var y = 0; y < rotatedFootprint.y; y++)
                {
                    into.Add(new Vector3Int(origin.x + x, origin.y + y, origin.z));
                }
            }
        }

        /// <summary>
        /// The grid, resolved on demand. Editor gizmos and the placement service both read cell
        /// maths before Awake has run, so the cached reference cannot be relied on alone.
        /// </summary>
        Grid Resolved() => grid != null ? grid : (grid = GetComponent<Grid>());
    }
}
