using System;
using System.Collections.Generic;
using Dojo.Framework.Events;
using Dojo.Game.Events;
using Dojo.Game.InGame.Agents;
using Dojo.Game.InGame.Controllers;
using Dojo.Game.Placement;
using Dojo.Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using VContainer;

namespace Dojo.Game.UI
{
    /// <summary>
    /// The elevator's floor picker: when the manager walks up to an elevator in Play, this lists
    /// the floors it stops at, top first, with what is on each. Pick one and he rides it there.
    /// </summary>
    /// <remarks>
    /// Opens on <see cref="ElevatorReached"/>, which the manager raises when a walk he was sent on
    /// ends against an elevator - clicking one sends him to its doors. A floor it passes without
    /// stopping is not listed. Each row is one of the block areas on the floors it stops at - the
    /// floor on its key, the area's name beside it - and picking one rides him there and walks him
    /// straight on into it, the area his new target. An area on his own floor he walks to without
    /// the elevator; the one he is standing in says so and cannot be picked. A floor with no areas
    /// has a row of its own instead, which rides him to its doors. Rows past what the card can hold
    /// scroll. He waits at the doors while it is up, rather than wandering off after his usual
    /// pause.
    /// <para>
    /// The picked row shows "heading there" for a moment, then the panel goes: the ride itself is
    /// in the world, and the panel would hide it.
    /// </para>
    /// <para>
    /// Follows the holo mock-up (<c>dojo-ui-mockups/addOns/elevator-popup.html</c>, version 5), in
    /// the game's fonts. Its parts are in the Game scene, under the in-game canvas.
    /// </para>
    /// </remarks>
    public sealed class ElevatorPanel : MonoBehaviour
    {
        const float FadeSeconds = 0.15f;

        /// <summary>How long the picked floor shows "heading there" before the panel goes.</summary>
        const float PickedSeconds = 0.35f;

        /// <summary>How long the light sweeping down the panel takes to cross it.</summary>
        const float SweepSeconds = 5f;

        /// <summary>How small the card starts as it opens, so it grows into place.</summary>
        const float OpeningScale = 0.97f;

        static readonly Color Green = new Color32(93, 227, 164, 255);
        static readonly Color Cyan = new Color32(103, 232, 249, 255);

        [Tooltip("Everything the panel shows. Off while it is closed.")]
        [SerializeField] GameObject view;

        [SerializeField] CanvasGroup group;

        [Tooltip("The panel itself, sized to its floors when it opens.")]
        [SerializeField] RectTransform card;

        [Tooltip("The dark sheet behind the panel. Clicking it closes the panel.")]
        [SerializeField] Button scrim;

        [SerializeField] Button close;

        [Tooltip("The big number: the floor the manager is on.")]
        [SerializeField] TMP_Text readout;

        [SerializeField] TMP_Text status;
        [SerializeField] Graphic statusDot;

        [Tooltip("The window the rows scroll in, clipped at its edges.")]
        [SerializeField] RectTransform viewport;

        [Tooltip("Scrolls the rows when there are more than the card can hold.")]
        [SerializeField] ScrollRect scroll;

        [Tooltip("Where the rows go, from the top down: what scrolls.")]
        [SerializeField] RectTransform rows;

        [Tooltip("One row, copied for each floor and each area. Kept switched off.")]
        [SerializeField] ElevatorFloorRow rowTemplate;

        [Tooltip("The band of light that sweeps down the panel.")]
        [SerializeField] RectTransform sweep;

        [Header("Layout")]
        [Tooltip("From the top of the card to the top of the first row.")]
        [SerializeField] float rowsTop = 190f;

        [Tooltip("The row of a floor with no areas, which stands in for them.")]
        [SerializeField] float rowHeight = 95f;

        [Tooltip("An area's row.")]
        [SerializeField] float areaHeight = 80f;

        [SerializeField] float rowGap = 11f;

        [Tooltip("Room above the first row and below the last, inside the scrolling window, so the " +
                 "rows' light is not cut off at its edges.")]
        [SerializeField] float listPadding = 14f;

        [Tooltip("Under the last row, to the bottom of the card.")]
        [SerializeField] float bottomPadding = 22f;

        [Tooltip("The tallest the card may be. Rows past that scroll.")]
        [SerializeField] float maxHeight = 1000f;

        Storeys storeys;
        IEventHub hub;
        IGamePhase phase;
        ManagerAssembler managers;
        ElevatorShafts shafts;
        BlockAreas areas;
        IDisposable reached;

        readonly List<ElevatorFloorRow> made = new List<ElevatorFloorRow>();

        /// <summary>The stop he reached: the shaft being ridden.</summary>
        FurniturePiece stop;

