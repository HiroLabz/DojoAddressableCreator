using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Framework.UI
{
    /// <summary>
    /// A modal for saving: pick one of the existing names to replace, or type a new one.
    /// </summary>
    /// <remarks>
    /// The two answers are mutually exclusive and the dialog keeps them that way — choosing a row
    /// empties the box, and touching the box un-chooses the row — so there is only ever one answer
    /// on screen. Two live answers with one button is how somebody overwrites a world they meant to
    /// keep.
    /// <para>
    /// The action button says what pressing it will do, and is re-read after every change: choosing
    /// a row makes it Overwrite, and so does typing a name that already exists, because that is
    /// what would happen. A button that says Save New and then replaces something would be lying at
    /// the exact moment it matters.
    /// </para>
    /// </remarks>
    public sealed class SaveAsDialog : MonoBehaviour, ISaveAsDialog
    {
        /// <summary>What the button would do if pressed right now.</summary>
        public enum Action
        {
            /// <summary>Nothing to save under — no row chosen and nothing typed.</summary>
            None,

            /// <summary>Write a world that does not exist yet.</summary>
            SaveNew,

            /// <summary>Replace one that does.</summary>
            Overwrite,
        }

        [Header("Authored parts")]
        [Tooltip("Turned on and off to show and hide the dialog. Leave every field here empty to " +
                 "have a plain one generated instead.")]
        [SerializeField] GameObject root;

        [SerializeField] TMP_Text title;

        [Tooltip("Rows are created under this.")]
        [SerializeField] RectTransform rows;

        [Tooltip("Where a new name is typed.")]
        [SerializeField] TMP_InputField input;

        [Tooltip("The action button. Its caption is rewritten as the answer changes.")]
        [SerializeField] Button actionButton;

        [SerializeField] Button closeButton;

        [Header("Captions")]
        [SerializeField] string saveNewCaption = "SAVE NEW";

        [SerializeField] string overwriteCaption = "OVERWRITE";

        [Tooltip("Longest name accepted. Zero for no limit.")]
        [SerializeField] int maxLength = 40;

        DialogRowList list;
        TMP_Text actionCaption;
        System.Action<string> confirmed;
        System.Action cancelled;

        // Guards the two fields from answering each other: emptying the box to reflect a chosen row
        // would otherwise read as the player typing, and un-choose the row that just set it.
        bool settling;

        // Built and wired exactly once, whenever that first turns out to be needed.
        bool ready;

        /// <inheritdoc />
        public bool IsOpen => root != null && root.activeSelf;

        /// <summary>What the button would do if pressed right now.</summary>
        public Action Pending
        {
            get
            {
                if (list != null && list.Selected != null)
                {
                    return Action.Overwrite;
                }

                var typed = Typed();

                if (string.IsNullOrEmpty(typed))
                {
                    return Action.None;
                }

                return list.Contains(typed) ? Action.Overwrite : Action.SaveNew;
            }
        }

        /// <summary>The name that would be saved under, or null.</summary>
        public string Target
        {
            get
            {
                if (list != null && list.Selected != null)
                {
                    return list.Selected;
                }

                var typed = Typed();

                if (string.IsNullOrEmpty(typed))
                {
                    return null;
                }

                // A typed name that already exists resolves to that world's own spelling, so
                // replacing it does not also rename it.
                return list.Match(typed) ?? typed;
            }
        }

        void Awake()
        {
            EnsureBuilt();
        }

        void EnsureBuilt()
        {
            if (ready)
            {
                return;
            }

            ready = true;

            if (root == null || title == null || rows == null || input == null
                || actionButton == null || closeButton == null)
            {
                Build();
            }

            list = new DialogRowList(rows);
            list.Changed += OnRowChanged;

            actionCaption = actionButton.GetComponentInChildren<TMP_Text>(true);

            actionButton.onClick.AddListener(Accept);
            closeButton.onClick.AddListener(Dismiss);
            input.onValueChanged.AddListener(OnTyped);
            input.onSelect.AddListener(OnInputTouched);
            input.onSubmit.AddListener(OnSubmit);

            root.SetActive(false);
        }

        void OnDestroy()
        {
            if (actionButton != null) { actionButton.onClick.RemoveListener(Accept); }
            if (closeButton != null) { closeButton.onClick.RemoveListener(Dismiss); }
            if (input != null)
            {
                input.onValueChanged.RemoveListener(OnTyped);
                input.onSelect.RemoveListener(OnInputTouched);
                input.onSubmit.RemoveListener(OnSubmit);
            }

            if (list != null) { list.Changed -= OnRowChanged; }
        }

        /// <summary>Puts the dialog on screen.</summary>
        /// <param name="existing">Names already saved, offered as rows to replace.</param>
        public void Ask(
            string heading,
            IList<string> existing,
            System.Action<string> onConfirm,
            System.Action onCancel = null)
        {
            EnsureBuilt();

            if (IsOpen)
            {
                Dismiss();
            }

            confirmed = onConfirm;
            cancelled = onCancel;

            settling = true;
            list.Show(existing);
            input.text = string.Empty;
            settling = false;

            Show();
        }

        /// <summary>Takes the dialog away unanswered, as the close button does.</summary>
        public void Close()
        {
            if (IsOpen)
            {
                Dismiss();
            }
        }

        /// <summary>Choosing a row is an answer, so the box stops being one.</summary>
        void OnRowChanged()
        {
            if (settling)
            {
                Refresh();
                return;
            }

            if (list.Selected != null && !string.IsNullOrEmpty(input.text))
            {
                settling = true;
                input.text = string.Empty;
                settling = false;
            }

            Refresh();
        }

        /// <summary>Touching the box is an answer, so the chosen row stops being one.</summary>
        void OnInputTouched(string value)
        {
            if (settling)
            {
                return;
            }

            settling = true;
            list.ClearSelection();
            settling = false;

            Refresh();
        }

        void OnTyped(string value)
        {
            if (settling)
            {
                return;
            }

            if (list.Selected != null)
            {
                settling = true;
                list.ClearSelection();
                settling = false;
            }

            Refresh();
        }

        void OnSubmit(string value)
        {
            if (IsOpen && Pending != Action.None)
            {
                Accept();
            }
        }

        /// <summary>Re-reads the answer and makes the button say what it would do.</summary>
        void Refresh()
        {
            var pending = Pending;

            actionButton.interactable = pending != Action.None;

            if (actionCaption != null)
            {
                actionCaption.text = pending == Action.Overwrite ? overwriteCaption : saveNewCaption;
            }
        }

        string Typed() => input == null || input.text == null ? string.Empty : input.text.Trim();

        void Accept()
        {
            var target = Target;

            if (string.IsNullOrEmpty(target))
            {
                return;
            }

            var callback = confirmed;
            Hide();

            callback?.Invoke(target);
        }

        void Dismiss()
        {
            var callback = cancelled;
            Hide();

            callback?.Invoke();
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
            // The button caption depends on what is chosen, and nothing is until it is on screen.
            Refresh();
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

            var panel = DialogChrome.Centred("Panel", (RectTransform)root.transform, new Vector2(660f, 700f));
            panel.gameObject.AddComponent<Image>().color = DialogChrome.Panel;

            title = DialogChrome.AddLabel("Title", panel, new Vector2(0f, 298f), new Vector2(600f, 60f), 36f);

            DialogChrome.AddLabel("ReplaceLabel", panel, new Vector2(0f, 246f), new Vector2(600f, 40f), 24f)
                .text = "Replace a saved world";

            var viewport = DialogChrome.Centred("Viewport", panel, new Vector2(600f, 360f));
            viewport.anchoredPosition = new Vector2(0f, 52f);
            viewport.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);
            viewport.gameObject.AddComponent<RectMask2D>();

            rows = DialogChrome.New("Rows", viewport);
            rows.anchorMin = new Vector2(0f, 1f);
            rows.anchorMax = new Vector2(1f, 1f);
            rows.pivot = new Vector2(0.5f, 1f);
            rows.anchoredPosition = Vector2.zero;
            rows.sizeDelta = Vector2.zero;

            var layout = rows.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.padding = new RectOffset(16, 16, 16, 16);
            layout.childControlHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            rows.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = rows;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            DialogChrome.AddLabel("NewLabel", panel, new Vector2(0f, -152f), new Vector2(600f, 40f), 24f)
                .text = "or name a new one";

            input = BuildInput(panel, new Vector2(0f, -204f), new Vector2(560f, 64f));

            actionButton = DialogChrome.AddButton(
                "Action", panel, new Vector2(-110f, -290f), new Vector2(200f, 60f), saveNewCaption, DialogChrome.Accept);

            closeButton = DialogChrome.AddButton(
                "Close", panel, new Vector2(120f, -290f), new Vector2(180f, 60f), "CANCEL", DialogChrome.Dismiss);
        }

        TMP_InputField BuildInput(RectTransform parent, Vector2 position, Vector2 size)
        {
            var rect = DialogChrome.Centred("Input", parent, size);
            rect.anchoredPosition = position;
            rect.gameObject.AddComponent<Image>().color = new Color(0.08f, 0.09f, 0.11f, 1f);

            var viewport = DialogChrome.Stretched("TextArea", rect);
            viewport.offsetMin = new Vector2(16f, 8f);
            viewport.offsetMax = new Vector2(-16f, -8f);
            viewport.gameObject.AddComponent<RectMask2D>();

            var text = Fill(DialogChrome.AddLabel("Text", viewport, Vector2.zero, size, 30f));
            var placeholder = Fill(DialogChrome.AddLabel("Placeholder", viewport, Vector2.zero, size, 30f));
            placeholder.color = new Color(1f, 1f, 1f, 0.35f);
            placeholder.text = "New world name";

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

        static TMP_Text Fill(TMP_Text text)
        {
            var rect = (RectTransform)text.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            text.alignment = TextAlignmentOptions.Left;

            return text;
        }
    }
}
