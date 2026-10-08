using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Framework.UI
{
    /// <summary>
    /// A rectangle with its top-left and bottom-right corners cut off at 45 degrees - the holo
    /// panels' shape - drawn as a fill, an edge line, or a glow, at any size and with no sprite.
    /// </summary>
    /// <remarks>
    /// The mock-ups clip boxes to this polygon in CSS, and the UI kit has no sprite for it. Drawing
    /// it as a mesh keeps every size crisp, and lets one shape do four jobs:
    /// <list type="bullet">
    /// <item><b>Fill</b> - the whole shape, in one colour or a straight gradient.</item>
    /// <item><b>Line</b> - an edge of an even width just inside the rectangle. Even round the
    /// angled corners too, so the outline never breaks or thins where it turns.</item>
    /// <item><b>Glow</b> - a band outside the shape, fading from the colour at its edge to nothing.
    /// Two or three of them, wide and narrow, stand in for the mock-ups' stacked drop shadows.</item>
    /// <item><b>InnerGlow</b> - the same band, fading inwards: a backlit edge.</item>
    /// </list>
    /// A glow reaches beyond the rectangle, so it should not take clicks: leave its
    /// <see cref="Graphic.raycastTarget"/> off.
    /// </remarks>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ChamferShape : MaskableGraphic
    {
        /// <summary>What is drawn of the shape.</summary>
        public enum Style
        {
            Fill,
            Line,
            Glow,
            InnerGlow,
        }

        [SerializeField] Style style = Style.Fill;

        [Tooltip("How far along each side the two corners are cut, in canvas units. Never more than " +
                 "half the shorter side. Zero is a plain rectangle.")]
        [SerializeField] float cut = 18f;

        [Tooltip("Line: its width. Glow and InnerGlow: how far the glow reaches.")]
        [SerializeField] float width = 1f;

        [Tooltip("Shade from the colour to End Color: top to bottom, or left to right.")]
        [SerializeField] bool gradient;

        [SerializeField] Color endColor = Color.white;

        [Tooltip("Left to right instead of top to bottom.")]
        [SerializeField] bool horizontal;

        readonly List<Vector2> outer = new List<Vector2>(6);
        readonly List<Vector2> other = new List<Vector2>(6);

        public Style Shape
        {
            get => style;
            set { style = value; SetVerticesDirty(); }
        }

        public float Cut
        {
            get => cut;
            set { cut = value; SetVerticesDirty(); }
        }

        public float Width
        {
            get => width;
            set { width = value; SetVerticesDirty(); }
        }

        /// <summary>Shades from <see cref="Graphic.color"/> to <paramref name="end"/>.</summary>
        public void SetGradient(Color start, Color end, bool leftToRight = false)
        {
            gradient = true;
            horizontal = leftToRight;
            endColor = end;
            color = start;
            SetVerticesDirty();
        }

        /// <summary>One colour all over.</summary>
        public void SetSolid(Color colour)
        {
            gradient = false;
            color = colour;
            SetVerticesDirty();
        }

        /// <summary>
        /// The shape's corners, clockwise from the lower end of the top-left cut, with y up. A plain
        /// rectangle - four corners - when there is no cut.
        /// </summary>
        public static void Outline(Rect rect, float cut, List<Vector2> points)
        {
            points.Clear();

            var c = Mathf.Clamp(cut, 0f, Mathf.Min(rect.width, rect.height) * 0.5f);

            if (c <= 0f)
            {
                points.Add(new Vector2(rect.xMin, rect.yMax));
                points.Add(new Vector2(rect.xMax, rect.yMax));
                points.Add(new Vector2(rect.xMax, rect.yMin));
                points.Add(new Vector2(rect.xMin, rect.yMin));
                return;
            }

            points.Add(new Vector2(rect.xMin, rect.yMax - c));
            points.Add(new Vector2(rect.xMin + c, rect.yMax));
            points.Add(new Vector2(rect.xMax, rect.yMax));
            points.Add(new Vector2(rect.xMax, rect.yMin + c));
            points.Add(new Vector2(rect.xMax - c, rect.yMin));
            points.Add(new Vector2(rect.xMin, rect.yMin));
        }

        /// <summary>
        /// The same clockwise outline with every side moved out by <paramref name="distance"/>, or
        /// in when it is negative. Each corner moves along its mitre, so it stays exactly that far
        /// from both of its sides.
        /// </summary>
        public static void Offset(IReadOnlyList<Vector2> polygon, float distance, List<Vector2> into)
        {
            into.Clear();

            var count = polygon.Count;

            for (var i = 0; i < count; i++)
            {
                var previous = polygon[(i + count - 1) % count];
                var here = polygon[i];
                var next = polygon[(i + 1) % count];

                var a = Outward(previous, here);
                var b = Outward(here, next);
                var mitre = (a + b).normalized;

                // How far along the mitre is a side's distance away. Floored so a corner far
                // sharper than these shapes ever have cannot throw a point to infinity.
                var along = Mathf.Max(Vector2.Dot(mitre, a), 0.2f);

                into.Add(here + mitre * (distance / along));
            }
        }

        /// <summary>The outward normal of one side of a clockwise outline, with y up.</summary>
        static Vector2 Outward(Vector2 from, Vector2 to)
        {
            var along = (to - from).normalized;
            return new Vector2(-along.y, along.x);
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            var rect = GetPixelAdjustedRect();

            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            Outline(rect, cut, outer);

            switch (style)
            {
                case Style.Fill:
                    Fan(vh, rect);
                    break;

                case Style.Line:
                    Offset(outer, -Mathf.Max(0f, width), other);
                    Ring(vh, rect, outer, 1f, other, 1f);
                    break;

                case Style.Glow:
                    Offset(outer, Mathf.Max(0f, width), other);
                    Ring(vh, rect, outer, 1f, other, 0f);
                    break;

                case Style.InnerGlow:
                    Offset(outer, -Mathf.Max(0f, width), other);
                    Ring(vh, rect, outer, 1f, other, 0f);
                    break;
            }
        }

        /// <summary>The whole shape: a fan from its middle, which is exact for a straight gradient.</summary>
        void Fan(VertexHelper vh, Rect rect)
        {
            vh.AddVert(rect.center, ColourAt(rect.center, rect, 1f), Vector4.zero);

            foreach (var point in outer)
            {
                vh.AddVert(point, ColourAt(point, rect, 1f), Vector4.zero);
            }

            for (var i = 0; i < outer.Count; i++)
            {
                vh.AddTriangle(0, 1 + i, 1 + (i + 1) % outer.Count);
            }
        }

        /// <summary>The band between two outlines, each with its own share of the colour's alpha.</summary>
        void Ring(VertexHelper vh, Rect rect, List<Vector2> a, float alphaA, List<Vector2> b, float alphaB)
        {
            var count = a.Count;

            for (var i = 0; i < count; i++)
            {
                vh.AddVert(a[i], ColourAt(a[i], rect, alphaA), Vector4.zero);
                vh.AddVert(b[i], ColourAt(b[i], rect, alphaB), Vector4.zero);
            }

            for (var i = 0; i < count; i++)
            {
                var j = (i + 1) % count;
                vh.AddTriangle(2 * i, 2 * j, 2 * j + 1);
                vh.AddTriangle(2 * i, 2 * j + 1, 2 * i + 1);
            }
        }

        Color ColourAt(Vector2 point, Rect rect, float alpha)
        {
            var colour = color;

            if (gradient)
            {
                var t = horizontal
                    ? Mathf.InverseLerp(rect.xMin, rect.xMax, point.x)
                    : Mathf.InverseLerp(rect.yMax, rect.yMin, point.y);

                colour = Color.Lerp(color, endColor, t);
            }

            colour.a *= alpha;
            return colour;
        }
    }
}
