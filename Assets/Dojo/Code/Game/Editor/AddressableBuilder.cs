using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Dojo.Game.Editor
{
    /// <summary>
    /// <c>Tools ▸ Dojo ▸ Addressables ▸ 5. Addressable Builder</c>: processes every pack in
    /// <c>Assets/Process</c> and builds the content as the next version, straight into the folder
    /// the game loads from.
    /// </summary>
    /// <remarks>
    /// <b>Process Addressable Folder</b> runs the Addressable Generator over each pack folder in
    /// turn, 'default' first — the same code its Generate Thumbnails button runs, with the settings
    /// shown here. <b>Build</b> builds the content as the output folder's next version: the
    /// catalogue and bundles go straight into that folder, <c>version.json</c> is written after
    /// them, and the files of earlier builds are removed, so there is nothing to copy or clean up
    /// afterwards. See <see cref="ContentRelease"/>.
    /// </remarks>
    public sealed class AddressableBuilder : EditorWindow
    {
        const string OptionsKey = "Dojo.AddressableBuilder.Options";
        const string OutputKey = "Dojo.AddressableBuilder.Output";
        const string Log = "[Addressable Builder]";

        AddressableGenerator.PackOptions options = new AddressableGenerator.PackOptions();
        string outputFolder = string.Empty;

        readonly List<string> packFolders = new List<string>();
        readonly Dictionary<string, AddressableGenerator.PackSummary> summaries =
            new Dictionary<string, AddressableGenerator.PackSummary>();

        Vector2 scroll;
        bool processing;
        string processResult = string.Empty;
        string buildResult = string.Empty;

        [MenuItem("Tools/Dojo/Addressables/5. Addressable Builder", false, 5)]
        static void Open()
        {
            var window = GetWindow<AddressableBuilder>(true, "Addressable Builder", true);
            window.minSize = new Vector2(480f, 520f);
        }

        void OnEnable()
        {
            var saved = EditorPrefs.GetString(OptionsKey, string.Empty);
            if (saved.Length > 0)
            {
                try
                {
                    JsonUtility.FromJsonOverwrite(saved, options);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(Log + " saved settings could not be read, using the defaults: " + exception.Message);
                }
            }

            outputFolder = EditorPrefs.GetString(OutputKey, string.Empty);
            RefreshPacks();
        }

        void OnFocus() => RefreshPacks();

        void OnProjectChange()
        {
            RefreshPacks();
            Repaint();
        }

        void SaveSettings()
        {
            EditorPrefs.SetString(OptionsKey, JsonUtility.ToJson(options));
            EditorPrefs.SetString(OutputKey, outputFolder ?? string.Empty);
        }

        /// <summary>Finds every pack folder in Assets/Process again and what each holds.</summary>
        void RefreshPacks()
        {
            // Processing imports assets, which calls OnProjectChange and OnFocus. Rebuilding the
            // list then would pull it from under ProcessAll's loop; ProcessAll refreshes at the end.
            if (processing)
            {
                return;
            }

            packFolders.Clear();
            summaries.Clear();

            var absolute = Path.Combine(ProjectRoot, ContentRelease.ProcessFolder);
            foreach (var name in ContentRelease.PackNames(absolute))
            {
                var folder = ContentRelease.ProcessFolder + "/" + name;
                packFolders.Add(folder);
                summaries[folder] = AddressableGenerator.Summarise(folder, options);
            }
        }

        static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUI.BeginChangeCheck();

            DrawPacks();
            DrawSettings();

            EditorGUILayout.Space();
            var processable = packFolders.FindAll(folder => summaries[folder].CanProcess).Count;
            using (new EditorGUI.DisabledScope(processable == 0))
            {
                if (GUILayout.Button("Process Addressable Folder", GUILayout.Height(32f)))
                {
                    SaveSettings();
                    ProcessAll();
                    GUIUtility.ExitGUI();
                }
            }

            if (!string.IsNullOrEmpty(processResult))
            {
                EditorGUILayout.HelpBox(processResult, MessageType.None);
            }

            EditorGUILayout.Space(12f);
            DrawBuild();

            if (EditorGUI.EndChangeCheck())
            {
                SaveSettings();
            }

            EditorGUILayout.EndScrollView();
        }

        void DrawPacks()
        {
            EditorGUILayout.LabelField("Packs", EditorStyles.boldLabel);

            if (packFolders.Count == 0)
            {
                EditorGUILayout.HelpBox("There are no pack folders in " + ContentRelease.ProcessFolder + ".", MessageType.Warning);
                return;
            }

            EditorGUILayout.HelpBox("Every folder in " + ContentRelease.ProcessFolder + " is processed, one pack at a "
                + "time, '" + AddressableGenerator.DefaultPackName + "' first. Each pack's content is labelled and "
                + "addressed with its own folder's name.", MessageType.None);

            var packStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };

            foreach (var folder in packFolders)
            {
                var summary = summaries[folder];
                EditorGUILayout.LabelField(summary.Pack.ToUpperInvariant() + "    " + folder, packStyle, GUILayout.Height(20f));
                EditorGUILayout.HelpBox(summary.Text, summary.CanProcess ? MessageType.Info : MessageType.Warning);
            }

            options.includeSubfolders = EditorGUILayout.Toggle(
                new GUIContent("Include subfolders",
                    "Whether prefabs in folders under Prefabs are rendered too. The other folders are always read in full."),
                options.includeSubfolders);
        }

        void DrawSettings()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Addressables", EditorStyles.boldLabel);
            options.applyToAddressables = EditorGUILayout.Toggle("Apply to Addressables", options.applyToAddressables);

            using (new EditorGUI.DisabledScope(!options.applyToAddressables))
            {
                EditorGUI.indentLevel++;
                options.prefabGroupName = EditorGUILayout.TextField("Prefab group", options.prefabGroupName);
                options.thumbnailGroupName = EditorGUILayout.TextField("Thumbnail group", options.thumbnailGroupName);
                EditorGUILayout.LabelField(" ", "addresses → <pack>/<prefab>,   <pack>/Thumbnails/<icon>");
                EditorGUILayout.LabelField(" ", "other folders → <pack>/<Folder>/<file>, in Content_<Folder> or Content_Shared");
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Categories", EditorStyles.boldLabel);
            options.categorySource = EditorGUILayout.TextField(
                new GUIContent("Inherit from",
                    "An existing pack manifest to take categories from, matched on prefab name. Leave empty to inherit nothing."),
                options.categorySource);
            EditorGUILayout.LabelField(" ", "writes Assets/Dojo/Content/Packs/<pack>/items.json");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Data assets", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Registered by the '" + AddressableGenerator.DefaultPackName + "' pack only; every "
                + "other pack leaves them alone.", MessageType.None);

            using (new EditorGUI.DisabledScope(!options.applyToAddressables))
            {
                EditorGUILayout.LabelField(" ", "Assets the game asks for by a fixed address.");
                options.dataGroupName = EditorGUILayout.TextField("Group", options.dataGroupName);
                options.dataAssets = EditorGUILayout.TextArea(options.dataAssets, GUILayout.Height(40f));
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Image", EditorStyles.boldLabel);
            options.size = Mathf.Clamp(EditorGUILayout.IntField("Pixel size", options.size), 16, 1024);
            options.transparentBackground = EditorGUILayout.Toggle("Transparent background", options.transparentBackground);
            using (new EditorGUI.DisabledScope(options.transparentBackground))
            {
                options.background = EditorGUILayout.ColorField("Background", options.background);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Framing", EditorStyles.boldLabel);
            options.yaw = EditorGUILayout.Slider("Yaw", options.yaw, 0f, 360f);
            options.pitch = EditorGUILayout.Slider("Pitch", options.pitch, 0f, 89f);
            options.padding = EditorGUILayout.Slider("Padding", options.padding, 1f, 2f);
        }

        void DrawBuild()
        {
            EditorGUILayout.LabelField("Build", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                outputFolder = EditorGUILayout.TextField(
                    new GUIContent("Save to",
                        "The folder the game loads content from: version.json, the catalogue and every bundle go straight in here."),
                    outputFolder);

                if (GUILayout.Button("Browse", GUILayout.Width(70f)))
                {
                    var picked = EditorUtility.OpenFolderPanel("Where the built content is saved", outputFolder, string.Empty);
                    if (!string.IsNullOrEmpty(picked))
                    {
                        outputFolder = picked.Replace('/', Path.DirectorySeparatorChar);
                        SaveSettings();
                        GUI.FocusControl(null);
                    }
                }
            }

            var folder = (outputFolder ?? string.Empty).Trim();
            var next = -1;

            if (folder.Length == 0)
            {
                EditorGUILayout.HelpBox("Choose where to save the built content.", MessageType.Warning);
            }
            else if (!Path.IsPathRooted(folder))
            {
                EditorGUILayout.HelpBox("Use a full path, for example E:\\xamp\\htdocs\\...\\development.", MessageType.Warning);
            }
            else
            {
                try
                {
                    var current = Directory.Exists(folder) ? ContentRelease.CurrentVersion(folder) : null;
                    next = ContentRelease.NextVersion(current);

                    var line = current.HasValue
                        ? "Current version: " + current.Value + "    →    Build makes version " + next
                        : "No version here yet    →    Build makes version " + next
                          + (Directory.Exists(folder) ? string.Empty : "  (the folder is created)");

                    var buildFiles = ContentRelease.BuildFiles(folder);
                    if (buildFiles.Count > 0)
                    {
                        line += "\n" + buildFiles.Count + " build file(s) here now; Build replaces them with version "
                            + next + "'s, so only one release is ever in the folder.";
                    }

                    var other = ContentRelease.OtherFiles(folder);
                    if (other.Count > 0)
                    {
                        line += "\nNot build files, left alone: " + string.Join(", ", other);
                    }

                    EditorGUILayout.HelpBox(line, MessageType.Info);
                }
                catch (Exception exception)
                {
                    next = -1;
                    EditorGUILayout.HelpBox(exception.Message, MessageType.Error);
                }
            }

            using (new EditorGUI.DisabledScope(next < 0))
            {
                if (GUILayout.Button(next < 0 ? "Build" : "Build  (version " + next + ")", GUILayout.Height(32f)))
                {
                    SaveSettings();
                    Build(folder, next);
                    GUIUtility.ExitGUI();
                }
            }

            if (!string.IsNullOrEmpty(buildResult))
            {
                EditorGUILayout.HelpBox(buildResult, MessageType.None);
            }
        }

        /// <summary>Runs the Addressable Generator over every pack folder, 'default' first.</summary>
        void ProcessAll()
        {
            RefreshPacks();

            var reports = new List<string>();
            var problems = new List<string>();

            // A copy of the list as it is now, so nothing done while processing can change it.
            var work = new List<KeyValuePair<string, AddressableGenerator.PackSummary>>();
            foreach (var folder in packFolders)
            {
                work.Add(new KeyValuePair<string, AddressableGenerator.PackSummary>(folder, summaries[folder]));
            }

            processing = true;
            try
            {
                foreach (var pair in work)
                {
                    var folder = pair.Key;
                    var summary = pair.Value;
                    if (!summary.CanProcess)
                    {
                        reports.Add("── " + summary.Pack + ": skipped. " + summary.Text.Replace("\n", " "));
                        continue;
                    }

                    try
                    {
                        Debug.Log(Log + " processing " + folder);
                        var result = AddressableGenerator.Process(folder, options);
                        reports.Add("── " + result.Pack + " ──\n" + result.Report);

                        if (result.Skipped > 0)
                        {
                            problems.Add(result.Pack + ": " + result.Skipped + " prefab(s) could not be rendered (see the report).");
                        }
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                        problems.Add(summary.Pack + ": " + exception.Message);
                        reports.Add("── " + summary.Pack + ": FAILED — " + exception.Message);
                    }
                }
            }
            finally
            {
                processing = false;
            }

            processResult = string.Join("\n\n", reports);
            RefreshPacks();

            if (problems.Count > 0)
            {
                Debug.LogError(Log + " processing finished with problems:\n" + string.Join("\n", problems));
                EditorUtility.DisplayDialog("Addressable folders: problems", string.Join("\n", problems), "OK");
            }
            else
            {
                Debug.Log(Log + " processed " + reports.Count + " pack folder(s).");
            }
        }

        /// <summary>
        /// Builds the content as <paramref name="version"/> and puts it in <paramref name="folder"/>:
        /// built into a temporary folder inside the project, then installed by <see cref="ContentRelease.Install"/>.
        /// </summary>
        void Build(string folder, int version)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Fail("There are no Addressables settings in this project.");
                return;
            }

            if (!EditorUtility.DisplayDialog("Build content version " + version,
                    "Builds the content as version " + version + " into:\n" + folder + "\n\n"
                    + "The catalogue and bundles go straight into that folder, version.json is set to " + version
                    + ", and the files of earlier builds are removed. Nothing to copy afterwards.",
                    "Build version " + version, "Cancel"))
            {
                return;
            }

            var staging = Path.Combine(ProjectRoot, "Temp", "AddressableBuilder", "build_" + version.ToString(CultureInfo.InvariantCulture));
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, true);
            }

            Directory.CreateDirectory(staging);

            var profiles = settings.profileSettings;
            var profile = settings.activeProfileId;
            var previousPath = profiles.GetValueByName(profile, AddressableAssetSettings.kRemoteBuildPath);
            var previousVersion = settings.OverridePlayerVersion;
            var previousMonoScriptNaming = settings.MonoScriptBundleNaming;
            var previousMonoScriptPrefix = settings.MonoScriptBundleCustomNaming;
            var previousBuiltInNaming = settings.BuiltInBundleNaming;
            var previousBuiltInPrefix = settings.BuiltInBundleCustomNaming;

            AddressablesPlayerBuildResult result;
            string catalogFolder;

            try
            {
                // For this build only: the remote build path is pointed at the temporary folder and
                // the catalogue is named for this version. Both go back afterwards, so the profile
                // and the other menus are untouched.
                profiles.SetValue(profile, AddressableAssetSettings.kRemoteBuildPath, staging);
                settings.OverridePlayerVersion = version.ToString(CultureInfo.InvariantCulture);

                // The script-references and built-in bundles are named from the project, not their
                // contents, so they keep their name while their bytes change. The CDN caches every
                // bundle for a year, so a reused name serves the old bytes and fails the catalogue's
                // CRC check (17 Nov, version 1). Named after the version, they never reuse a path.
                var prefix = ContentRelease.BundlePrefix(version);
                settings.MonoScriptBundleNaming = MonoScriptBundleNaming.Custom;
                settings.MonoScriptBundleCustomNaming = prefix;
                settings.BuiltInBundleNaming = BuiltInBundleNaming.Custom;
                settings.BuiltInBundleCustomNaming = prefix;

                catalogFolder = settings.RemoteCatalogBuildPath != null
                    ? settings.RemoteCatalogBuildPath.GetValue(settings)
                    : null;
                catalogFolder = string.IsNullOrWhiteSpace(catalogFolder) ? staging : Path.GetFullPath(catalogFolder);

                AddressableAssetSettings.BuildPlayerContent(out result);
            }
            finally
            {
                profiles.SetValue(profile, AddressableAssetSettings.kRemoteBuildPath, previousPath);
                settings.OverridePlayerVersion = previousVersion;
                settings.MonoScriptBundleNaming = previousMonoScriptNaming;
                settings.MonoScriptBundleCustomNaming = previousMonoScriptPrefix;
                settings.BuiltInBundleNaming = previousBuiltInNaming;
                settings.BuiltInBundleCustomNaming = previousBuiltInPrefix;
                AssetDatabase.SaveAssets();
            }

            if (result == null || !string.IsNullOrEmpty(result.Error))
            {
                Fail("The Addressables build failed, so nothing was changed in " + folder + ":\n"
                    + (result == null ? "no result" : result.Error));
                return;
            }

            var bundles = new List<string>();
            if (result.AssetBundleBuildResults != null)
            {
                foreach (var bundle in result.AssetBundleBuildResults)
                {
                    bundles.Add(Path.GetFullPath(bundle.FilePath));
                }
            }

            ContentRelease.Installed installed;
            try
            {
                Directory.CreateDirectory(folder);
                installed = ContentRelease.Install(folder, version, catalogFolder, bundles, DateTime.UtcNow);
            }
            catch (Exception exception)
            {
                Fail(exception.Message);
                return;
            }
            finally
            {
                try
                {
                    Directory.Delete(staging, true);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(Log + " could not remove the temporary build folder " + staging + ": " + exception.Message);
                }
            }

            buildResult = "Version " + version + " built " + DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture)
                + "\n" + ContentRelease.CatalogFileName(version) + " and " + installed.Bundles + " bundle(s) in " + folder
                + "\nversion.json there now says " + version + "."
                + (installed.Removed.Count > 0
                    ? "\nRemoved " + installed.Removed.Count + " file(s) from earlier builds."
                    : string.Empty);

            Debug.Log(Log + " " + buildResult.Replace("\n", " "));
            EditorUtility.DisplayDialog("Content version " + version + " built", buildResult, "OK");
        }

        void Fail(string message)
        {
            buildResult = "Not built: " + message;
            Debug.LogError(Log + " " + message);
            EditorUtility.DisplayDialog("Content not built", message, "OK");
        }
    }
}
