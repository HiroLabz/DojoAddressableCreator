using System;
using System.Collections.Generic;
using System.Threading;
using Dojo.Framework.Inventory;
using Dojo.Framework.Session;
using UnityEngine;

namespace Dojo.Framework.Content
{
    /// <summary>
    /// Holds what this player is entitled to, and is the only thing that may change it.
    /// </summary>
    /// <remarks>
    /// <b>Why this exists.</b> The entitlement list decides what content downloads and, through the
    /// catalogue built from it, what the player may place. Before this class it lived behind a
    /// public setter on <see cref="ContentSettings"/>, which meant any class in any assembly could
    /// assign it — a gate anyone can open is not a gate. The list now lives in a private field
    /// here and leaves only through <see cref="IEntitlements"/>, which has no way to write it.
    /// <para>
    /// <b>The two doors.</b> A key can enter by exactly two routes, and neither takes a caller's
    /// word for it:
    /// </para>
    /// <list type="number">
    /// <item><see cref="AdoptSession"/> — the server's answer at sign-in. It takes a
    /// <see cref="SessionResult"/> rather than a list of strings, and only <see cref="ISessionService"/>
    /// can mint one of those, so a caller cannot fabricate an entitlement by assembling a list. It
    /// also refuses a second call, which makes "once at startup" a rule rather than a habit.</item>
    /// <item><see cref="AcquireAsync"/> — a pack acquired mid-session, which does the real work of
    /// bringing it down before admitting the player holds it.</item>
    /// </list>
    /// <para>
    /// <b>The ordering is the point.</b> <see cref="AcquireAsync"/> runs the same sequence
    /// <c>AppStartup</c> runs at startup — download, refresh the catalogue, tell the backend — for
    /// the reason its comments already gave: <em>"A pack bought mid-session takes the same two
    /// lines: preload it, then refresh."</em> Publishing the pack's contents before granting it
    /// is not a preference either; the module writes one ownership row per item definition it can
    /// see, so a grant that arrives first grants a pack containing nothing.
    /// </para>
    /// </remarks>
    public sealed class EntitlementService : IEntitlements, IEntitlementGrants
    {
        const string LogPrefix = "[Entitlements]";

        readonly IContentService content;
        readonly IItemCatalog catalog;
        readonly ICatalogPublisher catalogPublisher;
        readonly IPackGranter packGranter;

        /// <summary>
        /// The live list. Private, and never handed out by reference — <see cref="Keys"/> returns a
        /// read-only view, so a caller cannot cast its way to an <c>Add</c>.
        /// </summary>
        readonly List<string> keys = new List<string>();

        public EntitlementService(
            IContentService content,
            IItemCatalog catalog,
            ICatalogPublisher catalogPublisher,
            IPackGranter packGranter)
        {
            this.content = content;
            this.catalog = catalog;
            this.catalogPublisher = catalogPublisher;
            this.packGranter = packGranter;
        }

        public bool IsReady { get; private set; }

        public IReadOnlyList<string> Keys => keys.AsReadOnly();

        public event Action<EntitlementsChanged> Changed;

