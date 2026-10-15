using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Game.UI
{
    /// <summary>
    /// Makes a chat input wrap and grow taller as the player types, a line at a time, up to a cap.
    /// </summary>
    /// <remarks>
    /// Both chat windows are anchored rather than laid out, so a taller input cannot push anything
    /// out of its way by itself. The rects above it are moved here by exactly the height the input
    /// gained: a vertically stretched one (the transcript) gives up its bottom edge, a fixed one
    /// (the busy line) is carried up whole. Past the cap the box stops growing and the field
    /// scrolls inside itself, which is what the field does on its own once it is multi-line.
    /// <para>
    /// Attached in code by the window that owns the input rather than authored, because everything
    /// it needs is already a reference on that window — authoring it too would be a second place
    /// to keep those references in step.
    /// </para>
    /// <para>
    /// Measured in <c>LateUpdate</c> rather than on <c>onValueChanged</c>, for two reasons. The
    /// agent window clears the field with <c>SetTextWithoutNotify</c> after sending, which raises
    /// no event, so the box would stay tall after the text was gone. And maximising or restoring a
    /// window changes how many lines the same text needs. Comparing the text and the width once a
    /// frame catches both, and only measures when one of them has actually changed.
    /// </para>
    /// </remarks>
    public sealed class ChatInputAutoGrow : MonoBehaviour
    {
        /// <summary>
        /// A height big enough to never constrain a measurement — the same stand-in TMP uses for
        /// its own preferred-height calculation.
        /// </summary>
        const float Unbounded = 32767f;

        /// <summary>
        /// What one line is measured with.
        /// </summary>
        /// <remarks>
        /// Not a single capital. Line height depends on which glyphs are in the line: in the agent
        /// window's JetBrains Mono at 24pt, <c>X</c>, <c>x</c> and <c>h</c> measure 26.8 while
        /// <c>t</c>, <c>g</c> and <c>y</c> measure 31.7 — those come from a fallback font with
        /// taller line metrics. Measured against <c>X</c>, a one-line message with a <c>t</c> in it
        /// grew the box by five pixels. This sample takes the tall case as one line.
        /// </remarks>
        const string LineSample = "Xgjty|";

        /// <summary>A rect above the input that moves up as the input grows.</summary>
        struct Follower
        {
            public RectTransform Rect;

            /// <summary>
            /// True for a rect stretched between two vertical anchors, which shrinks from the
            /// bottom; false for a fixed-height one, which is moved up whole.
            /// </summary>
            public bool Stretched;

            public float BaseBottom;
            public float BaseTop;
        }

        readonly List<Follower> followers = new List<Follower>();

        TMP_InputField input;
        TMP_Text text;

        /// <summary>
        /// What actually gets taller. The input itself in one window, the bar it sits in in the
        /// other — whichever carries the background the player sees.
        /// </summary>
        RectTransform resized;

        ScrollRect transcript;
        float baseHeight;

        /// <summary><see cref="LineSample"/> once per line allowed, measured to find the cap.</summary>
        string capSample;

        // Only the width changes these, so they are measured again only when it does.
        float lineHeight;
        float capHeight;

        string measuredText;
        float measuredWidth = -1f;
        float growth;

        /// <summary>
        /// Puts the behaviour on <paramref name="input"/>, or returns the one already there.
        /// </summary>
        /// <param name="input">The field the player types into.</param>
        /// <param name="resized">
        /// The rect that grows. Expected to be anchored to the bottom with its pivot at the bottom,
        /// so it grows upward and its bottom edge stays where it was authored.
        /// </param>
        /// <param name="maxLines">How many lines it grows to before the field scrolls instead.</param>
        /// <param name="transcript">
        /// The conversation, kept scrolled to the newest message as it shrinks — if it was already
        /// there. Optional.
        /// </param>
        /// <param name="followers">The rects above the input that move up with it.</param>
        /// <remarks>
        /// Configured once. The base heights are captured on the first call, while every rect is
        /// still at its authored size; a second call would capture the grown sizes as the base.
        /// </remarks>
        public static ChatInputAutoGrow Attach(
            TMP_InputField input,
            RectTransform resized,
            int maxLines,
            ScrollRect transcript,
            params RectTransform[] followers)
        {
            if (input == null || input.textComponent == null || resized == null)
            {
                return null;
            }

            var grow = input.GetComponent<ChatInputAutoGrow>();

            if (grow != null)
            {
                return grow;
            }

            grow = input.gameObject.AddComponent<ChatInputAutoGrow>();
            grow.Configure(input, resized, maxLines, transcript, followers);

            return grow;
        }

        void Configure(
            TMP_InputField field,
            RectTransform grows,
            int maxLines,
            ScrollRect scroll,
            RectTransform[] above)
        {
            input = field;
            text = field.textComponent;
            resized = grows;
            transcript = scroll;
            baseHeight = grows.rect.height;

            var sample = new StringBuilder(LineSample);

            for (int i = 1; i < Mathf.Max(1, maxLines); i++)
            {
                sample.Append('\n').Append(LineSample);
            }

            capSample = sample.ToString();

            if (above != null)
            {
                foreach (var rect in above)
                {
                    if (rect == null)
                    {
                        continue;
                    }

                    followers.Add(new Follower
                    {
                        Rect = rect,
                        Stretched = !Mathf.Approximately(rect.anchorMin.y, rect.anchorMax.y),
                        BaseBottom = rect.offsetMin.y,
                        BaseTop = rect.offsetMax.y,
                    });
                }
            }

            // Wrap instead of scrolling sideways. MultiLineSubmit wraps like a multi-line field but
            // still ends the edit on Enter, which is what both windows' onSubmit — and so Send —
            // hang off. MultiLineNewline would turn Enter into a line break and nothing would send.
            field.lineType = TMP_InputField.LineType.MultiLineSubmit;

            // Setting the line type already switches wrapping on in the TMP versions this was
            // written against. Checked rather than assumed, because without wrapping nothing below
            // ever measures more than one line.
            if (text.textWrappingMode == TextWrappingModes.NoWrap
                || text.textWrappingMode == TextWrappingModes.PreserveWhitespaceNoWrap)
            {
                text.textWrappingMode = TextWrappingModes.Normal;
            }
        }

        void LateUpdate()
        {
            if (input == null || text == null)
            {
                return;
            }

            string current = input.text;
            float width = text.rectTransform.rect.width;

            bool widthChanged = !Mathf.Approximately(width, measuredWidth);

            if (!widthChanged && string.Equals(current, measuredText, System.StringComparison.Ordinal))
            {
                return;
            }

            if (widthChanged)
            {
                measuredWidth = width;
                lineHeight = Height(LineSample, width);
                capHeight = Height(capSample, width);
            }

            measuredText = current;

            Apply(Growth(current, width));
        }

        /// <summary>
        /// How much taller than one line the text needs the box to be, capped.
        /// </summary>
        /// <remarks>
        /// Relative to one line rather than absolute, so the authored padding around the first line
        /// is kept exactly as designed and every added line adds exactly one line's height.
        /// </remarks>
        float Growth(string value, float width)
        {
            if (width <= 0f || string.IsNullOrEmpty(value))
            {
                return 0f;
            }

            float needed = Height(value, width);
            float cap = Mathf.Max(0f, capHeight - lineHeight);

            return Mathf.Ceil(Mathf.Clamp(needed - lineHeight, 0f, cap));
        }

        float Height(string value, float width) => text.GetPreferredValues(value, width, Unbounded).y;

        void Apply(float next)
        {
            if (Mathf.Approximately(next, growth))
            {
                return;
            }

            // Read before anything moves: shrinking the transcript from the bottom would otherwise
            // push the newest message out of sight while the player is typing a reply to it.
            bool atNewest = transcript != null && transcript.verticalNormalizedPosition <= 0.001f;

            growth = next;

            resized.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, baseHeight + growth);

            foreach (var follower in followers)
            {
                var rect = follower.Rect;

                if (rect == null)
                {
                    continue;
                }

                // Only the vertical offsets are touched, and the horizontal ones are read back
                // fresh, so anything else moving these rects sideways is left alone.
                rect.offsetMin = new Vector2(rect.offsetMin.x, follower.BaseBottom + growth);

                if (!follower.Stretched)
                {
                    rect.offsetMax = new Vector2(rect.offsetMax.x, follower.BaseTop + growth);
                }
            }

            if (atNewest)
            {
                transcript.verticalNormalizedPosition = 0f;
            }
        }
    }
}
