namespace Dojo.Framework.World
{
    /// <summary>
    /// Changes which saved world is the current one — the world the game scene opens on.
    /// </summary>
    /// <remarks>
    /// Deliberately separate from <see cref="IWorldSource"/>, and deliberately this narrow. The
    /// source is read-only on purpose: it is what most of the app depends on, and nothing reachable
    /// through it can move the player's selection. This is the one door that can, so it is the one
    /// door to audit.
    /// <para>
    /// The pair mirrors <c>IEntitlements</c> / <c>IEntitlementGrants</c> — the many things that ask
    /// a question take the read interface, and only the thing making the change takes this one.
    /// </para>
    /// <para>
    /// Lives in the framework rather than beside <c>WorldService</c> because the implementation is
    /// in <c>Dojo.Spacetime</c>, which cannot reference <c>Dojo.Game</c>. The dependency runs the
    /// other way, exactly as it does for <see cref="IWorldSource"/>.
    /// </para>
    /// </remarks>
    public interface IWorldSelection
    {
        /// <summary>True when a selection can actually be recorded.</summary>
        /// <remarks>
        /// False means the backend is not up, not that the world does not exist. A caller should
        /// treat it as "do not offer the choice" rather than "the choice failed".
        /// </remarks>
        bool CanSelect { get; }

        /// <summary>
        /// Records this world as the one the game should open. Does not load anything.
        /// </summary>
        /// <remarks>
        /// Naming a world that does not exist is ignored rather than treated as an error: the list
        /// the player chose from can go stale between the choosing and the click, and refusing
        /// loudly would turn a race into a crash.
        /// </remarks>
        /// <param name="worldName">The world's name, as <see cref="IWorldSource.Names"/> reports it.</param>
        /// <returns>True when the selection was recorded.</returns>
        bool SetCurrent(string worldName);

        /// <summary>
        /// Records that no world is current: the player has started a new one, which has no name
        /// until it is first saved.
        /// </summary>
        /// <returns>True when the change was recorded.</returns>
        bool ClearCurrent();
    }

    /// <summary>The selection door used when there is no backend: it opens onto nothing.</summary>
    public sealed class NullWorldSelection : IWorldSelection
    {
        /// <inheritdoc />
        public bool CanSelect => false;

        /// <inheritdoc />
        public bool SetCurrent(string worldName) => false;

        /// <inheritdoc />
        public bool ClearCurrent() => false;
    }
}
