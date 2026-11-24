using UnityEngine;

namespace Dojo.Game.InGame.Terminals
{
    /// <summary>
    /// Makes a terminal's emissive parts breathe: the blue panels of the MVP kit's server rack glow
    /// brighter and dimmer, slowly, so it reads as alive - the bridge to Niru (Jacob, 19 Nov). The rack
    /// has no screen to change face like the supercomputers did, so this is its sign of life for now.
    /// </summary>
    /// <remarks>
    /// Only the emission colour, by property block on this model's own renderers: every piece of the
    /// kit wears the same atlas material, and only the parts its emission map marks (the blue) emit,
    /// so nothing else on the rack, and nothing else in the world, changes. Each rack keeps its own
    /// beat, offset by where it stands, so a row of racks doesn't blink in step.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class TerminalPulse : MonoBehaviour
    {
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [Tooltip("Seconds from bright to dim and back.")]
        [SerializeField] float period = 2.4f;

        [Tooltip("Emission strength at its dimmest; 1 is the material's own.")]
        [SerializeField] float low = 0.35f;

        [Tooltip("Emission strength at its brightest; above 1 glows past the material's own.")]
        [SerializeField] float high = 1.8f;

        Renderer[] parts;
        Color[] baseColors;
        MaterialPropertyBlock block;
        float phase;

        /// <summary>How strong the glow is at <paramref name="seconds"/> into a beat of <paramref name="period"/>, from <paramref name="low"/> to <paramref name="high"/>.</summary>
        public static float Strength(float seconds, float period, float low, float high)
        {
            if (period <= 0f)
            {
                return high;
            }

            var wave = 0.5f - 0.5f * Mathf.Cos(seconds / period * Mathf.PI * 2f);
            return Mathf.Lerp(low, high, wave);
        }

        void OnEnable()
        {
            parts = GetComponentsInChildren<Renderer>(true);
            baseColors = new Color[parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                var material = parts[i] != null ? parts[i].sharedMaterial : null;
                baseColors[i] = material != null && material.HasProperty(EmissionColorId) ? material.GetColor(EmissionColorId) : Color.black;
            }

            var at = transform.position;
            phase = Mathf.Repeat(at.x * 0.37f + at.z * 0.61f, 1f) * period;
        }

        // On the physics tick, not every rendered frame: 3D objects run on FixedUpdate (Bob, 19 Nov,
        // ClickUp 14ze67xfu9w), the click glows included. Time.time keeps the beat the same length.
        void FixedUpdate()
        {
            if (parts == null)
            {
                return;
            }

            block ??= new MaterialPropertyBlock();
            var strength = Strength(Time.time + phase, period, low, high);

            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (part == null || baseColors[i].maxColorComponent <= 0f)
                {
                    continue;
                }

                part.GetPropertyBlock(block, 0);
                block.SetColor(EmissionColorId, baseColors[i] * strength);
                part.SetPropertyBlock(block, 0);
            }
        }

        void OnDisable()
        {
            if (parts == null)
            {
                return;
            }

            // Back to the material's own glow.
            for (var i = 0; i < parts.Length; i++)
            {
                if (parts[i] != null && baseColors[i].maxColorComponent > 0f)
                {
                    parts[i].GetPropertyBlock(block ??= new MaterialPropertyBlock(), 0);
                    block.SetColor(EmissionColorId, baseColors[i]);
                    parts[i].SetPropertyBlock(block, 0);
                }
            }
        }
    }
}
