using TMPro;
using UnityEngine;

namespace Dojo.Game.UI.Account
{
    /// <summary>
    /// Borrows a card's subtitle to say why sign-in stopped - "Sign-in cancelled." - and gives the
    /// subtitle back once there is nothing to say.
    /// </summary>
    [RequireComponent(typeof(TMP_Text))]
    public sealed class CardMessage : MonoBehaviour
    {
        [SerializeField] Color warning = new Color(1f, 0.71f, 0.29f, 1f);   // the kit's caution, #ffb54a

        TMP_Text label;
        string usualText;
        Color usualColour;

        public void Show(string message)
        {
            if (label == null)
            {
                // Remembered on first use, not in Awake: the card may be hidden, and never woken, when
                // the first message arrives.
                label = GetComponent<TMP_Text>();
                usualText = label.text;
                usualColour = label.color;
            }

            var any = !string.IsNullOrEmpty(message);
            label.text = any ? message : usualText;
            label.color = any ? warning : usualColour;
        }
    }
}
