using System;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Dojo.Framework.Utilities
{
    /// <summary>How a scene joins the currently loaded set.</summary>
    public enum SceneLoadMode
    {
        /// <summary>Unloads everything else first. The usual choice for a hard transition.</summary>
        Single,

        /// <summary>Adds the scene alongside what is already loaded.</summary>
        Additive,
    }

    /// <summary>What a scene operation is doing right now, as dispatched to subscribers.</summary>
    public readonly struct SceneLoadProgress
    {
        /// <summary>The scene being loaded or unloaded.</summary>
        public readonly string SceneName;

        /// <summary>0..1, already corrected for Unity's 0.9 activation cutoff.</summary>
        public readonly float Normalized;

        /// <summary>0..100, rounded. Convenient for "Loading 42%" labels.</summary>
        public readonly int Percent;

        /// <summary>True once streaming is done and only activation remains.</summary>
        public readonly bool IsReadyToActivate;

        /// <summary>False while unloading, so one subscriber can drive both directions.</summary>
        public readonly bool IsLoad;

        internal SceneLoadProgress(string sceneName, float normalized, bool isReadyToActivate, bool isLoad)
        {
            SceneName = sceneName;
            Normalized = normalized;
            Percent = Mathf.RoundToInt(normalized * 100f);
            IsReadyToActivate = isReadyToActivate;
            IsLoad = isLoad;
        }
    }

    /// <summary>
    /// Everything that varies between loads, so <see cref="ILoadingService"/> keeps one entry point
    /// no matter how the call site wants the scene brought in.
    /// </summary>
    public readonly struct SceneLoadRequest
    {
        public readonly string SceneName;
        public readonly SceneLoadMode Mode;

        /// <summary>
        /// When false the scene loads to 100% and stops, and you drive the last step with
        /// <see cref="SceneLoadHandle.ActivateAsync"/>. That is how you hold a loading screen
        /// until it is ready to hand over.
        /// </summary>
        public readonly bool ActivateOnLoad;

        /// <summary>Additive only: make this the active scene once it is in.</summary>
        public readonly bool SetAsActiveScene;

        /// <summary>Background thread priority for the load. Higher finishes sooner, hitches more.</summary>
        public readonly int Priority;

        public SceneLoadRequest(
            string sceneName,
            SceneLoadMode mode = SceneLoadMode.Single,
            bool activateOnLoad = true,
            bool setAsActiveScene = false,
            int priority = 100)
        {
            SceneName = sceneName;
            Mode = mode;
            ActivateOnLoad = activateOnLoad;
            SetAsActiveScene = setAsActiveScene;
            Priority = priority;
        }

        /// <summary>Replace the current scene set with <paramref name="sceneName"/>.</summary>
        public static SceneLoadRequest Single(string sceneName, bool activateOnLoad = true)
            => new SceneLoadRequest(sceneName, SceneLoadMode.Single, activateOnLoad);

        /// <summary>Layer <paramref name="sceneName"/> on top of what is loaded.</summary>
        public static SceneLoadRequest Additive(string sceneName, bool setAsActiveScene = false, bool activateOnLoad = true)
            => new SceneLoadRequest(sceneName, SceneLoadMode.Additive, activateOnLoad, setAsActiveScene);
    }

    /// <summary>
    /// A load in flight. Only interesting when the request set
    /// <see cref="SceneLoadRequest.ActivateOnLoad"/> to false; otherwise it comes back already done
    /// and can be ignored.
    /// </summary>
    public sealed class SceneLoadHandle
    {
        /// <summary>
        /// Unity parks an AsyncOperation at 0.9 when the scene is streamed but not yet activated,
        /// so raw progress never reaches 1 on a deferred load. Everything public is rescaled past it.
        /// </summary>
        internal const float ActivationThreshold = 0.9f;

        readonly AsyncOperation operation;
        readonly SceneLoadRequest request;
        readonly Action onFinished;

        internal SceneLoadHandle(AsyncOperation operation, SceneLoadRequest request, Action onFinished)
        {
            this.operation = operation;
            this.request = request;
            this.onFinished = onFinished;
        }

        public string SceneName => request.SceneName;

        public bool IsDone => operation.isDone;

        /// <summary>This operation's own progress, 0..1. Reaches 1 even on a deferred load.</summary>
        public float Progress => Normalize(operation.progress);

        /// <summary>This operation's own progress, 0..100.</summary>
        public int Percent => Mathf.RoundToInt(Progress * 100f);

        /// <summary>True once the scene is streamed in and only activation remains.</summary>
        public bool IsReadyToActivate => operation.progress >= ActivationThreshold;

        internal static float Normalize(float rawProgress) => Mathf.Clamp01(rawProgress / ActivationThreshold);

        /// <summary>
        /// Finishes a deferred load. Safe to call on an already-completed handle, so callers do not
        /// have to branch on how the request was configured.
        /// </summary>
        public async Awaitable ActivateAsync(CancellationToken cancellationToken = default)
        {
            if (operation.isDone)
            {
                return;
            }

            operation.allowSceneActivation = true;
            while (!operation.isDone)
            {
                await Awaitable.NextFrameAsync(cancellationToken);
            }

            Finish();
        }

        internal void Finish()
        {
            if (request.Mode == SceneLoadMode.Additive && request.SetAsActiveScene)
            {
                var scene = SceneManager.GetSceneByName(request.SceneName);
                if (scene.IsValid())
                {
                    SceneManager.SetActiveScene(scene);
                }
            }

            onFinished?.Invoke();
        }
    }

    public interface ILoadingService
    {
        /// <summary>True while a load is streaming, including one parked awaiting activation.</summary>
        bool IsLoading { get; }

        /// <summary>
        /// Latest dispatched progress. Read this on subscribe so a listener that joins mid-load
        /// starts from the real value instead of zero.
        /// </summary>
        SceneLoadProgress CurrentProgress { get; }

        /// <summary>
        /// Fires on the main thread whenever the rounded percent changes, plus once at 0 on start
        /// and once at 100 on completion.
        /// </summary>
        event Action<SceneLoadProgress> ProgressChanged;

        Awaitable<SceneLoadHandle> LoadAsync(SceneLoadRequest request, CancellationToken cancellationToken = default);

        Awaitable UnloadAsync(string sceneName, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Scene loading behind one interface. Register once with <c>Lifetime.Singleton</c> and inject it
    /// wherever a transition is needed.
    /// </summary>
    public sealed class LoadingService : ILoadingService
    {
        public bool IsLoading { get; private set; }

        public SceneLoadProgress CurrentProgress { get; private set; }

        public event Action<SceneLoadProgress> ProgressChanged;

        public async Awaitable<SceneLoadHandle> LoadAsync(
            SceneLoadRequest request,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(request.SceneName))
            {
                throw new ArgumentException("Scene name must not be empty.", nameof(request));
            }

            if (IsLoading)
            {
                throw new InvalidOperationException(
                    $"A load is already in progress; cannot start '{request.SceneName}'. " +
                    "Await the previous load, or guard the call site with IsLoading.");
            }

            var mode = request.Mode == SceneLoadMode.Additive
                ? LoadSceneMode.Additive
                : LoadSceneMode.Single;

            var operation = SceneManager.LoadSceneAsync(request.SceneName, mode);
            if (operation == null)
            {
                throw new InvalidOperationException(
                    $"Scene '{request.SceneName}' could not be loaded. Check that it is added to Build Settings.");
            }

            IsLoading = true;
            operation.priority = request.Priority;
            operation.allowSceneActivation = request.ActivateOnLoad;

            var handle = new SceneLoadHandle(operation, request, () => IsLoading = false);

            Dispatch(request.SceneName, 0f, false, isLoad: true, force: true);

            while (!operation.isDone)
            {
                Dispatch(request.SceneName, operation.progress, handle.IsReadyToActivate, isLoad: true);

                // A deferred load never reports isDone on its own; it sits at the threshold
                // until the caller activates it.
                if (!request.ActivateOnLoad && handle.IsReadyToActivate)
                {
                    return handle;
                }

                await Awaitable.NextFrameAsync(cancellationToken);
            }

            Dispatch(request.SceneName, 1f, true, isLoad: true, force: true);
            handle.Finish();
            return handle;
        }

        public async Awaitable UnloadAsync(string sceneName, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                throw new ArgumentException("Scene name must not be empty.", nameof(sceneName));
            }

            var scene = SceneManager.GetSceneByName(sceneName);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return;
            }

            var operation = SceneManager.UnloadSceneAsync(scene);
            if (operation == null)
            {
                return;
            }

            Dispatch(sceneName, 0f, false, isLoad: false, force: true);

            while (!operation.isDone)
            {
                Dispatch(sceneName, operation.progress, false, isLoad: false);
                await Awaitable.NextFrameAsync(cancellationToken);
            }

            Dispatch(sceneName, 1f, false, isLoad: false, force: true);
        }

        /// <summary>
        /// Publishes progress, skipping frames where the rounded percent has not moved so
        /// subscribers are not woken ~60 times a second for the same number.
        /// </summary>
        void Dispatch(string sceneName, float rawProgress, bool isReadyToActivate, bool isLoad, bool force = false)
        {
            var next = new SceneLoadProgress(
                sceneName,
                SceneLoadHandle.Normalize(rawProgress),
                isReadyToActivate,
                isLoad);

            if (!force && next.Percent == CurrentProgress.Percent && sceneName == CurrentProgress.SceneName)
            {
                return;
            }

            CurrentProgress = next;

            // A throwing subscriber must not strand the load loop or leave IsLoading stuck true.
            try
            {
                ProgressChanged?.Invoke(next);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }
}
