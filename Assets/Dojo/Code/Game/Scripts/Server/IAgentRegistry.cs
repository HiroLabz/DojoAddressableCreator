using System.Collections.Generic;
using System.Threading;
using Dojo.Game.InGame.Agents;
using UnityEngine;

namespace Dojo.Game.Server
{
    /// <summary>Where the agents the game is showing came from.</summary>
    public enum AgentSource
    {
        /// <summary>Nothing loaded yet.</summary>
        None,

        /// <summary>The local credentials file, because no refresh has been attempted yet.</summary>
        Backup,

        /// <summary>The platform, reconciled over the local file.</summary>
        Platform,

        /// <summary>
        /// The platform could not be read, and the local file is what is being shown.
        /// </summary>
        /// <remarks>
        /// Distinct from <see cref="Backup"/>, which means nobody has asked yet. This one means
        /// somebody asked and was refused, and it is what the roster readiness gate fails on —
        /// see <see cref="LastError"/> for the reason to put in front of the player.
        /// </remarks>
        Failed,
    }

    /// <summary>
    /// The agents, as the rest of the game sees them. One source of truth, held in memory.
    /// </summary>
    /// <remarks>
    /// <b>Nothing in the game reads the credentials file any more.</b> The inventory, the chat, the
    /// placement code and the world loader all come here, and this decides where the data actually
    /// came from — the platform when it can be reached, the local file when it cannot. That is the
    /// whole point: a class that reads JSON directly is a class that has to be rewritten the day
    /// the data moves, and it moved.
    /// <para>
    /// The DTO handed out is <see cref="AgentCredential"/>, unchanged. It already carried the name,
    /// department and painted area; it now also carries <c>hiroAgentId</c>, so one object answers
    /// both "what do I show" and "who do I ask". Callers get the live instances rather than copies,
    /// which is what lets the agents panel paint an area onto one and have the change be real.
    /// </para>
    /// <para>
    /// <b>Painted areas are local and stay local.</b> The platform has no concept of a patch of
    /// floor, so an area can only ever live in the file — it is keyed by the local <c>id</c>, it is
    /// re-attached after every refresh, and it is the one thing a refresh must never lose. See
    /// <see cref="RefreshAsync"/>.
    /// </para>
    /// </remarks>
    public interface IAgentRegistry
    {
        /// <summary>Every agent, in roster order. Never null; empty before the first load.</summary>
        IReadOnlyList<AgentCredential> Agents { get; }

        /// <summary>Where the current contents came from.</summary>
        AgentSource Source { get; }

        /// <summary>
        /// Why the last refresh failed, or null when it did not.
        /// </summary>
        /// <remarks>
        /// Only ever set alongside <see cref="AgentSource.Failed"/>. It exists so the startup
        /// class can tell the player what went wrong without reaching past this interface into the
        /// transport — the roster is a required gate now, and a gate that fails silently is worse
        /// than one that does not exist.
        /// </remarks>
        string LastError { get; }

        /// <summary>
        /// The HTTP status of the last failed refresh, or 0 when it did not fail or never reached the
        /// server. 401 means the platform does not know the signed-in account.
        /// </summary>
        long LastStatus { get; }

        /// <summary>The agent with that local id, or null.</summary>
        AgentCredential Find(int id);

        /// <summary>
        /// Records one agent's painted area and persists it.
        /// </summary>
        /// <remarks>
        /// Goes to the local file, because that is the only place it can go. Kept on the interface
        /// rather than left to callers so that no part of the game needs to know which file, or
        /// that there is a file at all.
        /// </remarks>
        void SaveArea(AgentCredential agent);

        /// <summary>Reloads from the platform, keeping every local area.</summary>
        Awaitable RefreshAsync(CancellationToken token);
    }
}
