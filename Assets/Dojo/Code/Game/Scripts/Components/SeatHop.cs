using UnityEngine;

namespace Dojo.Game.Components
{
    /// <summary>
    /// The little arcing hop between the floor and a seat. A plain class rather than a component
    /// so anything that can sit down — the manager, an agent — can drive one without inheriting
    /// from anything.
    /// </summary>
    public sealed class SeatHop
    {
        Vector3 fromPosition;
        Quaternion fromRotation;
        Vector3 toPosition;
        Quaternion toRotation;
        float startedAt;
        float duration;
        float height;

        /// <summary>True while a hop is in flight.</summary>
        public bool IsHopping { get; private set; }

        /// <summary>Where this hop is headed. Meaningless once it has finished.</summary>
        public Vector3 Destination => toPosition;

        public void Begin(Transform mover, Vector3 destination, Quaternion facing, float seconds, float arcHeight)
        {
            fromPosition = mover.position;
            fromRotation = mover.rotation;
            toPosition = destination;
            toRotation = facing;
            duration = Mathf.Max(0.01f, seconds);
            height = arcHeight;
            startedAt = Time.time;
            IsHopping = true;
        }

        /// <summary>Advances the hop. Returns true on the frame it lands.</summary>
        public bool Tick(Transform mover)
        {
            if (!IsHopping)
            {
                return false;
            }

            var k = Mathf.Clamp01((Time.time - startedAt) / duration);

            // Constant travel along the line with a parabola over it, so it reads as a hop rather
            // than a slide. The arc term is zero at both ends and peaks halfway across.
            var position = Vector3.Lerp(fromPosition, toPosition, k);
            position.y += height * 4f * k * (1f - k);
            mover.position = position;

            // Turned early so the mover lands already facing the right way.
            mover.rotation = Quaternion.Slerp(fromRotation, toRotation, Mathf.SmoothStep(0f, 1f, k));

            if (k < 1f)
            {
                return false;
            }

            IsHopping = false;
            return true;
        }

        public void Cancel() => IsHopping = false;
    }
}
