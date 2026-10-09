using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Dojo.Game.Editor
{
    /// <summary>
    /// Turns a placeholder model into a default-pack prefab, laid out the way the pack's others
    /// are: an empty root named for the piece, a <c>_mesh</c> child per part, a box collider round
    /// the whole, and URP materials and textures filed under <c>Process/default</c>.
    /// </summary>
    /// <remarks>
    /// Textures are found by the model's own naming: <c>&lt;model&gt;_albedo</c>, <c>_normal</c>,
    /// <c>_metallicRoughness</c> and, for a glowing part, <c>_faces</c>, all in the model folder's
    /// <c>Textures</c>. A material whose name has "face" in it is that glowing part - the faces
    /// texture as both its colour and its emission; every other material gets the rest.
    /// <para>
    /// The metallic-roughness map is glTF's packing - roughness in green, metal in blue - which URP
    /// cannot read, so a converted copy is written beside it: metal in red, smoothness in alpha.
    /// </para>
    /// <para>
    /// Run <c>Tools ▸ Dojo ▸ Addressable Generator</c> on the default pack afterwards: that is what
    /// gives the prefab its address, its thumbnail and its place in the inventory.
    /// </para>
    /// </remarks>
    public static class PackPrefabBuilder
    {
        const string ModelFolder = "Assets/Dojo/Art/3D/placeholders/officeSetup";
        const string PackFolder = "Assets/Process/default";

        /// <summary>The three supercomputer placeholders, built in one go.</summary>
        static readonly string[] SuperComputers =
        {
            "default_superComputer",
            "default_superComputer_2",
            "default_superComputer_3",
        };

        [MenuItem("Tools/Dojo/Build Super Computer Prefabs")]
        public static void BuildSuperComputers()
        {
            var built = 0;

            foreach (var name in SuperComputers)
            {
                if (Build(ModelFolder + "/" + name + ".fbx") != null)
                {
                    built++;
                }
            }

            Debug.Log("[PackPrefab] Built " + built + " of " + SuperComputers.Length + " supercomputer prefab(s).");
        }

        [MenuItem("Assets/Dojo/Build Default Pack Prefab", true)]
        static bool CanBuildSelected()
        {
            return Selection.activeObject != null
                && AssetDatabase.GetAssetPath(Selection.activeObject).EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase);
        }

        [MenuItem("Assets/Dojo/Build Default Pack Prefab")]
        static void BuildSelected()
        {
            Build(AssetDatabase.GetAssetPath(Selection.activeObject));
        }

        /// <summary>Builds or rebuilds the pack prefab for one model. Returns its path, or null.</summary>
        public static string Build(string modelPath)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);

            if (model == null)
            {
                Debug.LogError("[PackPrefab] There is no model at " + modelPath + ".");
                return null;
            }

            var name = Path.GetFileNameWithoutExtension(modelPath);
            var textureFolder = Path.GetDirectoryName(modelPath).Replace('\\', '/') + "/Textures";

            Directory.CreateDirectory(PackFolder + "/Textures");
            Directory.CreateDirectory(PackFolder + "/Materials");
            Directory.CreateDirectory(PackFolder + "/Prefabs");

            var albedo = CopyTexture(textureFolder, name + "_albedo", false);
            var normal = CopyTexture(textureFolder, name + "_normal", true);
            var faces = CopyTexture(textureFolder, name + "_faces", false);
            var metallic = ConvertMetallicRoughness(textureFolder, name);

            var instance = Object.Instantiate(model);
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;

            var root = new GameObject(name);
            var materials = new Dictionary<string, Material>();

            try
            {
                var bounds = new Bounds();
                var measured = false;

                foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var filter = renderer.GetComponent<MeshFilter>();

                    if (filter == null || filter.sharedMesh == null)
                    {
                        continue;
                    }

                    var part = new GameObject(renderer.name + "_mesh");
                    part.transform.SetParent(root.transform, false);
                    part.transform.SetPositionAndRotation(renderer.transform.position, renderer.transform.rotation);
                    part.transform.localScale = renderer.transform.lossyScale;

                    part.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;

                    var source = renderer.sharedMaterials;
                    var assigned = new Material[source.Length];

                    for (var i = 0; i < source.Length; i++)
                    {
                        var key = source[i] != null ? source[i].name : name + "_body";

                        Material material;
                        if (!materials.TryGetValue(key, out material))
                        {
                            material = MakeMaterial(key, albedo, normal, metallic, faces);
                            materials[key] = material;
                        }

                        assigned[i] = material;
                    }

                    part.AddComponent<MeshRenderer>().sharedMaterials = assigned;

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
                    Debug.LogError("[PackPrefab] '" + name + "' has no meshes to build a prefab from.", model);
                    return null;
                }

                // One box round every part, as the other pieces have: what picks it up, what it
                // stands on, and what it is measured by when it is placed.
                var box = root.AddComponent<BoxCollider>();
                box.center = bounds.center;
                box.size = bounds.size;

                var prefabPath = PackFolder + "/Prefabs/" + name + ".prefab";
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);

                // After the save, never before: reimporting the model can rebuild its meshes, and
                // the parts above still point at the ones it has now.
                RemapModelMaterials(modelPath, materials);

                Debug.Log("[PackPrefab] Built " + prefabPath + " - " + root.transform.childCount + " part(s), "
                    + materials.Count + " material(s), " + bounds.size.x.ToString("0.00") + " x "
                    + bounds.size.y.ToString("0.00") + " x " + bounds.size.z.ToString("0.00")
                    + " m. Run Tools > Dojo > Addressable Generator on the default pack to give it an "
                    + "address, a thumbnail and an inventory entry.", AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));

                return prefabPath;
            }
            finally
            {
                Object.DestroyImmediate(instance);
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>A URP Lit material for one of the model's materials, written to the pack.</summary>
        static Material MakeMaterial(string materialName, Texture2D albedo, Texture2D normal, Texture2D metallic, Texture2D faces)
        {
            var path = PackFolder + "/Materials/" + materialName + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Lit");

            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            var glowing = materialName.ToLowerInvariant().Contains("face") && faces != null;

            if (glowing)
            {
                material.SetTexture("_BaseMap", faces);
                material.SetTexture("_EmissionMap", faces);
                material.SetColor("_EmissionColor", Color.white);
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                material.SetFloat("_Smoothness", 0.6f);
            }
            else
            {
                material.SetTexture("_BaseMap", albedo);

                if (normal != null)
                {
                    material.SetTexture("_BumpMap", normal);
                    material.EnableKeyword("_NORMALMAP");
                }

                if (metallic != null)
                {
                    material.SetTexture("_MetallicGlossMap", metallic);
                    material.SetFloat("_Smoothness", 1f);
                    material.EnableKeyword("_METALLICSPECGLOSSMAP");
                }
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Copies one texture into the pack, as a normal map when it is one. Null when the model has none.</summary>
        static Texture2D CopyTexture(string folder, string textureName, bool isNormal)
        {
            var source = folder + "/" + textureName + ".png";

            if (!File.Exists(source))
            {
                return null;
            }

            var target = PackFolder + "/Textures/" + textureName + ".png";

            if (!File.Exists(target))
            {
                AssetDatabase.CopyAsset(source, target);
            }

            if (isNormal)
            {
                var importer = AssetImporter.GetAtPath(target) as TextureImporter;

                if (importer != null && importer.textureType != TextureImporterType.NormalMap)
                {
                    importer.textureType = TextureImporterType.NormalMap;
                    importer.SaveAndReimport();
                }
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(target);
        }

        /// <summary>
        /// The glTF metallic-roughness map repacked the way URP reads it: metal in red, smoothness
        /// (one minus roughness) in alpha. Null when the model has none.
        /// </summary>
        static Texture2D ConvertMetallicRoughness(string folder, string modelName)
        {
            var source = folder + "/" + modelName + "_metallicRoughness.png";

            if (!File.Exists(source))
            {
                return null;
            }

            var target = PackFolder + "/Textures/" + modelName + "_metallicSmoothness.png";
            var read = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);

            try
            {
                read.LoadImage(File.ReadAllBytes(source));
                var pixels = read.GetPixels32();

                for (var i = 0; i < pixels.Length; i++)
                {
                    var p = pixels[i];
                    pixels[i] = new Color32(p.b, 0, 0, (byte)(255 - p.g));
                }

                read.SetPixels32(pixels);
                read.Apply(false, false);
                File.WriteAllBytes(target, read.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(read);
            }

            AssetDatabase.ImportAsset(target);

            // Data, not colour: read as linear so the metal and smoothness values are what was written.
            var importer = AssetImporter.GetAtPath(target) as TextureImporter;
            if (importer != null && importer.sRGBTexture)
            {
                importer.sRGBTexture = false;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(target);
        }

        /// <summary>
        /// Points the model's own materials at the pack's, as the monitor's model already is, so
        /// the model itself previews the way the prefab looks.
        /// </summary>
        static void RemapModelMaterials(string modelPath, Dictionary<string, Material> materials)
        {
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;

            if (importer == null)
            {
                return;
            }

            foreach (var pair in materials)
            {
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key), pair.Value);
            }

            importer.SaveAndReimport();
        }
    }
}
