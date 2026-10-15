using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Game.UI
{
    /// <summary>One offered answer inside a <see cref="ChatQuestionCard"/>.</summary>
    /// <remarks>
    /// Its own component rather than a label and a <see cref="Button"/> wired by hand in the card,
    /// because these are cloned at runtime: the card cannot reach into a prefab it has not made yet
    /// to find the right child, and a template that knows its own parts survives being restyled.
    /// </remarks>
    public sealed class ChatOptionButton : MonoBehaviour
    {
        [SerializeField] Button button;

        [SerializeField] TMP_Text label;

        Action<string> clicked;
        string option;

        /// <summary>Shows <paramref name="text"/> and reports it verbatim when clicked.</summary>
        /// <remarks>
        /// The text is the answer. The platform carries no option ids — see
        /// <see cref="Hiro.ChatQuestion"/> — so what is drawn and what is sent must be the same
        /// string, and shortening a label for the layout would change the reply.
        /// </remarks>
        public void Bind(string text, Action<string> onClicked)
        {
            option = text;
            clicked = onClicked;

            if (label != null)
            {
                label.text = text;
            }

            if (button != null)
            {
                // Wired here rather than in Awake, so binding is all a caller has to do and the
                // listener cannot depend on a lifecycle callback that has not run — which is also
                // what makes the click testable outside play mode. Removed first: Bind is called
                // again every redraw, and adding blindly would stack a listener per redraw and
                // fire the same answer several times.
                button.onClick.RemoveListener(Fire);
                button.onClick.AddListener(Fire);
            }

            SetInteractable(true);
        }

        public void SetInteractable(bool interactable)
        {
            if (button != null)
            {
                button.interactable = interactable;
            }
        }

        void Fire()
        {
            var handler = clicked;
            if (handler != null)
            {
                handler(option);
            }
        }
    }
}
