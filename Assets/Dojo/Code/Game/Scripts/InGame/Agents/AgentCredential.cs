using System;
using System.Collections.Generic;
using Dojo.Hiro;
using UnityEngine;

namespace Dojo.Game.InGame.Agents
{
    /// <summary>
    /// One agent's credentials: who they are, which department they answer to, and the patch of
    /// floor they are allowed to roam.
    /// </summary>
    /// <remarks>
    /// Shaped for <see cref="JsonUtility"/> — plain fields and Unity's own vector types — the same
    /// way <c>WorldSave</c> and the thumbnail manifest are, so the whole file is one
    /// <c>FromJson</c> call with no third-party serializer in the project.
    /// <para>
    /// <see cref="type"/> is a string rather than an <see cref="AgentKind"/> field because
    /// <c>JsonUtility</c> writes an enum as its underlying integer. A credentials file is something
    /// a person opens and edits, and <c>"type": "SubAgent"</c> can be read and corrected where
    /// <c>"type": 1</c> cannot. <see cref="Kind"/> is the parsed answer.
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class AgentCredential
    {
        [Tooltip("Stable identity. What a saved area is matched back to its agent by.")]
        public int id;

        [Tooltip("Name shown to the player.")]
        public string name;

        [Tooltip("One line about what this agent does, shown on the agents panel.")]
        public string description;

        [Tooltip("Department code — purchasing, engineering, accounting, and so on.")]
        public string workId;

        /// <summary>
        /// True once the player has picked <see cref="workId"/> on the agent card, rather than it
        /// being the guess the roster sync made from the platform.
        /// </summary>
        /// <remarks>
        /// The sync never overwrites a department either way. This is what keeps a chosen one in
        /// the saved file: a guess is made again identically next launch, a choice cannot be.
        /// </remarks>
        [Tooltip("The player picked this department on the agent card. The sync only guesses one.")]
        public bool departmentChosen;

        /// <summary>
        /// What the player calls this agent's job: "Lead researcher". Empty until one is set.
        /// </summary>
        /// <remarks>
        /// Not "role": the server's <c>role</c> already means Agent or Manager. The platform has no
        /// job field at all (checked against the live <c>/v1/agents</c> schema), so the game owns it.
        /// </remarks>
        [Tooltip("The agent's job, as the player named it. Empty until one is set.")]
        public string jobTitle;

        /// <summary>
        /// This agent's id on the Hiro platform, or empty when they are local only.
        /// </summary>
        /// <remarks>
        /// The link between a roster entry and a real agent on the server, and the only durable
        /// one: the platform identifies agents by UUID, and matching on name works exactly once —
        /// the first time somebody renames either side it stops. Filled in by the startup sync
        /// and, once set, is what every later sync matches on.
        /// <para>
        /// Empty is a legitimate state, not a missing value. A roster entry with no platform agent
        /// behind it still shows on the Agents tab, still paints an area and still gets placed; it
        /// just has nobody to answer when the player talks to it, and the chat says so.
        /// </para>
        /// </remarks>
        [Tooltip("UUID of the matching agent on the Hiro platform. Filled in by the startup sync; " +
                 "empty means this agent is local only and cannot be chatted with.")]
        public string hiroAgentId;

        [Tooltip("The platform's own slug for this agent. Also what workId is filled from, since " +
                 "the platform has no department field of its own.")]
        public string hiroSlug;

        /// <summary>
        /// Where this agent came from on the platform: builder, marketplace, api.
        /// </summary>
        /// <remarks>
        /// One of three descriptors that replaced a local <c>type</c> field. That field used to say
        /// Agent, SubAgent or Manager, and nothing in the game ever branched on it — it was shown
        /// on a card and stored, unread, on every placed agent. The platform reports three real
        /// facts instead, and these are they.
        /// <para>
        /// Strings in the file with enum accessors beside them, the same trade the old field made:
        /// <c>JsonUtility</c> writes an enum as its integer, and a credentials file is something a
        /// person opens. <c>"origin": "builder"</c> can be read and corrected; <c>"origin": 1</c>
        /// cannot.
        /// </para>
        /// </remarks>
        [Tooltip("Platform origin: builder, marketplace, api.")]
        public string origin;

        [Tooltip("Platform status: draft, active, disabled, archived. Nothing is gated on it - a " +
                 "draft agent answers perfectly well.")]
        public string status;

        [Tooltip("Platform isolation mode: shared, dedicated.")]
        public string isolationMode;

        /// <summary>
        /// Middle of every cell of the block area this agent has been given, in world space, for
        /// this session only. Empty when it has none.
        /// </summary>
        /// <remarks>
        /// <b>Never saved with the credentials.</b> An area belongs to the world it was painted in
        /// - one source of truth - and is given to an agent there (<c>BlockAreas</c>). This is the
        /// copy <c>AgentAreaBinding</c> hands over while that world is open, and it is not written
        /// to any credentials file: a file that kept areas was a second, stale copy of them.
        /// </remarks>
        [NonSerialized]
        public List<Vector3> area = new List<Vector3>();

        /// <summary>Width of the cells in <see cref="area"/>, in metres. Session only, like the area.</summary>
        [NonSerialized]
        public float areaCellSize = 0.25f;

        /// <summary>
        /// The platform's latest record of this agent, for this session only: what it may do -
        /// its models, tools, approvals, guardrails and budget - for the Agent Passport. Null until
        /// the platform has answered, and for an agent it does not know.
        /// </summary>
        /// <remarks>
        /// Never saved, like the area: it is the platform's to say, and a saved copy would only go
        /// stale. Set by the roster sync.
        /// </remarks>
        [NonSerialized]
        public RemoteAgent platform;

        /// <summary>True once the player has painted somewhere for this agent to roam.</summary>
        public bool HasArea => area != null && area.Count > 0;

        /// <summary>True once the player has set a department or a job title on the agent card.</summary>
        public bool HasPlayerDetails => departmentChosen || !string.IsNullOrEmpty(jobTitle);

        /// <summary>True when this agent is linked to one on the platform and can be chatted with.</summary>
        public bool HasPlatformAgent => !string.IsNullOrEmpty(hiroAgentId);

        /// <summary>How many cells the area covers. Zero when there is no area.</summary>
        public int AreaCellCount => area == null ? 0 : area.Count;

        /// <summary><see cref="origin"/> parsed. Unrecognised text becomes Unknown.</summary>
        public AgentOrigin Origin => PlatformAgentTraits.ParseOrigin(origin);

        /// <summary><see cref="status"/> parsed. Unrecognised text becomes Unknown.</summary>
        public AgentStatus Status => PlatformAgentTraits.ParseStatus(status);

        /// <summary><see cref="isolationMode"/> parsed. Unrecognised text becomes Unknown.</summary>
        public AgentIsolation IsolationMode
            => PlatformAgentTraits.ParseIsolation(isolationMode);

        // There was a TryAreaBounds here, handing out the rectangle around the painted cells. It
        // has gone deliberately rather than been left for a caller who might want it: confining an
        // agent by that rectangle is precisely the bug that let them wander outside their area, and
        // an accessor that offers the wrong answer conveniently is an invitation to reintroduce it.
        // AgentRoutine.ConfineTo takes the cells.

        /// <summary>
        /// A copy without the department and job the player gave it in the open world.
        /// </summary>
        /// <remarks>
        /// For anything that outlives the world: the roster overlay and the shipped catalogue.
        /// Those details belong to the world they were set in and are saved with it; written
        /// anywhere else they would come back in the next world opened.
        /// </remarks>
        public AgentCredential CopyWithoutWorldDetails()
        {
            var copy = Copy();

            if (copy.departmentChosen)
            {
                // The department was the player's, not the platform's guess: the next refresh
                // fills the guess back in.
                copy.workId = string.Empty;
            }

            copy.departmentChosen = false;
            copy.jobTitle = string.Empty;
            return copy;
        }

        /// <summary>A copy, so an edit in progress cannot change the roster until it is committed.</summary>
        public AgentCredential Copy()
            => new AgentCredential
            {
                id = id,
                name = name,
                description = description,
                workId = workId,
                departmentChosen = departmentChosen,
                jobTitle = jobTitle,
                hiroAgentId = hiroAgentId,
                hiroSlug = hiroSlug,
                origin = origin,
                status = status,
                isolationMode = isolationMode,
                areaCellSize = areaCellSize,
                area = area == null ? new List<Vector3>() : new List<Vector3>(area),
                platform = platform,
            };
    }

    /// <summary>
    /// Every agent's credentials. One file holds them all, the way <c>WorldLibrary</c> holds every
    /// saved world.
    /// </summary>
    [Serializable]
    public sealed class AgentRoster
    {
        /// <summary>Bumped when the shape of this file changes.</summary>
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;

        public List<AgentCredential> agents = new List<AgentCredential>();

        /// <summary>The agent with that id, or null.</summary>
        public AgentCredential Find(int id)
        {
            if (agents == null)
            {
                return null;
            }

            foreach (var agent in agents)
            {
                if (agent != null && agent.id == id)
                {
                    return agent;
                }
            }

            return null;
        }

        /// <summary>
        /// Puts an agent in, replacing the one with the same id rather than adding a second.
        /// </summary>
        public void Put(AgentCredential agent)
        {
            if (agent == null)
            {
                return;
            }

            if (agents == null)
            {
                agents = new List<AgentCredential>();
            }

            for (var i = 0; i < agents.Count; i++)
            {
                if (agents[i] != null && agents[i].id == agent.id)
                {
                    agents[i] = agent;
                    return;
                }
            }

            agents.Add(agent);
        }
    }
}
