using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Dojo.Framework.Content
{
    /// <summary>How far a content download has got, as dispatched to subscribers.</summary>
    /// <remarks>
    /// Shaped after <see cref="Dojo.Framework.Utilities.SceneLoadProgress"/> on purpose: the Lobby
    /// shows scene loading and content downloading with the same bar, and one subscriber should be
    /// able to drive both without special-casing either.
    /// </remarks>
    public readonly struct ContentProgress
    {
        /// <summary>The content set being downloaded.</summary>
        public readonly string Label;

        /// <summary>0..1.</summary>
        public readonly float Normalized;

        /// <summary>0..100, rounded. Convenient for "Downloading 42%" labels.</summary>
        public readonly int Percent;

        /// <summary>Bytes fetched so far. Zero when the whole set was already cached.</summary>
        public readonly long DownloadedBytes;

        /// <summary>Total bytes this download has to move. Zero when nothing needed fetching.</summary>
        public readonly long TotalBytes;

        public ContentProgress(string label, float normalized, long downloadedBytes, long totalBytes)
        {
            Label = label;
            Normalized = Mathf.Clamp01(normalized);
            Percent = Mathf.RoundToInt(Mathf.Clamp01(normalized) * 100f);
            DownloadedBytes = downloadedBytes;
            TotalBytes = totalBytes;
        }
    }

    /// <summary>
    /// Remote content behind one interface: bring the catalogue down, then hand out the assets in
    /// it. Register once with <c>Lifetime.Singleton</c> and inject wherever an asset is needed.
    /// </summary>
    /// <remarks>
    /// Nothing outside <c>Dojo.Content</c> knows this is Addressables, which is the same discipline
    /// ARCH-001 applies to the SpacetimeDB SDK. That is what makes the delivery mechanism — local
    /// folder today, CloudFront tomorrow — a change to one assembly and no call sites.
    /// <para>
    /// Note that <see cref="LoadAsync{T}"/> is only meaningful after <see cref="PreloadAsync"/> has
    /// completed for the label the key belongs to. That ordering is the whole reason content is a
    /// required startup preprocess rather than something each consumer does for itself.
    /// </para>
    /// </remarks>
    public interface IContentService
    {
        /// <summary>True once the catalogue is loaded and its keys can be resolved.</summary>
        bool IsReady { get; }

        /// <summary>
        /// Latest dispatched progress. Read this on subscribe so a listener joining mid-download
        /// starts from the real value instead of zero.
        /// </summary>
        ContentProgress CurrentProgress { get; }

        /// <summary>Fires on the main thread whenever the rounded percent changes.</summary>
        event Action<ContentProgress> ProgressChanged;

        /// <summary>
        /// Brings up the content system and loads the remote catalogue. Must complete before any
        /// other call here does anything useful.
        /// </summary>
        Awaitable InitializeAsync(CancellationToken cancellationToken = default);

        /// <summary>The content sets currently resident, in the order they were loaded.</summary>
        IReadOnlyCollection<string> LoadedKeys { get; }

        /// <summary>True when this content set has already been brought down and is resident.</summary>
        bool IsLoaded(string key);

        /// <summary>
        /// Bytes that <see cref="PreloadAsync(string, CancellationToken)"/> would have to fetch for
        /// this content set. Zero when it is all cached already, which is how a second run tells
        /// itself there is nothing to wait for.
        /// </summary>
        Awaitable<long> GetDownloadSizeAsync(string key, CancellationToken cancellationToken = default);

        /// <summary>
        /// Brings down one content set and makes its assets resolvable, reporting through
        /// <see cref="ProgressChanged"/>. Safe to call when nothing needs downloading, and safe to
        /// call for a set that is already loaded — it returns immediately.
        /// </summary>
        /// <remarks>
        /// Callable at any time, not only during startup. A set the player acquires mid-session is
        /// loaded with exactly this call; nothing about it assumes it is running before the game is.
        /// A key the build has never heard of is skipped with a warning rather than throwing — the
        /// same "skip it and warn once" rule DB-001 §6 applies to a catalogue mismatch.
        /// </remarks>
        Awaitable PreloadAsync(string key, CancellationToken cancellationToken = default);

        /// <summary>
        /// Brings down several content sets in order. Sets already resident are skipped, so this is
        /// safe to call again with a longer list once sign-in says the player is entitled to more.
        /// </summary>
        Awaitable PreloadAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default);

        /// <summary>Resolves one asset by key. The key is an address, not a path.</summary>
        Awaitable<T> LoadAsync<T>(string key, CancellationToken cancellationToken = default)
            where T : UnityEngine.Object;

        /// <summary>
        /// An asset that <see cref="PreloadAsync"/> already brought into memory, without awaiting.
        /// False when the key is unknown or was not part of a preloaded label.
        /// </summary>
        /// <remarks>
        /// This exists because the call sites that need content — placing a saved world, filling
        /// the inventory — are synchronous, and were synchronous when they read from
        /// <c>Resources</c>. Making them async would push awaits up through world loading and UI
        /// construction for no benefit, because the readiness gate already guarantees the
        /// catalogue is fully downloaded before any game scene loads. The asset is in memory by
        /// the time anything asks; this just says so without ceremony.
        /// <para>
        /// A caller that wants something outside the preloaded set must still use
        /// <see cref="LoadAsync{T}"/>.
        /// </para>
        /// </remarks>
        bool TryGet<T>(string key, out T asset) where T : UnityEngine.Object;

        /// <summary>
        /// Hands an asset back. Content is reference counted, so an asset loaded twice must be
        /// released twice before its bundle can be unloaded.
        /// </summary>
        void Release<T>(T asset) where T : UnityEngine.Object;
    }
}
