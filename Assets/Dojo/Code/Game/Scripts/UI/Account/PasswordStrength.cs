using System.Collections.Generic;

namespace Dojo.Game.UI.Account
{
    /// <summary>
    /// How strong a password is, as the Create account card shows it: up to four lit segments and
    /// a line such as "Strong — 15 characters, mixed case and a number".
    /// </summary>
    /// <remarks>
    /// A guide for the player, not a rule the server enforces - whoever creates the account decides
    /// what it accepts. Under eight characters is too short whatever it contains; past that, each of
    /// mixed case, a number and a symbol lights one more segment.
    /// </remarks>
    public static class PasswordStrength
    {
        /// <summary>Shorter than this is too short, whatever else it has.</summary>
        public const int MinimumLength = 8;

        /// <summary>How many segments the bar has.</summary>
        public const int Segments = 4;

        static readonly string[] Names = { string.Empty, "Weak", "Fair", "Strong", "Very strong" };

        /// <summary>What the bar shows.</summary>
        public readonly struct Rating
        {
            /// <summary>0 for nothing typed, then 1 to <see cref="Segments"/>.</summary>
            public readonly int Score;

            /// <summary>The line under the bar, or empty for nothing typed.</summary>
            public readonly string Description;

            public Rating(int score, string description)
            {
                Score = score;
                Description = description;
            }
        }

        public static Rating Rate(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                return new Rating(0, string.Empty);
            }

            if (password.Length < MinimumLength)
            {
                return new Rating(1, "Too short — at least " + MinimumLength + " characters");
            }

            bool upper = false, lower = false, digit = false, symbol = false;

            foreach (var c in password)
            {
                if (char.IsUpper(c)) upper = true;
                else if (char.IsLower(c)) lower = true;
                else if (char.IsDigit(c)) digit = true;
                else symbol = true;
            }

            var traits = new List<string>();
            if (upper && lower) traits.Add("mixed case");
            if (digit) traits.Add("a number");
            if (symbol) traits.Add("a symbol");

            var score = 1 + traits.Count;
            var length = password.Length + " characters";

            if (traits.Count == 0)
            {
                return new Rating(score, Names[score] + " — " + length + ", add capitals, numbers or symbols");
            }

            traits.Insert(0, length);
            return new Rating(score, Names[score] + " — " + JoinAsList(traits));
        }

        /// <summary>"a, b and c".</summary>
        static string JoinAsList(List<string> parts)
        {
            if (parts.Count == 1)
            {
                return parts[0];
            }

            return string.Join(", ", parts.GetRange(0, parts.Count - 1)) + " and " + parts[parts.Count - 1];
        }
    }
}
