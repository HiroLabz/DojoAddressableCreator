using UnityEngine;
using UnityEngine.AI;

namespace Dojo.Game.InGame.Agents
{
    /// <summary>
    /// Sizes the <see cref="NavMeshAgent"/> and the pick collider from whatever model is actually
    /// under this object, at runtime, rather than from numbers typed into the prefab.
    /// </summary>
    /// <remarks>
    /// The model is the one part of an agent certain to be replaced — a capsule became a robot,
    /// and a robot will become something else. Every time that happens, a radius and a height
    /// authored by hand go stale silently: navigation keeps reserving the old footprint while the
    /// new body sticks out of it, and the agent walks through desks with nothing logged anywhere.
    /// Measuring on <c>Awake</c> makes the body the single source of truth and the swap free.
    /// <para>
    /// <b>Bind-pose bounds, not renderer bounds.</b> <see cref="SkinnedMeshRenderer.bounds"/> is
    /// padded so that animation never clips outside it — for this robot it reports 1.32 against a
    /// true 1.20, a tenth too big. The mesh's own <see cref="Mesh.bounds"/> is the geometry, so
    /// that is what gets measured, pushed through each renderer's transform into this object's
    /// space so a Z-up import or a scaled child is accounted for without special cases.
    /// </para>
    /// <para>
    /// The radius comes from the <em>widest</em> horizontal extent rather than the narrowest. A
    /// radius cut to the narrow axis leaves the shoulders outside what navigation reserved, which
    /// is exactly how an agent ends up penetrating the furniture it was meant to walk around.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class AgentNavFit : MonoBehaviour
    {
        [Tooltip("Also resize the BoxCollider to the measured body. Off leaves the authored " +
                 "collider alone, for a model whose pick target should not be its full extent.")]
        [SerializeField] bool fitCollider = true;

        [Tooltip("Added to the measured radius. A little slack keeps an agent from grazing the " +
                 "furniture it rounds; too much stops it fitting through its own doorways.")]
        [SerializeField] float radiusPadding;

        void Awake()
        {
            Fit();
        }

        /// <summary>Measures the body and applies it. Safe to call again after swapping models.</summary>
        public void Fit()
        {
            Bounds local;
            if (!TryMeasure(out local))
            {
                Debug.LogWarning("[Agent] Nothing to measure under '" + name + "', so the "
                    + "NavMeshAgent keeps whatever was authored on the prefab.", this);
                return;
            }

            var agent = GetComponent<NavMeshAgent>();
            agent.height = local.size.y;
            agent.radius = Mathf.Max(local.size.x, local.size.z) * 0.5f + radiusPadding;

            // The model stands on the pivot, so the agent needs no lift off the mesh. Authoring a
            // base offset as well would raise it by its own feet.
            agent.baseOffset = 0f;

            if (!fitCollider)
            {
                return;
            }

            var box = GetComponent<BoxCollider>();
            if (box != null)
            {
                box.size = local.size;
                box.center = local.center;
            }
        }

        /// <summary>The body's box in this object's own space, or false when there is no geometry.</summary>
        bool TryMeasure(out Bounds result)
        {
            result = new Bounds();
            var found = false;
            var toLocal = transform.worldToLocalMatrix;

            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                var mesh = MeshOf(renderer);
                if (mesh == null)
                {
                    continue;
                }

                // The mesh's eight corners, each carried through the renderer's own transform, so
                // rotation and scale anywhere in the chain land in the right place.
                var matrix = toLocal * renderer.transform.localToWorldMatrix;
                var centre = mesh.bounds.center;
                var extents = mesh.bounds.extents;

                for (var corner = 0; corner < 8; corner++)
                {
                    var offset = new Vector3(
                        (corner & 1) == 0 ? -extents.x : extents.x,
                        (corner & 2) == 0 ? -extents.y : extents.y,
                        (corner & 4) == 0 ? -extents.z : extents.z);

                    var point = matrix.MultiplyPoint3x4(centre + offset);

                    if (!found)
                    {
                        result = new Bounds(point, Vector3.zero);
                        found = true;
                    }
                    else
                    {
                        result.Encapsulate(point);
                    }
                }
            }

            return found;
        }

        static Mesh MeshOf(Renderer renderer)
        {
            var skinned = renderer as SkinnedMeshRenderer;
            if (skinned != null)
            {
                return skinned.sharedMesh;
            }

            var filter = renderer.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }
    }
}
