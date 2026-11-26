using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Dojo.Game.Editor
{
    /// <summary>
    /// What the pack folders that build into a group of their own - Rooms, Floors - share: the
    /// group made from <c>Content_Shared</c>'s settings, the sprite import for their thumbnails, and
    /// the file an index address names. <see cref="RoomsPack"/> and <see cref="FloorsPack"/> keep
    /// their own names, prefixes and index shape on top of this.
    /// </summary>
    public static class ContentPackFolder
    {
        /// <summary>Whose settings a new group is made with.</summary>
        public const string TemplateGroup = "Content_Shared";

        /// <summary>
        /// The group named <paramref name="groupName"/>, made the first time it is asked for with
        /// exactly the settings <c>Content_Shared</c> has - bundled by label, built and loaded from
        /// the same paths - so the folder gets bundles of its own and nothing else about delivery changes.
        /// </summary>
        public static AddressableAssetGroup EnsureGroup(AddressableAssetSettings settings, string groupName)
        {
            if (settings == null)
            {
                return null;
            }

            var template = settings.FindGroup(TemplateGroup);
            var group = settings.FindGroup(groupName);

            if (group != null)
            {
                // A group made before every schema was copied - an earlier failed run - is given
                // the ones it is missing, so it ends up with all of the template's settings.
                if (template != null)
                {
                    foreach (var schema in new List<AddressableAssetGroupSchema>(template.Schemas))
                    {
                        if (schema != null && !group.HasSchema(schema.GetType()))
                        {
                            group.AddSchema(schema, false);
                            Debug.Log("[ContentPackFolder] Gave " + groupName + " the " + schema.GetType().Name
                                + " it was missing, from " + TemplateGroup + ".", group);
                        }
                    }
                }

                return group;
            }

            if (template == null)
            {
                Debug.LogError("[ContentPackFolder] There is no " + TemplateGroup + " group to copy " + groupName
                    + "'s settings from, so it was not made.");
                return null;
            }

            // A copy of the list, not the template's own: adding a schema to the new group raises
            // events that touch the template's list while CreateGroup is still walking it.
            group = settings.CreateGroup(groupName, false, false, true,
                new List<AddressableAssetGroupSchema>(template.Schemas));
            Debug.Log("[ContentPackFolder] Made the " + groupName + " group, with " + TemplateGroup + "'s settings.", group);

            return group;
        }

        /// <summary>
        /// Imports a thumbnail as one sprite with alpha and no mipmaps: what the tabs' cards draw,
        /// and what the content service hands back when a card asks for a sprite.
        /// </summary>
        public static void ImportAsSprite(string assetPath)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;

            if (importer == null)
            {
                return;
            }

            if (importer.textureType == TextureImporterType.Sprite
                && importer.spriteImportMode == SpriteImportMode.Single
                && importer.alphaIsTransparency
                && !importer.mipmapEnabled)
            {
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }

        /// <summary>The file an address under the folder names, or null when it is not one.</summary>
        public static string FileFor(string folder, string prefix, string address, string extension)
        {
            if (string.IsNullOrEmpty(address) || !address.StartsWith(prefix, StringComparison.Ordinal))
            {
                return null;
            }

            return folder + "/" + address.Substring(prefix.Length) + (extension ?? string.Empty);
        }

        /// <summary>Whether an image of that name exists, whatever its extension.</summary>
        public static bool IconExists(string withoutExtension)
        {
            var folder = Path.GetDirectoryName(withoutExtension);
            var name = Path.GetFileName(withoutExtension);

            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                return false;
            }

            foreach (var file in Directory.GetFiles(folder, name + ".*"))
            {
                if (!file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
