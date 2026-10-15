using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Framework.UI
{
    /// <summary>
    /// A modal that asks the player to pick one of a list: a title, a scrolling set of rows, and
    /// two buttons.
    /// </summary>
    /// <remarks>
    /// Works authored or unauthored like the other dialogs, though there is more to author here:
    /// as well as the parts, it needs a row prefab and something to parent rows under. Leave any of
    /// it empty and a plain one is built.
    /// <para>
    /// Nothing is chosen to begin with, and the accepting button stays disabled until something is.
    /// Picking a default would be a way to load the wrong world by reflex.
    /// </para>
    /// </remarks>
    public sealed class ChooserDialog : MonoBehaviour, IChooserDialog
    {
        [Header("Authored parts")]
        [Tooltip("Turned on and off to show and hide the chooser. Leave every field here empty to " +
                 "have a plain one generated instead.")]
        [SerializeField] GameObject root;

        [Tooltip("Label the title is written into.")]
        [SerializeField] TMP_Text title;

        [Tooltip("Rows are created under this. Wants a vertical layout group.")]
        [SerializeField] RectTransform rows;

        [Tooltip("Shown instead of the rows when there is nothing to choose from.")]
        [SerializeField] TMP_Text emptyNotice;

        [Tooltip("Button that accepts the chosen row.")]
        [SerializeField] Button okButton;

        [Tooltip("Button that dismisses.")]
        [SerializeField] Button closeButton;

        DialogRowList list;

        Action<string> confirmed;
        Action cancelled;

        // Built and wired exactly once, whenever that first turns out to be needed. Awake is the
        // normal moment, but a caller that creates this component and asks it something in the same
        // breath - or from its own Awake, where order is not promised - would otherwise reach a
        // dialog whose parts do not exist yet.
        bool ready;


        /// <inheritdoc />
        public bool IsOpen => root != null && root.activeSelf;

        /// <summary>What is currently picked, or null.</summary>
        public string Chosen => list == null ? null : list.Selected;

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

            if (root == null || title == null || rows == null || okButton == null || closeButton == null)
            {
                Build();
            }

            list = new DialogRowList(rows);
            list.Changed += OnRowChanged;

            okButton.onClick.AddListener(Accept);
            closeButton.onClick.AddListener(Dismiss);

            root.SetActive(false);
        }

        void OnDestroy()
        {
            if (okButton != null) { okButton.onClick.RemoveListener(Accept); }
            if (closeButton != null) { closeButton.onClick.RemoveListener(Dismiss); }
            if (list != null) { list.Changed -= OnRowChanged; }
        }

        void OnRowChanged()
        {
            okButton.interactable = list.Selected != null;
        }

        /// <summary>Puts the list on screen.</summary>
        public void Choose(string heading, IList<string> choices, Action<string> onConfirm, Action onCancel = null)
        {
            EnsureBuilt();

            if (IsOpen)
            {
                Dismiss();
            }

            confirmed = onConfirm;
            cancelled = onCancel;

            if (title != null)
            {
                title.text = heading;
            }

            list.Show(choices);

            if (emptyNotice != null)
            {
                emptyNotice.gameObject.SetActive(list.Count == 0);
            }

            okButton.interactable = false;

            Show();
        }

        /// <summary>Takes the chooser away without picking anything.</summary>
        public void Close()
        {
            if (IsOpen)
            {
                Dismiss();
            }
        }

        void Accept()
        {
            var picked = Chosen;
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

            if (list != null)
            {
                list.ClearSelection();
            }
        }

        void Build()
        {
            DialogChrome.MakeOverlay(gameObject);

            root = DialogChrome.Stretched("Dialog", (RectTransform)transform).gameObject;
            DialogChrome.AddScrim((RectTransform)root.transform);

            var panel = DialogChrome.Centred("Panel", (RectTransform)root.transform, new Vector2(660f, 620f));
            panel.gameObject.AddComponent<Image>().color = DialogChrome.Panel;

            title = DialogChrome.AddLabel("Title", panel, new Vector2(0f, 258f), new Vector2(600f, 60f), 36f);

            emptyNotice = DialogChrome.AddLabel(
                "Empty", panel, new Vector2(0f, 40f), new Vector2(560f, 60f), 26f);
            emptyNotice.color = new Color(1f, 1f, 1f, 0.5f);
            emptyNotice.text = "No saved worlds yet.";

            // A viewport that clips, with the rows stacked inside it.
            var viewport = DialogChrome.Centred("Viewport", panel, new Vector2(600f, 420f));
            viewport.anchoredPosition = new Vector2(0f, 20f);
            viewport.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);
            viewport.gameObject.AddComponent<RectMask2D>();

            rows = DialogChrome.New("Rows", viewport);
            rows.anchorMin = new Vector2(0f, 1f);
            rows.anchorMax = new Vector2(1f, 1f);
            rows.pivot = new Vector2(0.5f, 1f);
            rows.anchoredPosition = Vector2.zero;
            rows.sizeDelta = new Vector2(0f, 0f);

            var layout = rows.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.padding = new RectOffset(16, 16, 16, 16);
            layout.childControlHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            var fitter = rows.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = rows;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            okButton = DialogChrome.AddButton(
                "Ok", panel, new Vector2(-110f, -258f), new Vector2(180f, 60f), "LOAD", DialogChrome.Accept);

            closeButton = DialogChrome.AddButton(
                "Close", panel, new Vector2(110f, -258f), new Vector2(180f, 60f), "EXIT", DialogChrome.Dismiss);
        }
    }
}
