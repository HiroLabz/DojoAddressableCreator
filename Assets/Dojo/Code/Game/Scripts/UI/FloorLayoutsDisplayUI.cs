using System.Collections.Generic;
using Dojo.Framework.UI;
using Dojo.Game.Placement;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace Dojo.Game.UI
{
    /// <summary>
    /// The FLOOR PLANS side of the FLOOR tab: every floor plan the loaded packs offer and the ones
    /// made here, as cards like the rooms. <see cref="RoomsDisplayUI"/> for floors (ClickUp
    /// 14ze67xgf0v). "Floor plan" is what the designer reads; the code keeps "layout".
    /// </summary>
    /// <remarks>
    /// Clicking a card puts the layout on the cursor the way a room is - see
    /// <see cref="PlacementController.BeginLayoutFromInventory"/> - and the card stays lit until it
    /// is laid or put back; the status line counts the pieces that would be skipped where spots are
    /// taken. + CREATE LAYOUT leaves the world for a blank floor with the frame on it; EDIT on a
    /// card opens that layout in the floor editor, to be saved over itself; DELETE and PROMOTE are
    /// the store's.
    /// </remarks>
    public sealed class FloorLayoutsDisplayUI : MonoBehaviour
    {
        [Header("Header")]
        [Tooltip("Which floor a layout would go on, and how many layouts there are.")]
        [SerializeField] TMP_Text subtitleLabel;

        [Tooltip("What the last press could not do, when it could not.")]
        [SerializeField] TMP_Text statusLabel;

        [Header("Grid")]
        [SerializeField] Transform listContent;
        [SerializeField] FloorLayoutCard cardPrefab;

        [Tooltip("Shown when there are no layouts at all.")]
        [SerializeField] GameObject emptyState;

        [Header("Making layouts")]
        [Tooltip("Above the cards: leaves the world for a blank floor to lay a layout out on.")]
        [SerializeField] Button createButton;

        FloorLayoutCatalog catalog;
        FloorLayoutPlacer placer;
        Storeys storeys;
        PlacementController placement;
        FloorLayoutEditor editor;
        RoomEditor rooms;
        IDialogManager dialogs;
        Dojo.Game.Managers.PopupPrefabs popups;

        readonly List<FloorLayoutCard> cards = new List<FloorLayoutCard>();

        // Read and measured once per layout: the size on its card, and what is carried when it is
        // picked up.
        readonly Dictionary<string, Vector2Int> sizes = new Dictionary<string, Vector2Int>();
        readonly Dictionary<string, FloorLayoutPlacer.Plan> plans = new Dictionary<string, FloorLayoutPlacer.Plan>();

        bool listening;

        [Inject]
        public void Construct(FloorLayoutCatalog floorCatalog, FloorLayoutPlacer layoutPlacer, Storeys floors,
            PlacementController placementController, FloorLayoutEditor floorEditor, RoomEditor roomEditor,
            IDialogManager dialogManager, Dojo.Game.Managers.PopupPrefabs popupPrefabs)
        {
            catalog = floorCatalog;
            placer = layoutPlacer;
            storeys = floors;
            placement = placementController;
            editor = floorEditor;
            rooms = roomEditor;
            dialogs = dialogManager;
            popups = popupPrefabs;

            editor.Changed += OnEditorChanged;
            editor.Saved += OnLayoutSaved;

            if (catalog != null && catalog.Custom != null)
            {
                catalog.Custom.Changed += OnLayoutsChanged;
            }
        }

        void Awake()
        {
            if (createButton != null)
            {
                createButton.onClick.AddListener(OnCreate);
            }
        }

        void OnDestroy()
        {
            Unlisten();

            if (editor != null)
            {
                editor.Changed -= OnEditorChanged;
                editor.Saved -= OnLayoutSaved;
            }

            if (catalog != null && catalog.Custom != null)
            {
                catalog.Custom.Changed -= OnLayoutsChanged;
            }
        }

        /// <summary>True, saying why, when nothing can be started now: something in hand, or an editor open.</summary>
        bool Busy()
        {
            if (placement != null && placement.IsBusy)
            {
                Say("Put down what is in hand first.");
                return true;
            }

            if (rooms != null && rooms.IsEditing)
            {
                Say("Finish the room first: save it or cancel it from the room bar.");
                return true;
            }

            return false;
        }

        void OnCreate()
        {
            if (editor == null || editor.IsEditing || Busy())
            {
                return;
            }

            if (!editor.Begin())
            {
                Say("The floor editor is not set up in this scene. Run Tools > Dojo > Build Floor Editor and save the scene.");
            }
        }

        /// <summary>Opens a layout in the floor editor, to be changed and saved over itself.</summary>
        void OnEdit(FloorLayoutEntry layout)
        {
            if (layout == null || editor == null || editor.IsEditing || Busy())
            {
                return;
            }

            if (!CustomFloorLayouts.IsCustom(layout.address) && !Application.isEditor)
            {
                Say("'" + layout.name + "' is one of the pack's floor plans, and can only be changed in the editor.");
                return;
            }

            string problem;

            if (!editor.Edit(layout, out problem))
            {
                Say(problem);
            }
        }

        void OnDelete(FloorLayoutEntry layout)
        {
            if (layout == null || catalog == null)
            {
                return;
            }

            if (!CustomFloorLayouts.IsCustom(layout.address))
            {
                Say("'" + layout.name + "' is one of the pack's floor plans, and cannot be deleted.");
                return;
            }

            if (Busy())
            {
                return;
            }

            void Delete()
            {
                string problem;

                if (!catalog.Custom.Delete(layout.address, out problem))
                {
                    Say(problem);
                    return;
                }

                sizes.Remove(layout.address);
                plans.Remove(layout.address);
                Rebuild();
                Say("'" + layout.name + "' deleted.");
                Debug.Log("[Floors] Deleted floor layout '" + layout.name + "' (" + layout.address + "): its file, thumbnail and metas.");
            }

            if (dialogs == null)
            {
                Delete();
                return;
            }

            PopupAsk.Confirm(popups, dialogs, "Delete '" + layout.name + "'?", "Its file and thumbnail are removed for good. "
                + "Floors it was laid on keep what it laid.", "DELETE", MessagePopup.Tone.Alert, Delete);
        }

        void OnPromote(FloorLayoutEntry layout)
        {
            if (layout == null || catalog == null || !CustomFloorLayouts.IsCustom(layout.address) || Busy())
            {
                return;
            }

            void Promote()
            {
                string problem;

                if (!catalog.Custom.Promote(layout.address, out problem))
                {
                    Say(problem);
                    return;
                }

                Say("'" + layout.name + "' is one of the pack's floor plans now.");
            }

            if (dialogs == null)
            {
                Promote();
                return;
            }

            PopupAsk.Confirm(popups, dialogs, "Promote '" + layout.name + "'?", "It becomes one of the pack's floor plans: it ships "
                + "with the content and can no longer be deleted here.", "PROMOTE", MessagePopup.Tone.Info, Promote);
        }

        /// <summary>No + CREATE LAYOUT while a layout is being made: there is nowhere to go from there.</summary>
        void OnEditorChanged()
        {
            if (createButton != null)
            {
                createButton.gameObject.SetActive(editor == null || !editor.IsEditing);
            }
        }

        /// <summary>
        /// Lists the layouts again whenever they change - one saved, deleted or promoted - so no
        /// card is left for a layout that is gone. Hidden, it is rebuilt when next shown.
        /// </summary>
        void OnLayoutsChanged()
        {
            sizes.Clear();
            plans.Clear();

            if (gameObject.activeInHierarchy)
            {
                Rebuild();
                stale = false;
            }
            else
            {
                stale = true;
            }
        }

        // True when the layouts changed while the panel was hidden, so its cards are out of date.
        bool stale;

        void OnEnable()
        {
            if (stale && catalog != null)
            {
                stale = false;
                Rebuild();
            }
        }

        void OnLayoutSaved(FloorLayoutEntry layout)
        {
            sizes.Remove(layout.address);
            plans.Remove(layout.address);

            var listed = cards.Exists(card => card != null && card.Layout != null && card.Layout.address == layout.address);

            if (gameObject.activeInHierarchy)
            {
                Rebuild();
                Say("'" + layout.name + "' saved." + (listed ? "" : " It is at the bottom of the list."));
            }
        }

        public void Show()
        {
            stale = false;
            gameObject.SetActive(true);
            Listen();
            Say(string.Empty);
            OnEditorChanged();
            Rebuild();
        }

        public void Dismiss()
        {
            Unlisten();
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Lights the card whose layout is on the cursor, and while it is there, says how many of
        /// its pieces would be skipped where spots are taken.
        /// </summary>
        void Update()
        {
            var inHand = placement != null ? placement.LayoutInHand : null;

            foreach (var card in cards)
            {
                if (card != null)
                {
                    card.SetSelected(inHand != null && card.Layout != null && card.Layout.address == inHand.address);
                }
            }

            if (inHand != null)
            {
                var clashes = placement.LayoutClashes;
                Say(clashes == 0
                    ? string.Empty
                    : clashes + (clashes == 1 ? " piece is" : " pieces are") + " skipped here: their spots are taken. What is on the floor stays.");
                carrying = true;
            }
            else if (carrying)
            {
                Say(string.Empty);
                carrying = false;
            }
        }

        bool carrying;

        FloorLayoutPlacer.Plan PlanFor(FloorLayoutEntry layout)
        {
            if (layout == null || placer == null)
            {
                return null;
            }

            FloorLayoutPlacer.Plan plan;

            if (!plans.TryGetValue(layout.address, out plan))
            {
                string problem;
                plan = placer.Prepare(layout, out problem);

                if (plan == null)
                {
                    Debug.LogWarning("[Floors] '" + layout.name + "' cannot be laid: " + problem, this);
                }

                plans[layout.address] = plan;
            }

            return plan;
        }

        void Listen()
        {
            if (!listening && storeys != null)
            {
                storeys.Changed += RefreshSubtitle;
                listening = true;
            }
        }

        void Unlisten()
        {
            if (listening && storeys != null)
            {
                storeys.Changed -= RefreshSubtitle;
            }

            listening = false;
        }

        void Rebuild()
        {
            foreach (var card in cards)
            {
                if (card != null)
                {
                    Destroy(card.gameObject);
                }
            }

            cards.Clear();

            var layouts = catalog != null ? catalog.All() : new List<FloorLayoutEntry>();

            if (cardPrefab != null && listContent != null)
            {
                foreach (var layout in layouts)
                {
                    var card = Instantiate(cardPrefab, listContent);
                    card.Bind(layout, catalog.IconOf(layout), SizeOf(layout));
                    card.Picked += OnPicked;
                    card.DeletePressed += OnDelete;
                    card.PromotePressed += OnPromote;
                    card.EditPressed += OnEdit;
                    cards.Add(card);
                }

                var content = listContent as RectTransform;

                if (content != null)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(content);
                }
            }

            if (emptyState != null)
            {
                emptyState.SetActive(layouts.Count == 0);
            }

            RefreshSubtitle();
        }

        /// <summary>The frame that fits the layout, in tiles, from its file; zero when it cannot be read.</summary>
        Vector2Int SizeOf(FloorLayoutEntry layout)
        {
            if (layout == null || string.IsNullOrEmpty(layout.address))
            {
                return Vector2Int.zero;
            }

            Vector2Int size;

            if (!sizes.TryGetValue(layout.address, out size))
            {
                var world = catalog.Read(layout);

                if (world == null)
                {
                    Debug.LogWarning("[Floors] '" + layout.name + "' could not be read for its size.", this);
                    size = Vector2Int.zero;
                }
                else
                {
                    int columns, rows;
                    FloorLayoutEditor.FitSize(world, out columns, out rows);
                    size = new Vector2Int(columns, rows);
                }

                sizes[layout.address] = size;
            }

            return size;
        }

        void RefreshSubtitle()
        {
            if (subtitleLabel == null)
            {
                return;
            }

            var floor = storeys != null ? storeys.Viewed : 0;
            subtitleLabel.text = Storeys.LabelFor(floor) + " · " + cards.Count + (cards.Count == 1 ? " FLOOR PLAN" : " FLOOR PLANS");
        }

        void OnPicked(FloorLayoutEntry layout)
        {
            if (placement == null || layout == null)
            {
                return;
            }

            if ((editor != null && editor.IsEditing) || (rooms != null && rooms.IsEditing))
            {
                Say("Save or cancel the editor first.");
                return;
            }

            var plan = PlanFor(layout);

            if (plan == null)
            {
                Say("That floor plan's file could not be read.");
                return;
            }

            if (placement.BeginLayoutFromInventory(placer, plan))
            {
                Say(string.Empty);
            }
            else if (placement.IsBusy)
            {
                Say("Put down what is in hand first.");
            }
            else
            {
                Say("Floor plans are laid from the Top or Perspective view.");
            }
        }

        void Say(string text)
        {
            if (statusLabel == null)
            {
                return;
            }

            statusLabel.text = text;
            statusLabel.gameObject.SetActive(!string.IsNullOrEmpty(text));
        }
    }
}
