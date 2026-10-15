using System;
using UnityEngine;

namespace Dojo.Framework.UI
{
    /// <summary>
    /// Something <see cref="PopupManager"/> puts on the popup layer: shown, then hidden, then gone.
    /// </summary>
    /// <remarks>
    /// Four steps, each its own method so a popup can take over any of them:
    /// <list type="number">
    /// <item><see cref="Show"/> starts it coming on screen.</item>
    /// <item><see cref="ShowComplete"/> is called when it has finished arriving - after the fade,
    /// or whatever animation a popup plays instead.</item>
    /// <item><see cref="Hide"/> starts it going.</item>
    /// <item><see cref="HideComplete"/> is called when it has finished leaving. The manager destroys
    /// it after this, so nothing has to remember to clean up.</item>
    /// </list>
    /// The same four names the dialogs use, so a popup and a dialog read alike. The default is a
    /// short fade on a <see cref="CanvasGroup"/>; override <see cref="Show"/> and <see cref="Hide"/>
    /// to animate differently, and call the Complete half when the animation ends.
    /// <para>
    /// Every popup is a prefab: its parts are authored, never built from code at runtime. What
    /// needs doing once - hooking up its buttons - goes in <see cref="Build"/>, which runs exactly
    /// once, before the first <see cref="Show"/>.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(RectTransform))]
    public abstract class PopupBase : MonoBehaviour
    {
        /// <summary>Where a popup is on its way on and off the screen.</summary>
        public enum Phase
        {
            Hidden,
            Showing,
            Shown,
            Hiding,
        }

        [Tooltip("Seconds the default fade in and out takes. Zero shows and hides at once.")]
        [SerializeField] float fadeSeconds = 0.15f;

        CanvasGroup group;
        bool built;
        int fade = -1;

        /// <summary>Where it is now.</summary>
        public Phase State { get; private set; } = Phase.Hidden;

        /// <summary>True from <see cref="Show"/> until <see cref="Hide"/>.</summary>
        public bool IsOpen => State == Phase.Showing || State == Phase.Shown;

        /// <summary>Raised once it has fully arrived, after <see cref="ShowComplete"/>.</summary>
        public event Action<PopupBase> Shown;

        /// <summary>Raised once it has fully gone, after <see cref="HideComplete"/>.</summary>
        public event Action<PopupBase> Hidden;

        /// <summary>The group the default fade drives, made if the popup has none.</summary>
        protected CanvasGroup Group
        {
            get
            {
                if (group == null)
                {
                    group = GetComponent<CanvasGroup>();

                    if (group == null)
                    {
                        group = gameObject.AddComponent<CanvasGroup>();
                    }
                }

                return group;
            }
        }

        /// <summary>
        /// One-time setup of the prefab's authored parts, such as hooking up its buttons. Runs once,
        /// before the first <see cref="Show"/>. Never builds parts: the prefab has them. Nothing to
        /// do by default.
        /// </summary>
        protected virtual void Build()
        {
        }

        /// <summary>Starts it coming on screen. Does nothing if it is already there or on its way.</summary>
        public virtual void Show()
        {
            if (IsOpen)
            {
                return;
            }

            EnsureBuilt();

            // From nothing, or the fade in is never seen: a new popup's group starts fully opaque.
            if (State == Phase.Hidden)
            {
                Group.alpha = 0f;
            }

            gameObject.SetActive(true);
            State = Phase.Showing;

            // Not clickable until it has fully arrived, so a stray click during the fade cannot
            // press a button the player has not seen yet.
            Group.interactable = false;
            Group.blocksRaycasts = true;

            FadeTo(1f, ShowComplete);
        }

        /// <summary>It has fully arrived: it can be used now.</summary>
        public virtual void ShowComplete()
        {
            if (State != Phase.Showing)
            {
                return;
            }

            State = Phase.Shown;
            Group.alpha = 1f;
            Group.interactable = true;

            var handler = Shown;
            if (handler != null)
            {
                handler(this);
            }
        }

        /// <summary>Starts it going. Does nothing if it is already gone or on its way.</summary>
        public virtual void Hide()
        {
            if (!IsOpen)
            {
                return;
            }

            State = Phase.Hiding;

            // Clicks go straight through a popup that is leaving.
            Group.interactable = false;
            Group.blocksRaycasts = false;

            FadeTo(0f, HideComplete);
        }

        /// <summary>It has fully gone. The manager takes it away after this.</summary>
        public virtual void HideComplete()
        {
            if (State != Phase.Hiding)
            {
                return;
            }

            State = Phase.Hidden;
            Group.alpha = 0f;
            gameObject.SetActive(false);

            var handler = Hidden;
            if (handler != null)
            {
                handler(this);
            }
        }

        /// <summary>
        /// Builds the popup if it has not been, once. Called by the manager as it adopts a popup,
        /// and by <see cref="Show"/> for one that was shown some other way.
        /// </summary>
        /// <remarks>
        /// Not left to Awake: a popup made and shown in the same breath - which is how the manager
        /// makes every one - would otherwise be shown before its parts exist, and outside Play Awake
        /// does not run at all.
        /// </remarks>
        internal void EnsureBuilt()
        {
            if (built)
            {
                return;
            }

            built = true;
            Build();
        }

        /// <summary>Fades to <paramref name="alpha"/>, then calls <paramref name="done"/>.</summary>
        /// <remarks>
        /// On unscaled time, so a popup still arrives while the game is paused. Outside Play, where
        /// no tween runs, it simply jumps there.
        /// </remarks>
        void FadeTo(float alpha, Action done)
        {
            if (fade >= 0)
            {
                LeanTween.cancel(fade);
                fade = -1;
            }

            if (fadeSeconds <= 0f || !Application.isPlaying)
            {
                Group.alpha = alpha;
                done();
                return;
            }

            fade = LeanTween.alphaCanvas(Group, alpha, fadeSeconds)
                .setIgnoreTimeScale(true)
                .setOnComplete(() =>
                {
                    fade = -1;
                    done();
                })
                .id;
        }

        protected virtual void OnDestroy()
        {
            if (fade >= 0)
            {
                LeanTween.cancel(fade);
                fade = -1;
            }
        }
    }
}
