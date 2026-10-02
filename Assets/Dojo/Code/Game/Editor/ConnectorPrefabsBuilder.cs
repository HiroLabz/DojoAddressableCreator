using System.IO;
using Dojo.Game.Placement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Dojo.Game.Editor
{
    /// <summary>
    /// Makes the placeholder elevator and stairs in the default pack: <c>default_elevator</c> and
    /// <c>default_stairs</c>, built from boxes until real art replaces them.
    /// </summary>
    /// <remarks>
    /// Both are one floor tall - <see cref="FloorHeight"/>, which must match the game's
    /// <c>PlacementGrid</c> floor spacing - and shaped like every other prefab in the pack: a bare
    /// root with the parts beneath it, the pivot at the bottom centre, and a box collider on each part
    /// that is exactly the size of what it draws.
    /// <list type="bullet">
    /// <item><b>Elevator</b> - one floor's stop: floor, walls, roof, door frame, and two door panels
    /// named <c>DoorLeft</c> and <c>DoorRight</c> so the game can slide them. The doors are on -X.</item>
    /// <item><b>Stairs</b> - twelve steps rising one floor along +Z.</item>
    /// </list>
    /// <para>
    /// Every part shares one unit cube saved into the pack rather than Unity's built-in one, because
    /// the game bakes its walking maps at run time, and in a player that needs meshes it can read.
    /// </para>
    /// <para>
    /// Re-runnable: each run overwrites the two prefabs in place, keeping their GUIDs. Run the
    /// Addressable Generator on the default pack afterwards so they are rendered and registered.
    /// </para>
    /// </remarks>
    public static class ConnectorPrefabsBuilder
    {
        /// <summary>Height of one floor, in metres. The game's PlacementGrid floor spacing.</summary>
        public const float FloorHeight = 3f;

        const string Pack = "Assets/Process/default/";
        const string PrefabFolder = Pack + "Prefabs/";
        const string MaterialFolder = Pack + "Materials/";
        const string MeshFolder = Pack + "Meshes/";
        const string CubePath = MeshFolder + "connector_cube.asset";

        public const string ElevatorName = "default_elevator";
        public const string StairsName = "default_stairs";

        [MenuItem("Tools/Dojo/Build Elevator And Stairs")]
        public static void BuildFromMenu()
        {
            Debug.Log("[Connectors] " + Build());
        }

        /// <summary>Makes or remakes both prefabs. Returns a line describing what was done.</summary>
        public static string Build()
        {
            Directory.CreateDirectory(MeshFolder);
            var cube = UnitCube();

            var shaft = MaterialAt("elevator_shaft", Hex("#55637a"), 0.55f, 0.45f);
            var doors = MaterialAt("elevator_doors", Hex("#c3cede"), 0.85f, 0.65f);
            var lamp = MaterialAt("elevator_lamp", Hex("#2f8fff"), 0f, 0.8f, emissive: true);
            var steps = MaterialAt("stairs_steps", Hex("#8e9aad"), 0.05f, 0.35f);

            // Put together in a preview scene, so the working copies never touch - or mark as
            // changed - whatever scene is open.
            var stage = EditorSceneManager.NewPreviewScene();

            try
            {
                Save(Elevator(stage, cube, shaft, doors, lamp), ElevatorName);
                Save(Stairs(stage, cube, steps), StairsName);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(stage);
            }

            AssetDatabase.SaveAssets();
            return "made " + ElevatorName + " and " + StairsName + " in " + PrefabFolder
                + ". Run the Addressable Generator on the default pack to register them.";
        }

        // ── The elevator ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// One floor's stop of the shaft: 2 m square, a little under one floor tall so it never
        /// pokes through the floor above. The doors face -X.
        /// </summary>
        /// <remarks>
        /// -X because it is the one face both cameras see: the game's isometric view looks at -X and
        /// -Z, and the pack's thumbnail at -X and +Z. Doors anywhere else would make the inventory
        /// show a plain box. The player can still turn it in quarter turns like anything else.
        /// </remarks>
        static GameObject Elevator(Scene stage, Mesh cube, Material shaft, Material doors, Material lamp)
        {
            var root = Root(ElevatorName, stage);
            root.AddComponent<ElevatorPiece>();

            const float width = 2f;    // along X, front to back
            const float depth = 2f;    // along Z, side to side
            const float height = FloorHeight - 0.1f;
            const float wall = 0.1f;
            const float opening = 1.2f;
            const float doorHeight = 2.3f;

            var halfW = width * 0.5f;
            var halfD = depth * 0.5f;

            Part(root, "Floor", cube, shaft, new Vector3(0f, 0.025f, 0f), new Vector3(width, 0.05f, depth));
            Part(root, "WallBack", cube, shaft, new Vector3(halfW - wall * 0.5f, height * 0.5f, 0f), new Vector3(wall, height, depth));
            Part(root, "WallLeft", cube, shaft, new Vector3(0f, height * 0.5f, halfD - wall * 0.5f), new Vector3(width, height, wall));
            Part(root, "WallRight", cube, shaft, new Vector3(0f, height * 0.5f, -halfD + wall * 0.5f), new Vector3(width, height, wall));
            Part(root, "Roof", cube, shaft, new Vector3(0f, height - wall * 0.5f, 0f), new Vector3(width, wall, depth));

            // The front, around the door opening.
            var post = (depth - opening) * 0.5f;
            var front = -halfW + wall * 0.5f;
            Part(root, "FrontTop", cube, shaft, new Vector3(front, (doorHeight + height) * 0.5f, 0f), new Vector3(wall, height - doorHeight, depth));
            Part(root, "FrontLeft", cube, shaft, new Vector3(front, doorHeight * 0.5f, halfD - post * 0.5f), new Vector3(wall, doorHeight, post));
            Part(root, "FrontRight", cube, shaft, new Vector3(front, doorHeight * 0.5f, -halfD + post * 0.5f), new Vector3(wall, doorHeight, post));

            // The doors, just proud of the frame so they read as separate panels, meeting in the middle.
            var doorX = -halfW - 0.02f;
            Part(root, "DoorLeft", cube, doors, new Vector3(doorX, doorHeight * 0.5f, opening * 0.25f), new Vector3(0.04f, doorHeight, opening * 0.5f));
            Part(root, "DoorRight", cube, doors, new Vector3(doorX, doorHeight * 0.5f, -opening * 0.25f), new Vector3(0.04f, doorHeight, opening * 0.5f));

            // A lit strip over the doors, so the stop reads as an elevator from across the room.
            Part(root, "Lamp", cube, lamp, new Vector3(-halfW - 0.03f, doorHeight + 0.15f, 0f), new Vector3(0.02f, 0.1f, 0.6f), collide: false);

            return root;
        }

        // ── The stairs ───────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Twelve steps rising exactly one floor along +Z: the foot at -Z on the floor it stands on,
        /// the head at +Z level with the floor above. 1.2 m wide.
        /// </summary>
        /// <remarks>
        /// Each step is a solid block down to the ground, so the flight looks solid from the side and
        /// a walking map sees a stepped slope rather than gaps. 0.25 m a step over 0.3 m of tread
        /// keeps the slope under 40 degrees.
        /// <para>
        /// No rails: on their own, a metre over the nosings, they floated, and the stepped side
        /// already says what this is. Posts and rails can come with the real art.
        /// </para>
        /// </remarks>
        static GameObject Stairs(Scene stage, Mesh cube, Material steps)
        {
            var root = Root(StairsName, stage);
            root.AddComponent<StairsPiece>();

            const int count = 12;
            const float width = 1.2f;
            const float tread = 0.3f;
            var rise = FloorHeight / count;
            var run = tread * count;
            var start = -run * 0.5f;

            for (var i = 0; i < count; i++)
            {
                var top = rise * (i + 1);
                Part(root, "Step" + (i + 1).ToString("00"), cube, steps,
                    new Vector3(0f, top * 0.5f, start + tread * (i + 0.5f)),
                    new Vector3(width, top, tread));
            }

            return root;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────────────

        static GameObject Root(string name, Scene stage)
        {
            var root = new GameObject(name);
            SceneManager.MoveGameObjectToScene(root, stage);
            return root;
        }

        /// <summary>
        /// One box: the shared cube scaled to <paramref name="size"/>, its collider exactly the size
        /// of what it draws.
        /// </summary>
        static GameObject Part(GameObject root, string name, Mesh cube, Material material,
            Vector3 centre, Vector3 size, bool collide = true)
        {
            var part = new GameObject(name);
            part.transform.SetParent(root.transform, false);
            part.transform.localPosition = centre;
            part.transform.localScale = size;

            part.AddComponent<MeshFilter>().sharedMesh = cube;
            part.AddComponent<MeshRenderer>().sharedMaterial = material;

            if (collide)
            {
                // A unit box on a unit cube: the scale makes both the same size as the part.
                part.AddComponent<BoxCollider>();
            }

            return part;
        }

        static void Save(GameObject root, string name)
        {
            bool saved;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabFolder + name + ".prefab", out saved);

            if (!saved)
            {
                Debug.LogError("[Connectors] Could not save " + PrefabFolder + name + ".prefab.");
            }
        }

        /// <summary>The pack's own unit cube, made once from Unity's and kept readable.</summary>
        static Mesh UnitCube()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(CubePath);
            if (existing != null)
            {
                return existing;
            }

            var cube = Object.Instantiate(Resources.GetBuiltinResource<Mesh>("Cube.fbx"));
            cube.name = "connector_cube";
            AssetDatabase.CreateAsset(cube, CubePath);

            return cube;
        }

        /// <summary>A URP Lit material in the pack's Materials folder, made or updated in place.</summary>
        static Material MaterialAt(string name, Color colour, float metallic, float smoothness, bool emissive = false)
        {
            var path = MaterialFolder + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", colour);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);

            if (emissive)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", colour * 2f);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var colour);
            return colour;
        }
    }
}
