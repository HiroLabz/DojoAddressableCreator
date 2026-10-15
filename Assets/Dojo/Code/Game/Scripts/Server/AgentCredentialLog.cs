using System.Collections.Generic;
using System.Text;
using Dojo.Game.InGame.Agents;
using UnityEngine;

namespace Dojo.Game.Server
{
    /// <summary>
    /// Prints every agent credential the game is holding, under one searchable prefix.
    /// </summary>
    /// <remarks>
    /// One log line per agent plus a header, all prefixed <see cref="Prefix"/>, so typing that into
    /// the Unity console's search box shows the whole roster and nothing else. A single multi-line
    /// entry would have been fewer lines but worse: the console collapses long messages, and the
    /// interesting field is usually the one that got cut.
    /// <para>
    /// Every field is printed, including the ones that are empty. A blank <c>workId</c> or a
    /// missing <c>hiroAgentId</c> is the most likely reason something is not behaving, and a dump
    /// that hides absent values is a dump that hides the answer.
    /// </para>
    /// </remarks>
    public static class AgentCredentialLog
    {
        /// <summary>
        /// The searchable prefix. Type this into the console filter to see the whole roster.
        /// </summary>
        public const string Prefix = "[AGENTDUMP]";

        /// <summary>Prints the roster, or says plainly that there is nothing in it.</summary>
        /// <param name="agents">Whatever the registry is holding.</param>
        /// <param name="source">Where it came from, so the dump says whether it is live.</param>
        /// <param name="context">Optional object the log lines select in the hierarchy.</param>
        public static void Dump(
            IReadOnlyList<AgentCredential> agents,
            AgentSource source,
            Object context = null)
        {
            var count = agents == null ? 0 : agents.Count;

            Debug.Log(Prefix + " ===== " + count + " agent credential(s), source=" + source
                + " =====", context);

            if (count == 0)
            {
                Debug.LogWarning(Prefix + " nothing loaded. With no API key the local backup is "
                    + "used; if that is empty too, check Assets/Dojo/Content/Agents/agents.json.",
                    context);
                return;
            }

            for (var i = 0; i < count; i++)
            {
                var agent = agents[i];

                if (agent == null)
                {
                    Debug.LogWarning(Prefix + " [" + i + "] null entry in the roster.", context);
                    continue;
                }

                Debug.Log(Prefix + " " + Describe(agent), context);
            }

            Debug.Log(Prefix + " ===== end =====", context);
        }

        /// <summary>
        /// One agent on one line, every field named.
        /// </summary>
        /// <remarks>
        /// Fields are labelled rather than positional. A positional dump is shorter and unreadable
        /// six months later, when nobody remembers whether the fourth column was the department or
        /// the type.
        /// </remarks>
        public static string Describe(AgentCredential agent)
        {
            var line = new StringBuilder();

            line.Append("id=").Append(agent.id);
            line.Append(" name='").Append(Or(agent.name)).Append('\'');
            line.Append(" workId='").Append(Or(agent.workId)).Append('\'');

            // The three platform descriptors that replaced the old local type field.
            line.Append(" origin=").Append(Or(agent.origin)).Append("(").Append(agent.Origin).Append(")");
            line.Append(" status=").Append(Or(agent.status)).Append("(").Append(agent.Status).Append(")");
            line.Append(" isolation=").Append(Or(agent.isolationMode))
                .Append("(").Append(agent.IsolationMode).Append(")");

            // The platform link, and the reason a chat either works or refuses to open.
            line.Append(" hiroAgentId=").Append(agent.HasPlatformAgent ? agent.hiroAgentId : "<none>");
            line.Append(" hiroSlug='").Append(Or(agent.hiroSlug)).Append('\'');

            // Areas are local-only and survive every refresh; the count is how you confirm that.
            line.Append(" areaCells=").Append(agent.AreaCellCount);
            line.Append(" areaCellSize=").Append(agent.areaCellSize.ToString("0.###"));

            line.Append(" description='").Append(Or(agent.description)).Append('\'');

            return line.ToString();
        }

        /// <summary>An explicit marker for an empty value, so a blank is not mistaken for a space.</summary>
        static string Or(string text) => string.IsNullOrEmpty(text) ? "<empty>" : text;
    }
}
