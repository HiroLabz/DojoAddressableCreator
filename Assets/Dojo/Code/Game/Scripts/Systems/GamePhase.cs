using System;
using UnityEngine;

namespace Dojo.Game.Systems
{
    /// <summary>
    /// What the player is doing right now: arranging the room, or living in it.
    /// </summary>
    /// <remarks>
    /// Two phases and no third. Everything that behaves differently between them — whether
    /// furniture can be moved or thrown away, whether agents walk, whether the owner answers a
    /// click on the floor — reads this rather than keeping its own flag, because a second flag is
    /// a second thing to get out of step.
    /// </remarks>
    public enum GamePhase
    {
        /// <summary>The room is live: agents walk, the owner takes orders, furniture is fixed.</summary>
        Play,

        /// <summary>The room is being arranged: furniture moves, everyone else stands still.</summary>
        Edit,
    }

    /// <summary>
    /// Which phase the game is in. Read-only — nothing reachable through this can change it.
    /// </summary>
    /// <remarks>
    /// The same split the entitlement gate uses, for the same reason: many things need to ask,
    /// one thing decides. Inject this unless you are the thing doing the deciding.
    /// </remarks>
    public interface IGamePhase
    {
        /// <summary>The phase in force now.</summary>
        GamePhase Current { get; }

        /// <summary>Shorthand for the question almost every caller actually asks.</summary>
        bool IsEdit { get; }

        /// <summary>
        /// Fires after the phase has changed and everything that reacts to it has been applied.
        /// </summary>
        /// <remarks>
        /// After, not during. A subscriber that ran mid-transition could see agents halted but
        /// furniture still locked, which is a state the game is never actually in.
        /// </remarks>
        event Action<GamePhase> Changed;
    }

    /// <summary>
    /// Decides the phase, and is the only thing that can.
    /// </summary>
    /// <remarks>
    /// <see cref="IGamePhase"/> is what everything else holds, and it has no setter — so the phase
    /// cannot drift because some panel flipped it on its way past. Today the inventory drawer is
    /// the only caller of <see cref="Enter"/>: opening it means the player came to rearrange,
    /// closing it means they are done.
    /// <para>
    /// Kept in this file beside the enum and the interface rather than in one of its own. They are
    /// one small idea — what phase we are in, who may ask, who may decide — and splitting three
    /// dozen lines across three files buys nothing.
    /// </para>
    /// </remarks>
    public sealed class GamePhaseService : IGamePhase
    {
        const string LogPrefix = "[Phase]";

        public GamePhase Current { get; private set; } = GamePhase.Play;

        public bool IsEdit => Current == GamePhase.Edit;

        public event Action<GamePhase> Changed;

        /// <summary>
        /// Moves to <paramref name="phase"/>, or does nothing if it is already in force.
        /// </summary>
        /// <remarks>
        /// Idempotent on purpose. Opening a panel that is already open must not re-apply the
        /// phase: resuming an agent that was already walking would restart it mid-stride.
        /// <para>
        /// The reason string is not decoration. When the room freezes and nobody knows why, a log
        /// line naming what asked for it is the difference between a minute and an afternoon.
        /// </para>
        /// </remarks>
        public void Enter(GamePhase phase, string reason)
        {
            if (Current == phase)
            {
                return;
            }

            Current = phase;

            Debug.Log($"{LogPrefix} -> {phase} ({(string.IsNullOrWhiteSpace(reason) ? "no reason given" : reason)})");

            var handler = Changed;

            if (handler == null)
            {
                // Worth saying. A phase change nothing listens to looks identical to one that
                // worked, right up until the furniture will not move.
                Debug.LogWarning($"{LogPrefix} nothing is listening for phase changes; "
                    + "GamePhaseApplier is probably missing from this scene.");
                return;
            }

            try
            {
                handler(phase);
            }
            catch (Exception exception)
            {
                // One bad subscriber must not leave the phase half-applied for everybody else.
                Debug.LogException(exception);
            }
        }
    }
}
