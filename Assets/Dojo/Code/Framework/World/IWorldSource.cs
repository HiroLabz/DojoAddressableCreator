using System;
using System.Collections.Generic;

namespace Dojo.Framework.World
{
    /// <summary>
    /// Where saved worlds are read from.
    /// </summary>
    /// <remarks>
    /// Synchronous on purpose. The backing store is a local cache kept current by a subscription,
    /// so a read costs nothing and every caller — the chooser, the startup path, autosave — can
    /// stay as it is. Making these async would have spread awaits through half the UI for a lookup
    /// that never touches the network.
    /// <para>
    /// <see cref="IsReady"/> is the honest answer to "can this be trusted yet": before the first
    /// snapshot arrives the cache is legitimately empty, and a caller must be able to tell that
    /// apart from a player who has no worlds.
    /// </para>
    /// </remarks>
    public interface IWorldSource
    {
        /// <summary>True once worlds can be read. False means "do not know", not "none".</summary>
        bool IsReady { get; }

        /// <summary>
        /// Fires when what this source would report may have changed — in particular when
        /// <see cref="IsReady"/> becomes true.
        /// </summary>
        /// <remarks>
        /// Readiness arrives later than the connection it depends on: the startup gate reports the
        /// backend connected before the subscription has delivered its first snapshot. Without this
        /// event a caller can only read <see cref="IsReady"/> at some arbitrary earlier moment, see
        /// false, and never learn otherwise — a lobby that hides the continue card from a player
        /// who has worlds.
        /// <para>
        /// Raised on the main thread, so a handler may touch the scene directly.
        /// </para>
        /// </remarks>
        event Action Changed;

        /// <summary>Every world this player has saved, in the order the store returns them.</summary>
        List<string> Names();

        /// <summary>The world the player last had open, or empty.</summary>
        string Current();

        /// <summary>One world, or null when there is no such world.</summary>
        WorldSnapshot Load(string name);
    }

    /// <summary>The source used when there is no backend.</summary>
    /// <remarks>
    /// <see cref="IsReady"/> is false rather than true-with-nothing-in-it, so callers fall back to
    /// the file instead of concluding the player has no worlds.
    /// </remarks>
    public sealed class NullWorldSource : IWorldSource
    {
        public bool IsReady => false;

        /// <summary>Never fires — this source never becomes ready and never changes.</summary>
        public event Action Changed
        {
            add { }
            remove { }
        }

        public List<string> Names() => new List<string>();

        public string Current() => string.Empty;

        public WorldSnapshot Load(string name) => null;
    }
}
