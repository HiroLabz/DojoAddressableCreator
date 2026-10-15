using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// Gives each character a walkable floor built for the size of the model it is wearing.
    /// </summary>
    /// <remarks>
    /// How close a character may come to a wall is not a property of the character. It is built
    /// into the walkable floor itself, one floor per size, and a <see cref="NavMeshAgent"/> can only
    /// walk the floor built for its agent type. The owner and the agents used to have one fixed
    /// type each - 0.3m and 0.2m - and a robot 0.9m across sank a quarter of a metre into every
    /// wall it walked past.
    /// <para>
    /// So the size is measured from the model, never written down anywhere: the widest side of the
    /// part of it that walks, halved - see <see cref="WalkingShare"/> for why a head and swinging
    /// arms are left out. That is matched to the smallest step on the project's clearance ladder - the
    /// <c>Clearance 0.15</c> to <c>Clearance 0.75</c> agent types, every 5cm - and the floor for
    /// that step is built the first time anything that size appears. A different model means a
    /// different measurement and, if it needs one, a different floor; a size nothing wears is
    /// never built at all.
    /// </para>
    /// <para>
    /// A ladder of project agent types rather than types made up at runtime, because a size made
    /// up at runtime cannot be given its radius: Unity keeps agent sizes in the project settings,
    /// and a floor built for a type reads its radius from there. Adding a step to the ladder is
    /// adding an agent type with this prefix - nothing in code lists them.
    /// </para>
    /// </remarks>
    public sealed class ClearanceRegistry
    {
        /// <summary>The name every step on the ladder starts with.</summary>
        public const string TypePrefix = "Clearance ";

        const string LogPrefix = "[Nav]";

        readonly WorldRoot world;

        public ClearanceRegistry(WorldRoot world)
        {
            this.world = world;
        }

        /// <summary>
        /// How much of the body's height, from the floor up, decides its clearance.
        /// </summary>
        /// <remarks>
        /// The part that walks. Legs, a body and arms hanging at the sides reach down into it; a
        /// head, and hands mid-swing, are held above it and may overhang a wall's edge for a
        /// moment, as they do in most games. The owner measured whole was 1.7m across - his swinging
        /// hands - and could barely go anywhere; the robot agent, which is one block from the floor
        /// up, measures the same either way.
        /// </remarks>
        public const float WalkingShare = 0.4f;

        /// <summary>
        /// Half the widest side of the body that walks, across the floor, measured from the body's
        /// own pivot. Zero when it has nothing to measure.
        /// </summary>
        /// <remarks>
        /// Every part that reaches down into the lowest <see cref="WalkingShare"/> of the body's
        /// height counts, at its full width; a part wholly above that does not. Whole parts rather
        /// than slices of them, because a part is one rigid thing - an arm that hangs down to the
        /// hip is as wide at the shoulder.
        /// <para>
        /// In the body's own frame, so the answer does not depend on which way it happens to be
        /// facing: a world-space box around a model turned at an angle is wider than the model.
        /// From the pivot because that is where the NavMeshAgent stands - a model hung off-centre
        /// reaches further on one side, and that side is the one that meets the wall.
        /// </para>
        /// </remarks>
        public static float MeasureRadius(Transform body)
        {
            if (body == null)
            {
                return 0f;
            }

            var unturn = Quaternion.Inverse(body.rotation);
            var parts = new System.Collections.Generic.List<Vector4>();   // x = reach, y = lowest, z = highest
            var bottom = float.MaxValue;
            var top = float.MinValue;

            foreach (var renderer in body.GetComponentsInChildren<Renderer>())
            {
                // Effects, trails and lines are not the body.
                if (renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer)
                {
                    continue;
                }

                var local = renderer.localBounds;
                var min = local.min;
                var max = local.max;

                var reach = 0f;
                var lowest = float.MaxValue;
                var highest = float.MinValue;

                for (var corner = 0; corner < 8; corner++)
                {
                    var point = new Vector3(
                        (corner & 1) == 0 ? min.x : max.x,
                        (corner & 2) == 0 ? min.y : max.y,
                        (corner & 4) == 0 ? min.z : max.z);

                    var offset = unturn * (renderer.transform.TransformPoint(point) - body.position);

                    reach = Mathf.Max(reach, Mathf.Abs(offset.x), Mathf.Abs(offset.z));
                    lowest = Mathf.Min(lowest, offset.y);
                    highest = Mathf.Max(highest, offset.y);
                }

                parts.Add(new Vector4(reach, lowest, highest, 0f));
                bottom = Mathf.Min(bottom, lowest);
                top = Mathf.Max(top, highest);
            }

            if (parts.Count == 0)
            {
                return 0f;
            }

            // The floor is at the pivot; a model sunk slightly below it starts where it starts.
            var floor = Mathf.Min(bottom, 0f);
            var bandTop = floor + (top - floor) * WalkingShare;

            var widest = 0f;

            foreach (var part in parts)
            {
                if (part.y < bandTop)
                {
                    widest = Mathf.Max(widest, part.x);
                }
            }

            return widest;
        }

        /// <summary>
        /// The smallest step on the clearance ladder at least <paramref name="radius"/> wide.
        /// </summary>
        /// <remarks>
        /// A model wider than the top step gets the top step and a warning - it will reach into
        /// walls, and the fix is another step on the ladder, not a silent failure to walk.
        /// </remarks>
        public static bool TryPickAgentType(float radius, out int agentTypeID, out float typeRadius)
        {
            agentTypeID = -1;
            typeRadius = 0f;

            var bestFit = float.MaxValue;
            var largest = -1f;
            var largestId = -1;

            for (var i = 0; i < NavMesh.GetSettingsCount(); i++)
            {
                var settings = NavMesh.GetSettingsByIndex(i);
                var name = NavMesh.GetSettingsNameFromID(settings.agentTypeID);

                if (name == null || !name.StartsWith(TypePrefix))
                {
                    continue;
                }

                if (settings.agentRadius > largest)
                {
                    largest = settings.agentRadius;
                    largestId = settings.agentTypeID;
                }

                // A hair of slack, so a model measuring 0.4500001 is not pushed up a whole step.
                if (settings.agentRadius + 1e-3f >= radius && settings.agentRadius < bestFit)
                {
                    bestFit = settings.agentRadius;
                    agentTypeID = settings.agentTypeID;
                }
            }

            if (agentTypeID != -1)
            {
                typeRadius = bestFit;
                return true;
            }

            if (largestId == -1)
            {
                Debug.LogError(LogPrefix + " The project has no '" + TypePrefix + "…' agent types, so "
                    + "nothing can be given a walkable floor to its size. Add them under "
                    + "Navigation > Agents.");
                return false;
            }

            Debug.LogWarning(LogPrefix + " A model " + (radius * 2f).ToString("F2") + "m across is wider "
                + "than the largest clearance step (" + (largest * 2f).ToString("F2") + "m), so it will "
                + "reach into walls. Add a bigger '" + TypePrefix + "…' agent type.");

            agentTypeID = largestId;
            typeRadius = largest;
            return true;
        }

        /// <summary>
        /// The world's walkable floor for <paramref name="agentTypeID"/>, built on the spot if the
        /// world does not have one yet.
        /// </summary>
        /// <remarks>
        /// A new floor copies the settings of one the world already has - what it collects, which
        /// layers, how big its tiles are - so it sees exactly the same pieces and differs only in
        /// how far it keeps from them. Its voxels are a third of the clearance, the size Unity
        /// recommends: finer is slower to build and buys nothing, coarser rounds the clearance off.
        /// </remarks>
        public NavMeshSurface SurfaceFor(int agentTypeID)
        {
            if (world == null)
            {
                return null;
            }

            NavMeshSurface template = null;

            foreach (var surface in world.Surfaces)
            {
                if (surface == null)
                {
                    continue;
                }

                if (surface.agentTypeID == agentTypeID)
                {
                    return surface;
                }

                if (template == null)
                {
                    template = surface;
                }
            }

            var radius = NavMesh.GetSettingsByID(agentTypeID).agentRadius;
            var built = world.gameObject.AddComponent<NavMeshSurface>();

            built.agentTypeID = agentTypeID;
            built.collectObjects = template != null ? template.collectObjects : CollectObjects.Children;
            built.useGeometry = template != null ? template.useGeometry : NavMeshCollectGeometry.RenderMeshes;
            built.layerMask = template != null ? template.layerMask : (LayerMask)~0;
            built.defaultArea = template != null ? template.defaultArea : 0;
            built.ignoreNavMeshAgent = true;
            built.ignoreNavMeshObstacle = true;
            built.overrideTileSize = template != null && template.overrideTileSize;
            built.tileSize = template != null ? template.tileSize : 256;
            built.minRegionArea = template != null ? template.minRegionArea : 2f;
            built.overrideVoxelSize = true;
            built.voxelSize = Mathf.Max(0.02f, radius / 3f);

            if (template != null && template.collectObjects == CollectObjects.Volume)
            {
                built.center = template.center;
                built.size = template.size;
            }

            built.BuildNavMesh();

            // The world keeps its list of surfaces; this one has to be in it, or the next floor laid
            // would rebuild every floor but this one and leave this size walking the old layout.
            world.Refresh();

            Debug.Log(LogPrefix + " Built a walkable floor for "
                + NavMesh.GetSettingsNameFromID(agentTypeID) + " (" + (radius * 2f).ToString("F2")
                + "m across) - the first thing that size in this world.", world);

            return built;
        }

        /// <summary>
        /// Measures the model on <paramref name="agent"/> and puts it on the walkable floor for that
        /// size. Call it whenever the model changes; it does nothing when the size is the same.
        /// </summary>
        /// <remarks>
        /// The agent is switched off for the change of type and back on after it. An enabled agent
        /// changing type is taken off one floor and dropped onto the other wherever Unity decides;
        /// this puts it back exactly where it stood.
        /// </remarks>
        public bool Fit(NavMeshAgent agent)
        {
            if (agent == null)
            {
                return false;
            }

            var measured = MeasureRadius(agent.transform);

            if (measured <= 0f)
            {
                Debug.LogWarning(LogPrefix + " '" + agent.name + "' has nothing drawn to measure, so it "
                    + "keeps the walkable floor it has.", agent);
                return false;
            }

            int id;
            float radius;
            if (!TryPickAgentType(measured, out id, out radius))
            {
                return false;
            }

            SurfaceFor(id);

            if (agent.agentTypeID != id)
            {
                var wasOn = agent.enabled;
                var standing = agent.transform.position;

                agent.enabled = false;
                agent.agentTypeID = id;
                agent.enabled = wasOn;

                if (wasOn && agent.isOnNavMesh)
                {
                    agent.Warp(standing);
                }
            }

            // Avoidance too, so two characters keep apart by the same size they keep from walls.
            agent.radius = radius;

            return true;
        }
    }
}
