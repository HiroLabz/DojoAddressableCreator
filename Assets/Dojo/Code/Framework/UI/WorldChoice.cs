namespace Dojo.Framework.UI
{
    /// <summary>
    /// One saved world as the load dialog needs to show it.
    /// </summary>
    /// <remarks>
    /// A presentation model rather than the world itself: the dialog shows counts and a save time
    /// and never touches pieces or agents, so handing it a <c>WorldSnapshot</c> would mean loading
    /// every world in the list just to draw the list.
    /// <para>
    /// <see cref="SavedAt"/> is already formatted. Deciding that "today 14:02" beats a timestamp is
    /// the caller's business, and it is the caller that knows what "today" means.
    /// </para>
    /// </remarks>
    public readonly struct WorldChoice
    {
        /// <summary>The world's name, and the value handed back when it is picked.</summary>
        public readonly string Name;

        /// <summary>How many pieces are placed in it.</summary>
        public readonly int Pieces;

        /// <summary>How many agents live in it.</summary>
        public readonly int Agents;

        /// <summary>When it was saved, already formatted, or empty to leave it off the row.</summary>
        public readonly string SavedAt;

        /// <summary>Whether this is the automatic save rather than one the player named.</summary>
        public readonly bool IsAutosave;

        public WorldChoice(string name, int pieces, int agents, string savedAt = null, bool isAutosave = false)
        {
            Name = name ?? string.Empty;
            Pieces = pieces;
            Agents = agents;
            SavedAt = savedAt ?? string.Empty;
            IsAutosave = isAutosave;
        }

        /// <summary>
        /// The line under the name: counts, and the save time when there is one.
        /// </summary>
        public string Summary
        {
            get
            {
                string text = Pieces + " pieces  ·  " + Agents + " agents";
                return string.IsNullOrEmpty(SavedAt) ? text : text + "  ·  " + SavedAt;
            }
        }
    }
}
