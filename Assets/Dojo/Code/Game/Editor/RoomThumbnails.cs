using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

namespace Dojo.Game.Editor
{
    /// <summary>
    /// Renders a thumbnail of every room prefab, for the Rooms tab's cards.
    /// </summary>
    /// <remarks>
    /// The same look as the floor and wall icons the Addressable Generator makes - an orthographic
    /// preview-scene render keyed out to a transparent background - but from a little higher, so a
    /// room reads as a floor plan with its walls and desks rather than as a wall seen side on.
    /// <para>
    /// Each image is imported as a sprite and filed into the default group at
    /// <c>default/Rooms/Thumbnails/room_&lt;name&gt;</c>, the address the room index gives as its
    /// icon. Run it again after a room prefab changes.
    /// </para>
    /// </remarks>
    public static class RoomThumbnails
    {
        const string Pack = "default";
        const string PrefabFolder = "Assets/Process/default/Rooms/Prefabs";
        const string OutputFolder = "Assets/Process/default/Rooms/Thumbnails";

        const int Size = 256;
        const float Yaw = 135f;
        const float Pitch = 40f;
        const float Padding = 1.1f;

        static readonly Color KeyColour = new Color(1f, 0f, 1f, 1f);

        [MenuItem("Tools/Dojo/Generate Room Thumbnails")]
        public static void Generate()
        {
            if (!AssetDatabase.IsValidFolder(PrefabFolder))
            {
                Debug.LogError("[RoomThumbnails] There is no " + PrefabFolder + " to render.");
                return;
            }

            Directory.CreateDirectory(OutputFolder);

            var written = 0;
            var preview = new PreviewRenderUtility();

            try
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                    if (prefab == null)
                    {
                        continue;
                    }

                    var png = Render(preview, prefab);

                    if (png == null)
                    {
                        Debug.LogWarning("[RoomThumbnails] '" + prefab.name + "' has nothing to draw; skipped.", prefab);
                        continue;
                    }

                    File.WriteAllBytes(OutputFolder + "/" + prefab.name + ".png", png);
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
                ImportAsSprite(path);

                if (Register(guid, Pack + "/Rooms/Thumbnails/" + Path.GetFileNameWithoutExtension(path)))
                {
                    registered++;
                }
            }

            AssetDatabase.SaveAssets();

            Debug.Log("[RoomThumbnails] Rendered " + written + " room thumbnail(s) into " + OutputFolder
                + " and filed " + registered + " into the default group.");
        }

        /// <summary>One prefab, framed to its own bounds. Null when there is nothing to see.</summary>
        static byte[] Render(PreviewRenderUtility preview, GameObject prefab)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

            if (instance == null)
            {
                return null;
            }

            try
            {
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                var renderers = instance.GetComponentsInChildren<Renderer>(true);

                if (renderers.Length == 0)
                {
                    return null;
                }

                var bounds = renderers[0].bounds;
                for (var i = 1; i < renderers.Length; i++)
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }

                preview.AddSingleGO(instance);
                preview.BeginStaticPreview(new Rect(0f, 0f, Size, Size));

                var camera = preview.camera;
                var direction = Quaternion.Euler(Pitch, Yaw, 0f) * Vector3.forward;
                var extent = Mathf.Max(bounds.extents.x, Mathf.Max(bounds.extents.y, bounds.extents.z));

                camera.transform.position = bounds.center - direction * (extent * 4f);
                camera.transform.rotation = Quaternion.LookRotation(direction);
                camera.orthographic = true;
                camera.orthographicSize = extent * Padding;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = extent * 20f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = KeyColour;

                preview.lights[0].intensity = 1.1f;
                preview.lights[0].transform.rotation = Quaternion.Euler(50f, Yaw + 40f, 0f);
                preview.lights[1].intensity = 0.55f;
                preview.lights[1].transform.rotation = Quaternion.Euler(15f, Yaw - 130f, 0f);

                camera.Render();

                var rendered = preview.EndStaticPreview();

                if (rendered == null)
                {
                    return null;
                }

                try
                {
                    return KeyedOut(rendered);
                }
                finally
                {
                    Object.DestroyImmediate(rendered);
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>The render with its magenta background swapped for transparency.</summary>
        static byte[] KeyedOut(Texture2D rendered)
        {
            var pixels = rendered.GetPixels32();

            for (var i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];

                if (p.r > 200 && p.g < 60 && p.b > 200)
                {
                    pixels[i] = new Color32(0, 0, 0, 0);
                }
            }

            var output = new Texture2D(rendered.width, rendered.height, TextureFormat.RGBA32, false);

            try
            {
                output.SetPixels32(pixels);
                output.Apply(false, false);
                return output.EncodeToPNG();
            }
            finally
            {
                Object.DestroyImmediate(output);
            }
        }

        /// <summary>A sprite, because an <c>Image</c> draws nothing else.</summary>
        static void ImportAsSprite(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;

            if (importer == null
                || (importer.textureType == TextureImporterType.Sprite && importer.alphaIsTransparency && !importer.mipmapEnabled))
            {
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }

        /// <summary>
        /// Files one thumbnail into the default group under its address, with the pack's label so
        /// a preload of the pack takes it in. An entry already elsewhere keeps its group.
        /// </summary>
        static bool Register(string guid, string address)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;

            if (settings == null || settings.DefaultGroup == null)
            {
                Debug.LogError("[RoomThumbnails] There are no Addressables settings to file the thumbnails into.");
                return false;
            }

            var entry = settings.FindAssetEntry(guid) ?? settings.CreateOrMoveEntry(guid, settings.DefaultGroup, false, false);

            if (entry == null)
            {
                return false;
            }

            entry.SetAddress(address, false);
            entry.SetLabel(Pack, true, true, false);
            settings.SetDirty(UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.ModificationEvent.EntryModified, entry, true, true);

            return true;
        }
    }
}
