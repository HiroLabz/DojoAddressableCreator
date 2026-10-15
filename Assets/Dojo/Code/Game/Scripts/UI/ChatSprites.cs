using UnityEngine;

namespace Dojo.Game.UI
{
    /// <summary>
    /// The two shapes the chat panel is drawn out of: a rounded rectangle and a circle.
    /// </summary>
    /// <remarks>
    /// Generated rather than imported, because the project has no UI art at all — every sprite in
    /// it is a furniture thumbnail — and a chat panel built from Unity's default square
    /// <c>Image</c> looks nothing like the design. Two textures with no asset to lose, no importer
    /// to set up and nothing to keep in sync, which is the same trade <c>PlacementHighlight</c>
    /// already makes for its glow.
    /// <para>
    /// The rounded rectangle is a 9-sliced sprite, so one 48px texture draws a panel, a header, an
    /// input box and a bubble at any size without the corners stretching. Both are built once and
    /// shared by every <see cref="AgentChatUI"/> in the scene.
    /// </para>
    /// </remarks>
    public static class ChatSprites
    {
        static Sprite rounded;
        static Sprite circle;

        /// <summary>A white rounded rectangle, 9-sliced on its corner radius.</summary>
        public static Sprite Rounded => rounded != null ? rounded : (rounded = BuildRounded(48, 14));

        /// <summary>A white circle.</summary>
        public static Sprite Circle => circle != null ? circle : (circle = BuildCircle(96));

        /// <summary>
        /// A rounded rectangle with a hard edge softened by one pixel of coverage.
        /// </summary>
        /// <remarks>
        /// The border is set to the corner radius so Unity's sliced draw mode leaves the corners
        /// alone and stretches only the flat middle. Antialiasing is done by measuring how far each
        /// pixel is outside the rounded outline rather than by supersampling: one texel of falloff
        /// is all a UI corner needs, and it costs nothing to generate.
        /// </remarks>
        static Sprite BuildRounded(int size, int radius)
        {
            var texture = NewTexture(size, "ChatRoundedRect");
            var pixels = new Color32[size * size];

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    pixels[y * size + x] = Coverage(x, y, size, radius);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            // Border on all four sides = the radius, which is what keeps the corners circular at
            // any rect size. Sliced draw mode on the Image is what reads it.
            return Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(radius, radius, radius, radius));
        }

        /// <summary>How much of the pixel at (x, y) is inside the rounded outline, as white alpha.</summary>
        static Color32 Coverage(int x, int y, int size, int radius)
        {
            // Distance from the pixel centre to the nearest point of the rounded rectangle, worked
            // out in the corner quadrant the pixel falls in.
            var px = x + 0.5f;
            var py = y + 0.5f;

            var nearestX = Mathf.Clamp(px, radius, size - radius);
            var nearestY = Mathf.Clamp(py, radius, size - radius);

            var dx = px - nearestX;
            var dy = py - nearestY;
            var distance = Mathf.Sqrt(dx * dx + dy * dy);

            // Inside the straight part, distance is zero and the pixel is solid. Round the corner
            // over one texel so the edge is not stair-stepped.
            var alpha = Mathf.Clamp01(radius + 0.5f - distance);

            return new Color32(255, 255, 255, (byte)(alpha * 255f));
        }

        static Sprite BuildCircle(int size)
        {
            var texture = NewTexture(size, "ChatCircle");
            var pixels = new Color32[size * size];
            var centre = size * 0.5f;
            var radius = centre - 0.5f;

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = x + 0.5f - centre;
                    var dy = y + 0.5f - centre;
                    var alpha = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);

                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <remarks>
        /// <c>DontSave</c> so a generated texture is never written into the scene, and clamped so
        /// the softened edge does not sample round to the opposite side of the texture.
        /// </remarks>
        static Texture2D NewTexture(int size, string name)
            => new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
    }
}
