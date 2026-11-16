using System;
using System.Collections.Generic;
using Dojo.Framework.UI;
using Dojo.Game.Managers;
using Dojo.Game.Placement;
using Dojo.Game.Server;
using UnityEngine;

namespace Dojo.Game.UI
{
    /// <summary>
    /// Asks which department an area belongs to, in the Department popup, and gives it the answer
    /// (ClickUp 14ze67xcgzf).
    /// </summary>
    /// <remarks>
    /// One place for the three times it is asked: after a painted area is named, after a room is
    /// placed (once, for every area the room brought), and from an area's Department button.
    /// CANCEL leaves the areas as they were, which for a new area is no department.
    /// </remarks>
    public sealed class AreaDepartmentPrompt
    {
        readonly PopupPrefabs popups;
        readonly BlockAreas areas;
        readonly IAgentRegistry registry;

        public AreaDepartmentPrompt(PopupPrefabs popups, BlockAreas areas, IAgentRegistry registry)
        {
            this.popups = popups;
            this.areas = areas;
            this.registry = registry;
        }

        /// <summary>
        /// Asks for the department of <paramref name="areaIds"/>, called <paramref name="what"/> in the
        /// popup ("'Sales'", "the Sales Room"), starting from the first area's department.
        /// </summary>
        /// <param name="changing">
        /// True from an area's DEPT button: the area was asked before, so no department is the
        /// player's own No department and the box starts ticked. False for a new area or room.
        /// </param>
        public void Ask(string what, IReadOnlyList<int> areaIds, bool changing = false)
        {
            if (areaIds == null || areaIds.Count == 0 || areas == null)
            {
                return;
            }

            var first = areas.Find(areaIds[0]);
            var choice = AreaDepartmentChoice.ForArea(InUse(), first != null ? first.Department : null,
                first != null && first.ConferenceRoom, changing && first != null);

            if (popups == null || popups.Department == null)
            {
                // A scene the popup builder has not been run for: the areas keep no department.
                Debug.LogWarning("[Areas] There is no Department popup to ask with. Run Tools > Dojo > Build Department Popup.");
                return;
            }

            var popup = PopupManager.Instance.Show(popups.Department)
                .Tint(MessagePopup.Tone.Info)
                .Present("Department for " + what,
                    "Which department does it belong to? Tick No department for a room such as the pantry, "
                    + "and Conference room where the owner's meetings are held.")
                .Caption(MessagePopup.Choice.Cancel, "CANCEL")
                .Caption(MessagePopup.Choice.Primary, "SAVE");

            var picker = popup.GetComponent<DepartmentPicker>();
            if (picker != null)
            {
                picker.Bind(choice);
            }

            popup.Chosen += pressed =>
            {
                if (pressed != MessagePopup.Choice.Primary || !choice.CanSave)
                {
                    return;
                }

                foreach (var id in areaIds)
                {
                    areas.SetDepartment(id, choice.Result);
                    areas.SetConferenceRoom(id, choice.ConferenceRoom);
                }
            };
        }

        /// <summary>
        /// Creator only: asks for a department that is not yet on any area - a room's, as it is
        /// saved - and hands the answer to <paramref name="chosen"/> (department, conference room)
        /// instead of setting it. CANCEL calls <paramref name="cancelled"/>. Without the popup the
        /// starting values go straight through, so saving is never blocked by a missing prefab.
        /// </summary>
        public void AskFor(string what, string current, bool conferenceRoom, bool changing,
            Action<string, bool> chosen, Action cancelled = null)
        {
            var choice = AreaDepartmentChoice.ForArea(InUse(), current, conferenceRoom, changing);

            if (popups == null || popups.Department == null)
            {
                Debug.LogWarning("[Areas] There is no Department popup to ask with. Run Tools > Dojo > Build Department Popup.");
                chosen?.Invoke(current ?? string.Empty, conferenceRoom);
                return;
            }

            var popup = PopupManager.Instance.Show(popups.Department)
                .Tint(MessagePopup.Tone.Info)
                .Present("Department for " + what,
                    "Which department does it belong to? Tick No department for a room such as the pantry, "
                    + "and Conference room where the owner's meetings are held.")
                .Caption(MessagePopup.Choice.Cancel, "CANCEL")
                .Caption(MessagePopup.Choice.Primary, "SAVE");

            var picker = popup.GetComponent<DepartmentPicker>();
            if (picker != null)
            {
                picker.Bind(choice);
            }

            popup.Chosen += pressed =>
            {
                if (pressed == MessagePopup.Choice.Primary && choice.CanSave)
                {
                    chosen?.Invoke(choice.Result, choice.ConferenceRoom);
                }
                else
                {
                    cancelled?.Invoke();
                }
            };
        }

        /// <summary>
        /// The departments this world uses: on its areas. (The game also lists the departments the
        /// player gave its agents; the creator's roster has no such choice.)
        /// </summary>
        List<string> InUse() => new List<string>(areas.Departments());
    }
}
