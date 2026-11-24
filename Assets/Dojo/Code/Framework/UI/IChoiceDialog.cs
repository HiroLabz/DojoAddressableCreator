using System;
using System.Collections.Generic;

namespace Dojo.Framework.UI
{
    /// <summary>A question with a row of two or three answers, each its own button.</summary>
    public interface IChoiceDialog : IDialog
    {
        /// <summary>
        /// Puts the question up with one button per answer, left to right. <paramref name="onChoose"/>
        /// is given the index of the answer pressed. The first answer is drawn as the accepting one.
        /// </summary>
        void Ask(string question, IList<string> answers, Action<int> onChoose);
    }
}
