using System;
using UnityEngine;

namespace Dojo.Framework.Inventory
{
    /// <summary>
    /// The categories this build knows the names of. Not an exhaustive list, and deliberately not
    /// an enum.
    /// </summary>
    /// <remarks>
    /// A category classifies any object usable in the game, so the set grows with the content
    /// rather than with the code: the moment a pack ships an item categorised <c>3dskins</c>, that
    /// category exists, and nothing here has to be edited for it to work. These constants are a
    /// convenience for the consumers that look for known categories by name, not a registry — a
    /// category absent from this class is still valid, and one present here with no items is simply
    /// empty.
    /// </remarks>
    public static class ItemCategories
    {
        public const string Floors = "floors";
        public const string Walls = "walls";
        public const string Tables = "tables";
        public const string Plants = "plants";
        public const string Appliances = "appliances";
        public const string Drawers = "drawers";
        public const string Furnitures = "furnitures";
        public const string Others = "others";

        /// <summary>Named with the digit last, because an identifier cannot begin with one.</summary>
        public const string Skins2D = "2dskins";

        public const string Skins3D = "3dskins";
        public const string Effects = "effects";
        public const string Audio = "audio";

        /// <summary>
        /// The categories the room builder places. Declared here rather than inferred, so a
        /// category no placement code understands is simply not offered instead of half-working.
        /// </summary>
        public static readonly string[] Placeable =
        {
            Floors, Walls, Tables, Plants, Appliances, Drawers, Furnitures, Others,
        };
    }

    /// <summary>
    /// One object the player can use: a piece of furniture, a skin, an effect, a sound. Authored
    /// per pack and read through the content service, which is the only thing that knows how it
    /// arrived.
    /// </summary>
    /// <remarks>
    /// <see cref="category"/> is declared rather than inferred. The drawer used to decide a piece's
    /// tab by looking for keywords in its prefab name, which meant renaming an asset silently moved
    /// it between tabs, a piece matching no keyword fell through to a catch-all, and each new piece
    /// that tripped a rule needed another exclusion adding. A pack states what its contents are.
    /// <para>
    /// <see cref="address"/> is the object itself and <see cref="icon"/> is its picture. Which is
    /// which is carried by the field, never by the path, the folder or the asset type — that is
    /// what lets an item of any kind be described by the same record.
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class ItemDefinition
    {
        /// <summary>Stable id, unique across every pack. Conventionally <c>&lt;pack&gt;.&lt;slug&gt;</c>.</summary>
        public string id;

        /// <summary>What this is. See <see cref="ItemCategories"/>.</summary>
        public string category;

        /// <summary>Shown to the player.</summary>
        public string name;

        /// <summary>
        /// The content address of the object itself, and the string a saved world persists.
        /// </summary>
        public string address;

        /// <summary>Content address of this item's icon, or empty when it needs none.</summary>
        public string icon;

        /// <summary>What the item applies to — <c>owner</c> for a skin. Empty when meaningless.</summary>
        public string target;

        /// <summary>
        /// Category-specific payload as raw JSON, for the categories that carry more than the
        /// fields above. Left to the consumer of that category to parse, so a new one needs no
        /// change here.
        /// </summary>
        /// <summary>
        /// How many grid cells the item occupies, width by depth, unrotated. Zero when the pack
        /// did not state one.
        /// </summary>
        /// <remarks>
        /// Stated by the pack rather than measured from the prefab. The footprint is authored on
        /// <c>FurniturePiece</c>, which means reading it costs a prefab load — affordable for the
        /// one piece being placed, not for every tile in an open drawer. A manifest that carries it
        /// lets the drawer label a tile without loading anything.
        /// <para>
        /// Zero is the honest answer for every manifest written before this field existed, and
        /// consumers are expected to show nothing rather than guess. Packs report it from their
        /// next rebuild.
        /// </para>
        /// </remarks>
        public Vector2Int footprint;

        public string data;
    }
}
