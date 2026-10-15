using System;
using System.Collections.Generic;

namespace Dojo.Framework.UI
{
    /// <summary>A dialog that shows the flavour packs and lets the player buy one.</summary>
    /// <remarks>
    /// Its own interface rather than a dressed-up <see cref="IChooserDialog"/>, for the reason
    /// <see cref="ILoadWorldDialog"/> gives: a chooser offers names and asks which, this offers packs
    /// — each with contents, a price or a release date, and a buy of its own — and it does not ask
    /// the player to pick one at all. Closing it having bought nothing is the ordinary outcome.
    /// <para>
    /// The interface is how the dialog is identified and resolved, exactly as with the others — see
    /// <see cref="IDialogManager.OpenDialog{T}"/>.
    /// </para>
    /// </remarks>
    public interface IShopDialog : IDialog
    {
        /// <summary>
        /// Puts the catalogue on screen. Nothing is selected and nothing needs to be: unlike the
        /// world dialogs there is no accepting button waiting on a choice, because each pack carries
        /// its own buy.
        /// </summary>
        /// <param name="packs">
        /// What to offer, in the order to show them. Null or empty is allowed and shows the empty
        /// notice — a shop with nothing in it yet is a state worth drawing rather than a fault.
        /// </param>
        /// <param name="onBought">
        /// Given the pack's <see cref="ShopPack.Key"/> once it has actually been acquired, which
        /// includes finding it was already owned. Unlike the world dialogs' callbacks this reports
        /// what happened rather than asking the caller to make it happen: the dialog runs the
        /// acquisition itself through <c>IEntitlementGrants</c>, so by the time this arrives the
        /// pack is down and the catalogue has been refreshed. A failed acquisition never raises it
        /// and leaves the shop open to try again.
        /// <para>
        /// Anything that wants the change rather than the notice should listen for
        /// <c>EntitlementsChanged</c> instead — that reaches listeners which never opened the shop.
        /// </para>
        /// </param>
        /// <param name="onClose">Called when the player closes the shop, if supplied.</param>
        void Browse(IList<ShopPack> packs, Action<string> onBought, Action onClose = null);
    }
}
