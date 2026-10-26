using TMPro;
using UnityEngine;

namespace Dojo.Framework.UI
{
    /// <summary>
    /// A dropdown for inside a popup: its list opens over the popup instead of behind it.
    /// </summary>
    /// <remarks>
    /// <c>TMP_Dropdown</c> gives its open list a canvas of its own at sort order 30000, and the
    /// blocker that closes it one below. The popup layer is at <see cref="PopupManager.SortingOrder"/>,
    /// the highest order there is, so in a popup the list was drawn under the card: only what hung
    /// below it showed, and nothing could be picked (Bob, 23 Oct 2026). No higher order exists to
    /// give the list instead, so once it is open it is lifted into the popup itself and drawn last
    /// (<see cref="PopupLayering.Lift"/>). Outside a popup it behaves as a plain dropdown.
    /// </remarks>
    public class PopupDropdown : TMP_Dropdown
    {
        /// <summary>
        /// Called by <c>Show</c> once the list is built and placed, the last step of opening, so the
        /// list and the blocker can both be lifted here.
        /// </summary>
        protected override GameObject CreateBlocker(Canvas rootCanvas)
        {
            var blocker = base.CreateBlocker(rootCanvas);

            var popup = GetComponentInParent<PopupBase>(true);
            if (popup != null && template != null && template.parent != null)
            {
                PopupLayering.Lift(popup.transform, template.parent.Find("Dropdown List"), blocker != null ? blocker.transform : null);
            }

            return blocker;
        }
    }

    /// <summary>
    /// Puts a dropdown's open list, and the blocker that closes it, inside the popup it opened in.
    /// </summary>
    public static class PopupLayering
    {
        /// <summary>
        /// Makes <paramref name="list"/> and <paramref name="blocker"/> part of <paramref name="popup"/>,
        /// with no sort order of their own, drawn after everything else in it: the blocker covering
        /// the popup, so a click anywhere else on it closes the list, and the list over the blocker.
        /// </summary>
        public static void Lift(Transform popup, Transform list, Transform blocker)
        {
            if (popup == null)
            {
                return;
            }

            if (blocker != null)
            {
                blocker.SetParent(popup, false);

                if (blocker is RectTransform cover)
                {
                    cover.anchorMin = Vector2.zero;
                    cover.anchorMax = Vector2.one;
                    cover.sizeDelta = Vector2.zero;
                    cover.anchoredPosition = Vector2.zero;
                }

                OwnOrderOff(blocker);
                blocker.SetAsLastSibling();
            }

            if (list != null)
            {
                // Where it was placed under the field, kept.
                list.SetParent(popup, true);
                OwnOrderOff(list);
                list.SetAsLastSibling();
            }
        }

        static void OwnOrderOff(Transform part)
        {
            var canvas = part.GetComponent<Canvas>();
            if (canvas != null)
            {
                canvas.overrideSorting = false;
            }
        }
    }
}
