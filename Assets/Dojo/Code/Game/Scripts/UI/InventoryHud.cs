using System;
using System.Collections.Generic;
using Dojo.Framework.Content;
using Dojo.Framework.Inventory;
using TMPro;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Dojo.Game.UI
{
    /// <summary>
    /// The right-hand side of the inventory: the header, the pack filter, the search box, and the
    /// grid of whatever survives all three.
    /// </summary>
    /// <remarks>
    /// The grid is the intersection of three independent filters — the category the rail selected,
    /// the pack the filter selected, and whatever is typed in the search box. Each is held
    /// separately and applied in one place, so changing any one of them takes the same path and
    /// none of them can be left applied when another changes.
    /// <para>
    /// What can be shown at all comes from <c>IItemCatalog</c>, which is everything the player
    /// holds and nothing else. Items from packs the player has not bought are absent rather than
    /// locked: a manifest arrives with its pack or not at all, so there is nothing in this build
    /// that could describe an item that never came down. The locked caption and the counts that
    /// would go with it stay at zero until something can answer that question honestly.
    /// </para>
    /// <para>
    /// Cards are created through the container rather than with plain <c>Instantiate</c>, because
    /// the <see cref="ItemRenderer3D"/> beside each one needs the placement controller injected —
    /// a tile built without it looks right and does nothing when clicked.
    /// </para>
    /// </remarks>
    public sealed class InventoryHud : MonoBehaviour
    {
        [Header("Header")]
        [Tooltip("The category name, e.g. WALLS.")]
        [SerializeField] TextMeshProUGUI titleLabel;

        [Tooltip("How many items are showing, e.g. 5 items.")]
        [SerializeField] TextMeshProUGUI countLabel;

        [Header("Filters")]
        [Tooltip("The pack control. Filled from whatever the selected category contains.")]
        [SerializeField] InventoryPackFilter packFilter;

        [Tooltip("Free-text filter. Matches the item name.")]
        [SerializeField] TMP_InputField searchField;

        [Tooltip("Placeholder text, rewritten per category, e.g. Filter walls.")]
        [SerializeField] TextMeshProUGUI searchPlaceholder;

        [Header("Grid")]
        [Tooltip("Parent the cards are created under. The object carrying the GridLayoutGroup.")]
        [SerializeField] Transform gridContent;

        [Tooltip("One card, instantiated per item. Carries ItemRenderer3D beside InventoryItemCard.")]
        [SerializeField] InventoryItemCard cardPrefab;

        [Tooltip("Shown instead of the grid when nothing survives the filters.")]
        [SerializeField] GameObject emptyState;

        IObjectResolver resolver;
        IContentService content;
        IItemCatalog catalog;

        readonly List<InventoryItemCard> cards = new List<InventoryItemCard>();
        readonly List<ItemDefinition> matched = new List<ItemDefinition>();

        string category = string.Empty;
        string search = string.Empty;

        /// <summary>The category showing, or empty.</summary>
        public string Category => category;

        /// <summary>How many cards are in the grid.</summary>
        public int VisibleCount => cards.Count;

        [Inject]
        public void Construct(IObjectResolver container, IContentService contentService, IItemCatalog itemCatalog)
        {
            resolver = container;
            content = contentService;
            catalog = itemCatalog;
        }

        void Awake()
        {
            if (packFilter != null)
            {
                packFilter.PackSelected += OnPackChanged;
            }

            if (searchField != null)
            {
                searchField.onValueChanged.AddListener(OnSearchChanged);
            }
        }

        void OnDestroy()
        {
            if (packFilter != null)
            {
                packFilter.PackSelected -= OnPackChanged;
            }

            if (searchField != null)
            {
                searchField.onValueChanged.RemoveListener(OnSearchChanged);
            }
        }

        /// <summary>
        /// Shows a category. Clears the search, because a filter typed for walls almost never means
        /// anything once the player has moved to plants, and a grid that came up empty for a reason
        /// off screen is the kind of thing that reads as broken.
        /// </summary>
        public void Show(string categoryKey)
        {
            category = categoryKey ?? string.Empty;
            search = string.Empty;

            if (searchField != null)
            {
                // Without notify: this is a reset, not the player typing, and letting it raise
                // would rebuild the grid once here and again below.
                searchField.SetTextWithoutNotify(string.Empty);
            }

            ApplyHeader();
            RebuildPackFilter();
            Rebuild();
        }

        /// <summary>Empties the grid and the header. What the screen closes to.</summary>
        public void Clear()
        {
            category = string.Empty;
            search = string.Empty;

            ClearCards();
            ApplyHeader();
        }

        void OnPackChanged(string packKey)
        {
            Rebuild();
        }

        void OnSearchChanged(string text)
        {
            search = text ?? string.Empty;
            Rebuild();
        }

        /// <summary>
        /// Rebuilds the pack list for the category showing. The packs offered are the packs this
        /// category actually has items in, so the filter never offers a pack that would empty the
        /// grid the moment it was chosen.
        /// </summary>
        void RebuildPackFilter()
        {
            if (packFilter == null)
            {
                return;
            }

            var entries = new List<InventoryPackFilter.PackEntry>();
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();

            int total = 0;

            foreach (var item in ItemsInCategory())
            {
                total++;

                string pack = catalog != null ? catalog.PackOf(item.id) : string.Empty;
                if (string.IsNullOrEmpty(pack))
                {
                    continue;
                }

                if (!counts.ContainsKey(pack))
                {
                    counts[pack] = 0;
                    order.Add(pack);
                }

                counts[pack] = counts[pack] + 1;
            }

            for (int i = 0; i < order.Count; i++)
            {
                entries.Add(new InventoryPackFilter.PackEntry(order[i], Display(order[i]), counts[order[i]]));
            }

            packFilter.SetPacks(entries, total);

            // Nothing in this build can enumerate an item the player does not hold, so the locked
            // count is zero by construction rather than by omission. Left as an explicit call so
            // the day a storefront source exists, this is the one line that changes.
            packFilter.SetLockedCount(0);
        }

        void Rebuild()
        {
            ClearCards();
            Collect();

            for (int i = 0; i < matched.Count; i++)
            {
                var item = matched[i];
                var card = Create();

                if (card == null)
                {
                    break;
                }

                card.name = "Card_" + item.name;
                card.Bind(item.id, item.name, item.footprint, PackOf(item), false);

                // The tile beside the card is what actually carries the piece into the world.
                var tile = card.GetComponent<ItemRenderer3D>();
                if (tile != null)
                {
                    tile.Bind(item.id, item.name, LoadPrefab(item), LoadIcon(item), item.icon, item.address);
                }

                cards.Add(card);
            }

            ApplyHeader();

            if (emptyState != null)
            {
                emptyState.SetActive(cards.Count == 0);
            }
        }

        /// <summary>Gathers the items that pass the category, the pack and the search.</summary>
        void Collect()
        {
            matched.Clear();

            string pack = packFilter != null ? packFilter.Selected : InventoryPackFilter.AllPacks;

            foreach (var item in ItemsInCategory())
            {
                if (!string.IsNullOrEmpty(pack) &&
                    !string.Equals(PackOf(item), pack, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!Matches(item, search))
                {
                    continue;
                }

                matched.Add(item);
            }
        }

        IReadOnlyList<ItemDefinition> ItemsInCategory()
        {
            if (catalog == null || string.IsNullOrEmpty(category))
            {
                return Array.Empty<ItemDefinition>();
            }

            // There is no Plants tab, so anything a pack still files under plants shows here
            // rather than nowhere.
            if (string.Equals(category, ItemCategories.Others, StringComparison.OrdinalIgnoreCase))
            {
                var both = new List<ItemDefinition>(catalog.InCategory(ItemCategories.Others));
                both.AddRange(catalog.InCategory(ItemCategories.Plants));
                return both;
            }

            return catalog.InCategory(category);
        }

        static bool Matches(ItemDefinition item, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            return !string.IsNullOrEmpty(item.name) &&
                   item.name.IndexOf(text.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        string PackOf(ItemDefinition item)
        {
            return catalog != null ? catalog.PackOf(item.id) : string.Empty;
        }

        InventoryItemCard Create()
        {
            if (cardPrefab == null || gridContent == null)
            {
                return null;
            }

            // Through the resolver so ItemRenderer3D gets the placement controller. Plain
            // Instantiate yields a tile that renders and cannot be picked up.
            if (resolver != null)
            {
                return resolver.Instantiate(cardPrefab, gridContent);
            }

            return Instantiate(cardPrefab, gridContent);
        }

        void ClearCards()
        {
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i] != null)
                {
                    Destroy(cards[i].gameObject);
                }
            }

            cards.Clear();
        }

        void ApplyHeader()
        {
            if (titleLabel != null)
            {
                titleLabel.text = string.IsNullOrEmpty(category)
                    ? string.Empty
                    : category.ToUpperInvariant();
            }

            if (countLabel != null)
            {
                countLabel.text = cards.Count == 1 ? "1 item" : cards.Count + " items";
            }

            if (searchPlaceholder != null)
            {
                searchPlaceholder.text = string.IsNullOrEmpty(category)
                    ? "Filter"
                    : "Filter " + category.ToLowerInvariant();
            }
        }

        GameObject LoadPrefab(ItemDefinition item)
        {
            GameObject loaded;
            if (content != null && content.TryGet(item.address, out loaded) && loaded != null)
            {
                return loaded;
            }

            // The catalogue only lists items from packs that came down, so a miss means the pack's
            // manifest and its assets disagree — worth naming both, because the fix is in the pack.
            Debug.LogWarning("[InventoryHud] '" + item.id + "' names address '" + item.address
                + "', which is not resident. Its pack lists an asset it did not ship.", this);

            return null;
        }

        /// <summary>
        /// The item's picture, as something <c>Image</c> can draw.
        /// </summary>
        /// <remarks>
        /// Sprite first, because a PNG imported as one yields a sprite and that is what Image wants.
        /// A thumbnail imported as a plain texture is wrapped rather than dropped: the tile is
        /// otherwise blank with no indication why, which is exactly how the whole grid came up empty
        /// while every manifest named an icon that existed. The warning names the asset, because the
        /// fix is an import setting rather than anything in here.
        /// </remarks>
        Sprite LoadIcon(ItemDefinition item)
        {
            if (content == null || string.IsNullOrEmpty(item.icon))
            {
                return null;
            }

            Sprite sprite;
            if (content.TryGet(item.icon, out sprite) && sprite != null)
            {
                return sprite;
            }

            Texture2D texture;
            if (content.TryGet(item.icon, out texture) && texture != null)
            {
                Debug.LogWarning("[InventoryHud] '" + item.icon + "' is imported as a Texture, not a "
                    + "Sprite. Wrapped for now — set its Texture Type to Sprite (2D and UI).", this);

                return Sprite.Create(
                    texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f));
            }

            Debug.LogWarning("[InventoryHud] '" + item.id + "' names icon '" + item.icon
                + "', which is not resident.", this);

            return null;
        }

        /// <summary>
        /// Turns a pack key into what the menu shows. Upper case and spaced, because the keys are
        /// content keys rather than anything written to be read.
        /// </summary>
        static string Display(string packKey)
        {
            if (string.IsNullOrEmpty(packKey))
            {
                return "ALL PACKS";
            }

            return packKey.Replace('_', ' ').Replace('-', ' ').ToUpperInvariant();
        }
    }
}
