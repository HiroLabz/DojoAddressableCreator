using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Game.UI
{
    /// <summary>
    /// The Chooser popup's own control: a dropdown of the choices, the first picked to start with.
    /// Built onto the popup by <c>ChooserPopupBuilder</c>; filled by <see cref="PopupAsk.Choose"/>.
    /// </summary>
    public sealed class ChoicePicker : MonoBehaviour
    {
        [SerializeField] TMP_Dropdown dropdown;

        [Tooltip("The popup's primary button: answers only while something is picked.")]
        [SerializeField] Button primary;

        readonly List<string> options = new List<string>();

        /// <summary>The choice picked now, in its own words; null while there is none to pick.</summary>
        public string Picked => PopupAsk.PickedOf(dropdown != null ? dropdown.value : -1, options);

        /// <summary>Shows <paramref name="choices"/> in the dropdown, the first picked.</summary>
        public void Bind(IList<string> choices)
        {
            options.Clear();

            if (choices != null)
            {
                options.AddRange(choices);
            }

            if (dropdown != null)
            {
                dropdown.onValueChanged.RemoveListener(OnPicked);
                dropdown.ClearOptions();
                dropdown.AddOptions(new List<string>(options));
                dropdown.SetValueWithoutNotify(0);
                dropdown.RefreshShownValue();
                dropdown.interactable = options.Count > 1;
                dropdown.onValueChanged.AddListener(OnPicked);
            }

            Gate();
        }

        void OnPicked(int _) => Gate();

        void Gate()
        {
            if (primary != null)
            {
                primary.interactable = Picked != null;
            }
        }

        void OnDestroy()
        {
            if (dropdown != null)
            {
                dropdown.onValueChanged.RemoveListener(OnPicked);
            }
        }
    }
}
