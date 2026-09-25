using UnityEngine;

namespace Dojo.Game.Components
{
    /// <summary>
    /// Lights a monitor by swapping its material. Watches one <see cref="Chair"/> and switches on
    /// while somebody is actually sitting in it — claiming a chair on the way over is not enough.
    /// </summary>
    public sealed class MonitorScreen : MonoBehaviour
    {
        [Tooltip("Renderer to swap the material on. Defaults to the one on this GameObject.")]
        [SerializeField] Renderer screen;

        [Tooltip("Material shown while nobody is at the desk.")]
        [SerializeField] Material offMaterial;

        [Tooltip("Material shown while somebody is sitting at the desk.")]
        [SerializeField] Material onMaterial;

        [Tooltip("Which material slot is the screen face. The monitor mesh puts the case on slot 0 " +
                 "and the flat screen quad on slot 1, so lighting slot 0 would light the whole case.")]
        [SerializeField] int materialIndex = 1;

        [Tooltip("The desk's chair. The monitor lights up while this one is in use.")]
        [SerializeField] Chair chair;

        /// <summary>True while the screen is lit.</summary>
        public bool IsOn { get; private set; }

        /// <summary>The chair this monitor answers to.</summary>
        public Chair Chair => chair;

        void Awake()
        {
            if (screen == null)
            {
                screen = GetComponent<Renderer>();
            }

            Apply(false);
        }

        void OnEnable()
        {
            if (chair == null)
            {
                return;
            }

            chair.InUseChanged += OnChairInUseChanged;

            // Catch up: the chair may already be occupied when this is enabled.
            SetOn(chair.IsInUse);
        }

        void OnDisable()
        {
            if (chair != null)
            {
                chair.InUseChanged -= OnChairInUseChanged;
            }
        }

        void OnChairInUseChanged(Chair changed) => SetOn(changed.IsInUse);

        public void TurnOn() => SetOn(true);

        public void TurnOff() => SetOn(false);

        public void SetOn(bool on)
        {
            IsOn = on;
            Apply(on);
        }

        void Apply(bool on)
        {
            var material = on ? onMaterial : offMaterial;
            if (screen == null || material == null)
            {
                return;
            }

            // sharedMaterials, not material: assigning the latter would clone the asset for every
            // one of the 25 monitors and leak the copies. The getter hands back a copy of the
            // array, so it has to be written back after editing.
            var slots = screen.sharedMaterials;
            if (materialIndex < 0 || materialIndex >= slots.Length || slots[materialIndex] == material)
            {
                return;
            }

            slots[materialIndex] = material;
            screen.sharedMaterials = slots;
        }

        /// <summary>Points this monitor at a chair. Used by editor wiring.</summary>
        public void SetChair(Chair value) => chair = value;
    }
}
