using System;
using System.Collections.Generic;
using Dojo.Framework.Content;
using Dojo.Framework.Inventory;
using Dojo.Framework.UI;
using Dojo.Game.Systems;
using UnityEngine;
using UnityEngine.UI;
using VContainer;
using VContainer.Unity;

namespace Dojo.Game.UI
{
    /// <summary>
    /// The inventory drawer: category tabs, and a grid of <see cref="ItemRenderer3D"/> tiles built
    /// from whatever the player's packs actually contain.
    /// </summary>
    /// <remarks>
    /// The sliding, the toggle button and the remembered closed position come from
    /// <see cref="InGamePopup"/>, the same base <c>AgentDetails</c> uses; this adds the tabs and
    /// the grid. The panel is authored parked off the left of the canvas, which is the closed
    /// position the base captures.
    /// <para>
    /// The grid is <c>IItemCatalog.InCategory</c> for the selected tab, and nothing else. This
    /// class does not read a manifest, does not know which packs exist, and does not decide what
    /// category anything is — it maps a tab to a category name and renders what comes back. An
    /// item the player has no pack for is therefore absent rather than broken, which is the whole
    /// reason the list comes from the catalogue instead of from a file listing everything.
    /// </para>
    /// </remarks>
    public sealed class InventoryUI : InGamePopup
    {
        /// <summary>Which tab is showing.</summary>
        public enum Category
        {
            /// <summary>
            /// No tab chosen. What the drawer opens on: an empty grid, no card, and every tab the
            /// same size so none of them looks picked.
            /// </summary>
            /// <remarks>
            /// First in the enum so it is also the default value, which means a field that has
            /// never been set reads as "nothing selected" rather than as some arbitrary tab.
            /// </remarks>
            None,

            Floor,
            Wall,
            Tables,
            Plants,
            Drawers,
            Others,

            /// <summary>
            /// The office staff rather than the furniture. The one tab whose contents come from the
            /// agent roster rather than the item catalogue, and the one that hands its panel over
            /// to <see cref="AgentDisplayUI"/> rather than building tiles itself.
            /// </summary>
            Agents,

            /// <summary>
            /// The player. One entry rather than a roster, so it has no tile strip at all — the
            /// whole tab is the card, handed over to <see cref="ManagerDisplayUI"/>.
            /// </summary>
            You,

            // Appended rather than slotted in beside the other furniture tabs, so the values above
            // keep the ordinals anything already serialized was written with.

            /// <summary>Powered things: lights, screens, the vending machine.</summary>
            Appliances,

            /// <summary>Furniture with no tab of its own — seating, shelving.</summary>
            Furnitures,
        }

        [Header("Tabs")]
        [SerializeField] Button floorButton;
        [SerializeField] Button wallButton;
        [SerializeField] Button tableButton;
        [SerializeField] Button plantsButton;
        [SerializeField] Button drawersButton;
        [SerializeField] Button appliancesButton;
        [SerializeField] Button furnituresButton;
        [SerializeField] Button othersButton;
        [SerializeField] Button agentsButton;
        [SerializeField] Button youButton;

        [Header("Agents")]
        [Tooltip("The agentsbox panel and everything the Agents tab does. Left empty the tab still " +
                 "selects, and says once that there is nothing behind it.")]
        [SerializeField] AgentDisplayUI agentsPanel;

        [Header("You")]
        [Tooltip("The youBox panel and everything the You tab does. Left empty the tab still " +
                 "selects, and says once that there is nothing behind it.")]
        [SerializeField] ManagerDisplayUI youPanel;

        [Header("Grid")]
        [Tooltip("Parent the tiles are created under. The object carrying the GridLayoutGroup.")]
        [SerializeField] Transform containerRenderer;

        [Tooltip("Tile instantiated once per item.")]
        [SerializeField] ItemRenderer3D itemPrefab;

        // The drawer's contents used to be a serialized thumbnails.json plus five keyword rules
        // that guessed a piece's tab from its prefab name. Both are gone: what the player has and
        // what each thing is now come from IItemCatalog, which reads it from the packs that
        // actually came down. Nothing here is configured, because nothing here decides.

        [Header("Tab appearance")]
        [Tooltip("Scale applied to the tab that is showing.")]
        [SerializeField] float selectedScale = 1.2f;

        [Tooltip("Scale applied to the tabs that are not.")]
        [SerializeField] float unselectedScale = 1f;

        readonly List<ItemRenderer3D> tiles = new List<ItemRenderer3D>();

