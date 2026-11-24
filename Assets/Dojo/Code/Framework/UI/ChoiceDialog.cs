using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Framework.UI
{
    /// <summary>
    /// A question with two to four answers, each its own button - for a choice Confirm's OK and
    /// CLOSE cannot express, such as OK / CANCEL / PLACE IT FOR ME / EDIT ROOM.
    /// </summary>
    /// <remarks>
    /// Generated in code like the plain Confirm dialog, in the same chrome. The buttons are made
    /// once, four of them, and the ones a question does not use are hidden. The panel widens to
    /// hold a row of four.
    /// </remarks>
    public sealed class ChoiceDialog : MonoBehaviour, IChoiceDialog
    {
        const int MaxAnswers = 4;
        const float ButtonWidth = 260f;
        const float ButtonGap = 20f;

        // The panel's width with up to three answers; a row of four widens it to fit.
        const float PanelWidth = 880f;
        const float PanelHeight = 240f;
        const float PanelMargin = 30f;

        GameObject root;
        RectTransform panel;
        TMP_Text message;
        readonly Button[] buttons = new Button[MaxAnswers];
        readonly TMP_Text[] labels = new TMP_Text[MaxAnswers];

        Action<int> chosen;
        bool ready;

        /// <inheritdoc />
        public bool IsOpen => root != null && root.activeSelf;

        void Awake() => EnsureBuilt();

        void EnsureBuilt()
        {
            if (ready)
            {
                return;
            }

            ready = true;
            Build();
            root.SetActive(false);
        }

        /// <inheritdoc />
        public void Ask(string question, IList<string> answers, Action<int> onChoose)
        {
            EnsureBuilt();

            chosen = onChoose;

            if (message != null)
            {
                message.text = question;
            }

            var count = Mathf.Clamp(answers != null ? answers.Count : 0, 1, MaxAnswers);
            var span = count * ButtonWidth + (count - 1) * ButtonGap;

            if (panel != null)
            {
                panel.sizeDelta = new Vector2(Mathf.Max(PanelWidth, span + PanelMargin * 2f), PanelHeight);
            }

            for (var i = 0; i < MaxAnswers; i++)
            {
                var used = i < count;
                buttons[i].gameObject.SetActive(used);

                if (!used)
                {
                    continue;
                }

                labels[i].text = answers != null && i < answers.Count ? answers[i] : "OK";

                var rect = (RectTransform)buttons[i].transform;
                rect.anchoredPosition = new Vector2(-span * 0.5f + ButtonWidth * 0.5f + i * (ButtonWidth + ButtonGap), -60f);
            }

            Show();
        }

        void Choose(int index)
        {
            // Read and cleared before the callback runs, so an answer that asks another question
            // cannot be cut off by this one's own tidying up.
            var callback = chosen;
            Hide();
            callback?.Invoke(index);
        }

        /// <inheritdoc />
        public void Show()
        {
            EnsureBuilt();
            root.SetActive(true);
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
            chosen = null;
        }

        void Build()
        {
            DialogChrome.MakeOverlay(gameObject);

            root = DialogChrome.Stretched("Dialog", (RectTransform)transform).gameObject;
            DialogChrome.AddScrim((RectTransform)root.transform);

            panel = DialogChrome.Centred("Panel", (RectTransform)root.transform, new Vector2(PanelWidth, PanelHeight));
            panel.gameObject.AddComponent<Image>().color = DialogChrome.Panel;

            message = DialogChrome.AddLabel("Message", panel, new Vector2(0f, 40f), new Vector2(820f, 110f), 32f);

            for (var i = 0; i < MaxAnswers; i++)
            {
                var index = i;
                buttons[i] = DialogChrome.AddButton(
                    "Answer" + i, panel, Vector2.zero, new Vector2(ButtonWidth, 60f), "OK",
                    i == 0 ? DialogChrome.Accept : DialogChrome.Dismiss);
                labels[i] = buttons[i].GetComponentInChildren<TMP_Text>(true);
                buttons[i].onClick.AddListener(() => Choose(index));
            }
        }
    }
}