        /// <summary>A floor has been picked, and the panel is on its way out.</summary>
        bool picked;

        bool open;
        int fade = -1;
        int grow = -1;

        [Inject]
        public void Construct(Storeys storeys, IEventHub hub, IGamePhase phase,
            ManagerAssembler managers, ElevatorShafts shafts, BlockAreas areas)
        {
            this.storeys = storeys;
            this.hub = hub;
            this.phase = phase;
            this.managers = managers;
            this.shafts = shafts;
            this.areas = areas;
        }

        /// <summary>Whether the panel is up.</summary>
        public bool IsOpen => open;

        void Awake()
        {
            if (scrim != null) scrim.onClick.AddListener(Close);
            if (close != null) close.onClick.AddListener(Close);

            if (rowTemplate != null)
            {
                rowTemplate.gameObject.SetActive(false);
            }

            if (view != null)
            {
                view.SetActive(false);
            }
        }

        void Start()
        {
            if (hub != null)
            {
                reached = hub.Subscribe<ElevatorReached>(OnElevatorReached);
            }

            if (phase != null)
            {
                phase.Changed += OnPhaseChanged;
            }
        }

        void OnDestroy()
        {
            if (reached != null)
            {
                reached.Dispose();
            }

            if (phase != null)
            {
                phase.Changed -= OnPhaseChanged;
            }

            CancelTweens();
            Hold(null, false);
        }

        void OnElevatorReached(ElevatorReached e)
        {
            if (phase != null && phase.IsEdit)
            {
                return;
            }

            Open(e.Stop);
        }

        /// <summary>Arranging the room is no time to be picking floors: the panel goes at once.</summary>
        void OnPhaseChanged(GamePhase _)
        {
            if (phase != null && phase.IsEdit && open)
            {
                Shut();
            }
        }

        ManagerController Manager => managers != null ? managers.Active : null;

        /// <summary>
        /// Lists the floors the shaft <paramref name="reachedStop"/> belongs to stops at, and shows
        /// the panel.
        /// </summary>
        public void Open(FurniturePiece reachedStop)
        {
            if (reachedStop == null || storeys == null || shafts == null || view == null)
            {
                return;
            }

            stop = reachedStop;
            picked = false;

            var manager = Manager;
            var here = manager != null ? manager.Floor : -1;

            // Only the floors it stops at, ground first: a floor it passes is not on offer.
            var stopsAt = new List<int>();

            for (var floor = 0; floor < storeys.Count; floor++)
            {
                Vector3 door;
                if (shafts.TryDoorOn(stop, floor, out door))
                {
                    stopsAt.Add(floor);
                }
            }

            // Nowhere to go but where he is: nothing to offer.
            if (stopsAt.Count == 0 || (stopsAt.Count == 1 && stopsAt[0] == here))
            {
                Debug.Log("[Floors] " + stop.name + " stops at no other floor, so there is nothing to pick.");
                stop = null;
                return;
            }

            // Up before the rows are filled, though not yet seen: a row's text is laid out from
            // its size, and text that has never been shown measures nothing.
            group.alpha = 0f;
            view.SetActive(true);

            // One row per area, the top floor's first, as a lift's buttons read, and each floor's
            // by name. A floor with no areas has a row of its own instead, so the elevator still
            // goes there.
            var floorAreas = new List<List<BlockArea>>();
            var count = 0;

            for (var i = stopsAt.Count - 1; i >= 0; i--)
            {
                var onIt = AreasOn(stopsAt[i]);
                floorAreas.Add(onIt);
                count += Mathf.Max(1, onIt.Count);
            }

            Rows(count);

            var y = listPadding;
            var next = 0;
            var standing = manager != null ? manager.transform.position : Vector3.zero;

            for (var i = 0; i < floorAreas.Count; i++)
            {
                var floor = stopsAt[stopsAt.Count - 1 - i];
                var face = Storeys.LabelFor(floor);

                if (floorAreas[i].Count == 0)
                {
                    // He is on that floor already, at its doors: nothing to send him to.
                    var row = made[next++];
                    row.SetFloor(floor, face, Storeys.NameFor(floor), Describe(PiecesOn(floor), AgentsOn(floor)),
                        floor == here ? ElevatorFloorRow.Look.Here : ElevatorFloorRow.Look.Normal);
                    row.Interactable = floor != here;
                    y = Place(row, y, rowHeight);
                    continue;
                }

                foreach (var area in floorAreas[i])
                {
                    // The area he is standing in is where he is: nothing to send him to.
                    var inIt = manager != null && floor == here && area.Contains(standing);
                    var areaRow = made[next++];

                    areaRow.SetArea(floor, area, face, inIt ? ElevatorFloorRow.Look.Here : ElevatorFloorRow.Look.Normal);
                    areaRow.Interactable = !inIt;
                    y = Place(areaRow, y, areaHeight);
                }
            }

            if (readout != null)
            {
                readout.text = here >= 0 ? Storeys.LabelFor(here) : "--";
            }

            SetStatus(StopsLine(stopsAt), Green);

            // As tall as its rows, up to the tallest the card may be; past that they scroll.
            var listHeight = y - rowGap + listPadding;
            var top = rowsTop - listPadding;
            var bottom = bottomPadding - listPadding;
            var window = Mathf.Min(listHeight, maxHeight - top - bottom);

            rows.sizeDelta = new Vector2(rows.sizeDelta.x, listHeight);
            rows.anchoredPosition = new Vector2(rows.anchoredPosition.x, 0f);

            viewport.anchoredPosition = new Vector2(viewport.anchoredPosition.x, -top);
            viewport.sizeDelta = new Vector2(viewport.sizeDelta.x, window);

            if (scroll != null)
            {
                scroll.StopMovement();
                scroll.verticalNormalizedPosition = 1f;
            }

            card.sizeDelta = new Vector2(card.sizeDelta.x, top + window + bottom);

            // He waits at the doors while the player chooses, rather than wandering off.
            Hold(manager, true);

            open = true;
            FadeIn();
        }

