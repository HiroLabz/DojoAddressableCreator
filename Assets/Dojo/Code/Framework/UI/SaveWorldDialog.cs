using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Framework.UI
{
    /// <summary>
    /// The save-world dialog: the saves already on disk as rows to overwrite, and a field to type a
    /// new name into.
    /// </summary>
    /// <remarks>
    /// Built the same way as <see cref="LoadWorldDialog"/> and for the same reason — every part
    /// comes from <c>SaveWorldDialog.prefab</c>, and a missing reference is reported rather than
    /// quietly generated. A save screen that half-built itself would be one whose rows might not be
    /// the saves it is about to replace.
    /// <para>
    /// Picking a row and typing a name are two ways of answering one question, so they are held in
    /// one place: <see cref="Target"/> is whatever the field currently reads. A row fills the field
    /// rather than setting something separate, which means the name the player is about to save
    /// under is always the name they can see.
    /// </para>
    /// <para>
    /// Overwriting is not confirmed here. This dialog reports a name; whether that name already
    /// exists, and whether replacing it deserves a second question, is the caller's to decide —
    /// see <see cref="IConfirmDialog"/>.
    /// </para>
    /// </remarks>
    public sealed class SaveWorldDialog : MonoBehaviour, ISaveWorldDialog
    {
        [Header("Parts")]
        [Tooltip("The whole dialog. Switched off while it is not on screen.")]
        [SerializeField] GameObject root;

        [Tooltip("The small bracketed line above the title, e.g. [ 3 SAVED ].")]
        [SerializeField] TMP_Text eyebrow;

        [Tooltip("The heading.")]
        [SerializeField] TMP_Text title;

        [Tooltip("Parent the rows are created under.")]
        [SerializeField] RectTransform rows;

        [Tooltip("One row per existing save. Shares the load dialog's row.")]
        [SerializeField] LoadWorldRow rowPrefab;

        [Tooltip("Where the name is typed. The dialog's answer is whatever this reads.")]
        [SerializeField] TMP_InputField nameField;

        [Tooltip("Shown when there is nothing saved yet, in place of the rows.")]
        [SerializeField] GameObject emptyNotice;

        [Tooltip("Shown while the typed name matches a save that already exists.")]
        [SerializeField] GameObject overwriteWarning;

        [Header("Buttons")]
        [SerializeField] Button okButton;
        [SerializeField] Button cancelButton;

        [Tooltip("Optional. The X in the corner. Backs out, exactly as cancel does.")]
        [SerializeField] Button closeButton;

        [Tooltip("Optional. The accept button's caption, switched between the two labels below.")]
        [SerializeField] TMP_Text okLabel;

        [SerializeField] string saveLabel = "SAVE";

        [Tooltip("Shown instead while the name matches a save that already exists. WorldService " +
                 "declines to ask a separate are-you-sure on the strength of this button saying so.")]
        [SerializeField] string overwriteLabel = "OVERWRITE";

        [Header("Backdrop")]
        [Tooltip("Optional. Blurs what is behind the dialog while it is up.")]
        [SerializeField] UIBackdropBlur backdrop;

        readonly List<LoadWorldRow> pool = new List<LoadWorldRow>();

        Action<string> confirmed;
        Action cancelled;
        Action<string> deleted;

        LoadWorldRow chosen;

        /// <summary>Names currently on screen, for spotting an overwrite as the player types.</summary>
        readonly List<string> existing = new List<string>();

        bool ready;

        /// <inheritdoc />
        public bool IsOpen => root != null && root.activeSelf;

        /// <inheritdoc />
        public string Target
        {
            get
            {
                if (nameField == null)
                {
                    return chosen == null ? null : chosen.WorldName;
                }

                string typed = nameField.text;
                return string.IsNullOrWhiteSpace(typed) ? null : typed.Trim();
            }
        }

        void Awake()
        {
            EnsureWired();
        }

        void EnsureWired()
        {
            if (ready)
            {
                return;
            }

            ready = true;

            if (root == null || rows == null || rowPrefab == null || nameField == null
                || okButton == null || cancelButton == null)
            {
                Debug.LogError(
                    $"{nameof(SaveWorldDialog)} on '{name}' is missing authored parts — it needs at " +
                    "least root, rows, rowPrefab, nameField, okButton and cancelButton. Assign them " +
                    "on SaveWorldDialog.prefab.",
                    this);
                return;
            }

            okButton.onClick.AddListener(Accept);
            cancelButton.onClick.AddListener(Dismiss);
            nameField.onValueChanged.AddListener(OnNameChanged);

            if (closeButton != null)
            {
                closeButton.onClick.AddListener(Dismiss);
            }

            root.SetActive(false);
        }

        void OnDestroy()
        {
            if (okButton != null) { okButton.onClick.RemoveListener(Accept); }
            if (cancelButton != null) { cancelButton.onClick.RemoveListener(Dismiss); }
            if (closeButton != null) { closeButton.onClick.RemoveListener(Dismiss); }
            if (nameField != null) { nameField.onValueChanged.RemoveListener(OnNameChanged); }

            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i] != null)
                {
                    pool[i].Picked -= OnRowPicked;
                    pool[i].Replaced -= OnRowReplaced;
                    pool[i].Deleted -= OnRowDeleted;
                }
            }
        }

        /// <inheritdoc />
        public void Ask(
            IList<WorldChoice> worlds,
            string suggested,
            Action<string> onConfirm,
            Action onCancel = null,
            Action<string> onDelete = null)
        {
            EnsureWired();

            if (root == null || rowPrefab == null)
            {
                return;
            }

            if (IsOpen)
            {
                Dismiss();
            }

            confirmed = onConfirm;
            cancelled = onCancel;
            deleted = onDelete;

            chosen = null;
            Fill(worlds);

            if (nameField != null)
            {
                // Without notify: this is the caller setting up the question, not the player
                // answering it, and letting it raise would run the overwrite check against a field
                // that has not finished being filled.
                nameField.SetTextWithoutNotify(suggested ?? string.Empty);
            }

            ApplyName();
            Show();
        }

        void Fill(IList<WorldChoice> worlds)
        {
            int count = worlds == null ? 0 : worlds.Count;

            existing.Clear();

            while (pool.Count < count)
            {
                pool.Add(BuildRow());
            }

            int autosaves = 0;

            for (int i = 0; i < pool.Count; i++)
            {
                bool visible = i < count;
                pool[i].gameObject.SetActive(visible);

                if (!visible)
                {
                    continue;
                }

                pool[i].Bind(worlds[i], deleted != null);
                pool[i].SetReplaceVisible(false);
                existing.Add(worlds[i].Name);

                if (worlds[i].IsAutosave)
                {
                    autosaves++;
                }
            }

            if (eyebrow != null)
            {
                eyebrow.text = "[   " + (count - autosaves) + " SAVED   ·   " + autosaves + " AUTOSAVE   ]";
            }

            if (emptyNotice != null)
            {
                emptyNotice.SetActive(count == 0);
            }
        }

        LoadWorldRow BuildRow()
        {
            LoadWorldRow row = Instantiate(rowPrefab, rows, false);
            row.Picked += OnRowPicked;
            row.Replaced += OnRowReplaced;
            row.Deleted += OnRowDeleted;
            return row;
        }

        /// <summary>
        /// A picked row fills the name field rather than answering on its own, so the name about to
        /// be written is always the one on screen and can still be edited.
        /// </summary>
        void OnRowPicked(LoadWorldRow row)
        {
            if (chosen != null && chosen != row)
            {
                chosen.SetSelected(false);
                chosen.SetReplaceVisible(false);
            }

            chosen = row;
            chosen.SetSelected(true);
            chosen.SetReplaceVisible(true);

            if (nameField != null)
            {
                nameField.SetTextWithoutNotify(row.WorldName);
            }

            ApplyName();
        }

        /// <summary>
        /// Writes over the row's world directly. The row already names what is about to be
        /// replaced, so there is nothing further to read and nothing further to ask.
        /// </summary>
        void OnRowReplaced(LoadWorldRow row)
        {
            var callback = confirmed;
            string target = row.WorldName;

            Hide();

            if (callback != null && !string.IsNullOrEmpty(target))
            {
                callback(target);
            }
        }

        /// <remarks>
        /// Handed out rather than acted on: which worlds exist is not this dialog's to decide, and
        /// the caller refills the list once the delete has actually happened.
        /// </remarks>
        void OnRowDeleted(LoadWorldRow row)
        {
            var callback = deleted;
            if (callback != null)
            {
                callback(row.WorldName);
            }
        }

        void OnNameChanged(string text)
        {
            // Typing is a different answer from the row that was picked, so the row stops claiming
            // to be the one being saved over — unless what was typed has arrived back at its name.
            if (chosen != null && !string.Equals(chosen.WorldName, Target, StringComparison.Ordinal))
            {
                chosen.SetSelected(false);
                chosen.SetReplaceVisible(false);
                chosen = null;
            }

            ApplyName();
        }

        /// <summary>Keeps the accept button and the overwrite warning in step with the name.</summary>
        void ApplyName()
        {
            string target = Target;
            bool named = !string.IsNullOrEmpty(target);
            bool replacing = named && Exists(target);

            if (okButton != null)
            {
                okButton.interactable = named;
            }

            if (overwriteWarning != null)
            {
                overwriteWarning.SetActive(replacing);
            }

            if (okLabel != null)
            {
                okLabel.text = replacing ? overwriteLabel : saveLabel;
            }
        }

        bool Exists(string target)
        {
            for (int i = 0; i < existing.Count; i++)
            {
                if (string.Equals(existing[i], target, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        void Accept()
        {
            string target = Target;
            if (string.IsNullOrEmpty(target))
            {
                return;
            }

            var callback = confirmed;
            Hide();

            if (callback != null)
            {
                callback(target);
            }
        }

        void Dismiss()
        {
            var callback = cancelled;
            Hide();

            if (callback != null)
            {
                callback();
            }
        }

        /// <inheritdoc />
        public void Show()
        {
            EnsureWired();

            // The backdrop is photographed while the dialog is still switched off — a picture taken
            // afterwards would have the dialog in it. That costs one frame, which is why the root is
            // switched on in the callback rather than here.
            if (backdrop != null)
            {
                backdrop.CaptureThen(Reveal);
                return;
            }

            Reveal();
        }

        void Reveal()
        {
            if (root != null)
            {
                root.SetActive(true);
            }

            ShowComplete();
        }

        /// <inheritdoc />
        public void ShowComplete()
        {
            // Focus is taken here rather than in Show, because a field on a switched-off object
            // cannot take it.
            if (nameField != null)
            {
                nameField.Select();
                nameField.ActivateInputField();
            }
        }

        /// <inheritdoc />
        public void Hide()
        {
            if (root != null)
            {
                root.SetActive(false);
            }

            HideComplete();
        }

        /// <inheritdoc />
        public void HideComplete()
        {
            confirmed = null;
            cancelled = null;
            deleted = null;

            if (chosen != null)
            {
                chosen.SetSelected(false);
                chosen.SetReplaceVisible(false);
                chosen = null;
            }
        }
    }
}
