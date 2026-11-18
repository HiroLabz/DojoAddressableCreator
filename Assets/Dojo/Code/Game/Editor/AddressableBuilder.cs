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
    /// shown here. <b>Build</b> builds the content as the next version after the one published on
    /// GitHub (DojoAddressables' main, see <see cref="ContentRepo"/>), so every machine counts the
    /// same: the catalogue and bundles go straight into the checkout's development folder,
    /// <c>version.json</c> is written after them, the files of earlier builds are removed, and the
    /// result is committed. <b>Publish</b> pushes it, which uploads it to the CDN; a push someone
    /// else beat is discarded and built again as the next version. See <see cref="ContentRelease"/>.
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

        // The DojoAddressables checkout as last read, and what it allows.
        ContentRepo.State repoState;
        ContentRepo.Verdict verdict;
        string checkedFolder;
        string localhostNote;
        bool localhostServes;

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

            // GitHub is read once the window is up, so the version shown is never a stale local one.
            EditorApplication.delayCall += () =>
            {
                if (this != null)
                {
                    Check();
                }
            };
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
            EditorGUILayout.HelpBox("Builds into the '" + ContentRepo.ReleaseFolder + "' folder of your " + ContentRepo.Remote
                + " checkout. The version is counted from GitHub's " + ContentRepo.Branch + ", so it is the same on every "
                + "machine, and Publish pushes the build there, which uploads it to the CDN.", MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                outputFolder = EditorGUILayout.TextField(
                    new GUIContent("Release folder",
                        "The '" + ContentRepo.ReleaseFolder + "' folder of your DojoAddressables checkout, served by your web "
                        + "server at " + ContentRepo.LocalhostRoot + "."),
                    outputFolder);

                if (GUILayout.Button("Browse", GUILayout.Width(70f)))
                {
                    var picked = EditorUtility.OpenFolderPanel("The development folder of your DojoAddressables checkout",
                        outputFolder, string.Empty);
                    if (!string.IsNullOrEmpty(picked))
                    {
                        outputFolder = picked.Replace('/', Path.DirectorySeparatorChar);
                        SaveSettings();
                        GUI.FocusControl(null);
                        Check();
                        GUIUtility.ExitGUI();
                    }
                }

                if (GUILayout.Button("Check", GUILayout.Width(70f)))
                {
                    SaveSettings();
                    Check();
                    GUIUtility.ExitGUI();
                }
            }

            var folder = (outputFolder ?? string.Empty).Trim();

            if (verdict == null || !string.Equals(checkedFolder, folder, StringComparison.OrdinalIgnoreCase))
            {
                var shape = ContentRepo.FolderShapeProblem(folder);
                EditorGUILayout.HelpBox(shape ?? "Press Check to read GitHub's version for this folder.",
                    shape != null ? MessageType.Error : MessageType.Info);
                DrawBuildButton(-1);
            }
            else
            {
                EditorGUILayout.HelpBox(verdict.Message, verdict.Step == ContentRepo.Step.Build ? MessageType.Info
                    : verdict.Step == ContentRepo.Step.Blocked ? MessageType.Error : MessageType.Warning);

                if (!string.IsNullOrEmpty(localhostNote))
                {
                    EditorGUILayout.HelpBox(localhostNote, localhostServes ? MessageType.None : MessageType.Warning);
                }

                DrawSteps(folder);
            }

            if (!string.IsNullOrEmpty(buildResult))
            {
                EditorGUILayout.HelpBox(buildResult, MessageType.None);
            }
        }

        /// <summary>The buttons the verdict allows: Update, Discard, Publish, or Build.</summary>
        void DrawSteps(string folder)
        {
            var local = repoState != null && repoState.Local.HasValue ? repoState.Local.Value : -1;

            switch (verdict.Step)
            {
                case ContentRepo.Step.Update:
                    if (GUILayout.Button("Update from GitHub", GUILayout.Height(28f)))
                    {
                        RunGit("Updating from GitHub", () => ContentRepo.Update(repoState.Root));
                        GUIUtility.ExitGUI();
                    }

                    break;

                case ContentRepo.Step.DiscardChanges:
                    if (GUILayout.Button("Discard the changes in " + ContentRepo.ReleaseFolder + "/", GUILayout.Height(28f))
                        && EditorUtility.DisplayDialog("Discard changes",
                            "Throw away the " + repoState.Uncommitted.Count + " uncommitted change(s) in\n" + folder
                            + "\nso it matches the last release? Nothing outside it is touched.", "Discard", "Cancel"))
                    {
                        RunGit("Discarding changes", () => ContentRepo.DiscardChanges(repoState.Root));
                        GUIUtility.ExitGUI();
                    }

                    break;

                case ContentRepo.Step.PublishOrDiscard:
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Publish version " + local, GUILayout.Height(28f)))
                        {
                            Publish(local);
                            GUIUtility.ExitGUI();
                        }

                        if (GUILayout.Button("Discard version " + local, GUILayout.Height(28f))
                            && EditorUtility.DisplayDialog("Discard version " + local,
                                "Drop the unpublished version " + local + " and go back to GitHub's release?", "Discard", "Cancel"))
                        {
                            RunGit("Discarding version " + local, () => ContentRepo.DiscardBuild(repoState.Root));
                            GUIUtility.ExitGUI();
                        }
                    }

                    break;

                case ContentRepo.Step.DiscardBuild:
                    if (GUILayout.Button("Discard version " + local, GUILayout.Height(28f)))
                    {
                        RunGit("Discarding version " + local, () => ContentRepo.DiscardBuild(repoState.Root));
                        GUIUtility.ExitGUI();
                    }

                    break;
            }

            DrawBuildButton(verdict.Step == ContentRepo.Step.Build ? verdict.Next : -1);
        }

        void DrawBuildButton(int next)
        {
            using (new EditorGUI.DisabledScope(next < 0))
            {
                if (GUILayout.Button(next < 0 ? "Build" : "Build  (version " + next + ")", GUILayout.Height(32f)))
                {
                    SaveSettings();
                    Build((outputFolder ?? string.Empty).Trim(), next);
                    GUIUtility.ExitGUI();
                }
            }
        }

        /// <summary>
        /// Reads the checkout and GitHub again, and whether the game's Localhost address serves this
        /// folder.
        /// </summary>
        void Check()
        {
            var folder = (outputFolder ?? string.Empty).Trim();

            try
            {
                EditorUtility.DisplayProgressBar("Addressable Builder", "Reading GitHub's version…", 0.5f);
                repoState = ContentRepo.Read(folder);
                verdict = ContentRepo.Decide(repoState);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            checkedFolder = folder;
            CheckLocalhost();
            Repaint();
        }

        /// <summary>
        /// Asks the game's Localhost address for version.json and compares it with this folder's.
        /// Only a warning: publishing does not need it, testing with Content Source = Localhost does.
        /// </summary>
        void CheckLocalhost()
        {
            localhostNote = null;
            localhostServes = false;

            if (repoState == null || repoState.FolderProblem != null)
            {
                return;
            }

            var url = ContentRepo.LocalhostRoot + "/" + ContentRelease.VersionFileName;

            try
            {
                var request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                request.Timeout = 3000;
                request.ReadWriteTimeout = 3000;

                using (var response = request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream()))
                {
                    var served = ContentRelease.ParseVersion(reader.ReadToEnd(), url);
                    localhostServes = served == repoState.Local;
                    localhostNote = localhostServes
                        ? "Localhost serves this folder (" + url + " says version " + served + ")."
                        : url + " says version " + served + ", but this folder has "
                          + (repoState.Local.HasValue ? "version " + repoState.Local.Value : "none")
                          + ": your web server serves a different folder, so the game's Localhost source will not see these builds.";
                }
            }
            catch (Exception exception)
            {
                localhostNote = url + " did not answer (" + exception.Message + "). Only testing with Content Source = "
                    + "Localhost needs it: point your web server at this checkout.";
            }
        }

        /// <summary>Runs a git step, says so if it fails, then reads everything again.</summary>
        void RunGit(string what, Action step)
        {
            try
            {
                EditorUtility.DisplayProgressBar("Addressable Builder", what + "…", 0.5f);
                step();
            }
            catch (Exception exception)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogError(Log + " " + what + " failed: " + exception.Message);
                EditorUtility.DisplayDialog(what + " failed", exception.Message, "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Check();
        }

        /// <summary>
        /// Pushes the built version to GitHub's main, which publishes it. When someone else published
        /// first, GitHub refuses the push: this build is discarded and the next one counts from theirs.
        /// </summary>
        void Publish(int version)
        {
            var root = repoState != null ? repoState.Root : null;
            if (root == null)
            {
                Fail("The DojoAddressables checkout has not been read.");
                return;
            }

            bool published;
            try
            {
                EditorUtility.DisplayProgressBar("Addressable Builder", "Publishing version " + version + "…", 0.5f);
                published = ContentRepo.Publish(root);
            }
            catch (Exception exception)
            {
                EditorUtility.ClearProgressBar();
                Fail(exception.Message);
                Check();
                return;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (published)
            {
                buildResult = "Version " + version + " published " + DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture)
                    + ": pushed to " + ContentRepo.Remote + " " + ContentRepo.Branch + ". Its GitHub Action uploads it to the CDN.";
                Debug.Log(Log + " " + buildResult);
                EditorUtility.DisplayDialog("Version " + version + " published", buildResult, "OK");
                Check();
                return;
            }

            // Someone pushed first: their release has the number. Ours is dropped and counted again.
            RunGit("Discarding version " + version, () => ContentRepo.DiscardBuild(root));
            var theirs = repoState != null && repoState.Published.HasValue ? repoState.Published.Value.ToString(CultureInfo.InvariantCulture) : "?";
            buildResult = "Not published: someone else published version " + theirs + " first, so this machine's version "
                + version + " was discarded and the folder is back on GitHub's release. Build again to make the next version.";
            Debug.LogWarning(Log + " " + buildResult);
            EditorUtility.DisplayDialog("Version " + version + " not published", buildResult, "OK");
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

            // GitHub is read again right before building: someone may have published since the
            // window last looked, and the number must count from theirs.
            Check();
            if (verdict == null || verdict.Step != ContentRepo.Step.Build || verdict.Next != version)
            {
                Fail(verdict != null && verdict.Step == ContentRepo.Step.Build
                    ? "GitHub moved on while the window was open: the next version is now " + verdict.Next + ", not "
                      + version + ". Press Build again."
                    : (verdict != null ? verdict.Message : "The DojoAddressables checkout could not be read."));
                return;
            }

            var root = repoState.Root;

            if (!EditorUtility.DisplayDialog("Build content version " + version,
                    "GitHub has version " + (version - 1) + ", so this build is version " + version + ", built into:\n"
                    + folder + "\n\n"
                    + "The catalogue and bundles go straight into that folder, version.json is set to " + version
                    + ", the files of earlier builds are removed, and the result is committed. You are asked before "
                    + "anything is published.",
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

                // Every bundle is named after the version too, not only the script-references and
                // built-in ones: see VersionedPackedMode (content version 3, 18 Nov).
                result = VersionedPackedMode.Build(settings, version);
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

            try
            {
                ContentRepo.Commit(root, version);
            }
            catch (Exception exception)
            {
                Fail("Version " + version + " is built into " + folder + " but could not be committed, so it is not "
                    + "published:\n" + exception.Message);
                Check();
                return;
            }

            buildResult = "Version " + version + " built " + DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture)
                + "\n" + ContentRelease.CatalogFileName(version) + " and " + installed.Bundles + " bundle(s) in " + folder
                + "\nversion.json there now says " + version + ", committed as \"Content version " + version + "\"."
                + (installed.Removed.Count > 0
                    ? "\nRemoved " + installed.Removed.Count + " file(s) from earlier builds."
                    : string.Empty);

            Debug.Log(Log + " " + buildResult.Replace("\n", " "));
            Check();

            if (EditorUtility.DisplayDialog("Publish version " + version + "?",
                    "Version " + version + " is built and committed on this machine.\n\n"
                    + "Publish pushes it to " + ContentRepo.Remote + " " + ContentRepo.Branch + ", whose GitHub Action "
                    + "uploads it to the CDN for everyone.\n\n"
                    + "Not yet keeps it on this machine only, to try on Localhost first. The builder then offers "
                    + "Publish or Discard.",
                    "Publish version " + version, "Not yet"))
            {
                Publish(version);
            }
        }

        void Fail(string message)
        {
            buildResult = "Not built: " + message;
            Debug.LogError(Log + " " + message);
            EditorUtility.DisplayDialog("Content not built", message, "OK");
        }
    }
}
