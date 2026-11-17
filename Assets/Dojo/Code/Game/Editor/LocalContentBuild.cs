using System;
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
    /// <c>Tools ▸ Dojo ▸ Addressables ▸ 4. Build Local Test Content</c>: builds the content as a
    /// versioned release into a local folder outside DojoAddressables, for testing in the game
    /// without spending a published version number.
    /// </summary>
    /// <remarks>
    /// Laid out exactly as <see cref="VersionedContentBuild"/> lays out a published release -
    /// <c>version.json</c> at the top, every file of the release in <c>v&lt;n&gt;/</c> - so the game
    /// loads it the same way. Its numbers are its own: a test build never moves DojoAddressables'
    /// <c>version.json</c>, which a test build in the real folder would (the publish then refuses
    /// the next real version as "skips ahead").
    /// <para>
    /// The build is pointed at the local folder for its duration only, by setting the active
    /// profile's remote build path and restoring it afterwards, so DojoAddressables is not touched.
    /// </para>
    /// <para>
    /// To test: serve the folder (for example <c>python -m http.server 8765</c> in it) and set the
    /// game's local <c>RootLifetimeScope</c> content root to that address. The game keeps the last
    /// version it downloaded per content root, so local and published versions never mix.
    /// </para>
    /// </remarks>
    public static class LocalContentBuild
    {
        const string Log = "[Local Content]";
        const string FolderKey = "Dojo.LocalContentFolder";

        /// <summary>The local test folder: a sibling of the project, outside every repo, unless set.</summary>
        public static string Folder
        {
            get
            {
                var saved = EditorPrefs.GetString(FolderKey, string.Empty);
                return saved.Length > 0
                    ? saved
                    : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "DojoLocalContent"));
            }
            set => EditorPrefs.SetString(FolderKey, value ?? string.Empty);
        }

        [MenuItem("Tools/Dojo/Addressables/4. Build Local Test Content", false, 4)]
        static void BuildFromMenu()
        {
            var folder = Folder;
            int? current;

            try
            {
                current = VersionedContentBuild.ReadVersion(Path.Combine(folder, VersionedContentBuild.VersionFileName));
            }
            catch (Exception exception)
            {
                Fail(exception.Message);
                return;
            }

            var next = current.HasValue ? current.Value + 1 : 0;

            if (!EditorUtility.DisplayDialog("Build local test content " + next,
                    "Builds the content as local test version " + next + " into:\n" + folder
                    + "\n\nNot published, and DojoAddressables is not touched.",
                    "Build", "Cancel"))
            {
                return;
            }

            Build(folder);
        }

        /// <summary>Builds the next local test version into <paramref name="folder"/>; returns it, or -1.</summary>
        public static int Build(string folder)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Fail("There are no Addressables settings in this project.");
                return -1;
            }

            Directory.CreateDirectory(folder);

            int? current;
            try
            {
                current = VersionedContentBuild.ReadVersion(Path.Combine(folder, VersionedContentBuild.VersionFileName));
            }
            catch (Exception exception)
            {
                Fail(exception.Message);
                return -1;
            }

            var version = current.HasValue ? current.Value + 1 : 0;
            var staging = Path.Combine(folder, "_build");
            var release = Path.Combine(folder, VersionedContentBuild.ReleaseFolderName(version));

            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, true);
            }

            if (Directory.Exists(release))
            {
                Directory.Delete(release, true);
            }

            var profiles = settings.profileSettings;
            var profile = settings.activeProfileId;
            var previousPath = profiles.GetValueByName(profile, AddressableAssetSettings.kRemoteBuildPath);
            var previousVersion = settings.OverridePlayerVersion;

            AddressablesPlayerBuildResult result;
            try
            {
                profiles.SetValue(profile, AddressableAssetSettings.kRemoteBuildPath, staging);
                settings.OverridePlayerVersion = version.ToString(CultureInfo.InvariantCulture);
                AddressableAssetSettings.BuildPlayerContent(out result);
            }
            finally
            {
                profiles.SetValue(profile, AddressableAssetSettings.kRemoteBuildPath, previousPath);
                settings.OverridePlayerVersion = previousVersion;
            }

            if (result == null || !string.IsNullOrEmpty(result.Error))
            {
                Fail("The Addressables build failed: " + (result == null ? "no result" : result.Error));
                return -1;
            }

            var catalog = VersionedContentBuild.CatalogFileName(version);
            var hash = Path.ChangeExtension(catalog, ".hash");

            if (!File.Exists(Path.Combine(staging, catalog)) || !File.Exists(Path.Combine(staging, hash)))
            {
                Fail("The build finished but " + catalog + " or its hash is not in " + staging + ".");
                return -1;
            }

            Directory.CreateDirectory(release);
            var bundles = 0;

            foreach (var bundle in result.AssetBundleBuildResults)
            {
                var source = Path.GetFullPath(bundle.FilePath);
                File.Copy(source, Path.Combine(release, Path.GetFileName(source)), true);
                bundles++;
            }

            File.Copy(Path.Combine(staging, catalog), Path.Combine(release, catalog), true);
            File.Copy(Path.Combine(staging, hash), Path.Combine(release, hash), true);
            Directory.Delete(staging, true);

            File.WriteAllText(Path.Combine(folder, VersionedContentBuild.VersionFileName),
                "{\n  \"version\": " + version.ToString(CultureInfo.InvariantCulture) + ",\n  \"catalog\": \"" + catalog
                + "\",\n  \"built\": \"" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
                + "\",\n  \"local\": true\n}\n");

            Debug.Log(Log + " local test version " + version + " built: " + catalog + " and " + bundles + " bundle(s) in "
                + release + ". Serve " + folder + " and point the game's content root at it.");
            return version;
        }

        static void Fail(string message)
        {
            Debug.LogError(Log + " " + message);
            EditorUtility.DisplayDialog("Local test content not built", message, "OK");
        }
    }
}
