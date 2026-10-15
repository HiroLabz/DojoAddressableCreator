using System;
using Dojo.Framework.Content;
using Dojo.Framework.UI;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace Dojo.Game.UI
{
    /// <summary>
    /// A test harness for acquiring a content pack in a running game.
    /// </summary>
    /// <remarks>
    /// <b>This is scaffolding, not a store.</b> It stands in for the screen a purchase will
    /// eventually go through, so the path behind that screen — a payload arrives naming a content
    /// key, the pack comes down, the backend is told, the inventory refills — can be exercised and
    /// watched before any of the commerce exists. Delete this panel and nothing in
    /// <see cref="IEntitlementGrants"/> changes; that is the arrangement it is here to prove.
    /// <para>
    /// The panel itself is an ordinary <see cref="InGamePopup"/>: authored parked off the right of
    /// the canvas with its toggle handle protruding, exactly as the file tray is. It only overrides
    /// <see cref="ShownPosition"/>, because the base class slides in from the left.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(RectTransform))]
    public sealed class BuyStrawberryPopup : InGamePopup
    {
        [Header("Simulated purchase")]
        [Tooltip("The button that stands in for completing a purchase.")]
        [SerializeField] Button buyButton;

        [Tooltip("The content key the simulated payload carries. Must match a pack the CDN serves.")]
        [SerializeField] string contentKey = "strawberry";

        [Tooltip("Recorded on the backend against the grant, for later reconciliation.")]
        [SerializeField] string reason = "sim:buy";

        [Tooltip("Hide the panel once the pack has been acquired.")]
        [SerializeField] bool hideWhenAcquired = true;

        /// <summary>
        /// The acquisition door, and the only interface this panel needs.
        /// </summary>
        /// <remarks>
        /// <see cref="IEntitlementGrants"/> rather than the concrete service: this class can ask for
        /// a pack to be acquired and cannot do anything else to the entitlement list — it cannot
        /// seed it, assign it, or add a key without the download behind it having succeeded.
        /// </remarks>
        IEntitlementGrants grants;

        /// <summary>
        /// True while an acquisition is running, which is what stops a second click starting a
        /// second one.
        /// </summary>
        /// <remarks>
        /// Belt as well as braces: <c>EntitlementService</c> admits the key before its first await,
        /// so its own gate already turns a second call into <c>AlreadyOwned</c>. This exists to keep
        /// the button visibly disabled rather than silently inert, which is the difference between
        /// a player waiting and a player clicking harder.
        /// </remarks>
        bool running;

        [Inject]
        public void Construct(IEntitlementGrants grants)
        {
            this.grants = grants;
        }

        protected override void Awake()
        {
            // First: it captures the authored closed position and wires the toggle handle. Anything
            // here that moved the panel before that ran would teach the base class the wrong place
            // to hide it.
            base.Awake();

            if (buyButton != null)
            {
                buyButton.onClick.AddListener(OnBuyClicked);
            }
            else
            {
                Debug.LogWarning(
                    nameof(BuyStrawberryPopup) + " on '" + name + "' has no buy button assigned, so "
                    + "there is nothing to click. Assign buyStrawberryCTA in the Inspector.", this);
            }
        }

        protected override void OnDestroy()
        {
            if (buyButton != null)
            {
                buyButton.onClick.RemoveListener(OnBuyClicked);
            }

            base.OnDestroy();
        }

        /// <summary>Runs the simulated purchase. Wired to the CTA, and safe to call directly.</summary>
        public void OnBuyClicked()
        {
            if (running)
            {
                return;
            }

            _ = BuyAsync();
        }

        async Awaitable BuyAsync()
        {
            if (grants == null)
            {
                Debug.LogError(
                    nameof(BuyStrawberryPopup) + " on '" + name + "' was never injected, so it "
                    + "cannot acquire anything. Add it to GameLifetimeScope's injection list.", this);
                return;
            }

            running = true;
            SetButtonInteractable(false);

            try
            {
                // ── The simulated payload ────────────────────────────────────────────────
                // A real one would arrive from a purchase receipt and carry the key. Here the key
                // is the one on this component, which is the only part of this that is pretend —
                // everything downstream of this line is the real path.
                var result = await grants.AcquireAsync(contentKey, reason);

                switch (result.Outcome)
                {
                    case AcquireOutcome.Acquired:
                        Debug.Log("[BuyStrawberry] acquired '" + result.Key + "'.");
                        break;

                    case AcquireOutcome.AlreadyOwned:
                        // The gate held. Worth a line rather than silence: "nothing happened" and
                        // "nothing needed to happen" look identical from the outside.
                        Debug.Log("[BuyStrawberry] '" + result.Key + "' is already owned; nothing to do.");
                        break;

                    default:
                        Debug.LogWarning("[BuyStrawberry] could not acquire '" + result.Key + "': " + result.Error);
                        break;
                }

                // Hidden only when there is nothing left to buy, which covers the already-owned
                // case too. A failure leaves the panel up so the button can be tried again.
                if (hideWhenAcquired && result.Outcome != AcquireOutcome.Failed)
                {
                    Hide();
                }
            }
            catch (OperationCanceledException)
            {
                // Play mode ended mid-download. Nothing to report and nobody to report it to.
            }
            catch (Exception exception)
            {
                // AcquireAsync returns failures rather than throwing them, so anything arriving
                // here is unexpected and worth the stack trace.
                Debug.LogException(exception, this);
            }
            finally
            {
                running = false;
                SetButtonInteractable(true);
            }
        }

        void SetButtonInteractable(bool value)
        {
            if (buyButton != null)
            {
                buyButton.interactable = value;
            }
        }

        /// <summary>
        /// Where the panel rests when open: slid in from the right until its right edge meets the
        /// canvas's.
        /// </summary>
        /// <remarks>
        /// The base class comes in from the left, which suits the inventory and the call tray. This
        /// one is parked off the right like the file tray, and uses the same arithmetic it does.
        /// Computed from the current rects rather than stored, so it survives a resolution change.
        /// </remarks>
        protected override Vector2 ShownPosition()
        {
            var parent = Rect.parent as RectTransform;

            if (parent == null)
            {
                return HiddenPosition;
            }

            // anchoredPosition is measured from the anchor, so locate the anchor within the parent
            // first, then offset by this panel's own pivot.
            var anchorX = parent.rect.xMin + parent.rect.width * ((Rect.anchorMin.x + Rect.anchorMax.x) * 0.5f);
            var shownX = parent.rect.xMax - anchorX - Rect.rect.width * (1f - Rect.pivot.x);

            return new Vector2(shownX, HiddenPosition.y);
        }
    }
}
