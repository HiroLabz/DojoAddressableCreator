using System.Collections.Generic;
using Dojo.Framework.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Dojo.Game.Managers
{
    /// <summary>
    /// Keeps track of the sliding in-game panels — the inventory drawer, the agent details, the
    /// call group — and opens and closes them as a set.
    /// </summary>
    /// <remarks>
    /// <see cref="IInGamePopup"/> was written for exactly this and has been waiting for it: its own
    /// summary says to depend on the interface "when something needs to open or close a popup
    /// without caring which one it is — a menu controller closing every open popup, say". Until now
    /// nothing did, so each panel was opened by whoever happened to hold a reference to it and two
    /// could sit over each other.
    /// <para>
    /// The manager outlives the scene but the panels do not: they are authored into a scene's
    /// Canvas and die with it. So the set is gathered again on every scene load rather than being
    /// held from startup, and a panel destroyed under our feet is dropped rather than kept as a
    /// null that throws later.
    /// </para>
    /// <para>
    /// Was <c>PopupManager</c> until 2026-09-26. Renamed so that name could go to
    /// <c>Dojo.Framework.UI.PopupManager</c>, which does a different job: it builds popups on
    /// demand on its own always-on-top canvas, where this opens and closes panels already authored
    /// into a scene.
    /// </para>
    /// </remarks>
    public sealed class PanelManager : MonoBehaviour
    {
        [Tooltip("Only one panel open at a time. Opening one closes the others, which is usually " +
                 "what a screen this small wants; turn it off for panels meant to coexist.")]
        [SerializeField] bool exclusive = true;

        readonly List<IInGamePopup> popups = new List<IInGamePopup>();

        /// <summary>How many panels are currently known about.</summary>
        public int Count => popups.Count;

        /// <summary>How many are on screen.</summary>
        public int OpenCount
        {
            get
            {
                var open = 0;

                foreach (var popup in popups)
                {
                    if (Alive(popup) && popup.IsOpen)
                    {
                        open++;
                    }
                }

                return open;
            }
        }

        /// <summary>The one in the scene, made if there is not one yet.</summary>
        public static PanelManager Require()
        {
            var existing = FindAnyObjectByType<PanelManager>(FindObjectsInactive.Include);

            if (existing != null)
            {
                return existing;
            }

            // Persist() rather than DontDestroyOnLoad here: the component does it for itself in
            // Awake, which has already run by now.
            return new GameObject(nameof(PanelManager)).AddComponent<PanelManager>();
        }

        void Awake()
        {
            Persist();
            SceneManager.sceneLoaded += OnSceneLoaded;
            Rescan();
        }

        /// <summary>
        /// Marks this as surviving scene loads. Skipped outside play mode, where Unity refuses the
        /// call outright — there are no scene loads to survive there, so there is nothing to do.
        /// </summary>
        void Persist()
        {
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(gameObject);
            }
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Rescan();

        /// <summary>
        /// Finds every panel in the loaded scenes, replacing what was known before.
        /// </summary>
        /// <remarks>
        /// Inactive ones are included on purpose. A panel authored closed — <c>AgentDetails</c> is —
        /// is switched off in the hierarchy, and it is precisely the one something will later want
        /// to open.
        /// </remarks>
        public void Rescan()
        {
            popups.Clear();

            foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (behaviour is IInGamePopup popup)
                {
                    popups.Add(popup);
                }
            }
        }

        /// <summary>Adds a panel that was made after the last scan.</summary>
        public void Register(IInGamePopup popup)
        {
            if (popup != null && !popups.Contains(popup))
            {
                popups.Add(popup);
            }
        }

        /// <summary>Forgets a panel.</summary>
        public void Unregister(IInGamePopup popup) => popups.Remove(popup);

        /// <summary>Opens one panel, closing the rest when the manager is set to be exclusive.</summary>
        public void Open(IInGamePopup popup)
        {
            if (!Alive(popup))
            {
                return;
            }

            Register(popup);

            if (exclusive)
            {
                CloseAllExcept(popup);
            }

            popup.Show();
        }

        /// <summary>Closes one panel.</summary>
        public void Close(IInGamePopup popup)
        {
            if (Alive(popup))
            {
                popup.Hide();
            }
        }

        /// <summary>
        /// Opens a panel if it is closed and closes it if it is open — the same as its own toggle,
        /// except that opening it this way also clears the others.
        /// </summary>
        public void Toggle(IInGamePopup popup)
        {
            if (!Alive(popup))
            {
                return;
            }

            if (popup.IsOpen)
            {
                popup.Hide();
            }
            else
            {
                Open(popup);
            }
        }

        /// <summary>Finds a panel of a given kind, or null.</summary>
        public T Get<T>() where T : class, IInGamePopup
        {
            foreach (var popup in popups)
            {
                if (Alive(popup) && popup is T wanted)
                {
                    return wanted;
                }
            }

            return null;
        }

        /// <summary>Opens a panel of a given kind. Returns false when the scene has none.</summary>
        public bool Open<T>() where T : class, IInGamePopup
        {
            var popup = Get<T>();

            if (popup == null)
            {
                return false;
            }

            Open(popup);

            return true;
        }

        /// <summary>Sends every panel off screen.</summary>
        public void CloseAll() => CloseAllExcept(null);

        void CloseAllExcept(IInGamePopup keep)
        {
            // Backwards, so a panel destroyed since the last scan can be dropped as it is found.
            for (var i = popups.Count - 1; i >= 0; i--)
            {
                var popup = popups[i];

                if (!Alive(popup))
                {
                    popups.RemoveAt(i);
                    continue;
                }

                if (!ReferenceEquals(popup, keep) && popup.IsOpen)
                {
                    popup.Hide();
                }
            }
        }

        /// <summary>
        /// Whether a panel still exists.
        /// </summary>
        /// <remarks>
        /// The list holds interfaces, and an interface reference to a destroyed MonoBehaviour is not
        /// null in the C# sense — only Unity's own comparison knows. Casting back is the only honest
        /// way to ask.
        /// </remarks>
        static bool Alive(IInGamePopup popup) => popup is MonoBehaviour behaviour && behaviour != null;
    }
}
