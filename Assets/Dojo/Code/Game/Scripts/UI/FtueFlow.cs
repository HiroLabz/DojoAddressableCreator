namespace Dojo.Game.UI
{
    /// <summary>Where the first-time tour is.</summary>
    /// <remarks>
    /// Every step walks the same path:
    /// <code>
    /// Starting ─► Advance ─► Watching ─► Completing ─► Advance ─► (next step's) Watching ...
    ///                           │  ▲          │
    ///                           ▼  │          ▼
    ///                       SteppedAside ─► WaitingToAdvance ─► Advance
    /// </code>
    /// The side path is for a dialog or an agent chat: the card gets out of the way, and a step
    /// finished behind one waits for it to close before the next card comes up. A card can also
    /// ask for the inventory to be shut first (<see cref="FtueStep.NeedsInventoryClosed"/>): the
    /// tour waits in WaitingToAdvance until it is. Off is where the tour ends up once it is
    /// finished or skipped, and where it stays for a player who has done it.
    /// </remarks>
    public enum FtueState
    {
        /// <summary>Not running: finished, skipped, or never needed.</summary>
        Off,

        /// <summary>Waiting for the loading screen to go and the world to settle.</summary>
        Starting,

        /// <summary>A card is up, waiting for the player to do what it asks.</summary>
        Watching,

        /// <summary>A dialog or chat is up, so the card is hidden. Its goal is still watched.</summary>
        SteppedAside,

        /// <summary>The goal is met. The card stays up a beat with NEXT on, so the player sees it land.</summary>
        Completing,

        /// <summary>The step is done, but a dialog or chat is up. The next card waits for it to close.</summary>
        WaitingToAdvance,

        /// <summary>Time for the next card, or the end. Passed straight through: the tour acts on it at once.</summary>
        Advance,
    }

    /// <summary>What the tour reads from the game each frame, to decide where it goes next.</summary>
    public readonly struct FtueSignals
    {
        public FtueSignals(bool worldReady = false, bool goalMet = false, bool blocked = false, bool beatOver = false,
            bool nextBlocked = false)
        {
            WorldReady = worldReady;
            GoalMet = goalMet;
            Blocked = blocked;
            BeatOver = beatOver;
            NextBlocked = nextBlocked;
        }

        /// <summary>The loading screen has gone and the world has had a moment to stand.</summary>
        public bool WorldReady { get; }

        /// <summary>The player has done what the step on screen asks.</summary>
        public bool GoalMet { get; }

        /// <summary>
        /// Something is in the way of this step's card: a dialog or an agent chat, or the inventory
        /// when the card needs it shut.
        /// </summary>
        public bool Blocked { get; }

        /// <summary>The same, for the card after this one - what the tour waits on before moving to it.</summary>
        public bool NextBlocked { get; }

        /// <summary>The finished step has been on screen for its beat.</summary>
        public bool BeatOver { get; }
    }

    /// <summary>The tour's one rule for moving from state to state.</summary>
    /// <remarks>
    /// Kept apart from the component so the path can be read in one place and tested without a
    /// scene. The buttons are the only other way to move: NEXT goes <see cref="Onward"/>, SKIP TOUR
    /// to Off.
    /// </remarks>
    public static class FtueFlow
    {
        /// <summary>The state after <paramref name="state"/>, given what the game says this frame.</summary>
        public static FtueState Next(FtueState state, FtueSignals signals)
        {
            switch (state)
            {
                case FtueState.Starting:
                    // Not over a dialog that happens to be up as the world opens.
                    return signals.WorldReady && !signals.Blocked ? FtueState.Advance : FtueState.Starting;

                case FtueState.Watching:
                    // The goal first, so a step finished by opening a chat is never missed.
                    if (signals.GoalMet)
                    {
                        return signals.Blocked ? FtueState.WaitingToAdvance : FtueState.Completing;
                    }

                    return signals.Blocked ? FtueState.SteppedAside : FtueState.Watching;

                case FtueState.SteppedAside:
                    // Done behind the dialog: no beat, since nobody can see the card. The next one
                    // waits for the dialog to go.
                    if (signals.GoalMet)
                    {
                        return FtueState.WaitingToAdvance;
                    }

                    // Cancelled: the same card comes back.
                    return signals.Blocked ? FtueState.SteppedAside : FtueState.Watching;

                case FtueState.Completing:
                    if (signals.Blocked)
                    {
                        return FtueState.WaitingToAdvance;
                    }

                    return signals.BeatOver ? Onward(signals.NextBlocked) : FtueState.Completing;

                case FtueState.WaitingToAdvance:
                    return Onward(signals.NextBlocked);

                default:
                    // Off stays off; Advance is the tour's to act on, not this.
                    return state;
            }
        }

        /// <summary>
        /// Leaving a finished step - by the beat running out, or by NEXT: straight on, or wait
        /// while the next card has something in its way.
        /// </summary>
        public static FtueState Onward(bool nextBlocked)
            => nextBlocked ? FtueState.WaitingToAdvance : FtueState.Advance;

        /// <summary>Whether the card is on screen in <paramref name="state"/>.</summary>
        public static bool CardVisible(FtueState state)
            => state == FtueState.Watching || state == FtueState.Completing;
    }
}
