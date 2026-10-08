using System.Threading;
using Dojo.Framework.Content;
using UnityEngine;

namespace Dojo.Framework.Session
{
    /// <summary>
    /// Answers the session locally, because there is no server yet.
    /// </summary>
    /// <remarks>
    /// Everything here is hardcoded and says so. It exists so the startup sequence is the real
    /// sequence — connect, read the entitlements, then download — rather than a comment promising
    /// that sequence later. When the REST implementation arrives it replaces this one line in
    /// <c>RootLifetimeScope</c> and nothing else moves.
    /// <para>
    /// The entitlement it reports is whatever <see cref="ContentSettings.ContentKeys"/> holds — the
    /// list on the root scope's Inspector. With no server to ask, the person running the editor is
    /// the closest thing to one, so that field is the answer rather than a constant here. It is
    /// also the only way the field means anything: <c>AppStartup</c> writes the session's reply
    /// over it, so a set typed into the Inspector that this ignored would be silently discarded.
    /// </para>
    /// </remarks>
    public sealed class SimulatedSessionService : ISessionService
    {
        /// <summary>Stand-in identity. Not durable, not an OIDC subject, and not meant to be.</summary>
        const string SimulatedPlayerId = "plr_simulated";

        /// <summary>
        /// Stands in for the server's entitlement table.
        /// </summary>
        /// <remarks>
        /// Injected rather than read from a static, so the REST implementation that replaces this
        /// class takes the same dependency for the opposite reason — it needs the roots, not the
        /// keys — and the registration in <c>RootLifetimeScope</c> does not change shape.
        /// </remarks>
        readonly ContentSettings contentSettings;

        /// <summary>
        /// What the backend says this player already owns.
        /// </summary>
        /// <remarks>
        /// The part of this simulation that is not pretend. A real sign-in payload names the packs
        /// the player has bought, and the only place that knows is the server — so the simulation
        /// asks it rather than reading a list off the prefab. Without this the payload could never
        /// contain a pack bought after the build shipped, which is exactly the bug it fixes: buy a
        /// pack, close the game, reopen it, and the pack was gone.
        /// </remarks>
        readonly IOwnedPackSource ownedPacks;

        public SimulatedSessionService(ContentSettings contentSettings, IOwnedPackSource ownedPacks)
        {
            // Never null in practice — the root scope registers an instance — but a simulation that
            // throws during startup teaches nothing, so an empty settings object stands in and
            // ContentKeys falls back to the default set on its own.
            this.contentSettings = contentSettings ?? new ContentSettings();
            this.ownedPacks = ownedPacks ?? new NullOwnedPackSource();
        }

        public SessionResult Current { get; private set; }

        public bool IsConnected => Current != null && Current.Connected;

        public async Awaitable<SessionResult> ConnectAsync(CancellationToken cancellationToken = default)
        {
            // Logged before the await, not only after. A connection that hangs is the case anyone
            // debugging startup most needs to see, and a line that only prints on success says
            // nothing at all about the run that never finished.
            Debug.Log("[Session] connecting… (simulated — no server configured)");

            // Connect, then read. This is the whole point of the payload arriving *after* a
            // connection: what the player owns is a fact the server holds, and asking it is the
            // only way the answer can include a pack bought in a previous run.
            var owned = await ownedPacks.OwnedAsync(cancellationToken);

            // Seed first, so the default set is always the floor, then whatever the server says
            // this player has bought on top. A brand-new player's owned list arrives holding the
            // starter pack the module grants on connect, and this still comes out right; an
            // unreachable backend answers empty, and this degrades to exactly the old behaviour.
            var keys = new System.Collections.Generic.List<string>(contentSettings.ContentKeys);

            foreach (var pack in owned)
            {
                if (string.IsNullOrWhiteSpace(pack))
                {
                    continue;
                }

                var trimmed = pack.Trim();

                if (!keys.Exists(k => string.Equals(k, trimmed, System.StringComparison.OrdinalIgnoreCase)))
                {
                    keys.Add(trimmed);
                }
            }

            Debug.Log("[Session] seed: " + string.Join(", ", contentSettings.ContentKeys)
                + "  |  owned per the backend: "
                + (owned.Count == 0 ? "<none>" : string.Join(", ", owned)));

            Current = SessionResult.Success(SimulatedPlayerId, keys);

            Debug.Log("[Session] connected as '" + SimulatedPlayerId + "' — entitled to: "
                + string.Join(", ", new System.Collections.Generic.List<string>(Current.ContentKeys).ToArray()));

            return Current;
        }
    }
}
