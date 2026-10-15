using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Dojo.Framework.Content
{
    /// <summary>
    /// Which part of an acquisition is running, for anything drawing progress over it.
    /// </summary>
    /// <remarks>
    /// Reported rather than inferred. An acquisition is one await from the outside, and a caller
    /// that wanted to show the three parts separately would otherwise have to guess at the moments
    /// between them — which is a progress bar made of guesses.
    /// <para>
    /// Only <see cref="Downloading"/> takes any real time, and only it can report a fraction of
    /// itself: see <c>IContentService.ProgressChanged</c>, which reports real bytes for the label
    /// being fetched. The other two are near-instant locally and wait only on the backend.
    /// </para>
    /// </remarks>
    public enum AcquirePhase
    {
        /// <summary>Bringing the pack's bundles down.</summary>
        Downloading,

        /// <summary>Rebuilding the item catalogue from the manifests that just arrived.</summary>
        ReadingCatalogue,

        /// <summary>Telling the backend what the pack holds, and who now owns it.</summary>
        UpdatingInventory,
    }

    /// <summary>How an acquisition ended.</summary>
    public enum AcquireOutcome
    {
        /// <summary>The content came down and the player now holds the key.</summary>
        Acquired,

        /// <summary>The player already held the key, so nothing ran.</summary>
        AlreadyOwned,

        /// <summary>Something went wrong; the key was not added.</summary>
        Failed,
    }

    /// <summary>What an <see cref="IEntitlementGrants.AcquireAsync"/> call did.</summary>
    public readonly struct AcquireResult
    {
        /// <summary>The content key that was asked for.</summary>
        public readonly string Key;

        public readonly AcquireOutcome Outcome;

        /// <summary>Why it failed, or empty.</summary>
        public readonly string Error;

        /// <summary>True only when this call is what added the key.</summary>
        public bool Succeeded => Outcome == AcquireOutcome.Acquired;

        AcquireResult(string key, AcquireOutcome outcome, string error)
        {
            Key = key ?? string.Empty;
            Outcome = outcome;
            Error = error ?? string.Empty;
        }

        public static AcquireResult Acquired(string key) => new AcquireResult(key, AcquireOutcome.Acquired, null);

        public static AcquireResult AlreadyOwned(string key) => new AcquireResult(key, AcquireOutcome.AlreadyOwned, null);

        public static AcquireResult Failed(string key, string error) => new AcquireResult(key, AcquireOutcome.Failed, error);
    }

    /// <summary>Dispatched after a key is added and everything behind it is in place.</summary>
    public readonly struct EntitlementsChanged
    {
        /// <summary>The key just added. Empty when the whole set was adopted at sign-in.</summary>
        public readonly string AddedKey;

        /// <summary>Everything the player holds, after the change.</summary>
        public readonly IReadOnlyList<string> Keys;

        public EntitlementsChanged(string addedKey, IReadOnlyList<string> keys)
        {
            AddedKey = addedKey ?? string.Empty;
            Keys = keys ?? new string[0];
        }
    }

    /// <summary>
    /// What this player is entitled to. Read-only on purpose — this is the interface everything
    /// else in the app depends on, and nothing reachable through it can change what is held.
    /// </summary>
    /// <remarks>
    /// The list decides what downloads and, through the catalogue built from it, what may be
    /// placed. That makes it a gate rather than a setting, and a gate with a public setter is not
    /// a gate: before this existed, <c>ContentSettings.ContentKeys</c> could be assigned by any
    /// class in any assembly, so any class could hand the player a pack they had not acquired.
    /// <para>
    /// Mutation lives on <see cref="IEntitlementGrants"/> and on the concrete service, and both
    /// doors are narrow — see <c>EntitlementService</c>. Inject <em>this</em> interface unless you
    /// are the thing performing an acquisition.
    /// </para>
    /// </remarks>
    public interface IEntitlements
    {
        /// <summary>
        /// True once the session's answer has been adopted. False before that, which is different
        /// from "entitled to nothing" and worth distinguishing: a gate consulted too early would
        /// otherwise report the seed as though the server had confirmed it.
        /// </summary>
        bool IsReady { get; }

        /// <summary>
        /// Everything the player holds, in the order it was granted. Never empty — a player
        /// entitled to nothing still holds the default set, the same rule
        /// <see cref="ContentSettings.ContentKeys"/> applies to the seed.
        /// </summary>
        IReadOnlyList<string> Keys { get; }

        /// <summary>
        /// Whether the player holds this key. Case-insensitive, because a key typed into an
        /// Inspector and a key sent by a server should not disagree over capitalisation.
        /// </summary>
        bool Has(string key);

        /// <summary>
        /// Fires on the main thread after a change is complete — the content resident, the
        /// catalogue refreshed, the backend told. A subscriber can therefore rebuild straight from
        /// the catalogue without checking whether the assets have landed yet.
        /// </summary>
        event Action<EntitlementsChanged> Changed;
    }

    /// <summary>
    /// The acquisition door: the only way, short of the session's own answer, that a key enters
    /// <see cref="IEntitlements.Keys"/>.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="IEntitlements"/> so the many things that only ask a question depend
    /// on an interface that cannot change the answer. A store screen takes this one; everything
    /// else takes the other.
    /// </remarks>
    public interface IEntitlementGrants
    {
        /// <summary>
        /// Acquires one content pack: brings it down, refreshes the catalogue, tells the backend,
        /// then publishes the change.
        /// </summary>
        /// <remarks>
        /// Safe to call for a key the player already holds — that returns
        /// <see cref="AcquireOutcome.AlreadyOwned"/> without doing any work, which is the gate the
        /// caller would otherwise have to remember to apply for itself.
        /// </remarks>
        /// <param name="onPhase">
        /// Called as the acquisition moves between its parts, for a caller drawing progress over
        /// it. Optional, and never called for a key that was already owned — nothing runs.
        /// </param>
        Awaitable<AcquireResult> AcquireAsync(
            string key,
            string reason,
            CancellationToken cancellationToken = default,
            Action<AcquirePhase> onPhase = null);
    }
}
