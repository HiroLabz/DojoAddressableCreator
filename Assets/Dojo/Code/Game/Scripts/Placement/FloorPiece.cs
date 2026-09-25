using UnityEngine;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// Marks an art prefab as a piece of floor rather than something that stands on one.
    /// </summary>
    /// <remarks>
    /// The one thing a floor model has to declare about itself. Everything else about it is still
    /// measured like any other model — its footprint, its collider, where its middle is — but no
    /// measurement can tell a thin wide slab meant to be walked on from a thin wide slab meant to
    /// be a rug or a table top, and the difference decides four separate behaviours:
    /// <list type="bullet">
    /// <item>it holds no grid cells, so furniture can be put down on top of it;</item>
    /// <item>it never obstructs navigation, since carving a hole in the ground is nonsense;</item>
    /// <item>it counts as placeable ground, so a piece dropped on it is in bounds;</item>
    /// <item>putting one down rebuilds the navigation mesh, because it has added somewhere to walk
    /// and carving can only ever take walkable space away, never give it.</item>
    /// </list>
    /// <para>
    /// Add it to the prefab's root and nothing else is needed. It carries no settings on purpose:
    /// a marker that can be got wrong is worse than one that cannot.
    /// </para>
    /// </remarks>
    public sealed class FloorPiece : MonoBehaviour
    {
    }
}
