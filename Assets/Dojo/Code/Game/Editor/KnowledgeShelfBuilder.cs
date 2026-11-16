using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Dojo.Game.Editor
{
    /// <summary>
    /// Builds the default pack's knowledge shelf: a primitive bookcase standing for the company's
    /// knowledge (vision §15), to be swapped for final art later without changing its address.
    /// </summary>
    /// <remarks>
    /// What the game relies on, and what final art must keep:
    /// <list type="bullet">
    /// <item>Address <c>default/default_knowledge_shelf</c> (item id <c>default.defaultknowledgeshelf</c>,
    /// thumbnail <c>default/Thumbnails/default_knowledge_shelf</c>), from the prefab's name.</item>
    /// <item>The footprint: 2 m wide by 0.5 m deep - 8 by 2 cells on the 0.25 m grid - and 2 m tall,
    /// standing on its own origin, its front facing +z - the way the pack's other furniture faces,
    /// and the side its thumbnail is rendered from.</item>
    /// <item>The marker: a child transform named <c>StandPoint</c>, 0.85 m in front of the shelf on the
    /// ground, facing it (-z). It is where someone stands to use the shelf; it carries no renderer or
    /// collider, so it changes neither the footprint nor what can be clicked. No script is on the
    /// prefab: the game recognises the shelf by its address and finds the stand point by name.</item>
    /// </list>
    /// Built as one mesh asset in <c>Process/default/Meshes</c> - a frame and three colours of books -
    /// with URP materials in <c>Process/default/Materials</c>, so the pack carries everything it draws.
    /// <para>
    /// Run <c>Tools ▸ Dojo ▸ Addressables ▸ 2. Addressable Generator</c> on the default pack afterwards: that gives the
    /// prefab its address, thumbnail and catalogue entry, and its name files it under drawers.
    /// </para>
    /// </remarks>
    public static class KnowledgeShelfBuilder
    {
        public const string Name = "default_knowledge_shelf";
        public const string StandPointName = "StandPoint";

        const string PackFolder = "Assets/Process/default";

        // Outer size, in metres.
        const float Width = 2f;
        const float Depth = 0.5f;
        const float Height = 2f;

        /// <summary>Thickness of the sides, shelves and top.</summary>
        const float Board = 0.04f;

        /// <summary>How far in front of the shelf its stand point is.</summary>
        const float StandDistance = 0.85f;

        /// <summary>The tops of the shelf boards, bottom to top.</summary>
        static readonly float[] ShelfTops = { Board, 0.5f, 0.95f, 1.4f };

        static readonly Color Wood = new Color(0.45f, 0.3f, 0.18f);
        static readonly Color[] BookColours =
        {
            new Color(0.22f, 0.38f, 0.62f),
            new Color(0.7f, 0.25f, 0.22f),
            new Color(0.88f, 0.72f, 0.35f),
        };

        [MenuItem("Tools/Dojo/Build Knowledge Shelf Prefab")]
        public static void Build()
        {
            Directory.CreateDirectory(PackFolder + "/Meshes");
            Directory.CreateDirectory(PackFolder + "/Materials");
            Directory.CreateDirectory(PackFolder + "/Prefabs");

            var mesh = SaveMesh();

            var materials = new Material[1 + BookColours.Length];
            materials[0] = SaveMaterial(Name + "_frame", Wood, 0.25f);

            for (var i = 0; i < BookColours.Length; i++)
            {
                materials[i + 1] = SaveMaterial(Name + "_books_" + (i + 1), BookColours[i], 0.35f);
            }

            var root = new GameObject(Name);

            try
            {
                var body = new GameObject(Name + "_mesh");
                body.transform.SetParent(root.transform, false);
                body.AddComponent<MeshFilter>().sharedMesh = mesh;
                body.AddComponent<MeshRenderer>().sharedMaterials = materials;

                var collider = root.AddComponent<BoxCollider>();
                collider.center = new Vector3(0f, Height * 0.5f, 0f);
                collider.size = new Vector3(Width, Height, Depth);

                // Where someone stands to use it: on the ground in front (+z), facing the shelf (-z).
                var stand = new GameObject(StandPointName);
                stand.transform.SetParent(root.transform, false);
                stand.transform.localPosition = new Vector3(0f, 0f, Depth * 0.5f + StandDistance);
                stand.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

                var path = PackFolder + "/Prefabs/" + Name + ".prefab";
                PrefabUtility.SaveAsPrefabAsset(root, path);

                Debug.Log("[KnowledgeShelf] Built " + path + ": " + Width + " x " + Depth + " m, " + Height + " m tall, "
                    + StandPointName + " " + StandDistance + " m in front. Run the Addressable Generator on the default "
                    + "pack to give it its address, thumbnail and catalogue entry.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ── The mesh ────────────────────────────────────────────────────────────────────────

        sealed class Part
        {
            public readonly List<int> Triangles = new List<int>();
        }

        /// <summary>Fills <paramref name="mesh"/> with the shelf, replacing whatever it held.</summary>
        static void BuildMesh(Mesh mesh)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var parts = new Part[1 + BookColours.Length];

            for (var i = 0; i < parts.Length; i++)
            {
                parts[i] = new Part();
            }

            var frame = parts[0];
            var inner = Width - Board * 2f;

            // Sides, top, back.
            Box(vertices, normals, uvs, frame, new Vector3(-(Width - Board) * 0.5f, Height * 0.5f, 0f), new Vector3(Board, Height, Depth));
            Box(vertices, normals, uvs, frame, new Vector3((Width - Board) * 0.5f, Height * 0.5f, 0f), new Vector3(Board, Height, Depth));
            Box(vertices, normals, uvs, frame, new Vector3(0f, Height - Board * 0.5f, 0f), new Vector3(inner, Board, Depth));
            Box(vertices, normals, uvs, frame, new Vector3(0f, Height * 0.5f, -(Depth * 0.5f - 0.01f)), new Vector3(inner, Height - Board * 2f, 0.02f));

            // The shelf boards, and a row of books on each.
            var random = new System.Random(15);

            for (var s = 0; s < ShelfTops.Length; s++)
            {
                var top = ShelfTops[s];
                Box(vertices, normals, uvs, frame, new Vector3(0f, top - Board * 0.5f, 0f), new Vector3(inner, Board, Depth));

                var ceiling = s + 1 < ShelfTops.Length ? ShelfTops[s + 1] - Board : Height - Board;
                var room = ceiling - top;
                var x = -inner * 0.5f + 0.03f;
                var end = inner * 0.5f - 0.03f;
                var book = 0;

                while (x < end)
                {
                    var thick = 0.035f + (float)random.NextDouble() * 0.045f;

                    if (x + thick > end)
                    {
                        break;
                    }

                    // Now and then a gap, so the rows read as books rather than a block.
                    if (random.NextDouble() < 0.08)
                    {
                        x += thick * 2f;
                        continue;
                    }

                    var tall = Mathf.Min(room - 0.04f, room * (0.6f + (float)random.NextDouble() * 0.3f));
                    var deep = Depth * (0.6f + (float)random.NextDouble() * 0.15f);
                    var colour = parts[1 + (book + s) % BookColours.Length];

                    Box(vertices, normals, uvs, colour,
                        new Vector3(x + thick * 0.5f, top + tall * 0.5f, -(Depth * 0.5f - 0.02f - deep * 0.5f)),
                        new Vector3(thick, tall, deep));

                    x += thick + 0.004f;
                    book++;
                }
            }

            mesh.Clear();
            mesh.name = Name;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = parts.Length;

            for (var i = 0; i < parts.Length; i++)
            {
                mesh.SetTriangles(parts[i].Triangles, i);
            }

            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
        }

        /// <summary>A box with its own vertices per face, so its edges stay sharp.</summary>
        static void Box(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, Part part, Vector3 centre, Vector3 size)
        {
            var h = size * 0.5f;
            Face(vertices, normals, uvs, part, centre, Vector3.right, new Vector3(0, 0, h.z), new Vector3(0, h.y, 0), h.x);
            Face(vertices, normals, uvs, part, centre, Vector3.left, new Vector3(0, 0, -h.z), new Vector3(0, h.y, 0), h.x);
            Face(vertices, normals, uvs, part, centre, Vector3.up, new Vector3(h.x, 0, 0), new Vector3(0, 0, h.z), h.y);
            Face(vertices, normals, uvs, part, centre, Vector3.down, new Vector3(-h.x, 0, 0), new Vector3(0, 0, h.z), h.y);
            Face(vertices, normals, uvs, part, centre, Vector3.forward, new Vector3(-h.x, 0, 0), new Vector3(0, h.y, 0), h.z);
            Face(vertices, normals, uvs, part, centre, Vector3.back, new Vector3(h.x, 0, 0), new Vector3(0, h.y, 0), h.z);
        }

        /// <summary>One face: <paramref name="u"/> and <paramref name="v"/> are its half-extents, wound to face <paramref name="normal"/>.</summary>
        static void Face(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, Part part,
            Vector3 centre, Vector3 normal, Vector3 u, Vector3 v, float distance)
        {
            var middle = centre + normal * distance;
            var first = vertices.Count;

            vertices.Add(middle - u - v);
            vertices.Add(middle - u + v);
            vertices.Add(middle + u + v);
            vertices.Add(middle + u - v);

            for (var i = 0; i < 4; i++)
            {
                normals.Add(normal);
            }

            uvs.Add(new Vector2(0, 0));
            uvs.Add(new Vector2(0, 1));
            uvs.Add(new Vector2(1, 1));
            uvs.Add(new Vector2(1, 0));

            // Wound so the face shows from the side its normal points to.
            var facing = Vector3.Dot(Vector3.Cross(u, v), normal) < 0f;

            if (facing)
            {
                part.Triangles.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
            }
            else
            {
                part.Triangles.AddRange(new[] { first, first + 2, first + 1, first, first + 3, first + 2 });
            }
        }

        // ── Assets ──────────────────────────────────────────────────────────────────────────

        static Mesh SaveMesh()
        {
            var path = PackFolder + "/Meshes/" + Name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);

            if (existing != null)
            {
                // Rebuilt in place, so the prefab and anything else pointing at it keep pointing at it.
                // Through the Mesh API rather than CopySerialized: that changed the data on disk but
                // left the editor drawing the old shape, and the thumbnail was rendered from it.
                BuildMesh(existing);
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssets();
                return existing;
            }

            var mesh = new Mesh();
            BuildMesh(mesh);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static Material SaveMaterial(string materialName, Color colour, float smoothness)
        {
            var path = PackFolder + "/Materials/" + materialName + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);

            // The pack's own shader, so it fades with its floor like the rest of the pack.
            var shader = Shader.Find("Dojo/Fade Lit") ?? Shader.Find("Universal Render Pipeline/Lit");

            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            material.SetColor("_BaseColor", colour);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(material);

            return material;
        }
    }
}
