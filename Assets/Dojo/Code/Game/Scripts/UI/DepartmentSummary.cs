using System;
using System.Collections.Generic;
using Dojo.Framework.World;
using Dojo.Game.Placement;
using Dojo.Game.Server;

namespace Dojo.Game.UI
{
    /// <summary>One department in the building: its name, and what of the building is in it.</summary>
    public sealed class DepartmentLine
    {
        public string Name;
        public int Areas;

        /// <summary>The agents assigned to its areas, each once, in the order they were met.</summary>
        public List<string> AgentNames = new List<string>();

        public int Agents => AgentNames.Count;

        /// <summary>The floors it has areas on, ground first.</summary>
        public List<int> Floors = new List<int>();

        /// <summary>Its area on the lowest floor.</summary>
        public BlockArea First;

        /// <summary>Every area it has, in the building's order.</summary>
        public List<BlockArea> AreaList = new List<BlockArea>();
    }

    /// <summary>
    /// The building's departments, read from its areas, and the words the Departments tab shows
    /// for them.
    /// </summary>
    /// <remarks>
    /// In the game these are the static half of <c>DepartmentsPanel</c>, the in-game menu's
    /// DEPARTMENTS list. That panel is not here - the creator's menu has no entry for it - so the
    /// part the inventory's Departments tab shares with it stands on its own, unchanged.
    /// </remarks>
    public static class DepartmentSummary
    {
        /// <summary>
        /// The building's departments from its areas, by name: areas with the same department -
        /// whatever its case - are one line. Areas with none are left out.
        /// </summary>
        public static List<DepartmentLine> Summarise(IEnumerable<BlockArea> all, Func<int, IEnumerable<string>> agentsIn)
        {
            var lines = new List<DepartmentLine>();

            if (all == null)
            {
                return lines;
            }

            foreach (var area in all)
            {
                if (area == null || string.IsNullOrWhiteSpace(area.Department))
                {
                    continue;
                }

                var name = area.Department.Trim();
                var line = lines.Find(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));

                if (line == null)
                {
                    line = new DepartmentLine { Name = name };
                    lines.Add(line);
                }

                line.Areas++;
                line.AreaList.Add(area);

                var names = agentsIn != null ? agentsIn(area.Id) : null;
                if (names != null)
                {
                    foreach (var agent in names)
                    {
                        if (!string.IsNullOrWhiteSpace(agent) && !line.AgentNames.Contains(agent))
                        {
                            line.AgentNames.Add(agent);
                        }
                    }
                }

                if (!line.Floors.Contains(area.Floor))
                {
                    line.Floors.Add(area.Floor);
                }

                if (line.First == null || area.Floor < line.First.Floor)
                {
                    line.First = area;
                }
            }

            foreach (var line in lines)
            {
                line.Floors.Sort();
            }

            lines.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return lines;
        }

        /// <summary>
        /// Who is assigned, by name: "ANA, BEN", the rest counted past <paramref name="most"/>
        /// ("ANA, BEN +2"), or "NO AGENT ASSIGNED".
        /// </summary>
        public static string AgentsLine(IList<string> names, int most)
        {
            if (names == null || names.Count == 0)
            {
                return "NO AGENT ASSIGNED";
            }

            var shown = Math.Max(1, Math.Min(most, names.Count));
            var words = new string[shown];
            for (var i = 0; i < shown; i++)
            {
                words[i] = names[i].ToUpperInvariant();
            }

            var rest = names.Count - shown;
            return string.Join(", ", words) + (rest > 0 ? " +" + rest : string.Empty);
        }

        /// <summary>What it holds: "2 AREAS · 1F, 2F".</summary>
        public static string Describe(int areaCount, IList<int> floors)
        {
            var parts = new List<string> { areaCount + (areaCount == 1 ? " AREA" : " AREAS") };

            if (floors != null && floors.Count > 0)
            {
                var labels = new string[floors.Count];
                for (var i = 0; i < floors.Count; i++)
                {
                    labels[i] = Storeys.LabelFor(floors[i]);
                }

                parts.Add(string.Join(", ", labels));
            }

            return string.Join(" · ", parts);
        }

        /// <summary>"4 DEPARTMENTS · 2 FLOORS".</summary>
        public static string StatusLine(int departments, int floors)
            => departments + (departments == 1 ? " DEPARTMENT" : " DEPARTMENTS")
               + " · " + floors + (floors == 1 ? " FLOOR" : " FLOORS");

        /// <summary>The credential ids of the agents assigned to <paramref name="these"/> areas, each once.</summary>
        public static List<int> AgentIdsIn(BlockAreas areas, IEnumerable<BlockArea> these)
        {
            var ids = new List<int>();

            if (areas == null || these == null)
            {
                return ids;
            }

            foreach (var area in these)
            {
                foreach (var holder in areas.Holders(area.Id))
                {
                    if (holder.Role == SnapshotRole.Agent && !ids.Contains(holder.CredentialId))
                    {
                        ids.Add(holder.CredentialId);
                    }
                }
            }

            return ids;
        }

        /// <summary>
        /// The agents assigned to an area, by their roster names - the names the Areas tab shows.
        /// The manager is not an agent and is left out.
        /// </summary>
        public static List<string> AgentNamesIn(BlockAreas areas, IAgentRegistry registry, int areaId)
        {
            var names = new List<string>();

            if (areas == null)
            {
                return names;
            }

            foreach (var holder in areas.Holders(areaId))
            {
                if (holder.Role == SnapshotRole.Agent)
                {
                    var agent = registry != null ? registry.Find(holder.CredentialId) : null;
                    names.Add(agent != null ? agent.name : "Agent " + holder.CredentialId);
                }
            }

            return names;
        }
    }
}
