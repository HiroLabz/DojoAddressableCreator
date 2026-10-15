using UnityEngine;
using UnityEngine.EventSystems;

namespace Dojo.Game.UI
{
    /// <summary>
    /// Lets a window be dragged around its canvas, and keeps the whole of it on screen.
    /// </summary>
    /// <remarks>
    /// The clamp is the point. A window dragged half off the edge takes its own title bar with it,
    /// and the title bar is the only thing left to grab once it is minimised — so a window that may
    /// leave the screen is a window that can be lost.
    /// <para>
    /// Positions are worked out in the parent's own space rather than in screen pixels, so the same
    /// numbers hold at any resolution and under any canvas scale. That means this only works while
    /// the window is centre-anchored, which <see cref="ChatAgentWindow"/> guarantees for the two
    /// sizes that can be dragged; a maximised window fills the screen and is not draggable at all.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(RectTransform))]
    public sealed class ChatWindowDrag : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        [Tooltip("What actually moves. Defaults to this object.")]
        [SerializeField] RectTransform target;

        [Tooltip("How close to the screen edge the window may be pushed.")]
        [SerializeField] float margin = 8f;

        /// <summary>Whether dragging is allowed at all. Off while the window fills the screen.</summary>
        public bool CanDrag { get; set; } = true;

        RectTransform Parent => target != null ? target.parent as RectTransform : null;

        Vector2 grabOffset;

        void Awake()
        {
            if (target == null)
            {
                target = (RectTransform)transform;
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!CanDrag || target == null || Parent == null)
            {
                return;
            }

            Vector2 pointer;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    Parent, eventData.position, eventData.pressEventCamera, out pointer))
            {
                // Where inside the window it was grabbed, so it does not jump to centre on the
                // first frame of the drag.
                grabOffset = target.anchoredPosition - pointer;
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!CanDrag || target == null || Parent == null)
            {
                return;
            }

            Vector2 pointer;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    Parent, eventData.position, eventData.pressEventCamera, out pointer))
            {
                return;
            }

            target.anchoredPosition = Clamp(pointer + grabOffset);
        }

        /// <summary>Puts the window back inside the screen, wherever it currently is.</summary>
        public void ClampIntoView()
        {
            if (target != null)
            {
                target.anchoredPosition = Clamp(target.anchoredPosition);
            }
        }

        /// <summary>
        /// The furthest the window may sit from the middle and still be wholly on screen.
        /// </summary>
        /// <remarks>
        /// Floored at zero: a window wider than the screen has no legal range, and a negative one
        /// would clamp it to the wrong side of centre and look like a jump.
        /// </remarks>
        public Vector2 Limit()
        {
            var parent = Parent;

            if (parent == null || target == null)
            {
                return Vector2.zero;
            }

            Vector2 half = (parent.rect.size - target.rect.size) * 0.5f;
            half -= new Vector2(margin, margin);

            return new Vector2(Mathf.Max(0f, half.x), Mathf.Max(0f, half.y));
        }

        Vector2 Clamp(Vector2 position)
        {
            Vector2 limit = Limit();

            return new Vector2(
                Mathf.Clamp(position.x, -limit.x, limit.x),
                Mathf.Clamp(position.y, -limit.y, limit.y));
        }
    }
}
