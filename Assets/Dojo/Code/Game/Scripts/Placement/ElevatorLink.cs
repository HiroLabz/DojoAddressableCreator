using UnityEngine;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// One leg of an elevator: the link between two of its stops, and which stops they are.
    /// </summary>
    /// <remarks>
    /// Carried on the object holding the leg's NavMesh links, so whoever arrives on one can tell an
    /// elevator ride from any other link and find the cars at each end. Made and remade by
    /// <see cref="ElevatorShafts"/>; nothing else creates one.
    /// </remarks>
    public sealed class ElevatorLink : MonoBehaviour
    {
        /// <summary>The stop at the bottom of the leg.</summary>
        public FurniturePiece Lower { get; private set; }

        /// <summary>The stop at the top of the leg.</summary>
        public FurniturePiece Upper { get; private set; }

        /// <summary>The floors the two stops are on.</summary>
        public int LowerFloor { get; private set; }

        public int UpperFloor { get; private set; }

        internal void Join(FurniturePiece lower, int lowerFloor, FurniturePiece upper, int upperFloor)
        {
            Lower = lower;
            LowerFloor = lowerFloor;
            Upper = upper;
            UpperFloor = upperFloor;
        }

        /// <summary>The stop nearer this height: the one a rider is getting on or off at.</summary>
        public FurniturePiece StopAt(Vector3 point, out int floor)
        {
            var lowerY = Lower != null ? Lower.transform.position.y : float.MaxValue;
            var upperY = Upper != null ? Upper.transform.position.y : float.MaxValue;
            var nearLower = Mathf.Abs(point.y - lowerY) <= Mathf.Abs(point.y - upperY);

            floor = nearLower ? LowerFloor : UpperFloor;
            return nearLower ? Lower : Upper;
        }
    }
}