        /// <summary>The tab currently showing.</summary>
        public Category Selected { get; private set; } = Category.None;

        /// <summary>Tiles currently in the grid.</summary>
        public int VisibleCount => tiles.Count;

        /// <remarks>
        /// base.Awake() first: it captures the authored closed position and wires the toggle
        /// button, and it has to run before anything can move the panel.
        /// </remarks>
        protected override void Awake()
        {
            base.Awake();

            Wire(floorButton, Category.Floor);
            Wire(wallButton, Category.Wall);
            Wire(tableButton, Category.Tables);
            Wire(plantsButton, Category.Plants);
            Wire(drawersButton, Category.Drawers);
            Wire(appliancesButton, Category.Appliances);
            Wire(furnituresButton, Category.Furnitures);
            Wire(othersButton, Category.Others);
            Wire(agentsButton, Category.Agents);
            Wire(youButton, Category.You);
        }

        void Start()
        {
            // Nothing selected. The drawer opens on an empty grid with no tab picked out, so the
            // first thing the player does is choose one — rather than the drawer having decided
            // for them that they came for the tables.
            //
            // Still run through SelectCategory rather than simply left alone: it is what sizes
            // every tab as unselected and lowers both cards, and the authored scene has the agents
            // card and the manager card visible so they can be arranged.
            SelectCategory(Category.None);
        }

        void Wire(Button button, Category category)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.AddListener(() => SelectCategory(category));
        }

        /// <summary>
        /// Opens the drawer, and with it the Edit phase.
        /// </summary>
        /// <remarks>
        /// The drawer being open <em>is</em> the edit session — there is no separate mode switch
        /// for the player to forget. Furniture becomes movable and deletable, and everyone living
        /// in the room stands still until it closes.
        /// </remarks>
        public override void Show()
        {
            base.Show();

            if (phase != null)
            {
                phase.Enter(GamePhase.Edit, "inventory opened");
            }
        }

        /// <summary>Closes the drawer and hands the room back to its occupants.</summary>
        public override void Hide()
        {
            base.Hide();

            if (phase != null)
            {
                phase.Enter(GamePhase.Play, "inventory closed");
            }
        }

        /// <summary>
        /// Rebuilds the grid from the catalogue as it stands now, keeping the selected tab.
        /// </summary>
        /// <remarks>
        /// For a catalogue that changed underneath the panel — a pack acquired mid-session, which
        /// makes items appear in tabs the player may already be looking at. Distinct from
        /// <see cref="SelectCategory"/> because nothing about the selection changed, and distinct
        /// from the inherited <c>Show()</c>, which slides the drawer rather than refilling it.
        /// </remarks>
        public void Refresh() => Rebuild();

        /// <summary>
        /// Switches tab and rebuilds the grid. Named apart from the inherited <c>Show()</c>, which
        /// slides the whole drawer in — changing tab and opening the panel are different things and
        /// two overloads of one name would invite calling the wrong one.
        /// </summary>
        public void SelectCategory(Category category)
        {
            Selected = category;
            SizeTabs();
            Rebuild();
        }

        /// <summary>Convenience entry points for a Button's own OnClick list in the inspector.</summary>
        public void ShowFloors() => SelectCategory(Category.Floor);

        public void ShowWalls() => SelectCategory(Category.Wall);

        public void ShowTables() => SelectCategory(Category.Tables);

        public void ShowPlants() => SelectCategory(Category.Plants);

        public void ShowDrawers() => SelectCategory(Category.Drawers);

        public void ShowAppliances() => SelectCategory(Category.Appliances);

        public void ShowFurnitures() => SelectCategory(Category.Furnitures);

        public void ShowOthers() => SelectCategory(Category.Others);

        public void ShowAgents() => SelectCategory(Category.Agents);

        public void ShowYou() => SelectCategory(Category.You);

        /// <summary>
        /// Marks the showing tab by size rather than by colour, so each tab keeps whatever colour
        /// it was authored with.
        /// </summary>
        void SizeTabs()
        {
            Size(floorButton, Category.Floor);
            Size(wallButton, Category.Wall);
            Size(tableButton, Category.Tables);
            Size(plantsButton, Category.Plants);
            Size(drawersButton, Category.Drawers);
            Size(appliancesButton, Category.Appliances);
            Size(furnituresButton, Category.Furnitures);
            Size(othersButton, Category.Others);
            Size(agentsButton, Category.Agents);
            Size(youButton, Category.You);
        }

