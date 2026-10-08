using System;
using System.Collections.Generic;
using Dojo.Framework.World;
using UnityEngine;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// Every world that has been saved. One file holds them all, each under its own name.
    /// </summary>
    /// <remarks>
    /// A list rather than a file per world, so the whole set can be read to populate a chooser
    /// without going near the file system twice, and so there is exactly one thing to back up or
    /// hand to somebody else.
    /// </remarks>
    [Serializable]
    public sealed class WorldLibrary
    {
        /// <summary>Bumped when the shape of this file changes.</summary>
        public const int CurrentVersion = 2;

        public int version = CurrentVersion;

        public List<WorldSave> worlds = new List<WorldSave>();

        /// <summary>
        /// Name of the world the game should open, or empty when nothing has been picked.
        /// </summary>
        /// <remarks>
        /// Kept here rather than in a file or a PlayerPref of its own, because "which of these is
        /// open" is a fact about this library and about nothing else: it can only ever name one of
        /// the worlds beside it, so the two belong in the same place and a library handed to
        /// another machine arrives still pointing at the same world.
        /// <para>
        /// Empty is the honest starting state and needs no migration. A library written before
        /// this field existed reads back as empty, which means "nothing picked yet" — which is
        /// exactly what was true of it. Hence no version bump.
        /// </para>
        /// </remarks>
        public string current = string.Empty;

        /// <summary>The world by that name, or null. Names are matched ignoring case and spacing.</summary>
        public WorldSave Find(string name)
        {
            if (worlds == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            foreach (var world in worlds)
            {
                if (world != null && Matches(world.name, name))
                {
                    return world;
                }
            }

            return null;
        }

        /// <summary>Every name held, in the order they were saved.</summary>
        public List<string> Names()
        {
            var names = new List<string>();

            if (worlds == null)
            {
                return names;
            }

            foreach (var world in worlds)
            {
                if (world != null && !string.IsNullOrEmpty(world.name))
                {
                    names.Add(world.name);
                }
            }

            return names;
        }

        /// <summary>
        /// The selected world's name as stored, or null when nothing valid is selected.
        /// </summary>
        /// <remarks>
        /// Resolved through <see cref="Find"/> rather than handed back as written, and that is the
        /// whole value of the method. A selected world can be renamed or dropped afterwards, and a
        /// <see cref="current"/> naming something no longer in the list has to read as "nothing
        /// picked" — otherwise the game opens on an error about a missing world instead of simply
        /// asking which one to use.
        /// <para>
        /// Returning the stored spelling rather than the remembered one also means the caller can
        /// hand it straight back to <see cref="Find"/> and to the chooser.
        /// </para>
        /// </remarks>
        public string CurrentName()
        {
            var world = Find(current);

            return world == null ? null : world.name;
        }

        /// <summary>
        /// Records which world is open, and says whether that changed anything.
        /// </summary>
        /// <remarks>
        /// Stores the name as the world itself spells it, not as the caller typed it, so a world
        /// picked as "world5" is remembered as "World5" and the two never look like different
        /// worlds later. Matching is <see cref="Find"/>'s, so it forgives case and spacing exactly
        /// where the chooser and the save dialog already do.
        /// <para>
        /// The return value is what lets a caller avoid rewriting the file when nothing moved —
        /// loading the world that is already selected is the common case, and it should not cost a
        /// write.
        /// </para>
        /// </remarks>
        public bool Select(string name)
        {
            var world = Find(name);
            var chosen = world == null ? string.Empty : world.name;

            if (string.Equals(current, chosen, StringComparison.Ordinal))
            {
                return false;
            }

            current = chosen;

            return true;
        }

        /// <summary>
        /// Puts a world in, replacing one of the same name rather than adding a second.
        /// </summary>
        /// <summary>
        /// Takes a world out of the library. Returns whether there was one to take.
        /// </summary>
        /// <remarks>
        /// Clears <see cref="current"/> when the world removed was the open one, so the library
        /// cannot go on naming a world it no longer holds — which is the state
        /// <see cref="CurrentName"/> already treats as "nothing chosen", reached honestly.
        /// </remarks>
        public bool Remove(string name)
        {
            if (worlds == null || string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            for (var i = 0; i < worlds.Count; i++)
            {
                if (worlds[i] == null || !Matches(worlds[i].name, name))
                {
                    continue;
                }

                worlds.RemoveAt(i);

                if (Matches(current, name))
                {
                    current = string.Empty;
                }

                return true;
            }

            return false;
        }

        public void Put(WorldSave world)
        {
            if (world == null)
            {
                return;
            }

            if (worlds == null)
            {
                worlds = new List<WorldSave>();
            }

            // Stored tidy, because this is the string the chooser shows back to them.
            world.name = (world.name ?? string.Empty).Trim();

            for (var i = 0; i < worlds.Count; i++)
            {
                if (worlds[i] != null && Matches(worlds[i].name, world.name))
                {
                    worlds[i] = world;
                    return;
                }
            }

            worlds.Add(world);
        }

        /// <summary>
        /// Two names are the same world. Compared loosely on purpose: a player who types "My
        /// Office " expecting to overwrite "my office" is right, and ending up with two worlds a
        /// space apart is not something they could ever untangle from the chooser.
        /// </summary>
        public static bool Matches(string a, string b)
            => string.Equals((a ?? string.Empty).Trim(), (b ?? string.Empty).Trim(),
                StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// One saved world: what the player named it, every piece they put down, and the settings the
    /// navigation mesh was baked with.
    /// </summary>
    /// <remarks>
    /// <b>The navigation mesh itself is deliberately not in here, and cannot be.</b>
    /// <c>NavMeshData</c> is an opaque native object with no public serialisation: it can be built,
    /// and it can be read back as a triangulation, but there is no way to turn a triangulation back
    /// into one. No amount of JSON gets round that.
    /// <para>
    /// It does not need to be saved, because it is derived. Rebuilding it from the pieces that have
    /// just been loaded is quick, and it is the only version that is guaranteed to agree with them —
    /// a stored mesh could only ever introduce a way for the walkable space and the world to
    /// disagree. What is worth keeping, and is kept here, are the <em>settings</em> it was baked
    /// with, so a load reproduces the same bake rather than whatever the scene happens to be
    /// configured for now.
    /// </para>
    /// <para>
    /// Shaped for <see cref="JsonUtility"/> — plain serializable classes and Unity's own vector
    /// types — which is what the thumbnail manifest already uses.
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class WorldSave
    {
        /// <summary>What the player called it. The name it is chosen by, and saved over by.</summary>
        public string name;

        /// <summary>When it was written, for the player's benefit rather than the loader's.</summary>
        public string savedUtc;

        /// <summary>
        /// The world's floors, ground first: World &gt; Floor &gt; everything standing on it.
        /// </summary>
        /// <remarks>
        /// Every position inside a floor is relative to that floor - a height of 0 is on its tiles -
        /// so removing a floor only renumbers the ones above it, and nothing has to move.
        /// </remarks>
        public List<SavedFloor> floors = new List<SavedFloor>();

        /// <summary>
        /// What joins the floors: the elevator now, stairs later. On the world rather than on a
        /// floor, because the elevator runs through every floor at one spot.
        /// </summary>
        public List<SavedConnector> connectors = new List<SavedConnector>();

        /// <summary>How the navigation mesh was built, one entry per agent type. One bake covers every floor.</summary>
        public List<SavedSurface> navigation = new List<SavedSurface>();

        /// <summary>
        /// Which block area each agent's and the manager's credentials use: at most one each.
        /// </summary>
        /// <remarks>
        /// On the world rather than a floor, because an agent and the area it is given may be on
        /// different floors. An area nobody is given is simply not mentioned here.
        /// </remarks>
        public List<SavedAssignment> assignments = new List<SavedAssignment>();

        // ── The shape before floors ─────────────────────────────────────────────────────────
        // Read, never written. The default world in the content pack and any world saved before
        // floors existed hold everything at the top level; Normalised moves it onto floor 0, where
        // its positions already are, since floor 0's own level is the ground.

        /// <summary>Before floors: every piece. Read only — see <see cref="Normalised"/>.</summary>
        public List<SavedPiece> pieces = new List<SavedPiece>();

        /// <summary>Before floors: every agent. Read only — see <see cref="Normalised"/>.</summary>
        public List<SavedAgent> agents = new List<SavedAgent>();

        /// <summary>Before floors: every agent's area. Read only — see <see cref="Normalised"/>.</summary>
        public List<SavedAgentArea> agentAreas = new List<SavedAgentArea>();

        /// <summary>Before floors: the manager. Read only — see <see cref="Normalised"/>.</summary>
        public List<SavedAgent> managers = new List<SavedAgent>();

        /// <summary>Before floors: the manager's area. Read only — see <see cref="Normalised"/>.</summary>
        public List<SavedAgentArea> managerAreas = new List<SavedAgentArea>();

        /// <summary>
        /// This world with floors: one written before floors existed has everything moved onto
        /// floor 0, and the floors are put in order. Safe to call more than once.
        /// </summary>
        public WorldSave Normalised()
        {
            if (floors == null)
            {
                floors = new List<SavedFloor>();
            }

            if (connectors == null)
            {
                connectors = new List<SavedConnector>();
            }

            if (navigation == null)
            {
                navigation = new List<SavedSurface>();
            }

            if (assignments == null)
            {
                assignments = new List<SavedAssignment>();
            }

            var legacy = Count(pieces) + Count(agents) + Count(agentAreas) + Count(managers) + Count(managerAreas);

            if (floors.Count == 0 || legacy > 0)
            {
                var ground = FloorAt(0);
                ground.pieces.AddRange(pieces ?? new List<SavedPiece>());
                ground.agents.AddRange(agents ?? new List<SavedAgent>());
                ground.agentAreas.AddRange(agentAreas ?? new List<SavedAgentArea>());
                ground.managers.AddRange(managers ?? new List<SavedAgent>());
                ground.managerAreas.AddRange(managerAreas ?? new List<SavedAgentArea>());
            }

            pieces = new List<SavedPiece>();
            agents = new List<SavedAgent>();
            agentAreas = new List<SavedAgentArea>();
            managers = new List<SavedAgent>();
            managerAreas = new List<SavedAgentArea>();

            floors.RemoveAll(floor => floor == null);
            floors.Sort((a, b) => a.index.CompareTo(b.index));

            foreach (var floor in floors)
            {
                floor.EnsureLists();
            }

            foreach (var floor in floors)
            {
                IntoBlockAreas(floor, floor.agentAreas, SnapshotRole.Agent);
                IntoBlockAreas(floor, floor.managerAreas, SnapshotRole.Manager);
            }

            return this;
        }

        /// <summary>
        /// Turns the areas painted for each agent, before block areas, into block areas given to
        /// that agent, and empties the old list.
        /// </summary>
        /// <remarks>
        /// One area per agent now, so where an agent had one on two floors, the first keeps the
        /// assignment and the other stays as an area of its own rather than being thrown away. The
        /// name is the owner's as it was written down; a world read from the database has none,
        /// and the world service names those after their owner on load.
        /// </remarks>
        void IntoBlockAreas(SavedFloor floor, List<SavedAgentArea> old, SnapshotRole role)
        {
            foreach (var painted in old)
            {
                if (painted == null || painted.area == null || painted.area.Count == 0)
                {
                    continue;
                }

                var area = new SavedBlockArea
                {
                    id = NewAreaId(),
                    name = painted.name,
                    area = new List<Vector3>(painted.area),
                    areaCellSize = painted.areaCellSize > 0f ? painted.areaCellSize : 0.25f,
                };

                floor.blockAreas.Add(area);

                if (AssignmentOf(role, painted.credentialId) == null)
                {
                    assignments.Add(new SavedAssignment
                    {
                        role = role,
                        credentialId = painted.credentialId,
                        areaId = area.id,
                    });
                }
            }

            old.Clear();
        }

        /// <summary>The assignment of one set of credentials, or null when they have no area.</summary>
        public SavedAssignment AssignmentOf(SnapshotRole role, int credentialId)
        {
            if (assignments == null)
            {
                return null;
            }

            foreach (var given in assignments)
            {
                if (given != null && given.role == role && given.credentialId == credentialId)
                {
                    return given;
                }
            }

            return null;
        }

        /// <summary>The block area with this id, on whichever floor, or null.</summary>
        public SavedBlockArea FindArea(int id)
        {
            if (floors == null)
            {
                return null;
            }

            foreach (var floor in floors)
            {
                if (floor == null || floor.blockAreas == null)
                {
                    continue;
                }

                foreach (var area in floor.blockAreas)
                {
                    if (area != null && area.id == id)
                    {
                        return area;
                    }
                }
            }

            return null;
        }

        /// <summary>An id no area in this world has: a random one, so a deleted area's is never handed out again.</summary>
        public int NewAreaId()
        {
            while (true)
            {
                var id = Guid.NewGuid().GetHashCode() & 0x7fffffff;

                if (id != 0 && !HasAreaId(id))
                {
                    return id;
                }
            }
        }

        bool HasAreaId(int id)
        {
            foreach (var floor in floors)
            {
                if (floor == null || floor.blockAreas == null)
                {
                    continue;
                }

                foreach (var area in floor.blockAreas)
                {
                    if (area != null && area.id == id)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>The floor with this index, added if the world does not have it yet.</summary>
        public SavedFloor FloorAt(int index)
        {
            if (floors == null)
            {
                floors = new List<SavedFloor>();
            }

            foreach (var floor in floors)
            {
                if (floor != null && floor.index == index)
                {
                    return floor;
                }
            }

            var added = new SavedFloor { index = index };
            floors.Add(added);
            floors.Sort((a, b) => a.index.CompareTo(b.index));

            return added;
        }

        /// <summary>Pieces on every floor.</summary>
        public int PieceCount() => SumOver(floor => Count(floor.pieces)) + Count(pieces);

        /// <summary>The player's agents on every floor. The manager is not one of them.</summary>
        public int AgentCount() => SumOver(floor => Count(floor.agents)) + Count(agents);

        /// <summary>Managers standing anywhere in the world: 0 or 1.</summary>
        public int ManagerCount() => SumOver(floor => Count(floor.managers)) + Count(managers);

        int SumOver(Func<SavedFloor, int> count)
        {
            var total = 0;

            if (floors != null)
            {
                foreach (var floor in floors)
                {
                    if (floor != null)
                    {
                        total += count(floor);
                    }
                }
            }

            return total;
        }

        static int Count<T>(List<T> list) => list == null ? 0 : list.Count;

        /// <summary>
        /// One floor and everything standing on it. Every position is relative to the floor.
        /// </summary>
        [Serializable]
        public sealed class SavedFloor
        {
            /// <summary>Which floor, from 0, the ground floor.</summary>
            public int index;

            /// <summary>Every piece on this floor, in no particular order.</summary>
            public List<SavedPiece> pieces = new List<SavedPiece>();

            /// <summary>Every agent the player has put down on this floor, and where they stand.</summary>
            public List<SavedAgent> agents = new List<SavedAgent>();

            /// <summary>
            /// The patch of this floor each agent may roam, one entry per set of credentials.
            /// </summary>
            /// <remarks>
            /// Beside the agents rather than inside each one: an area belongs to the credentials,
            /// so two copies of one agent on this floor share it, and an area painted before anyone
            /// is put down still survives a save.
            /// </remarks>
            public List<SavedAgentArea> agentAreas = new List<SavedAgentArea>();

            /// <summary>
            /// The manager, if they are on this floor. A list of at most one.
            /// </summary>
            /// <remarks>
            /// A list rather than a single field because <c>JsonUtility</c> has no null: a lone
            /// <see cref="SavedAgent"/> would be written as a default-constructed one whether or
            /// not anybody had been placed, and a load could not tell "no manager here" from "a
            /// manager at the origin with no credentials". An empty list says it plainly.
            /// </remarks>
            public List<SavedAgent> managers = new List<SavedAgent>();

            /// <summary>The manager's area on this floor, one entry per set of credentials.</summary>
            public List<SavedAgentArea> managerAreas = new List<SavedAgentArea>();

            /// <summary>The named areas painted on this floor, given to agents or to nobody.</summary>
            public List<SavedBlockArea> blockAreas = new List<SavedBlockArea>();

            internal void EnsureLists()
            {
                if (pieces == null) pieces = new List<SavedPiece>();
                if (agents == null) agents = new List<SavedAgent>();
                if (agentAreas == null) agentAreas = new List<SavedAgentArea>();
                if (managers == null) managers = new List<SavedAgent>();
                if (managerAreas == null) managerAreas = new List<SavedAgentArea>();
                if (blockAreas == null) blockAreas = new List<SavedBlockArea>();
            }
        }

        /// <summary>A named patch of one floor.</summary>
        [Serializable]
        public sealed class SavedBlockArea
        {
            /// <summary>Unique within the world and never reused, so an assignment cannot drift onto another area.</summary>
            public int id;

            /// <summary>What the player called it. The elevator will list it by this.</summary>
            public string name;

            /// <summary>Middle of every painted cell, relative to its floor.</summary>
            public List<Vector3> area = new List<Vector3>();

            /// <summary>Width of those cells in metres.</summary>
            public float areaCellSize = 0.25f;
        }

        /// <summary>Which block area one set of credentials uses.</summary>
        [Serializable]
        public sealed class SavedAssignment
        {
            public SnapshotRole role;
            public int credentialId;
            public int areaId;
        }

        /// <summary>Something that joins floors, and where it stands. It is on every floor at once.</summary>
        [Serializable]
        public sealed class SavedConnector
        {
            /// <summary>"elevator". Stairs, later, are another kind.</summary>
            public string kind;

            /// <summary><c>&lt;pack&gt;/&lt;name&gt;</c>, like a piece's resource.</summary>
            public string address;

            public float x;
            public float z;
            public int rotationSteps;
        }

        /// <summary>One piece of furniture or ground, and where it stands.</summary>
        [Serializable]
        public sealed class SavedPiece
        {
            /// <summary>Asset GUID of the prefab. The durable address.</summary>
            public string prefabId;

            /// <summary>Resources path of the prefab. The one a running game can load.</summary>
            public string resource;

            /// <summary>Only ever read by a person opening the file.</summary>
            public string name;

            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;

            /// <summary>
            /// Quarter turns, 0-3. Saved alongside the rotation rather than derived from it: the
            /// grid works in whole quarter turns and a footprint swap has to be exact, which is not
            /// something to recover from a quaternion by inspection.
            /// </summary>
            public int rotationSteps;

            /// <summary>Whether this piece is ground. Restored rather than re-derived.</summary>
            public bool isFloor;
        }

        /// <summary>One agent standing in the world, and where.</summary>
        /// <remarks>
        /// Only the identity is stored, not the prefab. An agent is not a catalogue entry the way
        /// furniture is — which is which is decided by the credentials file, and the model they all
        /// wear is one authored choice on the scene's world settings. So a save records who was
        /// standing where, and the load asks the assembler for the current model.
        /// </remarks>
        [Serializable]
        public sealed class SavedAgent
        {
            /// <summary>Id of the credentials this agent was placed from. The durable link.</summary>
            public int credentialId;

            /// <summary>Only ever read by a person opening the file.</summary>
            public string name;

            /// <summary>Also only for the reader — the credentials are the authority.</summary>
            public string workId;

            public Vector3 position;
            public Quaternion rotation;

            /// <summary>
            /// Quarter turns, 0-3. Saved alongside the rotation for the same reason a piece's is:
            /// the grid works in whole quarter turns and that is not something to recover from a
            /// quaternion by inspection.
            /// </summary>
            public int rotationSteps;
        }

        /// <summary>The floor one set of credentials may roam.</summary>
        [Serializable]
        public sealed class SavedAgentArea
        {
            public int credentialId;

            /// <summary>Only for the reader.</summary>
            public string name;

            /// <summary>Middle of every painted cell, relative to its floor (before floors: the ground).</summary>
            public List<Vector3> area = new List<Vector3>();

            /// <summary>Width of those cells in metres, so the area draws at the size it was painted.</summary>
            public float areaCellSize = 0.25f;
        }

        /// <summary>The bake settings of one navigation surface.</summary>
        [Serializable]
        public sealed class SavedSurface
        {
            /// <summary>Which agent type this surface is for.</summary>
            public int agentTypeId;

            public float agentRadius;
            public float agentHeight;
            public float agentSlope;
            public float agentClimb;

            public float voxelSize;
            public int tileSize;
            public float minRegionArea;

            /// <summary>What the mesh covered when it was baked. Diagnostic; the rebuild re-derives it.</summary>
            public Vector3 boundsCentre;

            public Vector3 boundsSize;
        }
    }
}
