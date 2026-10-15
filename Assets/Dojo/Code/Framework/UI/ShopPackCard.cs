using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Framework.UI
{
    /// <summary>
    /// One flavour pack in the shop: its art, a name and blurb, what it contains, and either a price
    /// and a buy button or a release date and a locked one.
    /// </summary>
    /// <remarks>
    /// Authored as a prefab and instantiated per pack, the same arrangement as
    /// <see cref="LoadWorldRow"/> and for the same reasons — serialized references rather than
    /// lookups by child name, so a renamed child is a null the console names instead of a card that
    /// quietly stops filling itself in.
    /// <para>
    /// The card reports being bought and decides nothing. Whether a purchase can actually go through
    /// is a question about entitlements and money, and <see cref="ShopDialog"/> is what asks it.
    /// </para>
    /// <para>
    /// Available and locked are two authored sub-trees, shown one at a time, rather than one tree
    /// that restyles itself. The art decides that: the pills and buttons come with their words drawn
    /// into them — AVAILABLE, COMING SOON, BUY — so the two states differ by sprite as well as
    /// colour, and there is no text to swap. Toggling whole objects is also how the entry point
    /// handles its open and closed icons, so this is the shape the codebase already reads as
    /// "one of these at a time".
    /// </para>
    /// </remarks>
    public sealed class ShopPackCard : MonoBehaviour
    {
        [Header("State")]
        [Tooltip("Everything shown only for a pack that can be bought: the pink frame, the " +
                 "AVAILABLE pill, and the BUY button.")]
        [SerializeField] GameObject availableState;

        [Tooltip("Everything shown only for a pack that is still coming: the hatched frame, the " +
                 "COMING SOON pill, and the dead COMING SOON button.")]
        [SerializeField] GameObject lockedState;

        [Header("Body")]
        [Tooltip("The pack's art. Left alone when a pack brings no sprite of its own.")]
        [SerializeField] Image icon;

        [Tooltip("The pack's name.")]
        [SerializeField] TMP_Text nameLabel;

        [Tooltip("The paragraph under the name.")]
        [SerializeField] TMP_Text descriptionLabel;

        [Tooltip("Bullets are created under this. Wants a vertical layout group.")]
        [SerializeField] RectTransform contents;

        [Tooltip("The bullet prefab, instantiated once per line of contents. Authored inactive, " +
                 "the way LoadWorldDialog authors its row — it is a template that lives among the " +
                 "things it is a template for, so leaving it on shows a spare line of placeholder " +
                 "text under every card's real contents.")]
        [SerializeField] TMP_Text bulletPrefab;

        [Header("Footer")]
        [Tooltip("The small caption bottom-left: PRICE on an available pack, RELEASES on a locked one.")]
        [SerializeField] TMP_Text footerCaption;

        [Tooltip("The value bottom-right: the price, or the release date.")]
        [SerializeField] TMP_Text footerValue;

        [Header("Buy")]
        [Tooltip("The BUY button, inside the available state. Nothing on the locked card is a button.")]
        [SerializeField] Button buyButton;

        [Tooltip("The button's graphic, dimmed while the pack is coming down.")]
        [SerializeField] Image buyGraphic;

        [Header("Wording")]
        [Tooltip("Footer caption for a pack that can be bought.")]
        [SerializeField] string priceCaption = "PRICE";

        [Tooltip("Footer caption for a pack that is still coming.")]
        [SerializeField] string releasesCaption = "RELEASES";

        [Tooltip("How far the buy button is dimmed while its pack downloads.")]
        [SerializeField, Range(0f, 1f)] float busyDim = 0.45f;

        [Tooltip("What a locked pack's art is tinted to. The mock shows it greyed back, so a pack " +
                 "you cannot have yet does not draw the eye harder than the one you can.")]
        [SerializeField] Color lockedArtTint = new Color(0.42f, 0.47f, 0.56f, 0.65f);

        [Tooltip("Put in front of every line of contents. Rich text, so the dot can be its own " +
                 "colour without being its own object.")]
        [SerializeField] string bulletPrefix = "<color=#F2617A>•</color>   ";

        /// <summary>Raised when the player presses this card's buy button.</summary>
        public event Action<ShopPackCard> Bought;

        /// <summary>The pack this card stands for.</summary>
        public string PackKey { get; private set; }

        /// <summary>
        /// The pack's display name. Kept because the loading screen a purchase runs behind titles
        /// itself with it, and reading it back off the label would tie that to the label surviving.
        /// </summary>
        public string PackName { get; private set; }

        /// <summary>Whether the pack on this card can be bought.</summary>
        public bool IsAvailable { get; private set; }

        // Bullets outlive a single Bind, so a card rebound to a shorter pack hides the spare lines
        // rather than destroying and remaking them.
        readonly List<TMP_Text> bullets = new List<TMP_Text>();

        // What the buy graphic looks like when it is not busy, read from how it was authored so the
        // dim is undone exactly rather than by assuming it was white.
        Color buyRest = Color.white;

        void Awake()
        {
            if (buyButton != null)
            {
                buyButton.onClick.AddListener(OnBuy);
            }

            if (buyGraphic != null)
            {
                buyRest = buyGraphic.color;
            }
        }

        void OnDestroy()
        {
            if (buyButton != null)
            {
                buyButton.onClick.RemoveListener(OnBuy);
            }
        }

        /// <summary>Fills the card in.</summary>
        public void Bind(ShopPack pack)
        {
            PackKey = pack.Key;
            PackName = pack.Name;
            IsAvailable = pack.IsAvailable;

            if (availableState != null)
            {
                availableState.SetActive(pack.IsAvailable);
            }

            if (lockedState != null)
            {
                lockedState.SetActive(!pack.IsAvailable);
            }

            if (icon != null)
            {
                icon.color = pack.IsAvailable ? Color.white : lockedArtTint;
            }

            if (nameLabel != null)
            {
                nameLabel.text = pack.Name;
            }

            if (descriptionLabel != null)
            {
                descriptionLabel.text = pack.Description;
            }

            FillContents(pack.Contents);

            if (footerCaption != null)
            {
                footerCaption.text = pack.IsAvailable ? priceCaption : releasesCaption;
            }

            if (footerValue != null)
            {
                footerValue.text = pack.IsAvailable ? pack.Price : pack.ReleasesAt;
            }

            // Binding is also what ends a busy state, for a card pooled back into use after a
            // purchase that closed the dialog while it was still dimmed.
            SetBusy(false);
        }

        /// <summary>
        /// Shows the card as working, while its pack is coming down.
        /// </summary>
        /// <remarks>
        /// Dimmed rather than relabelled. The word BUY is drawn into the button's art, so there is
        /// no text to replace with a progress line — and a button that only stopped responding,
        /// with no change at all, is indistinguishable from one that is broken.
        /// </remarks>
        public void SetBusy(bool busy)
        {
            if (buyButton != null)
            {
                buyButton.interactable = !busy && IsAvailable;
            }

            if (buyGraphic != null)
            {
                buyGraphic.color = busy
                    ? new Color(buyRest.r, buyRest.g, buyRest.b, buyRest.a * busyDim)
                    : buyRest;
            }
        }

        /// <summary>
        /// Puts a pack's art on the card. Separate from <see cref="Bind"/> because the sprite is
        /// authored on the dialog rather than described by the model — see <see cref="ShopDialog"/>.
        /// </summary>
        public void SetIcon(Sprite sprite)
        {
            if (icon != null && sprite != null)
            {
                icon.sprite = sprite;
            }
        }

        void FillContents(IList<string> lines)
        {
            if (contents == null || bulletPrefab == null)
            {
                return;
            }

            int count = lines == null ? 0 : lines.Count;

            while (bullets.Count < count)
            {
                TMP_Text bullet = Instantiate(bulletPrefab, contents, false);
                bullet.gameObject.SetActive(true);
                bullets.Add(bullet);
            }

            for (int i = 0; i < bullets.Count; i++)
            {
                bool visible = i < count;
                bullets[i].gameObject.SetActive(visible);

                if (visible)
                {
                    bullets[i].text = bulletPrefix + lines[i];
                }
            }
        }

        void OnBuy()
        {
            var handler = Bought;
            if (handler != null)
            {
                handler(this);
            }
        }
    }
}
