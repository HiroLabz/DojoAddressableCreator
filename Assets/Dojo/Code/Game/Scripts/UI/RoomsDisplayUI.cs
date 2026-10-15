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
    /// The Rooms tab: every room the loaded packs offer, as cards like the floors and walls.
    /// </summary>
    /// <remarks>
    /// Clicking a card puts the room on the cursor the way a floor tile or a wall is - see
    /// <see cref="PlacementController.BeginRoomFromInventory"/> - and the card stays lit until the
    /// room is put down or put back. Once down it moves as one: a press on any of its pieces lifts
    /// the whole room. EDIT on a card opens that room in the room editor, to be saved over itself.
    /// </remarks>
    public sealed class RoomsDisplayUI : MonoBehaviour
    {
        [Header("Header")]
        [Tooltip("Which floor a room would go on, and how many rooms there are.")]
        [SerializeField] TMP_Text subtitleLabel;

        [Tooltip("Why the last card could not be picked up, when it could not.")]
        [SerializeField] TMP_Text statusLabel;

        [Header("Grid")]
        [SerializeField] Transform listContent;
        [SerializeField] RoomCard cardPrefab;

        [Tooltip("Shown when no loaded pack offers any rooms.")]
        [SerializeField] GameObject emptyState;

        [Header("Making rooms")]
        [Tooltip("Above the cards: leaves the world for a blank floor to build a room of your own on.")]
        [SerializeField] Button createRoomButton;

        RoomCatalog catalog;
        RoomPlacer placer;
        Storeys storeys;
        PlacementController placement;

        readonly List<RoomCard> cards = new List<RoomCard>();

        // Read and measured once per room: the size on its card, and what is carried when it is
        // picked up.
        readonly Dictionary<string, RoomPlacer.Plan> plans = new Dictionary<string, RoomPlacer.Plan>();

        bool listening;

        [Inject]
        public void Construct(RoomCatalog roomCatalog, RoomPlacer roomPlacer, Storeys floors,
            PlacementController placementController, RoomEditor roomEditor, IDialogManager dialogManager)
        {
            catalog = roomCatalog;
            placer = roomPlacer;
            storeys = floors;
            placement = placementController;
            editor = roomEditor;
            dialogs = dialogManager;

            editor.Changed += OnEditorChanged;
            editor.Saved += OnRoomSaved;

            if (catalog != null && catalog.Custom != null)
            {
                catalog.Custom.Changed += OnRoomsChanged;
            }
        }

        RoomEditor editor;
        IDialogManager dialogs;

        /// <summary>
        /// Deletes a room the player made, after asking: its file and area, its thumbnail, and
        /// their metas. The pack's rooms have no DELETE, and the store refuses them regardless.
        /// </summary>
        /// <summary>
        /// Makes a room the player made one of the pack's, after asking: renamed to the pack's
        /// naming, added to the end of rooms.json, and both files imported again. From then on it is
        /// listed once, as a pack room, with neither DELETE nor PROMOTE.
        /// </summary>
        void OnPromote(RoomEntry room)
        {
            if (room == null || catalog == null || !CustomRooms.IsCustom(room.address))
            {
                return;
            }

            if (placement != null && placement.IsBusy)
            {
                Say("Put down what is in hand first.");
                return;
            }

            void Promote()
            {
                string problem;

                if (!catalog.Custom.Promote(room.address, out problem))
                {
                    Say(problem);
                    return;
                }

                Say("'" + room.name + "' is one of the pack's rooms now.");
            }

            if (dialogs == null)
            {
                Promote();
                return;
            }

            dialogs.Confirm("Promote '" + room.name + "' to the pack? It becomes one of the default rooms: it ships "
                + "with the pack, is kept by a reset that keeps promoted rooms, and can no longer be deleted here.", Promote);
        }

        /// <summary>
        /// Opens a room in the room editor, to be changed and saved over itself. Places it was put
        /// down already keep the copy they have.
        /// </summary>
        void OnEdit(RoomEntry room)
        {
            if (room == null || editor == null || editor.IsEditing)
            {
                return;
            }

            if (placement != null && placement.IsBusy)
            {
                Say("Put down what is in hand first.");
                return;
            }

            if (!CustomRooms.IsCustom(room.address) && !Application.isEditor)
            {
                Say("'" + room.name + "' is one of the pack's rooms, and can only be changed in the editor.");
                return;
            }

            string problem;

            if (!editor.Edit(room, out problem))
            {
                Say(problem);
            }
        }

        void OnDelete(RoomEntry room)
        {
            if (room == null || catalog == null)
            {
                return;
            }

            if (!CustomRooms.IsCustom(room.address))
            {
                Say("'" + room.name + "' is one of the pack's rooms, and cannot be deleted.");
                return;
            }

            if (placement != null && placement.IsBusy)
            {
                Say("Put down what is in hand first.");
                return;
            }

            void Delete()
            {
                string problem;

                if (!catalog.Custom.Delete(room.address, out problem))
                {
                    Say(problem);
                    return;
                }

                plans.Remove(room.address);
                Rebuild();
                Say("'" + room.name + "' deleted.");
                Debug.Log("[Rooms] Deleted room '" + room.name + "' (" + room.address + "): its file, area, thumbnail and metas.");
            }

            if (dialogs == null)
            {
                Delete();
                return;
            }

            dialogs.Confirm("Delete '" + room.name + "'? Its file, area and thumbnail are removed for good. "
                + "Worlds it was placed in keep their copy.", Delete);
        }

        void Awake()
        {
            if (createRoomButton != null)
            {
                createRoomButton.onClick.AddListener(OnCreateRoom);
            }
        }

        void OnDestroy()
        {
            Unlisten();

            if (editor != null)
            {
                editor.Changed -= OnEditorChanged;
                editor.Saved -= OnRoomSaved;
            }

            if (catalog != null && catalog.Custom != null)
            {
                catalog.Custom.Changed -= OnRoomsChanged;
            }
        }

        void OnCreateRoom()
        {
            if (editor == null || editor.IsEditing)
            {
                return;
            }

            if (placement != null && placement.IsBusy)
            {
                Say("Put down what is in hand first.");
                return;
            }

            if (!editor.Begin())
            {
                Say("The room editor is not set up in this scene. Run Tools > Dojo > Add Rooms Tab and save the scene.");
            }
        }

        /// <summary>No CREATE ROOM while one is being made: there is nowhere to go from there.</summary>
        void OnEditorChanged()
        {
            if (createRoomButton != null)
            {
                createRoomButton.gameObject.SetActive(editor == null || !editor.IsEditing);
            }
        }

        /// <summary>The room just made, at the bottom of the list with its thumbnail.</summary>
        /// <summary>
        /// Lists the rooms again whenever they change - a room saved or deleted, or every saved
        /// room deleted by RESET WORLD - so no card is left for a room that is gone. Showing or not:
        /// a panel down now is rebuilt when it is next shown anyway, but its sizes are not kept.
        /// </summary>
        void OnRoomsChanged()
        {
            plans.Clear();

            if (gameObject.activeInHierarchy)
            {
                Rebuild();
                stale = false;
            }
            else
            {
                // Hidden with the inventory - RESET WORLD runs from the menu - and reopening the
                // inventory on this tab shows it as it was, without Show. Rebuilt on the way back.
                stale = true;
            }
        }

        // True when the rooms changed while the panel was hidden, so its cards are out of date.
        bool stale;

        void OnEnable()
        {
            if (stale && catalog != null)
            {
                stale = false;
                Rebuild();
            }
        }

        void OnRoomSaved(RoomEntry room)
        {
            // A room saved over one of the same name is a different room now.
            plans.Remove(room.address);

            // Saved over itself, it keeps its place; a new one goes last.
            var listed = cards.Exists(card => card != null && card.Room != null && card.Room.address == room.address);

            if (gameObject.activeInHierarchy)
            {
                Rebuild();
                Say("'" + room.name + "' saved." + (listed ? "" : " It is at the bottom of the list."));
            }
        }

        public void Show()
        {
            // Rebuilt below regardless, so not once more on the way in.
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
        /// Lights the card whose room is on the cursor, and while it is red, says what is in the way.
        /// </summary>
        void Update()
        {
            var inHand = placement != null ? placement.RoomInHand : null;

            foreach (var card in cards)
            {
                if (card != null)
                {
                    card.SetSelected(inHand != null && card.Room == inHand);
                }
            }

            if (inHand != null)
            {
                var blocker = placement.RoomBlocker;
                Say(string.IsNullOrEmpty(blocker) ? string.Empty : "Blocked by " + blocker + ".");
                carrying = true;
            }
            else if (carrying)
            {
                // Put down or put back: the last "blocked" line no longer means anything.
                Say(string.Empty);
                carrying = false;
            }
        }

        bool carrying;

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

            var rooms = catalog != null ? catalog.All() : new List<RoomEntry>();

            if (cardPrefab != null && listContent != null)
            {
                foreach (var room in rooms)
                {
                    var plan = PlanFor(room);
                    var card = Instantiate(cardPrefab, listContent);
                    card.Bind(room, catalog.IconOf(room), plan != null ? plan.Footprint.size : Vector2.zero);
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
                emptyState.SetActive(rooms.Count == 0);
            }

            RefreshSubtitle();
        }

        RoomPlacer.Plan PlanFor(RoomEntry room)
        {
            if (room == null || placer == null)
            {
                return null;
            }

            RoomPlacer.Plan plan;

            if (!plans.TryGetValue(room.address, out plan))
            {
                string problem;
                plan = placer.Prepare(room, out problem);

                if (plan == null)
                {
                    Debug.LogWarning("[Rooms] '" + room.name + "' cannot be placed: " + problem, this);
                }

                plans[room.address] = plan;
            }

            return plan;
        }

        void RefreshSubtitle()
        {
            if (subtitleLabel == null)
            {
                return;
            }

            var floor = storeys != null ? storeys.Viewed : 0;
            subtitleLabel.text = Storeys.LabelFor(floor) + " · " + cards.Count + (cards.Count == 1 ? " ROOM" : " ROOMS");
        }

        void OnPicked(RoomEntry room)
        {
            if (placement == null || room == null)
            {
                return;
            }

            var plan = PlanFor(room);

            if (plan == null)
            {
                Say("That room's file could not be read.");
                return;
            }

            if (placement.BeginRoomFromInventory(placer, plan))
            {
                Say(string.Empty);
            }
            else if (placement.IsBusy)
            {
                Say("Put down what is in hand first.");
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
