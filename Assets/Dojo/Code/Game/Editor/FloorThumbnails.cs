using System.Collections.Generic;
using System.IO;
using Dojo.Game.Placement;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

namespace Dojo.Game.Editor
{
    /// <summary>
    /// Renders a thumbnail of every floor layout in the pack's Floors folder, for the Layouts tab's cards.
    /// </summary>
    /// <remarks>
    /// The same picture as <see cref="RoomThumbnails"/> makes of a room, but drawn from the layout's
    /// file rather than a prefab: a layout has none, so its pieces are stood up from their own
    /// prefabs for the render and thrown away after. Each image is imported as a sprite and, for a
    /// pack layout, filed into <see cref="FloorsPack.GroupName"/> at
    /// <c>default/Floors/Thumbnails/floor_&lt;name&gt;</c>, the address the index gives as its icon.
    /// A layout made here keeps its picture on disk only, where its store reads it.
    /// </remarks>
    public static class FloorThumbnails
    {
        const string Pack = FloorsPack.Pack;
        const string LayoutFolder = FloorsPack.Folder;
        const string OutputFolder = FloorsPack.ThumbnailFolder;

        [MenuItem("Tools/Dojo/Content/Floors/Generate Floor Thumbnails")]
        public static void Generate()
        {
            if (!Directory.Exists(LayoutFolder))
            {
                Debug.LogError("[FloorThumbnails] There is no " + LayoutFolder + " to render.");
                return;
            }

            Directory.CreateDirectory(OutputFolder);

            var files = new List<string>(Directory.GetFiles(LayoutFolder, "*.json"));
            files.RemoveAll(f => Path.GetFileName(f) == FloorsPack.IndexFile);
            files.Sort(System.StringComparer.Ordinal);

            var written = 0;
            var preview = new PreviewRenderUtility();

            try
            {
                // One render thrown away first: the preview renderer's first image can come back empty.
                var warmed = false;

                foreach (var file in files)
                {
                    var layout = Read(file);

                    if (layout == null)
                    {
                        Debug.LogWarning("[FloorThumbnails] '" + file + "' could not be read; skipped.");
                        continue;
                    }

                    var slug = ThumbnailNameFor(Path.GetFileNameWithoutExtension(file));
                    byte[] png;
                    var root = StandUp(slug, layout);

                    try
                    {
                        if (!warmed)
                        {
                            RoomThumbnails.RenderInstance(preview, root);
                            warmed = true;
                        }

                        png = RoomThumbnails.RenderInstance(preview, root);
                    }
                    finally
                    {
                        Object.DestroyImmediate(root);
                    }

                    if (png == null)
                    {
                        Debug.LogWarning("[FloorThumbnails] '" + slug + "' has nothing to draw; skipped.");
                        continue;
                    }

                    File.WriteAllBytes(OutputFolder + "/" + slug + ".png", png);
                    written++;
                }
            }
            finally
            {
                preview.Cleanup();
            }

            AssetDatabase.Refresh();

            var registered = 0;

            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { OutputFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                ContentPackFolder.ImportAsSprite(path);

                var name = Path.GetFileNameWithoutExtension(path);

                // Only the pack's own layouts are addressed; a made layout's picture is read from disk.
                if (File.Exists(LayoutFolder + "/" + CustomFloorLayouts.PackFilePrefix + name.Substring(CustomFloorLayouts.FilePrefix.Length) + ".json")
                    && FloorsPack.Register(path, Pack + "/Floors/Thumbnails/" + name))
                {
                    registered++;
                }
            }

            AssetDatabase.SaveAssets();

            Debug.Log("[FloorThumbnails] Rendered " + written + " layout thumbnail(s) into " + OutputFolder
                + " and filed " + registered + " into " + FloorsPack.GroupName + ".");
        }

        /// <summary><c>default_floor_x</c> and <c>floor_x</c> both picture as <c>floor_x</c>.</summary>
        static string ThumbnailNameFor(string fileName)
            => fileName.StartsWith(CustomFloorLayouts.PackFilePrefix, System.StringComparison.Ordinal)
                ? CustomFloorLayouts.FilePrefix + fileName.Substring(CustomFloorLayouts.PackFilePrefix.Length)
                : fileName;

        static WorldSave Read(string path)
        {
            try
            {
                var library = JsonUtility.FromJson<WorldLibrary>(File.ReadAllText(path));

                if (library == null || library.worlds == null || library.worlds.Count == 0)
                {
                    return null;
                }

                var world = library.Find(library.CurrentName()) ?? library.worlds[0];
                return world != null ? world.Normalised() : null;
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("[FloorThumbnails] '" + path + "' could not be read: " + exception.Message);
                return null;
            }
        }

        /// <summary>
        /// The layout's ground floor stood up in the scene from each piece's own prefab, where the
        /// file puts it, under one root - the same shape the rooms' prefabs have, without saving it.
        /// </summary>
        static GameObject StandUp(string name, WorldSave layout)
        {
            var root = new GameObject(name);
            var missing = new List<string>();
            var ground = layout.floors.Find(f => f != null && f.index == 0);

            foreach (var piece in ground != null ? ground.pieces : new List<WorldSave.SavedPiece>())
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PiecePrefabPath(piece.resource));

                if (prefab == null)
                {
                    if (!missing.Contains(piece.resource))
                    {
                        missing.Add(piece.resource);
                    }

                    continue;
                }

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                instance.transform.localPosition = piece.position;
                instance.transform.localRotation = Quaternion.Euler(0f, piece.rotationSteps * 90f, 0f);
                instance.transform.localScale = piece.scale == Vector3.zero ? Vector3.one : piece.scale;
            }

            if (missing.Count > 0)
            {
                Debug.LogWarning("[FloorThumbnails] '" + name + "': no prefab found for " + string.Join(", ", missing) + ".");
            }

            return root;
        }

        /// <summary>
        /// The prefab a piece's content address names: <c>&lt;pack&gt;/&lt;name&gt;</c> is
        /// <c>Assets/Process/&lt;pack&gt;/Prefabs/&lt;name&gt;.prefab</c>, as the generator addresses them.
        /// </summary>
        static string PiecePrefabPath(string address)
        {
            if (string.IsNullOrEmpty(address))
            {
                return string.Empty;
            }

            var slash = address.IndexOf('/');
            return slash < 0
                ? string.Empty
                : "Assets/Process/" + address.Substring(0, slash) + "/Prefabs/" + address.Substring(slash + 1) + ".prefab";
        }
    }
}
