using System.Collections.Generic;

namespace Dojo.Framework.Inventory
{
    /// <summary>
    /// Everything the player has, from every pack they hold, filed by what it is.
    /// </summary>
    /// <remarks>
    /// Deliberately separate from <c>IContentService</c>, and layered on top of it. The content
    /// service answers one question — "give me the asset at this address" — and knows nothing about
    /// furniture, skins or categories. This answers the other question: "what does the player have,
    /// and what is each thing for". Keeping them apart is what lets a pack contain any asset at
    /// all: delivery needs no vocabulary of item types, and the inventory needs no knowledge that
    /// Addressables exists.
    /// </remarks>
    public interface IItemCatalog
    {
        /// <summary>
        /// Re-reads the manifest of every resident pack. Cheap and idempotent: call it after a
        /// preload, including a pack bought mid-session.
        /// </summary>
        void Refresh();

        /// <summary>Every item the player holds, across every pack. Never null.</summary>
        IReadOnlyList<ItemDefinition> All { get; }

        /// <summary>
        /// Every item in one category — all the chairs, all the 3D skins. Never null, and empty
        /// for a category no resident pack contributes to.
        /// </summary>
        IReadOnlyList<ItemDefinition> InCategory(string category);

        /// <summary>
        /// Every item in any of these categories, in the order the categories are given. What a
        /// consumer uses to declare the slice of the catalogue it understands.
        /// </summary>
        IReadOnlyList<ItemDefinition> InCategories(IReadOnlyList<string> categories);

        /// <summary>
        /// The categories the resident packs actually contribute to, in first-seen order. Lets a
        /// consumer discover what arrived rather than assume a fixed set.
        /// </summary>
        IReadOnlyList<string> Categories { get; }

        /// <summary>Finds one item by its id.</summary>
        bool TryGet(string id, out ItemDefinition item);

        /// <summary>Which pack an item came from, or empty when the catalogue has no such item.</summary>
        string PackOf(string id);
    }
}
