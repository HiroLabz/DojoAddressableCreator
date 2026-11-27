using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// Renders a thumbnail of a room while the game runs, for a room the player has just made, and
    /// any other part of the world as a picture (the Departments tab's snapshots).
    /// </summary>
    /// <remarks>
    /// The same view as the pack rooms' thumbnails, which the editor renders: orthographic, from a
    /// three-quarter angle a little above, framed to the room, on a clear background. Drawn by a
    /// camera made for the one frame and only seeing the layer placed pieces are on, so the UI,
    /// the characters and the placement highlight are not in the picture.
    /// <para>
    /// Taken from DojoAddressableCreator as it is. The room editor (<see cref="RoomEditor"/>)
    /// hands it its own root, far from the world, so nothing of the world is ever framed.
    /// </para>
    /// <para>
    /// Framed by <see cref="ThumbnailFraming"/>, so the whole room is in the picture: it used to
    /// size the view to the room's largest half-width, which cut off its corners.
    /// </para>
    /// </remarks>
    public static class RoomThumbnailCamera
    {
        const int Size = 256;
        public const float Yaw = 135f;
        public const float Pitch = 40f;
        const float Padding = 1.1f;

        /// <summary>A PNG of everything drawn under <paramref name="root"/> on <paramref name="layer"/>, or null.</summary>
        public static byte[] Render(Transform root, int layer)
        {
            Bounds bounds;

            if (root == null || !TryBounds(root, layer, out bounds))
            {
                return null;
            }

            var picture = RenderBounds(bounds, 1 << layer, Size);

            if (picture == null)
            {
                return null;
            }

            try
            {
                return picture.EncodeToPNG();
            }
            finally
            {
                Discard(picture);
            }
        }

        /// <summary>
        /// A square picture of whatever <paramref name="cullingMask"/> draws inside
        /// <paramref name="bounds"/>, from the thumbnails' angle, on a clear background; null when it
        /// could not be drawn. The caller owns the texture.
        /// </summary>
        public static Texture2D RenderBounds(Bounds bounds, int cullingMask, int size = Size, float padding = Padding)
        {
            if (bounds.size == Vector3.zero || size <= 0)
            {
                return null;
            }

            var go = new GameObject("RoomThumbnailCamera");
            var camera = go.AddComponent<Camera>();
            var target = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32);
            var read = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;

            try
            {
                camera.enabled = false;
                ThumbnailFraming.Apply(camera, ThumbnailFraming.Fit(bounds, Pitch, Yaw, padding));
                camera.cullingMask = cullingMask;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                camera.targetTexture = target;

                // Through the render pipeline's own request where it takes one, which is how URP
                // renders a camera on demand; a plain Render for anything that does not.
                var request = new RenderPipeline.StandardRequest { destination = target };

                if (RenderPipeline.SupportsRenderRequest(camera, request))
                {
                    RenderPipeline.SubmitRenderRequest(camera, request);
                }
                else
                {
                    camera.Render();
                }

                RenderTexture.active = target;
                read.ReadPixels(new Rect(0f, 0f, size, size), 0, 0);
                read.Apply(false, false);

                var picture = read;
                read = null;
                return picture;
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                Discard(go);

                if (read != null)
                {
                    Discard(read);
                }

                target.Release();
                Discard(target);
            }
        }

        /// <summary>Destroys at the end of the frame in play, at once outside it, where Destroy is refused.</summary>
        static void Discard(Object thing)
        {
            if (Application.isPlaying)
            {
                Object.Destroy(thing);
            }
            else
            {
                Object.DestroyImmediate(thing);
            }
        }

        /// <summary>The world bounds of every renderer under the root on the layer.</summary>
        static bool TryBounds(Transform root, int layer, out Bounds bounds)
        {
            bounds = new Bounds();
            var found = false;
            var renderers = new List<Renderer>();
            root.GetComponentsInChildren(false, renderers);

            foreach (var renderer in renderers)
            {
                if (renderer.gameObject.layer != layer || renderer is ParticleSystemRenderer)
                {
                    continue;
                }

                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return found;
        }
    }
}
