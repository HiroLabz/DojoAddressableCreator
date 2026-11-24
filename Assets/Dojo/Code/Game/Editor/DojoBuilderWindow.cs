using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dojo.Game.Placement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Dojo.Game.Editor
{
    /// <summary>
    /// Tools ▸ Dojo ▸ Content ▸ Builder: everything for building the default world and adding to the
    /// MVP kit, in one window, for the person who takes over the default world.
    /// </summary>
    /// <remarks>
    /// <b>World</b>: start a world from scratch (one floor tile) or from a copy of the default world,
    /// play it in RuntimeMVP and build it there like a building game, then make it the default world -
    /// after a check against the building rules.
    /// <para>
    /// <b>Assets</b>: the models in Assets/Dojo/MVP, what each is (furniture, floor, wall, chair,
    /// elevator), its inventory tab and name; Build &amp; Add makes its prefab, its sprite thumbnail and
    /// its address, files it under its tab and puts it in the MVP scene's inventory.
    /// </para>
    /// <para>
    /// <b>Rooms</b>: the pack's rooms, and the rooms made in Play waiting to be promoted into the pack.
    /// </para>
    /// The rules are the game's own and stay in force: the 0.3 m grid, walls on the floor only, a room
    /// moved and deleted as one unit, sprite thumbnails.
    /// </remarks>
    public sealed class DojoBuilderWindow : EditorWindow
    {
        const string Pack = "Assets/Process/default";

        static readonly string[] Tabs = { "World", "Assets", "Rooms" };

        int tab;
        Vector2 scroll;

        // World
        string newName = "MyWorld";
        string chosenWorld;
        List<string> lastProblems;
        string lastChecked;

        // Assets
        readonly HashSet<string> ticked = new HashSet<string>();
        MvpKitSettings settings;

        [MenuItem("Tools/Dojo/Content/Builder (World, Assets, Rooms)", false, 0)]
        public static void Open()
        {
            var window = GetWindow<DojoBuilderWindow>("Dojo Builder");
            window.minSize = new Vector2(560f, 420f);
            window.Show();
        }

        void OnGUI()
        {
            tab = GUILayout.Toolbar(tab, Tabs, GUILayout.Height(26f));
            EditorGUILayout.Space(4f);

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.HelpBox("Playing: build the world in the Game view. SAVE WORLD from the in-game menu, then stop Play to come back here.", MessageType.Info);
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            switch (tab)
            {
                case 0: WorldTab(); break;
                case 1: AssetsTab(); break;
                default: RoomsTab(); break;
            }

            EditorGUILayout.EndScrollView();
        }

        // ── World ─────────────────────────────────────────────────────────────────────────────

        void WorldTab()
        {
            var saves = WorldPackExporter.Saves();
            var library = saves.Read();
            var playing = EditorApplication.isPlayingOrWillChangePlaymode;

            EditorGUILayout.HelpBox(
                "1. Start a world: from scratch, or from a copy of the default world.\n" +
                "2. Play it: it opens in RuntimeMVP. Build it from the inventory like a building game; it saves itself as you go.\n" +
                "3. Stop Play, pick it below and Make Default World. New players start in it once the content is published.",
                MessageType.None);

            using (new EditorGUI.DisabledScope(playing))
            {
                EditorGUILayout.LabelField("Start a world", EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    newName = EditorGUILayout.TextField("Name", newName);
                    if (GUILayout.Button("From scratch", GUILayout.Width(110f)))
                    {
                        StartWorld(saves, library, Blank(newName));
                    }

                    if (GUILayout.Button("Copy of default", GUILayout.Width(120f)))
                    {
                        var copy = DefaultCopy(newName);
                        if (copy != null)
                        {
                            StartWorld(saves, library, copy);
                        }
                    }
                }
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Saved worlds (" + Path.GetFileName(saves.FullPath) + ")", EditorStyles.boldLabel);

            var current = library.CurrentName();
            if (library.worlds.Count == 0)
            {
                EditorGUILayout.LabelField("None yet.", EditorStyles.miniLabel);
            }

            foreach (var world in library.worlds)
            {
                if (world == null)
                {
                    continue;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    var on = chosenWorld == world.name;
                    if (EditorGUILayout.Toggle(on, EditorStyles.radioButton, GUILayout.Width(18f)) && !on)
                    {
                        chosenWorld = world.name;
                        lastProblems = null;
                    }

                    var label = world.name + (world.name == current ? "   (opens on Play)" : string.Empty);
                    EditorGUILayout.LabelField(label, EditorStyles.boldLabel, GUILayout.MinWidth(160f));
                    EditorGUILayout.LabelField(world.PieceCount() + " pieces, " + world.floors.Count + " floor(s), "
                        + world.rooms.Count + " room(s)", EditorStyles.miniLabel);
                }
            }

            var chosen = library.Find(chosenWorld);
            using (new EditorGUI.DisabledScope(chosen == null || playing))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Play it"))
                    {
                        library.Select(chosen.name);
                        saves.Write(library);
                        Play();
                    }

                    if (GUILayout.Button("Check rules"))
                    {
                        lastProblems = WorldRules.Check(chosen);
                        lastChecked = chosen.name;
                    }

                    if (GUILayout.Button("Make Default World"))
                    {
                        WorldPackExporter.ExportWorld(chosen, true);
                    }

                    if (GUILayout.Button("Delete…", GUILayout.Width(70f))
                        && EditorUtility.DisplayDialog("Delete world", "Delete '" + chosen.name + "' from the saved worlds? The default world is not touched.", "Delete", "Cancel"))
                    {
                        library.Remove(chosen.name);
                        saves.Write(library);
                        chosenWorld = null;
                    }
                }
            }

            if (lastProblems != null && chosen != null && lastChecked == chosen.name)
            {
                EditorGUILayout.HelpBox(lastProblems.Count == 0
                        ? "'" + chosen.name + "' follows the rules."
                        : lastProblems.Count + " problem(s):\n- " + string.Join("\n- ", lastProblems.Take(12)) + (lastProblems.Count > 12 ? "\n…" : string.Empty),
                    lastProblems.Count == 0 ? MessageType.Info : MessageType.Warning);
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Default world", EditorStyles.boldLabel);
            var pack = ReadDefault();
            EditorGUILayout.LabelField(pack != null
                ? "'" + pack.name + "': " + pack.PieceCount() + " pieces, " + pack.floors.Count + " floor(s), " + pack.rooms.Count + " room(s)"
                : "None in the pack.", EditorStyles.miniLabel);
        }

        static WorldSave Blank(string name)
        {
            var world = new WorldSave { name = name, savedUtc = DateTime.UtcNow.ToString("o") };
            var tile = "default/" + MvpKitImporter.PrefabName("LightGrayFloorTile");
            world.FloorAt(0).pieces.Add(new WorldSave.SavedPiece
            {
                prefabId = string.Empty, resource = tile, name = tile,
                position = Vector3.zero, rotation = Quaternion.identity, scale = Vector3.one, isFloor = true,
            });
            return world;
        }

        static WorldSave ReadDefault()
        {
            if (!File.Exists(WorldPackExporter.PackPath))
            {
                return null;
            }

            var pack = JsonUtility.FromJson<WorldLibrary>(File.ReadAllText(WorldPackExporter.PackPath));
            return pack != null && pack.worlds.Count > 0 ? pack.Find(pack.CurrentName()) ?? pack.worlds[0] : null;
        }

        static WorldSave DefaultCopy(string name)
        {
            var pack = ReadDefault();
            if (pack == null)
            {
                EditorUtility.DisplayDialog("Copy of default", "There is no default world in the pack to copy.", "OK");
                return null;
            }

            pack.name = name;
            pack.savedUtc = DateTime.UtcNow.ToString("o");
            return pack;
        }

        void StartWorld(WorldLibraryFile saves, WorldLibrary library, WorldSave world)
        {
            if (string.IsNullOrWhiteSpace(world.name))
            {
                EditorUtility.DisplayDialog("Start a world", "Give the world a name first.", "OK");
                return;
            }

            world.name = world.name.Trim();
            if (library.Find(world.name) != null
                && !EditorUtility.DisplayDialog("Start a world", "There is already a saved world called '" + world.name + "'. Replace it?", "Replace", "Cancel"))
            {
                return;
            }

            library.Put(world);
            library.Select(world.name);
            saves.Write(library);
            chosenWorld = world.name;
            Debug.Log("[Builder] '" + world.name + "' saved in " + saves.FullPath + " and set to open on Play.");
            Play();
        }

        /// <summary>Opens RuntimeMVP (asking to save the open scene first) and plays it.</summary>
        static void Play()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            if (EditorSceneManager.GetActiveScene().path != MvpKitImporter.MvpScene)
            {
                EditorSceneManager.OpenScene(MvpKitImporter.MvpScene, OpenSceneMode.Single);
            }

            EditorApplication.isPlaying = true;
        }

        // ── Assets ────────────────────────────────────────────────────────────────────────────

        void AssetsTab()
        {
            settings = settings != null ? settings : MvpKitSettings.Load();
            var playing = EditorApplication.isPlayingOrWillChangePlaymode;

            EditorGUILayout.HelpBox(
                "Adding a piece: put its .fbx in " + MvpKitImporter.ModelFolder + " and its textures in " + MvpKitImporter.ModelFolder
                + "/textures, named as the model's materials point at them. It appears below.\n" +
                "Say what it is (Kind) and which inventory tab it goes under, tick it, and Build & Add: its prefab, sprite thumbnail, " +
                "address and inventory entry are made, and it is put in the MVP scene's inventory.\n" +
                "Walls are known by name: a Wall gets \"_wall\" on its name if it doesn't say wall, partition or railing.",
                MessageType.None);

            var models = MvpKitImporter.Models();

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("", GUILayout.Width(18f));
                GUILayout.Label("Model", EditorStyles.miniBoldLabel, GUILayout.Width(170f));
                GUILayout.Label("Kind", EditorStyles.miniBoldLabel, GUILayout.Width(80f));
                GUILayout.Label("Tab", EditorStyles.miniBoldLabel, GUILayout.Width(80f));
                GUILayout.Label("Prefab name (empty: from the model)", EditorStyles.miniBoldLabel, GUILayout.MinWidth(120f));
                GUILayout.Label("State", EditorStyles.miniBoldLabel, GUILayout.Width(70f));
            }

            foreach (var model in models)
            {
                var piece = settings.For(model);
                using (new EditorGUILayout.HorizontalScope())
                {
                    var on = ticked.Contains(model);
                    if (EditorGUILayout.Toggle(on, GUILayout.Width(18f)) != on)
                    {
                        if (on) ticked.Remove(model); else ticked.Add(model);
                    }

                    EditorGUILayout.LabelField(model, GUILayout.Width(170f));

                    EditorGUI.BeginChangeCheck();
                    var kind = (KitPieceKind)EditorGUILayout.EnumPopup(piece.kind, GUILayout.Width(80f));
                    var categoryIndex = Mathf.Max(0, Array.IndexOf(MvpKitSettings.Categories, piece.category ?? string.Empty));
                    var category = MvpKitSettings.Categories[EditorGUILayout.Popup(categoryIndex,
                        MvpKitSettings.Categories.Select(c => c.Length == 0 ? "(guess)" : c).ToArray(), GUILayout.Width(80f))];
                    var prefabName = EditorGUILayout.DelayedTextField(piece.prefabName ?? string.Empty, GUILayout.MinWidth(120f));
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(settings, "Kit piece");
                        piece.kind = kind;
                        piece.category = category;
                        piece.prefabName = prefabName;
                        EditorUtility.SetDirty(settings);
                    }

                    EditorGUILayout.LabelField(State(model), GUILayout.Width(70f));
                }

                var warning = Warning(model);
                if (warning != null)
                {
                    EditorGUILayout.HelpBox(model + ": " + warning, MessageType.Warning);
                }
            }

            if (EditorUtility.IsDirty(settings))
            {
                AssetDatabase.SaveAssetIfDirty(settings);
            }

            EditorGUILayout.Space(6f);
            using (new EditorGUI.DisabledScope(playing))
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(ticked.Count == 0))
                {
                    if (GUILayout.Button("Build & Add ticked (" + ticked.Count + ")", GUILayout.Height(26f)))
                    {
                        BuildAndAdd(new List<string>(ticked));
                    }
                }

                var fresh = models.Where(m => State(m) == "new").ToList();
                using (new EditorGUI.DisabledScope(fresh.Count == 0))
                {
                    if (GUILayout.Button("Build & Add new (" + fresh.Count + ")", GUILayout.Height(26f)))
                    {
                        BuildAndAdd(fresh);
                    }
                }

                if (GUILayout.Button("Select settings", GUILayout.Height(26f), GUILayout.Width(110f)))
                {
                    Selection.activeObject = settings;
                }
            }
        }

        static string State(string model)
        {
            var kind = MvpKitImporter.KindOf(model);
            if (kind == KitPieceKind.Skip)
            {
                return "skipped";
            }

            var prefab = MvpKitImporter.PrefabPath(model);
            if (!File.Exists(prefab))
            {
                return "new";
            }

            var modelFile = MvpKitImporter.ModelFolder + "/" + model + ".fbx";
            return File.Exists(modelFile) && File.GetLastWriteTimeUtc(modelFile) > File.GetLastWriteTimeUtc(prefab) ? "changed" : "built";
        }

        static string Warning(string model)
        {
            var kind = MvpKitImporter.KindOf(model);
            var name = MvpKitImporter.PrefabName(model);

            if (kind != KitPieceKind.Wall && kind != KitPieceKind.Skip && RoomOpenings.IsWall(name))
            {
                return "'" + name + "' says wall, partition or railing, so rooms will take it for a wall. Give it another prefab name, or make its Kind Wall.";
            }

            return null;
        }

        void BuildAndAdd(List<string> models)
        {
            var built = MvpKitImporter.Build(models);
            if (built == 0)
            {
                EditorUtility.DisplayDialog("Build & Add", "Nothing was built. Skipped models are left out.", "OK");
                return;
            }

            EditorUtility.DisplayProgressBar("Build & Add", "Thumbnails, addresses and the inventory…", 0.5f);
            try
            {
                AddressableGenerator.RunOn(Pack);
                var tabs = MvpKitImporter.ApplyCategories();
                MvpKitImporter.SetUpScene();

                var addresses = string.Join("\n", models.Where(m => MvpKitImporter.KindOf(m) != KitPieceKind.Skip)
                    .Select(m => "default/" + MvpKitImporter.PrefabName(m)));
                ticked.Clear();

                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Build & Add",
                    built + " piece(s) built, with thumbnails and addresses" + (tabs > 0 ? ", " + tabs + " tab(s) set" : string.Empty)
                    + ", and in RuntimeMVP's inventory:\n\n" + addresses
                    + "\n\nFor the game: add them to Game.unity's inventory list in dojo-game-app, then build and publish the content (Documentation/Content-Release-Guide.md).",
                    "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // ── Rooms ─────────────────────────────────────────────────────────────────────────────

        void RoomsTab()
        {
            var playing = EditorApplication.isPlayingOrWillChangePlaymode;

            EditorGUILayout.HelpBox(
                "Rooms are made in Play: open the Rooms tab of the inventory and use the room editor. A saved room is yours " +
                "(room_<name>); Promote puts it in the pack, so it ships with the content and every player has it. A placed room " +
                "always moves, turns and is deleted as one unit.",
                MessageType.None);

            EditorGUILayout.LabelField("Made, not yet in the pack", EditorStyles.boldLabel);
            var made = Directory.Exists(RoomsPack.Folder) ? Directory.GetFiles(RoomsPack.Folder, "room_*.json") : new string[0];
            if (made.Length == 0)
            {
                EditorGUILayout.LabelField("None.", EditorStyles.miniLabel);
            }

            foreach (var file in made)
            {
                var path = file.Replace('\\', '/');
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(Path.GetFileNameWithoutExtension(path));
                    using (new EditorGUI.DisabledScope(playing))
                    {
                        if (GUILayout.Button("Promote", GUILayout.Width(80f)))
                        {
                            var problem = RoomsPack.Promote(path);
                            if (problem == null)
                            {
                                MvpKitImporter.SetUpScene();
                            }

                            EditorUtility.DisplayDialog("Promote room", problem ?? "In the pack, and in RuntimeMVP's inventory.", "OK");
                        }
                    }
                }
            }

            EditorGUILayout.Space(8f);
            var pack = Directory.Exists(RoomsPack.Folder) ? Directory.GetFiles(RoomsPack.Folder, "default_room_*.json") : new string[0];
            EditorGUILayout.LabelField("In the pack (" + pack.Length + ")", EditorStyles.boldLabel);
            foreach (var file in pack)
            {
                var slug = Path.GetFileNameWithoutExtension(file);
                var thumb = RoomsPack.ThumbnailFolder + "/" + slug.Replace("default_room_", "room_") + ".png";
                EditorGUILayout.LabelField(slug + (File.Exists(thumb) ? string.Empty : "   (no thumbnail)"), EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(6f);
            using (new EditorGUI.DisabledScope(playing))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate room thumbnails"))
                {
                    RoomThumbnails.Generate();
                }

                if (GUILayout.Button("Check room openings"))
                {
                    RoomOpeningsMarker.Check();
                }
            }
        }

        void OnInspectorUpdate() => Repaint();
    }
}
