using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        /// <summary>
        /// Models that are not one piece. Empty since Harold's 16 Nov Elevator.fbx (3dDump 36b872c):
        /// it used to hold the whole kit, and is now the elevator alone.
        /// </summary>
        static readonly HashSet<string> Skipped = new HashSet<string>();

        /// <summary>
        /// Models that are an elevator stop, so they get an <see cref="ElevatorPiece"/>: the game then
        /// shows one per floor and lets the manager ride between them.
        /// </summary>
        static readonly HashSet<string> Elevators = new HashSet<string> { "Elevator" };

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
            // Not default_elevator: that address is the first kit's elevator, which saved worlds
            // and the inventory use, and building over it would replace that prefab.
            { "Elevator", "default_elevator_mvp" },
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

            /// <summary>The material's Metallic value when it has no metallic map. Kept by every build.</summary>
            public float MetallicValue;

            /// <summary>
            /// For a look with a roughness map: the least smoothness anywhere. The map's own smoothness
            /// (1 - roughness) is scaled into the range above it, so its grain stays but the surface
            /// reads as polished. 0 keeps the map as it is.
            /// </summary>
            public float GlossFloor;
            public bool ScreenGlow;
        }

        /// <summary>Every material name the models use, and what it is made of.</summary>
        static readonly Dictionary<string, Look> Kit = new Dictionary<string, Look>
        {
            // RoughnessMetallic.png is one flat grey (66 of 255): roughness 0.26 across the kit.
            // Metallic 0.5: the kit's painted-metal look (set by hand on 26 Nov, kept here so a build
            // no longer puts it back to 0).
            { "BaseMaterial", new Look { Name = "mvp_Base", Base = "BaseColor.png", Emission = "Emissive.png", Smoothness = 0.74f, MetallicValue = 0.5f } },
            { "Chair", new Look { Name = "mvp_Chair", Base = "Chair_BaseColor.png", Normal = "Chair_Normal.png", Metallic = "Chair_Metallic.png", Roughness = "Chair_Roughness.png" } },
            { "PlantColor", new Look { Name = "mvp_Plant", Base = "Plant_BaseColor.jpg", Normal = "Plant_Normal.png", Metallic = "Plant_Metallic.JPEG", Roughness = "Plant_Roughness.JPEG" } },
            { "Tree", new Look { Name = "mvp_Tree", Base = "Tree_BaseColor.jpg", Normal = "Tree_normal.png", Metallic = "Tree_Metallic.JPEG", Roughness = "Tree_Roughness.JPEG" } },
            { "Glass", new Look { Name = "mvp_Glass", Base = "Glass_BaseColor.png", Opacity = "Glass_Opacity.png", Smoothness = 0.9f } },
            { "DeskAccessories", new Look { Name = "mvp_DeskAccessories", Base = "DeskAccessories_BaseColor.png", Emission = "DeskAccessories_Emmisive.png" } },
            { "DesktopScreenReplaceable", new Look { Name = "mvp_DesktopScreen", Base = "DesktopScreenReplaceable.png", ScreenGlow = true, Smoothness = 0.8f } },
            { "ScreenPreview", new Look { Name = "mvp_ScreenPreview", Base = "Screen_Replaceable.png", ScreenGlow = true, Smoothness = 0.8f } },
            { "LargePresentationWallDisplay_Replaceable", new Look { Name = "mvp_PresentationScreen", Base = "PresentationWallScreen.png", ScreenGlow = true, Smoothness = 0.8f } },
            // The floors' roughness maps alone leave them matte (smoothness 0.2-0.3); GlossFloor 0.5
            // makes them polished (about 0.5-0.8) with the maps' grain kept (27 Nov).
            { "Material", new Look { Name = "mvp_LightGrayFloorTile", Base = "LightGrayFloorTile_Base_Color.png", Normal = "LightGrayFloorTile_Base_Color_normal.png", Roughness = "LightGrayFloorTile_roughness.png", GlossFloor = 0.5f } },
            { "WoodFloorTile", new Look { Name = "mvp_WoodFloorTile", Base = "WoodFloorTile_BaseColor.png", Normal = "WoodFloorTile_Normal.png", Roughness = "WoodFloorTile_Roughness.png", GlossFloor = 0.5f } },
        };

        [MenuItem("Tools/Dojo/Content/1. Build MVP Kit Prefabs", false, 1)]
        public static void BuildAll() => Build(null);

        /// <summary>
        /// Builds the prefabs of <paramref name="only"/> (model names), or of every model when null.
        /// Returns how many were built.
        /// </summary>
        public static int Build(ICollection<string> only)
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

                if (KindOf(model) == KitPieceKind.Skip || Path.GetDirectoryName(path).Replace('\\', '/') != ModelFolder
                    || (only != null && !only.Contains(model)))
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
            return built;
        }

        public const string MvpScene = "Assets/Scenes/RuntimeMVP.unity";
        public const string MvpWorld = "default/World/world_mvp";
        public const string MvpSaveFile = "world_mvp.json";
        public const float MvpCell = 0.3f;
        const string RoomPrefix = "default_room_mvp_";

        /// <summary>
        /// Sets the MVP scene to the kit: a 0.3 m grid, the MVP default world, its own save file,
        /// and an inventory of the kit's pieces and rooms only. Run it after new pieces come in.
        /// </summary>
        /// <remarks>
        /// It used to make the MVP scene from the Runtime scene; that scene went with the old
        /// assets (18 Nov), so the MVP scene has to be there.
        /// </remarks>
        [MenuItem("Tools/Dojo/Content/2. Update MVP Scene Inventory", false, 2)]
        public static void SetUpScene()
        {
            if (!File.Exists(MvpScene))
            {
                Debug.LogError("[MVP Kit] There is no " + MvpScene + " to set up.");
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

                        // The You and Agents tabs, so the default world gets its owner's and agents'
                        // places: whoever builds it puts them down (the game fills them with the
                        // player's own).
                        settings.FindPropertyRelative("characterTabs").boolValue = true;

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

            // Every room on the pack's list: the kit's own and every room promoted since.
            foreach (var file in Directory.GetFiles(PackFolder + "/Rooms", "default_room_*.json"))
            {
                addresses.Add("default/Rooms/" + Path.GetFileNameWithoutExtension(file));
            }

            addresses.AddRange(Borrowed);
            return addresses;
        }

        /// <summary>
        /// Pieces of the first kit the MVP scene offers for now, until the kit has its own (Jacob, 16
        /// Nov): the knowledge shelf, for the knowledge base; the supercomputers, which the
        /// integration terminals stand on. Stairs are dropped, and the elevator is the kit's own
        /// now, default_elevator_mvp (Bob, 16 Nov). Take one off and run Set Up MVP Scene.
        /// <para>
        /// Empty since 19 Nov: the display shelf holds the books and the server rack is the
        /// terminal now, so the old shelf and the supercomputers left the list (fd95808). The
        /// content keeps them.
        /// </para>
        /// </summary>
        static readonly string[] Borrowed = { };

        /// <summary>The prefab name for a model: default_ and the model name in snake case.</summary>
        public static string PrefabName(string model)
        {
            var named = NameFor(model);

            // A wall is known by its name ("wall", "partition" or "railing"): a piece built as one
            // gets "_wall" on the end when its name doesn't already say so.
            if (KindOf(model) == KitPieceKind.Wall && !RoomOpenings.IsWall(named))
            {
                named += "_wall";
            }

            return named;
        }

        /// <summary>The name before the wall rule: the settings' own, the kit's renames, or default_ and snake case.</summary>
        static string NameFor(string model)
        {
            var settings = MvpKitSettings.Load(false);
            var piece = settings != null ? settings.Find(model) : null;
            if (piece != null && !string.IsNullOrWhiteSpace(piece.prefabName))
            {
                var given = piece.prefabName.Trim().ToLowerInvariant().Replace(' ', '_');
                return given.StartsWith("default_", StringComparison.Ordinal) ? given : "default_" + given;
            }

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

        /// <summary>
        /// What <paramref name="model"/> is built as: the settings' choice, or worked out from the
        /// kit's lists and its name when that is Auto.
        /// </summary>
        public static KitPieceKind KindOf(string model)
        {
            var settings = MvpKitSettings.Load(false);
            var piece = settings != null ? settings.Find(model) : null;
            if (piece != null && piece.kind != KitPieceKind.Auto)
            {
                return piece.kind;
            }

            if (Skipped.Contains(model)) return KitPieceKind.Skip;
            if (Elevators.Contains(model)) return KitPieceKind.Elevator;
            if (Floors.Contains(model)) return KitPieceKind.Floor;
            if (model.EndsWith("Chair", StringComparison.Ordinal)) return KitPieceKind.Chair;
            if (RoomOpenings.IsWall(NameFor(model))) return KitPieceKind.Wall;
            return KitPieceKind.Furniture;
        }

        /// <summary>Every model file in the kit's folder, in name order, whatever it is built as.</summary>
        public static List<string> Models()
        {
            var list = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { ModelFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetDirectoryName(path).Replace('\\', '/') == ModelFolder)
                {
                    list.Add(Path.GetFileNameWithoutExtension(path));
                }
            }

            list.Sort(string.CompareOrdinal);
            return list;
        }

        /// <summary>Where <paramref name="model"/>'s prefab is, built or not yet.</summary>
        public static string PrefabPath(string model) => PackFolder + "/Prefabs/" + PrefabName(model) + ".prefab";

        /// <summary>
        /// Writes the settings' inventory tabs into the pack's items.json, over the Addressable
        /// Generator's guesses. Returns how many it changed.
        /// </summary>
        public static int ApplyCategories()
        {
            var settings = MvpKitSettings.Load(false);
            const string manifest = "Assets/Dojo/Content/Packs/default/items.json";
            if (settings == null || !File.Exists(manifest))
            {
                return 0;
            }

            var text = File.ReadAllText(manifest);
            var changed = 0;

            foreach (var piece in settings.pieces)
            {
                if (piece == null || string.IsNullOrWhiteSpace(piece.category) || string.IsNullOrEmpty(piece.model))
                {
                    continue;
                }

                var category = piece.category.Trim();
                var pattern = "(\"category\"\\s*:\\s*\")([^\"]*)(\"\\s*,\\s*\"name\"\\s*:\\s*\""
                    + System.Text.RegularExpressions.Regex.Escape(PrefabName(piece.model)) + "\")";
                text = System.Text.RegularExpressions.Regex.Replace(text, pattern, m =>
                {
                    if (m.Groups[2].Value == category)
                    {
                        return m.Value;
                    }

                    changed++;
                    return m.Groups[1].Value + category + m.Groups[3].Value;
                });
            }

            if (changed > 0)
            {
                File.WriteAllText(manifest, text);
                AssetDatabase.ImportAsset(manifest);
            }

            return changed;
        }

        /// <summary>
        /// A material for one of a new model's own materials: Dojo/Fade Lit, with the colour, maps
        /// and glow its file brought in, saved beside the kit's. For a material the kit has no
        /// hand-made look for.
        /// </summary>
        static Material OwnMaterial(Material from, string model)
        {
            var name = "mvp_" + model.Replace(" ", string.Empty) + "_" + from.name.Replace(" ", "_");
            var path = PackFolder + "/Materials/" + name + ".mat";
            var shader = Shader.Find("Dojo/Fade Lit") ?? Shader.Find("Universal Render Pipeline/Lit");
            var made = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (made == null)
            {
                made = new Material(shader);
                AssetDatabase.CreateAsset(made, path);
            }
            else
            {
                made.shader = shader;
            }

            Texture Map(params string[] names)
            {
                foreach (var n in names)
                {
                    if (from.HasProperty(n) && from.GetTexture(n) != null) return from.GetTexture(n);
                }

                return null;
            }

            Color Colour(Color fallback, params string[] names)
            {
                foreach (var n in names)
                {
                    if (from.HasProperty(n)) return from.GetColor(n);
                }

                return fallback;
            }

            made.SetTexture("_BaseMap", Map("_BaseMap", "_MainTex"));
            made.SetColor("_BaseColor", Colour(Color.white, "_BaseColor", "_Color"));

            var normal = Map("_BumpMap", "_NormalMap");
            made.SetTexture("_BumpMap", normal);
            if (normal != null) made.EnableKeyword("_NORMALMAP"); else made.DisableKeyword("_NORMALMAP");

            var gloss = Map("_MetallicGlossMap");
            made.SetTexture("_MetallicGlossMap", gloss);
            if (gloss != null) made.EnableKeyword("_METALLICSPECGLOSSMAP"); else made.DisableKeyword("_METALLICSPECGLOSSMAP");
            made.SetFloat("_Metallic", from.HasProperty("_Metallic") ? from.GetFloat("_Metallic") : 0f);
            made.SetFloat("_Smoothness", from.HasProperty("_Smoothness") ? from.GetFloat("_Smoothness")
                : from.HasProperty("_Glossiness") ? from.GetFloat("_Glossiness") : 0.5f);

            var glowMap = Map("_EmissionMap");
            var glow = Colour(Color.black, "_EmissionColor");
            made.SetTexture("_EmissionMap", glowMap);
            made.SetColor("_EmissionColor", glowMap != null && glow.maxColorComponent <= 0f ? Color.white : glow);
            if (glowMap != null || glow.maxColorComponent > 0f)
            {
                made.EnableKeyword("_EMISSION");
                made.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                made.DisableKeyword("_EMISSION");
            }

            var seeThrough = from.renderQueue >= (int)UnityEngine.Rendering.RenderQueue.Transparent
                             || (from.HasProperty("_Surface") && from.GetFloat("_Surface") > 0.5f);
            if (seeThrough) Transparent(made); else Opaque(made);

            EditorUtility.SetDirty(made);
            AssetDatabase.SaveAssetIfDirty(made);
            Debug.Log("[MVP Kit] " + model + ": its material '" + from.name + "' became " + path + ", from the model's own textures.");
            return made;
        }

        /// <summary>Every model in the kit and the prefab it becomes, in name order.</summary>
        public static List<KeyValuePair<string, string>> Pieces()
        {
            var list = new List<KeyValuePair<string, string>>();

            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { ModelFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var model = Path.GetFileNameWithoutExtension(path);

                if (KindOf(model) != KitPieceKind.Skip && Path.GetDirectoryName(path).Replace('\\', '/') == ModelFolder)
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
            var name = PrefabName(model);

            // Found before the model is copied out: making the clip loop reimports the model.
            var clip = LoopingClip(modelPath, model);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var instance = Object.Instantiate(source);
            instance.transform.SetPositionAndRotation(Vector3.zero, pose.Rotation);
            instance.transform.localScale = pose.Scale;

            var root = new GameObject(name);

            try
            {
                // Skinned meshes too: built static, in the pose they were modelled in, unless the
                // piece's settings name a clip to loop.
                var renderers = instance.GetComponentsInChildren<Renderer>(true)
                    .Where(r => r is MeshRenderer || r is SkinnedMeshRenderer).ToArray();
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

                Material[] KitMaterials(Material[] from)
                {
                    var to = new Material[from.Length];

                    for (var i = 0; i < from.Length; i++)
                    {
                        Material made;
                        var key = from[i] != null ? from[i].name : string.Empty;

                        if (!materials.TryGetValue(key, out made))
                        {
                            // A material the kit has no look for: made from the model's own, with the
                            // textures its file points at, so a new model keeps its colours.
                            made = from[i] != null ? OwnMaterial(from[i], model) : materials["BaseMaterial"];
                        }

                        to[i] = made;
                    }

                    return to;
                }

                if (clip != null)
                {
                    // A piece with a clip to loop (the Supercomputer's sphere): the model is kept whole,
                    // so the clip still finds the parts it moves by their names, under an Animator.
                    instance.name = model;
                    instance.transform.SetParent(root.transform, true);
                    instance.transform.position += shift;

                    foreach (var renderer in renderers)
                    {
                        renderer.sharedMaterials = KitMaterials(renderer.sharedMaterials);
                    }

                    var animator = instance.GetComponent<Animator>();
                    if (animator == null)
                    {
                        animator = instance.AddComponent<Animator>();
                    }

                    animator.runtimeAnimatorController = LoopController(clip, name);
                    animator.applyRootMotion = false;
                    animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                }
                else
                {
                    foreach (var renderer in renderers)
                    {
                        var skin = renderer as SkinnedMeshRenderer;
                        var filter = renderer.GetComponent<MeshFilter>();
                        var mesh = skin != null ? skin.sharedMesh : filter != null ? filter.sharedMesh : null;
                        if (mesh == null)
                        {
                            continue;
                        }

                        var part = new GameObject(renderer.name + "_mesh");
                        part.transform.SetParent(root.transform, false);
                        part.transform.SetPositionAndRotation(renderer.transform.position + shift, renderer.transform.rotation);
                        part.transform.localScale = renderer.transform.lossyScale;
                        part.AddComponent<MeshFilter>().sharedMesh = mesh;
                        part.AddComponent<MeshRenderer>().sharedMaterials = KitMaterials(renderer.sharedMaterials);
                    }
                }

                var size = bounds.size;

                var kind = KindOf(model);

                if (kind == KitPieceKind.Elevator)
                {
                    // Harold's elevator has its doors on +Z; the game's elevators open on -X
                    // (ElevatorPiece), where the manager stands to ride. A quarter turn about Y.
                    foreach (Transform part in root.transform)
                    {
                        part.RotateAround(Vector3.zero, Vector3.up, -90f);
                    }

                    size = new Vector3(size.z, size.y, size.x);
                }

                var box = root.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, size.y * 0.5f, 0f);
                box.size = size;

                if (kind == KitPieceKind.Floor)
                {
                    root.AddComponent<FloorPiece>();
                }

                if (kind == KitPieceKind.Elevator)
                {
                    root.AddComponent<ElevatorPiece>();
                }

                string seat = null;
                if (kind == KitPieceKind.Chair)
                {
                    seat = AddSeat(root, bounds.size);
                }

                var prefabPath = PackFolder + "/Prefabs/" + name + ".prefab";
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);

                return name + " " + bounds.size.x.ToString("0.00") + "x" + bounds.size.y.ToString("0.00") + "x" + bounds.size.z.ToString("0.00")
                    + (seat != null ? " " + seat : string.Empty);
            }
            finally
            {
                Object.DestroyImmediate(instance);
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// The clip the settings say <paramref name="model"/> loops, set to loop in the model's import
        /// settings; null when it names none, or names one the model doesn't have.
        /// </summary>
        static AnimationClip LoopingClip(string modelPath, string model)
        {
            var settings = MvpKitSettings.Load(false);
            var piece = settings != null ? settings.Find(model) : null;
            var wanted = piece != null && !string.IsNullOrWhiteSpace(piece.animation) ? piece.animation.Trim() : null;
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (wanted == null || importer == null || !importer.importAnimation)
            {
                return null;
            }

            var clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
            var entry = clips.FirstOrDefault(c => c.name == wanted);
            if (entry == null)
            {
                Debug.LogWarning("[MVP Kit] " + model + " has no clip '" + wanted + "', so it is built static. Its clips: "
                    + string.Join(", ", clips.Select(c => c.name)));
                return null;
            }

            if (!entry.loopTime)
            {
                entry.loopTime = true;
                importer.clipAnimations = clips;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>().FirstOrDefault(c => c.name == wanted);
        }

        /// <summary>An Animator Controller that plays <paramref name="clip"/> and nothing else, saved in the pack.</summary>
        static RuntimeAnimatorController LoopController(AnimationClip clip, string name)
        {
            Directory.CreateDirectory(PackFolder + "/Animations");
            var path = PackFolder + "/Animations/" + name + ".controller";
            var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(path);

            if (controller == null)
            {
                return UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPathWithClip(path, clip);
            }

            // Rebuilt in place, so the controller keeps its GUID and anything pointing at it.
            var state = controller.layers[0].stateMachine.defaultState;
            state.motion = clip;
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);
            return controller;
        }

        /// <summary>
        /// Makes a chair sittable: a <see cref="Components.Chair"/> with a seat anchor on the cushion,
        /// facing away from the backrest. Without one, agents and the manager never sit on the kit's
        /// chairs.
        /// </summary>
        /// <remarks>
        /// Measured from the mesh rather than typed in, since every chair model is shaped differently:
        /// the backrest is what rises above the top third, so the seat faces away from it; the cushion
        /// is the highest point in the front half that is still below the top third.
        /// </remarks>
        static string AddSeat(GameObject root, Vector3 size)
        {
            var points = new List<Vector3>();
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
            {
                foreach (var vertex in filter.sharedMesh.vertices)
                {
                    points.Add(root.transform.InverseTransformPoint(filter.transform.TransformPoint(vertex)));
                }
            }

            var high = size.y * 0.66f;
            var back = Vector3.zero;
            var count = 0;
            foreach (var p in points)
            {
                if (p.y > high)
                {
                    back += new Vector3(p.x, 0f, p.z);
                    count++;
                }
            }

            var forward = count > 0 && back.sqrMagnitude / (count * count) > 0.0004f
                ? -(back / count).normalized
                : Vector3.forward;

            // The middle strip only, so armrests at the sides are not taken for the cushion.
            var cushion = 0f;
            var right = Vector3.Cross(Vector3.up, forward);
            var reach = Mathf.Max(size.x, size.z) * 0.35f;
            var strip = Mathf.Min(size.x, size.z) * 0.15f;
            foreach (var p in points)
            {
                var flat = new Vector3(p.x, 0f, p.z);
                var ahead = Vector3.Dot(flat, forward);
                if (p.y < high && ahead >= 0f && ahead < reach && Mathf.Abs(Vector3.Dot(flat, right)) < strip && p.y > cushion)
                {
                    cushion = p.y;
                }
            }

            if (cushion <= 0f)
            {
                cushion = size.y * 0.42f;
            }

            var anchor = new GameObject("SeatAnchor").transform;
            anchor.SetParent(root.transform, false);
            anchor.localPosition = new Vector3(0f, cushion, 0f);
            anchor.localRotation = Quaternion.LookRotation(forward, Vector3.up);

            var chair = root.AddComponent<Components.Chair>();
            var serialized = new SerializedObject(chair);
            serialized.FindProperty("seatAnchor").objectReferenceValue = anchor;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return "seat " + cushion.ToString("0.00") + "m facing " + forward.ToString("F2");
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
                ? MetallicSmoothness(look.Metallic, look.Roughness, look.Name + "_metallicSmoothness", look.GlossFloor)
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
            material.SetFloat("_Metallic", look.MetallicValue);
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
        static Texture2D MetallicSmoothness(string metallicFile, string roughnessFile, string name, float glossFloor = 0f)
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
                var smooth = 255 - r[i].r;
                if (glossFloor > 0f)
                {
                    smooth = Mathf.RoundToInt(255f * glossFloor + smooth * (1f - glossFloor));
                }

                o[i] = new Color32(metallic, metallic, metallic, (byte)Mathf.Clamp(smooth, 0, 255));
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
