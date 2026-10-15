using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Dojo.Framework.Content
{
    /// <summary>
    /// What packs the backend says this player owns.
    /// </summary>
    /// <remarks>
    /// The read side of <see cref="IPackGranter"/>, and the thing that makes ownership outlive a
    /// run. Ownership is recorded against the player's identity, not against a session — so a pack
    /// bought in one run is still owned in the next, and the only question is whether anything
    /// bothers to ask.
    /// <para>
    /// This is what the sign-in payload is built from. Without it the payload can only report what
    /// was typed into an Inspector before the game shipped, which by definition cannot contain a
    /// pack the player bought afterwards.
    /// </para>
    /// <para>
    /// An interface in <c>Dojo.Framework</c> with its implementation in <c>Dojo.Spacetime</c>, the
    /// same split <see cref="ICatalogPublisher"/> and <see cref="IPackGranter"/> use.
    /// </para>
    /// </remarks>
    public interface IOwnedPackSource
    {
        /// <summary>
        /// The content keys this player owns, according to the backend.
        /// </summary>
        /// <remarks>
        /// Asynchronous because the connection has to be open before there is anything to read, and
        /// opening it is this call's job. Empty is a legitimate answer — a backend that is switched
        /// off, unreachable, or has never seen this player — and the caller treats it as "fall back
        /// to the seed" rather than as "this player owns nothing".
        /// </remarks>
        Awaitable<IReadOnlyList<string>> OwnedAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The source used when there is no backend configured.
    /// </summary>
    /// <remarks>
    /// Answers empty, which sends the caller to the seed list — the behaviour the game had before
    /// ownership was persisted anywhere. Registered rather than left null so the no-backend path is
    /// the same code as the real one.
    /// </remarks>
    public sealed class NullOwnedPackSource : IOwnedPackSource
    {
        public async Awaitable<IReadOnlyList<string>> OwnedAsync(CancellationToken cancellationToken = default)
        {
            await Awaitable.NextFrameAsync(cancellationToken);
            return new string[0];
        }
    }
}
