using System;
using System.Collections.Generic;

namespace Dojo.Game.InGame.Agents
{
    /// <summary>
    /// The departments an agent can be put in from the agent card.
    /// </summary>
    /// <remarks>
    /// The fourteen presets are the vision's own (§13 Preset Departments and Custom Departments, in
    /// its order), so a new business starts from the same list the product describes. Anything else
    /// is a custom department, typed by the player; it is stored the same way, as the name itself in
    /// <see cref="AgentCredential.workId"/>.
    /// </remarks>
    public static class Departments
    {
        /// <summary>The vision's preset departments, in its order.</summary>
        public static readonly IReadOnlyList<string> Presets = new[]
        {
            "Reception",
            "Sales",
            "Marketing",
            "Customer Support",
            "Customer Success",
            "Finance",
            "Accounting",
            "HR",
            "Recruitment",
            "Legal",
            "Research",
            "Operations",
            "IT and Security",
            "Product and Development",
        };

        /// <summary>
        /// Where <paramref name="department"/> sits in <see cref="Presets"/>, ignoring case, or -1
        /// when it is a custom one or empty.
        /// </summary>
        public static int IndexOf(string department)
        {
            if (string.IsNullOrWhiteSpace(department))
            {
                return -1;
            }

            var wanted = department.Trim();

            for (var i = 0; i < Presets.Count; i++)
            {
                if (string.Equals(Presets[i], wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// A department as typed, made fit to store: trimmed, and a preset's own spelling when it
        /// matches one, so "sales" and "Sales" are the same department. Empty for blank text.
        /// </summary>
        public static string Normalise(string department)
        {
            if (string.IsNullOrWhiteSpace(department))
            {
                return string.Empty;
            }

            var preset = IndexOf(department);
            return preset >= 0 ? Presets[preset] : department.Trim();
        }
    }
}