        void Size(Button button, Category category)
        {
            if (button == null)
            {
                return;
            }

            var scale = category == Selected ? selectedScale : unselectedScale;
            button.transform.localScale = new Vector3(scale, scale, 1f);
        }

        IObjectResolver resolver;

        // Prefabs and thumbnails come from here now. Synchronous reads are safe: the startup gate
        // holds the Lobby's entry points shut until the catalogue is downloaded, so by the time
        // this panel builds a tile the asset is already in memory.
        IContentService content;

        // What the drawer shows, and the only thing that knows an address is a chair. The grid is
        // whatever this holds for the selected tab, so an item the player has no pack for is not a
        // broken tile — it is simply not in the list.
        IItemCatalog catalog;

        // What the player holds. Read-only by design — this panel asks the gate what is owned and
        // listens for it changing; it has no way to change the answer, which is the whole point of
        // the interface being separate from the one a store screen takes.
        IEntitlements entitlements;

        // Whether the grid has been filled at least once, which is what says a late-arriving
        // container has something to go back and fix.
        bool built;

        /// <summary>
        /// Receives the container the tiles are built through, and rebuilds anything built without
        /// it.
        /// </summary>
        /// <remarks>
        /// The resolver itself rather than a PlacementController this panel does not use: the
        /// dependency belongs to the tile, and passing it down through <c>Bind</c> would make this
        /// panel a courier for every dependency a tile ever grows.
        /// <para>
        /// The rebuild is not defensive tidying, it is the fix for a real ordering problem. This
        /// panel fills its grid in <c>Start</c>, and injection does not reliably beat that: the
        /// scene scope defers building until its parent root scope is ready, and that root is
        /// <c>DontDestroyOnLoad</c> and older than the scene, so the callback that injects this
        /// panel can land after <c>Start</c> has already run. It did — every tile in the opening
        /// tab came out with no controller and did nothing when clicked. Rebuilding here makes the
        /// order irrelevant instead of hoping for one: whichever of the two happens second, the
        /// tiles standing at the end were built through the container.
        /// </para>
        /// </remarks>
        /// <summary>
        /// The phase decider, as the concrete service rather than <c>IGamePhase</c>.
        /// </summary>
        /// <remarks>
        /// Deliberate, and the same arrangement the entitlement gate uses: <c>Enter</c> is not on
        /// the read-only interface, so the one panel allowed to change the phase is the one panel
        /// that has to name the service. Opening this drawer is what "the player came to rearrange
        /// the room" means, and closing it is what ends it.
        /// </remarks>
        GamePhaseService phase;

        [Inject]
        public void Construct(
            IObjectResolver container,
            IContentService content,
            IItemCatalog catalog,
            IEntitlements entitlements,
            GamePhaseService phase)
        {
            this.resolver = container;
            this.content = content;
            this.catalog = catalog;
            this.phase = phase;

            // Swapped rather than added to, because injection can run more than once across a
            // scene reload and a second subscription would rebuild the grid twice per change.
            if (this.entitlements != null)
            {
                this.entitlements.Changed -= OnEntitlementsChanged;
            }

            this.entitlements = entitlements;

            if (this.entitlements != null)
            {
                this.entitlements.Changed += OnEntitlementsChanged;
            }

            if (built)
            {
                Rebuild();
            }
        }

        /// <summary>
        /// Refills the grid when the player acquires a pack mid-session.
        /// </summary>
        /// <remarks>
        /// The panel listens rather than being told, so whatever performs an acquisition — the test
        /// harness today, a store screen later — needs no reference to the inventory and no memory
        /// to refresh it. The event fires only after the content is resident and the catalogue
        /// rebuilt, so reading the catalogue here is safe without checking anything first.
        /// </remarks>
        void OnEntitlementsChanged(EntitlementsChanged change)
        {
            if (string.IsNullOrEmpty(change.AddedKey))
            {
                // The opening adoption at sign-in, which lands before this panel exists in any
                // meaningful sense and is already covered by the Start/Construct rebuild.
                return;
            }

            Refresh();
        }

        protected override void OnDestroy()
        {
            if (entitlements != null)
            {
                entitlements.Changed -= OnEntitlementsChanged;
                entitlements = null;
            }

            base.OnDestroy();
        }

