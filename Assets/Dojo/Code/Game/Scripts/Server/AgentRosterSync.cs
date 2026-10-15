using System;
using System.Collections.Generic;
using System.Text;
using Dojo.Game.InGame.Agents;
using Dojo.Hiro;

namespace Dojo.Game.Server
{
    /// <summary>
    /// Reconciles the local agent roster against what the platform says exists.
    /// </summary>
    /// <remarks>
    /// Pure: it takes a roster and a list of remote agents and returns what changed. No file, no
    /// network, no Unity types — which is what makes the rules below something that can be read
    /// and argued with rather than inferred from a method that also does IO.
    /// <para>
    /// <b>The platform decides who exists.</b> When it answers, its list <em>is</em> the roster:
    /// a local entry with no agent behind it is a placeholder from before the platform was
    /// connected, and it is removed. That is the whole point of the sync — the client should show
    /// the agents that really exist, not those plus whatever the shipped file happened to carry.
    /// </para>
    /// <para>
    /// Removal only ever happens when the platform actually replied. An unreachable API, a missing
    /// key or an expired one produce an empty list, and an empty list changes nothing: the roster
    /// the game already has is far better than no agents at all. See the guard in
    /// <see cref="Reconcile"/>.
    /// </para>
    /// <para>
    /// A removed entry takes its painted area with it, which is why <see cref="Report.Removed"/>
    /// reports the cell count — losing player work silently would be the one unforgivable thing
    /// here, so it is at least said out loud.
    /// </para>
    /// </remarks>
    public static class AgentRosterSync
    {
        /// <summary>What one reconciliation did.</summary>
        public sealed class Report
        {
            /// <summary>Entries already linked by id, whose details were refreshed.</summary>
            public readonly List<string> Refreshed = new List<string>();

            /// <summary>Entries linked to a platform agent for the first time.</summary>
            public readonly List<string> Linked = new List<string>();

            /// <summary>Platform agents that had no local entry and were added as new ones.</summary>
            public readonly List<string> Added = new List<string>();

            /// <summary>Local entries the platform has never heard of, and that were removed.</summary>
            public readonly List<string> Removed = new List<string>();

            /// <summary>True when nothing about the roster changed.</summary>
            public bool NoChange
                => Refreshed.Count == 0 && Linked.Count == 0
                && Added.Count == 0 && Removed.Count == 0;

            public override string ToString()
            {
                var text = new StringBuilder();

                text.Append(Refreshed.Count).Append(" refreshed, ");
                text.Append(Linked.Count).Append(" newly linked, ");
                text.Append(Added.Count).Append(" added");

                if (Removed.Count > 0)
                {
                    text.Append(", ").Append(Removed.Count).Append(" removed: ");
                    text.Append(string.Join(", ", Removed.ToArray()));
                }

                return text.ToString();
            }
        }

