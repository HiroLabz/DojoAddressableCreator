using System;
using System.Collections.Generic;

namespace Dojo.Framework.UI
{
    /// <summary>A dialog that asks the player which saved world to open.</summary>
    /// <remarks>
    /// Its own interface rather than a dressed-up <see cref="IChooserDialog"/>, because it asks a
    /// different question: a chooser offers names, this offers worlds — each with counts, a save
    /// time, and a delete of its own. Sharing the chooser would mean widening every caller's idea of
    /// a choice to carry fields only this screen uses.
    /// <para>
    /// The interface is how the dialog is identified and resolved, exactly as with the other four —
    /// see <see cref="IDialogManager.OpenDialog{T}"/>.
    /// </para>
    /// </remarks>
    public interface ILoadWorldDialog : IDialog
    {
        /// <summary>The world currently picked, or null.</summary>
        string Chosen { get; }

        /// <summary>
        /// Puts the list of worlds on screen. Nothing is picked to begin with, and the accepting
        /// button stays disabled until something is — picking a default here is a way to replace
        /// the open floor by reflex.
        /// </summary>
        /// <param name="worlds">What to offer, in the order to show them.</param>
        /// <param name="onConfirm">Given the chosen world's name.</param>
        /// <param name="onCancel">Called when the player backs out, if supplied.</param>
        /// <param name="onDelete">
        /// Given a world to delete. Leave null and the per-row delete buttons are hidden, so the
        /// dialog never offers a control that would do nothing.
        /// </param>
        void Choose(
            IList<WorldChoice> worlds,
            Action<string> onConfirm,
            Action onCancel = null,
            Action<string> onDelete = null);
    }
}
