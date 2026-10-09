using System;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Game.UI
{
    /// <summary>
    /// The strip of category buttons down the left of the inventory. Reports which one is showing
    /// and nothing else.
    /// </summary>
    /// <remarks>
    /// The rail knows the names of categories but not their contents: it never touches the
    /// catalogue, never counts an item and never decides whether a category is worth offering. That
    /// belongs to <see cref="InventoryHud"/>, which is what lets the two be looked at separately —
    /// a rail that highlights the wrong button and a grid that fills with the wrong items are
    /// different faults in different files.
    /// <para>
    /// Entries are a serialised list rather than one field per button. <c>ItemCategories</c> is
    /// deliberately not an enum, because the set of categories grows with the content rather than
    /// with the code; a rail hard-coded to ten fields would have to be edited to show an eleventh.
    /// </para>
    /// </remarks>
    public sealed class InventoryRail : MonoBehaviour
    {
        /// <summary>One button on the rail, and the category it stands for.</summary>
        [Serializable]
        public sealed class Entry
        {
            [Tooltip("The button itself.")]
            public Button button;

            [Tooltip("Category key this selects, e.g. 'walls'. Matches ItemCategories where the " +
                     "tab shows items; 'you' and 'agents' name tabs that show something else.")]
            public string category;

            [Tooltip("Shown only while this entry is the selected one. Usually the lit plate.")]
            public GameObject selected;
        }

        [Header("Entries")]
        [Tooltip("Top to bottom, in the order they sit on the rail.")]
        [SerializeField] Entry[] entries = new Entry[0];

        [Header("Selected")]
        [Tooltip("How much bigger the selected button is than the rest.")]
        [SerializeField] float selectedScale = 1.1f;

        [Tooltip("Seconds a button takes to grow when selected, or shrink back when not.")]
        [SerializeField] float scaleSeconds = 0.18f;

        /// <summary>Raised with the category key of the entry the player pressed.</summary>
        public event Action<string> CategorySelected;

        /// <summary>The category showing, or empty before anything has been picked.</summary>
        public string Selected { get; private set; } = string.Empty;

        void Awake()
        {
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (entry == null || entry.button == null)
                {
                    continue;
                }

                // Captured into a local so every listener closes over its own entry rather than
                // over whichever one the loop finished on.
                string category = entry.category;
                entry.button.onClick.AddListener(() => Select(category));
            }

            ApplySelection(false);
        }

        void OnDestroy()
        {
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i] != null && entries[i].button != null)
                {
                    entries[i].button.onClick.RemoveAllListeners();
                }
            }
        }

        /// <summary>
        /// Shows a category as selected and tells anyone listening. Pressing the entry already
        /// showing re-raises it, so a caller can rely on the event to mean "the player asked for
        /// this" rather than "this changed".
        /// </summary>
        public void Select(string category)
        {
            Selected = category ?? string.Empty;
            ApplySelection(true);

            var handler = CategorySelected;
            if (handler != null)
            {
                handler(Selected);
            }
        }

        /// <summary>Takes a category's entry off the rail, so it cannot be picked.</summary>
        /// <remarks>
        /// The entries below move up into its place, so a hidden tab leaves no hole in the rail.
        /// </remarks>
        public void Hide(string category)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];

                if (entry == null || entry.button == null || !entry.button.gameObject.activeSelf
                    || !string.Equals(entry.category, category, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var rect = (RectTransform)entry.button.transform;
                var step = StepAt(i);

                entry.button.gameObject.SetActive(false);

                for (int j = i + 1; j < entries.Length; j++)
                {
                    if (entries[j] != null && entries[j].button != null)
                    {
                        var below = (RectTransform)entries[j].button.transform;
                        below.anchoredPosition += new Vector2(0f, step);
                    }
                }
            }
        }

        /// <summary>How far apart the entries stand, measured at <paramref name="index"/>.</summary>
        float StepAt(int index)
        {
            var here = entries[index].button.transform as RectTransform;
            var next = index + 1 < entries.Length && entries[index + 1] != null && entries[index + 1].button != null
                ? entries[index + 1].button.transform as RectTransform
                : index > 0 && entries[index - 1] != null && entries[index - 1].button != null
                    ? entries[index - 1].button.transform as RectTransform
                    : null;

            return here != null && next != null ? Mathf.Abs(here.anchoredPosition.y - next.anchoredPosition.y) : 0f;
        }

        /// <summary>Clears the selection without raising anything. What the screen closes to.</summary>
        public void Clear()
        {
            Selected = string.Empty;
            ApplySelection(false);
        }

        /// <summary>
        /// Lights the selected entry's plate and grows it a little, and puts every other entry
        /// back - eased when <paramref name="animate"/>, at once otherwise (on waking, and on
        /// closing, when nothing is on screen to watch it).
        /// </summary>
        void ApplySelection(bool animate)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (entry == null)
                {
                    continue;
                }

                var on = !string.IsNullOrEmpty(Selected) &&
                         string.Equals(entry.category, Selected, StringComparison.OrdinalIgnoreCase);

                if (entry.selected != null)
                {
                    entry.selected.SetActive(on);
                }

                if (entry.button != null)
                {
                    Resize((RectTransform)entry.button.transform, on ? selectedScale : 1f, on, animate);
                }
            }
        }

        void Resize(RectTransform button, float scale, bool growing, bool animate)
        {
            LeanTween.cancel(button.gameObject);
            var target = new Vector3(scale, scale, 1f);

            if (!animate || !Application.isPlaying || scaleSeconds <= 0f)
            {
                button.localScale = target;
                return;
            }

            // A small overshoot on the way up, so the choice lands; a plain ease back down.
            LeanTween.scale(button, target, scaleSeconds)
                .setEase(growing ? LeanTweenType.easeOutBack : LeanTweenType.easeOutCubic)
                .setIgnoreTimeScale(true);
        }
    }
}
