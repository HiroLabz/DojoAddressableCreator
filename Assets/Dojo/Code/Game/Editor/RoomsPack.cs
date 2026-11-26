using System;
using System.Collections.Generic;
using System.IO;
using Dojo.Game.Placement;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Dojo.Game.Editor
{
    /// <summary>
    /// The default pack's rooms as content: where they live, the group they build into, the checks
    /// that keep <c>rooms.json</c> honest, and promoting a room made in the editor into the pack.
    /// </summary>
    /// <remarks>
    /// <c>Assets/Process/default/Rooms</c> holds the pack's rooms (<c>default_room_&lt;x&gt;.json</c>),
    /// the rooms made in the room editor (<c>room_&lt;x&gt;.json</c>), the index the Rooms tab
    /// reads (<c>rooms.json</c>), every room's thumbnail (<c>Thumbnails/room_&lt;x&gt;.png</c>), and
    /// under <c>Editor/</c> what only editor tools read - the room prefabs the thumbnails are
    /// rendered from, which the Addressable Generator leaves out of the download.
    /// <para>
    /// A built game only finds rooms through <c>rooms.json</c>, so a room made in the editor reaches
    /// players once it is promoted: renamed into the pack's naming and added to the index.
    /// </para>
    /// <para>
    /// What the floors' folder shares with this - the group made from <c>Content_Shared</c>, the
    /// sprite import, the file an index address names - lives in <see cref="ContentPackFolder"/>.
    /// </para>
    /// </remarks>
    public static class RoomsPack
    {
        public const string Pack = "default";
        public const string Folder = "Assets/Process/default/Rooms";
        public const string ThumbnailFolder = Folder + "/Thumbnails";
        public const string PrefabFolder = Folder + "/Editor/Prefabs";
        public const string IndexPath = Folder + "/rooms.json";

        /// <summary>The group the pack's rooms build into, apart from the rest of the pack.</summary>
        public const string GroupName = "Content_Rooms";

        const string PackRoomPrefix = "default_room_";
        const string MadeRoomPrefix = "room_";

        [Serializable]
        sealed class Index
        {
            public List<RoomEntry> rooms = new List<RoomEntry>();
        }

        // ── The group ───────────────────────────────────────────────────────────────────────

        /// <summary><see cref="GroupName"/>, made on first need with <c>Content_Shared</c>'s settings (<see cref="ContentPackFolder.EnsureGroup"/>).</summary>
        public static AddressableAssetGroup EnsureGroup(AddressableAssetSettings settings)
            => ContentPackFolder.EnsureGroup(settings, GroupName);

        // ── Thumbnails ──────────────────────────────────────────────────────────────────────

        /// <summary>Imports a thumbnail as one sprite with alpha and no mipmaps (<see cref="ContentPackFolder.ImportAsSprite"/>).</summary>
        public static void ImportAsSprite(string assetPath) => ContentPackFolder.ImportAsSprite(assetPath);

        // ── Checking the index ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Everything wrong between <c>rooms.json</c> and the files beside it: entries pointing at a
        /// room or thumbnail that is not there, and pack rooms the index does not list. Reports
        /// only - the index is never rewritten here, since its order is a choice someone made.
        /// </summary>
        /// <param name="roomsFolder">The pack's Rooms folder, e.g. <c>Assets/Process/default/Rooms</c>.</param>
        /// <param name="pack">The pack's name, the first segment of every address.</param>
        public static List<string> CheckIndex(string roomsFolder, string pack)
        {
            var problems = new List<string>();
            var indexPath = roomsFolder + "/rooms.json";

            if (!File.Exists(indexPath))
            {
                if (Directory.GetFiles(roomsFolder, PackRoomPrefix + "*.json").Length > 0)
                {
                    problems.Add(indexPath + " is missing, so none of the pack's rooms will be listed.");
                }

                return problems;
            }

            Index index;

            try
            {
                index = JsonUtility.FromJson<Index>(File.ReadAllText(indexPath));
            }
            catch (Exception exception)
            {
                problems.Add(indexPath + " could not be read: " + exception.Message);
                return problems;
            }

            var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var prefix = pack + "/Rooms/";

            foreach (var room in index != null && index.rooms != null ? index.rooms : new List<RoomEntry>())
            {
                if (room == null)
                {
                    continue;
                }

                var file = FileFor(roomsFolder, prefix, room.address, ".json");
                if (file == null)
                {
                    problems.Add("rooms.json: '" + room.name + "' points to room '" + room.address
                        + "', which is not a " + prefix + "… address.");
                }
                else if (!File.Exists(file))
                {
                    problems.Add("rooms.json: '" + room.name + "' points to room '" + room.address
                        + "', but there is no " + file + ".");
                }
                else
                {
                    listed.Add(Path.GetFileName(file));
                }

                var icon = FileFor(roomsFolder, prefix, room.icon, null);
                if (icon == null || !IconExists(icon))
                {
                    problems.Add("rooms.json: '" + room.name + "' points to thumbnail '" + room.icon
                        + "', but there is no such image" + (icon != null ? " at " + icon + ".*" : "") + ".");
                }
            }

            foreach (var file in Directory.GetFiles(roomsFolder, PackRoomPrefix + "*.json"))
            {
                var name = Path.GetFileName(file);

                if (!listed.Contains(name))
                {
                    problems.Add(name + " is a pack room but rooms.json does not list it, so it is never "
                        + "offered. Add it to rooms.json where it should appear.");
                }
            }

            return problems;
        }

        /// <summary>The file an address under the Rooms folder names, or null when it is not one.</summary>
        static string FileFor(string roomsFolder, string prefix, string address, string extension)
            => ContentPackFolder.FileFor(roomsFolder, prefix, address, extension);

        /// <summary>Whether an image of that name exists, whatever its extension.</summary>
        static bool IconExists(string withoutExtension) => ContentPackFolder.IconExists(withoutExtension);

        // ── Promoting a made room ───────────────────────────────────────────────────────────

        /// <summary>
        /// Makes a room from the room editor part of the pack: <c>room_&lt;x&gt;.json</c> becomes
        /// <c>default_room_&lt;x&gt;.json</c>, its thumbnail stays <c>Thumbnails/room_&lt;x&gt;.png</c>,
        /// it is added to the end of <c>rooms.json</c>, and both files are imported again. From then
        /// on the Rooms tab offers it once, as a pack room, and a built game can load it.
        /// </summary>
        /// <returns>Null when done; otherwise why it was not.</returns>
        public static string Promote(string assetPath)
        {
            assetPath = (assetPath ?? string.Empty).Replace('\\', '/');
            var fileName = Path.GetFileName(assetPath);

            if (Path.GetDirectoryName(assetPath)?.Replace('\\', '/') != Folder
                || !fileName.StartsWith(MadeRoomPrefix, StringComparison.Ordinal)
                || !fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                return "Only a room made in the room editor can be promoted: a room_<name>.json directly in " + Folder + ".";
            }

            // The renaming and the room lists are the game's own promotion - the Rooms tab's PROMOTE
            // runs the same - and filing into Addressables, which only editor code can do, is added here.
            var slug = Path.GetFileNameWithoutExtension(fileName);
            string name;
            var problem = CustomRooms.PromoteInProject(slug, out name);

            if (problem != null)
            {
                return problem;
            }

            var x = slug.Substring(MadeRoomPrefix.Length);
            Register(Folder + "/" + PackRoomPrefix + x + ".json", Pack + "/Rooms/" + PackRoomPrefix + x);
            Register(ThumbnailFolder + "/" + slug + ".png", Pack + "/Rooms/Thumbnails/" + slug);

            return null;
        }

        /// <summary>Files one of the pack's room files into the rooms' group at an address, with the pack's label.</summary>
        static void Register(string assetPath, string address)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            var group = EnsureGroup(settings);
            var guid = AssetDatabase.AssetPathToGUID(assetPath);

            if (group == null || string.IsNullOrEmpty(guid))
            {
                return;
            }

            var entry = settings.CreateOrMoveEntry(guid, group, false, false);

            if (entry == null)
            {
                return;
            }

            entry.SetAddress(address, false);
            entry.SetLabel(Pack, true, true, false);
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true, true);
            AssetDatabase.SaveAssets();
        }

        // ── Menu ────────────────────────────────────────────────────────────────────────────

        [MenuItem("Assets/Dojo/Promote Room To Pack", true)]
        [MenuItem("Tools/Dojo/Content/Rooms/Promote Room To Pack", true)]
        static bool CanPromoteSelected()
        {
            var path = Selection.activeObject != null ? AssetDatabase.GetAssetPath(Selection.activeObject) : null;
            return !string.IsNullOrEmpty(path)
                && Path.GetDirectoryName(path).Replace('\\', '/') == Folder
                && Path.GetFileName(path).StartsWith(MadeRoomPrefix, StringComparison.Ordinal)
                && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Promotes the room_&lt;x&gt;.json selected in the Project window.</summary>
        [MenuItem("Assets/Dojo/Promote Room To Pack")]
        [MenuItem("Tools/Dojo/Content/Rooms/Promote Room To Pack")]
        static void PromoteSelected()
        {
            var path = AssetDatabase.GetAssetPath(Selection.activeObject);
            var problem = Promote(path);

            if (problem != null)
            {
                Debug.LogError("[RoomsPack] " + problem);
            }
        }
    }
}