        /// <summary>
        /// Brings <paramref name="roster"/> in line with <paramref name="remote"/>, in place.
        /// </summary>
        /// <remarks>
        /// Matched in two passes, and the order is the point.
        /// <list type="number">
        /// <item><b>By platform id.</b> Once an entry carries a <c>hiroAgentId</c> that is what it
        /// is, and no amount of renaming on either side changes it.</item>
        /// <item><b>By name, exactly once.</b> Only for entries that have no id yet, and only
        /// against remote agents not already claimed. This is the bootstrap: it is how the first
        /// sync can link anything at all, and it stops mattering the moment ids are written.</item>
        /// </list>
        /// <para>
        /// <c>workId</c> and <c>type</c> are never overwritten on an existing entry, because the
        /// platform has neither field. Its payload was read field by field: <c>name</c> and
        /// <c>description</c> match ours exactly, and nothing corresponds to a department or to
        /// Agent-versus-SubAgent. The nearest candidates — <c>slug</c>, <c>origin</c>,
        /// <c>status</c>, <c>isolation_mode</c> — all mean something else, and copying one into
        /// <c>workId</c> would replace a value the game uses with something that merely looks like
        /// it. A <em>new</em> entry has no local choice to protect, so it seeds its department from
        /// the agent's first module.
        /// </para>
        /// </remarks>
        public static Report Reconcile(AgentRoster roster, IReadOnlyList<RemoteAgent> remote)
        {
            var report = new Report();

            if (roster == null)
            {
                return report;
            }

            if (roster.agents == null)
            {
                roster.agents = new List<AgentCredential>();
            }

            if (remote == null || remote.Count == 0)
            {
                // Nothing to reconcile against. Every local entry is orphaned in the sense that
                // matters least — the platform was unreachable, not empty — so nothing is said
                // about them. The caller has already logged why the list was empty.
                return report;
            }

            var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Gathered while walking the list and removed afterwards, because removing from a list
            // being iterated is how an entry gets skipped.
            var doomed = new List<AgentCredential>();

            // Pass one: entries that already know which agent they are.
            foreach (var local in roster.agents)
            {
                if (local == null || !local.HasPlatformAgent)
                {
                    continue;
                }

                var match = Find(remote, local.hiroAgentId);

                if (match == null)
                {
                    // Linked to an agent the platform no longer lists, so it has been deleted
                    // there. It goes here too — the roster mirrors the platform.
                    doomed.Add(local);
                    report.Removed.Add(Label(local) + " — no longer on the platform");
                    continue;
                }

                claimed.Add(match.Id);

                if (Apply(local, match))
                {
                    report.Refreshed.Add(local.name);
                }
            }

            // Pass two: unlinked entries, matched by name against whatever is left.
            foreach (var local in roster.agents)
            {
                if (local == null || local.HasPlatformAgent)
                {
                    continue;
                }

                var match = FindByName(remote, local.name, claimed);

                if (match == null)
                {
                    // A placeholder. Nothing on the platform answers to it, so it has no business
                    // in the client.
                    doomed.Add(local);
                    report.Removed.Add(Label(local) + " — placeholder");
                    continue;
                }

                claimed.Add(match.Id);

                local.hiroAgentId = match.Id;
                Apply(local, match);

                report.Linked.Add(local.name + " -> " + match.Name);
            }

            foreach (var gone in doomed)
            {
                roster.agents.Remove(gone);
            }

            // Whatever the platform has that nothing local claimed becomes a new entry.
            foreach (var agent in remote)
            {
                if (agent == null || string.IsNullOrEmpty(agent.Id) || claimed.Contains(agent.Id))
                {
                    continue;
                }

                var added = new AgentCredential
                {
                    id = NextId(roster),
                    name = string.IsNullOrEmpty(agent.Name) ? agent.Slug : agent.Name,
                    description = agent.Description,
                    // Filled from the slug, which is the platform's nearest thing to a
                    // department. Only for a brand new entry: an existing workId is a deliberate
                    // local choice, and Apply never overwrites one.
                    workId = DepartmentFrom(agent),

                    hiroAgentId = agent.Id,
                    hiroSlug = agent.Slug,
                    origin = agent.Origin,
                    status = agent.Status,
                    isolationMode = agent.IsolationMode,
                };

                roster.agents.Add(added);
                report.Added.Add(added.name + " (id " + added.id + ")");
            }

            return report;
        }

        /// <summary>
        /// Copies the platform's details onto a local entry, and says whether anything moved.
        /// </summary>
        /// <remarks>
        /// The comparison is what keeps a sync from rewriting the credentials file on every launch:
        /// with nothing changed there is nothing to write, and the file's timestamp — and its diff
        /// in source control — stays quiet.
        /// </remarks>
        static bool Apply(AgentCredential local, RemoteAgent remote)
        {
            var changed = false;

            if (local.name != remote.Name && !string.IsNullOrEmpty(remote.Name))
            {
                local.name = remote.Name;
                changed = true;
            }

            if (local.description != remote.Description && !string.IsNullOrEmpty(remote.Description))
            {
                local.description = remote.Description;
                changed = true;
            }

            if (local.hiroSlug != remote.Slug)
            {
                local.hiroSlug = remote.Slug;
                changed = true;
            }

            if (local.origin != remote.Origin)
            {
                local.origin = remote.Origin;
                changed = true;
            }

            if (local.status != remote.Status)
            {
                local.status = remote.Status;
                changed = true;
            }

            if (local.isolationMode != remote.IsolationMode)
            {
                local.isolationMode = remote.IsolationMode;
                changed = true;
            }

            // workId is filled in when an entry is created and left alone afterwards, so a
            // department corrected by hand survives every later sync. The one exception is an
            // entry with none at all: there is no local choice to protect, and a blank department
            // reads as a bug on the card.
            if (string.IsNullOrEmpty(local.workId))
            {
                var department = DepartmentFrom(remote);

                if (!string.IsNullOrEmpty(department))
                {
                    local.workId = department;
                    changed = true;
                }
            }

            return changed;
        }

