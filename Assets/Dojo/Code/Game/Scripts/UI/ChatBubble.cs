using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Game.UI
{
    /// <summary>
    /// One line of a transcript: what was said, in the shape that speaker's lines take.
    /// </summary>
    /// <remarks>
    /// One prefab per speaker rather than one prefab told who is speaking. The plate, the text
    /// colour and the side the bubble sits on are authored rather than assigned, so the two can be
    /// opened side by side and changed without reading any code.
    /// <para>
    /// The side comes from the row's <c>childAlignment</c> and not from anchors. Anchors were what
    /// held it before, and a layout group overwrites those the moment it runs — which is the whole
    /// reason alignment has to be expressed as a layout property once there is a layout.
    /// </para>
    /// <para>
    /// The width is measured here rather than left to a fitter. A bubble has to stop short of the
    /// far side of the transcript, and the usual way to say that — a fitter on the plate, inside a
    /// layout that also wants to size the plate — is two components fighting over one number.
    /// </para>
    /// </remarks>
    public sealed class ChatBubble : MonoBehaviour
    {
        [Header("Parts")]
        [SerializeField] TMP_Text label;

        [Tooltip("Sized to the wrapped text. The plate takes its size from this, through the " +
                 "layout, which is why nothing here sets the plate directly.")]
        [SerializeField] LayoutElement labelSize;

        [Header("Speaker")]
        [Tooltip("Whose lines this prefab is for. Read when a transcript is drawn, so a row is " +
                 "only ever reused for the speaker it was authored for.")]
        [SerializeField] bool fromPlayer;

        [Tooltip("How much of the transcript's width one bubble may take before it wraps. The " +
                 "remainder is what makes the far side read as the other speaker's.")]
        [Range(0.3f, 1f)]
        [SerializeField] float widthShare = 0.72f;

        [Tooltip("Stands in for the transcript's width before there is one to measure.")]
        [SerializeField] float assumedWidth = 800f;

        /// <summary>Whose lines this row is for.</summary>
        public bool FromPlayer { get { return fromPlayer; } }

        /// <summary>Fills the row in, and sizes it against the width it has been given to sit in.</summary>
        /// <param name="text">What was said.</param>
        /// <param name="rowWidth">How wide the transcript is, which is what the share is taken of.</param>
        public void Bind(string text, float rowWidth)
        {
            if (label == null)
            {
                return;
            }

            label.text = text ?? string.Empty;

            if (labelSize == null)
            {
                return;
            }

            // Before the transcript has been laid out there is no width to take a share of, and a
            // bubble measured against nothing wraps to one word a line — which is not a narrow
            // bubble but a column of single words a thousand units tall.
            if (rowWidth <= 1f)
            {
                rowWidth = assumedWidth;
            }

            float max = Mathf.Max(1f, rowWidth * widthShare);

            // Measured at the width it is allowed rather than at the width it would like, so a
            // short line stays a short bubble and a long one reports the height it needs once
            // wrapped — before the layout asks, rather than a frame after it has already answered.
            Vector2 wanted = label.GetPreferredValues(label.text, max, 0f);

            labelSize.preferredWidth = Mathf.Min(wanted.x, max);
            labelSize.preferredHeight = wanted.y;
        }
    }
}
