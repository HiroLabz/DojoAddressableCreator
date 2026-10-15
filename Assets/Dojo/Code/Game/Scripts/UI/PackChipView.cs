using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Game
{
    /// <summary>One pack chip: a coloured marker and the pack's name.</summary>
    /// <remarks>
    /// Exists so <see cref="PackComponent"/> can fill a cloned chip without searching it by child
    /// name or index. A chip whose parts are found by name breaks silently the first time someone
    /// renames a child in the Inspector; serialised references break loudly, in the console, with
    /// the object selected.
    /// </remarks>
    public sealed class PackChipView : MonoBehaviour
    {
        [Tooltip("The diamond marker to the left of the name.")]
        [SerializeField] Image marker;

        [Tooltip("The pack name. Filled in upper case by PackComponent.")]
        [SerializeField] TextMeshProUGUI label;

        /// <summary>Fills this chip in.</summary>
        /// <param name="displayName">Already upper-cased by the caller.</param>
        /// <param name="markerSprite">Which diamond to show.</param>
        public void Bind(string displayName, Sprite markerSprite)
        {
            if (label != null)
            {
                label.text = displayName;
            }

            if (marker != null && markerSprite != null)
            {
                marker.sprite = markerSprite;
            }
        }
    }
}
