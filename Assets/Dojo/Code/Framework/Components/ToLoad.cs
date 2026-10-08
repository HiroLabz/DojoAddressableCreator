using Dojo.Framework.Startup;
using Dojo.Framework.Utilities;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace Dojo.Framework.Components
{
    /// <summary>Drops on a <see cref="Button"/> to make it load a scene.</summary>
    /// <remarks>
    /// Also the Lobby's gate. The button stays non-interactable until every required startup
    /// preprocess has succeeded, so a player cannot enter a game scene before the content it needs
    /// has been downloaded. That is the whole mechanism: nothing blocks the Lobby, and this is what
    /// stops the player walking into an empty world instead.
    /// </remarks>
    [RequireComponent(typeof(Button))]
    public sealed class ToLoad : MonoBehaviour
    {
        [Tooltip("Scene to load. Must be in Build Settings, spelled exactly as the asset.")]
        [SerializeField] string sceneName;

        ILoadingService loadingService;
        IAppReadiness readiness;
        Button button;

        [Inject]
        public void Construct(ILoadingService loadingService, IAppReadiness readiness)
        {
            this.loadingService = loadingService;
            this.readiness = readiness;
        }

        void Start()
        {
            button = GetComponent<Button>();
            button.onClick.AddListener(OnButtonClick);

            if (readiness != null)
            {
                readiness.Changed += ApplyReadiness;
            }

            // Applied once on top of subscribing, rather than waiting for the next event. A
            // preprocess that finished before this scene loaded has already raised its only
            // Changed, and a button that waited for another would stay dead forever.
            ApplyReadiness();
        }

        void OnDestroy()
        {
            if (readiness != null)
            {
                readiness.Changed -= ApplyReadiness;
            }
        }

        /// <summary>
        /// Enables the button only when the app is ready. An uninjected component is treated as
        /// ready rather than dead: a scene used on its own, outside the normal startup path, should
        /// still be navigable instead of silently unusable.
        /// </summary>
        void ApplyReadiness()
        {
            if (button == null)
            {
                return;
            }

            button.interactable = readiness == null || readiness.AllRequiredSucceeded;
        }

        void OnButtonClick()
        {
            // LoadingService throws on a second concurrent load, so ignore clicks while one runs.
            if (loadingService.IsLoading)
            {
                return;
            }

            // Belt and braces alongside interactable: a click can still arrive in the frame a
            // preprocess fails, and entering a game scene without content is the one outcome this
            // component exists to prevent.
            if (readiness != null && !readiness.AllRequiredSucceeded)
            {
                return;
            }

            _ = loadingService.LoadAsync(SceneLoadRequest.Single(sceneName));
        }
    }
}
