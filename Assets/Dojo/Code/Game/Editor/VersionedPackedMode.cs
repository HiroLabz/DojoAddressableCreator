using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Build.DataBuilders;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine.Build.Pipeline;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Dojo.Game.Editor
{
    /// <summary>
    /// Addressables' packed build with the release version in front of every bundle's name
    /// (<c>dojo_v4_content_agents_assets_default_….bundle</c>), so no release ever reuses a name the
    /// CDN already serves.
    /// </summary>
    /// <remarks>
    /// Addressables appends a hash of a bundle's own content to its name, but a bundle's bytes also
    /// hold the names of the bundles it depends on. Since 17 Nov the script-references and built-in
    /// bundles carry the version (<see cref="ContentRelease.BundlePrefix"/>), so every bundle that
    /// points at them changed its bytes from one version to the next while keeping its name. The CDN
    /// caches a name for a year and refuses such an upload (content version 3, 18 Nov:
    /// content_agents_assets_default_… and content_thumbnails_assets_strawberry_…). With the version
    /// in every name, each release is a fresh set of files; the game clears its bundle cache on a
    /// new content version anyway, so nothing that could have been reused is lost.
    /// </remarks>
    public sealed class VersionedPackedMode : BuildScriptPackedMode
    {
        /// <summary>The version's prefix, set for one build by <see cref="Build"/>.</summary>
        string prefix = string.Empty;

        public override string Name => "Dojo versioned packed build";

        /// <summary>
        /// <paramref name="assetBundleName"/> as Addressables names it, with <see cref="prefix"/> in
        /// front unless it already starts with it (the script-references and built-in bundles do).
        /// </summary>
        protected override string ConstructAssetBundleName(AddressableAssetGroup assetGroup, BundledAssetGroupSchema schema,
            BundleDetails info, string assetBundleName)
            => ContentRelease.Prefixed(prefix, base.ConstructAssetBundleName(assetGroup, schema, info, assetBundleName));

        /// <summary>
        /// Builds the player content the way <c>AddressableAssetSettings.BuildPlayerContent</c> does
        /// (the build folder cleared first, the assets refreshed after), with this builder and every
        /// bundle named after <paramref name="version"/>. The project's own data builders are left
        /// as they are.
        /// </summary>
        public static AddressablesPlayerBuildResult Build(AddressableAssetSettings settings, int version)
        {
            if (Directory.Exists(Addressables.BuildPath))
            {
                Directory.Delete(Addressables.BuildPath, true);
            }

            var builder = CreateInstance<VersionedPackedMode>();
            builder.prefix = ContentRelease.BundlePrefix(version);

            try
            {
                return builder.BuildData<AddressablesPlayerBuildResult>(new AddressablesDataBuilderInput(settings));
            }
            finally
            {
                DestroyImmediate(builder);
                AssetDatabase.Refresh();
            }
        }
    }
}
