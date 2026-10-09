using System;
using System.Collections.Generic;
using Dojo.Framework.Content;
using UnityEngine;

namespace Dojo.Game.Placement
{
    /// <summary>A room the Rooms tab offers: its name, and the world file it is built from.</summary>
    [Serializable]
    public sealed class RoomEntry
    {
        public string name;

        /// <summary>Content address of the room's world file: <c>&lt;pack&gt;/Rooms/room_&lt;name&gt;</c>.</summary>
        public string address;

        /// <summary>
        /// Content address of the room's thumbnail: <c>&lt;pack&gt;/Rooms/Thumbnails/room_&lt;name&gt;</c>.
        /// Rendered by <c>Tools ▸ Dojo ▸ Generate Room Thumbnails</c>.
        /// </summary>
        public string icon;
    }

    /// <summary>
    /// The rooms each loaded pack offers, and the world files they are built from.
    /// </summary>
    /// <remarks>
    /// Every pack may carry a <c>&lt;pack&gt;/Rooms/rooms</c> index naming its rooms in the order the
    /// tab lists them. An index rather than a search of the pack's addresses, because the order is
    /// a choice - the company starts at Reception - and a search can only give the alphabet.
    /// <para>
    /// A room itself is a world library file of one floor, the same shape the game saves, authored
    /// in DojoAddressableCreator under <c>Process/&lt;pack&gt;/Rooms</c>.
    /// </para>
    /// </remarks>
    public sealed class RoomCatalog
    {
        [Serializable]
        sealed class RoomIndex
        {
            public List<RoomEntry> rooms = new List<RoomEntry>();
        }

        readonly IContentService content;

        public RoomCatalog(IContentService content)
        {
            this.content = content;
        }

        /// <summary>Content address of a pack's room index.</summary>
        public static string IndexAddressFor(string pack) => pack + "/Rooms/rooms";

        /// <summary>Every room in every loaded pack, in pack order and then index order.</summary>
        public List<RoomEntry> All()
        {
            var found = new List<RoomEntry>();

            if (content == null)
            {
                return found;
            }

            foreach (var pack in content.LoadedKeys)
            {
                TextAsset asset;

                if (!content.TryGet(IndexAddressFor(pack), out asset) || asset == null)
                {
                    continue;
                }

                RoomIndex index;

                try
                {
                    index = JsonUtility.FromJson<RoomIndex>(asset.text);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[Rooms] The room index at '" + IndexAddressFor(pack)
                        + "' could not be read: " + exception.Message);
                    continue;
                }

                if (index == null || index.rooms == null)
                {
                    continue;
                }

                foreach (var room in index.rooms)
                {
                    if (room != null && !string.IsNullOrWhiteSpace(room.address))
                    {
                        found.Add(room);
                    }
                }
            }

            return found;
        }

        /// <summary>The room's thumbnail, or null when it has none yet.</summary>
        public Sprite IconOf(RoomEntry room)
        {
            Sprite sprite;

            if (room == null || content == null || string.IsNullOrEmpty(room.icon)
                || !content.TryGet(room.icon, out sprite))
            {
                return null;
            }

            return sprite;
        }

        /// <summary>A piece's prefab, by the content address a room file names it by.</summary>
        public bool TryPrefab(string address, out GameObject prefab)
        {
            prefab = null;
            return content != null && content.TryGet(address, out prefab) && prefab != null;
        }

        /// <summary>The room's world, or null when its file is missing or cannot be read.</summary>
        public WorldSave Read(RoomEntry room)
        {
            TextAsset asset;

            if (room == null || content == null || !content.TryGet(room.address, out asset) || asset == null)
            {
                return null;
            }

            WorldLibrary library;

            try
            {
                library = JsonUtility.FromJson<WorldLibrary>(asset.text);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Rooms] The room at '" + room.address + "' could not be read: "
                    + exception.Message);
                return null;
            }

            if (library == null || library.worlds == null || library.worlds.Count == 0)
            {
                return null;
            }

            var world = library.Find(library.CurrentName()) ?? library.worlds[0];

            return world != null ? world.Normalised() : null;
        }
    }
}
