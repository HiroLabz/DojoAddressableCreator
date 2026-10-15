using UnityEngine;

namespace Dojo.Game.InGame.Effects
{
    /// <summary>
    /// Lights everything drawn under this object up at once, then fades it back to exactly how it
    /// was. What a clicked agent does to show it was the one clicked.
    /// </summary>
    /// <remarks>
    /// Done with property blocks, never by changing a material. Every agent wears the same
    /// material asset, so lighting the material would light all of them, and a copy per agent
    /// would outlive the glow and have to be cleaned up. A property block sits on this one
    /// renderer, and removing it at the end leaves no trace at all.
    /// <para>
    /// Emission where the material has it switched on, because that is a true glow: the texture
    /// stays visible under it. A material with emission off cannot be made to emit from outside,
    /// so its base colour is brightened towards the glow colour instead.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class RendererGlow : MonoBehaviour
    {
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        const string EmissionKeyword = "_EMISSION";

        [Tooltip("How long it takes to reach full brightness.")]
        [SerializeField] float riseSeconds = 0.1f;

        [Tooltip("How long it takes to fade back to the original colours.")]
        [SerializeField] float fadeSeconds = 0.6f;

        [Tooltip("Strength of the glow at its peak. Below 1 so the texture still shows through.")]
        [SerializeField] float peakIntensity = 0.9f;

        Color color = Color.white;
        Renderer[] renderers;
        MaterialPropertyBlock block;
        float elapsed;

        /// <summary>How long it takes to reach full brightness.</summary>
        public float RiseSeconds => riseSeconds;

        /// <summary>How long it takes to fade back.</summary>
        public float FadeSeconds => fadeSeconds;

        /// <summary>True from the click until the colours are back to normal.</summary>
        public bool IsGlowing { get; private set; }

        /// <summary>
        /// Lights <paramref name="target"/> up in <paramref name="glow"/>, or starts it over if it is
        /// already glowing.
        /// </summary>
        /// <remarks>
        /// Started over rather than stacked: two clicks in a row mean the same thing as one, and a
        /// second glow on top of a fading first would restore colours the first had changed.
        /// </remarks>
        public static RendererGlow Pulse(GameObject target, Color glow)
        {
            var effect = target.GetComponent<RendererGlow>();

            if (effect == null)
            {
                effect = target.AddComponent<RendererGlow>();
            }

            effect.Restart(glow);
            return effect;
        }

        void Restart(Color glow)
        {
            color = glow;

            // Found again on every click rather than cached, in case the agent was rebuilt since.
            renderers = GetComponentsInChildren<Renderer>(true);
            elapsed = 0f;
            IsGlowing = true;
            enabled = true;

            Apply(Strength(0f));
        }

        void Update()
        {
            Advance(Time.deltaTime);
        }

        /// <summary>Moves the glow on by <paramref name="seconds"/>. Called by Update, and by tests.</summary>
        public void Advance(float seconds)
        {
            if (!IsGlowing)
            {
                return;
            }

            elapsed += seconds;

            if (elapsed >= riseSeconds + fadeSeconds)
            {
                Finish();
                enabled = false;
                return;
            }

            Apply(Strength(elapsed));
        }

        void OnDisable()
        {
            // Switched off mid-glow - the agent was put away, say - must not leave it stuck lit.
            if (IsGlowing)
            {
                Finish();
            }
        }

        /// <summary>0 to 1: a straight rise, then an eased fall back to nothing.</summary>
        float Strength(float t)
        {
            if (t < riseSeconds)
            {
                return riseSeconds > 0f ? t / riseSeconds : 1f;
            }

            var fade = fadeSeconds > 0f ? (t - riseSeconds) / fadeSeconds : 1f;
            return 1f - Mathf.SmoothStep(0f, 1f, fade);
        }

        void Apply(float strength)
        {
            if (block == null)
            {
                block = new MaterialPropertyBlock();
            }

            foreach (var renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                var materials = renderer.sharedMaterials;

                // One block per material slot, because each slot started from its own colour.
                for (var i = 0; i < materials.Length; i++)
                {
                    var material = materials[i];

                    if (material == null)
                    {
                        continue;
                    }

                    block.Clear();

                    if (material.IsKeywordEnabled(EmissionKeyword) && material.HasProperty(EmissionColor))
                    {
                        var original = material.GetColor(EmissionColor);
                        block.SetColor(EmissionColor, Color.Lerp(original, color * peakIntensity, strength));
                    }
                    else if (material.HasProperty(BaseColor))
                    {
                        var original = material.GetColor(BaseColor);
                        block.SetColor(BaseColor, Color.Lerp(original, color, strength * peakIntensity));
                    }
                    else
                    {
                        continue;   // a text mesh or a particle: nothing here to light
                    }

                    renderer.SetPropertyBlock(block, i);
                }
            }
        }

        /// <summary>Takes every override off, which puts every colour back as the material has it.</summary>
        void Finish()
        {
            IsGlowing = false;

            if (renderers == null)
            {
                return;
            }

            foreach (var renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                var slots = renderer.sharedMaterials.Length;

                for (var i = 0; i < slots; i++)
                {
                    renderer.SetPropertyBlock(null, i);
                }

                renderer.SetPropertyBlock(null);
            }
        }
    }
}
