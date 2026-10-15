using UnityEngine;

namespace Dojo.Game.UI
{
    /// <summary>
    /// The marker standing at one block area: a pin over a lit pad on the floor, at the spot the
    /// owner stops at when he is sent there.
    /// </summary>
    /// <remarks>
    /// The pin is the animated one from the content pack (<see cref="UseModel"/>) - its orb spins
    /// and glows by itself - with the red pin model as a stand-in until the content is there. The
    /// pin bobs and turns to face the camera, since it is flat; the pad's outer ring pulses.
    /// Nothing here has a collider, so clicks and drags go straight through to the floor.
    /// <para>
    /// Made and placed by <see cref="AreaLocators"/>, which also draws its label.
    /// </para>
    /// </remarks>
    public sealed class AreaLocator : MonoBehaviour
    {
        [Tooltip("The pin model, which bobs and faces the camera.")]
        [SerializeField] Transform pin;

        [Tooltip("Where the label's line meets the pin: the middle of its head.")]
        [SerializeField] Transform head;

        [Tooltip("The rings lit on the floor. The first one pulses.")]
        [SerializeField] LineRenderer[] pad;

        [SerializeField] float bobHeight = 0.06f;
        [SerializeField] float bobSeconds = 1.8f;
        [SerializeField] float pulseSeconds = 1.6f;

        Vector3 pinRest;
        Color[] padStart;
        Color[] padEnd;
        Transform view;

        /// <summary>Where the label's line meets the pin, in the world.</summary>
        public Vector3 Head => head != null ? head.position : transform.position;

        /// <summary>Whether the pin is the content's model rather than the stand-in.</summary>
        public bool HasModel { get; private set; }

        /// <summary>
        /// Puts <paramref name="model"/> on the pin in place of the stand-in, keeping the bob and
        /// the turn to face the camera, which are the pin's own.
        /// </summary>
        /// <remarks>
        /// The model is a pack item, so it has colliders for being placed and dragged. A locator
        /// must never take a click, so they are taken off, and it all goes on the Ignore Raycast
        /// layer like the rest of the locator.
        /// </remarks>
        public void UseModel(GameObject model)
        {
            if (model == null || pin == null || HasModel)
            {
                return;
            }

            foreach (var standIn in pin.GetComponents<Renderer>())
            {
                standIn.enabled = false;
            }

            var copy = Instantiate(model, pin, false);
            copy.name = model.name;
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;

            var layer = gameObject.layer;

            foreach (var part in copy.GetComponentsInChildren<Transform>(true))
            {
                part.gameObject.layer = layer;
            }

            foreach (var collider in copy.GetComponentsInChildren<Collider>(true))
            {
                Destroy(collider);
            }

            HasModel = true;
        }

        void Awake()
        {
            if (pin != null)
            {
                pinRest = pin.localPosition;
            }

            if (pad != null)
            {
                padStart = new Color[pad.Length];
                padEnd = new Color[pad.Length];

                for (var i = 0; i < pad.Length; i++)
                {
                    if (pad[i] != null)
                    {
                        padStart[i] = pad[i].startColor;
                        padEnd[i] = pad[i].endColor;
                    }
                }
            }
        }

        void Update()
        {
            var time = Time.unscaledTime;

            if (pin != null)
            {
                pin.localPosition = pinRest + Vector3.up * (Mathf.Sin(time * Mathf.PI * 2f / bobSeconds) * bobHeight);

                // Flat, so it is turned to face the camera, about the upright only.
                if (view == null && Camera.main != null)
                {
                    view = Camera.main.transform;
                }

                if (view != null)
                {
                    var towards = view.position - pin.position;
                    towards.y = 0f;

                    if (towards.sqrMagnitude > 0.0001f)
                    {
                        pin.rotation = Quaternion.LookRotation(-towards.normalized, Vector3.up);
                    }
                }
            }

            if (pad != null && pad.Length > 0 && pad[0] != null)
            {
                var glow = 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin(time * Mathf.PI * 2f / pulseSeconds));
                pad[0].startColor = Faded(padStart[0], glow);
                pad[0].endColor = Faded(padEnd[0], glow);
            }
        }

        static Color Faded(Color colour, float share) => new Color(colour.r, colour.g, colour.b, colour.a * share);
    }
}
