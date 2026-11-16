using System;
using System.Collections.Generic;
using Dojo.Game.InGame.Agents;

namespace Dojo.Game.UI
{
    /// <summary>
    /// What the Department popup offers for an area, and when it can be saved (ClickUp 14ze67xcgzf).
    /// </summary>
    /// <remarks>
    /// The dropdown lists the 14 presets in the vision's order, then the departments this world
    /// already uses that are not presets, A to Z, then <see cref="CreateNew"/>. Picking that shows a
    /// box for the new name. <see cref="NoDepartment"/> is for rooms such as the pantry: ticked, it
    /// turns the dropdown off and saves the area with none.
    /// <para>
    /// Kept apart from the popup itself so the rules can be checked without a scene.
    /// </para>
    /// </remarks>
    public sealed class AreaDepartmentChoice
    {
        /// <summary>The last option: type a department of your own.</summary>
        public const string CreateNew = "Create a new department…";

        readonly List<string> options = new List<string>();

        /// <param name="inUse">The departments this world already uses, on its areas or agents.</param>
        /// <param name="current">The area's department when it is being changed; null for a new area.</param>
        /// <param name="conferenceRoom">Whether the area is a conference room already.</param>
        public AreaDepartmentChoice(IEnumerable<string> inUse, string current, bool conferenceRoom = false)
        {
            ConferenceRoom = conferenceRoom;

            options.AddRange(Departments.Presets);

            var own = new List<string>();
            var wanted = new List<string>(inUse ?? new string[0]) { current };

            foreach (var department in wanted)
            {
                var tidy = Departments.Normalise(department);
                if (tidy.Length == 0 || Departments.IndexOf(tidy) >= 0 || own.Exists(d => Same(d, tidy)))
                {
                    continue;
                }

                own.Add(tidy);
            }

            own.Sort(StringComparer.OrdinalIgnoreCase);
            options.AddRange(own);
            options.Add(CreateNew);

            var start = Departments.Normalise(current);
            Picked = start.Length > 0 ? options.FindIndex(o => Same(o, start)) : -1;
        }

        /// <summary>
        /// The choice for one area: a new one starts with nothing picked; one being changed starts
        /// from its department, or with No department ticked when it has none.
        /// </summary>
        /// <remarks>
        /// "None" and "never asked" are both an empty department, so only the caller knows which
        /// it is. An area changed from its row's DEPT button has been asked before - when it was
        /// painted or its room placed - so empty there is the No department the player saved, and
        /// the box shows it ticked rather than looking as though it was never saved.
        /// </remarks>
        public static AreaDepartmentChoice ForArea(IEnumerable<string> inUse, string department, bool conferenceRoom,
            bool changing)
        {
            var choice = new AreaDepartmentChoice(inUse, department, conferenceRoom);

            if (changing && Departments.Normalise(department).Length == 0)
            {
                choice.NoDepartment = true;
            }

            return choice;
        }

        /// <summary>The dropdown's lines, top to bottom.</summary>
        public IReadOnlyList<string> Options => options;

        /// <summary>Which line is picked: -1 for none yet.</summary>
        public int Picked { get; set; }

        /// <summary>The name typed for a new department.</summary>
        public string NewName { get; set; }

        /// <summary>Ticked: the area belongs to no department.</summary>
        public bool NoDepartment { get; set; }

        /// <summary>
        /// Ticked: the area is a conference room, where the owner's meetings are held. Apart from the
        /// department - a conference room may belong to one or to none - so it never turns SAVE on
        /// by itself.
        /// </summary>
        public bool ConferenceRoom { get; set; }

        /// <summary>The last line is picked, so the new-name box is shown.</summary>
        public bool CreatingNew => !NoDepartment && Picked == options.Count - 1;

        /// <summary>The dropdown is on: off while No department is ticked.</summary>
        public bool CanPick => !NoDepartment;

        /// <summary>A department is picked, a new one is named, or No department is ticked.</summary>
        public bool CanSave
        {
            get
            {
                if (NoDepartment) return true;
                if (CreatingNew) return !string.IsNullOrWhiteSpace(NewName);
                return Picked >= 0 && Picked < options.Count - 1;
            }
        }

        /// <summary>What the area gets: the department, empty for none, null while nothing can be saved.</summary>
        public string Result
        {
            get
            {
                if (!CanSave) return null;
                if (NoDepartment) return string.Empty;
                return CreatingNew ? Departments.Normalise(NewName) : options[Picked];
            }
        }

        static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
