using UnityEngine;

namespace Dojo.Game.Components
{
    /// <summary>
    /// Marks a chair as sittable and says exactly where a sitting agent's feet go. Agents find
    /// these themselves, so adding or removing chairs needs no other wiring.
    /// </summary>
    public sealed class Chair : MonoBehaviour
    {
        [Tooltip("Optional. Drop an empty GameObject here to place the seat by hand; its position " +
                 "and facing win over the values below. Leave empty to use the measured seat.")]
        [SerializeField] Transform seatAnchor;

        [Tooltip("Seat centre in the chair's local space. Default is the measured centroid of the " +
                 "seat pad mesh, which lands between the armrests.")]
        [SerializeField] Vector3 seatOffset = new Vector3(0.091f, 0.587f, 0.028f);

        /// <summary>World point where a sitting agent's feet rest.</summary>
        public Vector3 SeatPosition
            => seatAnchor != null ? seatAnchor.position : transform.TransformPoint(seatOffset);

        /// <summary>
        /// Which way someone sitting here looks. The chair model's local +X points away from the
        /// backrest, so that axis flattened onto the ground plane is the sitting direction.
        /// </summary>
        public Quaternion SeatRotation
        {
            get
            {
                var axis = seatAnchor != null ? seatAnchor.forward : transform.right;
                var forward = Vector3.ProjectOnPlane(axis, Vector3.up);
                return forward.sqrMagnitude < 0.0001f
                    ? transform.rotation
                    : Quaternion.LookRotation(forward.normalized, Vector3.up);
            }
        }

        /// <summary>True once somebody has claimed this chair, including while walking to it.</summary>
        public bool IsOccupied { get; private set; }

        /// <summary>
        /// True only while somebody is actually sitting here. Claiming the chair on the way over
        /// does not count, which is the difference that decides whether a monitor lights up.
        /// </summary>
        public bool IsInUse { get; private set; }

        /// <summary>Raised whenever <see cref="IsInUse"/> changes.</summary>
        public event System.Action<Chair> InUseChanged;

        /// <summary>Called by whoever lands on, or leaves, the seat.</summary>
        public void SetInUse(bool value)
        {
            if (IsInUse == value)
            {
                return;
            }

            IsInUse = value;
            InUseChanged?.Invoke(this);
        }

        /// <summary>Claims the chair. False when someone is already sitting here.</summary>
        public bool TryOccupy()
        {
            if (IsOccupied)
            {
                return false;
            }

            IsOccupied = true;
            return true;
        }

        public void Release()
        {
            IsOccupied = false;
            SetInUse(false);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(SeatPosition, 0.05f);
            Gizmos.DrawRay(SeatPosition, SeatRotation * Vector3.forward * 0.4f);
        }
    }
}
