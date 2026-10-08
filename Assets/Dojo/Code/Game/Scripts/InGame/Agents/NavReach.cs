using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Dojo.Game.InGame.Agents
{
    /// <summary>
    /// The walking questions an agent asks before it hops: can I get close enough to that seat,
    /// and am I somewhere I can walk out of at all.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="AgentRoutine"/> so each answer can be checked against a real
    /// NavMesh in a test, rather than only in the room.
    /// <para>
    /// Every answer is a path, never a distance. The two bugs this exists for were both a distance
    /// standing in for a path: the nearest floor to a seat was taken to be somewhere the agent
    /// could stand, and it could be a pocket between a chair, a desk and a wall with no way in;
    /// and an agent that had stopped walking was taken to have arrived, and hopped to the seat
    /// from across the room.
    /// </para>
    /// </remarks>
    public static class NavReach
    {
        /// <summary>How far a probe point may snap to reach the mesh.</summary>
        const float Snap = 0.3f;

        /// <summary>Directions tried around a point. Twelve is every 30 degrees.</summary>
        const int Directions = 12;

        /// <summary>Distance across the floor, ignoring height: a seat is always above the floor.</summary>
        public static float Flat(Vector3 a, Vector3 b)
        {
            var dx = a.x - b.x;
            var dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// The walkable spot nearest <paramref name="around"/>, within <paramref name="reach"/> of it
        /// across the floor, that there is a complete path to from <paramref name="from"/>.
        /// </summary>
        /// <remarks>
        /// The nearest floor to the seat is tried first, as before - it is usually right, and when
        /// it is the hop is shortest. Then a ring of spots around the seat, which is what finds the
        /// open side of a chair whose nearest floor is a shut-in corner.
        /// </remarks>
        /// <param name="allowed">Extra test on a candidate, e.g. "inside my area". Null allows all.</param>
        public static bool TryReachableNear(
            Vector3 from,
            Vector3 around,
            float reach,
            NavMeshQueryFilter filter,
            Func<Vector3, bool> allowed,
            NavMeshPath scratch,
            out Vector3 point)
        {
            var chosen = from;
            var best = float.MaxValue;
            var found = false;

            NavMeshHit hit;

            if (NavMesh.SamplePosition(around, out hit, reach, filter))
            {
                Consider(hit.position);
            }

            for (var ring = 1; ring <= 2; ring++)
            {
                var radius = reach * ring * 0.5f;

                for (var i = 0; i < Directions; i++)
                {
                    var angle = i * Mathf.PI * 2f / Directions;
                    var probe = around + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

                    if (NavMesh.SamplePosition(probe, out hit, Snap, filter))
                    {
                        Consider(hit.position);
                    }
                }
            }

            point = chosen;
            return found;

            void Consider(Vector3 candidate)
            {
                var distance = Flat(candidate, around);

                if (distance > reach || distance >= best)
                {
                    return;
                }

                if (allowed != null && !allowed(candidate))
                {
                    return;
                }

                if (!NavMesh.CalculatePath(from, candidate, filter, scratch)
                    || scratch.status != NavMeshPathStatus.PathComplete)
                {
                    return;
                }

                best = distance;
                chosen = candidate;
                found = true;
            }
        }

        /// <summary>
        /// Whether somebody standing at <paramref name="point"/> can walk to any of
        /// <paramref name="anchors"/> - that is, is not shut away from them entirely.
        /// </summary>
        /// <remarks>
        /// Any, not most. An area split by walls into rooms leaves most of its points out of reach
        /// from any one room, and "most" called every such room a trap. The only real trap is a
        /// spot from which none of it can be reached. With no anchors there is nothing to be shut
        /// away from, so the answer is yes.
        /// </remarks>
        public static bool IsOpen(Vector3 point, IList<Vector3> anchors, NavMeshQueryFilter filter, NavMeshPath scratch)
        {
            if (anchors == null || anchors.Count == 0)
            {
                return true;
            }

            foreach (var anchor in anchors)
            {
                if (NavMesh.CalculatePath(point, anchor, filter, scratch)
                    && scratch.status == NavMeshPathStatus.PathComplete)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The nearest open spot within <paramref name="reach"/> of <paramref name="from"/>, for an
        /// agent that is shut in to hop out to.
        /// </summary>
        /// <param name="allowed">Extra test on a candidate, e.g. "inside my area". Null allows all.</param>
        public static bool TryWayOut(
            Vector3 from,
            float reach,
            IList<Vector3> anchors,
            NavMeshQueryFilter filter,
            Func<Vector3, bool> allowed,
            NavMeshPath scratch,
            out Vector3 exit)
        {
            exit = from;

            // Outwards in half-metre rings, so the first open spot found is the nearest, and the
            // hop out is as short as the corner allows.
            for (var radius = 0.5f; radius <= reach + 1e-4f; radius += 0.5f)
            {
                var best = float.MaxValue;
                var found = false;

                for (var i = 0; i < Directions; i++)
                {
                    var angle = i * Mathf.PI * 2f / Directions;
                    var probe = from + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

                    NavMeshHit hit;
                    if (!NavMesh.SamplePosition(probe, out hit, Snap, filter))
                    {
                        continue;
                    }

                    var distance = Flat(hit.position, from);

                    if (distance > reach || distance >= best)
                    {
                        continue;
                    }

                    if (allowed != null && !allowed(hit.position))
                    {
                        continue;
                    }

                    if (!IsOpen(hit.position, anchors, filter, scratch))
                    {
                        continue;
                    }

                    best = distance;
                    exit = hit.position;
                    found = true;
                }

                if (found)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
