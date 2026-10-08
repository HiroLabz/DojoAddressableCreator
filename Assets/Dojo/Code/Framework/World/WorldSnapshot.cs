using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Dojo.Framework.World
{
    /// <summary>Whether a placed character is one of the player's agents, or the manager.</summary>
    public enum SnapshotRole
    {
        Agent,
        Manager,
    }

    /// <summary>One placed object, as the backend needs it.</summary>
    public sealed class SnapshotPiece
    {
        /// <summary><c>&lt;pack&gt;/&lt;name&gt;</c>. The durable answer to "what was this?".</summary>
        public string address;

        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;

        /// <summary>Quarter turns, 0-3, kept beside the rotation because the grid works in whole turns.</summary>
        public int rotationSteps;

        public bool isFloor;
    }

    /// <summary>One placed agent or manager.</summary>
    public sealed class SnapshotAgent
    {
        public SnapshotRole role;
        public int credentialId;
        public Vector3 position;
        public Quaternion rotation;
        public int rotationSteps;
    }

    /// <summary>The patch of floor one set of credentials may roam.</summary>
    public sealed class SnapshotArea
    {
        public SnapshotRole role;
        public int credentialId;
        public float cellSize;
        public List<Vector3> points = new List<Vector3>();
    }

    /// <summary>A named patch of one floor, which agents or the manager can be given.</summary>
    public sealed class SnapshotBlockArea
    {
        /// <summary>Unique within its world, and never reused.</summary>
        public int id;

        public string name;
        public float cellSize;

        /// <summary>Middle of every painted cell, relative to its floor.</summary>
        public List<Vector3> points = new List<Vector3>();
    }

    /// <summary>Which block area one set of credentials uses.</summary>
    public sealed class SnapshotAssignment
    {
        public SnapshotRole role;
        public int credentialId;
        public int areaId;
    }

    /// <summary>How the navigation mesh was baked, for one agent type.</summary>
    public sealed class SnapshotSurface
    {
        public int agentTypeId;
        public float agentRadius;
        public float agentHeight;
        public float agentSlope;
        public float agentClimb;
        public float voxelSize;
        public int tileSize;
        public float minRegionArea;
        public Vector3 boundsCentre;
        public Vector3 boundsSize;
    }

    /// <summary>
    /// One floor of a world and everything standing on it. Every position is relative to the floor.
    /// </summary>
    public sealed class SnapshotFloor
    {
        /// <summary>Which floor, from 0, the ground floor.</summary>
        public int index;

        public List<SnapshotPiece> pieces = new List<SnapshotPiece>();

        /// <summary>The agents and the manager on this floor; <see cref="SnapshotAgent.role"/> says which.</summary>
        public List<SnapshotAgent> agents = new List<SnapshotAgent>();

        /// <summary>
        /// Before block areas: the area painted for each set of credentials. Read, never written -
        /// a load turns each into a block area given to its owner.
        /// </summary>
        public List<SnapshotArea> areas = new List<SnapshotArea>();

        /// <summary>The named areas painted on this floor.</summary>
        public List<SnapshotBlockArea> blockAreas = new List<SnapshotBlockArea>();
    }

    /// <summary>Something that joins floors - the elevator now, stairs later - and where it stands.</summary>
    public sealed class SnapshotConnector
    {
        /// <summary>"elevator".</summary>
        public string kind;

        public string address;
        public float x;
        public float z;
        public int rotationSteps;
    }

    /// <summary>
    /// A saved world, in terms no assembly has to know about to pass along.
    /// </summary>
    /// <remarks>
    /// <c>WorldSave</c> itself lives in <c>Dojo.Game</c>, which <c>Dojo.Spacetime</c> cannot
    /// reference — the dependency runs the other way. Rather than invert it, the world is handed
    /// over as this, which is a shape both ends already agree on and neither owns.
    /// <para>
    /// World &gt; Floor &gt; objects: everything placed is inside one of <see cref="floors"/>, and
    /// what joins them is on the world. A world saved before floors existed comes back with one.
    /// </para>
    /// <para>
    /// The navmesh is deliberately absent, exactly as it is from the file: <c>NavMeshData</c> has
    /// no serialisation and is rebuilt from the pieces on load. What is kept is the settings it was
    /// baked with, so a reload reproduces the same bake.
    /// </para>
    /// </remarks>
    public sealed class WorldSnapshot
    {
        public string name;
        public List<SnapshotFloor> floors = new List<SnapshotFloor>();
        public List<SnapshotConnector> connectors = new List<SnapshotConnector>();
        public List<SnapshotSurface> navigation = new List<SnapshotSurface>();

        /// <summary>
        /// Which block area each agent's and the manager's credentials use. On the world, not a
        /// floor, because an agent and its area may be on different floors.
        /// </summary>
        public List<SnapshotAssignment> assignments = new List<SnapshotAssignment>();

        /// <summary>The floor with this index, added if the world does not have it yet.</summary>
        public SnapshotFloor FloorAt(int index)
        {
            if (floors == null)
            {
                floors = new List<SnapshotFloor>();
            }

            foreach (var floor in floors)
            {
                if (floor != null && floor.index == index)
                {
                    return floor;
                }
            }

            var added = new SnapshotFloor { index = index };
            floors.Add(added);
            floors.Sort((a, b) => a.index.CompareTo(b.index));

            return added;
        }

        /// <summary>Pieces on every floor.</summary>
        public int PieceCount()
        {
            var count = 0;

            if (floors != null)
            {
                foreach (var floor in floors)
                {
                    if (floor != null && floor.pieces != null)
                    {
                        count += floor.pieces.Count;
                    }
                }
            }

            return count;
        }

        /// <summary>
        /// How many of the player's agents stand in this world, on every floor. The manager - the
        /// player themselves - is among the agents too, and is not one of them.
        /// </summary>
        public int AgentCount()
        {
            if (floors == null)
            {
                return 0;
            }

            var count = 0;

            foreach (var floor in floors)
            {
                if (floor == null || floor.agents == null)
                {
                    continue;
                }

                foreach (var agent in floor.agents)
                {
                    if (agent != null && agent.role == SnapshotRole.Agent)
                    {
                        count++;
                    }
                }
            }

            return count;
        }
    }

    /// <summary>
    /// Sends a saved world to the backend.
    /// </summary>
    /// <remarks>
    /// <b>Advisory, never blocking.</b> The world is already on disk by the time this is called, so
    /// a failure here costs a server-side copy and nothing the player can see. Saving must never
    /// fail because a database was unreachable.
    /// </remarks>
    public interface IWorldPublisher
    {
        Awaitable PublishAsync(WorldSnapshot world, CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes a world from the backend.
        /// </summary>
        /// <remarks>
        /// Advisory in the same way as <see cref="PublishAsync"/>, and for the same reason: the
        /// world is already gone from disk by the time this runs. A failure here leaves a
        /// server-side copy of something the player has deleted, which the next save or sync
        /// settles — it must not be allowed to fail the delete they asked for.
        /// </remarks>
        Awaitable DeleteAsync(string worldName, CancellationToken cancellationToken = default);
    }

    /// <summary>The publisher used when there is no backend configured.</summary>
    /// <remarks>
    /// Registered instead of a null, so the no-backend path is the same code as the real one rather
    /// than a branch at every call site.
    /// </remarks>
    public sealed class NullWorldPublisher : IWorldPublisher
    {
        public async Awaitable DeleteAsync(string worldName, CancellationToken cancellationToken = default)
        {
            await Awaitable.NextFrameAsync(cancellationToken);
        }

        public async Awaitable PublishAsync(WorldSnapshot world, CancellationToken cancellationToken = default)
        {
            await Awaitable.NextFrameAsync(cancellationToken);
        }
    }
}
