using UnityEngine;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// Marks an art prefab as a staircase from the floor it stands on to the floor above.
    /// </summary>
    /// <remarks>
    /// The model rises exactly one floor: its foot is at its own -Z end on the floor it stands on,
    /// and its head is at its +Z end, level with the floor above. The game joins the two with a link
    /// only the manager may use. It carries no settings on purpose, like <c>FloorPiece</c>: a marker
    /// that can be got wrong is worse than one that cannot.
    /// <para>
    /// This script exists in two projects - the game and DojoAddressableCreator - and the copies must
    /// stay identical, in an assembly named <c>Dojo.Game</c>: a bundle finds a component by assembly,
    /// namespace and class name.
    /// </para>
    /// </remarks>
    public sealed class StairsPiece : MonoBehaviour
    {
    }
}
