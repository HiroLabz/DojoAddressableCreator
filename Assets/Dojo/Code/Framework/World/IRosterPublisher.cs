using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Dojo.Framework.World
{
    /// <summary>
    /// One agent's credentials, in terms no assembly has to know about to pass along.
    /// </summary>
    /// <remarks>
    /// Deliberately no area. An agent's roam area belongs to the room it was painted in, not to the
    /// credentials — that is <c>world_agent_area</c>, and duplicating it here would put the same
    /// fact in two places with no rule for which wins.
    /// </remarks>
    public sealed class RosterCredential
    {
        /// <summary>The local id a placed agent refers to.</summary>
        public int credentialId;

        public SnapshotRole role;
        public string name;
        public string description;
        public string workId;

        /// <summary>The platform agent this one stands for, if any.</summary>
        public string hiroAgentId;

        public string hiroSlug;
        public string origin;
        public string status;
        public string isolationMode;
    }

    /// <summary>
    /// Sends the player's agent roster to the backend.
    /// </summary>
    /// <remarks>
    /// Without this a saved world names agents it cannot describe: <c>world_agent.credential_id</c>
    /// points at a local file, so a world opened anywhere that file did not travel finds a
    /// placement whose agent has no name, no platform link and no way to be assembled.
    /// <para>
    /// Advisory, like the other publishers. A roster that fails to upload costs the server its copy
    /// and costs the player nothing.
    /// </para>
    /// </remarks>
    public interface IRosterPublisher
    {
        Awaitable PublishAsync(
            IReadOnlyList<RosterCredential> credentials,
            CancellationToken cancellationToken = default);
    }

    /// <summary>The publisher used when there is no backend.</summary>
    public sealed class NullRosterPublisher : IRosterPublisher
    {
        public async Awaitable PublishAsync(
            IReadOnlyList<RosterCredential> credentials,
            CancellationToken cancellationToken = default)
        {
            await Awaitable.NextFrameAsync(cancellationToken);
        }
    }
}
