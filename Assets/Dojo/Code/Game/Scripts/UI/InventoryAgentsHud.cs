using System;
using System.Collections.Generic;
using Dojo.Game.InGame.Agents;
using Dojo.Game.Server;
using TMPro;
using UnityEngine;
using VContainer;

namespace Dojo.Game.UI
{
    /// <summary>
    /// The Agents tab: the roster as a list of rows, and below it the card that says what the
    /// selected agent is and offers the four things that can be done with its area.
    /// </summary>
    /// <remarks>
    /// A separate hud from <see cref="InventoryHud"/> rather than a mode of it. The furniture tabs
    /// are a grid of interchangeable things the player drags out; the roster is a list of named
    /// agents with a card beneath, and the two share only a header. Folding both into one component
    /// would mean every field on it applied to half the tabs.
    /// <para>
    /// What this does <em>not</em> own is everything below the list. The card, the four buttons, the
    /// brush, the commit and every rule about painting an area belong to
    /// <see cref="AgentDisplayUI"/>, which already has them; this lists agents and tells that which
    /// one to show. The division is the same one the rail and the grid keep — one thing chooses,
    /// another displays.
    /// </para>
    /// </remarks>
    public sealed class InventoryAgentsHud : MonoBehaviour
    {
        [Header("Header")]
        [Tooltip("Reads AGENTS. Fixed, but kept here so the tab can rename itself.")]
        [SerializeField] TextMeshProUGUI titleLabel;

        [Tooltip("Reads ROSTER - 3 LIVE.")]
        [SerializeField] TextMeshProUGUI subtitleLabel;

        [Tooltip("Reads 3 of 7 - how many rows are showing, out of the whole roster.")]
        [SerializeField] TextMeshProUGUI countLabel;

        [Header("Filter")]
        [Tooltip("Free-text filter. Matches the agent name and its subtitle.")]
        [SerializeField] TMP_InputField searchField;

        [SerializeField] TextMeshProUGUI searchPlaceholder;

        [Header("Roster")]
        [Tooltip("Parent the rows are created under. The object carrying the layout group.")]
        [SerializeField] Transform rosterContent;

        [Tooltip("One row, instantiated per agent.")]
        [SerializeField] AgentRosterRow rowPrefab;

        [Tooltip("Shown instead of the list when the roster is empty or nothing matches.")]
        [SerializeField] GameObject emptyState;

        [Header("Card")]
        [Tooltip("The agent card and its four area buttons. Owns everything below the list.")]
        [SerializeField] AgentDisplayUI display;

        [Tooltip("The line under the name on the card, e.g. ONBOARDING. Filled here because the " +
                 "card component carries no field for it.")]
        [SerializeField] TextMeshProUGUI cardSubtitleLabel;

        [Tooltip("How big the selected agent's area is, e.g. 543 cells.")]
        [SerializeField] TextMeshProUGUI cardCellsLabel;

        [Tooltip("The DRAFT flag. Shown while the agent has an area that is not yet committed.")]
        [SerializeField] GameObject cardDraftMarker;

        IAgentRegistry registry;

        readonly List<AgentRosterRow> rows = new List<AgentRosterRow>();

        string search = string.Empty;

        /// <summary>How many rows are showing.</summary>
        public int VisibleCount => rows.Count;

        /// <summary>The agent the card is showing, or zero.</summary>
        public int SelectedId { get; private set; }

        [Inject]
        public void Construct(IAgentRegistry agentRegistry)
        {
            registry = agentRegistry;
        }

        void Awake()
        {
            if (searchField != null)
            {
                searchField.onValueChanged.AddListener(OnSearchChanged);
            }

            if (searchPlaceholder != null)
            {
                searchPlaceholder.text = "Filter roster";
            }
        }

        void OnDestroy()
        {
            if (searchField != null)
            {
                searchField.onValueChanged.RemoveListener(OnSearchChanged);
            }
        }

        /// <summary>
        /// Opens the tab: raises the card, then lists the roster. The card is raised first for the
        /// same reason <see cref="AgentDisplayUI.Populate()"/> does it first — pressing the tab has
        /// to show something even when the roster reads empty, or a working tab and a broken one
        /// look identical.
        /// </summary>
        public void Show()
        {
            search = string.Empty;

            if (searchField != null)
            {
                searchField.SetTextWithoutNotify(string.Empty);
            }

            if (display != null)
            {
                display.Populate();
            }

            Rebuild();
        }

        /// <summary>
        /// Closes the tab. Handed to <see cref="AgentDisplayUI.Dismiss"/> rather than just hidden,
        /// because a paint session left running would outlive the only card that says who it is for.
        /// </summary>
        public void Hide()
        {
            if (display != null)
            {
                display.Dismiss();
            }

            ClearRows();
        }

        void OnSearchChanged(string text)
        {
            search = text ?? string.Empty;
            Rebuild();
        }

