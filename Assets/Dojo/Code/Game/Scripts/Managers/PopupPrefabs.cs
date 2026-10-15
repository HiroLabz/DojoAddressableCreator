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
    }
}
