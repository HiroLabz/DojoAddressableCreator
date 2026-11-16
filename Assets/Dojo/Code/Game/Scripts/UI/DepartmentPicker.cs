using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Game.UI
{
    /// <summary>
    /// The Department popup's own controls: the dropdown, the box for a new department's name, the
    /// No department checkbox (ClickUp 14ze67xcgzf), and the Conference room checkbox (meetings).
    /// </summary>
    /// <remarks>
    /// Sits beside the <c>MessagePopup</c> on <c>DepartmentPopup.prefab</c>, which keeps the title,
    /// the tone and the buttons. This shows an <see cref="AreaDepartmentChoice"/> and keeps it in
    /// step with what the player does, and SAVE on only while the choice can be saved. Built by
    /// <c>Tools ▸ Dojo ▸ Build Department Popup</c>.
    /// </remarks>
    public sealed class DepartmentPicker : MonoBehaviour
    {
        [SerializeField] TMP_Dropdown dropdown;

        [Tooltip("The row with the box for a new department's name; shown only while creating one.")]
        [SerializeField] GameObject newNameRow;

        [SerializeField] TMP_InputField newName;

        [Tooltip("This area belongs to no department: a pantry, a bathroom.")]
        [SerializeField] Toggle noDepartment;

        [Tooltip("This area is a conference room, where the owner's meetings are held.")]
        [SerializeField] Toggle conferenceRoom;

        [Tooltip("The popup's SAVE button, on only while the choice can be saved.")]
        [SerializeField] Button save;

        AreaDepartmentChoice choice;
        bool wired;

        /// <summary>What the player has chosen so far.</summary>
        public AreaDepartmentChoice Choice => choice;

        /// <summary>Shows <paramref name="shown"/> and follows the player's changes to it.</summary>
        public void Bind(AreaDepartmentChoice shown)
        {
            choice = shown;
            Wire();

            if (dropdown != null)
            {
                dropdown.ClearOptions();
                dropdown.AddOptions(new List<string>(choice.Options));
                dropdown.SetValueWithoutNotify(choice.Picked);
                dropdown.RefreshShownValue();
            }

            if (newName != null)
            {
                newName.SetTextWithoutNotify(choice.NewName ?? string.Empty);
            }

            if (noDepartment != null)
            {
                noDepartment.SetIsOnWithoutNotify(choice.NoDepartment);
            }

            if (conferenceRoom != null)
            {
                conferenceRoom.SetIsOnWithoutNotify(choice.ConferenceRoom);
            }

            Refresh();
        }

        void Wire()
        {
            if (wired)
            {
                return;
            }

            wired = true;

            if (dropdown != null)
            {
                dropdown.onValueChanged.AddListener(value =>
                {
                    choice.Picked = value;
                    Refresh();

                    if (choice.CreatingNew && newName != null)
                    {
                        newName.ActivateInputField();
                    }
                });
            }

            if (newName != null)
            {
                newName.onValueChanged.AddListener(text =>
                {
                    choice.NewName = text;
                    Refresh();
                });
            }

            if (noDepartment != null)
            {
                noDepartment.onValueChanged.AddListener(on =>
                {
                    choice.NoDepartment = on;
                    Refresh();
                });
            }

            if (conferenceRoom != null)
            {
                conferenceRoom.onValueChanged.AddListener(on =>
                {
                    choice.ConferenceRoom = on;
                    Refresh();
                });
            }
        }

        void Refresh()
        {
            if (choice == null)
            {
                return;
            }

            if (dropdown != null)
            {
                dropdown.interactable = choice.CanPick;
            }

            if (newNameRow != null)
            {
                newNameRow.SetActive(choice.CreatingNew);
            }

            if (save != null)
            {
                save.interactable = choice.CanSave;
            }
        }

        void OnDestroy()
        {
            if (dropdown != null) dropdown.onValueChanged.RemoveAllListeners();
            if (newName != null) newName.onValueChanged.RemoveAllListeners();
            if (noDepartment != null) noDepartment.onValueChanged.RemoveAllListeners();
            if (conferenceRoom != null) conferenceRoom.onValueChanged.RemoveAllListeners();
        }
    }
}
