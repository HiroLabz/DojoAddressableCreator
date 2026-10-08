using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace Dojo.Game.Systems
{
    /// <summary>
    /// Owns the one EventSystem that has to outlive every scene load. Scene loading itself is
    /// driven by <see cref="AppStartup"/>, which the root scope runs once the container is up.
    /// </summary>
    public sealed class Bootstrapper : MonoBehaviour
    {
        void Awake()
        {
            EnsurePersistentEventSystem();
        }

        /// <summary>
        /// UI input needs exactly one EventSystem alive at all times. Scene-local ones die on a
        /// Single load and take clicks with them, so the canonical one lives here and persists.
        /// Idempotent, so it is safe if another scene already brought one along.
        /// </summary>
        static void EnsurePersistentEventSystem()
        {
            var existing = FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include);
            if (existing != null)
            {
                DontDestroyOnLoad(existing.transform.root.gameObject);
                return;
            }

            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();

            // Project is on the new Input System, so the uGUI StandaloneInputModule would throw.
            var module = go.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();

            DontDestroyOnLoad(go);
        }
    }
}
