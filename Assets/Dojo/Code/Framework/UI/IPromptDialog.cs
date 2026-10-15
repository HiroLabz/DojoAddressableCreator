using System;

namespace Dojo.Framework.UI
{
    /// <summary>A dialog that asks for a line of text.</summary>
    public interface IPromptDialog : IDialog
    {
        /// <summary>
        /// Puts the question on screen with a box to answer it in.
        /// </summary>
        /// <param name="suggestion">What the box starts with, ready to be typed over.</param>
        void Ask(string question, string suggestion, Action<string> onConfirm, Action onCancel = null);
    }
}
