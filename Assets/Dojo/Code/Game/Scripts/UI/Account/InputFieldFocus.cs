using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Game.UI.Account
{
    /// <summary>
    /// Gives a text field the kit's focused look while it is being typed in: the brighter frame and
    /// the soft glow around it. Back to idle when focus leaves.
    /// </summary>
    [RequireComponent(typeof(TMP_InputField))]
    public sealed class InputFieldFocus : MonoBehaviour
    {
        [Tooltip("The field's frame. Its sprite is swapped between the two below.")]
        [SerializeField] Image frame;

        [SerializeField] Sprite idle;
        [SerializeField] Sprite focused;

        [Tooltip("The halo behind the frame, shown only while focused.")]
        [SerializeField] Image glow;

        TMP_InputField field;

        void Awake()
        {
            field = GetComponent<TMP_InputField>();
            field.onSelect.AddListener(Focus);
            field.onDeselect.AddListener(Blur);
            Apply(false);
        }

        void OnDestroy()
        {
            if (field != null)
            {
                field.onSelect.RemoveListener(Focus);
                field.onDeselect.RemoveListener(Blur);
            }
        }

        void Focus(string _) => Apply(true);

        void Blur(string _) => Apply(false);

        void Apply(bool on)
        {
            if (frame != null && idle != null && focused != null)
            {
                frame.sprite = on ? focused : idle;
            }

            if (glow != null)
            {
                glow.enabled = on;
            }
        }
    }
}
