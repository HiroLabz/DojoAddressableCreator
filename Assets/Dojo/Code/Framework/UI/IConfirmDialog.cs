using System;

namespace Dojo.Framework.UI
{
    /// <summary>A dialog that asks a yes/no question.</summary>
    public interface IConfirmDialog : IDialog
    {
        /// <summary>
        /// Puts the question on screen. <paramref name="onConfirm"/> runs if the player accepts,
        /// <paramref name="onCancel"/> if they dismiss it; both are optional.
        /// </summary>
        void Ask(string question, Action onConfirm, Action onCancel = null);
    }
}
