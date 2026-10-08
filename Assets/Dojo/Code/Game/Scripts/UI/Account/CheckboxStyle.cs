using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Game.UI.Account
{
    /// <summary>
    /// A check box in the kit's look: a signal-blue box with a white tick when on, a dark box with
    /// a faint edge when off. The <see cref="Toggle"/> itself shows and hides the tick.
    /// </summary>
    [RequireComponent(typeof(Toggle))]
    public sealed class CheckboxStyle : MonoBehaviour
    {
        [SerializeField] Image box;
        [SerializeField] Image edge;
        [SerializeField] Color on = new Color(0.184f, 0.561f, 1f, 1f);
        [SerializeField] Color off = new Color(0.047f, 0.071f, 0.118f, 1f);

        Toggle toggle;

        void Awake()
        {
            toggle = GetComponent<Toggle>();
            toggle.onValueChanged.AddListener(Apply);
            Apply(toggle.isOn);
        }

        void OnDestroy()
        {
            if (toggle != null)
            {
                toggle.onValueChanged.RemoveListener(Apply);
            }
        }

        void Apply(bool isOn)
        {
            if (box != null)
            {
                box.color = isOn ? on : off;
            }

            if (edge != null)
            {
                // The edge only matters on the dark box; on the blue one it would be a second outline.
                edge.enabled = !isOn;
            }
        }
    }
}
