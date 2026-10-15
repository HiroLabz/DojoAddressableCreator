using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Framework.UI
{
    /// <summary>
    /// A modal that asks for a line of text: a question, a box to type in, and two buttons.
    /// </summary>
    /// <remarks>
    /// Works authored or unauthored, the same way <see cref="ConfirmDialog"/> does — wire the parts
    /// and it uses exactly what you built, leave any of them empty and it builds a plain one.
    /// </remarks>
    public sealed class PromptDialog : MonoBehaviour, IPromptDialog
    {
        [Header("Authored parts")]
        [Tooltip("Turned on and off to show and hide the prompt. Leave every field here empty to " +
                 "have a plain one generated instead.")]
        [SerializeField] GameObject root;

        [Tooltip("Label the question is written into.")]
        [SerializeField] TMP_Text message;

        [Tooltip("Where the player types.")]
        [SerializeField] TMP_InputField input;

        [Tooltip("Button that accepts. Hands the typed text to the caller and closes.")]
        [SerializeField] Button okButton;

        [Tooltip("Button that dismisses.")]
        [SerializeField] Button closeButton;

        [Header("Rules")]
        [Tooltip("Refuse to accept an empty answer. The OK button greys out until something is typed.")]
        [SerializeField] bool requireText = true;

        [Tooltip("Longest answer accepted. Zero for no limit.")]
        [SerializeField] int maxLength = 40;

        Action<string> confirmed;
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

            if (root == null || message == null || input == null || okButton == null || closeButton == null)
            {
                Build();
            }

            okButton.onClick.AddListener(Accept);
            closeButton.onClick.AddListener(Dismiss);
            input.onValueChanged.AddListener(OnTyped);
            input.onSubmit.AddListener(OnSubmit);

            root.SetActive(false);
        }

        void OnDestroy()
        {
            if (okButton != null) { okButton.onClick.RemoveListener(Accept); }
            if (closeButton != null) { closeButton.onClick.RemoveListener(Dismiss); }
            if (input != null)
            {
                input.onValueChanged.RemoveListener(OnTyped);
                input.onSubmit.RemoveListener(OnSubmit);
            }
        }

        /// <summary>
        /// Puts a question on screen with a box to answer it in.
        /// </summary>
        /// <param name="suggestion">What the box starts with, and what is selected ready to replace.</param>
        public void Ask(string question, string suggestion, Action<string> onConfirm, Action onCancel = null)
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

            input.text = suggestion ?? string.Empty;

            Show();
        }

        /// <summary>Takes the prompt away unanswered, as the close button does.</summary>
        public void Close()
        {
            if (IsOpen)
            {
                Dismiss();
            }
        }

        void OnTyped(string value)
        {
            if (okButton != null)
            {
                okButton.interactable = !requireText || !string.IsNullOrWhiteSpace(value);
            }
        }

        void OnSubmit(string value)
        {
            // Enter accepts, but only when the answer would have been accepted by the button.
            if (IsOpen && (!requireText || !string.IsNullOrWhiteSpace(value)))
            {
                Accept();
            }
        }

        void Accept()
        {
            var answer = input.text == null ? string.Empty : input.text.Trim();

            if (requireText && string.IsNullOrEmpty(answer))
            {
                return;
            }

            // Read and cleared before the callback runs, so an action that opens another dialog is
            // not cut off by this one's own tidying up.
            var callback = confirmed;
            Hide();

            if (callback != null)
            {
                callback(answer);
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
            // Focus only lands on something that is actually on screen, which is the whole reason
            // this is a separate step rather than the tail of Show.
            input.Select();
            input.ActivateInputField();
            input.caretPosition = input.text.Length;

            OnTyped(input.text);
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

        void Build()
        {
            DialogChrome.MakeOverlay(gameObject);

            root = DialogChrome.Stretched("Dialog", (RectTransform)transform).gameObject;
            DialogChrome.AddScrim((RectTransform)root.transform);

            var panel = DialogChrome.Centred("Panel", (RectTransform)root.transform, new Vector2(640f, 280f));
            panel.gameObject.AddComponent<Image>().color = DialogChrome.Panel;

            message = DialogChrome.AddLabel("Message", panel, new Vector2(0f, 88f), new Vector2(560f, 60f), 34f);

            input = BuildInput(panel, new Vector2(0f, 16f), new Vector2(560f, 68f));

            okButton = DialogChrome.AddButton(
                "Ok", panel, new Vector2(-110f, -90f), new Vector2(180f, 60f), "SAVE", DialogChrome.Accept);

            closeButton = DialogChrome.AddButton(
                "Close", panel, new Vector2(110f, -90f), new Vector2(180f, 60f), "CANCEL", DialogChrome.Dismiss);
        }

        /// <summary>
        /// A text box. Built by hand rather than with TMP's own factory, which needs an editor-only
        /// menu path and would drag a prefab dependency into a runtime component.
        /// </summary>
        TMP_InputField BuildInput(RectTransform parent, Vector2 position, Vector2 size)
        {
            var rect = DialogChrome.Centred("Input", parent, size);
            rect.anchoredPosition = position;
            rect.gameObject.AddComponent<Image>().color = new Color(0.08f, 0.09f, 0.11f, 1f);

            var viewport = DialogChrome.Stretched("TextArea", rect);
            viewport.offsetMin = new Vector2(16f, 8f);
            viewport.offsetMax = new Vector2(-16f, -8f);
            viewport.gameObject.AddComponent<RectMask2D>();

            var text = DialogChrome.AddLabel("Text", viewport, Vector2.zero, size, 30f);
            var textRect = (RectTransform)text.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            text.alignment = TextAlignmentOptions.Left;
            text.raycastTarget = true;

            var placeholder = DialogChrome.AddLabel("Placeholder", viewport, Vector2.zero, size, 30f);
            var placeholderRect = (RectTransform)placeholder.transform;
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = Vector2.zero;
            placeholderRect.offsetMax = Vector2.zero;
            placeholder.alignment = TextAlignmentOptions.Left;
            placeholder.color = new Color(1f, 1f, 1f, 0.35f);
            placeholder.text = "Name this world";

            var field = rect.gameObject.AddComponent<TMP_InputField>();
            field.textViewport = viewport;
            field.textComponent = (TextMeshProUGUI)text;
            field.placeholder = placeholder;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.characterLimit = Mathf.Max(0, maxLength);
            field.targetGraphic = rect.GetComponent<Image>();
            field.caretWidth = 2;
            field.customCaretColor = true;
            field.caretColor = Color.white;

            return field;
        }
    }
}