        void Rebuild()
        {
            ClearRows();

            var agents = registry != null ? registry.Agents : null;
            int total = agents != null ? agents.Count : 0;
            int live = 0;

            if (agents != null)
            {
                for (int i = 0; i < agents.Count; i++)
                {
                    var agent = agents[i];
                    if (agent == null)
                    {
                        continue;
                    }

                    if (IsLive(agent))
                    {
                        live++;
                    }

                    if (!Matches(agent, search))
                    {
                        continue;
                    }

                    var row = Create();
                    if (row == null)
                    {
                        break;
                    }

                    row.name = "Row_" + agent.name;
                    row.Bind(agent.id, agent.name, SubtitleOf(agent), agent.AreaCellCount > 0);
                    row.Clicked += OnRowClicked;

                    rows.Add(row);
                }
            }

            ApplyHeader(total, live);

            if (emptyState != null)
            {
                emptyState.SetActive(rows.Count == 0);
            }

            // The card chooses its own agent when the tab opens — whoever was showing last time, or
            // the first in the roster — and it is the card that is on screen. Reading that choice
            // back is what keeps the lit row and the card below it the same agent. Without it the
            // hud highlights whatever it last recorded itself, which on a first open is nothing,
            // leaving a card describing an agent that no row claims.
            if (display != null && display.Selected != null)
            {
                SelectedId = display.Selected.id;
            }

            ApplySelection(SelectedId);
        }

        void OnRowClicked(int agentId)
        {
            SelectedId = agentId;

            if (display != null)
            {
                display.Select(agentId);
            }

            ApplySelection(agentId);
        }

        void ApplySelection(int agentId)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].SetSelected(rows[i].AgentId == agentId);
            }

            ApplyCard(registry != null ? registry.Find(agentId) : null);
        }

        /// <summary>
        /// Fills the parts of the card <see cref="AgentDisplayUI"/> has no field for. It owns the
        /// name, the description, the portrait and the four buttons; these three are the design's
        /// additions, and driving them here is cheaper than widening a component that already works.
        /// </summary>
        void ApplyCard(AgentCredential agent)
        {
            int cells = agent != null ? agent.AreaCellCount : 0;

            if (cardSubtitleLabel != null)
            {
                cardSubtitleLabel.text = agent != null
                    ? SubtitleOf(agent).ToUpperInvariant()
                    : string.Empty;
            }

            // Where the agent walks: the area it has been given and its floor, or its whole floor.
            if (cardCellsLabel != null)
            {
                cardCellsLabel.text = agent != null && display != null ? display.AreaText(agent) : string.Empty;
            }

            // Lit while the agent has an area of its own.
            if (cardDraftMarker != null)
            {
                cardDraftMarker.SetActive(cells > 0);
            }
        }

        void ApplyHeader(int total, int live)
        {
            if (titleLabel != null)
            {
                titleLabel.text = "AGENTS";
            }

            if (subtitleLabel != null)
            {
                subtitleLabel.text = "ROSTER · " + live + " LIVE";
            }

            if (countLabel != null)
            {
                countLabel.text = rows.Count + " of " + total;
            }
        }

        AgentRosterRow Create()
        {
            if (rowPrefab == null || rosterContent == null)
            {
                return null;
            }

            return Instantiate(rowPrefab, rosterContent);
        }

        void ClearRows()
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] != null)
                {
                    rows[i].Clicked -= OnRowClicked;
                    Destroy(rows[i].gameObject);
                }
            }

            rows.Clear();
        }

        static bool Matches(AgentCredential agent, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            string needle = text.Trim();

            return Contains(agent.name, needle) || Contains(SubtitleOf(agent), needle);
        }

        static bool Contains(string haystack, string needle)
        {
            return !string.IsNullOrEmpty(haystack) &&
                   haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// The line under an agent's name. <c>workId</c> is what an agent is for, which is the
        /// nearest thing the roster carries to the job titles in the design.
        /// </summary>
        static string SubtitleOf(AgentCredential agent)
        {
            if (!string.IsNullOrWhiteSpace(agent.workId))
            {
                return agent.workId;
            }

            return !string.IsNullOrWhiteSpace(agent.origin) ? agent.origin : string.Empty;
        }

        /// <summary>
        /// Whether an agent counts towards the LIVE tally. A roster that states no status at all
        /// counts every agent, so the header reads as a roster size rather than as zero.
        /// </summary>
        static bool IsLive(AgentCredential agent)
        {
            if (string.IsNullOrWhiteSpace(agent.status))
            {
                return true;
            }

            return agent.status.Equals("live", StringComparison.OrdinalIgnoreCase)
                || agent.status.Equals("active", StringComparison.OrdinalIgnoreCase)
                || agent.status.Equals("ready", StringComparison.OrdinalIgnoreCase);
        }
    }
}
