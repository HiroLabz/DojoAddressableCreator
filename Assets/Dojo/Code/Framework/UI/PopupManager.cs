using System.Collections.Generic;
using UnityEngine;

namespace Dojo.Framework.UI
{
    /// <summary>
    /// Puts popups on screen on their own layer above everything else, and takes each one away
    /// once it has finished hiding.
    /// </summary>
    /// <remarks>
    /// The popup counterpart of the dialog manager, with two differences worth knowing.
    /// <list type="bullet">
    /// <item><b>Several can be up at once.</b> A dialog is a question and only one is ever asked;
    /// a popup is news - "no connection", "saved" - and news can pile up. The newest goes on top.</item>
    /// <item><b>It is a singleton,</b> <see cref="Instance"/>, made before the first scene loads and
    /// kept across scene loads. A popup may need to appear before any scene's container is built,
    /// or between scenes when none is.</item>
    /// </list>
    /// <para>
    /// Its canvas is at <see cref="SortingOrder"/>, the highest Unity allows. Dialogs sit one below
    /// it, so a popup can appear over a question as well as over the game, the menus and the
    /// loading screen.
    /// </para>
    /// <para>
    /// Popups derive from <see cref="PopupBase"/> and are prefabs: <see cref="Show{T}(T)"/> copies
    /// one, shows it at once and destroys it after its <c>HideComplete</c>. None is built from code -
    /// that costs memory and time at runtime for what a prefab already holds. And only in Play:
    /// outside it nothing is made at all (see <see cref="Instance"/>).
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PopupManager : MonoBehaviour
    {
        /// <summary>The popup canvas's sort order. The highest there is: nothing draws over a popup.</summary>
        public const int SortingOrder = short.MaxValue;

        const string LogPrefix = "[Popups]";

        static PopupManager instance;

        readonly List<PopupBase> live = new List<PopupBase>();

        /// <summary>
        /// The one popup layer, made now if there is not one yet. Null outside Play: there is
        /// nobody to show a popup to, and nothing is made.
        /// </summary>
        /// <remarks>
        /// Popups are prefabs put on screen while the game runs, never in the editor. The usual way
        /// to be asked outside Play is a task that finishes after Play was stopped - a sign-in the
        /// browser was still waiting on. Anything made then lands in the scene open in the editor,
        /// where nothing clears it away, and is saved with it. So nothing is made, and a warning
        /// says what was asked for. Callers that can run that late check for null.
        /// </remarks>
        public static PopupManager Instance
        {
            get
            {
                if (instance == null)
                {
                    if (!Application.isPlaying && !AllowedOutsidePlay)
                    {
                        Debug.LogWarning(LogPrefix + " A popup was asked for outside Play, so nothing was made. "
                            + "Something is still running after Play was stopped.");
                        return null;
                    }

                    instance = new GameObject(nameof(PopupManager), typeof(RectTransform))
                        .AddComponent<PopupManager>();

                    // Awake does not run outside Play, so the layer is set up here as well.
                    instance.SetUp();
                }

                return instance;
            }
        }

        /// <summary>The edit-mode tests' leave to make the layer outside Play. Nothing else sets it.</summary>
        internal static bool AllowedOutsidePlay { get; set; }

        /// <summary>Whether the layer exists yet, without making it.</summary>
        public static bool HasInstance => instance != null;

        /// <summary>How many popups are on the layer.</summary>
        public int Count
        {
            get
            {
                live.RemoveAll(popup => popup == null);
                return live.Count;
            }
        }

        /// <summary>
        /// Cleared at the very start of every play session, so the layer from the last session is
        /// never taken for this one when domain reload is switched off.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ForgetLastSession()
        {
            instance = null;
        }

        /// <summary>
        /// Made before the first scene loads, so the layer is alive - and in the hierarchy under
        /// DontDestroyOnLoad - from the first frame, rather than only once something shows a popup.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void WakeFirst()
        {
            _ = Instance;
        }

        void Awake()
        {
            if (instance != null && instance != this)
            {
                // One layer. A copy authored into a scene gives way to the one already alive.
                Destroy(gameObject);
                return;
            }

            instance = this;
            SetUp();
        }

        /// <summary>The canvas, once; and survival across scene loads, in Play.</summary>
        bool setUp;

        void SetUp()
        {
            if (setUp)
            {
                return;
            }

            setUp = true;

            DialogChrome.MakeOverlay(gameObject);
            GetComponent<Canvas>().sortingOrder = SortingOrder;

            if (Application.isPlaying)
            {
                DontDestroyOnLoad(gameObject);
            }
        }

        void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        /// <summary>
        /// Makes a popup from <paramref name="prefab"/>, and shows it. The only way to show one:
        /// every popup is a prefab, and none is built from code at runtime.
        /// </summary>
        public T Show<T>(T prefab) where T : PopupBase
        {
            if (prefab == null)
            {
                Debug.LogError(LogPrefix + " Asked to show a popup with no prefab.", this);
                return null;
            }

            var popup = Instantiate(prefab, transform, false);
            popup.name = prefab.name;

            return Adopt(popup);
        }

        /// <summary>The first popup of this kind on the layer, or null.</summary>
        public T Find<T>() where T : PopupBase
        {
            foreach (var popup in live)
            {
                if (popup != null && popup is T found)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>Starts <paramref name="popup"/> hiding. It is taken away once it has.</summary>
        public void Hide(PopupBase popup)
        {
            if (popup != null)
            {
                popup.Hide();
            }
        }

        /// <summary>Starts every popup on the layer hiding.</summary>
        public void HideAll()
        {
            // A copy, because each one leaves the list as it finishes hiding.
            foreach (var popup in live.ToArray())
            {
                Hide(popup);
            }
        }

        /// <summary>Takes a new popup onto the layer, on top of the others, and shows it.</summary>
        T Adopt<T>(T popup) where T : PopupBase
        {
            if (popup.transform.parent != transform)
            {
                popup.transform.SetParent(transform, false);
            }

            popup.transform.SetAsLastSibling();

            live.Add(popup);
            popup.Hidden += Retire;

            popup.EnsureBuilt();
            popup.Show();

            return popup;
        }

        /// <summary>A popup has finished hiding: off the layer and gone.</summary>
        void Retire(PopupBase popup)
        {
            popup.Hidden -= Retire;
            live.Remove(popup);

            if (Application.isPlaying)
            {
                Destroy(popup.gameObject);
            }
            else
            {
                DestroyImmediate(popup.gameObject);
            }
        }
    }
}
