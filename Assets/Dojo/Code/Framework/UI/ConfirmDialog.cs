using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Framework.UI
{
    public sealed class ConfirmDialog : MonoBehaviour, IConfirmDialog
    {
        [Header("Authored parts")]
        [Tooltip("Turned on and off to show and hide the question. Leave every field here empty " +
                 "to have a plain dialog generated instead.")]
        [SerializeField] GameObject root;

        [Tooltip("Label the question is written into.")]
        [SerializeField] TMP_Text message;

        [Tooltip("Button that accepts. Runs the caller's confirm action and closes.")]
        [SerializeField] Button okButton;

        [Tooltip("Button that dismisses. Runs the caller's cancel action, if it gave one, and closes.")]
        [SerializeField] Button closeButton;

        Action confirmed;
        Action cancelled;

        // Built and wired exactly once, whenever that first turns out to be needed. Awake is the
        // normal moment, but a caller that creates this component and asks it something in the same
        // breath - or from its own Awake, where order is not promised - would otherwise reach a
        // dialog whose parts do not exist yet.
        bool ready;


        /// <inheritdoc />
        public bool IsOpen => root != null && root.activeSelf;

        void Awake()
        {
            EnsureBuilt();
        }

        /// <summary>Makes sure the dialog exists and is wired, once.</summary>
        void EnsureBuilt()
        {
            if (ready)
            {
                return;
            }

            ready = true;

            if (root == null || message == null || okButton == null || closeButton == null)
            {
                Build();
            }

            okButton.onClick.AddListener(Accept);
            closeButton.onClick.AddListener(Dismiss);

            root.SetActive(false);
        }

        void OnDestroy()
        {
            if (okButton != null)
            {
                okButton.onClick.RemoveListener(Accept);
            }

            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(Dismiss);
            }
        }

        /// <summary>
        /// Puts a question on screen. <paramref name="onConfirm"/> runs if the player accepts,
        /// <paramref name="onCancel"/> if they dismiss it; both are optional.
        /// </summary>
        /// <remarks>
        /// Asking again while a question is already up replaces it. The one being replaced is
        /// treated as dismissed, so a caller waiting on it is never left holding a callback that
        /// will not fire.
        /// </remarks>
        public void Ask(string question, Action onConfirm, Action onCancel = null)
        {
            EnsureBuilt();

            if (IsOpen)
            {
                Dismiss();
            }

            confirmed = onConfirm;
            cancelled = onCancel;

            if (message != null)
            {
                message.text = question;
            }

            Show();
        }

        /// <summary>
        /// Takes the question away without answering it. The cancel action runs, as it would had
        /// the player pressed the close button — closing and dismissing are the same thing.
        /// </summary>
        public void Close()
        {
            if (IsOpen)
            {
                Dismiss();
            }
        }

        void Accept()
        {
            // Read and cleared before the callback runs, so an action that asks another question
            // cannot be cut off by this one's own tidying up.
            var callback = confirmed;
            Hide();

            if (callback != null)
            {
                callback();
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
            EnsureBuilt();

            if (root != null)
            {
                root.SetActive(true);
            }

            // Nothing animates here, so it is on screen the instant it is switched on.
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
        }

        /// <summary>Builds a plain dialog, for when there is nothing authored to use.</summary>
        void Build()
        {
            DialogChrome.MakeOverlay(gameObject);

            root = DialogChrome.Stretched("Dialog", (RectTransform)transform).gameObject;
            DialogChrome.AddScrim((RectTransform)root.transform);

            var panel = DialogChrome.Centred("Panel", (RectTransform)root.transform, new Vector2(560f, 220f));
            panel.gameObject.AddComponent<Image>().color = DialogChrome.Panel;

            message = DialogChrome.AddLabel("Message", panel, new Vector2(0f, 40f), new Vector2(480f, 90f), 36f);

            okButton = DialogChrome.AddButton(
                "Ok", panel, new Vector2(-110f, -60f), new Vector2(180f, 60f), "OK", DialogChrome.Accept);

            closeButton = DialogChrome.AddButton(
                "Close", panel, new Vector2(110f, -60f), new Vector2(180f, 60f), "CLOSE", DialogChrome.Dismiss);
        }
    }
}
