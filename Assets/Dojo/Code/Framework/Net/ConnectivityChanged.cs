namespace Dojo.Framework.Net
{
    /// <summary>
    /// The game went online or offline. Published on the event hub by <see cref="NetworkManager"/>.
    /// </summary>
    /// <remarks>
    /// Published sticky, so subscribe with <c>SubscribeSticky</c>: a screen that opens long after
    /// the connection dropped is handed the current state straight away rather than waiting for
    /// the next change, which may never come.
    /// </remarks>
    public readonly struct ConnectivityChanged
    {
        /// <summary>What the connection is now.</summary>
        public readonly ConnectionState State;

        /// <summary>What it was before this change.</summary>
        public readonly ConnectionState Previous;

        public ConnectivityChanged(ConnectionState state, ConnectionState previous)
        {
            State = state;
            Previous = previous;
        }

        /// <summary>Shorthand for the question nearly every subscriber asks.</summary>
        public bool IsOnline => State == ConnectionState.Online;

        public override string ToString() => $"ConnectivityChanged({Previous} -> {State})";
    }
}
