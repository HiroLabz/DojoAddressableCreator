using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Framework.UI
{
    /// <summary>
    /// The popup shell from the Dialogs mock-up: a coloured top edge that names the tone, an icon
    /// badge, a title that states the outcome, a line or two of explanation, and up to three
    /// buttons right-aligned in ascending commitment.
    /// </summary>
    /// <remarks>
    /// One script behind four prefabs in <c>Prefabs/UI/Popups</c>. What differs between them - the
    /// colour, the icon, which buttons there are - is baked into each prefab, not chosen at run time:
    /// <list type="bullet">
    /// <item><b>NoticePopup</b> - red, no buttons. It stays until whoever showed it hides it, so its
    /// words must say what to do instead: there is nothing to click.</item>
    /// <item><b>AffirmationPopup</b> - green, one button, which dismisses it.</item>
    /// <item><b>ConfirmationPopup</b> - blue, Cancel and the primary.</item>
    /// <item><b>ThreeWayPopup</b> - red, Cancel, a destructive middle, and the primary. The
    /// destructive choice is never where the primary usually is, so muscle memory cannot fire it.</item>
    /// </list>
    /// The colour baked in is only the default. <see cref="Tint"/> gives any of them any
    /// <see cref="Tone"/>, because the buttons a message needs and what it means are separate
    /// questions - an error that offers QUIT or RETRY wants the Confirmation's buttons in red.
    /// <para>
    /// A press reports the button through <see cref="Chosen"/>, then hides the popup; the
    /// <see cref="PopupManager"/> takes it away once it has gone. The card grows to fit its words,
    /// and each button to fit its caption, so any title, explanation or caption fits.
    /// </para>
    /// <para>
    /// Built by <c>Tools ▸ Dojo ▸ Build Popup Prefabs</c>, which fills in every reference below.
    /// </para>
    /// </remarks>
    public sealed class MessagePopup : PopupBase
    {
        /// <summary>What a message means, and so its colour.</summary>
        public enum Tone
        {
            /// <summary>Red: an error or a warning.</summary>
            Alert,

            /// <summary>Green: a go - it worked, carry on.</summary>
            Go,

            /// <summary>Blue: information, and any message that is neither of the others.</summary>
            Info,
        }

        /// <summary>The colour of a tone, from the UI kit's palette.</summary>
        public static Color ColorOf(Tone tone)
        {
            switch (tone)
            {
                case Tone.Alert: return new Color32(0xff, 0x5f, 0x6d, 0xff);
                case Tone.Go: return new Color32(0x46, 0xd3, 0x9a, 0xff);
                default: return new Color32(0x2f, 0x8f, 0xff, 0xff);
            }
        }
        /// <summary>Which button was pressed, named for its job rather than its caption.</summary>
        public enum Choice
        {
            /// <summary>The leftmost, always a ghost: back out, nothing happens.</summary>
            Cancel,

            /// <summary>The red middle one: go ahead and lose something.</summary>
            Destructive,

            /// <summary>The rightmost, the one the popup is asking for.</summary>
            Primary,
        }

        [Header("Words")]
        [SerializeField] TMP_Text title;
        [SerializeField] TMP_Text body;
        [Tooltip("The small line under the explanation - where it came from, when. Hidden when empty.")]
        [SerializeField] TMP_Text detail;

        [Header("Buttons (leave empty for one this popup does not have)")]
        [SerializeField] Button cancel;
        [SerializeField] Button destructive;
        [SerializeField] Button primary;

        [Header("Answer (leave empty for a popup that asks for nothing typed)")]
        [Tooltip("A box for the player to type an answer in - a name. The primary button stays off " +
                 "while it is empty, and Enter presses it.")]
        [SerializeField] TMP_InputField answer;

        [Header("Tone")]
        [Tooltip("Everything drawn in the popup's colour: the top edge, the corner markers and the "
            + "badge. Each keeps its own transparency when tinted.")]
        [SerializeField] Graphic[] toned;

        [Tooltip("The mark in the badge, which changes with the tone.")]
        [SerializeField] Image icon;

        [SerializeField] Sprite alertMark;
        [SerializeField] Sprite goMark;
        [SerializeField] Sprite infoMark;

        bool answered;

        /// <summary>A button was pressed. Raised once, just before the popup starts hiding.</summary>
        public event Action<Choice> Chosen;

        /// <summary>The buttons this popup has, left to right.</summary>
        public IReadOnlyList<Choice> Choices
        {
            get
            {
                var present = new List<Choice>();

                foreach (Choice choice in Enum.GetValues(typeof(Choice)))
                {
                    if (ButtonFor(choice) != null)
                    {
                        present.Add(choice);
                    }
                }

                // By where each one sits in its row, which is what the player sees.
                present.Sort((a, b) => ButtonFor(a).transform.GetSiblingIndex()
                    .CompareTo(ButtonFor(b).transform.GetSiblingIndex()));

                return present;
            }
        }

        /// <summary>Whether this popup has that button.</summary>
        public bool Has(Choice choice) => ButtonFor(choice) != null;

        /// <summary>What it says. The detail line is hidden when there is none.</summary>
        public MessagePopup Present(string heading, string explanation, string footnote = null)
        {
            if (title != null)
            {
                title.text = heading;
            }

            if (body != null)
            {
                body.text = explanation;
            }

            if (detail != null)
            {
                detail.text = footnote ?? string.Empty;
                detail.gameObject.SetActive(!string.IsNullOrEmpty(footnote));
            }

            return this;
        }

        /// <summary>What the player typed, trimmed. Empty for a popup with no box to type in.</summary>
        public string Answer => answer != null ? answer.text.Trim() : string.Empty;

        /// <summary>
        /// Starts the box off with <paramref name="suggestion"/>, all of it selected so typing
        /// replaces it, and puts the cursor there. Ignored by a popup with no box.
        /// </summary>
        public MessagePopup Ask(string suggestion)
        {
            if (answer == null)
            {
                return this;
            }

            answer.text = suggestion ?? string.Empty;
            answer.ActivateInputField();
            answer.selectionAnchorPosition = 0;
            answer.selectionFocusPosition = answer.text.Length;
            RefreshAnswer();

            return this;
        }

        /// <summary>Renames a button - "LOAD WORLD" instead of the prefab's own. Ignored if it has none.</summary>
        public MessagePopup Caption(Choice choice, string caption)
        {
            var button = ButtonFor(choice);
            var label = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;

            if (label != null)
            {
                label.text = caption;
            }

            return this;
        }

        /// <summary>
        /// Colours the popup for what it means - red for an error or a warning, green for a go,
        /// blue for the rest - whichever prefab it is.
        /// </summary>
        public MessagePopup Tint(Tone tone)
        {
            var colour = ColorOf(tone);

            if (toned != null)
            {
                foreach (var graphic in toned)
                {
                    if (graphic != null)
                    {
                        graphic.color = new Color(colour.r, colour.g, colour.b, graphic.color.a);
                    }
                }
            }

            var mark = MarkFor(tone);

            if (icon != null && mark != null)
            {
                icon.sprite = mark;
            }

            return this;
        }

        Sprite MarkFor(Tone tone)
        {
            switch (tone)
            {
                case Tone.Alert: return alertMark;
                case Tone.Go: return goMark;
                default: return infoMark;
            }
        }

        protected override void Build()
        {
            Listen(cancel, Choice.Cancel);
            Listen(destructive, Choice.Destructive);
            Listen(primary, Choice.Primary);

            if (answer != null)
            {
                answer.onValueChanged.AddListener(_ => RefreshAnswer());

                // Enter answers, as the primary button would - but not with nothing typed.
                answer.onSubmit.AddListener(_ =>
                {
                    if (Answer.Length > 0)
                    {
                        Choose(Choice.Primary);
                    }
                });

                RefreshAnswer();
            }
        }

        /// <summary>The primary button only answers once something has been typed.</summary>
        void RefreshAnswer()
        {
            if (answer != null && primary != null)
            {
                primary.interactable = Answer.Length > 0;
            }
        }

        public override void Show()
        {
            answered = false;
            base.Show();
        }

        void Listen(Button button, Choice choice)
        {
            if (button != null)
            {
                button.onClick.AddListener(() => Choose(choice));
            }
        }

        /// <summary>
        /// Reports the press and closes. One answer per showing: a second click that lands while
        /// it fades out is not a second decision.
        /// </summary>
        void Choose(Choice choice)
        {
            if (answered || !IsOpen)
            {
                return;
            }

            answered = true;

            try
            {
                var handler = Chosen;
                if (handler != null)
                {
                    handler(choice);
                }
            }
            finally
            {
                // Closed even if a listener throws, so a bug behind a button cannot leave the
                // player stuck under a popup that no longer does anything.
                Hide();
            }
        }

        Button ButtonFor(Choice choice)
        {
            switch (choice)
            {
                case Choice.Cancel: return cancel;
                case Choice.Destructive: return destructive;
                case Choice.Primary: return primary;
                default: return null;
            }
        }

        protected override void OnDestroy()
        {
            foreach (var button in new[] { cancel, destructive, primary })
            {
                if (button != null)
                {
                    button.onClick.RemoveAllListeners();
                }
            }

            if (answer != null)
            {
                answer.onValueChanged.RemoveAllListeners();
                answer.onSubmit.RemoveAllListeners();
            }

            base.OnDestroy();
        }
    }
}
