namespace Dojo.Game.Events
{
    /// <summary>
    /// Raised when the built world has been altered and what is on disk is now out of date.
    /// </summary>
    /// <remarks>
    /// Carries a reason rather than what changed, and deliberately nothing that could be acted on:
    /// every listener wants the same single fact — the world moved — and handing over the piece
    /// would invite somebody to try saving only that. A world save is whole-world, so a partial
    /// description would be a lie about what is possible.
    /// <para>
    /// Published by the places that commit a change, not by the things that changed. A piece does
    /// not know whether it was moved by the player or rebuilt by a world load, and only the
    /// caller does — which is why the loader can rebuild a whole world without a single one of
    /// these going out, and why an autosave cannot be triggered by the act of loading.
    /// </para>
    /// </remarks>
    public readonly struct WorldChanged
    {
        /// <summary>Short phrase for the log: "committed", "deleted", "area painted".</summary>
        public readonly string Reason;

        public WorldChanged(string reason)
        {
            Reason = reason;
        }

        public override string ToString() => $"WorldChanged({Reason})";
    }
}
