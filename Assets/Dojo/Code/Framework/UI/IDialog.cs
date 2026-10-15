namespace Dojo.Framework.UI
{
    /// <summary>
    /// A modal that can be put on screen and taken off again, with a hook at each end.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="IInGamePopup"/> on purpose, though the two look alike. A popup is a
    /// panel the player opens and closes at will and several can reasonably be open; a dialog is a
    /// question that owns the screen until it is answered, and something is always waiting on the
    /// answer. Toggling a dialog would be meaningless, which is why that is not here.
    /// <para>
    /// The four methods are two pairs: <see cref="Show"/> and <see cref="Hide"/> begin the change,
    /// and <see cref="ShowComplete"/> and <see cref="HideComplete"/> mark it finished. A dialog that
    /// simply appears calls the second of each pair itself, immediately. One that slides or fades
    /// leaves them for whatever drives the animation to call at the end — so the moment a dialog is
    /// genuinely ready to be typed into, or genuinely gone, is something a caller can rely on rather
    /// than guess at with a coroutine.
    /// </para>
    /// </remarks>
    public interface IDialog
    {
        /// <summary>True from <see cref="Show"/> until the dialog is off screen again.</summary>
        bool IsOpen { get; }

        /// <summary>Starts putting the dialog on screen.</summary>
        void Show();

        /// <summary>
        /// The dialog is fully on screen. Where anything that needs it to be visible belongs —
        /// taking keyboard focus, most obviously, which does nothing to an object that is still
        /// switched off.
        /// </summary>
        void ShowComplete();

        /// <summary>
        /// Starts taking the dialog off screen. Does not answer it: a caller waiting on a reply is
        /// told by whichever button was pressed, not by this.
        /// </summary>
        void Hide();

        /// <summary>
        /// The dialog is fully off screen. Where it lets go of whoever was waiting on it, so a
        /// stale callback cannot be fired by a later question.
        /// </summary>
        void HideComplete();
    }
}
