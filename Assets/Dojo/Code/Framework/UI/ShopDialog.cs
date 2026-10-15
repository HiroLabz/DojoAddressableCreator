using System;
using System.Collections.Generic;
using Dojo.Framework.Content;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace Dojo.Framework.UI
{
    /// <summary>
    /// A modal showing the flavour packs: a count line, a title, a blurb, and a card per pack.
    /// </summary>
    /// <remarks>
    /// <b>Authored only.</b> Like the two world dialogs this builds nothing in code — every part
    /// comes from <c>ShopDialog.prefab</c>, and a missing reference is reported rather than papered
    /// over with a generated stand-in.
    /// <para>
    /// Cards are pooled rather than rebuilt per open, for the reason <see cref="LoadWorldDialog"/>
    /// gives for pooling rows: the catalogue is opened far more often than it changes.
    /// </para>
    /// <para>
    /// There is no accepting button and no selection. Each card carries its own buy, so the dialog
    /// has nothing to be waiting on — which is why closing it is an ordinary ending here and a
    /// cancel in the dialogs that do ask a question.
    /// </para>
    /// </remarks>
    public sealed class ShopDialog : MonoBehaviour, IShopDialog
    {
        /// <summary>One pack's art, kept against the key the pack is known by.</summary>
        /// <remarks>
        /// Authored here rather than carried on <see cref="ShopPack"/>, which would make the model
        /// hold a Sprite and so make whoever builds the list responsible for loading art. The list
        /// is built from a catalogue; the art belongs with the rest of the dialog's look.
        /// </remarks>
        [Serializable]
        public sealed class PackArt
        {
            [Tooltip("The pack's key, matching ShopPack.Key.")]
            public string Key;

            [Tooltip("What to draw on that pack's card.")]
            public Sprite Sprite;
        }

        [Header("Chrome")]
        [Tooltip("Turned on and off to show and hide the dialog.")]
        [SerializeField] GameObject root;

        [Tooltip("The dialog's title, beside the diamond.")]
        [SerializeField] TMP_Text title;

        [Tooltip("The count line beside the title, e.g. '3 packages  ·  1 available'.")]
        [SerializeField] TMP_Text countLine;

        [Tooltip("The line under the title explaining what a pack is.")]
        [SerializeField] TMP_Text blurb;

        [Header("Cards")]
        [Tooltip("Cards are created under this. Wants a horizontal layout group.")]
        [SerializeField] RectTransform cards;

        [Tooltip("The card prefab, instantiated once per pack.")]
        [SerializeField] ShopPackCard cardPrefab;

        [Tooltip("Shown instead of the cards when there is nothing to sell.")]
        [SerializeField] GameObject emptyNotice;

        [Tooltip("Each pack's art, matched to the pack by key. A pack with no entry keeps whatever " +
                 "the card prefab was authored with.")]
        [SerializeField] PackArt[] art = new PackArt[0];

        [Header("Buttons")]
        [Tooltip("The X in the corner. Closes the shop.")]
        [SerializeField] Button closeButton;

        [Header("Backdrop")]
        [Tooltip("Optional. Photographs the screen before the dialog appears so the scrim can blur " +
                 "it. Leave empty for a dialog with no blurred backdrop.")]
        [SerializeField] UIBackdropBlur backdrop;

        [Header("Purchase")]
        [Tooltip("Recorded on the backend against the grant, for later reconciliation.")]
        [SerializeField] string reason = "shop:buy";

        readonly List<ShopPackCard> pool = new List<ShopPackCard>();

        /// <summary>
        /// The acquisition door, and the only service this dialog needs.
        /// </summary>
        /// <remarks>
        /// <see cref="IEntitlementGrants"/> rather than the concrete service, for the reason
        /// <c>BuyStrawberryPopup</c> gives for asking for the same one: through this the shop can
        /// ask for a pack and can do nothing else to the entitlement list — it cannot seed it,
        /// assign it, or add a key without the download behind it having succeeded.
        /// <para>
        /// Injected, which works because <c>EntitlementService</c> is registered on the same root
        /// scope that registers this dialog. A dialog built by the root container cannot reach
        /// anything registered on a scene scope, so a service that moved there would arrive null.
        /// </para>
        /// </remarks>
        IEntitlementGrants grants;

        /// <summary>
        /// The screen a purchase runs behind, and the content service whose download feeds its bar.
        /// </summary>
        /// <remarks>
        /// Optional, both of them: a shop with no screen still buys packs, it just does it with the
        /// card dimmed. That keeps the purchase path working in any scene that has not brought a
        /// loading screen up.
        /// </remarks>
        ILoadingScreen loadingScreen;

        IContentService content;

        Action<string> bought;
        Action closed;

        /// <summary>
        /// True while an acquisition is running, which is what stops a second click starting a
        /// second one.
        /// </summary>
        /// <remarks>
        /// Belt as well as braces, exactly as in <c>BuyStrawberryPopup</c>: the service admits the
        /// key before its first await, so its own gate already turns a second call into
        /// <see cref="AcquireOutcome.AlreadyOwned"/>. This keeps the button visibly disabled rather
        /// than silently inert, which is the difference between a player waiting and one clicking
        /// harder.
        /// </remarks>
        bool running;

        [Inject]
        public void Construct(
            IEntitlementGrants entitlementGrants,
            ILoadingScreen screen,
            IContentService contentService)
        {
            grants = entitlementGrants;
            loadingScreen = screen;
            content = contentService;
        }

        // Wired exactly once. Awake is the normal moment, but a caller that creates this component
        // and asks it something in the same breath would otherwise reach an unwired dialog.
        bool ready;

        /// <inheritdoc />
        public bool IsOpen => root != null && root.activeSelf;

        void Awake()
        {
            EnsureWired();
        }

        void EnsureWired()
        {
            if (ready)
            {
                return;
            }

            ready = true;

            if (root == null || cards == null || cardPrefab == null)
            {
                Debug.LogError(
                    $"{nameof(ShopDialog)} on '{name}' is missing authored parts — it needs at " +
                    "least root, cards and cardPrefab. Assign them on ShopDialog.prefab.",
                    this);
                return;
            }

            if (closeButton != null)
            {
                closeButton.onClick.AddListener(Dismiss);
            }

            root.SetActive(false);
        }

        void OnDestroy()
        {
            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(Dismiss);
            }

            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i] != null)
                {
                    pool[i].Bought -= OnCardBought;
                }
            }
        }

        /// <inheritdoc />
        public void Browse(IList<ShopPack> packs, Action<string> onBought, Action onClose = null)
        {
            EnsureWired();

            if (root == null || cardPrefab == null)
            {
                return;
            }

            if (IsOpen)
            {
                Dismiss();
            }

            bought = onBought;
            closed = onClose;

            Fill(packs);
            Show();
        }

        void Fill(IList<ShopPack> packs)
        {
            int count = packs == null ? 0 : packs.Count;

            while (pool.Count < count)
            {
                pool.Add(BuildCard());
            }

            int available = 0;

            for (int i = 0; i < pool.Count; i++)
            {
                bool visible = i < count;
                pool[i].gameObject.SetActive(visible);

                if (!visible)
                {
                    continue;
                }

                pool[i].Bind(packs[i]);
                pool[i].SetIcon(ArtFor(packs[i].Key));

                if (packs[i].IsAvailable)
                {
                    available++;
                }
            }

            if (countLine != null)
            {
                countLine.text = count + (count == 1 ? " package" : " packages")
                    + "  ·  " + available + " available";
            }

            if (emptyNotice != null)
            {
                emptyNotice.SetActive(count == 0);
            }
        }

        /// <summary>The art authored for a pack, or null to leave the card's own sprite alone.</summary>
        Sprite ArtFor(string key)
        {
            for (int i = 0; i < art.Length; i++)
            {
                if (art[i] != null && art[i].Key == key)
                {
                    return art[i].Sprite;
                }
            }

            return null;
        }

        ShopPackCard BuildCard()
        {
            ShopPackCard card = Instantiate(cardPrefab, cards, false);
            card.Bought += OnCardBought;
            return card;
        }

        /// <remarks>
        /// The locked cards keep a live button so they can still say when the pack arrives, and
        /// their interactable is off — but that is a look, and a look is not a guarantee. The check
        /// is repeated here so a card that is somehow pressed cannot start a purchase of something
        /// that is not for sale.
        /// </remarks>
        void OnCardBought(ShopPackCard card)
        {
            if (card == null || !card.IsAvailable || running)
            {
                return;
            }

            _ = BuyAsync(card);
        }

        /// <summary>
        /// Acquires the pack behind a card.
        /// </summary>
        /// <remarks>
        /// The same path <c>BuyStrawberryPopup</c> exercised, reached from a real storefront instead
        /// of a test panel: a payload names a content key, the pack comes down, the backend is told,
        /// the inventory refills. What the harness only pretended is the step before this one —
        /// where the key comes from. Here it is the card the player pressed rather than a string
        /// typed into a component, and that is the whole of the difference.
        /// <para>
        /// No money changes hands yet. A receipt should be what names the key, and until there is
        /// one this is a button that grants a pack for free — see the note on
        /// <see cref="IShopDialog.Browse"/> about what has to exist before the shop can ship.
        /// </para>
        /// </remarks>
        async Awaitable BuyAsync(ShopPackCard card)
        {
            if (grants == null)
            {
                Debug.LogError(
                    $"{nameof(ShopDialog)} on '{name}' was never injected, so it cannot acquire "
                    + "anything. It has to be built by the container — see RootLifetimeScope.",
                    this);
                return;
            }

            running = true;
            card.SetBusy(true);

            // The shop goes before the screen comes up: a pack can take a long time to arrive, and
            // a dimmed button on a storefront is not an answer to "is anything happening". The
            // screen owns the wait from here, and the player is returned to the game rather than to
            // a shop they have finished with.
            LoadingRun run = BeginPurchaseRun(card);

            if (run != null)
            {
                Hide();
            }

            Action<ContentProgress> onContent = null;

            if (run != null && content != null)
            {
                // Filtered by label: other packs can be downloading at the same time, and their
                // bytes are not this bar's business.
                string key = card.PackKey;

                onContent = progress =>
                {
                    if (run.Current != null && progress.Label == key)
                    {
                        run.Current.Report(progress.Normalized);
                    }
                };

                content.ProgressChanged += onContent;
            }

            try
            {
                AcquireResult result = await grants.AcquireAsync(
                    card.PackKey,
                    reason,
                    run == null ? default : run.CancellationToken,
                    run == null ? (Action<AcquirePhase>)null : phase => OnPhase(run, phase));

                switch (result.Outcome)
                {
                    case AcquireOutcome.Acquired:
                        Debug.Log("[Shop] acquired '" + result.Key + "'.");
                        break;

                    case AcquireOutcome.AlreadyOwned:
                        // The gate held. Worth a line rather than silence: "nothing happened" and
                        // "nothing needed to happen" look identical from the outside.
                        Debug.Log("[Shop] '" + result.Key + "' is already owned; nothing to do.");
                        break;

                    default:
                        Debug.LogWarning("[Shop] could not acquire '" + result.Key + "': " + result.Error);
                        break;
                }

                // A failure puts the shop back so the button can be tried again — the player has
                // not got what they came for, and returning them to the game would be claiming
                // otherwise. With no loading screen the shop never left, and this is a no-op.
                if (result.Outcome == AcquireOutcome.Failed)
                {
                    if (run != null)
                    {
                        Show();
                    }

                    return;
                }

                var callback = bought;

                // Closed before the callback, the order the other dialogs use: whatever answers a
                // purchase is free to ask the next question without a dead shop still on screen.
                Hide();

                if (callback != null)
                {
                    callback(result.Key);
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

                if (onContent != null)
                {
                    content.ProgressChanged -= onContent;
                }

                if (run != null)
                {
                    run.Complete();
                }

                // The card is gone if the dialog was torn down mid-purchase, which is ordinary:
                // the manager destroys a dialog once it is closed.
                if (card != null)
                {
                    card.SetBusy(false);
                }
            }
        }

        /// <summary>
        /// Puts up the screen a purchase runs behind, or null when there is no screen to use.
        /// </summary>
        /// <remarks>
        /// Three steps, matching what an acquisition actually does — see
        /// <see cref="AcquirePhase"/>. Only the download is weighted, because only the download
        /// takes time worth drawing: the other two are a catalogue rebuild and two backend calls.
        /// <para>
        /// Cancellable, unlike the startup run. A pack download is long, it is something the player
        /// chose to start, and <c>AcquireAsync</c> rolls the key back out of the entitlement list
        /// when it is cancelled — so there is a safe way out to offer.
        /// </para>
        /// </remarks>
        LoadingRun BeginPurchaseRun(ShopPackCard card)
        {
            if (loadingScreen == null)
            {
                return null;
            }

            return loadingScreen.Begin(new LoadingRequest(
                PackTitle(card),
                "Unlocking your new pieces.",
                new[]
                {
                    new LoadingStepPlan("DOWNLOADING", "Pack downloaded", 8f),
                    new LoadingStepPlan("READING CATALOGUE", "Catalogue read", 1f),
                    new LoadingStepPlan("UPDATING INVENTORY", "Inventory updated", 1f),
                },
                cancellable: true));
        }

        /// <summary>The pack's own name as the screen's title, falling back to its key.</summary>
        static string PackTitle(ShopPackCard card)
        {
            string name = card == null ? null : card.PackName;

            return string.IsNullOrEmpty(name)
                ? (card == null ? "PACK" : card.PackKey.ToUpperInvariant())
                : name.ToUpperInvariant();
        }

        /// <remarks>
        /// <c>Advance</c> rather than an index lookup, so the steps stay in step with the phases
        /// by order alone — the plan above is written in the order the phases arrive.
        /// </remarks>
        static void OnPhase(LoadingRun run, AcquirePhase phase)
        {
            switch (phase)
            {
                case AcquirePhase.Downloading:
                    run.Advance("DOWNLOADING");
                    break;

                case AcquirePhase.ReadingCatalogue:
                    run.Advance("READING CATALOGUE");
                    break;

                case AcquirePhase.UpdatingInventory:
                    run.Advance("UPDATING INVENTORY");
                    break;
            }
        }

        void Dismiss()
        {
            var callback = closed;
            Hide();

            if (callback != null)
            {
                callback();
            }
        }

        /// <inheritdoc />
        public void Show()
        {
            EnsureWired();

            // The backdrop is photographed while the dialog is still switched off — a picture taken
            // afterwards would have the dialog in it. That costs one frame, which is why the root is
            // switched on in the callback rather than here.
            if (backdrop != null)
            {
                backdrop.CaptureThen(Reveal);
                return;
            }

            Reveal();
        }

        void Reveal()
        {
            if (root != null)
            {
                root.SetActive(true);
            }

            // Nothing animates here yet, so it is on screen the instant it is switched on.
            ShowComplete();
        }

        /// <inheritdoc />
        public void ShowComplete()
        {
        }

        /// <inheritdoc />
        public void Hide()
        {
            if (root != null)
            {
                root.SetActive(false);
            }

            HideComplete();
        }

        /// <inheritdoc />
        public void HideComplete()
        {
            bought = null;
            closed = null;
        }
    }
}
