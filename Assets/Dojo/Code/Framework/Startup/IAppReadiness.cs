using System;

namespace Dojo.Framework.Startup
{
    /// <summary>Where one startup preprocess has got to.</summary>
    public enum PreprocessState
    {
        /// <summary>Declared, not started.</summary>
        Pending,

        /// <summary>Running now.</summary>
        Running,

        /// <summary>Finished, and the thing it was responsible for is available.</summary>
        Succeeded,

        /// <summary>Finished badly. See <see cref="IAppReadiness.TryGetError"/> for why.</summary>
        Failed,
    }

    /// <summary>
    /// Tracks the preprocesses that run while the Lobby is already on screen, and answers the one
    /// question the Lobby's entry points care about: is it safe to enter a game scene yet.
    /// </summary>
    /// <remarks>
    /// Nothing blocks the Lobby — that is the startup decision this exists to serve. Content and
    /// the SpacetimeDB session run behind it, and the <c>ToLoad</c> buttons stay disabled until
    /// every <em>required</em> one has succeeded. Advisory preprocesses report their outcome the
    /// same way but never hold the gate shut: the agent roster already falls back to a local file,
    /// so a platform outage should cost you the live roster, not the game.
    /// <para>
    /// Names are plain strings rather than an enum deliberately. The set of preprocesses differs
    /// between this assembly and whatever declares them, and an enum here would mean Dojo.Framework
    /// having to know that SpacetimeDB exists.
    /// </para>
    /// </remarks>
    public interface IAppReadiness
    {
        /// <summary>
        /// True when every declared required preprocess has succeeded. Vacuously true before
        /// anything is declared, which is safe because declaration happens synchronously while the
        /// container is built — long before any UI exists to ask.
        /// </summary>
        bool AllRequiredSucceeded { get; }

        /// <summary>
        /// Fires on any state change. Read <see cref="AllRequiredSucceeded"/> on subscribe as well,
        /// so a listener that joins late is not stuck waiting for an edge that already passed.
        /// </summary>
        event Action Changed;

        /// <summary>
        /// Registers a preprocess as <see cref="PreprocessState.Pending"/>. Declaring the same name
        /// twice is ignored rather than an error, so a scene reload cannot double-register.
        /// </summary>
        void Declare(string name, bool required);

        /// <summary>Moves a declared preprocess to <see cref="PreprocessState.Running"/>.</summary>
        void MarkRunning(string name);

        /// <summary>Moves a declared preprocess to <see cref="PreprocessState.Succeeded"/>.</summary>
        void MarkSucceeded(string name);

        /// <summary>
        /// Moves a declared preprocess to <see cref="PreprocessState.Failed"/> and records why, for
        /// the error popup to show.
        /// </summary>
        void MarkFailed(string name, string error);

        /// <summary>
        /// State of one preprocess, or <see cref="PreprocessState.Pending"/> if it was never
        /// declared — an undeclared name has not run, which is what Pending means.
        /// </summary>
        PreprocessState StateOf(string name);

        /// <summary>The failure reason, when this preprocess is in <see cref="PreprocessState.Failed"/>.</summary>
        bool TryGetError(string name, out string error);
    }
}
