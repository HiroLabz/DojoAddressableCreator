using System.Collections.Generic;
using Dojo.Framework.UI;
using Dojo.Framework.World;
using Dojo.Game.InGame.Agents;
using Dojo.Game.Managers;
using Dojo.Game.Placement;
using Dojo.Game.Server;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace Dojo.Game.UI
{
    /// <summary>
    /// The inventory's Areas tab: every block area on the floor being viewed, and the one place an
    /// area is painted, named, painted again, renamed or deleted.
    /// </summary>
    /// <remarks>
    /// <b>+ New area</b> hands the gesture to <see cref="AreaPainter"/> - the brush, which knows
    /// nothing about what it paints for - and waits for <c>Committed</c> or <c>Cancelled</c>. A new
    /// area that was painted is then named in a popup; cancelling the name throws the painting away,
    /// since an area nobody named is not one the player meant to keep.
    /// <para>
    /// Who uses an area is decided on the agent and You cards, not here. This only says who does.
    /// </para>
    /// </remarks>
    public sealed class AreasDisplayUI : MonoBehaviour
    {
        [Header("Header")]
        [Tooltip("Which floor, and how many areas are on it.")]
        [SerializeField] TMP_Text subtitleLabel;

        [Header("Painting")]
        [SerializeField] Button newAreaButton;
        [SerializeField] TMP_Text newAreaLabel;

        [Tooltip("How to paint, shown while the brush is up.")]
        [SerializeField] TMP_Text hintLabel;

        [Header("List")]
        [SerializeField] Transform listContent;
        [SerializeField] AreaRow rowPrefab;

        [Tooltip("Shown when the floor has no areas yet.")]
        [SerializeField] GameObject emptyState;

        // The scroll view around the list, found from the list itself.
        ScrollRect listScroll;

        BlockAreas areas;
        AreaPainter painter;
        Storeys storeys;
        PlacementGrid grid;
        PopupPrefabs popups;
        IAgentRegistry registry;
        ManagerRosterFile managerRoster;

        readonly List<AreaRow> rows = new List<AreaRow>();

        // What the brush is up for: a new area, or painting one again. The painter does not know.
        bool painting;
        int repaintingId;

        bool listening;

        [Inject]
        public void Construct(BlockAreas blockAreas, AreaPainter areaPainter, Storeys floors,
            PlacementGrid placementGrid, PopupPrefabs popupPrefabs, IAgentRegistry agentRegistry,
            ManagerRosterFile managerFile)
        {
            areas = blockAreas;
            painter = areaPainter;
            storeys = floors;
            grid = placementGrid;
            popups = popupPrefabs;
            registry = agentRegistry;
            managerRoster = managerFile;
        }

        void Awake()
        {
            if (newAreaButton != null)
            {
                newAreaButton.onClick.AddListener(OnNewArea);
            }
        }

        void OnDestroy() => Unlisten();

        /// <summary>Raises the panel and lists the areas of the floor being viewed.</summary>
        public void Show()
        {
            gameObject.SetActive(true);
            Listen();
            Rebuild();
        }

        /// <summary>Hides the panel, dropping a painting under way: nothing would say what it was for.</summary>
        public void Dismiss()
        {
            if (painting && painter != null && painter.IsActive)
            {
                painter.Cancel();
            }

            painting = false;
            Unlisten();
            gameObject.SetActive(false);
        }

        void Listen()
        {
            if (listening)
            {
                return;
            }

            if (areas != null) areas.Changed += Rebuild;
            if (storeys != null) storeys.Changed += Rebuild;

            if (painter != null)
            {
                painter.Committed += OnCommitted;
                painter.Cancelled += OnCancelled;
            }

            listening = true;
        }

        void Unlisten()
        {
            if (!listening)
            {
                return;
            }

            if (areas != null) areas.Changed -= Rebuild;
            if (storeys != null) storeys.Changed -= Rebuild;

            if (painter != null)
            {
                painter.Committed -= OnCommitted;
                painter.Cancelled -= OnCancelled;
            }

            listening = false;
        }

        // ── The list ────────────────────────────────────────────────────────────────────────

        void Rebuild()
        {
            var floor = storeys != null ? storeys.Viewed : 0;
            var onFloor = new List<BlockArea>();

            if (areas != null)
            {
                onFloor.AddRange(areas.OnFloor(floor));
            }

            if (subtitleLabel != null)
            {
                subtitleLabel.text = Storeys.LabelFor(floor) + " · " + onFloor.Count
                    + (onFloor.Count == 1 ? " AREA" : " AREAS");
            }

            foreach (var row in rows)
            {
                if (row != null)
                {
                    row.transform.SetParent(null, false);
                    Destroy(row.gameObject);
                }
            }

            rows.Clear();

            if (listContent != null && rowPrefab != null)
            {
                foreach (var area in onFloor)
                {
                    var row = Instantiate(rowPrefab, listContent);
                    row.name = "Area_" + area.Name;
                    row.Bind(area.Id, area.Name, HoldersOf(area));
                    row.RepaintPressed += OnRepaint;
                    row.RenamePressed += OnRename;
                    row.DeletePressed += OnDelete;
                    row.SetInteractable(!painting);
                    rows.Add(row);
                }
            }

            if (emptyState != null)
            {
                emptyState.SetActive(onFloor.Count == 0);
            }

            RefreshScrolling();
            RefreshPainting();
        }

        /// <summary>
        /// Scrolls only when the rows are taller than the list. While they fit, the list stays put at
        /// the top and its scrollbar is hidden (the scroll view hides it and gives the rows its room).
        /// </summary>
        void RefreshScrolling()
        {
            if (listScroll == null && listContent != null)
            {
                listScroll = listContent.GetComponentInParent<ScrollRect>(true);
            }

            var content = listContent as RectTransform;

            if (listScroll == null || content == null || listScroll.viewport == null)
            {
                return;
            }

            // The rows were added this frame; lay them out now so their height can be read.
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);

            var fits = content.rect.height <= listScroll.viewport.rect.height + 0.5f;
            listScroll.vertical = !fits;

            if (fits)
            {
                listScroll.verticalNormalizedPosition = 1f;
            }
        }

        /// <summary>Who uses an area, in words.</summary>
        string HoldersOf(BlockArea area)
        {
            var names = new List<string>();

            foreach (var holder in areas.Holders(area.Id))
            {
                if (holder.Role == SnapshotRole.Manager)
                {
                    var all = managerRoster != null ? managerRoster.Read() : null;
                    var boss = all != null ? all.Find(holder.CredentialId) : null;
                    names.Insert(0, (boss != null ? boss.name : "The manager") + " (you)");
                }
                else
                {
                    var agent = registry != null ? registry.Find(holder.CredentialId) : null;
                    names.Add(agent != null ? agent.name : "Agent " + holder.CredentialId);
                }
            }

            var detail = names.Count == 0 ? "NOT ASSIGNED" : string.Join(", ", names).ToUpperInvariant();
            return detail + " · " + area.Cells.Count + " CELLS";
        }

        void RefreshPainting()
        {
            if (newAreaLabel != null)
            {
                newAreaLabel.text = painting ? "RIGHT-CLICK TO FINISH" : "+ NEW AREA";
            }

            if (hintLabel != null)
            {
                hintLabel.gameObject.SetActive(painting);
            }

            foreach (var row in rows)
            {
                if (row != null)
                {
                    row.SetInteractable(!painting);
                }
            }
        }

        // ── Painting ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Starts painting a new area on the floor being viewed, or - pressed again while painting
        /// - stops without keeping anything.
        /// </summary>
        void OnNewArea()
        {
            if (painter == null)
            {
                Debug.LogWarning("[Areas] There is no area brush in this scene.", this);
                return;
            }

            if (painting)
            {
                painter.Cancel();
                return;
            }

            if (!painter.Begin(new List<Vector3>()))
            {
                return;   // the painter has said why
            }

            painting = true;
            repaintingId = 0;
            RefreshPainting();
        }

        void OnRepaint(int areaId)
        {
            var area = areas != null ? areas.Find(areaId) : null;

            if (area == null || painter == null || painting)
            {
                return;
            }

            if (!painter.Begin(new List<Vector3>(area.Cells)))
            {
                return;
            }

            painting = true;
            repaintingId = areaId;
            RefreshPainting();
        }

        void OnCommitted(List<Vector3> cells)
        {
            if (!painting)
            {
                return;   // the brush was up for somebody else
            }

            painting = false;
            var repainted = repaintingId;
            repaintingId = 0;
            RefreshPainting();

            if (cells == null || cells.Count == 0)
            {
                // Nothing painted. A new area of nothing is simply not made; an area painted down
                // to nothing keeps what it had - deleting is its own button, and asks first.
                Debug.Log("[Areas] Nothing was painted, so nothing changed.", this);
                return;
            }

            if (repainted != 0)
            {
                areas.Repaint(repainted, cells);
                return;
            }

            var floor = storeys != null ? storeys.Viewed : 0;
            var size = grid != null ? grid.CellSize : 0.25f;

            AskName("Name this area",
                "What should this area be called? It's how agents and the elevator will know it.",
                SuggestedName(),
                name => areas.Add(name, floor, cells, size));
        }

        void OnCancelled()
        {
            painting = false;
            repaintingId = 0;
            RefreshPainting();
        }

        // ── Renaming and deleting ───────────────────────────────────────────────────────────

        void OnRename(int areaId)
        {
            var area = areas != null ? areas.Find(areaId) : null;

            if (area == null)
            {
                return;
            }

            AskName("Rename '" + area.Name + "'", "What should this area be called now?", area.Name,
                name => areas.Rename(areaId, name));
        }

        void OnDelete(int areaId)
        {
            var area = areas != null ? areas.Find(areaId) : null;

            if (area == null)
            {
                return;
            }

            var used = new List<BlockAreas.Holder>(areas.Holders(areaId)).Count;
            var body = used == 0
                ? "The area is removed from this floor."
                : "The area is removed from this floor, and " + (used == 1 ? "the 1 agent using it is" : "the " + used + " using it are")
                    + " left with none.";

            if (popups == null || popups.Confirmation == null)
            {
                areas.Delete(areaId);
                return;
            }

            var popup = PopupManager.Instance.Show(popups.Confirmation)
                .Tint(MessagePopup.Tone.Alert)
                .Present("Delete '" + area.Name + "'?", body)
                .Caption(MessagePopup.Choice.Cancel, "CANCEL")
                .Caption(MessagePopup.Choice.Primary, "DELETE");

            popup.Chosen += choice =>
            {
                if (choice == MessagePopup.Choice.Primary)
                {
                    areas.Delete(areaId);
                }
            };
        }

        /// <summary>
        /// Asks for a name in the prompt popup, and hands it over if one is given. Nothing happens
        /// when it is cancelled.
        /// </summary>
        void AskName(string heading, string question, string suggestion, System.Action<string> named)
        {
            if (popups == null || popups.Prompt == null)
            {
                // No prompt prefab yet: named for the player rather than lost. Logged, because a
                // scene without it is one the popup builder has not been run for.
                Debug.LogWarning("[Areas] There is no prompt popup to ask for a name with; named it '"
                    + suggestion + "'. Run Tools > Dojo > Build Popup Prefabs.", this);
                named(suggestion);
                return;
            }

            var popup = PopupManager.Instance.Show(popups.Prompt)
                .Tint(MessagePopup.Tone.Info)
                .Present(heading, question)
                .Caption(MessagePopup.Choice.Cancel, "CANCEL")
                .Caption(MessagePopup.Choice.Primary, "SAVE")
                .Ask(suggestion);

            popup.Chosen += choice =>
            {
                if (choice == MessagePopup.Choice.Primary && popup.Answer.Length > 0)
                {
                    named(popup.Answer);
                }
            };
        }

        /// <summary>"Area 3": the next number not already taken on any floor.</summary>
        string SuggestedName()
        {
            var taken = new HashSet<string>();

            if (areas != null)
            {
                foreach (var area in areas.All)
                {
                    taken.Add(area.Name);
                }
            }

            var n = 1;

            while (taken.Contains("Area " + n))
            {
                n++;
            }

            return "Area " + n;
        }
    }
}
