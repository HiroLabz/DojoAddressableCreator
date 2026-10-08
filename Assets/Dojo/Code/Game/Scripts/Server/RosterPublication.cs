using System;
using System.Collections.Generic;
using Dojo.Framework.World;
using Dojo.Game.InGame.Agents;

namespace Dojo.Game.Server
{
    /// <summary>Publishes the same identities the live game registry gives to placements.</summary>
    public static class RosterPublication
    {
        public static List<RosterCredential> Create(
            IReadOnlyList<AgentCredential> agents, IReadOnlyList<AgentCredential> managers)
        {
            var result = new List<RosterCredential>();
            var ids = new HashSet<int>();
            Add(result, ids, agents, SnapshotRole.Agent);
            Add(result, ids, managers, SnapshotRole.Manager);
            return result;
        }

        static void Add(List<RosterCredential> result, HashSet<int> ids,
            IReadOnlyList<AgentCredential> source, SnapshotRole role)
        {
            if (source == null) return;
            foreach (var item in source)
            {
                if (item == null) continue;
                // The server keys by owner + integer id, not by role. Do not let
                // a manager silently overwrite an agent with the same id.
                if (!ids.Add(item.id)) throw new InvalidOperationException(
                    "Duplicate roster credential id " + item.id + "; refusing ambiguous publication.");
                result.Add(new RosterCredential {
                    credentialId = item.id, role = role, name = item.name,
                    description = item.description, workId = item.workId,
                    hiroAgentId = item.hiroAgentId, hiroSlug = item.hiroSlug,
                    origin = item.origin, status = item.status, isolationMode = item.isolationMode
                });
            }
        }
    }
}
