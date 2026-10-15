namespace Dojo.Game.Events
{
    /// <summary>
    /// Raised when the player sent the manager to another floor and there is no way up or down to it.
    /// </summary>
    /// <remarks>
    /// The manager does not move: walking as far as the floor he is on allows would end nowhere the
    /// player asked for. Whoever shows messages says why instead.
    /// </remarks>
    public readonly struct FloorUnreachable
    {
        /// <summary>The floor he could not get to, from 0.</summary>
        public readonly int Storey;

        public FloorUnreachable(int storey)
        {
            Storey = storey;
        }

        public override string ToString() => $"FloorUnreachable({Storey})";
    }
}
