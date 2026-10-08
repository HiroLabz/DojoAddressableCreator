using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Dojo.Game.InGame.Agents;
using Dojo.Hiro;
using UnityEngine;

namespace Dojo.Game.Server
{
    /// <summary>
    /// Holds the agents in memory: from the platform, and only once it has answered, with every
    /// painted area put back on top.
    /// </summary>
    /// <remarks>
    /// An ordinary object registered once on the root scope, so every scene shares the same list
    /// and a refresh is seen by all of them. It reads no transform and runs nothing per frame.
    /// <para>
    /// Nothing is shown before the platform's payload arrives. The platform decides who exists
    /// and how many there are; the shipped catalogue is deliberately empty and is never read
    /// here. Until the first successful refresh the list is empty, and the Lobby is gated on that
    /// refresh anyway (<c>RosterPreprocess</c>), so nobody sees the empty list.
    /// </para>
    /// </remarks>
    public sealed class AgentRegistry : IAgentRegistry
    {
        static readonly List<AgentCredential> None = new List<AgentCredential>();

        readonly IAgentDirectory directory;
        readonly AgentRosterFile file;

        /// <summary>Null until the platform has answered once.</summary>
        AgentRoster roster;

        public AgentSource Source { get; private set; } = AgentSource.None;

        /// <inheritdoc />
        public string LastError { get; private set; }

        /// <inheritdoc />
        public long LastStatus { get; private set; }

        public AgentRegistry(IAgentDirectory directory, AgentRosterFile file)
        {
            this.directory = directory;
            this.file = file;
        }

        /// <summary>
        /// The agents the platform returned, or an empty list before it has answered.
        /// </summary>
        public IReadOnlyList<AgentCredential> Agents => roster != null ? roster.agents : None;

        public AgentCredential Find(int id)
        {
            foreach (var agent in Agents)
            {
                if (agent != null && agent.id == id)
                {
                    return agent;
                }
            }

            return null;
        }

        public void SaveArea(AgentCredential agent)
        {
            if (agent == null || file == null)
            {
                return;
            }

            file.SaveArea(agent);
        }

