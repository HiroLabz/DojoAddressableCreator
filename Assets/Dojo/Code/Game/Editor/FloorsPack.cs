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
    /// The default pack's floor layouts as content: where they live, the group they build into,
    /// the checks that keep <c>floors.json</c> honest, and promoting a layout made in the editor
    /// into the pack. <see cref="RoomsPack"/> for floors (ClickUp 14ze67xgf0v).
    /// </summary>
    /// <remarks>
    /// <c>Assets/Process/default/Floors</c> holds the pack's layouts (<c>default_floor_&lt;x&gt;.json</c>),
    /// the layouts made in the floor editor (<c>floor_&lt;x&gt;.json</c>), the index the Layouts tab
    /// reads (<c>floors.json</c>) and every layout's thumbnail (<c>Thumbnails/floor_&lt;x&gt;.png</c>).
    /// No prefabs: a layout is laid from its file, and its thumbnail is rendered from the file too.
    /// <para>
    /// A built game only finds layouts through <c>floors.json</c>, so a layout made in the editor
    /// reaches players once it is promoted: renamed into the pack's naming and added to the index.
    /// </para>
    /// </remarks>
    public static class FloorsPack
    {
        public const string Pack = "default";
        public const string Folder = "Assets/Process/default/Floors";
        public const string ThumbnailFolder = Folder + "/Thumbnails";
        public const string IndexFile = "floors.json";
        public const string IndexPath = Folder + "/" + IndexFile;

        /// <summary>The group the pack's layouts build into, apart from the rest of the pack.</summary>
        public const string GroupName = "Content_Floors";

        const string PackLayoutPrefix = CustomFloorLayouts.PackFilePrefix;
        const string MadeLayoutPrefix = CustomFloorLayouts.FilePrefix;

        [Serializable]
        sealed class Index
        {
            public List<FloorLayoutEntry> floors = new List<FloorLayoutEntry>();
        }

        /// <summary><see cref="GroupName"/>, made on first need with <c>Content_Shared</c>'s settings.</summary>
        public static AddressableAssetGroup EnsureGroup(AddressableAssetSettings settings)
            => ContentPackFolder.EnsureGroup(settings, GroupName);

        // ── Checking the index ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Everything wrong between <c>floors.json</c> and the files beside it: entries pointing at
        /// a layout or thumbnail that is not there, and pack layouts the index does not list.
        /// Reports only - the index is never rewritten here, since its order is a choice someone made.
        /// </summary>
        /// <param name="floorsFolder">The pack's Floors folder, e.g. <c>Assets/Process/default/Floors</c>.</param>
        /// <param name="pack">The pack's name, the first segment of every address.</param>
        public static List<string> CheckIndex(string floorsFolder, string pack)
        {
            var problems = new List<string>();
            var indexPath = floorsFolder + "/" + IndexFile;

            if (!File.Exists(indexPath))
            {
                if (Directory.Exists(floorsFolder) && Directory.GetFiles(floorsFolder, PackLayoutPrefix + "*.json").Length > 0)
                {
                    problems.Add(indexPath + " is missing, so none of the pack's layouts will be listed.");
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
            var prefix = pack + "/Floors/";

            foreach (var layout in index != null && index.floors != null ? index.floors : new List<FloorLayoutEntry>())
            {
                if (layout == null)
                {
                    continue;
                }

                var file = ContentPackFolder.FileFor(floorsFolder, prefix, layout.address, ".json");
                if (file == null)
                {
                    problems.Add(IndexFile + ": '" + layout.name + "' points to layout '" + layout.address
                        + "', which is not a " + prefix + "… address.");
                }
                else if (!File.Exists(file))
                {
                    problems.Add(IndexFile + ": '" + layout.name + "' points to layout '" + layout.address
                        + "', but there is no " + file + ".");
                }
                else
                {
                    listed.Add(Path.GetFileName(file));
                }

                var icon = ContentPackFolder.FileFor(floorsFolder, prefix, layout.icon, null);
                if (icon == null || !ContentPackFolder.IconExists(icon))
                {
                    problems.Add(IndexFile + ": '" + layout.name + "' points to thumbnail '" + layout.icon
                        + "', but there is no such image" + (icon != null ? " at " + icon + ".*" : "") + ".");
                }
            }

            foreach (var file in Directory.GetFiles(floorsFolder, PackLayoutPrefix + "*.json"))
            {
                var name = Path.GetFileName(file);

                if (!listed.Contains(name))
                {
                    problems.Add(name + " is a pack layout but " + IndexFile + " does not list it, so it is never "
                        + "offered. Add it to " + IndexFile + " where it should appear.");
                }
            }

            return problems;
        }

        // ── Promoting a made layout ─────────────────────────────────────────────────────────

        /// <summary>
        /// Makes a layout from the floor editor part of the pack: <c>floor_&lt;x&gt;.json</c> becomes
        /// <c>default_floor_&lt;x&gt;.json</c>, its thumbnail stays <c>Thumbnails/floor_&lt;x&gt;.png</c>,
        /// it is added to the end of <c>floors.json</c>, and both files are filed into
        /// <see cref="GroupName"/>. From then on the Layouts tab offers it once, as a pack layout.
        /// </summary>
        /// <returns>Null when done; otherwise why it was not.</returns>
        public static string Promote(string assetPath)
        {
            assetPath = (assetPath ?? string.Empty).Replace('\\', '/');
            var fileName = Path.GetFileName(assetPath);

            if (Path.GetDirectoryName(assetPath)?.Replace('\\', '/') != Folder
                || !fileName.StartsWith(MadeLayoutPrefix, StringComparison.Ordinal)
                || !fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                return "Only a layout made in the floor editor can be promoted: a floor_<name>.json directly in " + Folder + ".";
            }

            // The renaming and the layout list are the store's own promotion; filing into
            // Addressables, which only editor code can do, is added here.
            var slug = Path.GetFileNameWithoutExtension(fileName);
            string name;
            var problem = new CustomFloorLayouts(null).PromoteInProject(slug, out name);

            if (problem != null)
            {
                return problem;
            }

            var x = slug.Substring(MadeLayoutPrefix.Length);
            Register(Folder + "/" + PackLayoutPrefix + x + ".json", Pack + "/Floors/" + PackLayoutPrefix + x);
            Register(ThumbnailFolder + "/" + slug + ".png", Pack + "/Floors/Thumbnails/" + slug);

            return null;
        }

        /// <summary>Files one of the pack's layout files into the floors' group at an address, with the pack's label.</summary>
        public static bool Register(string assetPath, string address)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            var group = EnsureGroup(settings);
            var guid = AssetDatabase.AssetPathToGUID(assetPath);

            if (group == null || string.IsNullOrEmpty(guid))
            {
                return false;
            }

            var entry = settings.CreateOrMoveEntry(guid, group, false, false);

            if (entry == null)
            {
                return false;
            }

            entry.SetAddress(address, false);
            entry.SetLabel(Pack, true, true, false);
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true, true);
            AssetDatabase.SaveAssets();

            return true;
        }

        // ── Menu ────────────────────────────────────────────────────────────────────────────

        [MenuItem("Assets/Dojo/Promote Floor Layout To Pack", true)]
        [MenuItem("Tools/Dojo/Content/Floors/Promote Floor Layout To Pack", true)]
        static bool CanPromoteSelected()
        {
            var path = Selection.activeObject != null ? AssetDatabase.GetAssetPath(Selection.activeObject) : null;
            return !string.IsNullOrEmpty(path)
                && Path.GetDirectoryName(path).Replace('\\', '/') == Folder
                && Path.GetFileName(path).StartsWith(MadeLayoutPrefix, StringComparison.Ordinal)
                && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Promotes the floor_&lt;x&gt;.json selected in the Project window.</summary>
        [MenuItem("Assets/Dojo/Promote Floor Layout To Pack")]
        [MenuItem("Tools/Dojo/Content/Floors/Promote Floor Layout To Pack")]
        static void PromoteSelected()
        {
            var path = AssetDatabase.GetAssetPath(Selection.activeObject);
            var problem = Promote(path);

            if (problem != null)
            {
                Debug.LogError("[FloorsPack] " + problem);
            }
        }
    }
}
