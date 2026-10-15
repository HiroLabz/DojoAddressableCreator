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

        /// <summary>
        /// Address of the room's world file: <c>&lt;pack&gt;/Rooms/default_room_&lt;name&gt;</c> for
        /// the pack's rooms, <c>custom/room_&lt;name&gt;</c> for the ones the player has made.
        /// </summary>
        public string address;

        /// <summary>
        /// Content address of the room's thumbnail: <c>&lt;pack&gt;/Rooms/Thumbnails/room_&lt;name&gt;</c>.
        /// Rendered by <c>Tools ▸ Dojo ▸ Generate Room Thumbnails</c>.
        /// </summary>
        public string icon;

        /// <summary>
        /// What kind of pack room it is: <see cref="DefaultTag"/> for the pack's original rooms,
        /// <see cref="PromotedTag"/> for a room made in the room editor and promoted into the pack.
        /// Which of them RESET WORLD keeps depends on it.
        /// </summary>
        public string tag;

        /// <summary>One of the pack's original rooms: never deleted by anything.</summary>
        public const string DefaultTag = "default";

        /// <summary>A room made in the room editor and promoted into the pack.</summary>
        public const string PromotedTag = "promoted";
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
        readonly CustomRooms custom;

        public RoomCatalog(IContentService content, CustomRooms custom)
        {
            this.content = content;
            this.custom = custom;
        }

        /// <summary>The rooms the player has made, kept on disk.</summary>
        public CustomRooms Custom => custom;

        /// <summary>The pack whose room list the player's saved rooms are added to.</summary>
        const string DefaultPack = "default";

        /// <summary>Content address of a pack's room index.</summary>
        public static string IndexAddressFor(string pack) => pack + "/Rooms/rooms";

        /// <summary>Every room in every loaded pack, in pack order and then index order.</summary>
        public List<RoomEntry> All()
        {
            var found = new List<RoomEntry>();

            // The rooms made here, by file name, so the pack's list does not offer them again.
            var madeRooms = custom != null ? custom.All() : new List<RoomEntry>();
            var made = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var room in madeRooms)
            {
                made.Add(CustomRooms.SlugOf(room.address));
            }

            if (content == null)
            {
                found.AddRange(madeRooms);
                return found;
            }

            foreach (var pack in content.LoadedKeys)
            {
                RoomIndex index;

                // The default pack's list as kept here - edited by saving and deleting rooms -
                // once there is one; every other pack's, and the default's until then, as shipped.
                var kept = pack == DefaultPack && custom != null ? custom.ListedRooms() : null;

                if (kept != null)
                {
                    index = new RoomIndex { rooms = kept };
                }
                else
                {
                    TextAsset asset;

                    if (!content.TryGet(IndexAddressFor(pack), out asset) || asset == null)
                    {
                        continue;
                    }

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
                }

                if (index == null || index.rooms == null)
                {
                    continue;
                }

                foreach (var room in index.rooms)
                {
                    // A room made here is on the list too once saved, but read from its own file
                    // - which is current the moment it is saved, and can be deleted - rather than
                    // from the pack, which only has it after the generator's next run.
                    if (room != null && !string.IsNullOrWhiteSpace(room.address)
                        && !made.Contains(CustomRooms.SlugOf(room.address)))
                    {
                        found.Add(room);
                    }
                }
            }

            // The rooms the player has made come after the pack's, oldest first, so the one just
            // made is at the bottom of the list.
            found.AddRange(madeRooms);

            return found;
        }

        /// <summary>The room's thumbnail, or null when it has none yet.</summary>
        public Sprite IconOf(RoomEntry room)
        {
            if (room != null && CustomRooms.IsCustom(room.icon))
            {
                return custom != null ? custom.Icon(room.icon) : null;
            }

            Sprite sprite;

            if (room == null || string.IsNullOrEmpty(room.icon))
            {
                return null;
            }

#if UNITY_EDITOR
            // Saved over from the room editor this session: the thumbnail just rendered, from its file.
            if (CustomRooms.WasRewritten(room.icon))
            {
                var rewritten = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(ProjectPathFor(room.icon, ".png"));

                if (rewritten != null)
                {
                    return rewritten;
                }
            }
#endif

            if (content != null && content.TryGet(room.icon, out sprite))
            {
                return sprite;
            }

#if UNITY_EDITOR
            // Not in Addressables yet - a room just promoted, before the generator's next run -
            // so straight from the pack's folder, which is where the generator will find it.
            var fromFile = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(ProjectPathFor(room.icon, ".png"));

            if (fromFile != null)
            {
                WarnFallback(room, "thumbnail", room.icon);
            }

            return fromFile;
#else
            return null;
#endif
        }

        /// <summary>
        /// Where a pack address's file is in the project: <c>&lt;pack&gt;/Rooms/x</c> is
        /// <c>Assets/Process/&lt;pack&gt;/Rooms/x</c>, as the Addressable Generator addresses it.
        /// </summary>
        static string ProjectPathFor(string address, string extension)
            => "Assets/Process/" + address + extension;

        /// <summary>The addresses already warned about this session, so each is said once.</summary>
        static readonly HashSet<string> warned = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Says that a room's file or thumbnail came from the project folder, not Addressables.
        /// </summary>
        /// <remarks>
        /// The fallback keeps a room just promoted usable before the generator's next run, but it
        /// would hide a room that does not resolve at all: a built game has no fallback, and that
        /// room would be missing or blank there. Said once per address, so a clean Console after
        /// Clear Addressables and a generator run means every room really resolved.
        /// </remarks>
        static void WarnFallback(RoomEntry room, string what, string address)
        {
            if (!warned.Add(what + ":" + address))
            {
                return;
            }

            Debug.LogWarning("[Rooms] '" + room.name + "' " + what + " is not in Addressables ('" + address
                + "'); read from its file in the project instead. A built game would not find it - run the "
                + "Addressable Generator on the default pack.");
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
            if (room != null && CustomRooms.IsCustom(room.address))
            {
                return custom != null ? custom.Read(room.address) : null;
            }

            if (room == null || string.IsNullOrEmpty(room.address))
            {
                return null;
            }

            TextAsset asset;
            string text = null;

#if UNITY_EDITOR
            // Saved over from the room editor this session: the file as just written.
            var rewrittenPath = System.IO.Path.Combine(Application.dataPath, "Process", room.address + ".json");

            if (CustomRooms.WasRewritten(room.address) && System.IO.File.Exists(rewrittenPath))
            {
                text = System.IO.File.ReadAllText(rewrittenPath);
            }
            else
#endif
            if (content != null && content.TryGet(room.address, out asset) && asset != null)
            {
                text = asset.text;
            }
#if UNITY_EDITOR
            else
            {
                // Not in Addressables yet - a room just promoted, before the generator's next run -
                // so straight from the pack's folder, which is where the generator will find it.
                var path = System.IO.Path.Combine(Application.dataPath, "Process", room.address + ".json");

                if (System.IO.File.Exists(path))
                {
                    text = System.IO.File.ReadAllText(path);
                    WarnFallback(room, "room file", room.address);
                }
            }
#endif

            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            WorldLibrary library;

            try
            {
                library = JsonUtility.FromJson<WorldLibrary>(text);
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
