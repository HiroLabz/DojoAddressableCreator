using UnityEngine;

namespace Dojo.Game.InGame.Controllers
{
    /// <summary>
    /// How far a walk is and roughly how long it will take, answered before it starts.
    /// </summary>
    /// <remarks>
    /// Computed from a freshly calculated path rather than read off the agent, because the agent's
    /// own path is still pending on the frame the order is given. Anything that wants to plan around
    /// the journey — a camera shot, a UI hint, an animation — needs the answer at that moment, not
    /// a frame or two later.
    /// </remarks>
    public readonly struct TravelEstimate
    {
        /// <summary>Where the manager is actually heading, snapped onto the navmesh.</summary>
        public readonly Vector3 Destination;

        /// <summary>Ground covered along the path, corner to corner, in world units.</summary>
        public readonly float Distance;

        /// <summary>
        /// Distance divided by top speed, less the stopping distance. A floor rather than a
        /// promise: acceleration and slowing into turns both cost time this does not model.
        /// </summary>
        public readonly float Seconds;

        /// <summary>
        /// False when the destination cannot actually be reached and the manager will stop short.
        /// <see cref="Distance"/> and <see cref="Seconds"/> still describe how far it does get.
        /// </summary>
        public readonly bool IsComplete;

        public TravelEstimate(Vector3 destination, float distance, float seconds, bool isComplete)
        {
            Destination = destination;
            Distance = distance;
            Seconds = seconds;
            IsComplete = isComplete;
        }
    }
}
