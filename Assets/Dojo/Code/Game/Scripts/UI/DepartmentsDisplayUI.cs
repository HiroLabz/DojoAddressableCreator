using System.Collections.Generic;
using Dojo.Game.Placement;
using Dojo.Game.Server;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace Dojo.Game.UI
{
    /// <summary>
    /// The inventory's Departments tab: one card for every department the building's areas carry,
    /// with its areas, its floors and the agents assigned to it.
    /// </summary>
    /// <remarks>
    /// The same summary as the game's in-game DEPARTMENTS menu, from the same
    /// <see cref="DepartmentSummary.Summarise"/>. A department is a name on an area, so a building
    /// whose areas carry none shows the empty state.
    /// </remarks>
    public sealed class DepartmentsDisplayUI : MonoBehaviour
    {
        [Header("Header")]
        [Tooltip("How many departments there are and on how many floors, e.g. 7 DEPARTMENTS · 2 FLOORS.")]
        [SerializeField] TMP_Text subtitleLabel;

        [Header("List")]
        [SerializeField] Transform listContent;
        [SerializeField] DepartmentRow rowPrefab;

        [Tooltip("Shown when no area has a department.")]
        [SerializeField] GameObject emptyState;

        BlockAreas areas;
        Storeys storeys;
        IAgentRegistry registry;
        AgentPortraits portraits;

        readonly List<DepartmentRow> rows = new List<DepartmentRow>();

        [Inject]
        public void Construct(BlockAreas blockAreas, Storeys floors, IAgentRegistry agentRegistry, AgentPortraits agentPortraits)
        {
            areas = blockAreas;
            storeys = floors;
            registry = agentRegistry;
            portraits = agentPortraits;
        }

        public void Show()
        {
            gameObject.SetActive(true);
            Rebuild();
        }

        public void Dismiss()
        {
            gameObject.SetActive(false);
        }

        /// <summary>How many department cards are showing.</summary>
        public int Count => rows.Count;

        void Rebuild()
        {
            foreach (var row in rows)
            {
                if (row != null)
                {
                    Destroy(row.gameObject);
                }
            }

            rows.Clear();

            var lines = areas != null
                ? DepartmentSummary.Summarise(areas.All, id => DepartmentSummary.AgentNamesIn(areas, registry, id))
                : new List<DepartmentLine>();

            if (rowPrefab != null && listContent != null)
            {
                foreach (var line in lines)
                {
                    var row = Instantiate(rowPrefab, listContent);
                    row.gameObject.SetActive(true);
                    var ids = DepartmentSummary.AgentIdsIn(areas, line.AreaList);
                    row.Bind(line, ids.Count > 0 && portraits != null ? portraits.For(ids[0]) : null);
                    rows.Add(row);
                }

                var content = listContent as RectTransform;
                if (content != null)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(content);
                }
            }
            else
            {
                Debug.LogWarning("[Departments] The Departments tab has no card or list to fill.", this);
            }

            if (emptyState != null)
            {
                emptyState.SetActive(lines.Count == 0);
            }

            if (subtitleLabel != null)
            {
                subtitleLabel.text = DepartmentSummary.StatusLine(lines.Count, storeys != null ? storeys.Count : 0);
            }
        }
    }
}
