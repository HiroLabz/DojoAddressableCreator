using System.Collections.Generic;

namespace Dojo.Framework.UI
{
    /// <summary>
    /// One flavour pack as the shop needs to show it.
    /// </summary>
    /// <remarks>
    /// A presentation model, for the reason <see cref="WorldChoice"/> gives for being one: the shop
    /// draws a card and never opens a pack, so handing it the pack itself would mean loading every
    /// pack in the catalogue just to draw the catalogue.
    /// <para>
    /// <see cref="Price"/> and <see cref="ReleasesAt"/> arrive already formatted. What a price looks
    /// like is a question of currency and locale, and what "March 2027" should read as is a question
    /// of where the player is — neither is something a card can answer, and both are things the
    /// caller already knows.
    /// </para>
    /// <para>
    /// Availability is a single flag rather than a status enum. The mock draws exactly two states, a
    /// pack you may buy and a pack you may not, and the second carries a release date instead of a
    /// price; a third state would be a change to the card's design before it is a change to this.
    /// </para>
    /// </remarks>
    public readonly struct ShopPack
    {
        /// <summary>The pack's key, and the value handed back when it is bought.</summary>
        public readonly string Key;

        /// <summary>The pack's name, as shown on the card.</summary>
        public readonly string Name;

        /// <summary>The paragraph under the name.</summary>
        public readonly string Description;

        /// <summary>
        /// What the pack contains, one bullet per line. A locked pack lists its contents as
        /// "Locked" — deciding that is the caller's, since only it knows what is being withheld.
        /// </summary>
        public readonly IList<string> Contents;

        /// <summary>What it costs, already formatted. Empty on a pack that cannot be bought yet.</summary>
        public readonly string Price;

        /// <summary>When it ships, already formatted. Empty on a pack that is already out.</summary>
        public readonly string ReleasesAt;

        /// <summary>Whether the player can buy it now, as opposed to being shown it coming.</summary>
        public readonly bool IsAvailable;

        public ShopPack(
            string key,
            string name,
            string description,
            IList<string> contents,
            bool isAvailable,
            string price = null,
            string releasesAt = null)
        {
            Key = key ?? string.Empty;
            Name = name ?? string.Empty;
            Description = description ?? string.Empty;
            Contents = contents;
            IsAvailable = isAvailable;
            Price = price ?? string.Empty;
            ReleasesAt = releasesAt ?? string.Empty;
        }
    }
}
