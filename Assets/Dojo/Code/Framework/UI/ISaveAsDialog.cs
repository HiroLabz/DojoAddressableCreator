using System;
using System.Collections.Generic;

namespace Dojo.Framework.UI
{
    /// <summary>
    /// A dialog for saving: pick one of the existing names to replace, or type a new one.
    /// </summary>
    public interface ISaveAsDialog : IDialog
    {
        /// <summary>The name that would be saved under, or null.</summary>
        string Target { get; }

        /// <summary>Puts the dialog on screen.</summary>
        /// <param name="existing">Names already saved, offered as rows to replace.</param>
        void Ask(string heading, IList<string> existing, Action<string> onConfirm, Action onCancel = null);
    }
}
