using System;
using System.Collections.Generic;
using Dojo.Framework.Content;
using Dojo.Framework.Inventory;
using UnityEngine;

namespace Dojo.Inventory
{
    /// <summary>
    /// <see cref="IItemCatalog"/> over whatever the content service currently holds.
    /// </summary>
    /// <remarks>
    /// This assembly references <c>Dojo.Framework</c> and nothing else — no Addressables, no
    /// resource manager. That is the point of the split rather than an accident of it: the
    /// inventory asks for a manifest by address and does not care whether it arrived from a CDN, a
    /// local web server or a folder. Replacing the delivery layer cannot break this file.
    /// </remarks>
    public sealed class ItemCatalog : IItemCatalog
    {
        const string LogPrefix = "[Inventory]";

        readonly IContentService content;

        readonly List<ItemDefinition> all = new List<ItemDefinition>();
        readonly List<string> categories = new List<string>();
        readonly Dictionary<string, List<ItemDefinition>> byCategory =
            new Dictionary<string, List<ItemDefinition>>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, ItemDefinition> byId = new Dictionary<string, ItemDefinition>();
        readonly Dictionary<string, string> packById = new Dictionary<string, string>();

        static readonly IReadOnlyList<ItemDefinition> None = new ItemDefinition[0];

        public ItemCatalog(IContentService content)
        {
            this.content = content;
        }

        public IReadOnlyList<ItemDefinition> All => all;

        public IReadOnlyList<string> Categories => categories;

        public void Refresh()
        {
            all.Clear();
            categories.Clear();
            byCategory.Clear();
            byId.Clear();
            packById.Clear();

            if (content == null)
            {
                return;
            }

            foreach (var key in content.LoadedKeys)
            {
                TextAsset manifestAsset;
                if (!content.TryGet(PackManifest.AddressFor(key), out manifestAsset) || manifestAsset == null)
                {
                    // A pack with no manifest ships assets and lists none of them. Legitimate — a
                    // pack can carry art that another pack's items point at — so not a warning.
                    continue;
                }

                var manifest = PackManifest.Parse(manifestAsset.text);
                if (manifest == null)
                {
                    Debug.LogWarning(LogPrefix + " the manifest for pack '" + key
                        + "' could not be read, so none of its items are listed.");
                    continue;
                }

                foreach (var item in manifest.items)
                {
                    Add(item, manifest.pack);
                }
            }
        }

        void Add(ItemDefinition item, string pack)
        {
            if (item == null || string.IsNullOrEmpty(item.id) || string.IsNullOrEmpty(item.category))
            {
                return;
            }

            if (byId.ContainsKey(item.id))
            {
                // Two packs claiming one id is a content bug, not a runtime one. First in wins so
                // the result is at least deterministic, and both packs are named so it is fixable.
                Debug.LogWarning(LogPrefix + " '" + item.id + "' is defined by both '"
                    + packById[item.id] + "' and '" + pack + "'; keeping the first.");
                return;
            }

            List<ItemDefinition> inCategory;
            if (!byCategory.TryGetValue(item.category, out inCategory))
            {
                inCategory = new List<ItemDefinition>();
                byCategory[item.category] = inCategory;
                categories.Add(item.category);
            }

            inCategory.Add(item);
            all.Add(item);
            byId[item.id] = item;
            packById[item.id] = pack;
        }

        public IReadOnlyList<ItemDefinition> InCategory(string category)
        {
            List<ItemDefinition> items;
            return !string.IsNullOrEmpty(category) && byCategory.TryGetValue(category, out items)
                ? items
                : None;
        }

        public IReadOnlyList<ItemDefinition> InCategories(IReadOnlyList<string> wanted)
        {
            if (wanted == null || wanted.Count == 0)
            {
                return None;
            }

            var matches = new List<ItemDefinition>();

            foreach (var category in wanted)
            {
                matches.AddRange(InCategory(category));
            }

            return matches;
        }

        public bool TryGet(string id, out ItemDefinition item)
        {
            item = null;
            return !string.IsNullOrEmpty(id) && byId.TryGetValue(id, out item);
        }

        public string PackOf(string id)
        {
            string pack;
            return !string.IsNullOrEmpty(id) && packById.TryGetValue(id, out pack) ? pack : string.Empty;
        }
    }
}
