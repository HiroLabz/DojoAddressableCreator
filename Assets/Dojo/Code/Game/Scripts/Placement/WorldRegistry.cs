using System;
using System.Collections.Generic;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// Every world the player has, in one dictionary, so that no save lands on a world it is not.
    /// </summary>
    /// <remarks>
    /// Held by <see cref="WorldService"/>, the one thing that saves worlds. It is filled from the
    /// file and the database together - a world only one of them knows is still a world, and one
    /// list asked on its own could miss it and hand its name to a new world. Worlds saved this
    /// session go in as they are saved, ahead of the database hearing about them.
    /// <para>
    /// A world is known by its name the way the library matches names: the spaces around it
    /// trimmed and its case ignored.
    /// </para>
    /// </remarks>
    public sealed class WorldRegistry
    {
        readonly Dictionary<string, string> worlds = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>How many worlds are known.</summary>
        public int Count => worlds.Count;

        /// <summary>Every known world's name, as it was spelled when it was added.</summary>
        public IEnumerable<string> Names => worlds.Values;

        /// <summary>Remembers a world. Knowing it twice is knowing it once.</summary>
        public void Add(string name)
        {
            var key = Key(name);

            if (key != null && !worlds.ContainsKey(key))
            {
                worlds.Add(key, name.Trim());
            }
        }

        /// <summary>Remembers every world in a list - the file's, or the database's.</summary>
        public void AddAll(IEnumerable<string> names)
        {
            if (names == null)
            {
                return;
            }

            foreach (var name in names)
            {
                Add(name);
            }
        }

        /// <summary>Forgets a deleted world.</summary>
        public void Remove(string name)
        {
            var key = Key(name);

            if (key != null)
            {
                worlds.Remove(key);
            }
        }

        /// <summary>True when a world by this name exists.</summary>
        public bool Contains(string name)
        {
            var key = Key(name);
            return key != null && worlds.ContainsKey(key);
        }

        /// <summary>
        /// The name to save the open world under: its own, or - for a world with none yet - the
        /// next World# no known world has.
        /// </summary>
        /// <remarks>
        /// A new name is taken the moment it is given, so two unnamed worlds saved one after the
        /// other cannot both be handed the same one.
        /// </remarks>
        public string NameFor(string openWorld)
        {
            if (Key(openWorld) != null)
            {
                return openWorld.Trim();
            }

            var fresh = NewWorldName.Next(worlds.Values);
            Add(fresh);
            return fresh;
        }

        static string Key(string name)
        {
            var key = (name ?? string.Empty).Trim();
            return key.Length == 0 ? null : key.ToLowerInvariant();
        }
    }
}
