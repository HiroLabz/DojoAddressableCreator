using Unity.AI.Navigation;
using UnityEngine;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// The object everything the player builds lives under, and the navigation surfaces that world
    /// bakes into.
    /// </summary>
    /// <remarks>
    /// A component with no behaviour, and the only part of world saving that has any business being
    /// one: a <see cref="Transform"/> exists in a scene and nowhere else, so something in the scene
    /// has to point at it. Registered with the scene's LifetimeScope, which is what lets
    /// <see cref="WorldService"/> and <see cref="PlacementController"/> be handed the same root
    /// rather than each running <c>GameObject.Find("DynamicWorld")</c> and hoping nobody renames
    /// the object.
    /// <para>
    /// A missing one is a resolve failure at scene load rather than a null check on every save, so
    /// a scene wired wrongly says so before the player has built anything to lose.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class WorldRoot : MonoBehaviour
    {
        NavMeshSurface[] surfaces;

        /// <summary>The transform built pieces are parented to, and the only branch that is saved.</summary>
        /// <remarks>
        /// Everything else in the scene — the camera, the UI, the agents, the placement grid — is
        /// scenery that exists whether or not anybody has built anything, and a save file that
        /// carried it would be a save file that could overwrite the game.
        /// </remarks>
        public Transform Root => transform;

        /// <summary>
        /// The navigation surfaces this world builds into.
        /// </summary>
        /// <remarks>
        /// The world's own surfaces come first, and if it has any, nothing else in the scene is
        /// considered. A surface parented to this root and set to collect its children can only
        /// ever see what the player has built — which is the whole point of building in one branch
        /// — so a stale surface left over from an authored environment cannot quietly get mixed in
        /// with it.
        /// <para>
        /// Collected once and kept. The surfaces are components on this object and its children,
        /// which do not come and go; the pieces underneath them do, and a surface does not care how
        /// many there are until it is asked to build.
        /// </para>
        /// </remarks>
        public NavMeshSurface[] Surfaces => surfaces ?? (surfaces = Collect());

        /// <summary>
        /// Forgets the surfaces collected so far, so the next ask finds any added since.
        /// </summary>
        /// <remarks>
        /// Surfaces do come and go after all: <see cref="ClearanceRegistry"/> adds one the first
        /// time a model of a new size appears.
        /// </remarks>
        public void Refresh()
        {
            surfaces = null;
        }

        NavMeshSurface[] Collect()
        {
            var own = GetComponentsInChildren<NavMeshSurface>(true);

            if (own.Length > 0)
            {
                return own;
            }

            // Nothing under the root, so fall back to the scene. Inactive ones are gathered on this
            // path only, so that a scene whose only surfaces are switched off produces an
            // explanation from the baker rather than silence.
            return FindObjectsByType<NavMeshSurface>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }
    }
}
