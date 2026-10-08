using Dojo.Game.Placement;

namespace Dojo.Game.Events
{
    /// <summary>
    /// Raised when the player tried to put something down and a rule they cannot see on screen
    /// refused it - an elevator's stop on another floor, say.
    /// </summary>
    /// <remarks>
    /// Only for refusals the red footprint cannot explain by itself. Overlapping a desk, or hanging
    /// off the floor, is plain from the footprint; a blocked spot two floors up is not.
    /// </remarks>
    public readonly struct PlacementRefused
    {
        public readonly PlacementRejection Reason;

        /// <summary>The floor the problem is on, from 0, or -1 when it is no floor in particular.</summary>
        public readonly int Storey;

        public PlacementRefused(PlacementRejection reason, int storey)
        {
            Reason = reason;
            Storey = storey;
        }

        public override string ToString() => $"PlacementRefused({Reason}, {Storey})";
    }
}
