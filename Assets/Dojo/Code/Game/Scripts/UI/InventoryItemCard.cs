using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Game.UI
{
    /// <summary>
    /// The chrome around one tile in the inventory grid: its name, its footprint badge, and which
    /// of the tile states is showing.
    /// </summary>
    /// <remarks>
    /// Deliberately not the thing that gets dragged. <see cref="ItemRenderer3D"/> sits beside this
    /// on the same prefab and owns the icon, the click and the whole placement gesture; a card that
    /// re-implemented any of that would be a second set of rules about how a piece leaves the
    /// drawer. This paints the parts of a tile that the gesture has no opinion about.
    /// <para>
    /// The footprint badge hides itself when the pack did not state a size, rather than showing
    /// <c>0x0</c>. Manifests written before <c>ItemDefinition.footprint</c> existed carry no
    /// footprint at all, so an absent badge is the truthful reading of an older pack and not a
    /// fault worth reporting.
    /// </para>
    /// </remarks>
    public sealed class InventoryItemCard : MonoBehaviour
    {
        [Header("Labels")]
        [Tooltip("The item's display name.")]
        [SerializeField] TextMeshProUGUI nameLabel;

        [Tooltip("Footprint, written as width x depth. Hidden when the pack states no size.")]
        [SerializeField] TextMeshProUGUI sizeLabel;

        [Tooltip("The whole badge, hidden along with the label so its plate goes too.")]
        [SerializeField] GameObject sizeBadge;

        [Header("States")]
        [Tooltip("Shown while this card is the one the player has picked up.")]
        [SerializeField] GameObject selectedState;

        [Tooltip("Shown when the item belongs to a pack the player does not own.")]
        [SerializeField] GameObject lockedState;

        /// <summary>The catalogue id of the item this card stands for.</summary>
        public string ItemId { get; private set; } = string.Empty;

        /// <summary>Which pack the item came from. Drives the pack filter.</summary>
        public string Pack { get; private set; } = string.Empty;

        /// <summary>Whether the player may actually place this.</summary>
        public bool IsLocked { get; private set; }

        /// <summary>
        /// Fills the card in. Everything arrives together so a half-populated card cannot reach the
        /// player, the same bargain <see cref="ItemRenderer3D.Bind"/> makes.
        /// </summary>
        public void Bind(string itemId, string displayName, Vector2Int footprint, string pack, bool locked)
        {
            ItemId = itemId ?? string.Empty;
            Pack = pack ?? string.Empty;
            IsLocked = locked;

            if (nameLabel != null)
            {
                nameLabel.text = displayName ?? string.Empty;
            }

            ApplyFootprint(footprint);
            SetSelected(false);

            if (lockedState != null)
            {
                lockedState.SetActive(locked);
            }
        }

        /// <summary>Lights the card while its piece is in hand.</summary>
        public void SetSelected(bool selected)
        {
            if (selectedState != null)
            {
                selectedState.SetActive(selected);
            }
        }

        void ApplyFootprint(Vector2Int footprint)
        {
            // Either dimension being zero means "not stated" rather than "flat", so both have to be
            // present before the badge claims a size.
            bool stated = footprint.x > 0 && footprint.y > 0;

            if (sizeBadge != null)
            {
                sizeBadge.SetActive(stated);
            }

            if (sizeLabel != null)
            {
                sizeLabel.gameObject.SetActive(stated);

                if (stated)
                {
                    // The multiplication sign rather than a lowercase x: the mock sets these as
                    // 1x3 in small caps, and the letter reads as part of the number at that size.
                    sizeLabel.text = footprint.x + "\u00d7" + footprint.y;
                }
            }
        }
    }
}
