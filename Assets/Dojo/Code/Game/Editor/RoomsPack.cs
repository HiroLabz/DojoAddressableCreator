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

        /// <summary>Whose settings a new <see cref="GroupName"/> is made with.</summary>
        const string TemplateGroup = "Content_Shared";

        const string PackRoomPrefix = "default_room_";
        const string MadeRoomPrefix = "room_";

        [Serializable]
        sealed class Index
        {
            public List<RoomEntry> rooms = new List<RoomEntry>();
        }

        // ── The group ───────────────────────────────────────────────────────────────────────

        /// <summary>
        /// <see cref="GroupName"/>, made the first time it is asked for with exactly the settings
        /// <c>Content_Shared</c> has - bundled by label, built and loaded from the same paths - so
        /// rooms get bundles of their own and nothing else about delivery changes.
        /// </summary>
        public static AddressableAssetGroup EnsureGroup(AddressableAssetSettings settings)
        {
            if (settings == null)
            {
                return null;
            }

            var template = settings.FindGroup(TemplateGroup);
            var group = settings.FindGroup(GroupName);

            if (group != null)
            {
                // A group made before every schema was copied - an earlier failed run - is given
                // the ones it is missing, so it ends up with all of the template's settings.
                if (template != null)
                {
                    foreach (var schema in new List<AddressableAssetGroupSchema>(template.Schemas))
                    {
                        if (schema != null && !group.HasSchema(schema.GetType()))
                        {
                            group.AddSchema(schema, false);
                            Debug.Log("[RoomsPack] Gave " + GroupName + " the " + schema.GetType().Name
                                + " it was missing, from " + TemplateGroup + ".", group);
                        }
                    }
                }

                return group;
            }

            if (template == null)
            {
                Debug.LogError("[RoomsPack] There is no " + TemplateGroup + " group to copy " + GroupName
                    + "'s settings from, so it was not made.");
                return null;
            }

            // A copy of the list, not the template's own: adding a schema to the new group raises
            // events that touch the template's list while CreateGroup is still walking it.
            group = settings.CreateGroup(GroupName, false, false, true,
                new List<AddressableAssetGroupSchema>(template.Schemas));
            Debug.Log("[RoomsPack] Made the " + GroupName + " group, with " + TemplateGroup + "'s settings.", group);

            return group;
        }

        // ── Thumbnails ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Imports a thumbnail as one sprite with alpha and no mipmaps: what the Rooms tab's cards
        /// draw, and what the content service hands back when a card asks for a sprite.
        /// </summary>
        public static void ImportAsSprite(string assetPath)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;

            if (importer == null)
            {
                return;
            }

            if (importer.textureType == TextureImporterType.Sprite
                && importer.spriteImportMode == SpriteImportMode.Single
                && importer.alphaIsTransparency
                && !importer.mipmapEnabled)
            {
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }

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
        {
            if (string.IsNullOrEmpty(address) || !address.StartsWith(prefix, StringComparison.Ordinal))
            {
                return null;
            }

            return roomsFolder + "/" + address.Substring(prefix.Length) + (extension ?? string.Empty);
        }

        /// <summary>Whether an image of that name exists, whatever its extension.</summary>
        static bool IconExists(string withoutExtension)
        {
            var folder = Path.GetDirectoryName(withoutExtension);
            var name = Path.GetFileName(withoutExtension);

            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                return false;
            }

            foreach (var file in Directory.GetFiles(folder, name + ".*"))
            {
                if (!file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

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

            var slug = Path.GetFileNameWithoutExtension(fileName).Substring(MadeRoomPrefix.Length);
            var target = Folder + "/" + PackRoomPrefix + slug + ".json";
            var thumbnail = ThumbnailFolder + "/" + MadeRoomPrefix + slug + ".png";
            var address = Pack + "/Rooms/" + PackRoomPrefix + slug;
            var icon = Pack + "/Rooms/Thumbnails/" + MadeRoomPrefix + slug;

            if (File.Exists(target))
            {
                return "The pack already has a room at " + target + ".";
            }

            var index = ReadIndex();
            if (index == null)
            {
                return IndexPath + " could not be read, so the room was not promoted.";
            }

            if (index.rooms.Exists(r => r != null && string.Equals(r.address, address, StringComparison.Ordinal)))
            {
                return "rooms.json already lists " + address + ".";
            }

            var name = RoomName(assetPath, slug);

            var moved = AssetDatabase.MoveAsset(assetPath, target);
            if (!string.IsNullOrEmpty(moved))
            {
                return "Could not rename " + assetPath + ": " + moved;
            }

            index.rooms.Add(new RoomEntry { name = name, address = address, icon = icon });
            File.WriteAllText(IndexPath, JsonUtility.ToJson(index, true));

            AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(IndexPath, ImportAssetOptions.ForceUpdate);

            if (File.Exists(thumbnail))
            {
                AssetDatabase.ImportAsset(thumbnail, ImportAssetOptions.ForceUpdate);
                ImportAsSprite(thumbnail);
            }
            else
            {
                Debug.LogWarning("[RoomsPack] '" + name + "' has no thumbnail at " + thumbnail
                    + "; rooms.json names it anyway, so its card will be blank until one is there.");
            }

            // Registered now, at the addresses rooms.json gives, so the Rooms tab offers it as a pack
            // room straight away rather than after the next generator run - which files it the same.
            Register(target, address);
            Register(thumbnail, icon);

            Debug.Log("[RoomsPack] Promoted '" + name + "' into the pack: " + target + ", listed last in "
                + IndexPath + " and filed into " + GroupName + ".",
                AssetDatabase.LoadAssetAtPath<TextAsset>(target));

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

        static Index ReadIndex()
        {
            if (!File.Exists(IndexPath))
            {
                return new Index();
            }

            try
            {
                var index = JsonUtility.FromJson<Index>(File.ReadAllText(IndexPath));
                if (index != null && index.rooms == null)
                {
                    index.rooms = new List<RoomEntry>();
                }

                return index;
            }
            catch (Exception exception)
            {
                Debug.LogError("[RoomsPack] " + IndexPath + " could not be read: " + exception.Message);
                return null;
            }
        }

        /// <summary>The name the room was saved under, from its own file; the file name failing that.</summary>
        static string RoomName(string assetPath, string slug)
        {
            try
            {
                var library = JsonUtility.FromJson<WorldLibrary>(File.ReadAllText(assetPath));
                var world = library != null && library.worlds != null && library.worlds.Count > 0
                    ? library.Find(library.CurrentName()) ?? library.worlds[0]
                    : null;

                if (world != null && !string.IsNullOrWhiteSpace(world.name))
                {
                    return world.name.Trim();
                }
            }
            catch (Exception)
            {
                // Falls back to the file name below.
            }

            return ObjectNames.NicifyVariableName(slug.Replace('_', ' '));
        }

        // ── Menu ────────────────────────────────────────────────────────────────────────────

        [MenuItem("Assets/Dojo/Promote Room To Pack", true)]
        [MenuItem("Tools/Dojo/Promote Room To Pack", true)]
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
        [MenuItem("Tools/Dojo/Promote Room To Pack")]
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
