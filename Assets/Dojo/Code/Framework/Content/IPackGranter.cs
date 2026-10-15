using System.Threading;
using UnityEngine;

namespace Dojo.Framework.Content
{
    /// <summary>
    /// Records on the backend that this player now owns a pack.
    /// </summary>
    /// <remarks>
    /// The sibling of <see cref="ICatalogPublisher"/>, and deliberately a separate call: the
    /// publisher says <em>what a pack contains</em>, this says <em>who holds it</em>. The module
    /// writes ownership rows by reading the item definitions the publisher registered, so the
    /// order matters — grant before publish and the player owns a pack containing nothing.
    /// <para>
    /// An interface in <c>Dojo.Framework</c> with its implementation in <c>Dojo.Spacetime</c>, for
    /// the same reason <see cref="ICatalogPublisher"/> is: <c>Dojo.Game</c> does not reference the
    /// SDK, and swapping the backend stays a change to one assembly.
    /// </para>
    /// <para>
    /// <b>Advisory, never blocking.</b> A failure means the server's record of ownership is behind
    /// while the player's own client already has the content. That is the right way round to be
    /// wrong, and it is why every failure here is a warning rather than an exception.
    /// </para>
    /// </remarks>
    public interface IPackGranter
    {
        /// <summary>
        /// Writes the ownership record for <paramref name="pack"/>.
        /// </summary>
        /// <param name="pack">The content key, which is also the pack name the module knows.</param>
        /// <param name="reason">
        /// What caused the grant, kept for later reconciliation — a purchase id in the real thing,
        /// a simulation marker in the test harness.
        /// </param>
        Awaitable GrantAsync(string pack, string reason, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The granter used when there is no backend configured.
    /// </summary>
    /// <remarks>
    /// Registered instead of a null, so the acquisition path has no branch for "is there a
    /// backend" and the no-backend case runs the same code as the real one.
    /// </remarks>
    public sealed class NullPackGranter : IPackGranter
    {
        public async Awaitable GrantAsync(
            string pack,
            string reason,
            CancellationToken cancellationToken = default)
        {
            await Awaitable.NextFrameAsync(cancellationToken);
        }
    }
}
