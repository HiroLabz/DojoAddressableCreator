using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Framework.UI
{
    /// <summary>
    /// Base for a panel that parks off screen and slides in when toggled. Derive from it for each
    /// group in the game and you inherit the toggle wiring, the remembered closed position and the
    /// tween; a subclass usually only adds its own content.
    /// </summary>
    /// <remarks>
    /// Author the panel in its <em>closed</em> position. That is what gets captured in
    /// <see cref="Awake"/> and what <see cref="Hide"/> returns to.
    /// </remarks>
    [RequireComponent(typeof(RectTransform))]
    public abstract class InGamePopup : MonoBehaviour, IInGamePopup
    {
        [Header("Popup")]
        [Tooltip("Optional. Toggles the popup when clicked. Leave empty to drive it from code.")]
        [SerializeField] protected Button toggleButton;

        [Tooltip("Seconds the slide takes.")]
        [SerializeField] protected float tweenSeconds = 0.35f;

        [Tooltip("Easing on the way in.")]
        [SerializeField] protected LeanTweenType easeIn = LeanTweenType.easeOutBack;

        [Tooltip("Easing on the way out.")]
        [SerializeField] protected LeanTweenType easeOut = LeanTweenType.easeInBack;

        [Tooltip("Deactivate the GameObject once it has slid off screen, and reactivate it on Show. " +
                 "Leave this off for a popup whose own toggle button lives inside it, or it could " +
                 "never be reopened.")]
        [SerializeField] protected bool deactivateWhenHidden;

        /// <summary>The slide currently running, or -1. Cancelled before a new one starts.</summary>
        protected int TweenId { get; set; } = -1;

        public bool IsOpen { get; protected set; }

        /// <summary>This popup's RectTransform. Valid from <see cref="Awake"/> onwards.</summary>
        protected RectTransform Rect { get; set; }

        /// <summary>The authored position, which is where <see cref="Hide"/> returns to.</summary>
        protected Vector2 HiddenPosition { get; set; }

        /// <remarks>Overriding subclasses must call base.Awake() before their own setup.</remarks>
        protected virtual void Awake()
        {
            Rect = (RectTransform)transform;

            // Captured before anything moves it, so the closed position survives repeated toggling.
            HiddenPosition = Rect.anchoredPosition;

            if (toggleButton != null)
            {
                toggleButton.onClick.AddListener(Toggle);
            }
        }

        protected virtual void OnDestroy()
        {
            if (toggleButton != null)
            {
                toggleButton.onClick.RemoveListener(Toggle);
            }

            CancelTween();
        }

        public virtual void Toggle()
        {
            IsOpen = !IsOpen;

            if (IsOpen)
            {
                Show();
            }
            else
            {
                Hide();
            }
        }

        public virtual void Show()
        {
            // A popup left disabled in the hierarchy has not run Awake yet, and LeanTween will not
            // drive an inactive object. Activating first lets Awake capture the closed position
            // before anything moves.
            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }

            IsOpen = true;
            TweenTo(ShownPosition(), easeIn);
        }

        public virtual void Hide()
        {
            IsOpen = false;

            if (!gameObject.activeInHierarchy)
            {
                return;   // nothing to slide, and no coroutine host to do it with
            }

            TweenTo(HiddenPosition, easeOut, deactivateWhenHidden ? DeactivateSelf : (System.Action)null);
        }

        protected virtual void DeactivateSelf() => gameObject.SetActive(false);

        /// <summary>
        /// Where the popup rests when open. The default slides it in from the left until its left
        /// edge meets the parent's left edge, which suits a tray parked off the left of the canvas.
        /// Override for a popup that comes in from another edge.
        /// </summary>
        /// <remarks>
        /// Computed from the current rects rather than stored, so it stays correct through a
        /// resolution change or a CanvasScaler rescale.
        /// </remarks>
        protected virtual Vector2 ShownPosition()
        {
            var parent = Rect.parent as RectTransform;
            if (parent == null)
            {
                return HiddenPosition;
            }

            // anchoredPosition is measured from the anchor, so locate the anchor within the parent
            // first, then offset by this popup's own pivot.
            var anchorX = parent.rect.xMin + parent.rect.width * ((Rect.anchorMin.x + Rect.anchorMax.x) * 0.5f);
            var shownX = parent.rect.xMin - anchorX + Rect.rect.width * Rect.pivot.x;
            return new Vector2(shownX, HiddenPosition.y);
        }

        /// <summary>Slides to <paramref name="target"/>, replacing any slide already running.</summary>
        protected virtual void TweenTo(Vector2 target, LeanTweenType ease, System.Action onComplete = null)
        {
            CancelTween();

            TweenId = LeanTween.value(gameObject, Rect.anchoredPosition, target, tweenSeconds)
                .setEase(ease)
                .setIgnoreTimeScale(true)      // menus must still work while the game is paused
                .setOnUpdateVector2(value => Rect.anchoredPosition = value)
                .setOnComplete(() =>
                {
                    TweenId = -1;
                    onComplete?.Invoke();
                })
                .id;
        }

        protected virtual void CancelTween()
        {
            if (TweenId != -1)
            {
                LeanTween.cancel(TweenId);
                TweenId = -1;
            }
        }
    }
}
