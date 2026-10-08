using System;
using System.Collections.Generic;

namespace Dojo.Framework.UI
{
    /// <summary>A dialog that asks the player what to save the current world as.</summary>
    /// <remarks>
    /// The mirror of <see cref="ILoadWorldDialog"/>, and deliberately shaped like it: the same rows,
    /// carrying the same counts and save times. Choosing what to overwrite is the same act of
    /// recognition as choosing what to open, and a player who can tell two saves apart on one screen
    /// should not have to tell them apart from bare names on the other.
    /// <para>
    /// That is also what separates this from <see cref="ISaveAsDialog"/>, which asks the same
    /// question about a plain list of names. This one is worth having only while it shows more than
    /// a name; if the rows are ever reduced to names, the two should be merged rather than kept as
    /// near-duplicates.
    /// </para>
    /// <para>
    /// The interface is how the dialog is identified and resolved, exactly as with the others — see
    /// <see cref="IDialogManager.OpenDialog{T}"/>.
    /// </para>
    /// </remarks>
    public interface ISaveWorldDialog : IDialog
    {
        /// <summary>The name the world would be saved under, or null.</summary>
        string Target { get; }

        /// <summary>
        /// Puts the dialog on screen.
        /// </summary>
        /// <remarks>
        /// Nothing is picked to begin with and the accepting button stays disabled until there is a
        /// name, the same bargain the load dialog makes. It matters more here: a default selection
        /// on a save screen is a way to overwrite a world by reflex.
        /// </remarks>
        /// <param name="worlds">Saves already on disk, offered as rows to overwrite.</param>
        /// <param name="suggested">
        /// Pre-filled into the name field, or empty to leave it blank. The world's current name
        /// belongs here, so re-saving is a confirmation rather than a retyping.
        /// </param>
        /// <param name="onConfirm">Given the name to save under, whether typed or picked.</param>
        /// <param name="onCancel">Called when the player backs out, if supplied.</param>
        /// <param name="onDelete">
        /// Given a world to delete. Leave null and the per-row delete buttons are hidden, so the
        /// dialog never offers a control that would do nothing.
        /// </param>
        void Ask(
            IList<WorldChoice> worlds,
            string suggested,
            Action<string> onConfirm,
            Action onCancel = null,
            Action<string> onDelete = null);
    }
}
