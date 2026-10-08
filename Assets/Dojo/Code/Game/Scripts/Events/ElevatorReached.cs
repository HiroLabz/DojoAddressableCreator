using Dojo.Game.Placement;

namespace Dojo.Game.Events
{
    /// <summary>
    /// Raised when the manager has walked up to an elevator in Play - his walk ended against it, at
    /// its doors or beside it, on his floor. The floors it goes to can be offered now.
    /// </summary>
    /// <remarks>
    /// Not raised for a walk that rode an elevator on the way: stepping out of the car is arriving
    /// from one, not walking up to it. Nor for the walks he takes by himself, or to an agent or a
    /// seat. The elevator panel answers this by opening, and sends him once a floor is picked.
    /// </remarks>
    public readonly struct ElevatorReached
    {
        /// <summary>The stop he reached: his floor's car of the shaft he would ride.</summary>
        public readonly FurniturePiece Stop;

        public ElevatorReached(FurniturePiece stop)
        {
            Stop = stop;
        }

        public override string ToString() => $"ElevatorReached({(Stop != null ? Stop.name : "none")})";
    }
}
