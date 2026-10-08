using System;
using System.Collections.Generic;
using Dojo.Framework.UI;
using Dojo.Framework.Utilities;
using Dojo.Framework.World;
using Dojo.Game.Placement;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace Dojo.Game.UI
{
    /// <summary>What the in-game menu is currently showing.</summary>
    /// <remarks>
    /// Three states rather than a pair of booleans, because the entry button means a different
    /// thing in each one and a pair of flags allows combinations that cannot happen — entries up
    /// and a panel open at once, for instance.
    /// </remarks>
    public enum InGameMenuState
    {
        /// <summary>Nothing showing. The button raises the entries.</summary>
        Closed,

        /// <summary>The entries are up. The button puts them away.</summary>
        Entries,

        /// <summary>
        /// A panel an entry opened is showing. The button puts it away and stops there.
        /// </summary>
        /// <remarks>
        /// It does not go on to raise the entries. Pressing the button with a panel up says "I am
        /// done with this", and answering that by opening the menu again decides for the player
        /// that they wanted something else instead of nothing.
        /// </remarks>
        Panel,
    }

    /// <summary>
    /// Owns the in-game canvas: whether it is drawing at all, and what the player asking for
    /// something out of the entry point actually means.
    /// </summary>
    /// <remarks>
    /// <see cref="InGameEntryPoint"/> sits a level down, on the object that parents the toggle and
    /// the entries, and reports presses as a bare index — it is deliberately ignorant of what any
    /// entry is for, so the set of entries can change without it changing. That leaves someone
    /// needing to say what index 2 means, and this is that someone: the one component on the canvas
    /// root, where the canvas-wide decisions already live.
    /// <para>
    /// The indices are translated into named events rather than being passed on as numbers, so a
    /// reordering of the entries in the inspector is a change in one place here instead of a silent
    /// change of meaning at every listener. Opening the dialogs is still not done here — see
    /// <see cref="LoadWorldRequested"/> and its siblings — because the dialog framework is reached
    /// through injection and this stays attachable to a canvas that has no container behind it.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(Canvas))]
    public sealed class InGameCanvasUI : MonoBehaviour
    {
        /// <summary>Entry order as authored on <see cref="InGameEntryPoint"/>, bottom first.</summary>
        const int BackEntry = 0;
        const int ShopEntry = 1;
        const int InventoryEntry = 2;
        const int SaveWorldEntry = 3;
        const int LoadWorldEntry = 4;

        /// <summary>
        /// The one content key with a pack behind it, as used by the BuyStrawberry harness.
        /// </summary>
        /// <remarks>
        /// Must match a pack the CDN serves — the harness carried the same warning on the field it
        /// kept this in.
        /// </remarks>
        const string StrawberryKey = "strawberry";

        [Header("Canvas")]
        [Tooltip("The nested canvas this screen draws on. Defaults to the one on this object.")]
        [SerializeField] Canvas canvas;

        [Tooltip("Turned off with the canvas so a hidden screen cannot still swallow clicks. " +
                 "Defaults to the raycaster on this object.")]
        [SerializeField] GraphicRaycaster raycaster;

        [Header("Entry point")]
        [Tooltip("The toggle and its entries. Lives on the child that parents them.")]
        [SerializeField] InGameEntryPoint entryPoint;

        [Header("Back")]
        [Tooltip("Where BACK takes the player. Must match the Lobby scene's name in Build Settings.")]
        [SerializeField] string lobbySceneName = "Lobby";

        /// <summary>Raised when the player asks to go back to the Lobby.</summary>
        public event Action BackRequested;

        /// <summary>Raised when the player asks for the shop.</summary>
        /// <remarks>
        /// Nothing listens yet — the shop has no panel behind it. The entry still closes the menu
        /// like any other, which is why pressing it reads as "nothing there" rather than as a
        /// button that failed: see the note above <see cref="OpenSaveWorld"/>.
        /// </remarks>
        public event Action ShopRequested;

        /// <summary>Raised when the player asks for the inventory.</summary>
        public event Action InventoryRequested;

        /// <summary>Raised when the player asks to save the world.</summary>
        public event Action SaveWorldRequested;

        /// <summary>Raised when the player asks to load a world.</summary>
        public event Action LoadWorldRequested;

        /// <summary>Raised whenever the menu changes state.</summary>
        /// <remarks>
        /// A panel listens for anything that is not <see cref="InGameMenuState.Panel"/> and shuts
        /// itself. That keeps this ignorant of which panels exist while still being the one place
        /// that decides when they are done.
        /// </remarks>
        public event Action<InGameMenuState> StateChanged;

        /// <summary>
        /// Raised with the name once the player has saved through the save dialog. Only then: the
        /// world also saves itself on leaving Edit, and that is not the player choosing to save.
        /// </summary>
        public event Action<string> SavedFromDialog;

        /// <summary>What the menu is showing.</summary>
        public InGameMenuState State { get; private set; } = InGameMenuState.Closed;

        IDialogManager dialogs;
        WorldService worlds;
        IWorldSource source;
        ILoadingService loading;
        WorldPictures pictures;

        /// <summary>
        /// The shop while it is up, so the toggle can put it away again.
        /// </summary>
        /// <remarks>
        /// Held only for the shop, and only because the shop is the one dialog the entry button
        /// dismisses — its own header says TAB TO CLOSE. The world dialogs are questions that own
        /// the screen until they are answered, so nothing out here needs a handle on them.
        /// </remarks>
        IShopDialog shop;

        /// <remarks>
        /// Listed in <c>GameLifetimeScope</c>, and only in a scene that has a world root —
        /// <see cref="WorldService"/> is registered nowhere else, and asking for one that was never
        /// registered throws.
        /// </remarks>
        [Inject]
        public void Construct(IDialogManager dialogManager, WorldService worldService, IWorldSource worldSource,
            ILoadingService loadingService, WorldPictures worldPictures)
        {
            dialogs = dialogManager;
            worlds = worldService;
            source = worldSource;
            loading = loadingService;
            pictures = worldPictures;
        }

        /// <summary>
        /// Esc, while the entries are up, is BACK - the key the mock prints beside it.
        /// </summary>
        /// <remarks>
        /// Only with the entries up. With them down, or a panel showing, Esc belongs to whatever
        /// is on screen, and leaving the world on a key meant for closing a dialog would lose it.
        /// </remarks>
        void Update()
        {
            if (State == InGameMenuState.Entries
                && UnityEngine.InputSystem.Keyboard.current != null
                && UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                OnEntrySelected(BackEntry);
            }
        }

        /// <summary>
        /// Whether the screen is drawing and taking clicks. Disabling the canvas rather than the
        /// object keeps the hierarchy alive, so nothing has to re-find its references on the way
        /// back and the entries keep the positions they were authored at.
        /// </summary>
        public bool Visible
        {
            get { return canvas != null && canvas.enabled; }
            set
            {
                if (canvas != null)
                {
                    canvas.enabled = value;
                }

                if (raycaster != null)
                {
                    raycaster.enabled = value;
                }

                // A screen that goes away with its entries still up would come back holding them
                // open over a floor the player has since changed.
                if (!value)
                {
                    SetState(InGameMenuState.Closed);
                }
            }
        }

        /// <summary>The entry point this canvas speaks for, for anything needing it directly.</summary>
        public InGameEntryPoint EntryPoint
        {
            get { return entryPoint; }
        }

        void Awake()
        {
            if (canvas == null)
            {
                canvas = GetComponent<Canvas>();
            }

            if (raycaster == null)
            {
                raycaster = GetComponent<GraphicRaycaster>();
            }

            if (entryPoint == null)
            {
                entryPoint = GetComponentInChildren<InGameEntryPoint>(true);
            }
        }

        void OnEnable()
        {
            if (entryPoint != null)
            {
                entryPoint.EntrySelected += OnEntrySelected;
                entryPoint.TogglePressed += OnTogglePressed;
            }
        }

        void OnDisable()
        {
            if (entryPoint != null)
            {
                entryPoint.EntrySelected -= OnEntrySelected;
                entryPoint.TogglePressed -= OnTogglePressed;
            }
        }

        /// <summary>
        /// The one button, read against what is on screen. This is the whole of the menu's
        /// behaviour, which is why it is worth being able to see it in one place.
        /// </summary>
        void OnTogglePressed()
        {
            switch (State)
            {
                case InGameMenuState.Closed:
                    SetState(InGameMenuState.Entries);
                    break;

                case InGameMenuState.Entries:
                case InGameMenuState.Panel:
                    SetState(InGameMenuState.Closed);
                    break;
            }
        }

        /// <summary>
        /// Told by a panel that it is now showing, so the button knows to dismiss it rather than
        /// open the menu.
        /// </summary>
        /// <remarks>
        /// Reported by the panel rather than assumed when an entry is pressed. An entry with
        /// nothing behind it yet would otherwise leave the menu believing a panel is up, and the
        /// next press would be spent closing something that was never there.
        /// </remarks>
        public void NotifyPanelOpened()
        {
            SetState(InGameMenuState.Panel);
        }

        /// <summary>Told by a panel that it has closed itself, by its own button or otherwise.</summary>
        public void NotifyPanelClosed()
        {
            if (State == InGameMenuState.Panel)
            {
                SetState(InGameMenuState.Closed);
            }
        }

        /// <summary>
        /// Moves to a state and makes the screen match it.
        /// </summary>
        /// <remarks>
        /// Guarded against a state it is already in, which is what stops the loop when a panel
        /// closes: closing tells this, which raises the change, which tells the panel to close.
        /// </remarks>
        void SetState(InGameMenuState next)
        {
            if (State == next)
            {
                return;
            }

            State = next;
            Apply();

            var handler = StateChanged;
            if (handler != null)
            {
                handler(State);
            }
        }

        void Apply()
        {
            // The shop goes away with the state that was holding it up, which is what makes the
            // toggle close it. Before the entry-point guard below, because a canvas with no entry
            // point authored would otherwise leave the shop on screen with nothing able to shut it.
            if (State != InGameMenuState.Panel)
            {
                CloseShop();
            }

            if (entryPoint == null)
            {
                return;
            }

            if (State == InGameMenuState.Entries)
            {
                entryPoint.SetOpen(true);
            }
            else
            {
                // Immediate, not a slide. Whatever the press opened is up at once, and entries
                // still travelling down in front of it read as the menu being slow to move.
                entryPoint.CloseImmediate();
            }
        }

        /// <summary>Puts the whole menu away, whatever it is showing. Safe to call when already shut.</summary>
        public void CloseEntries()
        {
            SetState(InGameMenuState.Closed);
        }

        /// <summary>Raises the entries, as the button would. The tour does this to show what is behind it.</summary>
        public void OpenEntries()
        {
            SetState(InGameMenuState.Entries);
        }

        /// <summary>The one button, for the tour to point at.</summary>
        public RectTransform ToggleRect => entryPoint != null ? entryPoint.ToggleRect : null;

        /// <summary>The SHOP entry, for the tour to point at.</summary>
        public RectTransform ShopEntryRect => entryPoint != null ? entryPoint.EntryAt(ShopEntry) : null;

        /// <summary>The INVENTORY entry, for the tour to point at.</summary>
        public RectTransform InventoryEntryRect => entryPoint != null ? entryPoint.EntryAt(InventoryEntry) : null;

        /// <summary>The SAVE WORLD entry, for the tour to point at.</summary>
        public RectTransform SaveWorldEntryRect => entryPoint != null ? entryPoint.EntryAt(SaveWorldEntry) : null;

        /// <summary>Opens the shop exactly as its entry does. The tour's OPEN SHOP.</summary>
        public void ShowShop() => OnEntrySelected(ShopEntry);

        /// <summary>Opens the inventory exactly as its entry does, putting the menu away. The tour uses it.</summary>
        public void ShowInventory() => OnEntrySelected(InventoryEntry);

        void OnEntrySelected(int index)
        {
            switch (index)
            {
                case BackEntry:
                    Raise(BackRequested);
                    GoToLobby();
                    break;

                case ShopEntry:
                    Raise(ShopRequested);
                    OpenShop();
                    break;

                case InventoryEntry:
                    Raise(InventoryRequested);
                    break;

                case SaveWorldEntry:
                    Raise(SaveWorldRequested);
                    OpenSaveWorld();
                    break;

                case LoadWorldEntry:
                    Raise(LoadWorldRequested);
                    OpenLoadWorld();
                    break;

                default:
                    // An entry was added to the prefab and nobody taught this what it means. Worth
                    // saying out loud, because the button will otherwise look merely unresponsive.
                    Debug.LogWarning(
                        string.Format("{0}: entry {1} was pressed but has no meaning here.", name, index),
                        this);
                    break;
            }
        }

        // Every entry closes the menu behind it. The state goes to Closed rather than straight to
        // Panel: whether a panel actually opens is the listener's business, and it says so through
        // NotifyPanelOpened. An entry with nothing behind it therefore leaves the menu closed,
        // which is the truth, instead of waiting to dismiss a panel that never appeared.
        /// <summary>
        /// Opens the save-world dialog on the saves already on disk.
        /// </summary>
        /// <remarks>
        /// The dialog is a view: it is handed the list and a callback and decides nothing. Whether
        /// the chosen name already exists, and what replacing it costs, is settled below.
        /// </remarks>
        void OpenSaveWorld()
        {
            if (!DialogsReady())
            {
                return;
            }

            var dialog = dialogs.OpenDialog<ISaveWorldDialog>();

            if (dialog == null)
            {
                Debug.LogError(
                    $"{name} could not open the save-world dialog. Assign SaveWorldDialog.prefab to "
                    + "DialogPrefabs on RootLifetimeScope.",
                    this);
                return;
            }

            // The world's own name is suggested, so re-saving is a confirmation rather than a
            // retyping, and the dialog's button already reads OVERWRITE when that is what it does.
            dialog.Ask(BuildWorldList(), worlds.CurrentWorld, OnSaveConfirmed, null, OnSaveDelete);
        }

        /// <remarks>
        /// Deleted, then the dialog is asked again on the shorter list. The dialog closes itself
        /// when a row's delete is pressed, and reopening is what shows the result — a list that
        /// still held the row would be describing a world that is gone.
        /// </remarks>
        void OnSaveDelete(string worldName)
        {
            worlds.Delete(worldName);
            OpenSaveWorld();
        }

        /// <summary>
        /// Opens the shop on the packs currently on offer.
        /// </summary>
        /// <remarks>
        /// Reported as a panel rather than left to close itself like the world dialogs, because the
        /// mock's header promises TAB TO CLOSE and only the panel state gives the toggle that
        /// meaning. <see cref="Apply"/> is what makes good on it.
        /// </remarks>
        void OpenShop()
        {
            if (!DialogsReady())
            {
                return;
            }

            var dialog = dialogs.OpenDialog<IShopDialog>();

            if (dialog == null)
            {
                Debug.LogError(
                    $"{name} could not open the shop. Assign ShopDialog.prefab to DialogPrefabs on "
                    + "RootLifetimeScope.",
                    this);
                return;
            }

            shop = dialog;
            dialog.Browse(BuildPackList(), OnPackBought, OnShopClosed);

            NotifyPanelOpened();
        }

        /// <summary>Puts the shop away, if it is up. Safe to call when it is not.</summary>
        void CloseShop()
        {
            IShopDialog dialog = shop;
            shop = null;

            // Through the component, not the interface — a destroyed dialog passes a plain null
            // check, for the reason DialogManager.Alive spells out.
            var component = dialog as Component;

            if (component != null && dialog.IsOpen)
            {
                dialog.Hide();
            }
        }

        /// <summary>The player closed the shop with its own X rather than with the toggle.</summary>
        void OnShopClosed()
        {
            shop = null;
            NotifyPanelClosed();
        }

        /// <remarks>
        /// The pack is already down by the time this runs — the dialog acquires it itself, through
        /// the same <c>IEntitlementGrants</c> door the BuyStrawberry harness used. Nothing is
        /// refreshed here on the strength of it: the inventory and anything else that cares listen
        /// for <c>EntitlementsChanged</c>, which also reaches them when a pack arrives by some
        /// route that never opened the shop.
        /// </remarks>
        void OnPackBought(string packKey)
        {
            shop = null;
            NotifyPanelClosed();
        }

        /// <summary>
        /// Describes every pack for the shop.
        /// </summary>
        /// <remarks>
        /// <b>Written out here rather than read from anywhere.</b> There is no pack catalogue to
        /// read: what is on sale, at what price and when it ships live in nobody's data yet, which
        /// is why the mock carries a literal [PRICE] and [DATE] and why those strings are repeated
        /// below instead of being invented. This is the one place to replace when a catalogue
        /// exists, and the shop needs no other change when it does.
        /// <para>
        /// Only <see cref="StrawberryKey"/> is real, and only because the content behind it is: it
        /// is the key the BuyStrawberry harness used, and the same pack the CDN already serves. The
        /// other two are drawn as coming soon precisely because there is nothing to hand over —
        /// marking either available would be a button that downloads a pack that does not exist.
        /// </para>
        /// </remarks>
        List<ShopPack> BuildPackList()
        {
            var locked = new List<string> { "Locked", "Locked", "Locked" };
            const string notOut = "Not released yet. Contents are locked until the pack ships.";

            return new List<ShopPack>
            {
                new ShopPack(
                    StrawberryKey,
                    "Strawberry Pack",
                    "The season-one starter set. Unlocks on purchase, no restart needed.",
                    new List<string> { "[PIECE COUNT] studio pieces", "[AGENT SKIN NAME]", "[FLOOR THEME NAME]" },
                    true,
                    price: "[PRICE]"),

                new ShopPack("grape", "Grape Pack", notOut, locked, false, releasesAt: "[DATE]"),
                new ShopPack("coffee", "Coffee Pack", notOut, locked, false, releasesAt: "[DATE]"),
            };
        }

        /// <summary>
        /// BACK: leaves the world for the Lobby, through the same loading service the Lobby's own
        /// buttons use.
        /// </summary>
        /// <remarks>
        /// Straight away, with no save question: the world is whatever was last saved, and there is
        /// nothing yet that knows whether anything changed since. A picture of the world is taken
        /// first, for the Lobby's continue card.
        /// </remarks>
        void GoToLobby()
        {
            if (loading == null)
            {
                Debug.LogWarning($"{name} was never injected, so BACK has no way to load the Lobby.", this);
                return;
            }

            if (loading.IsLoading)
            {
                return;
            }

            if (pictures != null)
            {
                pictures.Take();
            }

            _ = loading.LoadAsync(SceneLoadRequest.Single(lobbySceneName));
        }

        /// <summary>Opens the load-world dialog on the saves already on disk.</summary>
        void OpenLoadWorld()
        {
            if (!DialogsReady())
            {
                return;
            }

            var dialog = dialogs.OpenDialog<ILoadWorldDialog>();

            if (dialog == null)
            {
                Debug.LogError(
                    $"{name} could not open the load-world dialog. Assign LoadWorldDialog.prefab to "
                    + "DialogPrefabs on RootLifetimeScope.",
                    this);
                return;
            }

            dialog.Choose(BuildWorldList(), OnLoadConfirmed);
        }

        /// <remarks>
        /// No separate are-you-sure for a replacement. <see cref="WorldService.SaveAs"/> gives the
        /// reason: the dialog's own button says OVERWRITE when that is what pressing it does.
        /// </remarks>
        void OnSaveConfirmed(string worldName)
        {
            worlds.SaveAs(worldName);
            SavedFromDialog?.Invoke(worldName);
        }

        void OnLoadConfirmed(string worldName)
        {
            worlds.Load(worldName);
        }

        bool DialogsReady()
        {
            if (dialogs != null && worlds != null)
            {
                return true;
            }

            Debug.LogWarning(
                $"{name} was never injected, so the world entries do nothing. This component has to "
                + "be listed in GameLifetimeScope, and the scene needs a world root.",
                this);

            return false;
        }

        /// <summary>
        /// Describes every saved world for the two dialogs.
        /// </summary>
        /// <remarks>
        /// Each world is loaded to be counted, which is the only way to know its pieces and agents —
        /// the source reports names, not sizes. The same trade the lobby's list makes. Built on
        /// demand rather than held, so a save made a moment ago is in the next list.
        /// </remarks>
        List<WorldChoice> BuildWorldList()
        {
            var list = new List<WorldChoice>();

            List<string> names = worlds.WorldNames();
            if (names == null)
            {
                return list;
            }

            for (int i = 0; i < names.Count; i++)
            {
                WorldSnapshot snapshot = source != null && source.IsReady ? source.Load(names[i]) : null;

                if (snapshot == null)
                {
                    // Named but not loadable. Offered with no counts rather than left out: on the
                    // save screen it is still a name that would be overwritten, and hiding it would
                    // let the player replace something they were never shown.
                    list.Add(new WorldChoice(names[i], 0, 0));
                    continue;
                }

                list.Add(new WorldChoice(
                    names[i],
                    snapshot.PieceCount(),
                    snapshot.AgentCount()));
            }

            return list;
        }

        void Raise(Action handler)
        {
            SetState(InGameMenuState.Closed);

            if (handler != null)
            {
                handler();
            }
        }
    }
}