        /// <summary>
        /// The catalogue category each tab shows. A tab with no entry here is not a grid of items —
        /// Agents and You hand their panel over instead — and <see cref="Category.None"/> is the
        /// empty drawer.
        /// </summary>
        /// <remarks>
        /// A lookup rather than a rule: the tab says which category it shows, and the catalogue
        /// says which items are in it. Nothing reads a prefab's name to decide where it goes.
        /// </remarks>
        static string CategoryKeyOf(Category tab)
        {
            switch (tab)
            {
                case Category.Floor: return ItemCategories.Floors;
                case Category.Wall: return ItemCategories.Walls;
                case Category.Tables: return ItemCategories.Tables;
                case Category.Plants: return ItemCategories.Plants;
                case Category.Drawers: return ItemCategories.Drawers;
                case Category.Appliances: return ItemCategories.Appliances;
                case Category.Furnitures: return ItemCategories.Furnitures;
                case Category.Others: return ItemCategories.Others;
                default: return string.Empty;
            }
        }

        void Rebuild()
        {
            if (containerRenderer == null || itemPrefab == null)
            {
                Debug.LogWarning("[InventoryUI] No container or tile prefab assigned; nothing to build.", this);
                return;
            }

            Clear();
            built = true;

            // Neither the Agents tab nor the You tab is a category of furniture, and no manifest
            // entry could ever land in either. Each is handed over whole rather than filtered
            // into: the roster is a different file, a tile is a different thing, and the card
            // below the drawer is the point of the tab. This class stays the one that knows which
            // tab is up.
            if (Selected == Category.Agents)
            {
                DismissYouTab();
                ShowAgentsTab();
                return;
            }

            if (Selected == Category.You)
            {
                DismissAgentsTab();
                ShowYouTab();
                return;
            }

            DismissAgentsTab();
            DismissYouTab();

            // Nothing selected, so there is nothing to fill the grid with. Cleared above and left
            // that way, which is what the drawer opens on.
            if (Selected == Category.None)
            {
                return;
            }

            if (catalog == null)
            {
                Debug.LogWarning("[InventoryUI] No item catalogue; the drawer will be empty. It is "
                    + "injected at startup, so this means content never finished loading.", this);
                return;
            }

            foreach (var item in catalog.InCategory(CategoryKeyOf(Selected)))
            {
                var tile = Build(itemPrefab, containerRenderer);
                tile.name = "Item_" + item.name;
                tile.Bind(
                    item.id,
                    item.name,
                    LoadPrefab(item),
                    LoadIcon(item),
                    item.icon,
                    item.address);

                tiles.Add(tile);
            }
        }

        /// <summary>
        /// Fills the drawer with the agent roster and raises the card below it.
        /// </summary>
        /// <remarks>
        /// The container is handed over rather than the panel going looking for it, so the tab that
        /// knows the grid has just been emptied is the tab that says what may fill it.
        /// </remarks>
        void ShowAgentsTab()
        {
            if (agentsPanel == null)
            {
                Debug.LogWarning("[InventoryUI] The Agents tab has no AgentDisplayUI assigned, so "
                    + "it will be empty. Assign the agentsbox panel on this component.", this);
                return;
            }

            agentsPanel.Populate(containerRenderer);
        }

        /// <summary>
        /// Takes the agents card away, and any area gesture with it.
        /// </summary>
        /// <remarks>
        /// Called on every rebuild rather than only when leaving the Agents tab. A rebuild is the
        /// one thing that happens on every tab change, and asking the panel to dismiss itself when
        /// it is already down costs nothing — where tracking the previous tab to work out whether
        /// this was a departure is a second piece of state to get wrong.
        /// </remarks>
        void DismissAgentsTab()
        {
            if (agentsPanel != null)
            {
                agentsPanel.Dismiss();
            }
        }

        /// <summary>
        /// Raises the manager's card.
        /// </summary>
        /// <remarks>
        /// No container handed over, unlike the agents tab. There is one manager, so there is
        /// nothing to lay out in a strip — the card is the whole tab, and the grid stays empty.
        /// </remarks>
        void ShowYouTab()
        {
            if (youPanel == null)
            {
                Debug.LogWarning("[InventoryUI] The You tab has no ManagerDisplayUI assigned, so it "
                    + "will be empty. Assign the youBox panel on this component.", this);
                return;
            }

            youPanel.Show();
        }

        /// <summary>Takes the manager's card away, and any area gesture with it.</summary>
        void DismissYouTab()
        {
            if (youPanel != null)
            {
                youPanel.Dismiss();
            }
        }