        /// <summary>
        /// A department for an agent that has none, taken from the platform's slug.
        /// </summary>
        /// <remarks>
        /// The slug is a URL fragment — "customer-support-assistant" — so it is tidied into
        /// something that reads like the departments already in the file: hyphens become spaces,
        /// and a trailing "agent" or "assistant" is dropped, since a department is not the word
        /// "agent". What is left is the closest thing the platform offers, which is the whole
        /// reason it is used — there is no department field on a platform agent at all.
        /// <para>
        /// Falls back to the first enabled module when there is no slug, and to nothing when
        /// there is neither.
        /// </para>
        /// </remarks>
        public static string DepartmentFrom(RemoteAgent remote)
        {
            if (remote == null)
            {
                return string.Empty;
            }

            if (string.IsNullOrEmpty(remote.Slug))
            {
                return remote.PrimaryModule ?? string.Empty;
            }

            var words = remote.Slug.Replace('_', '-').Split('-');
            var kept = new List<string>();

            foreach (var word in words)
            {
                if (string.IsNullOrEmpty(word))
                {
                    continue;
                }

                // "agent" and "assistant" say what it is, not what it does.
                if (string.Equals(word, "agent", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(word, "assistant", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                kept.Add(word.ToLowerInvariant());
            }

            return kept.Count == 0
                ? (remote.PrimaryModule ?? string.Empty)
                : string.Join(" ", kept.ToArray());
        }

        /// <summary>
        /// An entry named for a log line, with its area count when it has one.
        /// </summary>
        /// <remarks>
        /// The cell count is the part that matters. A removed placeholder is uninteresting; a
        /// removed entry carrying nine hundred painted cells is something the person reading the
        /// log needs to know happened.
        /// </remarks>
        static string Label(AgentCredential local)
            => local.name + " (id " + local.id
                + (local.HasArea ? ", " + local.AreaCellCount + " painted cells lost" : string.Empty)
                + ")";

        static RemoteAgent Find(IReadOnlyList<RemoteAgent> remote, string id)
        {
            foreach (var agent in remote)
            {
                if (agent != null && string.Equals(agent.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return agent;
                }
            }

            return null;
        }

        /// <summary>
        /// A remote agent whose name matches, ignoring case and a trailing "Agent".
        /// </summary>
        /// <remarks>
        /// The suffix is trimmed because the platform's display names carry it — "Spelling Check
        /// Agent" — while a roster of things already called agents would not repeat it. Without
        /// this, an entry a person would obviously call the same thing fails to link and gets
        /// duplicated instead.
        /// </remarks>
        static RemoteAgent FindByName(
            IReadOnlyList<RemoteAgent> remote, string name, HashSet<string> claimed)
        {
            var wanted = Normalise(name);

            if (string.IsNullOrEmpty(wanted))
            {
                return null;
            }

            foreach (var agent in remote)
            {
                if (agent == null || string.IsNullOrEmpty(agent.Id) || claimed.Contains(agent.Id))
                {
                    continue;
                }

                if (Normalise(agent.Name) == wanted || Normalise(agent.Slug) == wanted)
                {
                    return agent;
                }
            }

            return null;
        }

        static string Normalise(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var trimmed = text.Trim();

            if (trimmed.EndsWith(" Agent", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(0, trimmed.Length - " Agent".Length).Trim();
            }

            return trimmed.ToLowerInvariant();
        }

        /// <summary>
        /// One past the highest id in use.
        /// </summary>
        /// <remarks>
        /// Never reuses a gap. An id freed by a deleted entry may still be what a saved area or a
        /// placed agent in a saved world is filed under, and handing it to somebody new would give
        /// them a stranger's floor to walk.
        /// </remarks>
        static int NextId(AgentRoster roster)
        {
            var highest = 0;

            foreach (var agent in roster.agents)
            {
                if (agent != null && agent.id > highest)
                {
                    highest = agent.id;
                }
            }

            return highest + 1;
        }
    }
}