        /// <summary>
        /// Reports a phase, swallowing anything the listener throws.
        /// </summary>
        /// <remarks>
        /// A progress bar that faults must not take an acquisition down with it — the pack is
        /// halfway to the player at this point, and the drawing of it is the least important thing
        /// happening.
        /// </remarks>
        static void Phase(Action<AcquirePhase> onPhase, AcquirePhase phase)
        {
            if (onPhase == null)
            {
                return;
            }

            try
            {
                onPhase(phase);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        public bool Has(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            var wanted = key.Trim();

            foreach (var held in keys)
            {
                if (string.Equals(held, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Takes the server's answer as the opening entitlement. Startup only, and only once.
        /// </summary>
        /// <remarks>
        /// Not on <see cref="IEntitlements"/> or <see cref="IEntitlementGrants"/>: the one class
        /// allowed to seed the gate depends on this concrete type, so the dependency is visible in
        /// its constructor rather than hidden behind an interface everything holds.
        /// <para>
        /// Throws rather than warns on a second call. A silent re-seed would replace a list that
        /// may already have had a mid-session acquisition added to it, and losing a pack the
        /// player just paid for is not something to discover from a log line.
        /// </para>
        /// </remarks>
        /// <exception cref="InvalidOperationException">Already adopted.</exception>
        public void AdoptSession(SessionResult result)
        {
            if (IsReady)
            {
                throw new InvalidOperationException(
                    "The session has already been adopted. Entitlements are seeded once at startup; "
                    + "use AcquireAsync for a pack acquired later.");
            }

            if (result == null || !result.Connected)
            {
                throw new InvalidOperationException(
                    "Only a successful session can seed entitlements. A failed connection means "
                    + "nothing is known about this player, which is not the same as owning nothing.");
            }

            keys.Clear();

            foreach (var key in result.ContentKeys)
            {
                if (!string.IsNullOrWhiteSpace(key) && !Has(key))
                {
                    keys.Add(key.Trim());
                }
            }

            IsReady = true;

            Debug.Log($"{LogPrefix} adopted {keys.Count} key(s) from the session: {string.Join(", ", keys)}");

            Raise(string.Empty);
        }

        public async Awaitable<AcquireResult> AcquireAsync(
            string key,
            string reason,
            CancellationToken cancellationToken = default,
            Action<AcquirePhase> onPhase = null)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                Debug.LogWarning($"{LogPrefix} asked to acquire a blank key; nothing to do.");
                return AcquireResult.Failed(key, "the content key was blank");
            }

            var wanted = key.Trim();

            // Said before the decision rather than after it, so the log shows what the gate was
            // comparing against. "Already owned" and "acquired" are both correct answers and they
            // look identical in a log that only reports the outcome.
            Debug.Log($"{LogPrefix} asked for '{wanted}' ({reason}). Currently holding: "
                + (keys.Count == 0 ? "<nothing>" : string.Join(", ", keys)));

            // ── The gate ──────────────────────────────────────────────────────────────────
            // First, and before any await, so a second click while the first is still running
            // cannot start the work twice: the key goes in below on this same synchronous run.
            if (Has(wanted))
            {
                Debug.Log($"{LogPrefix} GATE: '{wanted}' is already held — not processing it again. "
                    + "No download, no catalogue refresh, no backend call.");
                return AcquireResult.AlreadyOwned(wanted);
            }

            Debug.Log($"{LogPrefix} GATE: '{wanted}' is not held — proceeding with the acquisition.");

            // ── 1. The payload ────────────────────────────────────────────────────────────
            // Admitted here rather than at the end, because this is what the arriving payload
            // says. Rolled back below if the content behind it never lands, so the list never
            // claims a pack this client cannot actually render.
            keys.Add(wanted);

            try
            {
                Debug.Log($"{LogPrefix} acquiring '{wanted}' ({reason})…");

                // ── 2. Bring the content down ────────────────────────────────────────────
                Phase(onPhase, AcquirePhase.Downloading);
                await content.PreloadAsync(wanted, cancellationToken);

                // ── 3. Read what arrived ─────────────────────────────────────────────────
                // The catalogue is built from manifests the preload just made resident, so it is
                // refreshed here rather than lazily on first use.
                Phase(onPhase, AcquirePhase.ReadingCatalogue);
                catalog.Refresh();

                // ── 4. Tell the backend what the pack contains ───────────────────────────
                Phase(onPhase, AcquirePhase.UpdatingInventory);
                // Before the grant, never after: the module writes one ownership row per item
                // definition it can see, so granting first grants an empty pack.
                await catalogPublisher.PublishAsync(catalog.All, cancellationToken);

                // ── 5. Tell the backend who owns it ──────────────────────────────────────
                await packGranter.GrantAsync(wanted, reason, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                keys.Remove(wanted);
                throw;
            }
            catch (Exception exception)
            {
                // Rolled back, so a failed download does not leave the player holding a key whose
                // assets are not resident — the inventory would list items it cannot draw.
                keys.Remove(wanted);

                Debug.LogWarning($"{LogPrefix} could not acquire '{wanted}': {exception.Message}");
                return AcquireResult.Failed(wanted, exception.Message);
            }

            Debug.Log($"{LogPrefix} acquired '{wanted}'. Now holding: {string.Join(", ", keys)}");

            // ── 6. Announce it ───────────────────────────────────────────────────────────
            // Last, and only on success. Subscribers rebuild from the catalogue when this fires,
            // which is safe precisely because it fires after step 3.
            Raise(wanted);

            return AcquireResult.Acquired(wanted);
        }

        void Raise(string addedKey)
        {
            var handler = Changed;

            if (handler == null)
            {
                return;
            }

            try
            {
                handler(new EntitlementsChanged(addedKey, keys.AsReadOnly()));
            }
            catch (Exception exception)
            {
                // One bad subscriber must not fail the acquisition that has already happened, nor
                // stop the subscribers after it.
                Debug.LogException(exception);
            }
        }
    }
}
