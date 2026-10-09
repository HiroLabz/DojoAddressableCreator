using System.Collections.Generic;
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
    /// room is put down or put back. Once down its pieces are ordinary pieces, so moving or
    /// trimming a room afterwards is the same as for anything else placed.
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
            PlacementController placementController)
        {
            catalog = roomCatalog;
            placer = roomPlacer;
            storeys = floors;
            placement = placementController;
        }

        void OnDestroy() => Unlisten();

        public void Show()
        {
            gameObject.SetActive(true);
            Listen();
            Say(string.Empty);
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
