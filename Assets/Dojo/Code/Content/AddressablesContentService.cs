using System;
using System.Collections.Generic;
using System.Threading;
using Dojo.Framework.Content;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Dojo.Content
{
    /// <summary>
    /// <see cref="IContentService"/> over Addressables. The one class in the project that knows the
    /// package exists.
    /// </summary>
    /// <remarks>
    /// Every operation polls its handle with <c>Awaitable.NextFrameAsync</c> rather than awaiting
    /// <c>handle.Task</c>. Two reasons: it takes a cancellation token, which the Task does not, and
    /// it keeps continuations on the main thread — the same pattern
    /// <see cref="Dojo.Framework.Utilities.LoadingService"/> already uses for scene loads.
    /// <para>
    /// Failures are translated to <see cref="ContentException"/> at this boundary, so the startup
    /// popup and <c>AppReadiness</c> never see an Addressables type.
    /// </para>
    /// </remarks>
    public sealed class AddressablesContentService : IContentService
    {
        readonly ContentSettings settings;

        /// <summary>
        /// Everything loaded so far, by address. A list per address because one address can have
        /// several representations — a sprite-imported PNG answers to both <c>Sprite</c> and
        /// <c>Texture2D</c>, and the inventory asks for each in turn.
        /// </summary>
        readonly Dictionary<string, List<UnityEngine.Object>> cache =
            new Dictionary<string, List<UnityEngine.Object>>();

        /// <summary>Every address in a preloaded set: what <see cref="TryGet{T}"/> may load.</summary>
        readonly HashSet<string> available = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Address and type pairs already tried and not found, so they are not tried again.</summary>
        readonly HashSet<string> unavailable = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Content sets already resident, in load order. A list rather than a set because the order
        /// is the priority order, and because there will only ever be a handful.
        /// </summary>
        readonly List<string> loadedKeys = new List<string>();

        bool transformInstalled;

        public AddressablesContentService(ContentSettings settings)
        {
            this.settings = settings ?? new ContentSettings();
        }

        public bool IsReady { get; private set; }

        public ContentProgress CurrentProgress { get; private set; }

        public event Action<ContentProgress> ProgressChanged;

        public async Awaitable InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (IsReady)
            {
                return;
            }

            // Before InitializeAsync, so the catalogue itself is fetched from the configured root
            // rather than whatever the build baked in.
            InstallPathTransform();

            Log("fetching catalogue from " + Describe());
            var started = Time.realtimeSinceStartup;

            var handle = Addressables.InitializeAsync(false);
            while (!handle.IsDone)
            {
                await Awaitable.NextFrameAsync(cancellationToken);
            }

            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                var inner = handle.OperationException;
                Addressables.Release(handle);
                Debug.LogError(LogPrefix + " catalogue failed from " + Describe()
                    + (inner != null ? " — " + inner.Message : string.Empty));
                throw new ContentException(
                    "Could not load the content catalogue from " + Describe() + ".", inner);
            }

            Addressables.Release(handle);
            IsReady = true;
            Log("catalogue ready in " + Seconds(started));
        }

        public async Awaitable<long> GetDownloadSizeAsync(string label, CancellationToken cancellationToken = default)
        {
            var handle = Addressables.GetDownloadSizeAsync(label);
            while (!handle.IsDone)
            {
                await Awaitable.NextFrameAsync(cancellationToken);
            }

            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                var inner = handle.OperationException;
                Addressables.Release(handle);
                throw new ContentException("Could not determine the download size for '" + label + "'.", inner);
            }

            var size = handle.Result;
            Addressables.Release(handle);
            return size;
        }

        public IReadOnlyCollection<string> LoadedKeys => loadedKeys;

        public bool IsLoaded(string key) => !string.IsNullOrEmpty(key) && loadedKeys.Contains(key);

        public async Awaitable PreloadAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
        {
            if (keys == null || keys.Count == 0)
            {
                return;
            }

            Log("loading " + keys.Count + " content set(s): " + string.Join(", ", keys));

            // Sequential, not parallel. Two concurrent downloads share the same connection and
            // finish no sooner, but their progress interleaves into one meaningless bar — and the
            // gate cares that all of them arrive, not that any one did.
            for (var i = 0; i < keys.Count; i++)
            {
                // Counted, so a set that never starts is as visible as one that fails. Without it a
                // second set that is silently skipped looks identical to one that downloaded fast.
                Log("── [" + (i + 1) + "/" + keys.Count + "] '" + keys[i] + "' ──");
                await PreloadAsync(keys[i], cancellationToken);
            }

            // One line naming every set that made it, because the per-set lines above are separated
            // by however many progress lines each download produced. Anything asked for but missing
            // from this list was skipped or failed further up.
            var resident = new List<string>();
            foreach (var key in keys)
            {
                if (loadedKeys.Contains(key))
                {
                    resident.Add(key);
                }
            }

            Log("preload complete — resident: " + string.Join(", ", resident.ToArray())
                + "  (" + available.Count + " addresses)");
        }

        public async Awaitable PreloadAsync(string key, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            if (loadedKeys.Contains(key))
            {
                Log("'" + key + "' is already resident");
                return;
            }

            if (!await HasContentAsync(key, cancellationToken))
            {
                // The catalogue has never heard of this set. Warn and carry on rather than throw:
                // an entitlement naming a set this build does not ship is exactly DB-001 §6's
                // "the server knows an item this build cannot render — skip it and warn once", and
                // taking startup down over it would be a far worse answer.
                Debug.LogWarning(LogPrefix + " no content set named '" + key
                    + "' exists in the catalogue at " + Describe() + " — skipping it.");
                return;
            }

            var started = Time.realtimeSinceStartup;
            var label = key;
            var size = await GetDownloadSizeAsync(label, cancellationToken);

            if (size <= 0)
            {
                // Everything is cached already. Report complete rather than returning silently, so
                // a progress bar that is showing finishes instead of sitting at zero.
                Log("'" + label + "' already cached, nothing to download");
                Dispatch(label, 1f, 0, 0, force: true);

                // Still index. TryGet only loads what a preloaded set holds, so returning here
                // instead would leave a catalogue that resolved nothing.
                await IndexLabelAsync(label, cancellationToken);
                loadedKeys.Add(key);
                Log("'" + key + "' resident — " + available.Count + " addresses total");
                return;
            }

            Log("'" + label + "' needs " + Megabytes(size) + " from " + Describe());
            Dispatch(label, 0f, 0, size, force: true);

            // Logged per decile rather than per frame: at 60fps a 24 MB download is hundreds of
            // lines, which buries everything else in the console and tells you nothing extra.
            var nextLoggedDecile = 10;

            var handle = Addressables.DownloadDependenciesAsync(label, false);
            while (!handle.IsDone)
            {
                var status = handle.GetDownloadStatus();
                Dispatch(label, status.Percent, status.DownloadedBytes, status.TotalBytes);

                var percent = Mathf.RoundToInt(status.Percent * 100f);
                if (percent >= nextLoggedDecile)
                {
                    // Named, not just indented. Sets download one after another, so an unlabelled
                    // percentage belongs to whichever one you last read the name of — which is the
                    // wrong one as soon as anybody scrolls.
                    Log("  '" + label + "' " + percent + "%  " + Megabytes(status.DownloadedBytes)
                        + " / " + Megabytes(status.TotalBytes));
                    nextLoggedDecile = (percent / 10) * 10 + 10;
                }

                await Awaitable.NextFrameAsync(cancellationToken);
            }

            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                var inner = handle.OperationException;
                Addressables.Release(handle);
                Debug.LogError(LogPrefix + " download of '" + label + "' failed from " + Describe()
                    + (inner != null ? " — " + inner.Message : string.Empty));
                throw new ContentException(
                    "Could not download content for '" + label + "' from " + Describe() + ".", inner);
            }

            Addressables.Release(handle);
            Dispatch(label, 1f, size, size, force: true);
            Log("'" + label + "' downloaded " + Megabytes(size) + " in " + Seconds(started)
                + " from " + Describe());

            await IndexLabelAsync(label, cancellationToken);
            loadedKeys.Add(key);
            Log("'" + key + "' resident — " + available.Count + " addresses total");
        }

        /// <summary>
        /// Whether the catalogue knows this content set at all.
        /// </summary>
        /// <remarks>
        /// Asked before the download rather than letting <c>GetDownloadSizeAsync</c> throw on an
        /// unknown key. An entitlement list is data from a server that ships on its own schedule,
        /// so naming a set this build has never seen is an ordinary occurrence, not an error.
        /// </remarks>
        async Awaitable<bool> HasContentAsync(string key, CancellationToken cancellationToken)
        {
            var handle = Addressables.LoadResourceLocationsAsync(key);
            while (!handle.IsDone)
            {
                await Awaitable.NextFrameAsync(cancellationToken);
            }

            var any = handle.Status == AsyncOperationStatus.Succeeded
                && handle.Result != null
                && handle.Result.Count > 0;

            Addressables.Release(handle);
            return any;
        }

        /// <summary>
        /// Records every address under a label as one <see cref="TryGet{T}"/> may load, without
        /// loading any of them.
        /// </summary>
        /// <remarks>
        /// This used to load every asset under the label into memory, one at a time and a frame
        /// apart, before startup could finish - every texture, material and mesh of the pack, when
        /// a world only builds from a handful of its prefabs. Now an asset is loaded the first time
        /// it is asked for. The index keeps the old rule that only a preloaded set resolves, so a
        /// pack the player does not have still answers nothing.
        /// </remarks>
        async Awaitable IndexLabelAsync(string label, CancellationToken cancellationToken)
        {
            var locations = Addressables.LoadResourceLocationsAsync(label);

            while (!locations.IsDone)
            {
                await Awaitable.NextFrameAsync(cancellationToken);
            }

            if (locations.Status == AsyncOperationStatus.Succeeded && locations.Result != null)
            {
                foreach (var location in locations.Result)
                {
                    available.Add(location.PrimaryKey);
                }
            }

            Addressables.Release(locations);
        }

        /// <summary>
        /// Loads one indexed address as <typeparamref name="T"/>, there and then, and keeps it.
        /// </summary>
        /// <remarks>
        /// Synchronous, because <see cref="TryGet{T}"/> is: building a world asks for its pieces
        /// one after another and cannot wait a frame between them. The bundles are local, or were
        /// downloaded by the preload, so completing the load is a disk read rather than a fetch.
        /// <para>
        /// Located by type first, because the type asked for decides what comes back: a
        /// sprite-imported PNG answers to both <c>Sprite</c> and <c>Texture2D</c>, and a prefab to
        /// neither. A type an address cannot give is remembered, so asking again costs nothing.
        /// </para>
        /// </remarks>
        bool LoadNow<T>(string key, out T asset) where T : UnityEngine.Object
        {
            asset = null;

            var attempt = key + "|" + typeof(T).FullName;

            if (!available.Contains(key) || unavailable.Contains(attempt))
            {
                return false;
            }

            var locations = Addressables.LoadResourceLocationsAsync(key, typeof(T));
            locations.WaitForCompletion();

            var found = locations.Status == AsyncOperationStatus.Succeeded
                && locations.Result != null
                && locations.Result.Count > 0;

            if (found)
            {
                var load = Addressables.LoadAssetAsync<T>(locations.Result[0]);
                load.WaitForCompletion();

                if (load.Status == AsyncOperationStatus.Succeeded && load.Result != null)
                {
                    asset = load.Result;
                }
                else
                {
                    Addressables.Release(load);
                }
            }

            Addressables.Release(locations);

            if (asset == null)
            {
                unavailable.Add(attempt);
                return false;
            }

            List<UnityEngine.Object> existing;
            if (!cache.TryGetValue(key, out existing))
            {
                existing = new List<UnityEngine.Object>();
                cache[key] = existing;
            }

            existing.Add(asset);
            return true;
        }

        public bool TryGet<T>(string key, out T asset) where T : UnityEngine.Object
        {
            asset = null;

            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            List<UnityEngine.Object> candidates;
            if (cache.TryGetValue(key, out candidates))
            {
                foreach (var candidate in candidates)
                {
                    var typed = candidate as T;
                    if (typed != null)
                    {
                        asset = typed;
                        return true;
                    }
                }
            }

            // Not asked for as this type before: load it now, from a set that has been preloaded.
            return LoadNow(key, out asset);
        }

        public async Awaitable<T> LoadAsync<T>(string key, CancellationToken cancellationToken = default)
            where T : UnityEngine.Object
        {
            var handle = Addressables.LoadAssetAsync<T>(key);
            while (!handle.IsDone)
            {
                await Awaitable.NextFrameAsync(cancellationToken);
            }

            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                var inner = handle.OperationException;
                throw new ContentException("Could not load content '" + key + "'.", inner);
            }

            // Deliberately not released here: the caller owns the asset until it calls Release.
            return handle.Result;
        }

        public void Release<T>(T asset) where T : UnityEngine.Object
        {
            if (asset != null)
            {
                Addressables.Release(asset);
            }
        }

        /// <summary>
        /// Rewrites remote URLs to the configured root, so the environment can be changed without
        /// rebuilding content.
        /// </summary>
        /// <remarks>
        /// The build bakes an absolute URL into every remote location. Addressables always lays
        /// those out flat as <c>&lt;root&gt;/&lt;file&gt;</c> (the profile paths carry no
        /// [BuildTarget] segment), so keeping the last segment and replacing everything before it
        /// moves the whole catalogue to a new host without touching the bundles. Local locations
        /// are left alone — they are file paths, not URLs, and rewriting them would break the
        /// built-in catalogue.
        /// </remarks>
        void InstallPathTransform()
        {
            var root = settings.RemoteRoot;
            if (transformInstalled || string.IsNullOrEmpty(root))
            {
                return;
            }

            Addressables.ResourceManager.InternalIdTransformFunc = location =>
            {
                var id = location.InternalId;

                if (string.IsNullOrEmpty(id) ||
                    (!id.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                     !id.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                {
                    return id;
                }

                Uri uri;
                if (!Uri.TryCreate(id, UriKind.Absolute, out uri))
                {
                    return id;
                }

                var segments = uri.AbsolutePath.Trim('/').Split('/');
                if (segments.Length < 1)
                {
                    return id;
                }

                var tail = segments[segments.Length - 1];
                return root + "/" + tail;
            };

            transformInstalled = true;
        }

        /// <summary>Where content is coming from, for an error the player might actually read.</summary>
        string Describe()
        {
            var root = settings.RemoteRoot;
            return string.IsNullOrEmpty(root) ? "the built-in content path" : root;
        }

        const string LogPrefix = "[Content]";

        /// <summary>
        /// One line about the download, when the settings ask for it.
        /// </summary>
        /// <remarks>
        /// Opt-out rather than stripped in a build. Content arrives over a network the player owns
        /// and we do not, so "how much, from where, how long" is the first thing anyone asks when a
        /// game hangs on a loading bar — and a log that only exists in the editor cannot answer it.
        /// </remarks>
        void Log(string message)
        {
            if (settings.LogProgress)
            {
                Debug.Log(LogPrefix + " " + message);
            }
        }

        static string Megabytes(long bytes) => (bytes / 1048576f).ToString("0.00") + " MB";

        static string Seconds(float since) => (Time.realtimeSinceStartup - since).ToString("0.00") + "s";

        /// <summary>
        /// Publishes progress, skipping frames where the rounded percent has not moved so
        /// subscribers are not woken every frame for the same number.
        /// </summary>
        void Dispatch(string label, float normalized, long downloaded, long total, bool force = false)
        {
            var next = new ContentProgress(label, normalized, downloaded, total);

            if (!force && next.Percent == CurrentProgress.Percent && label == CurrentProgress.Label)
            {
                return;
            }

            CurrentProgress = next;

            var handler = ProgressChanged;
            if (handler != null)
            {
                handler(next);
            }
        }
    }
}
