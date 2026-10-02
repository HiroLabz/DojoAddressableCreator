using UnityEngine;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// Marks an art prefab as an elevator: one floor's stop of a shaft that runs through every floor.
    /// </summary>
    /// <remarks>
    /// The game shows this model once per floor, at the same spot on each, and joins the stops with
    /// links only the manager may use. It carries no settings on purpose, like <c>FloorPiece</c>: a
    /// marker that can be got wrong is worse than one that cannot.
    /// <para>
    /// The doors are the children named <c>DoorLeft</c> and <c>DoorRight</c>, slid apart while the
    /// manager steps in or out. The door side is the model's -X face.
    /// </para>
    /// <para>
    /// This script exists in two projects - the game and DojoAddressableCreator - and the copies must
    /// stay identical, in an assembly named <c>Dojo.Game</c>: a bundle finds a component by assembly,
    /// namespace and class name.
    /// </para>
    /// </remarks>
    public sealed class ElevatorPiece : MonoBehaviour
    {
    }
}
