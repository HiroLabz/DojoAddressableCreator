using System.Collections.Generic;
using System.Threading;
using Dojo.Framework.Inventory;
using UnityEngine;

namespace Dojo.Framework.Content
{
    /// <summary>
    /// Tells the backend what the packs this player just downloaded contain.
    /// </summary>
    /// <remarks>
    /// The manifest arrives inside the pack, so the client holding it is the first thing in the
    /// system that knows what the pack contains. Publishing from here rather than from an editor
    /// tool means there is no publish step anyone can forget to run — the catalogue is registered
    /// by the act of a player loading the content.
    /// <para>
    /// An interface in <c>Dojo.Framework</c> with its implementation in <c>Dojo.Spacetime</c>, for
    /// the same reason <see cref="IContentService"/> is: this is the only line that knows the
    /// backend is SpacetimeDB, and <c>Dojo.Game</c> does not reference the SDK at all.
    /// </para>
    /// <para>
    /// <b>Advisory, never blocking.</b> A failure here means the server's picture of the catalogue
    /// is behind, which nothing in a single-player session depends on. Startup must not gate on it
    /// — a player whose Lobby stayed shut because a catalogue upload timed out would be looking at
    /// a broken game for a reason that does not affect them.
    /// </para>
    /// </remarks>
    public interface ICatalogPublisher
    {
        /// <summary>
        /// Registers every item in <paramref name="items"/>, grouped by the pack that ships it.
        /// </summary>
        /// <remarks>
        /// Takes the merged catalogue rather than one pack at a time, because that is what
        /// <see cref="IItemCatalog"/> holds after a preload and splitting it is this method's job
        /// rather than every caller's.
        /// </remarks>
        Awaitable PublishAsync(
            IReadOnlyList<ItemDefinition> items,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The publisher used when there is no backend configured.
    /// </summary>
    /// <remarks>
    /// Registered instead of a null, so <c>AppStartup</c> has no branch for "is there a backend"
    /// and the no-backend path is exercised by exactly the same code as the real one.
    /// </remarks>
    public sealed class NullCatalogPublisher : ICatalogPublisher
    {
        public async Awaitable PublishAsync(
            IReadOnlyList<ItemDefinition> items,
            CancellationToken cancellationToken = default)
        {
            await Awaitable.NextFrameAsync(cancellationToken);
        }
    }
}
