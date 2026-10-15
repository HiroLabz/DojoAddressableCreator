using System;
using System.Collections.Generic;

namespace Dojo.Framework.UI
{
    /// <summary>
    /// The one owner of the game's modal dialogs: everything that needs to ask the player something
    /// asks it here.
    /// </summary>
    /// <remarks>
    /// A contract rather than the class itself so that anything can take it by injection, which is
    /// the point of the whole arrangement — a component that reached for the manager by searching
    /// the scene would work only where a scene search happens to find one, and would keep working
    /// by luck rather than by wiring.
    /// <para>
    /// Going through a single owner is also what stops two modals being on screen at once, each
    /// claiming to be the thing that must be answered while the one underneath still takes clicks
    /// through the gap. Opening a dialog closes whatever came before it.
    /// </para>
    /// </remarks>
    public interface IDialogManager
    {
        /// <summary>True while a dialog is waiting to be answered.</summary>
        bool IsAnyOpen { get; }

        /// <summary>
        /// Builds the dialog registered for <typeparamref name="T"/>, after taking down whatever was
        /// already being asked. Null if nothing is registered for it.
        /// </summary>
        /// <remarks>
        /// The interface asked for is what picks the dialog, so there is no id to keep in step with
        /// the set of dialogs. The dialog comes back un-shown: what to ask, and what to do with the
        /// answer, is the caller's business — see <see cref="Confirm"/> and the rest for the
        /// ordinary way in.
        /// </remarks>
        T OpenDialog<T>() where T : class, IDialog;

        /// <summary>Asks a yes/no question.</summary>
        void Confirm(string question, Action onConfirm, Action onCancel = null);

        /// <summary>Asks for a line of text.</summary>
        void Prompt(string question, string suggestion, Action<string> onConfirm, Action onCancel = null);

        /// <summary>Asks the player to pick one of a list.</summary>
        void Choose(string heading, IList<string> options, Action<string> onConfirm, Action onCancel = null);

        /// <summary>Asks which existing name to replace, or what to call something new.</summary>
        void SaveAs(string heading, IList<string> existing, Action<string> onConfirm, Action onCancel = null);

        /// <summary>
        /// Takes down whatever is being asked, and destroys it.
        /// </summary>
        /// <remarks>
        /// Does not answer it. A caller waiting on a reply is told by whichever button the player
        /// pressed, so a question closed this way simply drops its callbacks rather than handing
        /// back a cancel nobody chose.
        /// </remarks>
        void CloseAll();
    }
}
