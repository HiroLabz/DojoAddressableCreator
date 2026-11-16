using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Dojo.Game.Placement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Dojo.Game.Editor
{
    /// <summary>
    /// Builds the default pack's prefabs for the MVP kit - Harold's models in <c>Assets/Dojo/MVP</c> -
    /// facing and sized as they stand in the AddressableSetupV2 scene.
    /// </summary>
    /// <remarks>
    /// The models carry their up axis and units on their root (most turn a quarter about X and scale
    /// by 2.54), and the V2 scene is where each was checked by eye. So every piece is built from the
    /// rotation and scale its model has in that scene, never reset: resetting them is what laid the
    /// first import on its back at two fifths of its size.
    /// <para>
    /// Each prefab has its pivot at the bottom middle of the piece, one box collider round it all,
    /// and materials on the pack's Dojo/Fade Lit shader so it fades with its floor like the rest of
    /// the pack. Floor tiles get <see cref="FloorPiece"/>. Run the Addressable Generator afterwards
    /// for addresses, thumbnails and inventory entries.
    /// </para>
    /// </remarks>
    public static class MvpKitImporter
    {
        public const string ModelFolder = "Assets/Dojo/MVP";
        public const string ReferenceScene = "Assets/Dojo/Scenes/AddressableSetupV2.unity";
        const string TextureFolder = ModelFolder + "/textures";
        const string PackFolder = "Assets/Process/default";
        const string MadeTextures = PackFolder + "/Textures/mvp";

        /// <summary>Models that are not one piece. The elevator file holds the whole kit.</summary>
        static readonly HashSet<string> Skipped = new HashSet<string> { "Elevator" };

        static readonly HashSet<string> Floors = new HashSet<string> { "LightGrayFloorTile", "WoodFloorTile", "BasePlatform" };

        /// <summary>
        /// Prefab names for models whose own names say "wall" but are not walls. The rooms treat
        /// anything with "wall" in its address as a wall - cut for doorways, kept to the room's edge -
        /// so a screen or a planter named that way would be taken for one.
        /// </summary>
        static readonly Dictionary<string, string> Renamed = new Dictionary<string, string>
        {
            { "WallDashboardScreen", "default_dashboard_screen" },
            { "LargePresentationWallDisplay", "default_large_presentation_display" },
            { "PlanterWallBox", "default_planter_box" },
        };

        sealed class Look
        {
            public string Name;
            public string Base;
            public string Normal;
            public string Metallic;
            public string Roughness;
            public string Emission;
            public string Opacity;
            public float Smoothness = 0.5f;
            public bool ScreenGlow;
        }

        /// <summary>Every material name the models use, and what it is made of.</summary>
        static readonly Dictionary<string, Look> Kit = new Dictionary<string, Look>
        {
            // RoughnessMetallic.png is one flat grey (66 of 255): roughness 0.26 across the kit.
            { "BaseMaterial", new Look { Name = "mvp_Base", Base = "BaseColor.png", Emission = "Emissive.png", Smoothness = 0.74f } },
            { "Chair", new Look { Name = "mvp_Chair", Base = "Chair_BaseColor.png", Normal = "Chair_Normal.png", Metallic = "Chair_Metallic.png", Roughness = "Chair_Roughness.png" } },
            { "PlantColor", new Look { Name = "mvp_Plant", Base = "Plant_BaseColor.jpg", Normal = "Plant_Normal.png", Metallic = "Plant_Metallic.JPEG", Roughness = "Plant_Roughness.JPEG" } },
            { "Tree", new Look { Name = "mvp_Tree", Base = "Tree_BaseColor.jpg", Normal = "Tree_normal.png", Metallic = "Tree_Metallic.JPEG", Roughness = "Tree_Roughness.JPEG" } },
            { "Glass", new Look { Name = "mvp_Glass", Base = "Glass_BaseColor.png", Opacity = "Glass_Opacity.png", Smoothness = 0.9f } },
            { "DeskAccessories", new Look { Name = "mvp_DeskAccessories", Base = "DeskAccessories_BaseColor.png", Emission = "DeskAccessories_Emmisive.png" } },
            { "DesktopScreenReplaceable", new Look { Name = "mvp_DesktopScreen", Base = "DesktopScreenReplaceable.png", ScreenGlow = true, Smoothness = 0.8f } },
            { "ScreenPreview", new Look { Name = "mvp_ScreenPreview", Base = "Screen_Replaceable.png", ScreenGlow = true, Smoothness = 0.8f } },
            { "LargePresentationWallDisplay_Replaceable", new Look { Name = "mvp_PresentationScreen", Base = "PresentationWallScreen.png", ScreenGlow = true, Smoothness = 0.8f } },
            { "Material", new Look { Name = "mvp_LightGrayFloorTile", Base = "LightGrayFloorTile_Base_Color.png", Normal = "LightGrayFloorTile_Base_Color_normal.png", Roughness = "LightGrayFloorTile_roughness.png" } },
            { "WoodFloorTile", new Look { Name = "mvp_WoodFloorTile", Base = "WoodFloorTile_BaseColor.png", Normal = "WoodFloorTile_Normal.png", Roughness = "WoodFloorTile_Roughness.png" } },
        };

        [MenuItem("Tools/Dojo/MVP Kit/1. Build Prefabs")]
        public static void BuildAll()
        {
            Directory.CreateDirectory(PackFolder + "/Prefabs");
            Directory.CreateDirectory(PackFolder + "/Materials");
            Directory.CreateDirectory(MadeTextures);

            var poses = ReadReferencePoses();
            var materials = new Dictionary<string, Material>();

            foreach (var pair in Kit)
            {
                materials[pair.Key] = MakeMaterial(pair.Value);
            }

            var report = new StringBuilder();
            var built = 0;

            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { ModelFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var model = Path.GetFileNameWithoutExtension(path);

                if (Skipped.Contains(model) || Path.GetDirectoryName(path).Replace('\\', '/') != ModelFolder)
                {
                    continue;
                }

                Pose pose;
                if (!poses.TryGetValue(path, out pose))
                {
                    var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    pose = new Pose { Rotation = source.transform.localRotation, Scale = source.transform.localScale };
                    report.Append(model).Append(" is not in the V2 scene - built as its file has it. ");
                }

                var line = Build(path, model, pose, materials);
                if (line != null)
                {
                    built++;
                    report.Append(line).Append(" | ");
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[MVP Kit] Built " + built + " prefab(s): " + report);
        }

        public const string SourceScene = "Assets/Scenes/Runtime.unity";
        public const string MvpScene = "Assets/Scenes/RuntimeMVP.unity";
        public const string MvpWorld = "default/World/world_mvp";
        public const string MvpSaveFile = "world_mvp.json";
        public const float MvpCell = 0.3f;
        const string RoomPrefix = "default_room_mvp_";

        /// <summary>
        /// Makes the MVP scene from the Runtime scene, if there is none yet, and sets it to the kit:
        /// a 0.3 m grid, the MVP default world, its own save file, and an inventory of the kit's
        /// pieces and rooms only.
        /// </summary>
        [MenuItem("Tools/Dojo/MVP Kit/2. Set Up MVP Scene")]
        public static void SetUpScene()
        {
            if (!File.Exists(MvpScene) && !AssetDatabase.CopyAsset(SourceScene, MvpScene))
            {
                Debug.LogError("[MVP Kit] Could not copy " + SourceScene + " to " + MvpScene + ".");
                return;
            }

            var already = SceneManager.GetSceneByPath(MvpScene);
            var opened = !(already.IsValid() && already.isLoaded);
            var scene = opened ? EditorSceneManager.OpenScene(MvpScene, OpenSceneMode.Additive) : already;

            try
            {
                var grids = 0;
                var scopes = 0;

                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var placement in root.GetComponentsInChildren<PlacementGrid>(true))
                    {
                        var grid = placement.GetComponent<Grid>();
                        Undo.RecordObject(grid, "MVP grid");
                        grid.cellSize = new Vector3(MvpCell, MvpCell, grid.cellSize.z);
                        EditorUtility.SetDirty(grid);
                        grids++;
                    }

                    foreach (var scope in root.GetComponentsInChildren<Systems.GameLifetimeScope>(true))
                    {
                        var serialized = new SerializedObject(scope);
                        var settings = serialized.FindProperty("worldSettings");
                        settings.FindPropertyRelative("defaultWorld").stringValue = MvpWorld;
                        settings.FindPropertyRelative("saveFile").stringValue = MvpSaveFile;
                        settings.FindPropertyRelative("resetFloor").stringValue = "default/" + PrefabName("LightGrayFloorTile");

                        var offered = InventoryAddresses();
                        var list = settings.FindPropertyRelative("inventoryOnly");
                        list.arraySize = offered.Count;
                        for (var i = 0; i < offered.Count; i++)
                        {
                            list.GetArrayElementAtIndex(i).stringValue = offered[i];
                        }

                        serialized.ApplyModifiedProperties();
                        scopes++;
                    }
                }

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[MVP Kit] " + MvpScene + ": " + grids + " grid(s) at " + MvpCell + " m, " + scopes
                    + " game scope(s) set to " + MvpWorld + ", saving to " + MvpSaveFile + ", offering "
                    + InventoryAddresses().Count + " piece(s) and room(s).");
            }
            finally
            {
                if (opened)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        /// <summary>What the MVP scene's inventory offers: the kit's pieces and its rooms, by address.</summary>
        public static List<string> InventoryAddresses()
        {
            var addresses = new List<string>();

            foreach (var pair in Pieces())
            {
                addresses.Add("default/" + pair.Value);
            }

            foreach (var file in Directory.GetFiles(PackFolder + "/Rooms", RoomPrefix + "*.json"))
            {
                addresses.Add("default/Rooms/" + Path.GetFileNameWithoutExtension(file));
            }

            addresses.AddRange(Borrowed);
            return addresses;
        }

        /// <summary>
        /// Pieces of the first kit the MVP scene offers for now, until the kit has its own: the
        /// elevator, for trying out floors while building. Take one off and run Set Up MVP Scene.
        /// </summary>
        static readonly string[] Borrowed =
        {
            "default/default_elevator",
        };

        /// <summary>The prefab name for a model: default_ and the model name in snake case.</summary>
        public static string PrefabName(string model)
        {
            string renamed;
            if (Renamed.TryGetValue(model, out renamed))
            {
                return renamed;
            }

            var snake = new StringBuilder("default_");
            var text = model.Replace(" ", string.Empty);

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                var boundary = i > 0 && char.IsUpper(c)
                    && (char.IsLower(text[i - 1]) || (i + 1 < text.Length && char.IsLower(text[i + 1]) && char.IsUpper(text[i - 1])));

                if (boundary)
                {
                    snake.Append('_');
                }

                snake.Append(char.ToLowerInvariant(c));
            }

            return snake.ToString();
        }

        /// <summary>Every model in the kit and the prefab it becomes, in name order.</summary>
        public static List<KeyValuePair<string, string>> Pieces()
        {
            var list = new List<KeyValuePair<string, string>>();

            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { ModelFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var model = Path.GetFileNameWithoutExtension(path);

                if (!Skipped.Contains(model) && Path.GetDirectoryName(path).Replace('\\', '/') == ModelFolder)
                {
                    list.Add(new KeyValuePair<string, string>(model, PrefabName(model)));
                }
            }

            list.Sort((a, b) => string.CompareOrdinal(a.Value, b.Value));
            return list;
        }

        struct Pose
        {
            public Quaternion Rotation;
            public Vector3 Scale;
        }

        /// <summary>The rotation and scale each model stands at in the V2 scene, by model path.</summary>
        static Dictionary<string, Pose> ReadReferencePoses()
        {
            var poses = new Dictionary<string, Pose>();

            if (!File.Exists(ReferenceScene))
            {
                Debug.LogWarning("[MVP Kit] " + ReferenceScene + " is missing, so each model is built as its file has it.");
                return poses;
            }

            var already = SceneManager.GetSceneByPath(ReferenceScene);
            var scene = already.IsValid() && already.isLoaded
                ? already
                : EditorSceneManager.OpenScene(ReferenceScene, OpenSceneMode.Additive);

            try
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(root);
                    var path = source != null ? AssetDatabase.GetAssetPath(source) : null;

                    if (!string.IsNullOrEmpty(path) && !poses.ContainsKey(path))
                    {
                        poses[path] = new Pose { Rotation = root.transform.rotation, Scale = root.transform.lossyScale };
                    }
                }
            }
            finally
            {
                if (!(already.IsValid() && already.isLoaded))
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }

            return poses;
        }

        static string Build(string modelPath, string model, Pose pose, Dictionary<string, Material> materials)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var name = PrefabName(model);
            var instance = Object.Instantiate(source);
            instance.transform.SetPositionAndRotation(Vector3.zero, pose.Rotation);
            instance.transform.localScale = pose.Scale;

            var root = new GameObject(name);

            try
            {
                var renderers = instance.GetComponentsInChildren<MeshRenderer>(true);
                var bounds = new Bounds();
                var measured = false;

                foreach (var renderer in renderers)
                {
                    if (!measured)
                    {
                        bounds = renderer.bounds;
                        measured = true;
                    }
                    else
                    {
                        bounds.Encapsulate(renderer.bounds);
                    }
                }

                if (!measured)
                {
                    Debug.LogError("[MVP Kit] " + model + " has nothing to draw.", source);
                    return null;
                }

                // Pivot at the bottom middle, as every other piece in the pack has it.
                var shift = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);

                foreach (var renderer in renderers)
                {
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null)
                    {
                        continue;
                    }

                    var part = new GameObject(renderer.name + "_mesh");
                    part.transform.SetParent(root.transform, false);
                    part.transform.SetPositionAndRotation(renderer.transform.position + shift, renderer.transform.rotation);
                    part.transform.localScale = renderer.transform.lossyScale;
                    part.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;

                    var from = renderer.sharedMaterials;
                    var to = new Material[from.Length];

                    for (var i = 0; i < from.Length; i++)
                    {
                        Material made;
                        var key = from[i] != null ? from[i].name : string.Empty;

                        if (!materials.TryGetValue(key, out made))
                        {
                            Debug.LogWarning("[MVP Kit] " + model + " uses material '" + key + "', which the kit has no look for; it gets the base one.", source);
                            made = materials["BaseMaterial"];
                        }

                        to[i] = made;
                    }

                    part.AddComponent<MeshRenderer>().sharedMaterials = to;
                }

                var box = root.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, bounds.size.y * 0.5f, 0f);
                box.size = bounds.size;

                if (Floors.Contains(model))
                {
                    root.AddComponent<FloorPiece>();
                }

                var prefabPath = PackFolder + "/Prefabs/" + name + ".prefab";
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);

                return name + " " + bounds.size.x.ToString("0.00") + "x" + bounds.size.y.ToString("0.00") + "x" + bounds.size.z.ToString("0.00");
            }
            finally
            {
                Object.DestroyImmediate(instance);
                Object.DestroyImmediate(root);
            }
        }

        static Material MakeMaterial(Look look)
        {
            // Every texture made or reimported first: an import refreshes the asset database, and a
            // refresh in the middle drops what was set on the material before it.
            var baseMap = look.Opacity != null
                ? WithAlpha(look.Base, look.Opacity, look.Name + "_base")
                : Texture(look.Base, false);
            var normalMap = look.Normal != null ? Texture(look.Normal, true) : null;
            var metallicMap = look.Roughness != null
                ? MetallicSmoothness(look.Metallic, look.Roughness, look.Name + "_metallicSmoothness")
                : null;
            var emission = look.ScreenGlow ? baseMap : look.Emission != null ? Texture(look.Emission, false) : null;

            var path = PackFolder + "/Materials/" + look.Name + ".mat";
            var shader = Shader.Find("Dojo/Fade Lit") ?? Shader.Find("Universal Render Pipeline/Lit");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", look.Smoothness);
            material.SetTexture("_BumpMap", null);
            material.DisableKeyword("_NORMALMAP");
            material.SetTexture("_MetallicGlossMap", null);
            material.DisableKeyword("_METALLICSPECGLOSSMAP");
            material.SetTexture("_EmissionMap", null);
            material.SetColor("_EmissionColor", Color.black);
            material.DisableKeyword("_EMISSION");

            material.SetTexture("_BaseMap", baseMap);

            if (normalMap != null)
            {
                material.SetTexture("_BumpMap", normalMap);
                material.EnableKeyword("_NORMALMAP");
            }

            if (metallicMap != null)
            {
                material.SetTexture("_MetallicGlossMap", metallicMap);
                material.EnableKeyword("_METALLICSPECGLOSSMAP");
                material.SetFloat("_Smoothness", 1f);
            }

            if (emission != null)
            {
                material.SetTexture("_EmissionMap", emission);
                material.SetColor("_EmissionColor", Color.white);
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            if (look.Opacity != null)
            {
                Transparent(material);
            }
            else
            {
                Opaque(material);
            }

            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }

        static void Transparent(Material material)
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.SetOverrideTag("RenderType", "Transparent");
        }

        static void Opaque(Material material)
        {
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
            material.SetFloat("_ZWrite", 1f);
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = -1;
            material.SetOverrideTag("RenderType", "Opaque");
        }

        static Texture2D Texture(string file, bool normal)
        {
            var path = TextureFolder + "/" + file;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;

            if (importer == null)
            {
                Debug.LogWarning("[MVP Kit] There is no texture at " + path + ".");
                return null;
            }

            if (normal && importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>URP's metallic map: metal in red, smoothness (one minus roughness) in alpha.</summary>
        static Texture2D MetallicSmoothness(string metallicFile, string roughnessFile, string name)
        {
            var rough = Read(roughnessFile);
            var metal = metallicFile != null ? Read(metallicFile) : null;
            var width = rough.width;
            var height = rough.height;
            var r = rough.GetPixels32();
            var m = metal != null ? Fit(metal, width, height).GetPixels32() : null;
            var o = new Color32[r.Length];

            for (var i = 0; i < r.Length; i++)
            {
                var metallic = m != null ? m[i].r : (byte)0;
                o[i] = new Color32(metallic, metallic, metallic, (byte)(255 - r[i].r));
            }

            return Write(o, width, height, name, false);
        }

        /// <summary>The base colour with the opacity map in its alpha.</summary>
        static Texture2D WithAlpha(string baseFile, string opacityFile, string name)
        {
            var colour = Read(baseFile);
            var opacity = Fit(Read(opacityFile), colour.width, colour.height);
            var c = colour.GetPixels32();
            var a = opacity.GetPixels32();

            for (var i = 0; i < c.Length; i++)
            {
                c[i].a = a[i].r;
            }

            return Write(c, colour.width, colour.height, name, true);
        }

        static Texture2D Read(string file)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            texture.LoadImage(File.ReadAllBytes(TextureFolder + "/" + file));
            return texture;
        }

        static Texture2D Fit(Texture2D texture, int width, int height)
        {
            if (texture.width == width && texture.height == height)
            {
                return texture;
            }

            var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(texture, target);
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var fitted = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            fitted.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            fitted.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            return fitted;
        }

        static Texture2D Write(Color32[] pixels, int width, int height, string name, bool colour)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            texture.SetPixels32(pixels);
            texture.Apply();

            var path = MadeTextures + "/" + name + ".png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.sRGBTexture = colour;
            importer.alphaIsTransparency = colour;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
