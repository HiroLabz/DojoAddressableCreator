using System.Collections.Generic;
using System.Globalization;
using Dojo.Framework.Content;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Game
{
    /// <summary>
    /// The strip of pack chips along the bottom-right of the Lobby. Shows one chip per content key
    /// the player actually holds, and nothing for a pack they do not.
    /// </summary>
    /// <remarks>
    /// A pure view: it is handed a list of keys and renders it. It does not inject
    /// <see cref="IEntitlements"/> and does not subscribe to anything, so there is exactly one
    /// place — <see cref="LobbyMainMenu"/> — that decides when the entitlement list is worth
    /// believing. Two subscribers to the same event, each with their own idea of whether the list
    /// is ready, is how a screen ends up disagreeing with itself.
    /// <para>
    /// Positions are the layout group's business. The chips sit in a
    /// <see cref="UnityEngine.UI.HorizontalLayoutGroup"/> with <c>reverseArrangement</c> on, so the
    /// first chip added lands at the right edge and each new one pushes the row leftward. Nothing
    /// here computes a coordinate.
    /// </para>
    /// </remarks>
    public sealed class PackComponent : MonoBehaviour
    {
        [Tooltip("Holds the chips and owns the HorizontalLayoutGroup. The ScrollRect's content.")]
        [SerializeField] RectTransform container;

        [Tooltip("Scrolls the strip when the player holds more packs than fit across the screen. " +
                 "Optional: without one the strip simply runs off the left edge.")]
        [SerializeField] ScrollRect scroll;

        [Tooltip("Chip prefab, instantiated once per held pack.")]
        [SerializeField] PackChipView chipPrefab;

        [Tooltip("The content key that every player holds. Pinned to the right of the strip.")]
        [SerializeField] string defaultKey = ContentSettings.DefaultKey;

        [Tooltip("Marker for the default pack.")]
        [SerializeField] Sprite defaultMarker;

        [Tooltip("Marker for any pack the player acquired.")]
        [SerializeField] Sprite acquiredMarker;

        [Tooltip("The 'PACKS:' caption. Kept as the last child so the reversed layout puts it at " +
                 "the left end of the row. Hidden while the row is empty.")]
        [SerializeField] GameObject caption;

        readonly List<PackChipView> spawned = new List<PackChipView>();

        /// <summary>
        /// The row the chips go into, resolved on demand rather than in <c>Awake</c>.
        /// </summary>
        /// <remarks>
        /// This object starts disabled in the scene, so <c>Awake</c> may never have run by the time
        /// <see cref="Show"/> is first called — a method call on a component of an inactive
        /// GameObject still executes, but its lifecycle callbacks do not. Anything cached in
        /// <c>Awake</c> would be null at exactly the moment it was needed.
        /// </remarks>
        RectTransform Container
        {
            get { return container != null ? container : (RectTransform)transform; }
        }

        /// <summary>
        /// Rebuilds the strip to show exactly these keys, in this order, with the default pack
        /// pinned to the right edge.
        /// </summary>
        /// <remarks>
        /// Rebuilds wholesale rather than diffing. The strip is a handful of chips that changes
        /// only when the player acquires a pack, so a diff would be more code defending a saving
        /// nobody can measure.
        /// </remarks>
        public void Show(IReadOnlyList<string> keys)
        {
            Clear();

            if (keys == null || keys.Count == 0 || chipPrefab == null)
            {
                return;
            }

            // Shown only once there is something to show. The strip is authored disabled, and a
            // caption reading "PACKS:" above an empty row would be worse than no row at all.
            gameObject.SetActive(true);

            // Default first so that, reversed by the layout group, it lands rightmost — where the
            // mock puts it, and where it stays put as packs accumulate to its left.
            for (int i = 0; i < keys.Count; i++)
            {
                if (IsDefault(keys[i]))
                {
                    Spawn(keys[i]);
                }
            }

            for (int i = 0; i < keys.Count; i++)
            {
                if (!IsDefault(keys[i]))
                {
                    Spawn(keys[i]);
                }
            }

            // Last child, so the reversed layout puts the caption at the left end of the row —
            // reading "PACKS: … STRAWBERRY DEFAULT" while the row still grows leftward.
            if (caption != null)
            {
                caption.SetActive(true);
                caption.transform.SetAsLastSibling();
            }

            ScrollToDefault();
        }

        /// <summary>
        /// Parks the view at the right-hand end, where the default pack sits.
        /// </summary>
        /// <remarks>
        /// A strip that overflows would otherwise open wherever the ScrollRect last left it, which
        /// after a rebuild is arbitrary. The right-hand end is the one position that is always
        /// meaningful: it is where the mock puts the default chip and where the eye starts.
        /// <para>
        /// The layout has to be resolved before the normalised position means anything — until the
        /// content has a width, every position is the same position.
        /// </para>
        /// </remarks>
        void ScrollToDefault()
        {
            if (scroll == null)
            {
                return;
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(Container);
            scroll.horizontalNormalizedPosition = 1f;
        }

        /// <summary>
        /// Removes every chip and hides the strip, which is the correct state for a player whose
        /// entitlements have not arrived yet as well as one who somehow holds nothing.
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < spawned.Count; i++)
            {
                if (spawned[i] != null)
                {
                    Destroy(spawned[i].gameObject);
                }
            }

            spawned.Clear();

            if (caption != null)
            {
                caption.SetActive(false);
            }

            gameObject.SetActive(false);
        }

        void Spawn(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            // worldPositionStays: false. The chip is going straight into a layout group that will
            // place it, and keeping world position drags the canvas scale into the clone's
            // localScale on the way.
            PackChipView chip = Instantiate(chipPrefab, Container, false);
            chip.gameObject.name = "Chip_" + key;
            chip.gameObject.SetActive(true);
            chip.Bind(ToDisplayName(key), IsDefault(key) ? defaultMarker : acquiredMarker);
            spawned.Add(chip);
        }

        bool IsDefault(string key)
        {
            return string.Equals(key, defaultKey, System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Content keys are lower case by convention; the chips are upper case by design.
        /// </summary>
        /// <remarks>
        /// Invariant culture on purpose. <c>ToUpper()</c> under a Turkish locale turns the "i" in
        /// "verified" into "İ", which is the kind of bug that only ever appears on a player's
        /// machine.
        /// </remarks>
        static string ToDisplayName(string key)
        {
            return key.ToUpper(CultureInfo.InvariantCulture);
        }
    }
}
