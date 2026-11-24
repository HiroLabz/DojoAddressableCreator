using System;
using Dojo.Framework.UI;
using UnityEngine;

namespace Dojo.Game.Managers
{
    /// <summary>
    /// The four popup prefabs from <c>Prefabs/UI/Popups</c>, registered with the container so code
    /// that is not a scene object can show one.
    /// </summary>
    /// <remarks>
    /// The popup counterpart of <see cref="DialogPrefabs"/>. Show one with
    /// <c>PopupManager.Instance.Show(prefabs.Confirmation).Present(...)</c>. Each prefab bakes in its
    /// tone and its buttons - see <see cref="MessagePopup"/> for which is which.
    /// </remarks>
    [Serializable]
    public sealed class PopupPrefabs
    {
        [Tooltip("Red, no buttons: something is wrong and nothing can be clicked.")]
        [SerializeField] MessagePopup notice;

        [Tooltip("Green, one button: something went right.")]
        [SerializeField] MessagePopup affirmation;

        [Tooltip("Blue, Cancel and the primary: a question with one way forward.")]
        [SerializeField] MessagePopup confirmation;

        [Tooltip("Amber, Cancel, a destructive middle and the primary.")]
        [SerializeField] MessagePopup threeWay;

        [Tooltip("Blue, a box to type a name in, Cancel and the primary: asks for a name.")]
        [SerializeField] MessagePopup prompt;

        public MessagePopup Notice => notice;

        public MessagePopup Affirmation => affirmation;

        public MessagePopup Confirmation => confirmation;

        public MessagePopup ThreeWay => threeWay;

        /// <summary>Asks for a name: read it back from <see cref="MessagePopup.Answer"/>.</summary>
        public MessagePopup Prompt => prompt;

        [Tooltip("Blue, a dropdown of the choices, Cancel and the primary: asks for one of a list " +
                 "(Tools > Dojo > Build Chooser Popup).")]
        [SerializeField] MessagePopup chooser;

        /// <summary>Asks for one of a list: see <c>ChoicePicker</c> on it.</summary>
        public MessagePopup Chooser => chooser;

        [Tooltip("Blue, a department dropdown, a box for a new one and a No department checkbox: " +
                 "asks which department an area belongs to. Built by Tools > Dojo > Build Department Popup.")]
        [SerializeField] MessagePopup department;

        /// <summary>Asks for an area's department: see <c>DepartmentPicker</c> on it.</summary>
        public MessagePopup Department => department;

        [Tooltip("Blue, company name, industry, goals and world name, Cancel and START: the " +
                 "Lobby's first START asks for the player's business with it.")]
        [SerializeField] MessagePopup business;

        /// <summary>Asks for the player's business: see <c>BusinessPicker</c> on it.</summary>
        public MessagePopup Business => business;

        [Tooltip("Blue, a row per active agent with an area and a department, SKIP and START: " +
                 "puts the player's agents in the world before the first-time tour.")]
        [SerializeField] MessagePopup agentAssignment;

        /// <summary>Places the agents on first run: see <c>AgentAssignmentPicker</c> on it.</summary>
        public MessagePopup AgentAssignment => agentAssignment;
    }
}