        /// <summary>
        /// Replaces the credentials with the platform's, and puts every local area back.
        /// </summary>
        /// <remarks>
        /// The order is the whole of it.
        /// <list type="number">
        /// <item>The platform is asked first, before any roster is read. A failed call — no token,
        /// no network, an expired token — leaves the registry as it was and the source reported as
        /// <see cref="AgentSource.Failed"/>.</item>
        /// <item>Only then is a baseline chosen: the current roster on a later refresh, or on the
        /// first one the saved overlay, which holds a full copy of every painted agent. The shipped
        /// catalogue is not read — the platform says who exists.</item>
        /// <item>The two are reconciled by <see cref="AgentRosterSync"/>, which updates names and
        /// descriptions and links ids but never touches an <c>area</c>.</item>
        /// <item>The areas are re-attached from the saved overlay <b>by local id</b>, because
        /// reconciliation can add entries and renumber nothing — but a caller may have been handed
        /// an instance in between, and this is the point at which every area is provably back.</item>
        /// </list>
        /// <para>
        /// Areas are then written back, so an entry that gained a platform id keeps its area filed
        /// under the same id next launch.
        /// </para>
        /// </remarks>
        public async Awaitable RefreshAsync(CancellationToken token)
        {
            LastStatus = 0;

            if (directory == null)
            {
                Source = AgentSource.Failed;
                LastError = "There is no agent directory to read the roster from.";
                return;
            }

            var remote = await directory.ListAsync(token);

            if (!remote.Ok)
            {
                // The registry stands, untouched. Reconcile is deliberately NOT run here: it
                // deletes every entry the platform did not return and persists that, so running it
                // on a call that never got an answer would destroy the roster and every painted
                // area on the strength of a network error.
                Source = AgentSource.Failed;
                LastError = remote.Error;
                LastStatus = remote.Status;
                return;
            }

            // The payload as it arrived, before reconciliation rewrites it into local ids. This is
            // the only place the platform's own fields are still visible, so it is where they get
            // printed — after this point every id is the roster's, not hiro-ai-api's.
            Debug.Log("[Agents] /v1/agents returned " + remote.Count + " agent(s):"
                + DescribeAgents(remote.Agents));

            // Painted areas are the one thing here that cannot be got back: the platform has no
            // concept of a patch of floor, so an area exists only in the local file, and a
            // reconcile that drops the entry holding it destroys it for good.
            //
            // This refuses rather than asks. A roster answer that would orphan painted work is far
            // more likely to mean the credential is pointing at the wrong tenant than that somebody
            // deleted those agents on the platform — the failure this exists to stop cost three
            // areas, 1114 cells, on a token bound to a tenant holding one unrelated agent.
            // Deliberately the saved overlay, not the merged roster. The shipped catalogue carries
            // areas too, but those are seed data — they arrive in the content bundle, they belong
            // to whichever tenant the bundle was built against, and replacing them is the whole
            // point of syncing. Only what the player painted and this game saved is irreplaceable.
            var endangered = AreasOrphanedBy(file == null ? null : file.ReadSaved(), remote.Agents);

            if (endangered.Count > 0)
            {
                Source = AgentSource.Failed;
                LastError = "The platform's roster would delete " + endangered.Count
                    + " painted area(s): " + string.Join(", ", endangered.ToArray())
                    + ". Nothing was changed. This usually means the signed-in account is bound to "
                    + "a different tenant than the one these agents came from.";

                Debug.LogError("[Agents] Refusing the platform roster: it lists "
                    + remote.Count + " agent(s) and would orphan " + endangered.Count
                    + " painted area(s) — " + string.Join(", ", endangered.ToArray())
                    + ". The local roster is untouched and nothing was written to disk.");

                return;
            }

            // A successful but empty answer is still the answer, and it reconciles like any other:
            // the roster follows the payload. With no areas at stake the guard above has already
            // let it through, so this only notes that the roster is about to empty.
            if (remote.Count == 0)
            {
                Debug.LogWarning("[Agents] The platform returned an empty roster, and no local "
                    + "entry holds a painted area, so the roster is about to empty.");
            }

            // The baseline is picked only now, after the platform has answered. It is what lets a
            // painted agent keep its local id across launches; the platform's list still decides
            // who is in it.
            var baseline = roster ?? SavedBaseline();

            var report = AgentRosterSync.Reconcile(baseline, remote.Agents);

            roster = baseline;

            ReattachAreas();

            Source = AgentSource.Platform;
            LastError = null;

            if (!report.NoChange)
            {
                Debug.Log("[Agents] Registry reconciled against the platform: " + report);
            }

            // Written every time, not only on a change. The overlay is what a later run falls back
            // to, and it has to stop carrying areas for agents that no longer exist — otherwise a
            // reused id would hand one of them to a stranger next launch.
            file.SaveAreas(roster);
        }

        /// <summary>
        /// The saved overlay as a starting roster for the first reconcile, or an empty one.
        /// </summary>
        /// <remarks>
        /// The overlay alone, not <see cref="CredentialFile.Read"/>: that merges in the shipped
        /// catalogue, which is exactly what must not be consulted before the platform answers.
        /// </remarks>
        AgentRoster SavedBaseline()
        {
            var saved = file != null ? file.ReadSaved() : null;

            if (saved == null)
            {
                saved = new AgentRoster();
            }

            if (saved.agents == null)
            {
                saved.agents = new List<AgentCredential>();
            }

            return saved;
        }

