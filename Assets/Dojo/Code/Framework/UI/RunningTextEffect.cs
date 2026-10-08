using System.Text;
using TMPro;
using UnityEngine;

namespace Dojo.Framework.UI
{
    /// <summary>
    /// Fills a label with rolling random glyphs behind a fixed prefix, for a Matrix-style readout.
    /// A component of its own so panels can switch the effect on and off without any of them
    /// carrying the animation themselves.
    /// </summary>
    public sealed class RunningTextEffect : MonoBehaviour
    {
        [Tooltip("Label to write into. Defaults to a TMP_Text on this GameObject.")]
        [SerializeField] TMP_Text label;

        [Tooltip("Printed at the start of every line.")]
        [SerializeField] string prefix = "TASK: ";

        [Tooltip("Characters the rolling part is drawn from.")]
        [SerializeField] string glyphs = "#%01";

        [Tooltip("Rolling characters per line.")]
        [SerializeField] int charactersPerLine = 28;

        [Tooltip("How many lines to fill.")]
        [SerializeField] int lines = 6;

        [Tooltip("Seconds between rolls. Smaller is faster.")]
        [SerializeField] float interval = 0.07f;

        [Tooltip("Start rolling as soon as the object is enabled.")]
        [SerializeField] bool playOnEnable;

        readonly StringBuilder builder = new StringBuilder();
        float nextRollAt;

        /// <summary>True while the glyphs are rolling.</summary>
        public bool IsPlaying { get; private set; }

        void Awake()
        {
            if (label == null)
            {
                label = GetComponent<TMP_Text>();
            }
        }

        void OnEnable()
        {
            if (playOnEnable)
            {
                Play();
            }
        }

        /// <summary>Starts rolling, taking over the label until <see cref="Stop"/>.</summary>
        public void Play()
        {
            IsPlaying = true;
            nextRollAt = 0f;   // draw a frame immediately rather than after one interval
            Roll();
        }

        /// <summary>Stops rolling and leaves the label as it is, for a caller to overwrite.</summary>
        public void Stop() => IsPlaying = false;

        void Update()
        {
            if (!IsPlaying || label == null)
            {
                return;
            }

            // Unscaled so the readout keeps moving even if the game is paused behind the panel.
            if (Time.unscaledTime < nextRollAt)
            {
                return;
            }

            nextRollAt = Time.unscaledTime + Mathf.Max(0.01f, interval);
            Roll();
        }

        void Roll()
        {
            if (label == null || string.IsNullOrEmpty(glyphs))
            {
                return;
            }

            // Reused rather than reallocated: this runs many times a second.
            builder.Length = 0;

            for (var line = 0; line < Mathf.Max(1, lines); line++)
            {
                builder.Append(prefix);

                for (var i = 0; i < Mathf.Max(1, charactersPerLine); i++)
                {
                    builder.Append(glyphs[Random.Range(0, glyphs.Length)]);
                }

                if (line < lines - 1)
                {
                    builder.Append('\n');
                }
            }

            label.SetText(builder);
        }
    }
}
