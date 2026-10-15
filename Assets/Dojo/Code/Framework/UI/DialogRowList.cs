using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Framework.UI
{
    /// <summary>
    /// A pooled list of pickable rows inside a dialog: shows a set of names, remembers which one is
    /// chosen, and paints it.
    /// </summary>
    /// <remarks>
    /// A plain object rather than a component, because it owns nothing a scene needs to know about
    /// — it is a handful of buttons under a transform its dialog already has. Shared by the dialogs
    /// that offer a list so selection behaves identically in each.
    /// <para>
    /// Rows are pooled. Reopening a dialog would otherwise churn a GameObject per entry per open,
    /// and a list of saved worlds is opened far more often than it changes.
    /// </para>
    /// </remarks>
    public sealed class DialogRowList
    {
        readonly RectTransform parent;
        readonly List<Button> rows = new List<Button>();
        readonly List<string> options = new List<string>();

        /// <summary>Raised whenever the chosen row changes, including when it is cleared.</summary>
        public event Action Changed;

        public DialogRowList(RectTransform parent)
        {
            this.parent = parent;
        }

        /// <summary>Index of the chosen row, or -1 when nothing is chosen.</summary>
        public int SelectedIndex { get; private set; } = -1;

        /// <summary>The chosen name, or null.</summary>
        public string Selected =>
            SelectedIndex >= 0 && SelectedIndex < options.Count ? options[SelectedIndex] : null;

        /// <summary>How many rows are on offer.</summary>
        public int Count => options.Count;

        /// <summary>Replaces what is on offer. Nothing is chosen afterwards.</summary>
        public void Show(IList<string> names)
        {
            options.Clear();

            if (names != null)
            {
                options.AddRange(names);
            }

            SelectedIndex = -1;

            while (rows.Count < options.Count)
            {
                rows.Add(Build(rows.Count));
            }

            for (var i = 0; i < rows.Count; i++)
            {
                var visible = i < options.Count;
                rows[i].gameObject.SetActive(visible);

                if (visible)
                {
                    rows[i].GetComponentInChildren<TMP_Text>(true).text = options[i];
                }
            }

            Paint();
            Changed?.Invoke();
        }

        /// <summary>Un-chooses whatever was chosen, without disturbing the list.</summary>
        public void ClearSelection()
        {
            if (SelectedIndex == -1)
            {
                return;
            }

            SelectedIndex = -1;
            Paint();
            Changed?.Invoke();
        }

        /// <summary>Whether a name is one of the rows on offer. Matched ignoring case.</summary>
        public bool Contains(string name) => Match(name) != null;

        /// <summary>
        /// The row's own spelling of this name, or null when it is not on offer.
        /// </summary>
        /// <remarks>
        /// Matching ignores case, so what the player typed and what is stored can differ. This
        /// hands back the stored one, which is what should be written — replacing "Reception" by
        /// typing "reception" should not quietly rename it.
        /// </remarks>
        public string Match(string name)
        {
            foreach (var option in options)
            {
                if (string.Equals(option, name, StringComparison.OrdinalIgnoreCase))
                {
                    return option;
                }
            }

            return null;
        }

        /// <summary>Chooses the row with this name, if it is on offer.</summary>
        public void Select(string name)
        {
            for (var i = 0; i < options.Count; i++)
            {
                if (string.Equals(options[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    Pick(i);
                    return;
                }
            }

            ClearSelection();
        }

        void Pick(int index)
        {
            if (SelectedIndex == index)
            {
                return;
            }

            SelectedIndex = index;
            Paint();
            Changed?.Invoke();
        }

        void Paint()
        {
            for (var i = 0; i < rows.Count; i++)
            {
                var image = rows[i].targetGraphic as Image;

                if (image != null)
                {
                    image.color = i == SelectedIndex ? DialogChrome.RowChosen : DialogChrome.Row;
                }
            }
        }

        Button Build(int index)
        {
            var button = DialogChrome.AddButton(
                "Row" + index, parent, Vector2.zero, new Vector2(560f, 56f), string.Empty, DialogChrome.Row);

            var rect = (RectTransform)button.transform;
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);

            var label = button.GetComponentInChildren<TMP_Text>(true);
            label.alignment = TextAlignmentOptions.Left;

            var labelRect = (RectTransform)label.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(20f, 0f);
            labelRect.offsetMax = new Vector2(-20f, 0f);

            var captured = index;
            button.onClick.AddListener(() => Pick(captured));

            return button;
        }
    }
}