        /// <summary>The manager waiting at the elevator, so he stays put until the panel goes.</summary>
        ManagerController waiting;

        void Hold(ManagerController manager, bool held)
        {
            if (waiting != null && (!held || waiting != manager))
            {
                waiting.WaitingAtElevator = false;
                waiting = null;
            }

            if (held && manager != null)
            {
                manager.WaitingAtElevator = true;
                waiting = manager;
            }
        }

        /// <summary>Closes the panel. Nothing is sent anywhere.</summary>
        public void Close()
        {
            if (!open)
            {
                return;
            }

            open = false;
            FadeOut();
        }

        /// <summary>As many rows as there are floors and areas, made from the template the first time there are that many.</summary>
        void Rows(int count)
        {
            while (made.Count < count)
            {
                var row = Instantiate(rowTemplate, rows, false);
                row.name = "Row" + made.Count;
                row.Picked += Pick;
                made.Add(row);
            }

            for (var i = 0; i < made.Count; i++)
            {
                made[i].gameObject.SetActive(i < count);
            }
        }

        /// <summary>Puts a row <paramref name="y"/> down the list, this tall. Returns where the next one goes.</summary>
        float Place(ElevatorFloorRow row, float y, float height)
        {
            var rect = (RectTransform)row.transform;

            rect.offsetMin = new Vector2(0f, rect.offsetMin.y);
            rect.offsetMax = new Vector2(0f, rect.offsetMax.y);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -y);

            return y + height + rowGap;
        }


        /// <summary>A floor's areas, by name, for its row.</summary>
        List<BlockArea> AreasOn(int floor)
        {
            var found = new List<BlockArea>();

            if (areas != null)
            {
                found.AddRange(areas.OnFloor(floor));
                found.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            }

            return found;
        }

        /// <summary>
        /// A floor's row: he rides there and steps out at its doors. An area's: he rides there,
        /// steps out, and walks straight on into it.
        /// </summary>
        void Pick(ElevatorFloorRow row) => Send(row, row.Floor, row.Area);

        /// <summary>
        /// Sends the manager to <paramref name="floor"/> by this elevator - to its doors on his
        /// floor, then up or down in it - and on into <paramref name="area"/> when one was picked.
        /// </summary>
        void Send(ElevatorFloorRow pickedRow, int floor, BlockArea area)
        {
            if (!open || picked)
            {
                return;
            }

            var manager = Manager;

            if (manager == null)
            {
                Debug.Log("[Floors] No manager to send to " + Storeys.LabelFor(floor) + ".");
                Close();
                return;
            }

            var from = manager.Floor;

            // On his own floor only an area is somewhere to go: he is at its doors already.
            if (floor == from && area == null)
            {
                return;
            }

            picked = true;

            foreach (var row in made)
            {
                if (row.gameObject.activeSelf)
                {
                    row.Interactable = false;
                }
            }

            pickedRow.SetLook(ElevatorFloorRow.Look.Heading);

            SetStatus("SENDING YOU TO " + (area != null ? area.Name.ToUpperInvariant() + " · " : "")
                + Storeys.LabelFor(floor), Cyan);

            Vector3 board, alight;
            var boards = shafts.TryDoorOn(stop, from, out board);
            var alights = shafts.TryDoorOn(stop, floor, out alight);

            // Into the middle of the area, or - with something standing there - as near it as is free.
            manager.RideTo(floor,
                boards ? board : (Vector3?)null,
                alights ? alight : (Vector3?)null,
                shafts.ArrivalPoints(floor),
                area != null ? area.CentreFirst() : null,
                area != null ? area.CellSize * 0.5f : 0f);

            CloseAfter(PickedSeconds);
        }

