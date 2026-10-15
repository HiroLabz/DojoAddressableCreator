using UnityEngine;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// Slides an elevator stop's two door panels apart and back together.
    /// </summary>
    /// <remarks>
    /// Added to a stop the first time somebody rides it. The panels are the model's children named
    /// <c>DoorLeft</c> and <c>DoorRight</c> (see <see cref="ElevatorPiece"/>); each slides along the
    /// model's Z axis, away from the middle, by most of its own width.
    /// </remarks>
    public sealed class ElevatorDoors : MonoBehaviour
    {
        Transform left;
        Transform right;
        Vector3 leftClosed;
        Vector3 rightClosed;
        float travel;
        bool found;

        /// <summary>The doors of this stop, made the first time they are asked for.</summary>
        public static ElevatorDoors On(Component stop)
        {
            if (stop == null)
            {
                return null;
            }

            var doors = stop.GetComponent<ElevatorDoors>();
            return doors != null ? doors : stop.gameObject.AddComponent<ElevatorDoors>();
        }

        /// <summary>0 shut, 1 fully open.</summary>
        public void SetOpen(float amount)
        {
            if (!Find())
            {
                return;
            }

            var shift = Vector3.forward * travel * Mathf.Clamp01(amount);
            left.localPosition = leftClosed + shift;
            right.localPosition = rightClosed - shift;
        }

        bool Find()
        {
            if (found)
            {
                return left != null && right != null;
            }

            found = true;

            foreach (var part in GetComponentsInChildren<Transform>(true))
            {
                if (part.name == "DoorLeft") left = part;
                if (part.name == "DoorRight") right = part;
            }

            if (left == null || right == null)
            {
                return false;
            }

            leftClosed = left.localPosition;
            rightClosed = right.localPosition;

            // Most of a panel's own width, so a sliver stays in the frame rather than vanishing.
            travel = Mathf.Abs(left.localScale.z) * 0.9f;

            return true;
        }
    }
}
