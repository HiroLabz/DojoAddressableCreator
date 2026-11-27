using System;
using System.Collections.Generic;
using Dojo.Framework.UI;
using Dojo.Game.Managers;
using TMPro;

namespace Dojo.Game.UI
{
    /// <summary>
    /// The game's questions, asked in the styled popups (<see cref="PopupPrefabs"/>), with the old
    /// dialog standing in where a popup prefab is missing, so a missing asset costs the look and
    /// not the question. One place for the pattern <c>RoomPlacer</c> and <c>AgentDisplayUI</c> set
    /// (QA issue 8, PR #170), for the dialogs that were still drawn the old way (Bob, 23 Nov;
    /// ClickUp 14ze67xg5hb).
    /// </summary>
    public static class PopupAsk
    {
        public const string CancelCaption = "CANCEL";

        /// <summary>
        /// Yes or no in the Confirmation popup: Cancel and <paramref name="yes"/>, in the
        /// <paramref name="tone"/> given (alert for a destructive answer).
        /// </summary>
        public static void Confirm(PopupPrefabs popups, IDialogManager dialogs, string heading, string body, string yes,
            MessagePopup.Tone tone, Action onYes, Action onNo = null)
        {
            var layer = Layer(popups?.Confirmation);

            if (layer == null)
            {
                dialogs?.Confirm(Joined(heading, body), onYes, onNo);
                return;
            }

            var popup = layer.Show(popups.Confirmation)
                .Tint(tone)
                .Present(heading, body)
                .Caption(MessagePopup.Choice.Cancel, CancelCaption)
                .Caption(MessagePopup.Choice.Primary, yes);

            popup.Chosen += choice =>
            {
                if (choice == MessagePopup.Choice.Primary)
                {
                    onYes?.Invoke();
                }
                else
                {
                    onNo?.Invoke();
                }
            };
        }

        /// <summary>
        /// A line of text in the Prompt popup, starting from <paramref name="suggestion"/>, with
        /// <paramref name="hint"/> in the empty box. Cancel, Esc or nothing typed is
        /// <paramref name="cancelled"/>.
        /// </summary>
        public static void Prompt(PopupPrefabs popups, IDialogManager dialogs, string heading, string question,
            string suggestion, string hint, string save, Action<string> typed, Action cancelled = null)
        {
            var layer = Layer(popups?.Prompt);

            if (layer == null)
            {
                dialogs?.Prompt(question, suggestion, typed, cancelled, hint);
                return;
            }

            var popup = layer.Show(popups.Prompt)
                .Tint(MessagePopup.Tone.Info)
                .Present(heading, question)
                .Caption(MessagePopup.Choice.Cancel, CancelCaption)
                .Caption(MessagePopup.Choice.Primary, save)
                .Ask(suggestion);

            Hint(popup, hint);

            popup.Chosen += choice =>
            {
                if (choice == MessagePopup.Choice.Primary && popup.Answer.Length > 0)
                {
                    typed?.Invoke(popup.Answer);
                }
                else
                {
                    cancelled?.Invoke();
                }
            };
        }

        /// <summary>
        /// One of <paramref name="options"/> in the Chooser popup's dropdown, the first picked to
        /// start with; Cancel and <paramref name="pick"/>.
        /// </summary>
        public static void Choose(PopupPrefabs popups, IDialogManager dialogs, string heading, string question,
            IList<string> options, string pick, Action<string> chosen, Action cancelled = null)
        {
            var layer = Layer(popups?.Chooser);

            if (layer == null)
            {
                dialogs?.Choose(heading, options, chosen, cancelled);
                return;
            }

            var popup = layer.Show(popups.Chooser)
                .Tint(MessagePopup.Tone.Info)
                .Present(heading, question)
                .Caption(MessagePopup.Choice.Cancel, CancelCaption)
                .Caption(MessagePopup.Choice.Primary, pick);

            var picker = popup.GetComponent<ChoicePicker>();
            // Unity's own null check: a prefab that lost its picker answers as a fake null here.
            if (picker != null)
            {
                picker.Bind(options);
            }

            popup.Chosen += choice =>
            {
                var picked = picker != null ? picker.Picked : null;

                if (choice == MessagePopup.Choice.Primary && picked != null)
                {
                    chosen?.Invoke(picked);
                }
                else
                {
                    cancelled?.Invoke();
                }
            };
        }

        /// <summary>
        /// A name to save under, in the Prompt popup, starting from <paramref name="suggestion"/>. A
        /// name that is already one of <paramref name="existing"/> asks "Replace?" in the alert tone
        /// first, and answers with the existing name's own spelling. The old save-as dialog, with its
        /// rows of names to replace, stands in only where the Prompt popup is missing.
        /// <paramref name="what"/> is the kind of thing being saved, as the question names it: a
        /// room, or a floor plan.
        /// </summary>
        public static void SaveAs(PopupPrefabs popups, IDialogManager dialogs, string heading, string question,
            IList<string> existing, string suggestion, string hint, Action<string> onName, Action cancelled = null,
            string what = "room")
        {
            if (Layer(popups?.Prompt) == null)
            {
                dialogs?.SaveAs(heading, existing, onName, cancelled);
                return;
            }

            Prompt(popups, dialogs, heading, question, suggestion, hint, "SAVE", name =>
            {
                var replacing = Existing(name, existing);

                if (replacing == null)
                {
                    onName?.Invoke(name);
                    return;
                }

                Confirm(popups, dialogs, "Replace '" + replacing + "'?",
                    "You already have a " + what + " called that. Saving replaces it.", "REPLACE",
                    MessagePopup.Tone.Alert, () => onName?.Invoke(replacing), cancelled);
            }, cancelled);
        }

        /// <summary>
        /// The one of <paramref name="existing"/> that <paramref name="name"/> names, whatever its
        /// case or stray spaces, in its own spelling; null when it names none.
        /// </summary>
        public static string Existing(string name, IList<string> existing)
        {
            if (string.IsNullOrWhiteSpace(name) || existing == null)
            {
                return null;
            }

            var wanted = name.Trim();

            foreach (var candidate in existing)
            {
                if (candidate != null && string.Equals(candidate.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>The option a dropdown's <paramref name="value"/> stands for, or null when it stands for none.</summary>
        public static string PickedOf(int value, IList<string> options)
            => options != null && value >= 0 && value < options.Count ? options[value] : null;

        /// <summary>The popup layer when <paramref name="prefab"/> can be shown on it; null to use the dialog.</summary>
        static PopupManager Layer(MessagePopup prefab) => prefab != null ? PopupManager.Instance : null;

        /// <summary>The Prompt's own hint names an area; the caller's names what it asks for.</summary>
        static void Hint(MessagePopup popup, string hint)
        {
            if (string.IsNullOrEmpty(hint))
            {
                return;
            }

            var box = popup.GetComponentInChildren<TMP_InputField>(true);
            if (box != null && box.placeholder is TMP_Text placeholder)
            {
                placeholder.text = hint;
            }
        }

        /// <summary>The dialog takes one line: the heading and the body together.</summary>
        static string Joined(string heading, string body)
            => string.IsNullOrEmpty(heading) ? body ?? string.Empty
                : string.IsNullOrEmpty(body) ? heading : heading + "\n\n" + body;
    }
}
