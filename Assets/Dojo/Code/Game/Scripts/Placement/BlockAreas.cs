using System;
using System.Collections.Generic;
using Dojo.Framework.Events;
using Dojo.Framework.World;
using Dojo.Game.Events;
using UnityEngine;

namespace Dojo.Game.Placement
{
    /// <summary>A named patch of one floor, which agents or the manager can be given.</summary>
    public sealed class BlockArea
    {
        public BlockArea(int id, string name, int floor, float cellSize, List<Vector3> cells)
        {
            Id = id;
            Name = name;
            Floor = floor;
            CellSize = cellSize;
            Cells = cells ?? new List<Vector3>();
        }

        /// <summary>Unique within its world, and never reused.</summary>
        public int Id { get; }

        /// <summary>What the player called it.</summary>
        public string Name { get; internal set; }

        /// <summary>Which floor, from 0.</summary>
        public int Floor { get; internal set; }

        /// <summary>Width of a cell in metres.</summary>
        public float CellSize { get; internal set; }

        /// <summary>Middle of every painted cell, in world space.</summary>
        public List<Vector3> Cells { get; internal set; }

        /// <summary>
        /// The middle of the area: the average of its cells' middles. For an area that bends - an
        /// L - it can fall outside the area.
        /// </summary>
        public Vector3 Centre
        {
            get
            {
                if (Cells.Count == 0)
                {
                    return Vector3.zero;
                }

                var sum = Vector3.zero;

                foreach (var cell in Cells)
                {
                    sum += cell;
                }

                return sum / Cells.Count;
            }
        }

        /// <summary>
        /// Where to stop in the area, best first: its middle, when that is in the area, then every
        /// cell, nearest the middle first. Whoever is sent to the area stops at the first of these
        /// that is free - so at the middle, or with something standing there, as near it as can be.
        /// </summary>
        public List<Vector3> CentreFirst()
        {
            var spots = new List<Vector3>(Cells.Count + 1);

            if (Cells.Count == 0)
            {
                return spots;
            }

            var centre = Centre;
            var half = CellSize * 0.5f + 0.0001f;

            foreach (var cell in Cells)
            {
                if (Mathf.Abs(cell.x - centre.x) <= half && Mathf.Abs(cell.z - centre.z) <= half)
                {
                    spots.Add(centre);
                    break;
                }
            }

            var byNearness = new List<Vector3>(Cells);
            byNearness.Sort((a, b) => FlatSqr(a, centre).CompareTo(FlatSqr(b, centre)));
            spots.AddRange(byNearness);

            return spots;
        }

        /// <summary>
        /// Whether <paramref name="point"/> is in the area: over one of its cells, give or take a
        /// little, and at its floor's height.
        /// </summary>
        public bool Contains(Vector3 point)
        {
            var reach = CellSize * 0.75f;

            foreach (var cell in Cells)
            {
                if (Mathf.Abs(cell.x - point.x) <= reach && Mathf.Abs(cell.z - point.z) <= reach
                    && Mathf.Abs(cell.y - point.y) < 1f)
                {
                    return true;
                }
            }

            return false;
        }

        static float FlatSqr(Vector3 a, Vector3 b)
        {
            var dx = a.x - b.x;
            var dz = a.z - b.z;
            return dx * dx + dz * dz;
        }
    }

    /// <summary>
    /// The block areas of the world that is open, and which one each agent and the manager uses.
    /// </summary>
    /// <remarks>
    /// All area painting is kept here: agents and the manager no longer own a painted area, they
    /// are given one of these. Several agents may share an area; the manager never shares one with
    /// an agent, and the two refusals below are where that rule is kept.
    /// <para>
    /// Every change made through this is announced as a change to the world, so autosave keeps it.
    /// Loading a world is not a change, and says only <see cref="Changed"/>.
    /// </para>
    /// </remarks>
    public sealed class BlockAreas
    {
        readonly IEventHub hub;
        readonly List<BlockArea> areas = new List<BlockArea>();
        readonly Dictionary<Holder, int> assignments = new Dictionary<Holder, int>();

        public BlockAreas(IEventHub hub)
        {
            this.hub = hub;
        }

        /// <summary>Something about the areas or who has them has changed, for whatever reason.</summary>
        public event Action Changed;

        /// <summary>Every area, on every floor.</summary>
        public IReadOnlyList<BlockArea> All => areas;

        /// <summary>The area with this id, or null.</summary>
        public BlockArea Find(int id)
        {
            foreach (var area in areas)
            {
                if (area.Id == id)
                {
                    return area;
                }
            }

            return null;
        }

        /// <summary>The areas on one floor.</summary>
        public IEnumerable<BlockArea> OnFloor(int floor)
        {
            foreach (var area in areas)
            {
                if (area.Floor == floor)
                {
                    yield return area;
                }
            }
        }

        // ── Painting ────────────────────────────────────────────────────────────────────────

        /// <summary>A new area, given to nobody.</summary>
        public BlockArea Add(string name, int floor, List<Vector3> cells, float cellSize)
        {
            var area = new BlockArea(NewId(), Clean(name), floor, cellSize, new List<Vector3>(cells ?? new List<Vector3>()));
            areas.Add(area);
            Announce("area '" + area.Name + "' added");
            return area;
        }

        /// <summary>Paints an area again. Whoever has it keeps it.</summary>
        public bool Repaint(int id, List<Vector3> cells)
        {
            var area = Find(id);

            if (area == null)
            {
                return false;
            }

            area.Cells = new List<Vector3>(cells ?? new List<Vector3>());
            Announce("area '" + area.Name + "' repainted");
            return true;
        }

        public bool Rename(int id, string name)
        {
            var area = Find(id);

            if (area == null)
            {
                return false;
            }

            area.Name = Clean(name);
            Announce("area renamed to '" + area.Name + "'");
            return true;
        }

