using System;
using UnityEngine;

namespace Dojo.Framework.Inventory
{
    /// <summary>
    /// What one pack contributes to the inventory. Ships inside the pack, addressed
    /// <c>Packs/&lt;pack&gt;/items</c>, so it can never arrive without the content it describes.
    /// </summary>
    /// <remarks>
    /// One manifest per pack rather than one global list. A global list would have to name items the
    /// player has not got, which is how the old thumbnail manifest produced tiles for content that
    /// was never downloaded. A pack's manifest arrives with the pack or not at all, so the catalogue
    /// is exactly what the player holds.
    /// </remarks>
    [Serializable]
    public sealed class PackManifest
    {
        /// <summary>Where a pack's manifest lives. Convention, not configuration.</summary>
        public static string AddressFor(string pack) => "Packs/" + pack + "/items";

        /// <summary>The pack key. Identical to the content key and the Addressables label.</summary>
        public string pack;

        public int version;

        public ItemDefinition[] items;

        /// <summary>
        /// Parses a manifest, or returns null when it cannot be trusted.
        /// </summary>
        /// <remarks>
        /// Null rather than an empty manifest, so a caller can tell "this pack lists nothing" from
        /// "this pack's manifest is broken". The first is legitimate — a pack can ship assets that
        /// other packs' items refer to — and the second is worth a warning.
        /// </remarks>
        public static PackManifest Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            PackManifest parsed;

            try
            {
                parsed = JsonUtility.FromJson<PackManifest>(json);
            }
            catch (ArgumentException)
            {
                // JsonUtility throws on malformed input rather than returning null.
                return null;
            }

            if (parsed == null || string.IsNullOrWhiteSpace(parsed.pack))
            {
                return null;
            }

            if (parsed.items == null)
            {
                parsed.items = new ItemDefinition[0];
            }

            return parsed;
        }
    }
}
