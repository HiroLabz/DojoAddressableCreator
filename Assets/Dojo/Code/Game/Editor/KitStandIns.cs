using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Dojo.Game.Editor
{
    /// <summary>
    /// White stand-ins for the marks the UI kit does not have, drawn once from distance functions
    /// at the kit's 3x export scale and imported like the kit's own sprites.
    /// </summary>
    /// <remarks>
    /// Each lands in <c>Art/2D/UI/&lt;family&gt;/dojo_&lt;family&gt;_&lt;name&gt;_idle_base@3x.png</c>, under
    /// the kit's own naming, so real art can replace one file for file - overwrite the PNG, keep its
    /// .meta, and everything that uses it follows. A file that is already there is never redrawn.
    /// <para>
    /// Shared by the builders: the account cards drew the first ones (the <c>account</c> family), and
    /// the popups reuse the plain shapes from it - the box, its edge, the corner marker, the tick.
    /// </para>
    /// </remarks>
    internal static class KitStandIns
    {
        const string Kit = "Assets/Dojo/Art/2D/UI/";
        const int Scale = 3;
        const float Stroke = 1.6f;   // the kit's icon stroke, on its 24 grid

        public static Sprite Get(string family, string name)
        {
            var folder = Kit + family + "/";
            var path = folder + "dojo_" + family + "_" + name + "_idle_base@3x.png";

            if (!File.Exists(path))
            {
                Directory.CreateDirectory(folder);
                Draw(name, path);
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static void Draw(string name, string path)
        {
            int size;
            Func<float, float, float> distance;
            Vector4 border = Vector4.zero;

            // How opaque a point is, from how far outside the shape it is. By default a crisp edge,
            // anti-aliased over one export pixel.
            Func<float, float> shade = d => Mathf.Clamp01(0.5f - d * Scale);

            switch (name)
            {
                case "mail":
                    size = 24;
                    distance = (x, y) => Mathf.Min(
                        Ring(RoundBox(x, y, 12f, 12f, 9f, 6.5f, 1.5f)),
                        Mathf.Min(Line(x, y, 3.8f, 17.6f, 12f, 11.6f), Line(x, y, 12f, 11.6f, 20.2f, 17.6f)));
                    break;

                case "lock":
                    size = 24;
                    distance = (x, y) =>
                    {
                        var body = Mathf.Max(RoundBox(x, y, 12f, 9f, 7f, 5.5f, 1.6f), -Circle(x, y, 12f, 9.4f, 1.4f));
                        var arc = y >= 15f ? Ring(Circle(x, y, 12f, 15f, 4f)) : Mathf.Min(Line(x, y, 8f, 13f, 8f, 15f), Line(x, y, 16f, 13f, 16f, 15f));
                        return Mathf.Min(body, arc);
                    };
                    break;

                case "eye":
                    size = 24;
                    distance = (x, y) => Mathf.Min(
                        Ring(Mathf.Max(Circle(x, y, 12f, 3.5f, 11f), Circle(x, y, 12f, 20.5f, 11f))),
                        Circle(x, y, 12f, 12f, 3f));
                    break;

                case "user":
                    size = 24;
                    distance = (x, y) => Mathf.Min(
                        Ring(Circle(x, y, 12f, 15.5f, 3.8f)),
                        y >= 5f ? Ring(Circle(x, y, 12f, 5f, 7.2f)) : Mathf.Min(Line(x, y, 4.8f, 5f, 4.8f, 4f), Line(x, y, 19.2f, 5f, 19.2f, 4f)));
                    break;

                case "check":
                    size = 24;
                    distance = (x, y) => Mathf.Min(Line(x, y, 5.5f, 12.5f, 10f, 8f, 1.3f), Line(x, y, 10f, 8f, 18.5f, 16.5f, 1.3f));
                    break;

                case "back":
                    // A left arrow: the shaft, and the two strokes of its head.
                    size = 24;
                    distance = (x, y) => Mathf.Min(Line(x, y, 5f, 12f, 19f, 12f),
                        Mathf.Min(Line(x, y, 5f, 12f, 11f, 18f), Line(x, y, 5f, 12f, 11f, 6f)));
                    break;

                case "google":
                    size = 24;
                    distance = (x, y) =>
                    {
                        var angle = Mathf.Atan2(y - 12f, x - 12f) * Mathf.Rad2Deg;
                        var ring = angle > 0f && angle < 48f ? 99f : Ring(Circle(x, y, 12f, 12f, 7.5f), 1.25f);
                        return Mathf.Min(ring, Line(x, y, 12.5f, 12f, 19.5f, 12f, 1.25f));
                    };
                    break;

                case "apple":
                    size = 24;
                    distance = (x, y) =>
                    {
                        var body = Mathf.Min(Mathf.Min(Circle(x, y, 9.3f, 10.5f, 5.6f), Circle(x, y, 14.7f, 10.5f, 5.6f)), Circle(x, y, 12f, 8.2f, 5.2f));
                        body = Mathf.Max(body, -Circle(x, y, 20.2f, 12.5f, 2.8f));
                        var leaf = Circle((x - 13.6f) * 1.8f + 13.6f, y, 13.6f, 19.2f, 2.4f);
                        return Mathf.Min(body, leaf);
                    };
                    break;

                case "areas_flat":
                    // The rail's Areas icon: the outline of a patch of floor with a painted block
                    // in one corner of it.
                    size = 24;
                    distance = (x, y) => Mathf.Min(
                        Ring(RoundBox(x, y, 12f, 12f, 8.5f, 8.5f, 1.8f)),
                        RoundBox(x, y, 9.5f, 9.5f, 3.2f, 3.2f, 0.8f));
                    break;

                case "box":
                    size = 24;
                    distance = (x, y) => RoundBox(x, y, 12f, 12f, 12f, 12f, 5f);
                    border = new Vector4(7f, 7f, 7f, 7f) * Scale;
                    break;

                case "boxedge":
                    size = 24;
                    distance = (x, y) => Ring(RoundBox(x, y, 12f, 12f, 11.3f, 11.3f, 4.5f), 0.65f);
                    border = new Vector4(7f, 7f, 7f, 7f) * Scale;
                    break;

                case "corner":
                    size = 24;
                    distance = (x, y) => Ring(RoundBox(x, y, 12f, 12f, 6f, 6f, 1f), 0.8f);

                    // The corner marker also has a soft halo, like the kit's glows.
                    shade = d =>
                    {
                        var alpha = Mathf.Clamp01(0.5f - d * Scale);
                        return alpha < 1f ? Mathf.Max(alpha, Mathf.Exp(-Mathf.Max(0f, d) * 0.9f) * 0.45f) : alpha;
                    };
                    break;

                case "alert":
                    // A warning triangle with an exclamation mark.
                    size = 24;
                    distance = (x, y) =>
                    {
                        var sides = Mathf.Min(
                            Mathf.Min(Line(x, y, 12f, 19.6f, 3.4f, 4.6f), Line(x, y, 3.4f, 4.6f, 20.6f, 4.6f)),
                            Line(x, y, 20.6f, 4.6f, 12f, 19.6f));
                        var mark = Mathf.Min(Line(x, y, 12f, 14f, 12f, 10.4f), Circle(x, y, 12f, 7.7f, 0.95f));
                        return Mathf.Min(sides, mark);
                    };
                    break;

                case "info":
                    // An "i" in a circle.
                    size = 24;
                    distance = (x, y) => Mathf.Min(
                        Ring(Circle(x, y, 12f, 12f, 9f)),
                        Mathf.Min(Line(x, y, 12f, 7.6f, 12f, 11.6f), Circle(x, y, 12f, 15.4f, 0.95f)));
                    break;

                case "glowbox":
                    // A soft light around a rounded box, for a badge. Nine-sliced: the corners hold the
                    // whole rounded corner and its falloff, so any size keeps the same softness.
                    size = 48;
                    distance = (x, y) => RoundBox(x, y, 24f, 24f, 12f, 12f, 5f);
                    shade = d => Falloff(d, 12f) * 0.75f * Mathf.Clamp01(1f + d);
                    border = new Vector4(18f, 18f, 18f, 18f) * Scale;
                    break;

                case "glowline":
                    // A soft horizontal band, brightest along its middle, for a glowing edge.
                    size = 24;
                    distance = (x, y) => Mathf.Abs(y - 12f);
                    shade = d => Falloff(d, 12f) * 0.8f;
                    break;

                default:
                    throw new ArgumentException("No stand-in called '" + name + "'.", nameof(name));
            }

            var px = size * Scale;
            var texture = new Texture2D(px, px, TextureFormat.RGBA32, false);
            var pixels = new Color32[px * px];

            for (var j = 0; j < px; j++)
            {
                for (var i = 0; i < px; i++)
                {
                    var x = (i + 0.5f) / Scale;
                    var y = (j + 0.5f) / Scale;
                    var alpha = Mathf.Clamp01(shade(distance(x, y)));

                    pixels[j * px + i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 300f;   // the kit's 3x at 100 per unit
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.spriteBorder = border;
            importer.SaveAndReimport();
        }

        /// <summary>
        /// A glow's strength <paramref name="d"/> out from its shape: full at the edge, none at
        /// <paramref name="reach"/> - exactly none, so the sprite's own edge never shows as a faint box.
        /// </summary>
        static float Falloff(float d, float reach)
        {
            var t = 1f - Mathf.Clamp01(Mathf.Max(0f, d) / reach);
            return t * t * t;
        }

        static float Circle(float x, float y, float cx, float cy, float r)
            => Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r;

        static float RoundBox(float x, float y, float cx, float cy, float hx, float hy, float r)
        {
            var qx = Mathf.Abs(x - cx) - (hx - r);
            var qy = Mathf.Abs(y - cy) - (hy - r);
            var outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        /// <summary>A shape's outline, <paramref name="half"/> either side of its edge.</summary>
        static float Ring(float d, float half = Stroke / 2f) => Mathf.Abs(d) - half;

        static float Line(float x, float y, float ax, float ay, float bx, float by, float half = Stroke / 2f)
        {
            float px = x - ax, py = y - ay, dx = bx - ax, dy = by - ay;
            var h = Mathf.Clamp01((px * dx + py * dy) / (dx * dx + dy * dy));
            var ex = px - dx * h;
            var ey = py - dy * h;
            return Mathf.Sqrt(ex * ex + ey * ey) - half;
        }
    }
}
