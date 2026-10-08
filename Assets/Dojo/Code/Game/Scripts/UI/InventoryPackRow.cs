using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Game.UI
{
    /// <summary>
    /// One row in the pack filter's menu: a pack, how many items the player holds in it, and
    /// whether it is the one being shown.
    /// </summary>
    /// <remarks>
    /// Split out from <see cref="InventoryPackFilter"/> so the menu can be pooled. The filter
    /// creates as many of these as it needs once and then rebinds them, which matters because the
    /// menu is rebuilt on every category change and destroying rows each time is work on a frame
    /// that is already laying out a grid.
    /// </remarks>
    public sealed class InventoryPackRow : MonoBehaviour
    {
        [Header("Row")]
        [Tooltip("The whole row. Clicking it chooses this pack.")]
        [SerializeField] Button button;

        [Tooltip("The pack name, e.g. DEFAULT PACK.")]
        [SerializeField] TextMeshProUGUI label;

        [Tooltip("How many items the player holds in this pack.")]
        [SerializeField] TextMeshProUGUI countLabel;

        [Header("States")]
        [Tooltip("Shown only on the row for the pack currently being shown.")]
        [SerializeField] GameObject selectedState;

        [Tooltip("The coloured diamond. Tinted per pack so the default and a bought pack read apart.")]
        [SerializeField] Image marker;

        /// <summary>Raised with this row's pack key when the player picks it.</summary>
        public event Action<string> Clicked;

        /// <summary>The pack this row stands for, or empty for the all-packs row.</summary>
        public string PackKey { get; private set; } = string.Empty;

        /// <summary>What the row reads. Used by the filter to label its trigger.</summary>
        public string Label { get; private set; } = string.Empty;

        void Awake()
        {
            if (button != null)
            {
                button.onClick.AddListener(Raise);
            }
        }

        void OnDestroy()
        {
            if (button != null)
            {
                button.onClick.RemoveListener(Raise);
            }
        }

        /// <summary>Fills the row in from one entry.</summary>
        public void Bind(InventoryPackFilter.PackEntry entry)
        {
            PackKey = entry.Key;
            Label = entry.Label;

            if (label != null)
            {
                label.text = entry.Label;
            }

            if (countLabel != null)
            {
                countLabel.text = entry.Count.ToString();
            }

            // The all-packs row is a heading rather than a pack, so it carries no marker. Hiding it
            // rather than tinting it grey keeps the row from reading as a pack with no colour.
            if (marker != null)
            {
                marker.enabled = !string.IsNullOrEmpty(entry.Key);
            }
        }

        /// <summary>Lights the row for the pack being shown.</summary>
        public void SetSelected(bool selected)
        {
            if (selectedState != null)
            {
                selectedState.SetActive(selected);
            }
        }

        void Raise()
        {
            var handler = Clicked;
            if (handler != null)
            {
                handler(PackKey);
            }
        }
    }
}
