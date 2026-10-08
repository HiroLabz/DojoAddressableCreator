using UnityEngine;

namespace Dojo.Framework.Net
{
    /// <summary>Whether the game can reach its server.</summary>
    public enum ConnectionState
    {
        /// <summary>Not checked yet. Only ever the state for the first moment after start-up.</summary>
        Unknown,

        /// <summary>The server answered.</summary>
        Online,

        /// <summary>The server could not be reached, or there is no network at all.</summary>
        Offline,
    }

    /// <summary>
    /// Decides whether the connection is up or down from the probes' answers, and when to probe
    /// next. Knows nothing about how a probe is made.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="NetworkManager"/> so the timing - which is the part worth getting
    /// right - can be tested without a network.
    /// <list type="bullet">
    /// <item><b>Online:</b> checked every <see cref="OnlineInterval"/> seconds. Cheap, and a drop
    /// is also noticed at once whenever the network adapter changes or the app regains focus.</item>
    /// <item><b>Once online, one missed answer is not a drop.</b> It is checked again after
    /// <see cref="ConfirmDelay"/>; only <see cref="FailuresToGoOffline"/> in a row counts. A single
    /// slow reply on busy Wi-Fi would otherwise flash an "offline" at the player for nothing. At
    /// start-up, though, the first answer decides: startup is waiting on it.</item>
    /// <item><b>Offline:</b> retried quickly, then less often - <see cref="OfflineFirstRetry"/>
    /// doubling up to <see cref="OfflineMaxRetry"/> - so a connection that comes back is noticed
    /// within seconds without the game hammering a server that is not there.</item>
    /// </list>
    /// </remarks>
    public sealed class ConnectivityMonitor
    {
        /// <summary>Seconds between checks while online.</summary>
        public const float OnlineInterval = 30f;

        /// <summary>Seconds before re-checking after a missed answer.</summary>
        public const float ConfirmDelay = 2f;

        /// <summary>Missed answers in a row that make it offline.</summary>
        public const int FailuresToGoOffline = 2;

        /// <summary>Seconds before the first retry once offline.</summary>
        public const float OfflineFirstRetry = 2f;

        /// <summary>The longest wait between retries while offline.</summary>
        public const float OfflineMaxRetry = 15f;

        int failures;
        float offlineRetry = OfflineFirstRetry;

        /// <summary>What the connection is now.</summary>
        public ConnectionState State { get; private set; } = ConnectionState.Unknown;

        /// <summary>The result of one probe: what the state is now, and when to probe next.</summary>
        public readonly struct Verdict
        {
            public readonly ConnectionState Previous;
            public readonly ConnectionState Current;
            public readonly float NextCheckAt;

            public Verdict(ConnectionState previous, ConnectionState current, float nextCheckAt)
            {
                Previous = previous;
                Current = current;
                NextCheckAt = nextCheckAt;
            }

            /// <summary>True when this probe moved the state, which is when it is announced.</summary>
            public bool Changed => Previous != Current;
        }

        /// <summary>Takes one probe's answer at time <paramref name="now"/>.</summary>
        public Verdict Report(bool reached, float now)
        {
            var previous = State;

            if (reached)
            {
                failures = 0;
                offlineRetry = OfflineFirstRetry;
                State = ConnectionState.Online;
                return new Verdict(previous, State, now + OnlineInterval);
            }

            failures++;

            // Only a connection that was up gets the benefit of the doubt. At start-up there is no
            // state to flicker from, and the game is waiting on the answer to decide whether to open
            // a sign-in browser - one missed answer is the answer.
            if (State == ConnectionState.Online && failures < FailuresToGoOffline)
            {
                // Not yet: look again straight away before telling anybody.
                return new Verdict(previous, State, now + ConfirmDelay);
            }

            State = ConnectionState.Offline;

            var wait = offlineRetry;
            offlineRetry = Mathf.Min(offlineRetry * 2f, OfflineMaxRetry);

            return new Verdict(previous, State, now + wait);
        }

        /// <summary>
        /// There is no network adapter up at all - no Wi-Fi, no cable. Offline at once, with nothing
        /// to confirm and no request worth sending.
        /// </summary>
        public Verdict AdapterLost(float now)
        {
            var previous = State;

            failures = FailuresToGoOffline;
            State = ConnectionState.Offline;

            return new Verdict(previous, State, now + offlineRetry);
        }
    }
}
