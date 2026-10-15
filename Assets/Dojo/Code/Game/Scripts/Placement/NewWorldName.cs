using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// The name a world with none is saved under: World1, World2, and so on.
    /// </summary>
    /// <remarks>
    /// The next number after the highest there is, rather than the first gap, so a new world can
    /// never land on a name already in use - and never takes the name of one the player deleted,
    /// which would read as that world coming back.
    /// </remarks>
    public static class NewWorldName
    {
        const string Stem = "World";

        // Case and spacing ignored, the same way the library matches names.
        static readonly Regex Numbered = new Regex(@"^\s*world\s*(\d+)\s*$", RegexOptions.IgnoreCase);

        /// <summary>The next free World# after the highest in <paramref name="names"/>.</summary>
        public static string Next(IEnumerable<string> names)
        {
            var highest = 0;

            if (names != null)
            {
                foreach (var name in names)
                {
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    var match = Numbered.Match(name);
                    int number;

                    if (match.Success && int.TryParse(match.Groups[1].Value, out number) && number > highest)
                    {
                        highest = number;
                    }
                }
            }

            return Stem + (highest + 1);
        }
    }
}
