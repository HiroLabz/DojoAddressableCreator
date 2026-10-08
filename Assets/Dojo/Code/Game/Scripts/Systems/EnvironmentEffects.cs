using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Dojo.Game.Systems
{
    /// <summary>
    /// The screen effects every scene shares - bloom, for now - held on this object's global
    /// <c>Volume</c> and kept for the whole run.
    /// </summary>
    /// <remarks>
    /// Authored into the Lobby and carried through every scene after it. Coming back to the Lobby
    /// makes a second copy, which gives way to the first.
    /// <para>
    /// A Volume only shows through a camera that renders post-processing, and every camera had that
    /// off, so each scene's main camera has it switched on as the scene loads. The project's default
    /// profile is all neutral, so that changes nothing about a scene beyond what this Volume adds.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class EnvironmentEffects : MonoBehaviour
    {
        static EnvironmentEffects instance;

        void Awake()
        {
            if (instance != null && instance != this)
            {
                // One is enough: the copy in a Lobby loaded again gives way to the one kept.
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);

            SceneManager.sceneLoaded += OnSceneLoaded;
            ShowThroughMainCamera();
        }

        void OnDestroy()
        {
            if (instance != this)
            {
                return;
            }

            instance = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ShowThroughMainCamera();

        /// <summary>Has the main camera render post-processing, so the effects show through it.</summary>
        static void ShowThroughMainCamera()
        {
            var view = Camera.main;

            if (view == null)
            {
                return;
            }

            var data = view.GetUniversalAdditionalCameraData();

            if (data != null && !data.renderPostProcessing)
            {
                data.renderPostProcessing = true;
            }
        }
    }
}
