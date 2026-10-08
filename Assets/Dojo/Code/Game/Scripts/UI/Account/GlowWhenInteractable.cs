using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Game.UI.Account
{
    /// <summary>
    /// Shows a button's glow only while the button can be pressed, so a greyed-out CREATE ACCOUNT
    /// does not still shine as if it were waiting to be clicked.
    /// </summary>
    public sealed class GlowWhenInteractable : MonoBehaviour
    {
        [SerializeField] Selectable button;
        [SerializeField] Graphic glow;

        void LateUpdate()
        {
            if (button != null && glow != null)
            {
                glow.enabled = button.IsInteractable();
            }
        }
    }
}
