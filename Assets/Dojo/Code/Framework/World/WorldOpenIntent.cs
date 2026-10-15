namespace Dojo.Framework.World
{
    /// <summary>
    /// Carries which world the game scene should open — a named one, or none at all — from the
    /// Lobby across the scene load.
    /// </summary>
    /// <remarks>
    /// A scene load takes no arguments, so the request has to live somewhere that survives it. This
    /// is registered as a singleton on the root scope, which outlives every scene.
    /// <para>
    /// One carrier for both answers rather than two. "Start blank" and "open Studio Floor" are the
    /// same decision with different content, and splitting them into separate flags invites the
    /// state where both are set and the game scene has to guess which the player meant.
    /// </para>
    /// <para>
    /// <b>Why not simply write the stored current world.</b> There is a <c>set_current_world</c>
    /// reducer, and the game already opens whatever it points at. Using it from here would mean the
    /// Lobby rewriting saved state to express a navigation choice: backing out of a half-loaded
    /// world, or starting a blank one and changing your mind, would leave the stored pointer moved
    /// with nothing saved to justify it. The pointer should follow an actual save, not a click.
    /// </para>
    /// </remarks>
    public sealed class WorldOpenIntent
    {
        bool pending;
        string worldName = string.Empty;

        /// <summary>Whether a request has been made and not yet acted on.</summary>
        public bool IsPending => pending;

        /// <summary>Asks the next game scene to open on an empty floor.</summary>
        public void RequestBlank()
        {
            pending = true;
            worldName = string.Empty;
        }

        /// <summary>Asks the next game scene to open this saved world.</summary>
        /// <remarks>
        /// An empty or missing name is treated as a request for a blank floor rather than ignored,
        /// so a caller that loses the name cannot silently fall through to whatever was open last.
        /// </remarks>
        public void RequestWorld(string name)
        {
            pending = true;
            worldName = string.IsNullOrWhiteSpace(name) ? string.Empty : name;
        }

        /// <summary>
        /// Takes the request, if there is one. Returns true at most once per request.
        /// </summary>
        /// <param name="name">
        /// The world to open, or empty for a blank floor. Only meaningful when this returns true.
        /// </param>
        public bool Consume(out string name)
        {
            name = worldName;

            bool was = pending;
            pending = false;
            worldName = string.Empty;
            return was;
        }
    }
}
