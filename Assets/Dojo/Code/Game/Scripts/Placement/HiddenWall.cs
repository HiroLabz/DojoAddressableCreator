using UnityEngine;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// On a wall piece that is turned off because another wall stands in its place, or because a
    /// room's entrance opens through it (<see cref="RoomWalls"/>). As in dojo-game-app.
    /// </summary>
    /// <remarks>
    /// Keeps the wall's bounds as they were when it was turned off: a renderer on a switched-off
    /// object does not report where it is, and the wall has to be measured to know whether it can
    /// come back on.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class HiddenWall : MonoBehaviour
    {
        [SerializeField] Bounds bounds;

        public Bounds Bounds
        {
            get => bounds;
            set => bounds = value;
        }
    }
}
