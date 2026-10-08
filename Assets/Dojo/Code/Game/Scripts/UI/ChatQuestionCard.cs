using System;
using System.Collections.Generic;
using Dojo.Hiro;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Game.UI
{
    /// <summary>
    /// One transcript row that asks something and offers the answers as buttons.
    /// </summary>
    /// <remarks>
    /// The third kind of row, beside the player bubble and the agent bubble. It exists because the
    /// platform can stop mid-turn and ask — see <see cref="ChatQuestion"/> — and the options are
    /// structured data, not prose. Drawn as text they were unclickable, which is what made the
    /// question look like a dead end.
    /// <para>
    /// Option count varies run to run, so the buttons are pooled rather than authored: the prefab
    /// carries one <see cref="ChatOptionButton"/> to clone and the card grows to fit whatever
    /// arrives.
    /// </para>
    /// </remarks>
    public sealed class ChatQuestionCard : MonoBehaviour
    {
        [Tooltip("What is being asked.")]
        [SerializeField] TMP_Text prompt;

        [Tooltip("Parent the option buttons are created under.")]
        [SerializeField] RectTransform options;

        [Tooltip("Cloned once per option. A prefab of its own, so the buttons can be restyled "
                 + "without touching the card.")]
        [SerializeField] ChatOptionButton optionTemplate;

        [Tooltip("Caps how wide the card grows, as a share of the transcript width. Matches the "
                 + "agent bubble so the two read as the same column.")]
        [SerializeField] float widthShare = 0.72f;

        [SerializeField] LayoutElement cardSize;

        readonly List<ChatOptionButton> pool = new List<ChatOptionButton>();

        Action<string> chosen;

        /// <summary>
        /// Shows <paramref name="question"/> and calls <paramref name="onChosen"/> with the option
        /// text when one is clicked.
        /// </summary>
        /// <remarks>
        /// The callback is replaced rather than added to, so re-binding a pooled card cannot leave
        /// the previous question's handler attached — which would answer a conversation that has
        /// already moved on.
        /// </remarks>
        public void Bind(ChatQuestion question, float rowWidth, Action<string> onChosen)
        {
            chosen = onChosen;

            if (prompt != null)
            {
                prompt.text = question != null ? question.Prompt : string.Empty;
            }

            if (cardSize != null)
            {
                cardSize.preferredWidth = Mathf.Max(0f, rowWidth * widthShare);
            }

            var list = question != null ? question.Options : Array.Empty<string>();

            for (int i = 0; i < list.Count; i++)
            {
                while (pool.Count <= i)
                {
                    var made = Instantiate(optionTemplate, options, false);
                    pool.Add(made);
                }

                var button = pool[i];
                button.gameObject.SetActive(true);
                button.Bind(list[i], Choose);
            }

            for (int i = list.Count; i < pool.Count; i++)
            {
                pool[i].gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Locks the card so a second click cannot land while the first is being sent.
        /// </summary>
        public void SetInteractable(bool interactable)
        {
            for (int i = 0; i < pool.Count; i++)
            {
                pool[i].SetInteractable(interactable);
            }
        }

        void Choose(string option)
        {
            var handler = chosen;
            if (handler == null)
            {
                return;
            }

            // Cleared before the callback, not after: the handler rewrites the transcript and
            // redraws, which can re-bind this very card for another line.
            chosen = null;
            SetInteractable(false);
            handler(option);
        }
    }
}
