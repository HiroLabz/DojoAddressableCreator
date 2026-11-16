using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Dojo.Game.Editor
{
    /// <summary>
    /// <c>Tools ▸ Dojo ▸ Addressables ▸ 3. Build Content (Next Version)</c>: builds the content as the
    /// next release, 0 for the first and one higher each time, and writes <c>version.json</c> next
    /// to its catalogue.
    /// </summary>
    /// <remarks>
    /// Each release has its own catalogue, <c>catalog_&lt;version&gt;.bin</c>, instead of one
    /// <c>catalog_0.1.0.bin</c> that every build overwrote. The game reads <c>version.json</c> first:
    /// the same number keeps the bundles it has cached, a higher one clears them before the new
    /// release downloads. That is what stops the one-time "CRC Mismatch" errors a publish used to
    /// cause.
    /// <para>
    /// The next number is the one in the build folder's <c>version.json</c> plus one. Copy the whole
    /// build folder into DojoAddressables' <c>development/</c> as before; its publish refuses a
    /// version that is not exactly the published one plus one, and uploads <c>version.json</c> last.
    /// </para>
    /// <para>
    /// Building from the Addressables Groups window still works, but makes no new version and is
    /// refused by the publish: use this menu for anything that will be published.
    /// </para>
    /// </remarks>
    public static class VersionedContentBuild
    {
        const string Log = "[Content Version]";

        /// <summary>The file the game reads first. Matches the game's <c>ContentVersion.FileName</c>.</summary>
        public const string VersionFileName = "version.json";

        [MenuItem("Tools/Dojo/Addressables/3. Build Content (Next Version)", false, 3)]
        static void BuildNextVersion()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Fail("There are no Addressables settings in this project.");
                return;
            }

            string folder;
            try
            {
                folder = CatalogFolder(settings);
            }
            catch (Exception exception)
            {
                Fail(exception.Message);
                return;
            }

            int? current;
            try
            {
                current = ReadVersion(Path.Combine(folder, VersionFileName));
            }
            catch (Exception exception)
            {
                Fail(exception.Message);
                return;
            }

            var next = current.HasValue ? current.Value + 1 : 0;
            var catalog = CatalogFileName(next);

            if (File.Exists(Path.Combine(folder, catalog)))
            {
                Fail("Version " + next + " has already been built: " + Path.Combine(folder, catalog)
                    + " exists. Check version.json in that folder.");
                return;
            }

            var from = current.HasValue
                ? "The build folder is on version " + current.Value + "."
                : "The build folder has no " + VersionFileName + ", so this is the first version.";

            if (!EditorUtility.DisplayDialog("Build content version " + next,
                    from + "\n\nBuild the content as version " + next + " into:\n" + folder
                    + "\n\nIts catalogue will be " + catalog + ".",
                    "Build version " + next, "Cancel"))
            {
                return;
            }

            Build(settings, folder, next);
        }

        static void Build(AddressableAssetSettings settings, string folder, int version)
        {
            // The catalogue is named catalog_<player version>. Set for this build only, so a build
            // from the Groups window does not quietly reuse a release number.
            var previousOverride = settings.OverridePlayerVersion;
            settings.OverridePlayerVersion = version.ToString(CultureInfo.InvariantCulture);

            AddressablesPlayerBuildResult result;
            try
            {
                AddressableAssetSettings.BuildPlayerContent(out result);
            }
            finally
            {
                settings.OverridePlayerVersion = previousOverride;
            }

            if (result == null || !string.IsNullOrEmpty(result.Error))
            {
                Fail("The Addressables build failed, so no version was written: "
                    + (result == null ? "no result" : result.Error));
                return;
            }

            var catalog = CatalogFileName(version);
            if (!File.Exists(Path.Combine(folder, catalog)))
            {
                Fail("The build finished but " + catalog + " is not in " + folder
                    + ". Is the remote catalogue switched on in the Addressables settings?");
                return;
            }

            var json = "{\n"
                + "  \"version\": " + version.ToString(CultureInfo.InvariantCulture) + ",\n"
                + "  \"catalog\": \"" + catalog + "\",\n"
                + "  \"built\": \"" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) + "\"\n"
                + "}\n";
            File.WriteAllText(Path.Combine(folder, VersionFileName), json);

            Debug.Log(Log + " content version " + version + " built: " + catalog + " and " + VersionFileName
                + " in " + folder + ". Copy the folder into DojoAddressables' development/ to publish it.");
            EditorUtility.DisplayDialog("Content version " + version + " built",
                "Built " + catalog + " and " + VersionFileName + " in:\n" + folder
                + "\n\nCopy this folder into DojoAddressables' development/ folder and push to publish it.",
                "OK");
        }

        /// <summary>Where the build writes its remote catalogue, which is where version.json goes too.</summary>
        static string CatalogFolder(AddressableAssetSettings settings)
        {
            string folder = null;

            if (settings.RemoteCatalogBuildPath != null)
            {
                folder = settings.RemoteCatalogBuildPath.GetValue(settings);
            }

            if (string.IsNullOrWhiteSpace(folder))
            {
                var profiles = settings.profileSettings;
                var raw = profiles.GetValueByName(settings.activeProfileId, AddressableAssetSettings.kRemoteBuildPath);
                folder = profiles.EvaluateString(settings.activeProfileId, raw);
            }

            if (string.IsNullOrWhiteSpace(folder))
            {
                throw new InvalidOperationException("The active Addressables profile has no remote build path, "
                    + "so there is nowhere to build a published version to.");
            }

            return Path.GetFullPath(folder);
        }

        /// <summary>The version in an existing version.json, or null when there is none yet.</summary>
        internal static int? ReadVersion(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var text = File.ReadAllText(path);
            var match = Regex.Match(text, "\"version\"\\s*:\\s*(\\d+)\\s*[,}]");
            if (!match.Success)
            {
                throw new InvalidDataException(path + " has no \"version\" that is a whole number. "
                    + "Fix or remove it before building a new version.");
            }

            return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        internal static string CatalogFileName(int version)
            => "catalog_" + version.ToString(CultureInfo.InvariantCulture) + ".bin";

        static void Fail(string message)
        {
            Debug.LogError(Log + " " + message);
            EditorUtility.DisplayDialog("Content version not built", message, "OK");
        }
    }
}
