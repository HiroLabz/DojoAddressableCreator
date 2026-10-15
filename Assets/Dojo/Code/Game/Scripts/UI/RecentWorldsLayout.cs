using UnityEngine;

namespace Dojo.Game.UI
{
    /// <summary>
    /// The sizes of Recent Worlds, and how tall it is for so many rows.
    /// </summary>
    /// <remarks>
    /// Shared by the Lobby builder, which draws the panel and its row, and by
    /// <see cref="RecentWorldsPanel"/>, which sizes it at run time, so the two cannot disagree.
    /// </remarks>
    public static class RecentWorldsLayout
    {
        /// <summary>From the panel's top to the first row: the title and its rule.</summary>
        public const float Header = 82f;

        /// <summary>Below the last row.</summary>
        public const float Bottom = 24f;

        /// <summary>One world's row, tall enough for a picture the eye can read.</summary>
        public const float RowHeight = 112f;

        /// <summary>Between two rows.</summary>
        public const float Gap = 14f;

        /// <summary>Every row stacked, with the gaps between them.</summary>
        public static float ListHeight(int rows)
            => rows <= 0 ? 0f : rows * RowHeight + (rows - 1) * Gap;

        /// <summary>
        /// The panel's height: tall enough for its rows - or for one, where "No saved worlds yet"
        /// is written - and never taller than <paramref name="limit"/>.
        /// </summary>
        public static float PanelHeight(int rows, float limit)
            => Mathf.Min(Header + Mathf.Max(ListHeight(rows), RowHeight) + Bottom, limit);

        /// <summary>True when the rows are taller than the panel can grow, so the list scrolls.</summary>
        public static bool Scrolls(int rows, float limit)
            => Header + ListHeight(rows) + Bottom > limit;
    }
}