        /// <summary>Removes an area. Anybody who had it is left with none.</summary>
        public bool Delete(int id)
        {
            var area = Find(id);

            if (area == null)
            {
                return false;
            }

            areas.Remove(area);

            foreach (var holder in Holders(id))
            {
                assignments.Remove(holder);
            }

            Announce("area '" + area.Name + "' deleted");
            return true;
        }

        // ── Who has which ───────────────────────────────────────────────────────────────────

        /// <summary>The id of the area these credentials use, or 0 for none.</summary>
        public int AreaOf(SnapshotRole role, int credentialId)
        {
            int id;
            return assignments.TryGetValue(new Holder(role, credentialId), out id) ? id : 0;
        }

        /// <summary>The area these credentials use, or null for none.</summary>
        public BlockArea AreaFor(SnapshotRole role, int credentialId) => Find(AreaOf(role, credentialId));

        /// <summary>
        /// Gives these credentials an area, or takes theirs away with 0. False, and nothing
        /// changed, when the manager and an agent would end up sharing.
        /// </summary>
        public bool Assign(SnapshotRole role, int credentialId, int areaId)
        {
            var holder = new Holder(role, credentialId);

            if (areaId == 0)
            {
                if (assignments.Remove(holder))
                {
                    Announce("area taken away");
                }

                return true;
            }

            var area = Find(areaId);

            if (area == null)
            {
                return false;
            }

            if (role == SnapshotRole.Agent && IsManagers(areaId))
            {
                return false;
            }

            if (role == SnapshotRole.Manager && UsedByAgents(areaId))
            {
                return false;
            }

            int current;

            if (assignments.TryGetValue(holder, out current) && current == areaId)
            {
                return true;
            }

            assignments[holder] = areaId;
            Announce("area '" + area.Name + "' given");
            return true;
        }

        /// <summary>Everybody using an area.</summary>
        public IEnumerable<Holder> Holders(int areaId)
        {
            var found = new List<Holder>();

            foreach (var pair in assignments)
            {
                if (pair.Value == areaId)
                {
                    found.Add(pair.Key);
                }
            }

            return found;
        }

        /// <summary>Whether the manager has this area.</summary>
        public bool IsManagers(int areaId)
        {
            foreach (var holder in Holders(areaId))
            {
                if (holder.Role == SnapshotRole.Manager)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether any agent has this area.</summary>
        public bool UsedByAgents(int areaId)
        {
            foreach (var holder in Holders(areaId))
            {
                if (holder.Role == SnapshotRole.Agent)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>What an agent may be given: every area but the manager's.</summary>
        public IEnumerable<BlockArea> ChoicesForAgent()
        {
            foreach (var area in areas)
            {
                if (!IsManagers(area.Id))
                {
                    yield return area;
                }
            }
        }

        /// <summary>What the manager may be given: every area no agent uses, his own among them.</summary>
        public IEnumerable<BlockArea> ChoicesForManager()
        {
            foreach (var area in areas)
            {
                if (!UsedByAgents(area.Id))
                {
                    yield return area;
                }
            }
        }

        // ── Loading ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Takes on a world's areas and assignments, replacing whatever was here. A load, not a
        /// change: nothing is announced to autosave.
        /// </summary>
        public void Load(IEnumerable<BlockArea> loaded, IEnumerable<WorldSave.SavedAssignment> given)
        {
            areas.Clear();
            assignments.Clear();

            if (loaded != null)
            {
                foreach (var area in loaded)
                {
                    if (area != null && area.Id != 0 && Find(area.Id) == null)
                    {
                        areas.Add(area);
                    }
                }
            }

            if (given != null)
            {
                foreach (var assignment in given)
                {
                    if (assignment != null && Find(assignment.areaId) != null)
                    {
                        assignments[new Holder(assignment.role, assignment.credentialId)] = assignment.areaId;
                    }
                }
            }

            var handler = Changed;
            if (handler != null)
            {
                handler();
            }
        }

        /// <summary>Every assignment, for saving.</summary>
        public List<WorldSave.SavedAssignment> Assignments()
        {
            var list = new List<WorldSave.SavedAssignment>();

            foreach (var pair in assignments)
            {
                list.Add(new WorldSave.SavedAssignment
                {
                    role = pair.Key.Role,
                    credentialId = pair.Key.CredentialId,
                    areaId = pair.Value,
                });
            }

            return list;
        }

        void Announce(string what)
        {
            var handler = Changed;
            if (handler != null)
            {
                handler();
            }

            if (hub != null)
            {
                hub.Publish(new WorldChanged(what));
            }
        }

        int NewId()
        {
            while (true)
            {
                var id = Guid.NewGuid().GetHashCode() & 0x7fffffff;

                if (id != 0 && Find(id) == null)
                {
                    return id;
                }
            }
        }

        static string Clean(string name)
        {
            var trimmed = string.IsNullOrWhiteSpace(name) ? "Area" : name.Trim();
            return trimmed.Length > 40 ? trimmed.Substring(0, 40) : trimmed;
        }

        /// <summary>One set of credentials that can be given an area: an agent's, or the manager's.</summary>
        public readonly struct Holder : IEquatable<Holder>
        {
            public Holder(SnapshotRole role, int credentialId)
            {
                Role = role;
                CredentialId = credentialId;
            }

            public SnapshotRole Role { get; }
            public int CredentialId { get; }

            public bool Equals(Holder other) => Role == other.Role && CredentialId == other.CredentialId;
            public override bool Equals(object obj) => obj is Holder other && Equals(other);
            public override int GetHashCode() => ((int)Role * 397) ^ CredentialId;
        }
    }
}
