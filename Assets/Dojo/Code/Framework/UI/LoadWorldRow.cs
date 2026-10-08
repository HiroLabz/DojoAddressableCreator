using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Framework.UI
{
    /// <summary>
    /// One world in the load dialog's list: a thumbnail, the name, the counts and save time, an
    /// autosave badge, and a delete button.
    /// </summary>
    /// <remarks>
    /// Authored as a prefab and instantiated per world. Serialized references rather than lookups
    /// by child name: a row found by name breaks silently the first time someone renames a child in
    /// the Inspector, while a missing reference is a null the console names.
    /// <para>
    /// The row reports being picked and being deleted, and decides neither. Which row is selected is
    /// the dialog's business, because only the dialog knows that picking this one un-picks another.
    /// </para>
    /// </remarks>
    public sealed class LoadWorldRow : MonoBehaviour
    {
        [Tooltip("Clicking anywhere on the row picks it.")]
        [SerializeField] Button pickButton;

        [Tooltip("Shown while this row is the chosen one.")]
        [SerializeField] GameObject selectedHighlight;

        [Tooltip("The world's name.")]
        [SerializeField] TMP_Text nameLabel;

        [Tooltip("Counts and save time, on one line.")]
        [SerializeField] TMP_Text summaryLabel;

        [Tooltip("The AUTO badge. Shown only for the automatic save.")]
        [SerializeField] GameObject autoBadge;

        [Tooltip("Removes this world. Hidden when the dialog was opened without a delete handler.")]
        [SerializeField] Button deleteButton;

        [Tooltip("Optional. Writes over this world. Shown by the save dialog on the selected row " +
                 "only; left unassigned the row simply never offers it.")]
        [SerializeField] Button replaceButton;

        /// <summary>Raised when the player picks this row.</summary>
        public event Action<LoadWorldRow> Picked;

        /// <summary>Raised when the player asks to delete this row's world.</summary>
        public event Action<LoadWorldRow> Deleted;

        /// <summary>
        /// Raised when the player asks to write over this row's world.
        /// </summary>
        /// <remarks>
        /// Only the save dialog offers this, and only on the row that is selected — the button is
        /// hidden otherwise. A replace sitting on every row is a destructive action one stray click
        /// away, on a list the player is still reading.
        /// </remarks>
        public event Action<LoadWorldRow> Replaced;

        /// <summary>The world this row stands for.</summary>
        public string WorldName { get; private set; }

        void Awake()
        {
            if (pickButton != null)
            {
                pickButton.onClick.AddListener(OnPick);
            }

            if (deleteButton != null)
            {
                deleteButton.onClick.AddListener(OnDelete);
            }

            if (replaceButton != null)
            {
                replaceButton.onClick.AddListener(OnReplace);
                replaceButton.gameObject.SetActive(false);
            }
        }

        /// <summary>Shows or hides the replace button. Off on every row the save dialog builds.</summary>
        public void SetReplaceVisible(bool visible)
        {
            if (replaceButton != null)
            {
                replaceButton.gameObject.SetActive(visible);
            }
        }

        void OnReplace()
        {
            var handler = Replaced;
            if (handler != null)
            {
                handler(this);
            }
        }

        void OnDestroy()
        {
            if (pickButton != null)
            {
                pickButton.onClick.RemoveListener(OnPick);
            }

            if (deleteButton != null)
            {
                deleteButton.onClick.RemoveListener(OnDelete);
            }

            if (replaceButton != null)
            {
                replaceButton.onClick.RemoveListener(OnReplace);
            }
        }

        /// <summary>Fills the row in.</summary>
        /// <param name="world">What to show.</param>
        /// <param name="deletable">Whether the delete button should be offered at all.</param>
        public void Bind(WorldChoice world, bool deletable)
        {
            WorldName = world.Name;

            if (nameLabel != null)
            {
                nameLabel.text = world.Name;
            }

            if (summaryLabel != null)
            {
                summaryLabel.text = world.Summary;
            }

            if (autoBadge != null)
            {
                autoBadge.SetActive(world.IsAutosave);
            }

            if (deleteButton != null)
            {
                deleteButton.gameObject.SetActive(deletable);
            }

            SetSelected(false);
        }

        /// <summary>Paints this row as chosen, or not.</summary>
        public void SetSelected(bool selected)
        {
            if (selectedHighlight != null)
            {
                selectedHighlight.SetActive(selected);
            }
        }

        void OnPick()
        {
            var handler = Picked;
            if (handler != null)
            {
                handler(this);
            }
        }

        void OnDelete()
        {
            var handler = Deleted;
            if (handler != null)
            {
                handler(this);
            }
        }
    }
}