        async void CloseAfter(float seconds)
        {
            try
            {
                await Awaitable.WaitForSecondsAsync(seconds, destroyCancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            Close();
        }

        void Update()
        {
            if (!open)
            {
                return;
            }

            var keyboard = Keyboard.current;

            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !picked)
            {
                Close();
                return;
            }

            Sweep();
        }

        /// <summary>
        /// The band of light, from above the top of the panel to below its bottom, over and over.
        /// Its bottom edge is its pivot, so it starts wholly above.
        /// </summary>
        void Sweep()
        {
            if (sweep == null)
            {
                return;
            }

            var t = Mathf.Repeat(Time.unscaledTime, SweepSeconds) / SweepSeconds;
            var travel = card.rect.height + sweep.rect.height;

            sweep.anchoredPosition = new Vector2(sweep.anchoredPosition.x, -t * travel);
        }

        void SetStatus(string words, Color light)
        {
            if (status != null)
            {
                status.text = words;
            }

            if (statusDot != null)
            {
                statusDot.color = light;
            }
        }

        void FadeIn()
        {
            CancelTweens();

            group.alpha = 0f;
            group.interactable = true;
            group.blocksRaycasts = true;
            card.localScale = Vector3.one * OpeningScale;

            fade = LeanTween.alphaCanvas(group, 1f, FadeSeconds)
                .setIgnoreTimeScale(true)
                .setOnComplete(() => fade = -1)
                .id;

            grow = LeanTween.scale(card, Vector3.one, FadeSeconds)
                .setIgnoreTimeScale(true)
                .setEase(LeanTweenType.easeOutQuad)
                .setOnComplete(() => grow = -1)
                .id;
        }

        void FadeOut()
        {
            CancelTweens();

            // Clicks go straight through a panel that is leaving.
            group.interactable = false;
            group.blocksRaycasts = false;

            fade = LeanTween.alphaCanvas(group, 0f, FadeSeconds)
                .setIgnoreTimeScale(true)
                .setOnComplete(() =>
                {
                    fade = -1;
                    Shut();
                })
                .id;
        }

        /// <summary>Gone at once, with no fade.</summary>
        void Shut()
        {
            CancelTweens();
            Hold(null, false);

            open = false;
            stop = null;
            group.alpha = 0f;
            group.blocksRaycasts = false;
            view.SetActive(false);
        }

        void CancelTweens()
        {
            if (fade >= 0) { LeanTween.cancel(fade); fade = -1; }
            if (grow >= 0) { LeanTween.cancel(grow); grow = -1; }
        }

        /// <summary>How many things stand on a floor: everything but its tiles, its people and the elevator.</summary>
        int PiecesOn(int floor)
        {
            var count = 0;

            foreach (var piece in storeys.Floor(floor).GetComponentsInChildren<FurniturePiece>(true))
            {
                if (!piece.IsFloor && !piece.IsPerson && !ElevatorShafts.IsStop(piece))
                {
                    count++;
                }
            }

            return count;
        }

        int AgentsOn(int floor) => storeys.Floor(floor).GetComponentsInChildren<PlacedAgent>(true).Length;

        /// <summary>
        /// What is on a floor, in the row's small line: "34 PIECES · 2 AGENTS", leaving out what
        /// there is none of, or "EMPTY".
        /// </summary>
        public static string Describe(int pieces, int agents)
        {
            var parts = new List<string>(2);

            if (pieces > 0)
            {
                parts.Add(pieces + (pieces == 1 ? " PIECE" : " PIECES"));
            }

            if (agents > 0)
            {
                parts.Add(agents + (agents == 1 ? " AGENT" : " AGENTS"));
            }

            return parts.Count > 0 ? string.Join(" · ", parts) : "EMPTY";
        }

        /// <summary>The status line: which floors this elevator stops at, ground first.</summary>
        public static string StopsLine(IList<int> floors)
        {
            if (floors == null || floors.Count == 0)
            {
                return "NO STOPS";
            }

            var labels = new string[floors.Count];

            for (var i = 0; i < floors.Count; i++)
            {
                labels[i] = Storeys.LabelFor(floors[i]);
            }

            return "STOPS ON " + string.Join(" · ", labels);
        }
    }
}
