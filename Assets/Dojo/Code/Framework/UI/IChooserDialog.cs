using System;
using System.Collections.Generic;

namespace Dojo.Framework.UI
{
    /// <summary>A dialog that asks the player to pick one of a list.</summary>
    public interface IChooserDialog : IDialog
    {
        /// <summary>What is currently picked, or null.</summary>
        string Chosen { get; }

        /// <summary>Puts the list on screen. Nothing is picked to begin with.</summary>
        void Choose(string heading, IList<string> choices, Action<string> onConfirm, Action onCancel = null);
    }
}
