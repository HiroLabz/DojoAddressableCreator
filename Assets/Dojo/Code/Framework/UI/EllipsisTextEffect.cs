using System.Text;
using TMPro;
using UnityEngine;

namespace Dojo.Framework.UI
{
    /// <summary>
    /// Cycles a trailing ellipsis on a label — "Thinking", "Thinking.", "Thinking..",
    /// "Thinking..." — so a wait reads as something still happening rather than something stuck.
    /// </summary>
    /// <remarks>
    /// A component of its own, the same bargain <see cref="RunningTextEffect"/> makes: a panel
    /// switches it on and off and carries no animation of its own.
    /// <para>
    /// The base text is whatever it is asked to show, with any dots it arrived with taken off —
    /// see <see cref="Play(string)"/>. Callers name an activity ("Thinking…", "Searching docs…")
    /// and this owns the punctuation, so a caller cannot end up with "Thinking……".
    /// </para>
    /// </remarks>
    public sealed class EllipsisTextEffect : MonoBehaviour
    {
        [Tooltip("Label to write into. Defaults to a TMP_Text on this GameObject.")]
        [SerializeField] TMP_Text label;

        [Tooltip("Shown before the dots when nothing else has been supplied.")]
        [SerializeField] string baseText = "Thinking";

        [Tooltip("How many dots the cycle grows to before starting over.")]
        [SerializeField] int maxDots = 3;

        [Tooltip("Seconds each step is held. Smaller is faster.")]
        [SerializeField] float interval = 0.35f;

        [Tooltip("Start cycling as soon as the object is enabled. The busy block is switched on " +
                 "and off rather than re-created, so this is how it starts.")]
        [SerializeField] bool playOnEnable = true;

        /// <summary>
        /// Trailing characters stripped from a caller's text before the dots are added.
        /// </summary>
        /// <remarks>
        /// The ellipsis character is in here as well as the full stop because the call sites were
        /// written with one — "Thinking…" — and animating that would grow a second ellipsis onto
        /// the end of the first.
        /// </remarks>
        static readonly char[] Trailing = { '.', '…', ' ' };

        readonly StringBuilder builder = new StringBuilder();

        int dots;
        float nextStepAt;

        /// <summary>True while the dots are cycling.</summary>
        public bool IsPlaying { get; private set; }

        /// <summary>What is drawn before the dots.</summary>
        public string BaseText => baseText;

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

        void OnDisable()
        {
            // Stopped rather than left running: the block is hidden by switching the object off,
            // and a cycle that kept its place would come back mid-ellipsis on the next wait.
            IsPlaying = false;
        }

        /// <summary>Starts cycling, taking over the label until <see cref="Stop"/>.</summary>
        public void Play()
        {
            IsPlaying = true;
            dots = 0;
            nextStepAt = Time.unscaledTime + Mathf.Max(0.01f, interval);
            Draw();
        }

        /// <summary>
        /// Starts cycling under a new caption, with any dots it arrived with taken off.
        /// </summary>
        /// <param name="text">
        /// What to show before the dots. Ignored when empty, so a caller that has nothing
        /// particular to say leaves the authored caption alone rather than blanking the label.
        /// </param>
        public void Play(string text)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                baseText = text.TrimEnd(Trailing);
            }

            Play();
        }

        /// <summary>Stops cycling and leaves the label as it is, for a caller to overwrite.</summary>
        public void Stop() => IsPlaying = false;

        void Update()
        {
            if (!IsPlaying || label == null)
            {
                return;
            }

            // Unscaled, so the wait keeps moving even with the game paused behind the window.
            if (Time.unscaledTime < nextStepAt)
            {
                return;
            }

            nextStepAt = Time.unscaledTime + Mathf.Max(0.01f, interval);

            // One past the maximum, so the cycle spends a step with no dots at all — which is
            // what makes it read as counting up rather than flickering.
            dots = (dots + 1) % (Mathf.Max(1, maxDots) + 1);

            Draw();
        }

        void Draw()
        {
            if (label == null)
            {
                return;
            }

            // Reused rather than reallocated: this runs several times a second for as long as a
            // reply is outstanding.
            builder.Length = 0;
            builder.Append(baseText);
            builder.Append('.', dots);

            label.SetText(builder);
        }
    }
}
