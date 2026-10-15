using UnityEngine;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// One floor of the world: the container everything standing on it is parented to.
    /// </summary>
    /// <remarks>
    /// World &gt; Floor &gt; 3D objects. Made and numbered by <see cref="Storeys"/>, never authored:
    /// a floor exists because a world has it, and a load builds them all again from the save.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class StoreyFloor : MonoBehaviour
    {
        [SerializeField] int index;

        /// <summary>Which floor, from 0, the ground floor.</summary>
        public int Index
        {
            get => index;
            internal set => index = value;
        }
    }
}
