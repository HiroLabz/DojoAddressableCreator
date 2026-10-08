namespace Dojo.Framework.UI
{
    /// <summary>
    /// A panel that can be brought on and off screen. Depend on this rather than on a concrete
    /// group when something needs to open or close a popup without caring which one it is — a
    /// menu controller closing every open popup, say.
    /// </summary>
    public interface IInGamePopup
    {
        /// <summary>True while the popup is on screen.</summary>
        bool IsOpen { get; }

        /// <summary>Brings the popup on screen. Safe to call when it is already open.</summary>
        void Show();

        /// <summary>Sends the popup back off screen. Safe to call when it is already closed.</summary>
        void Hide();

        /// <summary>Shows the popup when closed, hides it when open.</summary>
        void Toggle();
    }
}
