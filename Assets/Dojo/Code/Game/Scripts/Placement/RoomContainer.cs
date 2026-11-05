using System.Collections.Generic;
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
    /// <para>
    /// It also remembers what the room changed around it, as the game's does: the order rooms were
    /// placed in (the later of two neighbours turns off its shared walls), the areas it brought,
    /// and its entrances (where it opens through what it was placed against), so those walls can
    /// be worked out again when a neighbour goes (<see cref="RoomWalls"/>).
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class RoomContainer : MonoBehaviour
    {
        /// <summary>One entrance of a placed room, in world x and z.</summary>
        [System.Serializable]
        public struct Entrance
        {
            /// <summary>The middle of the doorway, in world x and z.</summary>
            public Vector2 Centre;

            /// <summary>Which way it faces out of the room: one of (±1, 0) or (0, ±1).</summary>
            public Vector2Int Facing;

            public float Width;
        }

        [Tooltip("Unique within its world. What each saved piece names to say it belongs here.")]
        [SerializeField] string id;

        [Tooltip("The room's name, as the Rooms tab lists it.")]
        [SerializeField] string roomName;

        [Tooltip("Content address of the room file it was placed from.")]
        [SerializeField] string address;

        [Tooltip("When it was placed among the world's rooms: the later of two neighbours turns off its shared walls.")]
        [SerializeField] int order;

        [SerializeField] List<int> areaIds = new List<int>();
        [SerializeField] List<Entrance> entrances = new List<Entrance>();

        public string Id => id;

        public string RoomName => roomName;

        public string Address => address;

        public int Order => order;

        /// <summary>The block areas the room brought with it: moved and deleted with it.</summary>
        public IReadOnlyList<int> AreaIds => areaIds;

        /// <summary>Where it opens through what it was placed against, in world x and z.</summary>
        public IReadOnlyList<Entrance> Entrances => entrances;

        /// <summary>Makes a room container on <paramref name="floor"/>, standing at <paramref name="position"/>.</summary>
        public static RoomContainer Create(Transform floor, string id, string roomName, string address, Vector3 position)
            => Create(floor, id, roomName, address, NextOrder(floor != null ? floor.parent : null), position);

        /// <summary>As <see cref="Create(Transform, string, string, string, Vector3)"/>, placed <paramref name="order"/>th.</summary>
        public static RoomContainer Create(Transform floor, string id, string roomName, string address, int order, Vector3 position)
        {
            var go = new GameObject("Room - " + roomName);
            go.layer = floor != null ? floor.gameObject.layer : 0;
            go.transform.SetParent(floor, false);
            go.transform.SetPositionAndRotation(position, Quaternion.identity);

            var room = go.AddComponent<RoomContainer>();
            room.id = id;
            room.roomName = roomName;
            room.address = address;
            room.order = order;

            return room;
        }

        public void SetAreas(IEnumerable<int> ids)
        {
            areaIds.Clear();

            if (ids != null)
            {
                areaIds.AddRange(ids);
            }
        }

        public void SetEntrances(IEnumerable<Entrance> doors)
        {
            entrances.Clear();

            if (doors != null)
            {
                entrances.AddRange(doors);
            }
        }

        /// <summary>The room's pieces: the ones standing, and with <paramref name="hiddenToo"/> its turned-off walls as well.</summary>
        public List<FurniturePiece> Pieces(bool hiddenToo)
        {
            var pieces = new List<FurniturePiece>();

            foreach (Transform child in transform)
            {
                var piece = child.GetComponent<FurniturePiece>();

                if (piece != null && (hiddenToo || child.gameObject.activeSelf))
                {
                    pieces.Add(piece);
                }
            }

            return pieces;
        }

        /// <summary>The room a piece belongs to, or null for a piece placed on its own.</summary>
        public static RoomContainer Of(Component piece)
        {
            if (piece == null || piece.transform.parent == null)
            {
                return null;
            }

            return piece.transform.parent.GetComponent<RoomContainer>();
        }

        /// <summary>The next placement order for a room on any floor under <paramref name="world"/>.</summary>
        public static int NextOrder(Transform world)
        {
            var next = 0;

            if (world != null)
            {
                foreach (var room in world.GetComponentsInChildren<RoomContainer>(true))
                {
                    next = Mathf.Max(next, room.order + 1);
                }
            }

            return next;
        }
    }
}