        /// <summary>
        /// Makes one tile, through the container when there is one.
        /// </summary>
        /// <remarks>
        /// <c>resolver.Instantiate</c> rather than <c>Object.Instantiate</c>, which is the whole
        /// difference between a tile that is handed its PlacementController and a tile that goes
        /// looking for one. A tile is built at runtime and so is never in the scene when the
        /// LifetimeScope builds — enumeration cannot reach it, and this can.
        /// <para>
        /// Falls back to a plain Instantiate when nothing injected this panel, so the drawer still
        /// fills in a scene with no scope. The tiles are then uninjected and say so themselves when
        /// somebody clicks one, which beats an empty drawer with no explanation.
        /// </para>
        /// </remarks>
        ItemRenderer3D Build(ItemRenderer3D prefab, Transform parent)
            => resolver != null ? resolver.Instantiate(prefab, parent) : Instantiate(prefab, parent);

        void Clear()
        {
            // Every child goes, not just the tracked tiles: a tile left behind by an earlier run or
            // authored into the scene by hand would otherwise show up under the wrong tab.
            for (var i = containerRenderer.childCount - 1; i >= 0; i--)
            {
                var child = containerRenderer.GetChild(i).gameObject;

                if (Application.isPlaying)
                {
                    // Unparented before being destroyed: Destroy is deferred to the end of the
                    // frame, so without this the grid spends a frame laying out the old tiles and
                    // the new ones together and the switch visibly jumps.
                    child.transform.SetParent(null, false);
                    Destroy(child);
                }
                else
                {
                    DestroyImmediate(child);
                }
            }

            tiles.Clear();
        }

        /// <summary>The object itself, from the address its catalogue entry names.</summary>
        GameObject LoadPrefab(ItemDefinition item)
        {
            GameObject loaded;
            if (content != null && content.TryGet(item.address, out loaded) && loaded != null)
            {
                return loaded;
            }

            // The catalogue only lists items from packs that came down, so a miss here means the
            // pack's manifest and its assets disagree — worth a warning naming both, because the
            // fix is in the pack rather than in the game.
            Debug.LogWarning("[InventoryUI] '" + item.id + "' names address '" + item.address
                + "', which is not resident. Its pack lists an asset it did not ship.", this);

            return null;
        }

        /// <summary>The icon, from the address its catalogue entry names.</summary>
        Sprite LoadIcon(ItemDefinition item)
        {
            if (string.IsNullOrEmpty(item.icon))
            {
                return null;
            }

            // Sprite first: a PNG imported as a sprite yields one, and Image needs a Sprite.
            Sprite sprite;
            if (content != null && content.TryGet(item.icon, out sprite) && sprite != null)
            {
                return sprite;
            }

            // Imported as a plain texture instead — wrap it rather than show nothing, and say so,
            // because the fix is a one-field change on the importer.
            Texture2D texture;
            if (content != null && content.TryGet(item.icon, out texture) && texture != null)
            {
                Debug.LogWarning("[InventoryUI] '" + item.icon + "' is imported as a Texture, not a "
                    + "Sprite. Set its Texture Type to Sprite to avoid this conversion.", this);

                return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f));
            }

            Debug.LogWarning("[InventoryUI] '" + item.id + "' names icon '" + item.icon
                + "', which is not resident.", this);

            return null;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Complains when two tabs are wired to the same button.
        /// </summary>
        /// <remarks>
        /// Worth checking, because the failure is silent and reads as something else entirely.
        /// Every tab adds its own listener in <see cref="Awake"/>, so a button held by two fields
        /// fires both — and the second one wins. The tab lights up as though it were selected while
        /// the grid fills with the other category's contents, which looks like a broken category
        /// rule rather than a mis-assigned reference.
        /// <para>
        /// Easy to do by accident: this drawer grew from four tabs to six, and the field that used
        /// to point at the first tab kept pointing at it after a new tab was inserted in front.
        /// </para>
        /// </remarks>
        void OnValidate()
        {
            var buttons = new[]
            {
                floorButton, wallButton, tableButton, plantsButton, drawersButton,
                appliancesButton, furnituresButton, othersButton, agentsButton, youButton,
            };

            var names = new[]
            {
                "Floor", "Wall", "Table", "Plants", "Drawers",
                "Appliances", "Furnitures", "Others", "Agents", "You",
            };

            for (var i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == null)
                {
                    continue;
                }

                for (var j = i + 1; j < buttons.Length; j++)
                {
                    if (buttons[i] != buttons[j])
                    {
                        continue;
                    }

                    Debug.LogWarning("[InventoryUI] The " + names[i] + " and " + names[j] + " tabs are "
                        + "both wired to '" + buttons[i].name + "'. Clicking it will switch to "
                        + names[j] + ", whichever tab it is labelled as.", this);
                }
            }
        }

#endif
    }
}
