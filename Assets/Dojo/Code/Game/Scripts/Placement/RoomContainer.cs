using UnityEngine;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// One placed room: the object every piece of it stands under, on its floor.
    /// </summary>
    /// <remarks>
    /// Made when a room is placed from the Rooms tab and again when a world with rooms is loaded,
    /// so a room is one thing in the hierarchy - <c>Floor 1 / Room - Marketing / …</c> - rather than
    /// dozens of loose pieces. It is also what moves: a press on any piece of a room lifts the
    /// whole room, so it is picked up, turned and put down as one thing (see
    /// <c>PlacementController.Rooms.cs</c>). It has no collider or renderer of its own. It stands
    /// where the room was put down, square to the world, so its pieces keep the turns they were
    /// given; a turn made while moving it is handed to each piece when it is put down.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class RoomContainer : MonoBehaviour
    {
        [Tooltip("Unique within its world. What each saved piece names to say it belongs here.")]
        [SerializeField] string id;

        [Tooltip("The room's name, as the Rooms tab lists it.")]
        [SerializeField] string roomName;

        [Tooltip("Content address of the room file it was placed from.")]
        [SerializeField] string address;

        public string Id => id;

        public string RoomName => roomName;

        public string Address => address;

        /// <summary>Makes a room container on <paramref name="floor"/>, standing at <paramref name="position"/>.</summary>
        public static RoomContainer Create(Transform floor, string id, string roomName, string address, Vector3 position)
        {
            var go = new GameObject("Room - " + roomName);
            go.layer = floor != null ? floor.gameObject.layer : 0;
            go.transform.SetParent(floor, false);
            go.transform.SetPositionAndRotation(position, Quaternion.identity);

            var room = go.AddComponent<RoomContainer>();
            room.id = id;
            room.roomName = roomName;
            room.address = address;

            return room;
        }
    }
}