        /// <summary>
        /// Puts every saved area back onto the entry it belongs to.
        /// </summary>
        /// <remarks>
        /// Matched by local id <em>and</em> checked against the platform id, which is not belt and
        /// braces — it is the fix for a real mis-attachment. Areas are filed under the local id, and
        /// now that the platform decides who exists, ids are handed out to whichever agents it
        /// returns. A placeholder that used to be id 1 leaves an area filed under 1, and the next
        /// sync gives id 1 to a completely different agent: without this check that agent would
        /// silently inherit a stranger's floor.
        /// <para>
        /// So an overlay entry is only trusted when its platform id agrees with the live entry's.
        /// An overlay entry with no platform id at all is a placeholder's area and is dropped —
        /// there is nothing left for it to belong to.
        /// </para>
        /// </remarks>
        void ReattachAreas()
        {
            var saved = file != null ? file.ReadSaved() : null;

            if (saved == null || saved.agents == null)
            {
                return;
            }

            foreach (var stored in saved.agents)
            {
                if (stored == null || !stored.HasArea)
                {
                    continue;
                }

                var live = roster.Find(stored.id);

                if (live == null)
                {
                    Debug.Log("[Agents] Dropping a " + stored.AreaCellCount + "-cell area filed "
                        + "under id " + stored.id + " ('" + stored.name + "'): no agent with that "
                        + "id exists any more.");
                    continue;
                }

                // The id alone is not enough — see the remarks. Both sides must agree on which
                // platform agent this is, or the area belongs to somebody who has gone.
                if (!string.Equals(stored.hiroAgentId, live.hiroAgentId, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.Log("[Agents] Dropping a " + stored.AreaCellCount + "-cell area filed "
                        + "under id " + stored.id + " ('" + stored.name + "'): that id now belongs "
                        + "to '" + live.name + "', which is a different agent.");
                    continue;
                }

                live.area = stored.area;

                if (stored.areaCellSize > 0f)
                {
                    live.areaCellSize = stored.areaCellSize;
                }
            }
        }

        /// <summary>
        /// Painted areas that would be destroyed if this remote roster were reconciled in.
        /// </summary>
        /// <remarks>
        /// Mirrors <see cref="AgentRosterSync"/>'s first pass, which is the only one that removes
        /// an entry holding an area: a local entry naming a platform agent the roster no longer
        /// lists is deleted, and its area with it. The second pass only removes unlinked
        /// placeholders, whose areas <see cref="ReattachAreas"/> already drops on purpose.
        /// <para>
        /// A separate walk rather than a flag on the report, because by the time <c>Reconcile</c>
        /// has produced a report it has already mutated the roster — and the point of this is to
        /// decide <em>before</em> anything changes.
        /// </para>
        /// </remarks>
        static List<string> AreasOrphanedBy(AgentRoster roster, IReadOnlyList<RemoteAgent> remote)
        {
            var orphaned = new List<string>();

            if (roster == null || roster.agents == null)
            {
                return orphaned;
            }

            foreach (var local in roster.agents)
            {
                if (local == null || !local.HasArea || !local.HasPlatformAgent)
                {
                    continue;
                }

                var found = false;

                if (remote != null)
                {
                    foreach (var agent in remote)
                    {
                        if (agent != null
                            && string.Equals(agent.Id, local.hiroAgentId, StringComparison.OrdinalIgnoreCase))
                        {
                            found = true;
                            break;
                        }
                    }
                }

                if (!found)
                {
                    orphaned.Add("'" + local.name + "' (" + local.AreaCellCount + " cells)");
                }
            }

            return orphaned;
        }

        /// <summary>
        /// Every field the platform sent back, one agent per line.
        /// </summary>
        /// <remarks>
        /// Written out rather than left to <c>ToString</c>, which gives the name, status and id and
        /// nothing else — enough to count agents, not enough to see why one does not line up with a
        /// roster entry. Reconciling the two id schemes is the open question right below this call,
        /// so the fields that would settle it are exactly the ones worth printing.
        /// <para>
        /// One agent per line rather than one entry per agent: a single console entry is truncated
        /// past a certain length, but ten or twenty agents at a line each stays well inside it,
        /// and the whole roster reads in one place.
        /// </para>
        /// </remarks>
        static string DescribeAgents(IReadOnlyList<RemoteAgent> agents)
        {
            var text = new StringBuilder();

            foreach (var agent in agents)
            {
                if (agent == null)
                {
                    continue;
                }

                text.Append("\n    id=").Append(Or(agent.Id))
                    .Append("  name='").Append(Or(agent.Name)).Append('\'')
                    .Append("  slug=").Append(Or(agent.Slug))
                    .Append("  status=").Append(Or(agent.Status))
                    .Append("  origin=").Append(Or(agent.Origin))
                    .Append("  isolation=").Append(Or(agent.IsolationMode))
                    .Append("  modules=[")
                    .Append(agent.Modules == null ? string.Empty : string.Join(", ", agent.Modules))
                    .Append(']')
                    .Append("  description='").Append(Or(agent.Description)).Append('\'');
            }

            return text.ToString();
        }

        /// <summary>
        /// An absent field shown as a marker rather than as nothing at all.
        /// </summary>
        /// <remarks>
        /// Matches <c>AgentCredentialLog</c>'s spelling, so a field that is empty here and empty
        /// there reads the same in a console filtered on either.
        /// </remarks>
        static string Or(string value)
        {
            return string.IsNullOrEmpty(value) ? "<empty>" : value;
        }
    }
}
