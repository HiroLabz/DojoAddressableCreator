using TMPro;
using UnityEngine;

namespace Dojo.Game.UI
{
    /// <summary>
    /// An area's label, from the mock-up: a small dark card up and to the left of its pin - the
    /// area's name, and under it "3F · 12m · 2 AGENTS" - with a thin line down to the pin's head.
    /// </summary>
    /// <remarks>
    /// Drawn on the screen rather than in the world, so it stays the same size and crisp however
    /// far the camera is zoomed out. Its own spot is the pin's head; the card sits a fixed way off
    /// it and grows to fit the words, and the line runs from the card's bottom edge to the spot.
    /// <para>
    /// <see cref="AreaLocators"/> copies this from the template in the Game scene, once per area.
    /// </para>
    /// </remarks>
    public sealed class AreaLocatorLabel : MonoBehaviour
    {
        [Tooltip("The card. Its pivot is the middle of its bottom edge, where the line leaves it.")]
        [SerializeField] RectTransform card;

        [SerializeField] TMP_Text title;
        [SerializeField] TMP_Text details;

        [Tooltip("The line to the pin: an image pivoted at its left end.")]
        [SerializeField] RectTransform leader;

        [Tooltip("Room either side of the words, inside the card.")]
        [SerializeField] float padding = 26f;

        [Tooltip("The narrowest the card may be.")]
        [SerializeField] float minWidth = 160f;

        RectTransform rect;

        void Awake()
        {
            rect = (RectTransform)transform;
            Lead();
        }

        /// <summary>What it says. The card grows or shrinks to fit.</summary>
        public void Set(string name, string line)
        {
            if (title != null) title.text = name;
            if (details != null) details.text = line;

            if (card == null)
            {
                return;
            }

            var words = Mathf.Max(
                title != null ? title.GetPreferredValues(title.text).x : 0f,
                details != null ? details.GetPreferredValues(details.text).x : 0f);

            card.sizeDelta = new Vector2(Mathf.Max(minWidth, words + 2f * padding), card.sizeDelta.y);
        }

        /// <summary>Puts the label's spot - the end of its line - at this point on its canvas.</summary>
        public void Place(Vector2 head)
        {
            if (rect == null)
            {
                rect = (RectTransform)transform;
            }

            rect.anchoredPosition = head;
        }

        /// <summary>Lays the line from the middle of the card's bottom edge to the label's spot.</summary>
        void Lead()
        {
            if (leader == null || card == null)
            {
                return;
            }

            var from = card.anchoredPosition;
            var along = -from;

            leader.anchoredPosition = from;
            leader.sizeDelta = new Vector2(along.magnitude, leader.sizeDelta.y);
            leader.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(along.y, along.x) * Mathf.Rad2Deg);
        }
    }
}
