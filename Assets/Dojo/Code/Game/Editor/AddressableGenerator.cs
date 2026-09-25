using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using Dojo.Framework.Inventory;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace Dojo.Game.Editor
{
    /// <summary>
    /// Builds a whole pack from one folder: renders every prefab to a square PNG, writes a manifest
    /// pairing each prefab with its thumbnail, and registers everything else the folder holds.
    /// Built for the furniture inventory, which needs an icon per placeable piece and a lookup from
    /// prefab to icon at runtime.
    /// </summary>
    /// <remarks>
    /// The pack folder is laid out as <c>&lt;pack&gt;/Prefabs</c>, <c>&lt;pack&gt;/Thumbnails</c>
    /// and any number of other folders — <c>Textures</c>, <c>Audio</c>, whatever the pack needs.
    /// The pack's name is the folder's name. Everything in the other folders is labelled with the
    /// pack and addressed <c>&lt;pack&gt;/&lt;Folder&gt;/&lt;file&gt;</c>, so it downloads with the
    /// pack and can be loaded by address.
    /// <para>
    /// Rendering goes through <see cref="PreviewRenderUtility"/>, the same isolated preview scene
    /// the inspector uses for its asset previews. That matters: it brings its own lights and sees
    /// nothing of the open scene, so a thumbnail does not change depending on which scene happened
    /// to be loaded or where the prefab would have stood in it.
    /// </para>
    /// </remarks>
    public sealed class AddressableGenerator : EditorWindow
    {
        const string RootKey = "Dojo.ThumbnailGenerator.Root";

        /// <summary>
        /// Where the prefab folder used to be remembered. Read once, to start from the same pack
        /// the first time the window opens after it took a single folder.
        /// </summary>
        const string SourceKey = "Dojo.ThumbnailGenerator.Source";

        const string PrefabGroupKey = "Dojo.ThumbnailGenerator.PrefabGroup";
        const string ThumbnailGroupKey = "Dojo.ThumbnailGenerator.ThumbnailGroup";
        const string CategorySourceKey = "Dojo.ThumbnailGenerator.CategorySource";
        const string ManifestName = "thumbnails.json";

        /// <summary>Where a pack's item manifest is written, under a folder named for the pack.</summary>
        const string PackFolder = "Assets/Dojo/Content/Packs";

        /// <summary>The file inside that folder, and the last segment of its address.</summary>
        const string ItemsName = "items.json";

        /// <summary>The pack every player gets. Matches <c>ContentSettings.DefaultKey</c>.</summary>
        const string DefaultPack = "default";

        /// <summary>The folder inside the pack folder holding what gets rendered.</summary>
        const string PrefabsFolderName = "Prefabs";

        /// <summary>The folder inside the pack folder the PNGs are written to.</summary>
        const string ThumbnailsFolderName = "Thumbnails";

        /// <summary>
        /// How a group is named for one kind of content: <c>Content_Audio</c> for an
        /// <c>Audio</c> folder, and so on.
        /// </summary>
        const string GroupPrefix = "Content_";

        /// <summary>Where a folder goes when no group is named for it.</summary>
        const string SharedGroup = "Content_Shared";

        /// <summary>
        /// Background used when a transparent icon is wanted, then keyed out to alpha. Magenta
        /// because nothing in an office set is this colour, so nothing real gets punched out.
        /// </summary>
        static readonly Color KeyColour = new Color(1f, 0f, 1f, 1f);

        /// <summary>
        /// The pack folder. Everything else — the pack's name, where the prefabs are read from and
        /// where the PNGs go — is derived from it.
        /// </summary>
        string rootFolder = "Assets/Process/" + DefaultPack;

        bool applyToAddressables = true;

        // Which group this run's entries are filed into, and therefore which bundle they arrive in.
        // A group is the only lever for what downloads separately, so a run whose content should
        // arrive on its own schedule — skins swapped mid-session, audio fetched when something
        // plays — points at that group instead. Every group is remote and packs by label, so the
        // pack still does the gating whichever one is chosen.
        string prefabGroupName = "Content_Placeables";
        string thumbnailGroupName = "Content_Thumbnails";

        // The manifest categories are inherited from, matched on prefab name. A pack that reuses
        // names already categorised elsewhere — a re-skin of the same set, say — needs nothing
        // typed. Names found in neither this pack's own manifest nor here are reported rather than
        // guessed at.
        string categorySource = PackFolder + "/" + DefaultPack + "/" + ItemsName;

        /// <summary>Group the data assets below are filed into.</summary>
        string dataGroupName = "Content_Agents";

        /// <summary>
        /// Loose assets the running game asks for by a fixed address, one project path per line.
        /// </summary>
        /// <remarks>
        /// The rosters are not prefabs and no thumbnail is rendered for them, so nothing else in
        /// this window would ever register them — and the game reads them during startup, before
        /// anything is on screen to report a failure. Listing them here is what makes a pack
        /// self-sufficient: everything the code asks for is supplied by the run that builds it.
        /// <para>
        /// Their address is the path under the content root without its extension, which is the
        /// rule those two addresses were written under and cannot be changed now — the code names
        /// them literally.
        /// </para>
        /// </remarks>
        string dataAssets =
            "Assets/Dojo/Content/Agents/agents.json\n"
            + "Assets/Dojo/Content/Managers/manager.json";

        /// <summary>
        /// Pixel size of one thumbnail, square.
        /// </summary>
        /// <remarks>
        /// 256 rather than the 100 this used to default to. The inventory draws a tile into a
        /// 104-unit box against a canvas that scales on width from a 1920 reference, so the tile is
        /// 208 pixels on a 4K screen — a 100-pixel source was being upscaled on every display, which
        /// is what made the grid look soft. 256 clears 4K with headroom and is a power of two, so it
        /// mipmaps and block-compresses cleanly.
        /// <para>
        /// Raise it to 384 if 5K matters; that is the @3x the UI sprites are drawn at.
        /// </para>
        /// </remarks>
        int size = 256;
        bool includeSubfolders = true;
        bool transparentBackground = true;
        Color background = new Color(0.24f, 0.24f, 0.24f, 1f);

        // Matches the three-quarter view the inspector preview uses, which reads better for
        // furniture than a straight-on elevation: you can see the seat of a chair and the top of a
        // desk at once.
        float yaw = 135f;
        float pitch = 20f;
        float padding = 1.15f;

        Vector2 scroll;
        string lastResult = "";

        [MenuItem("Tools/Dojo/Addressable Generator")]
        static void Open()
        {
            var window = GetWindow<AddressableGenerator>(true, "Addressable Generator", true);
            window.minSize = new Vector2(460f, 430f);
        }

        void OnEnable()
        {
            var saved = EditorPrefs.GetString(RootKey, string.Empty);

            if (saved.Length == 0)
            {
                // First open since the tool took one folder: start from the pack the old
                // prefab-folder setting pointed into, which was always <pack>/Prefabs.
                var oldSource = EditorPrefs.GetString(SourceKey, string.Empty).Replace('\\', '/').TrimEnd('/');
                var parent = oldSource.Length > 0 ? Path.GetDirectoryName(oldSource) : null;

                saved = string.IsNullOrEmpty(parent) ? rootFolder : parent.Replace('\\', '/');
            }

            rootFolder = saved;
            prefabGroupName = EditorPrefs.GetString(PrefabGroupKey, prefabGroupName);
            thumbnailGroupName = EditorPrefs.GetString(ThumbnailGroupKey, thumbnailGroupName);
            categorySource = EditorPrefs.GetString(CategorySourceKey, categorySource);
        }

        /// <summary>The pack folder, normalised: forward slashes, no trailing slash.</summary>
        string Root => (rootFolder ?? string.Empty).Replace('\\', '/').TrimEnd('/');

        /// <summary>
        /// The pack this run builds: the name of the pack folder.
        /// </summary>
        /// <remarks>
        /// Derived rather than typed. It was a text field, and the field was wrong twice in one
        /// sitting — once producing 57 prefabs under the wrong pack, once handing another pack's
        /// rosters away — because nothing tied the name to the content being read. Taking it from
        /// the folder makes the two impossible to disagree: point the tool at
        /// <c>…/strawberry</c> and the pack is <c>strawberry</c>, with nothing to remember.
        /// </remarks>
        string Pack => Path.GetFileName(Root);

        /// <summary>Where the prefabs are read from.</summary>
        string PrefabFolder => Child(PrefabsFolderName);

        /// <summary>Where the PNGs and <see cref="ManifestName"/> are written.</summary>
        string ThumbnailFolder => Child(ThumbnailsFolderName);

        /// <summary>True when the pack folder exists inside this project's Assets folder.</summary>
        bool RootIsValid => Root.StartsWith("Assets") && AssetDatabase.IsValidFolder(Root);

        /// <summary>
        /// The named folder inside the pack folder, matched regardless of case, or where it would
        /// be when it does not exist yet.
        /// </summary>
        string Child(string name)
        {
            if (AssetDatabase.IsValidFolder(Root))
            {
                foreach (var sub in AssetDatabase.GetSubFolders(Root))
                {
                    if (string.Equals(Path.GetFileName(sub), name, StringComparison.OrdinalIgnoreCase))
                    {
                        return sub;
                    }
                }
            }

            return Root + "/" + name;
        }

        /// <summary>
        /// Every folder in the pack folder other than the prefabs and thumbnails — textures, audio,
        /// whatever the pack carries besides placeable pieces.
        /// </summary>
        List<string> ExtraFolders()
        {
            var extra = new List<string>();

            if (!AssetDatabase.IsValidFolder(Root))
            {
                return extra;
            }

            foreach (var sub in AssetDatabase.GetSubFolders(Root))
            {
                var name = Path.GetFileName(sub);

                if (string.Equals(name, PrefabsFolderName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, ThumbnailsFolderName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                extra.Add(sub);
            }

            extra.Sort(StringComparer.Ordinal);
            return extra;
        }

        /// <summary>
        /// True when the chosen folder holds packs rather than being one — <c>Assets/Process</c>
        /// instead of <c>Assets/Process/strawberry</c>.
        /// </summary>
        /// <remarks>
        /// Worth refusing outright. Every other folder counts as pack content now, so a run over
        /// the parent would label every pack inside it with the parent's name and address them all
        /// under it — relabelling content the run was never meant to touch.
        /// </remarks>
        bool LooksLikeFolderOfPacks()
        {
            if (!AssetDatabase.IsValidFolder(Root) || AssetDatabase.IsValidFolder(PrefabFolder))
            {
                return false;
            }

            foreach (var sub in AssetDatabase.GetSubFolders(Root))
            {
                foreach (var inner in AssetDatabase.GetSubFolders(sub))
                {
                    if (string.Equals(Path.GetFileName(inner), PrefabsFolderName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// The first segment of every address this run assigns: always the pack name.
        /// </summary>
        /// <remarks>
        /// One rule, no exceptions. It was briefly a field so the default pack could keep the
        /// <c>Furniture/</c> addresses its save files were written with — and the first run went
        /// out with the field empty, producing addresses no saved world could resolve. A setting
        /// that is only ever correct once is a setting that will be wrong every other time.
        /// </remarks>
        string Prefix => Pack.Trim();

        /// <summary>Address of the object itself. What a saved world persists.</summary>
        string AddressForPrefab(string prefabName) => Prefix + "/" + prefabName;

        /// <summary>
        /// Address of an object's icon, kept under the same prefix so a pack's whole address space
        /// is one subtree and two packs can ship identically named art without colliding.
        /// </summary>
        string AddressForThumbnail(string thumbnailFile)
            => Prefix + "/" + ThumbnailsFolderName + "/" + Path.GetFileNameWithoutExtension(thumbnailFile);

        /// <summary>
        /// Address of a file in one of the other folders: the pack, the folder's name, then the
        /// path under that folder — the same subtree rule the thumbnails follow.
        /// </summary>
        /// <param name="keepExtension">
        /// True when another file in the same folder has the same name, so the extension is what
        /// tells the two apart.
        /// </param>
        string AddressForExtra(string folder, string assetPath, bool keepExtension)
        {
            var relative = assetPath.Substring(folder.Length + 1);

            if (!keepExtension)
            {
                var dot = relative.LastIndexOf('.');
                var slash = relative.LastIndexOf('/');

                if (dot > slash)
                {
                    relative = relative.Substring(0, dot);
                }
            }

            return Prefix + "/" + Path.GetFileName(folder) + "/" + relative;
        }

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                rootFolder = EditorGUILayout.TextField(
                    new GUIContent("Pack folder",
                        "The pack's own folder, holding " + PrefabsFolderName + ", "
                        + ThumbnailsFolderName + " and anything else the pack carries. Its name is "
                        + "the pack's name."),
                    rootFolder);

                if (GUILayout.Button("Browse", GUILayout.Width(70f)))
                {
                    var picked = EditorUtility.OpenFolderPanel(
                        "The pack's folder (holds " + PrefabsFolderName + ", " + ThumbnailsFolderName
                        + " and any other folders)", "Assets", "");

                    if (!string.IsNullOrEmpty(picked))
                    {
                        rootFolder = ToProjectRelative(picked);
                    }
                }
            }

            includeSubfolders = EditorGUILayout.Toggle(
                new GUIContent("Include subfolders",
                    "Whether prefabs in folders under " + PrefabsFolderName + " are rendered too. "
                    + "The other folders are always read in full."),
                includeSubfolders);

            var rootValid = RootIsValid;
            var folderOfPacks = rootValid && LooksLikeFolderOfPacks();
            var found = 0;
            var extraFiles = 0;

            if (!rootValid)
            {
                EditorGUILayout.HelpBox(
                    Root.StartsWith("Assets")
                        ? "There is no folder at " + Root + "."
                        : "That folder is not inside this project's Assets folder.",
                    MessageType.Warning);
            }
            else if (folderOfPacks)
            {
                EditorGUILayout.HelpBox(
                    "This folder holds packs rather than being one. Pick one of the folders inside "
                    + "it — " + Root + "/<pack> — or every pack in here would be relabelled '"
                    + Pack + "'.",
                    MessageType.Error);
            }
            else
            {
                found = FindPrefabPaths().Count;

                var settings = AddressableAssetSettingsDefaultObject.Settings;
                var lines = new List<string>
                {
                    PrefabsFolderName + "/    " + found + " prefab(s)"
                        + (AssetDatabase.IsValidFolder(PrefabFolder) ? "" : "  — no such folder, nothing to render"),
                    ThumbnailsFolderName + "/    the PNGs and " + ManifestName + " are written here"
                        + (AssetDatabase.IsValidFolder(ThumbnailFolder) ? "" : "  (created on the first run)"),
                };

                foreach (var folder in ExtraFolders())
                {
                    var name = Path.GetFileName(folder);
                    var count = FindExtraAssets(folder).Count;
                    extraFiles += count;

                    lines.Add(name + "/    " + count + " file(s)  →  " + GroupFor(settings, name));
                }

                EditorGUILayout.HelpBox(
                    string.Join("\n", lines),
                    found + extraFiles > 0 ? MessageType.Info : MessageType.Warning);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Pack", EditorStyles.boldLabel);

            // Shown big and first, because it decides the label on every asset this run touches and
            // the address every one of them gets. It is not editable: it comes from the folder, so
            // the only way to change it is to point at different content.
            var packStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter,
            };

            EditorGUILayout.LabelField(
                string.IsNullOrWhiteSpace(Pack) ? "— no pack —" : Pack.ToUpperInvariant(),
                packStyle, GUILayout.Height(26f));

            EditorGUILayout.HelpBox(
                string.IsNullOrWhiteSpace(Pack)
                    ? "The pack is the name of the pack folder, and none is chosen. Point it at "
                      + "something like <staging>/<pack>."
                    : "Taken from the pack folder's name. Everything in this run is labelled '"
                      + Pack + "' and addressed " + Pack + "/…  — pick a different folder to build "
                      + "a different pack.",
                string.IsNullOrWhiteSpace(Pack) ? MessageType.Error : MessageType.Info);

            applyToAddressables = EditorGUILayout.Toggle("Apply to Addressables", applyToAddressables);

            using (new EditorGUI.DisabledScope(!applyToAddressables))
            {
                EditorGUI.indentLevel++;
                prefabGroupName = EditorGUILayout.TextField("Prefab group", prefabGroupName);
                thumbnailGroupName = EditorGUILayout.TextField("Thumbnail group", thumbnailGroupName);

                EditorGUILayout.LabelField(" ", "addresses → " + Prefix + "/<prefab>,   "
                    + Prefix + "/" + ThumbnailsFolderName + "/<icon>");
                EditorGUILayout.LabelField(" ", "other folders → " + Prefix + "/<Folder>/<file>, in "
                    + GroupPrefix + "<Folder> or " + SharedGroup);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Categories", EditorStyles.boldLabel);
            categorySource = EditorGUILayout.TextField(
                new GUIContent("Inherit from",
                    "An existing pack manifest to take categories from, matched on prefab name. A "
                    + "pack reusing names already categorised needs nothing typed. Leave empty to "
                    + "inherit nothing."),
                categorySource);

            EditorGUILayout.LabelField(" ", "writes " + PackFolder + "/" + (string.IsNullOrWhiteSpace(Pack) ? "<pack>" : Pack.Trim()) + "/" + ItemsName);

            EditorGUILayout.HelpBox(
                "The manifest mirrors " + PrefabsFolderName + "/: a prefab added there appears, one "
                + "removed disappears. Each item's category is, in order: one set by hand in "
                + ItemsName + ", a category-named folder inside " + PrefabsFolderName + " (e.g. "
                + PrefabsFolderName + "/Walls), the same name in the manifest above, a word in the "
                + "name (wall, table, chair, monitor…), or '" + ItemCategories.Others + "'.",
                MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Data assets", EditorStyles.boldLabel);

            var isDefaultPack = Pack == DefaultPack;

            if (!isDefaultPack)
            {
                EditorGUILayout.HelpBox(
                    "Only the '" + DefaultPack + "' pack registers these. This run builds '"
                    + (string.IsNullOrWhiteSpace(Pack) ? "—" : Pack)
                    + "', so they are left alone.",
                    MessageType.None);
            }

            using (new EditorGUI.DisabledScope(!applyToAddressables || !isDefaultPack))
            {
                EditorGUILayout.LabelField(" ", "Assets the game asks for by a fixed address.");
                dataGroupName = EditorGUILayout.TextField("Group", dataGroupName);
                dataAssets = EditorGUILayout.TextArea(dataAssets, GUILayout.Height(40f));
            }

            if (string.IsNullOrWhiteSpace(Pack))
            {
                EditorGUILayout.HelpBox(
                    "A pack name is required. '" + DefaultPack + "' is the set every player gets.",
                    MessageType.Warning);
            }
            else if (applyToAddressables)
            {
                EditorGUILayout.HelpBox(
                    "Every prefab in this run, the thumbnail rendered for it, and every file in the"
                    + " pack's other folders is labelled '" + Pack + "' in Addressables — replacing"
                    + " any label it carries now. Run the tool over one Pack's folder at a time, or"
                    + " you will relabel a pack you did not mean to touch.",
                    MessageType.Info);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Image", EditorStyles.boldLabel);
            size = Mathf.Clamp(EditorGUILayout.IntField("Pixel size", size), 16, 1024);
            transparentBackground = EditorGUILayout.Toggle("Transparent background", transparentBackground);
            using (new EditorGUI.DisabledScope(transparentBackground))
            {
                background = EditorGUILayout.ColorField("Background", background);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Framing", EditorStyles.boldLabel);
            yaw = EditorGUILayout.Slider("Yaw", yaw, 0f, 360f);
            pitch = EditorGUILayout.Slider("Pitch", pitch, 0f, 89f);
            padding = EditorGUILayout.Slider("Padding", padding, 1f, 2f);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(
                       !rootValid || folderOfPacks || string.IsNullOrWhiteSpace(Pack) || found + extraFiles <= 0))
            {
                if (GUILayout.Button("Generate Thumbnails", GUILayout.Height(32f)))
                {
                    Generate();
                }
            }

            if (!string.IsNullOrEmpty(lastResult))
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(lastResult, MessageType.None);
            }

            EditorGUILayout.EndScrollView();
        }

        List<string> FindPrefabPaths()
        {
            var paths = new List<string>();
            var folder = PrefabFolder;

            if (!AssetDatabase.IsValidFolder(folder))
            {
                return paths;
            }

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);

                // FindAssets always recurses, so a non-recursive run has to filter by depth.
                if (!includeSubfolders)
                {
                    var parent = Path.GetDirectoryName(path).Replace('\\', '/');
                    if (parent != folder)
                    {
                        continue;
                    }
                }

                paths.Add(path);
            }

            paths.Sort(StringComparer.Ordinal);
            return paths;
        }

        /// <summary>
        /// Every file in one of the pack's other folders, subfolders included, that can go into a
        /// bundle.
        /// </summary>
        /// <remarks>
        /// Scripts, assembly definitions and anything under an <c>Editor</c> folder are left out:
        /// Addressables refuses them, and none of them would exist in a player build anyway.
        /// </remarks>
        static List<string> FindExtraAssets(string folder)
        {
            var paths = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var guid in AssetDatabase.FindAssets(string.Empty, new[] { folder }))
            {
                if (!seen.Add(guid))
                {
                    continue;
                }

                var path = AssetDatabase.GUIDToAssetPath(guid);

                if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path))
                {
                    continue;
                }

                var extension = Path.GetExtension(path).ToLowerInvariant();

                if (extension == ".cs" || extension == ".asmdef" || extension == ".asmref"
                    || path.Contains("/Editor/"))
                {
                    continue;
                }

                paths.Add(path);
            }

            paths.Sort(StringComparer.Ordinal);
            return paths;
        }

        /// <summary>
        /// The group a folder's files are filed into: the one named for the folder when it exists,
        /// otherwise <see cref="SharedGroup"/>.
        /// </summary>
        /// <remarks>
        /// Derived rather than typed, the same trade as the pack name: an <c>Audio</c> folder lands
        /// in <c>Content_Audio</c> with nothing to set, and a kind of content nobody has made a
        /// group for yet still goes somewhere remote rather than nowhere.
        /// </remarks>
        static string GroupFor(AddressableAssetSettings settings, string folderName)
        {
            var wanted = GroupPrefix + folderName;

            if (settings != null)
            {
                foreach (var group in settings.groups)
                {
                    if (group != null && string.Equals(group.Name, wanted, StringComparison.OrdinalIgnoreCase))
                    {
                        return group.Name;
                    }
                }
            }

            return SharedGroup;
        }

        static string ToProjectRelative(string absolute)
        {
            var normalised = absolute.Replace('\\', '/');
            var root = Application.dataPath.Replace('\\', '/');

            return normalised.StartsWith(root)
                ? "Assets" + normalised.Substring(root.Length)
                : normalised;
        }

        void Generate()
        {
            var paths = FindPrefabPaths();

            EditorPrefs.SetString(RootKey, rootFolder);
            EditorPrefs.SetString(PrefabGroupKey, prefabGroupName);
            EditorPrefs.SetString(ThumbnailGroupKey, thumbnailGroupName);
            EditorPrefs.SetString(CategorySourceKey, categorySource);

            var entries = new List<ThumbnailEntry>();
            var skipped = new List<string>();
            var outputFolder = ThumbnailFolder;
            var packReport = "";

            // A pack can be nothing but loose content — audio, textures — with no piece to place.
            // Then there is nothing to render, and the item manifest is left exactly as it was
            // rather than rewritten empty.
            if (paths.Count == 0)
            {
                packReport = "\n\nNo prefabs in " + PrefabFolder + ": nothing rendered, and " + ItemsName
                    + " was left as it was.";
            }
            else
            {
                packReport = RenderPrefabs(paths, outputFolder, entries, skipped);
            }

            // Everything else in the pack folder: labelled and addressed, so it downloads with the
            // pack and can be asked for by name.
            packReport += RegisterExtraFolders();

            // Assets the game asks for by a fixed address — the rosters — which no amount of
            // thumbnail rendering would ever have registered.
            packReport += RegisterDataAssets();

            lastResult = entries.Count + " thumbnail(s) written to\n" + outputFolder
                + "\n\nPack: " + Pack + "   Addresses: " + Prefix + "/…"
                + packReport
                + (skipped.Count > 0 ? "\n\nSkipped " + skipped.Count + ":\n  " + string.Join("\n  ", skipped) : "");

            Debug.Log("[AddressableGenerator] wrote " + entries.Count + " PNG(s) at " + size + "x" + size
                + " to " + outputFolder + " as Pack '" + Pack + "'"
                + (skipped.Count > 0 ? "; skipped " + skipped.Count : ""));
        }

        /// <summary>
        /// Renders every prefab, imports the PNGs as sprites, labels both and writes the pack's item
        /// manifest.
        /// </summary>
        /// <returns>A report for the result box.</returns>
        string RenderPrefabs(List<string> paths, string outputFolder, List<ThumbnailEntry> entries, List<string> skipped)
        {
            Directory.CreateDirectory(outputFolder);

            var readableReport = EnsureMeshesReadable(paths);

            var preview = new PreviewRenderUtility();

            try
            {
                for (var i = 0; i < paths.Count; i++)
                {
                    var path = paths[i];
                    var name = Path.GetFileNameWithoutExtension(path);

                    if (EditorUtility.DisplayCancelableProgressBar(
                            "Addressable Generator", name + "  (" + (i + 1) + "/" + paths.Count + ")",
                            (float)i / Mathf.Max(1, paths.Count)))
                    {
                        break;
                    }

                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null)
                    {
                        skipped.Add(name + " (could not load)");
                        continue;
                    }

                    var png = Render(preview, prefab);
                    if (png == null)
                    {
                        skipped.Add(name + " (no renderers)");
                        continue;
                    }

                    var file = SanitiseFileName(name) + ".png";
                    File.WriteAllBytes(Path.Combine(outputFolder, file), png);

                    entries.Add(new ThumbnailEntry
                    {
                        prefabId = AssetDatabase.AssetPathToGUID(path),
                        prefabName = name,
                        prefabPath = path,
                        prefabResource = AddressForPrefab(name),
                        pack = Pack,
                        thumbnailId = Path.GetFileNameWithoutExtension(file),
                        thumbnailFile = file,
                        thumbnailResource = AddressForThumbnail(file),
                    });
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                preview.Cleanup();
            }

            WriteManifest(entries, outputFolder);

            // The pack folder is always inside the project now, so the PNGs can always be
            // imported — and have to be, before their GUIDs exist to be read.
            AssetDatabase.Refresh();
            ImportAsSprites(entries, outputFolder);
            StampThumbnailGuids(entries, outputFolder);
            WriteManifest(entries, outputFolder);

            // Imported again, and this time the manifest specifically. The write above lands after
            // the refresh, so without this Unity keeps serving the pre-stamp TextAsset — the file
            // on disk is right while anything reading it through the AssetDatabase sees a version
            // with no thumbnail paths in it.
            AssetDatabase.ImportAsset(outputFolder + "/" + ManifestName, ImportAssetOptions.ForceUpdate);

            // Labelling comes last because a thumbnail has no GUID to make addressable until the
            // PNG written above has been imported.
            var report = applyToAddressables ? ApplyToAddressables(entries) : "";

            // The Pack's item manifest: what the inventory reads, and for 'default' the base
            // catalogue. Written after labelling so it describes what actually got registered.
            report += WritePackManifest(entries);

            return readableReport + report;
        }

        /// <summary>
        /// Turns Read/Write on for every model a prefab's meshes come from.
        /// </summary>
        /// <remarks>
        /// A placed piece is baked into the navigation mesh at runtime, and the runtime baker has
        /// to read its mesh. Off, it works in the editor and fails in a player build — with only a
        /// console warning in the editor to say so. Every model in the original set had it on; a
        /// new model imports with it off, so the first pack built from new art hit exactly that.
        /// Set here so a new model needs nothing remembered.
        /// </remarks>
        /// <returns>A report line when anything was changed, otherwise empty.</returns>
        static string EnsureMeshesReadable(List<string> prefabPaths)
        {
            var models = new HashSet<string>(StringComparer.Ordinal);

            foreach (var prefabPath in prefabPaths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                {
                    continue;
                }

                foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mesh = filter.sharedMesh;
                    var modelPath = mesh != null ? AssetDatabase.GetAssetPath(mesh) : null;

                    if (!string.IsNullOrEmpty(modelPath) && AssetImporter.GetAtPath(modelPath) is ModelImporter)
                    {
                        models.Add(modelPath);
                    }
                }
            }

            var changed = new List<string>();

            foreach (var modelPath in models)
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);

                if (importer.isReadable)
                {
                    continue;
                }

                importer.isReadable = true;
                importer.SaveAndReimport();
                changed.Add(Path.GetFileNameWithoutExtension(modelPath));
            }

            return changed.Count == 0
                ? ""
                : "\n\nRead/Write turned on for " + changed.Count + " model(s), so they bake into "
                  + "navigation in a build: " + string.Join(", ", changed.ToArray());
        }

        /// <summary>
        /// Labels and addresses every file in the pack's other folders, so they download with the
        /// pack and can be asked for by name.
        /// </summary>
        /// <remarks>
        /// Addresses drop the extension, as the thumbnails' do. Two files in one folder that differ
        /// only by extension — a texture and a material both called <c>brick</c> — would then claim
        /// the same address, and <see cref="Assign"/> would hand it from one to the other; so for
        /// those the extension is kept, and both stay reachable.
        /// </remarks>
        /// <returns>A report for the result box, empty when the pack has no other folders.</returns>
        string RegisterExtraFolders()
        {
            var folders = ExtraFolders();

            if (folders.Count == 0)
            {
                return "";
            }

            if (!applyToAddressables)
            {
                return "\n\nOther folders: " + folders.Count + " found, not registered — Apply to "
                    + "Addressables is off.";
            }

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                return "";
            }

            settings.AddLabel(Pack, false);

            var problems = new List<string>();
            var groupsUsed = new List<string>();
            var report = "\n\nOther folders:";

            foreach (var folder in folders)
            {
                var name = Path.GetFileName(folder);
                var group = GroupFor(settings, name);
                var assets = FindExtraAssets(folder);

                var claims = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var assetPath in assets)
                {
                    var bare = AddressForExtra(folder, assetPath, false);
                    int count;
                    claims[bare] = claims.TryGetValue(bare, out count) ? count + 1 : 1;
                }

                var registered = 0;

                foreach (var assetPath in assets)
                {
                    var bare = AddressForExtra(folder, assetPath, false);
                    var address = claims[bare] > 1 ? AddressForExtra(folder, assetPath, true) : bare;

                    if (Assign(settings, assetPath, address, group, problems))
                    {
                        registered++;
                    }
                }

                if (!groupsUsed.Contains(group))
                {
                    groupsUsed.Add(group);
                }

                report += "\n  " + name + "/  →  " + group + ": " + registered + "/" + assets.Count
                    + " file(s), addressed " + Prefix + "/" + name + "/…"
                    + (group == SharedGroup && !string.Equals(name, "Shared", StringComparison.OrdinalIgnoreCase)
                        ? "   (no " + GroupPrefix + name + " group)"
                        : "");
            }

            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, null, true, true);
            AssetDatabase.SaveAssets();

            foreach (var group in groupsUsed)
            {
                report += BundlingWarning(settings, group);
            }

            if (problems.Count > 0)
            {
                report += "\n\nOther folders — problems (" + problems.Count + "):\n  "
                    + string.Join("\n  ", problems);
            }

            return report;
        }

        /// <summary>
        /// Puts this run's Pack onto the Addressables entries for every prefab rendered and every
        /// thumbnail written, so the <c>Pack</c> field in the manifest and the label in the
        /// catalogue are the same string by construction rather than by discipline.
        /// </summary>
        /// <remarks>
        /// The label is set to exactly the pack — anything else the entry carried is removed. An
        /// entry labelled both <c>default</c> and a pack would come down with the default set,
        /// which is the one outcome that makes a pack pointless.
        /// </remarks>
        /// <returns>A report for the result box, empty when there was nothing to say.</returns>
        string ApplyToAddressables(List<ThumbnailEntry> entries)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                return "\n\nAddressables: no settings asset in this project — nothing was labelled.";
            }

            settings.AddLabel(Pack, false);

            var problems = new List<string>();
            var prefabsLabelled = 0;
            var iconsLabelled = 0;

            foreach (var entry in entries)
            {
                if (Assign(settings, entry.prefabPath, entry.prefabResource, prefabGroupName, problems))
                {
                    prefabsLabelled++;
                }

                if (Assign(settings, entry.thumbnailPath, entry.thumbnailResource, thumbnailGroupName, problems))
                {
                    iconsLabelled++;
                }
            }

            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, null, true, true);
            AssetDatabase.SaveAssets();

            var report = "\n\nAddressables: labelled '" + Pack + "' on "
                + prefabsLabelled + "/" + entries.Count + " prefab(s) and "
                + iconsLabelled + "/" + entries.Count + " thumbnail(s).";

            // A group that packs everything together emits one bundle whatever the labels say, so
            // the pack would be a filter on paper and not a download boundary. Worth saying out
            // loud here, because nothing else in the pipeline ever will.
            report += BundlingWarning(settings, prefabGroupName);
            report += BundlingWarning(settings, thumbnailGroupName);

            if (problems.Count > 0)
            {
                report += "\n\nAddressables problems (" + problems.Count + "):\n  "
                    + string.Join("\n  ", problems);
            }

            return report;
        }

        /// <summary>
        /// Labels one asset, adding it to <paramref name="groupName"/> when it is not addressable
        /// yet. An asset that already has an entry keeps the group and address it has: this tool
        /// decides Pack membership, not layout.
        /// </summary>
        bool Assign(
            AddressableAssetSettings settings,
            string assetPath,
            string address,
            string groupName,
            List<string> problems)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid))
            {
                problems.Add("no GUID yet for " + assetPath);
                return false;
            }

            var group = settings.FindGroup(groupName);
            if (group == null)
            {
                // Deliberately not falling back to the default group: that one builds locally, so
                // the asset would ship inside the player and the pack would deliver nothing — a
                // failure that looks like success until someone checks the download.
                problems.Add("group '" + groupName + "' does not exist, skipped " + assetPath);
                return false;
            }

            // Take the address over before claiming it. An address is how everything downstream
            // names this object — saved worlds included — so it has to resolve to exactly one
            // asset. Leaving a previous owner in place would let a bundle load either, and which
            // one is not something worth discovering at runtime.
            ReleaseAddress(settings, address, guid, problems);

            var entry = settings.FindAssetEntry(guid);
            if (entry == null)
            {
                entry = settings.CreateOrMoveEntry(guid, group, false, false);
                if (entry == null)
                {
                    problems.Add("could not add " + assetPath + " to " + groupName);
                    return false;
                }
            }

            if (!string.IsNullOrEmpty(address) && entry.address != address)
            {
                entry.SetAddress(address, false);
            }

            SetSoleLabel(entry, Pack);
            return true;
        }

        /// <summary>
        /// Removes any other entry already holding <paramref name="address"/>, so the run can
        /// assign it unambiguously. Removals are reported rather than silent: an address changing
        /// hands is exactly the kind of thing worth seeing in the result box.
        /// </summary>
        void ReleaseAddress(
            AddressableAssetSettings settings,
            string address,
            string keepGuid,
            List<string> problems)
        {
            if (string.IsNullOrEmpty(address))
            {
                return;
            }

            // Collected first: removing while walking the groups' entry lists invalidates them.
            var holders = new List<AddressableAssetEntry>();

            foreach (var group in settings.groups)
            {
                if (group == null)
                {
                    continue;
                }

                foreach (var candidate in group.entries)
                {
                    if (candidate != null
                        && candidate.address == address
                        && candidate.guid != keepGuid)
                    {
                        holders.Add(candidate);
                    }
                }
            }

            foreach (var holder in holders)
            {
                var was = AssetDatabase.GUIDToAssetPath(holder.guid);
                settings.RemoveAssetEntry(holder.guid, false);
                problems.Add("address '" + address + "' taken from " + was);
            }
        }

        /// <summary>
        /// Writes this pack's item manifest and registers it into the pack, so the list of what the
        /// Pack contains travels inside the pack rather than alongside it.
        /// </summary>
        /// <remarks>
        /// Merges rather than overwrites. A category that has already been decided — by hand, or by
        /// an earlier run — is kept; only a name nobody has categorised yet comes out blank, and
        /// those are named in the report instead of being quietly filed under something plausible.
        /// Without that, every re-run would discard the categorisation and the tool would be usable
        /// exactly once.
        /// </remarks>
        string WritePackManifest(List<ThumbnailEntry> entries)
        {
            var folder = PackFolder + "/" + Pack;
            var assetPath = folder + "/" + ItemsName;

            // Categories already decided, by prefab name: the inherited manifest first, then this
            // pack's own, so a correction made by hand here outranks what the inherited one says.
            // Read before the rewrite below, which is what lets a hand-set category survive it.
            var inherited = new Dictionary<string, string>(StringComparer.Ordinal);
            var ownCategories = new Dictionary<string, string>(StringComparer.Ordinal);
            ReadCategories(categorySource, inherited);
            ReadCategories(assetPath, ownCategories);

            // The manifest mirrors the prefab folder. It used to merge with what was there, so a
            // pack assembled over several runs kept every run's items — and so a pack whose
            // prefabs were replaced kept every old item too, pointing at prefabs and icons that no
            // longer existed while the new ones went unlisted. Now that one folder is the whole
            // pack, the folder is the truth: what is in it is in the pack, and nothing else is.
            // The previous order is kept for items that survive, so a re-run does not reshuffle
            // the inventory; new items follow it.
            var previous = ReadItems(assetPath);
            var order = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var existing in previous)
            {
                if (existing != null && !string.IsNullOrEmpty(existing.id) && !order.ContainsKey(existing.id))
                {
                    order[existing.id] = order.Count;
                }
            }

            var items = new List<ItemDefinition>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var guessed = new List<string>();
            var defaulted = new List<string>();

            foreach (var entry in entries)
            {
                string how;
                var category = ResolveCategory(entry, ownCategories, inherited, out how);

                if (how == "name")
                {
                    guessed.Add(Readable(entry.prefabName) + " → " + category);
                }
                else if (how == "default")
                {
                    defaulted.Add(Readable(entry.prefabName));
                }

                var item = new ItemDefinition
                {
                    id = Pack + "." + Slug(entry.prefabName),
                    category = category,
                    name = Readable(entry.prefabName),
                    address = entry.prefabResource,
                    icon = entry.thumbnailResource,
                };

                if (seen.Add(item.id))
                {
                    items.Add(item);
                }
            }

            // Survivors keep their previous order and come first; new items follow in the order
            // the run found them. Both keys are explicit because List.Sort is not stable.
            var runOrder = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < entries.Count; i++)
            {
                var id = Pack + "." + Slug(entries[i].prefabName);
                if (!runOrder.ContainsKey(id)) runOrder[id] = i;
            }

            items.Sort((a, b) =>
            {
                int ia, ib;
                bool ha = order.TryGetValue(a.id, out ia);
                bool hb = order.TryGetValue(b.id, out ib);

                if (ha && hb) return ia.CompareTo(ib);
                if (ha) return -1;
                if (hb) return 1;
                return runOrder[a.id].CompareTo(runOrder[b.id]);
            });

            var removed = new List<string>();
            foreach (var existing in previous)
            {
                if (existing != null && !string.IsNullOrEmpty(existing.id) && !seen.Contains(existing.id))
                {
                    removed.Add(string.IsNullOrEmpty(existing.name) ? existing.id : existing.name);
                }
            }

            Directory.CreateDirectory(folder);

            var manifest = new PackManifest { pack = Pack, version = 1, items = items.ToArray() };
            File.WriteAllText(assetPath, JsonUtility.ToJson(manifest, true));
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            var report = "\n\nPack manifest: " + assetPath
                + "\n  " + items.Count + " item(s), matching " + PrefabsFolderName + "/"
                + (removed.Count > 0
                    ? "\n  removed " + removed.Count + " no longer in the folder: "
                      + string.Join(", ", removed.ToArray())
                    : "");

            if (applyToAddressables)
            {
                var settings = AddressableAssetSettingsDefaultObject.Settings;
                if (settings != null)
                {
                    var problems = new List<string>();
                    var address = PackManifest.AddressFor(Pack);

                    if (Assign(settings, assetPath, address, thumbnailGroupName, problems))
                    {
                        report += "\n  registered at " + address + ", labelled '" + Pack + "'";
                    }

                    foreach (var problem in problems)
                    {
                        report += "\n  " + problem;
                    }

                    settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, null, true, true);
                    AssetDatabase.SaveAssets();
                }
            }

            if (guessed.Count > 0)
            {
                report += "\n\nCategorised from the name (" + guessed.Count + ") — check these; "
                    + "change one in " + ItemsName + " or move its prefab into a category folder "
                    + "and it sticks:\n  " + string.Join("\n  ", guessed.ToArray());
            }

            if (defaulted.Count > 0)
            {
                report += "\n\nFiled under '" + ItemCategories.Others + "' (" + defaulted.Count
                    + ") — nothing in the name said what they are:\n  "
                    + string.Join("\n  ", defaulted.ToArray());
            }

            return report;
        }

        /// <summary>
        /// Words that say what a piece is, and the tab it belongs under. Checked against each word
        /// of a prefab's name, so <c>default_table_orange</c> and <c>OakDesk</c> both land in
        /// tables.
        /// </summary>
        /// <remarks>
        /// Each category's own name, singular and plural, is always recognised as well — so a word
        /// only needs listing here when it is not the category's name. Extend this list rather than
        /// categorising by hand when a new kind of piece arrives.
        /// </remarks>
        static readonly Dictionary<string, string> CategoryWords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "floor", ItemCategories.Floors }, { "tile", ItemCategories.Floors }, { "carpet", ItemCategories.Floors }, { "rug", ItemCategories.Floors },
            { "wall", ItemCategories.Walls }, { "window", ItemCategories.Walls }, { "door", ItemCategories.Walls }, { "separator", ItemCategories.Walls }, { "partition", ItemCategories.Walls },
            { "table", ItemCategories.Tables }, { "desk", ItemCategories.Tables }, { "counter", ItemCategories.Tables },
            { "plant", ItemCategories.Plants }, { "flower", ItemCategories.Plants }, { "tree", ItemCategories.Plants }, { "pot", ItemCategories.Plants }, { "cactus", ItemCategories.Plants },
            { "drawer", ItemCategories.Drawers }, { "cabinet", ItemCategories.Drawers }, { "shelf", ItemCategories.Drawers }, { "shelves", ItemCategories.Drawers }, { "locker", ItemCategories.Drawers }, { "bookcase", ItemCategories.Drawers },
            { "appliance", ItemCategories.Appliances }, { "monitor", ItemCategories.Appliances }, { "screen", ItemCategories.Appliances }, { "tv", ItemCategories.Appliances }, { "pc", ItemCategories.Appliances }, { "computer", ItemCategories.Appliances }, { "laptop", ItemCategories.Appliances }, { "printer", ItemCategories.Appliances }, { "projector", ItemCategories.Appliances }, { "microwave", ItemCategories.Appliances }, { "fridge", ItemCategories.Appliances }, { "vending", ItemCategories.Appliances }, { "dispenser", ItemCategories.Appliances }, { "lamp", ItemCategories.Appliances },
            { "furniture", ItemCategories.Furnitures }, { "chair", ItemCategories.Furnitures }, { "sofa", ItemCategories.Furnitures }, { "couch", ItemCategories.Furnitures }, { "bench", ItemCategories.Furnitures }, { "stool", ItemCategories.Furnitures }, { "armchair", ItemCategories.Furnitures }, { "beanbag", ItemCategories.Furnitures }, { "fatboy", ItemCategories.Furnitures },
        };

        /// <summary>
        /// The category an item is filed under. Never empty: an item with no category is listed
        /// under no tab at all, which reads as the item being missing.
        /// </summary>
        /// <param name="how">
        /// Where the answer came from — <c>set</c>, <c>folder</c>, <c>inherited</c>, <c>name</c> or
        /// <c>default</c> — so the report can point at the ones worth checking.
        /// </param>
        /// <remarks>
        /// In order: a category set by hand in this pack's manifest, because a person decided it;
        /// a category-named folder the prefab sits in under the prefab folder, because putting it
        /// there was a decision too; the same prefab name in the inherited manifest; a word in the
        /// name; and finally <see cref="ItemCategories.Others"/>. The first two are how a guess is
        /// corrected, and a correction made either way survives every later run.
        /// </remarks>
        string ResolveCategory(
            ThumbnailEntry entry,
            Dictionary<string, string> ownCategories,
            Dictionary<string, string> inherited,
            out string how)
        {
            string category;

            if (ownCategories.TryGetValue(entry.prefabName, out category) && !string.IsNullOrEmpty(category))
            {
                how = "set";
                return category;
            }

            var fromFolder = CategoryFromFolder(entry.prefabPath);
            if (fromFolder != null)
            {
                how = "folder";
                return fromFolder;
            }

            if (inherited.TryGetValue(entry.prefabName, out category) && !string.IsNullOrEmpty(category))
            {
                how = "inherited";
                return category;
            }

            foreach (var word in Words(Readable(entry.prefabName)))
            {
                var fromWord = CategoryForWord(word);
                if (fromWord != null)
                {
                    how = "name";
                    return fromWord;
                }
            }

            how = "default";
            return ItemCategories.Others;
        }

        /// <summary>
        /// The category named by a folder the prefab sits in under the prefab folder, or null —
        /// <c>Prefabs/Walls/Brick.prefab</c> is a wall whatever it is called.
        /// </summary>
        string CategoryFromFolder(string prefabPath)
        {
            var root = PrefabFolder + "/";

            if (string.IsNullOrEmpty(prefabPath) || !prefabPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var segments = prefabPath.Substring(root.Length).Split('/');

            // The last segment is the file itself; everything before it is a folder. The nearest
            // folder that names a category wins, so Prefabs/Office/Walls/x reads as a wall.
            for (var i = segments.Length - 2; i >= 0; i--)
            {
                var category = CategoryForWord(segments[i]);
                if (category != null)
                {
                    return category;
                }
            }

            return null;
        }

        /// <summary>
        /// The category a single word points at: a category's own name (singular or plural), or an
        /// entry in <see cref="CategoryWords"/>. Null when it says nothing.
        /// </summary>
        static string CategoryForWord(string word)
        {
            if (string.IsNullOrEmpty(word))
            {
                return null;
            }

            foreach (var category in AllCategories)
            {
                if (string.Equals(word, category, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(word + "s", category, StringComparison.OrdinalIgnoreCase))
                {
                    return category;
                }
            }

            string mapped;
            if (CategoryWords.TryGetValue(word, out mapped))
            {
                return mapped;
            }

            // Plurals of the listed words: chairs, desks, monitors.
            if (word.Length > 3 && word.EndsWith("s", StringComparison.OrdinalIgnoreCase)
                && CategoryWords.TryGetValue(word.Substring(0, word.Length - 1), out mapped))
            {
                return mapped;
            }

            return null;
        }

        /// <summary>Every category a word or folder can name.</summary>
        static readonly string[] AllCategories =
        {
            ItemCategories.Floors, ItemCategories.Walls, ItemCategories.Tables, ItemCategories.Plants,
            ItemCategories.Appliances, ItemCategories.Drawers, ItemCategories.Furnitures,
            ItemCategories.Others, ItemCategories.Skins2D, ItemCategories.Skins3D,
            ItemCategories.Effects, ItemCategories.Audio,
        };

        /// <summary>
        /// The words in a name: split on anything that is not a letter or digit, and at each
        /// lower-to-upper change, so <c>default_table_orange</c>, <c>OfficeChair</c> and
        /// <c>big-drawer 2</c> all come apart the way a person would read them.
        /// </summary>
        static IEnumerable<string> Words(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                yield break;
            }

            var current = new System.Text.StringBuilder();

            for (var i = 0; i < name.Length; i++)
            {
                var c = name[i];

                if (!char.IsLetter(c))
                {
                    if (current.Length > 0) { yield return current.ToString(); current.Length = 0; }
                    continue;
                }

                if (char.IsUpper(c) && current.Length > 0 && char.IsLower(name[i - 1]))
                {
                    yield return current.ToString();
                    current.Length = 0;
                }

                current.Append(c);
            }

            if (current.Length > 0)
            {
                yield return current.ToString();
            }
        }

        /// <summary>
        /// Registers the loose assets the game names by a fixed address, so a pack built by this
        /// tool contains everything the code asks for rather than everything it happened to render.
        /// </summary>
        string RegisterDataAssets()
        {
            if (!applyToAddressables || string.IsNullOrWhiteSpace(dataAssets))
            {
                return "";
            }

            // Base content, owned by the default pack. Every other run skips it rather than relying
            // on whoever is at the keyboard to empty the box first — they will not, and the failure
            // is invisible: the rosters quietly move onto that pack's label and startup stops
            // finding them. A run can only take what its own folder contains.
            if (Pack != DefaultPack)
            {
                return "\n\nData assets: skipped — they belong to the '" + DefaultPack
                    + "' pack, and this run builds '" + Pack + "'.";
            }

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                return "";
            }

            var problems = new List<string>();
            var done = new List<string>();

            foreach (var line in dataAssets.Split('\n'))
            {
                var assetPath = line.Trim();
                if (assetPath.Length == 0)
                {
                    continue;
                }

                if (!File.Exists(assetPath))
                {
                    problems.Add("no asset at " + assetPath);
                    continue;
                }

                var address = ContentRelativeAddress(assetPath);
                if (string.IsNullOrEmpty(address))
                {
                    problems.Add("not under the content root, so it has no address: " + assetPath);
                    continue;
                }

                // Listing an asset here is the claim: the run takes it, whoever held it before.
                // An earlier version refused when another Pack already owned it, which sounds safer
                // and is worse — it preserved whichever run got there first, so a wrong owner could
                // never be corrected by running the right Pack.
                if (Assign(settings, assetPath, address, dataGroupName, problems))
                {
                    done.Add(address);
                }
            }

            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, null, true, true);
            AssetDatabase.SaveAssets();

            var report = "\n\nData assets: " + done.Count + " registered into " + dataGroupName;
            foreach (var address in done)
            {
                report += "\n  " + address;
            }

            foreach (var problem in problems)
            {
                report += "\n  " + problem;
            }

            return report;
        }

        /// <summary>
        /// The pack currently owning an asset's entry, or empty when it has none.
        /// </summary>
        static string OwnerOf(AddressableAssetSettings settings, string assetPath)
        {
            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid))
            {
                return string.Empty;
            }

            var entry = settings.FindAssetEntry(guid);
            if (entry == null)
            {
                return string.Empty;
            }

            foreach (var label in entry.labels)
            {
                return label;
            }

            return string.Empty;
        }

        /// <summary>
        /// The path under the content root without its extension — the rule the fixed addresses in
        /// the code were written under.
        /// </summary>
        static string ContentRelativeAddress(string assetPath)
        {
            const string contentRoot = "Assets/Dojo/Content/";

            var normalised = assetPath.Replace('\\', '/');
            var at = normalised.IndexOf(contentRoot, StringComparison.OrdinalIgnoreCase);

            if (at < 0)
            {
                return string.Empty;
            }

            var relative = normalised.Substring(at + contentRoot.Length);
            var dot = relative.LastIndexOf('.');

            return dot >= 0 ? relative.Substring(0, dot) : relative;
        }

        /// <summary>
        /// Items already recorded in a manifest, or an empty list when there is no readable one.
        /// </summary>
        static List<ItemDefinition> ReadItems(string manifestPath)
        {
            var none = new List<ItemDefinition>();

            if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
            {
                return none;
            }

            PackManifest read;

            try
            {
                read = JsonUtility.FromJson<PackManifest>(File.ReadAllText(manifestPath));
            }
            catch (ArgumentException)
            {
                // Unreadable rather than absent. Returning empty would quietly discard a manifest
                // that is merely malformed, so say so and let the run continue with nothing carried
                // over — the file on disk is still there to fix.
                Debug.LogWarning("[AddressableGenerator] '" + manifestPath + "' could not be read as a pack "
                    + "manifest, so nothing was carried over from it.");
                return none;
            }

            return read != null && read.items != null ? new List<ItemDefinition>(read.items) : none;
        }

        /// <summary>
        /// Reads prefab-name → category out of a manifest, adding to <paramref name="into"/> and
        /// overwriting what is already there. Missing or unreadable files are not an error: the
        /// common case on a pack's first run is that neither file exists yet.
        /// </summary>
        static void ReadCategories(string manifestPath, Dictionary<string, string> into)
        {
            if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
            {
                return;
            }

            PackManifest read;

            try
            {
                read = JsonUtility.FromJson<PackManifest>(File.ReadAllText(manifestPath));
            }
            catch (ArgumentException)
            {
                return;
            }

            if (read == null || read.items == null)
            {
                return;
            }

            foreach (var item in read.items)
            {
                if (item == null || string.IsNullOrEmpty(item.address) || string.IsNullOrEmpty(item.category))
                {
                    continue;
                }

                // Keyed on the prefab's name rather than its address, so a pack that re-skins the
                // same set under a different prefix inherits every category for free.
                var slash = item.address.LastIndexOf('/');
                var name = slash >= 0 ? item.address.Substring(slash + 1) : item.address;

                into[name] = item.category;
            }
        }

        /// <summary>
        /// Lower-case, punctuation-free form of a prefab name, for use in an item id.
        /// </summary>
        /// <remarks>
        /// Built from the readable name, so the asset's "(Prb)" marker never reaches the id. It did
        /// once, and the cost was silent: an id of <c>prbseparator</c> did not match the
        /// <c>separator</c> already in the manifest, so a re-run appended a second copy of every
        /// piece instead of replacing it. An id is the key a merge is decided on — it has to come
        /// from what the thing is, not from how the file happens to be named.
        /// </remarks>
        static string Slug(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName))
            {
                return string.Empty;
            }

            var readable = Readable(prefabName);
            var builder = new System.Text.StringBuilder(readable.Length);

            foreach (var c in readable)
            {
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(char.ToLowerInvariant(c));
                }
            }

            return builder.ToString();
        }

        /// <summary>Strips the pack's "(Prb)" prefix so an item reads as furniture, not as an asset.</summary>
        static string Readable(string prefabName)
            => string.IsNullOrEmpty(prefabName)
                ? prefabName
                : prefabName.Replace("(Prb)", string.Empty).Trim();

        /// <summary>Leaves the entry carrying <paramref name="label"/> and nothing else.</summary>
        static void SetSoleLabel(AddressableAssetEntry entry, string label)
        {
            // Copied first: SetLabel mutates the very set being walked.
            var current = new List<string>(entry.labels);

            foreach (var existing in current)
            {
                if (!string.Equals(existing, label, StringComparison.Ordinal))
                {
                    entry.SetLabel(existing, false, false, false);
                }
            }

            entry.SetLabel(label, true, true, false);
        }

        static string BundlingWarning(AddressableAssetSettings settings, string groupName)
        {
            var group = settings.FindGroup(groupName);
            if (group == null)
            {
                return "";
            }

            var schema = group.GetSchema<BundledAssetGroupSchema>();
            if (schema == null
                || schema.BundleMode == BundledAssetGroupSchema.BundlePackingMode.PackTogetherByLabel)
            {
                return "";
            }

            return "\n\n'" + groupName + "' is set to " + schema.BundleMode + ", so every asset in it"
                + " lands in one bundle no matter which Pack it is labelled. Packs in this group"
                + " gate what the client will use, not what it downloads. Set the group to"
                + " Pack Together By Label to make the pack a real download boundary.";
        }

        /// <summary>
        /// Renders one prefab, framed to its own bounds. Returns null when there is nothing to see.
        /// </summary>
        byte[] Render(PreviewRenderUtility preview, GameObject prefab)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (instance == null)
            {
                return null;
            }

            try
            {
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                var bounds = BoundsOf(instance);
                if (bounds.size == Vector3.zero)
                {
                    return null;
                }

                preview.AddSingleGO(instance);
                preview.BeginStaticPreview(new Rect(0f, 0f, size, size));

                var camera = preview.camera;
                var direction = Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
                var extent = Mathf.Max(bounds.extents.x, Mathf.Max(bounds.extents.y, bounds.extents.z));

                // Orthographic so every icon shares one scale-independent look, and so a long sofa
                // and a small chair are framed the same way rather than by perspective distance.
                camera.transform.position = bounds.center - direction * (extent * 4f);
                camera.transform.rotation = Quaternion.LookRotation(direction);
                camera.orthographic = true;
                camera.orthographicSize = extent * padding;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = extent * 20f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = transparentBackground ? KeyColour : background;

                preview.lights[0].intensity = 1.1f;
                preview.lights[0].transform.rotation = Quaternion.Euler(35f, yaw + 40f, 0f);
                preview.lights[1].intensity = 0.55f;
                preview.lights[1].transform.rotation = Quaternion.Euler(15f, yaw - 130f, 0f);

                camera.Render();

                // EndStaticPreview is the only supported way to read a PreviewRenderUtility back.
                // Rendering its camera into a render texture of our own draws nothing: the preview
                // scene is only current between Begin and End, so every image comes out blank.
                var rendered = preview.EndStaticPreview();
                if (rendered == null)
                {
                    return null;
                }

                try
                {
                    return Encode(rendered);
                }
                finally
                {
                    DestroyImmediate(rendered);
                }
            }
            finally
            {
                DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// Encodes the rendered preview, restoring transparency if it was asked for.
        /// </summary>
        /// <remarks>
        /// The static preview comes back opaque, filled with the camera's background colour. When
        /// a transparent icon is wanted, that background is rendered as pure magenta — a colour no
        /// furniture uses — and swapped back out for alpha here. Keying on an unused colour is
        /// cruder than a real alpha channel but it is the only route the preview API leaves open.
        /// </remarks>
        byte[] Encode(Texture2D rendered)
        {
            if (!transparentBackground)
            {
                return rendered.EncodeToPNG();
            }

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
                DestroyImmediate(output);
            }
        }

        /// <summary>World bounds of everything drawn by the instance.</summary>
        static Bounds BoundsOf(GameObject instance)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new Bounds(Vector3.zero, Vector3.zero);
            }

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        /// <summary>
        /// Fills in each thumbnail's asset GUID, once the PNGs have been imported. Only possible
        /// when the output folder is inside the project.
        /// </summary>
        /// <summary>
        /// Imports the thumbnails just written as sprites.
        /// </summary>
        /// <remarks>
        /// Unity imports a new PNG as a plain texture, and <c>Image</c> can only draw a sprite. The
        /// inventory asks the content service for a <c>Sprite</c>, gets nothing back for a texture,
        /// and renders an empty tile — which is how a grid of blank cards sat over a manifest where
        /// every icon path was correct and every file was present.
        /// <para>
        /// Set here rather than left to the person who runs this. An import setting that has to be
        /// remembered for every new pack is one that will eventually be forgotten, and the symptom
        /// it produces points at the wrong place entirely.
        /// </para>
        /// </remarks>
        void ImportAsSprites(List<ThumbnailEntry> entries, string relativeFolder)
        {
            foreach (var entry in entries)
            {
                var assetPath = relativeFolder.TrimEnd('/') + "/" + entry.thumbnailFile;
                var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;

                if (importer == null)
                {
                    continue;
                }

                if (importer.textureType == TextureImporterType.Sprite
                    && importer.alphaIsTransparency
                    && importer.mipmapEnabled == false)
                {
                    continue;
                }

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;

                // The renders are cut out against nothing, so the alpha is coverage rather than a
                // stored value — without this the edges darken where they fade.
                importer.alphaIsTransparency = true;

                // A thumbnail is drawn at one size on a canvas and never in perspective, so mipmaps
                // are memory spent on levels nothing samples.
                importer.mipmapEnabled = false;

                importer.SaveAndReimport();
            }
        }

        void StampThumbnailGuids(List<ThumbnailEntry> entries, string relativeFolder)
        {
            foreach (var entry in entries)
            {
                var assetPath = relativeFolder.TrimEnd('/') + "/" + entry.thumbnailFile;
                var guid = AssetDatabase.AssetPathToGUID(assetPath);

                if (!string.IsNullOrEmpty(guid))
                {
                    entry.thumbnailId = guid;
                    entry.thumbnailPath = assetPath;

                    // The address was decided when the entry was built, from the pack's prefix
                    // rather than from where the PNG landed on disk — a staging folder outside the
                    // content root has no address to derive.
                }
            }
        }

        void WriteManifest(List<ThumbnailEntry> entries, string outputFolder)
        {
            var manifest = new ThumbnailManifest
            {
                generatedUtc = DateTime.UtcNow.ToString("o"),
                pixelSize = size,
                sourceFolder = PrefabFolder,
                pack = Pack,
                entries = entries.ToArray(),
            };

            File.WriteAllText(Path.Combine(outputFolder, ManifestName), JsonUtility.ToJson(manifest, true));
        }

        static string SanitiseFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            // The pack names its prefabs "(Prb)Sofa3"; the brackets are legal in a file name but
            // awkward in URLs and shell arguments, so they go.
            return name.Replace("(", string.Empty).Replace(")", string.Empty).Trim();
        }

        /// <summary>One prefab and the thumbnail rendered for it.</summary>
        [Serializable]
        public sealed class ThumbnailEntry
        {
            /// <summary>The prefab's asset GUID — stable across renames and moves.</summary>
            public string prefabId;

            public string prefabName;

            /// <summary>Project path. Editor-only: a build cannot resolve it.</summary>
            public string prefabPath;

            /// <summary>
            /// Content address, or empty when the prefab is not under the content root. This is the
            /// one a running game can load from, and the one saved worlds persist.
            /// </summary>
            public string prefabResource;

            /// <summary>
            /// The thumbnail's asset GUID when it was written inside the project, otherwise the
            /// PNG's file name without its extension.
            /// </summary>
            public string thumbnailId;

            public string thumbnailFile;

            /// <summary>Project path. Editor-only, like <see cref="prefabPath"/>.</summary>
            public string thumbnailPath;

            /// <summary>Content address, or empty when not under the content root.</summary>
            public string thumbnailResource;

            /// <summary>
            /// Which Pack this piece belongs to, and the same string as the Addressables label on
            /// both its prefab and its thumbnail. A client holding this key can render the piece;
            /// one without it cannot, and should not be offered it as placeable.
            /// </summary>
            public string pack;
        }

        [Serializable]
        public sealed class ThumbnailManifest
        {
            public string generatedUtc;
            public int pixelSize;
            public string sourceFolder;

            /// <summary>The pack the run was generated under. Per-entry <c>Pack</c> is what reads.</summary>
            public string pack;

            public ThumbnailEntry[] entries;
        }
    }
}
