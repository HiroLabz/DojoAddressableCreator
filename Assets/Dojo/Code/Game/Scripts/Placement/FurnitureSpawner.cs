using UnityEngine;
using UnityEngine.AI;
using VContainer;
using VContainer.Unity;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// Builds a piece the placement system can move out of one of the art prefabs the inventory
    /// offers.
    /// </summary>
    /// <remarks>
    /// The office pack's prefabs are scenery, not furniture: not one carries a
    /// <see cref="FurniturePiece"/> or a <see cref="NavMeshObstacle"/>, they sit on the Default
    /// layer, and they are flagged fully static. Every piece already standing in the scene had
    /// exactly those things fixed by hand as scene overrides — no prefab asset has them — so a
    /// piece pulled out of the inventory has to have the same done to it at runtime. Skip it and
    /// the drop lands as scenery: unpickable, and walked straight through.
    /// <para>
    /// <b>Adding new models.</b> Drop the prefab under <c>Resources/Furniture</c> and re-run
    /// <c>Tools ▸ Dojo ▸ Addressable Generator</c>; nothing here knows anything about the
    /// office pack, so a new model is measured and fitted out like any other: its footprint comes
    /// from its own bounds, and whether it may stand on a given piece of furniture is settled at
    /// the moment of the drop by comparing the two footprints. There is no threshold to tune and
    /// nothing to classify.
    /// </para>
    /// <para>
    /// <b>When the measurement guesses wrong.</b> Add a <see cref="FurniturePiece"/> to the art
    /// prefab's own root and author the values on it. A prefab that carries one is taken exactly as
    /// authored and nothing here is guessed — that is the escape hatch for a model whose shape does
    /// not describe how it should behave: an L-shaped desk that should not block its own empty
    /// corner, or anything whose <see cref="FurniturePiece.Stacking"/> rule needs stating, since
    /// size alone says a chair fits neatly on a desk.
    /// </para>
    /// </remarks>
    public static class FurnitureSpawner
    {
        /// <summary>
        /// Shaved off the measured size before rounding up, so a piece that is exactly two cells
        /// wide does not claim three on a float hair over the boundary.
        /// </summary>
        const float CellEpsilon = 0.01f;

        /// <summary>
        /// Instantiates <paramref name="prefab"/> standing on <paramref name="groundPoint"/> and
        /// fits it out for placement. Returns null, having built nothing, when the prefab has no
        /// renderers to measure or measures larger than <paramref name="maxFootprintCells"/>.
        /// </summary>
        /// <param name="groundPoint">
        /// Where the piece stands: its middle in X and Z, and the surface its underside rests on.
        /// </param>
        /// <param name="layer">Layer the whole piece joins. The drag ghost layer, to begin with.</param>
        /// <param name="worldRoot">
        /// Parent for the finished piece, so everything the player builds lives in one branch of
        /// the scene. Null leaves it at the top level.
        /// </param>
        /// <param name="resolver">
        /// The scene's container. Prefabs are built through it rather than through
        /// <c>Object.Instantiate</c>, so a component on the art that expects injection gets it —
        /// <c>ManagerController</c> needs an <c>IEventHub</c>, and without this it spent its whole
        /// life logging that nothing would hear it. Null falls back to a plain Instantiate, which
        /// is only right for a prefab that asks for nothing.
        /// </param>
        public static FurniturePiece Spawn(
            GameObject prefab,
            PlacementGrid grid,
            Vector3 groundPoint,
            int layer,
            int maxFootprintCells,
            Transform worldRoot,
            IObjectResolver resolver = null)
        {
            if (prefab == null || grid == null)
            {
                return null;
            }

            var cellSize = grid.CellSize;

            // Authored wins outright: someone has already decided what this model is, and a
            // measurement would only second-guess them.
            var piece = prefab.GetComponent<FurniturePiece>() != null
                ? BuildAuthored(prefab, groundPoint, layer, cellSize, maxFootprintCells, resolver)
                : BuildMeasured(prefab, groundPoint, layer, cellSize, maxFootprintCells, resolver);

            if (piece == null)
            {
                return null;
            }

            if (worldRoot != null)
            {
                piece.transform.SetParent(worldRoot, true);
            }

            // A staircase is walked up, not round: baked into the walking surface below, never
            // carving. Found on the art, since a measured prefab's marker sits under the container.
            if (piece.GetComponentInChildren<StairsPiece>(true) != null)
            {
                piece.SetWalkway(true);
            }

            SetUpNavigation(piece);

            // Added here rather than by the caller so that everything built through the spawner has
            // somewhere to record what it came from, whether or not the caller bothers to fill it in.
            if (piece.GetComponent<PlacedPiece>() == null)
            {
                piece.gameObject.AddComponent<PlacedPiece>();
            }

            return piece;
        }

        /// <summary>
        /// Decides how a new piece takes part in the navigation mesh.
        /// </summary>
        /// <remarks>
        /// Furniture is kept out of the bake entirely. It blocks navigation by carving at runtime,
        /// and the two mechanisms must not both apply: a piece baked in as an obstruction would
        /// leave a permanent hole behind when it moved, because baked data never heals. That was
        /// already true of the scene's authored furniture — which is painted Not Walkable instead —
        /// and it becomes urgent the moment anything rebuilds the mesh while the game is running.
        /// <para>
        /// Ground is the opposite: it is the whole reason a rebuild happens, so it is baked in and
        /// explicitly marked walkable rather than left to inherit whatever its parent says.
        /// </para>
        /// </remarks>
        static void SetUpNavigation(FurniturePiece piece)
        {
            var modifier = piece.GetComponent<Unity.AI.Navigation.NavMeshModifier>();
            if (modifier == null)
            {
                modifier = piece.gameObject.AddComponent<Unity.AI.Navigation.NavMeshModifier>();
            }

            if (piece.IsFloor)
            {
                modifier.ignoreFromBuild = false;
                modifier.overrideArea = true;
                modifier.area = 0;   // Walkable
                return;
            }

            // Stairs are baked in like ground, but in their own area: one only the manager walks,
            // so an agent never wanders up to another floor.
            if (piece.IsWalkway)
            {
                modifier.ignoreFromBuild = false;
                modifier.overrideArea = true;
                modifier.area = NavAreas.Stairs;
                return;
            }

            modifier.ignoreFromBuild = true;
            modifier.overrideArea = false;
        }

        /// <summary>
        /// A prefab that carries its own <see cref="FurniturePiece"/>: instantiated where it is
        /// asked for and left alone but for the layer, the static flags and the obstacle size.
        /// </summary>
        static FurniturePiece BuildAuthored(
            GameObject prefab, Vector3 groundPoint, int layer, float cellSize, int maxFootprintCells,
            IObjectResolver resolver)
        {
            var instance = Build(prefab, groundPoint, resolver);
            instance.name = prefab.name;

            var piece = instance.GetComponent<FurniturePiece>();

            if (instance.GetComponentInChildren<FloorPiece>(true) != null)
            {
                piece.SetFloor(true);
            }

            if (!piece.IsFloor && piece.Footprint.x * piece.Footprint.y > maxFootprintCells)
            {
                Debug.LogError("[Placement] '" + prefab.name + "' authors a footprint of "
                    + piece.Footprint + ", over the " + maxFootprintCells + " allowed.");
                Object.Destroy(instance);
                return null;
            }

            SetLayer(instance, layer);
            ClearStaticFlags(instance);
            EnsureCollider(piece);
            piece.SizeTo(cellSize);

            return piece;
        }

        /// <summary>
        /// A plain art prefab: measured, wrapped, and fitted out from its own geometry.
        /// </summary>
        static FurniturePiece BuildMeasured(
            GameObject prefab,
            Vector3 groundPoint,
            int layer,
            float cellSize,
            int maxFootprintCells,
            IObjectResolver resolver)
        {
            // At the origin unrotated, so the measured bounds are the piece's own size rather than
            // the diagonal of a turned one.
            var art = Build(prefab, Vector3.zero, resolver);
            art.name = prefab.name;

            Bounds bounds;
            if (!TryMeasure(art, out bounds))
            {
                Debug.LogError("[Placement] '" + prefab.name + "' has no renderers, so there is no "
                    + "shape to work a footprint out from. Add a FurniturePiece to the prefab's "
                    + "root to author its placement instead.");
                Object.Destroy(art);
                return null;
            }

            var footprint = FootprintFor(bounds, cellSize);

            // Ground is exempt from the cap. The limit exists to stop a mis-scaled model reserving
            // half the map, and a floor reserves nothing at all — a floor slab is legitimately
            // larger than any piece of furniture has any business being.
            var isFloor = prefab.GetComponentInChildren<FloorPiece>(true) != null;

            if (!isFloor && footprint.x * footprint.y > maxFootprintCells)
            {
                Debug.LogError("[Placement] '" + prefab.name + "' measures " + footprint + " cells on a "
                    + cellSize + "m grid, over the " + maxFootprintCells + " allowed. Check the "
                    + "prefab's scale.");
                Object.Destroy(art);
                return null;
            }

            // The art keeps its own shape and is parented under a container that carries the
            // placement components. Placement centres a footprint on the piece's own origin, and
            // art prefabs have their pivots wherever the artist left them; putting the container's
            // origin on the middle of the art's bounds is what makes the reserved cells sit under
            // the model and a quarter turn spin it about its middle.
            var container = new GameObject(prefab.name);
            container.transform.position = groundPoint;

            art.transform.SetParent(container.transform, true);
            art.transform.position += new Vector3(
                groundPoint.x - bounds.center.x,
                groundPoint.y - bounds.min.y,
                groundPoint.z - bounds.center.z);

            SetLayer(container, layer);
            ClearStaticFlags(container);

            // RequireComponent brings the NavMeshObstacle with it, so the footprint can be sized
            // onto the obstacle below.
            var piece = container.AddComponent<FurniturePiece>();
            piece.SetFootprint(footprint);
            piece.SetFloor(isFloor);

            // Nothing decides here whether it may stand on furniture. That is a question about two
            // pieces rather than one, and FurniturePiece.CanStandOn answers it at the moment of the
            // drop by comparing footprints — so a measured piece needs no classifying at all.

            EnsureCollider(piece);
            piece.SizeTo(cellSize);

            return piece;
        }

        /// <summary>
        /// One prefab instance, built through the container when there is one.
        /// </summary>
        /// <remarks>
        /// <c>resolver.Instantiate</c> rather than <c>Object.Instantiate</c>, which is the whole
        /// difference between a spawned <c>ManagerController</c> that is handed its event hub and
        /// one that complains every frame that nothing can hear it. The same choice the inventory
        /// tiles already make for themselves — a thing built at runtime is never in the scene when
        /// the LifetimeScope enumerates it, so this is the only way it can be injected.
        /// </remarks>
        static GameObject Build(GameObject prefab, Vector3 position, IObjectResolver resolver)
            => resolver != null
                ? resolver.Instantiate(prefab, position, Quaternion.identity)
                : Object.Instantiate(prefab, position, Quaternion.identity);

        /// <summary>Puts a whole hierarchy on one layer.</summary>
        public static void SetLayer(GameObject root, int layer)
        {
            root.layer = layer;

            for (var i = 0; i < root.transform.childCount; i++)
            {
                SetLayer(root.transform.GetChild(i).gameObject, layer);
            }
        }

        /// <summary>
        /// Gives a piece a collider if its art brought none.
        /// </summary>
        /// <remarks>
        /// Every model in the office pack has one, but a new one might not, and without a collider
        /// a piece is invisible to two things that matter: the ray that picks it up again, and the
        /// probe that decides whether a mug can stand on it. Said out loud rather than done
        /// silently, because a generated box is a guess and a hand-made collider is better.
        /// </remarks>
        static void EnsureCollider(FurniturePiece piece)
        {
            if (piece.GetComponentInChildren<Collider>(true) != null)
            {
                return;
            }

            Bounds bounds;
            if (!TryMeasure(piece.gameObject, out bounds))
            {
                return;
            }

            var scale = piece.transform.lossyScale;
            var box = piece.gameObject.AddComponent<BoxCollider>();

            box.center = piece.transform.InverseTransformPoint(bounds.center);
            box.size = new Vector3(
                bounds.size.x / Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
                bounds.size.y / Mathf.Max(0.0001f, Mathf.Abs(scale.y)),
                bounds.size.z / Mathf.Max(0.0001f, Mathf.Abs(scale.z)));

            Debug.LogWarning("[Placement] '" + piece.name + "' has no collider, so a box was fitted "
                + "to its bounds. Without one it could not be picked up again or have anything "
                + "stood on it. Add a proper collider to the prefab.", piece);
        }

        /// <summary>
        /// The art prefabs are authored fully static. Runtime instances are never actually folded
        /// into a static batch, so this changes nothing on its own — it is here so the flag does
        /// not lie about a piece that plainly does move, and so the service's static-batch check
        /// keeps meaning what it says.
        /// </summary>
        static void ClearStaticFlags(GameObject root)
        {
            root.isStatic = false;

            for (var i = 0; i < root.transform.childCount; i++)
            {
                ClearStaticFlags(root.transform.GetChild(i).gameObject);
            }
        }

        /// <summary>
        /// The art's combined world bounds. Particles are left out: a plant's dust motes say
        /// nothing about how much floor the pot needs.
        /// </summary>
        static bool TryMeasure(GameObject art, out Bounds bounds)
        {
            bounds = new Bounds();
            var found = false;

            foreach (var renderer in art.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer)
                {
                    continue;
                }

                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return found;
        }

        /// <summary>
        /// How many whole cells the piece covers. Rounded up, because a piece that overhangs a cell
        /// by a centimetre still stands in it, and never below one cell.
        /// </summary>
        static Vector2Int FootprintFor(Bounds bounds, float cellSize)
        {
            var across = Mathf.CeilToInt(bounds.size.x / cellSize - CellEpsilon);
            var along = Mathf.CeilToInt(bounds.size.z / cellSize - CellEpsilon);

            return new Vector2Int(Mathf.Max(1, across), Mathf.Max(1, along));
        }
    }
}
