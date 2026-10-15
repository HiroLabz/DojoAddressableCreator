using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Framework.UI
{
    /// <summary>
    /// A modal that asks which saved world to open: a count line, a title, a scrolling list of
    /// worlds, a warning, and two buttons.
    /// </summary>
    /// <remarks>
    /// <b>Authored only.</b> Unlike the four older dialogs this builds nothing in code — every part
    /// comes from <c>LoadWorldDialog.prefab</c>, and a missing reference is reported rather than
    /// papered over with a generated stand-in. A dialog that quietly draws itself differently
    /// depending on whether a field was filled in is a dialog nobody can design against.
    /// <para>
    /// Rows are pooled rather than rebuilt per open, for the reason <see cref="DialogRowList"/>
    /// gives for doing the same: a list of saved worlds is opened far more often than it changes.
    /// </para>
    /// </remarks>
    public sealed class LoadWorldDialog : MonoBehaviour, ILoadWorldDialog
    {
        [Header("Chrome")]
        [Tooltip("Turned on and off to show and hide the dialog.")]
        [SerializeField] GameObject root;

        [Tooltip("The bracketed line above the title, e.g. '[ 3 SAVED · 1 AUTOSAVE ]'.")]
        [SerializeField] TMP_Text eyebrow;

        [Tooltip("The dialog's title.")]
        [SerializeField] TMP_Text title;

        [Header("List")]
        [Tooltip("Rows are created under this. Wants a vertical layout group.")]
        [SerializeField] RectTransform rows;

        [Tooltip("The row prefab, instantiated once per world.")]
        [SerializeField] LoadWorldRow rowPrefab;

        [Tooltip("Shown instead of the rows when the player has saved nothing.")]
        [SerializeField] GameObject emptyNotice;

        [Tooltip("The line warning that loading replaces the open floor.")]
        [SerializeField] GameObject warning;

        [Header("Buttons")]
        [Tooltip("Opens the chosen world. Disabled until something is chosen.")]
        [SerializeField] Button okButton;

        [Tooltip("Backs out.")]
        [SerializeField] Button cancelButton;

        [Tooltip("The X in the corner. Backs out, same as Cancel.")]
        [SerializeField] Button closeButton;

        [Header("Backdrop")]
        [Tooltip("Optional. Photographs the screen before the dialog appears so the scrim can blur " +
                 "it. Leave empty for a dialog with no blurred backdrop.")]
        [SerializeField] UIBackdropBlur backdrop;

        readonly List<LoadWorldRow> pool = new List<LoadWorldRow>();

        Action<string> confirmed;
        Action cancelled;
        Action<string> deleted;

        LoadWorldRow chosen;

        // Wired exactly once. Awake is the normal moment, but a caller that creates this component
        // and asks it something in the same breath would otherwise reach an unwired dialog.
        bool ready;

        /// <inheritdoc />
        public bool IsOpen => root != null && root.activeSelf;

        /// <inheritdoc />
        public string Chosen => chosen == null ? null : chosen.WorldName;

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

            if (root == null || rows == null || rowPrefab == null || okButton == null || cancelButton == null)
            {
                Debug.LogError(
                    $"{nameof(LoadWorldDialog)} on '{name}' is missing authored parts — it needs at " +
                    "least root, rows, rowPrefab, okButton and cancelButton. Assign them on " +
                    "LoadWorldDialog.prefab.",
                    this);
                return;
            }

            okButton.onClick.AddListener(Accept);
            cancelButton.onClick.AddListener(Dismiss);

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

            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i] == null)
                {
                    continue;
                }

                pool[i].Picked -= OnRowPicked;
                pool[i].Deleted -= OnRowDeleted;
            }
        }

        /// <inheritdoc />
        public void Choose(
            IList<WorldChoice> worlds,
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

            if (okButton != null)
            {
                okButton.interactable = false;
            }

            Show();
        }

        void Fill(IList<WorldChoice> worlds)
        {
            int count = worlds == null ? 0 : worlds.Count;

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

            // With nothing to load, the warning would be answering a question nobody asked.
            if (warning != null)
            {
                warning.SetActive(count > 0);
            }
        }

        LoadWorldRow BuildRow()
        {
            LoadWorldRow row = Instantiate(rowPrefab, rows, false);
            row.Picked += OnRowPicked;
            row.Deleted += OnRowDeleted;
            return row;
        }

        void OnRowPicked(LoadWorldRow row)
        {
            if (chosen == row)
            {
                return;
            }

            if (chosen != null)
            {
                chosen.SetSelected(false);
            }

            chosen = row;
            chosen.SetSelected(true);

            if (okButton != null)
            {
                okButton.interactable = true;
            }
        }

        void OnRowDeleted(LoadWorldRow row)
        {
            var callback = deleted;
            if (callback == null)
            {
                return;
            }

            // Handed out rather than acted on: which worlds exist is not this dialog's to decide,
            // and the caller refills the list once the delete has actually happened.
            callback(row.WorldName);
        }

        void Accept()
        {
            string picked = Chosen;
            if (picked == null)
            {
                return;
            }

            var callback = confirmed;
            Hide();

            if (callback != null)
            {
                callback(picked);
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

            // Nothing animates here yet, so it is on screen the instant it is switched on.
            ShowComplete();
        }

        /// <inheritdoc />
        public void ShowComplete()
        {
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
                chosen = null;
            }
        }
    }
}
