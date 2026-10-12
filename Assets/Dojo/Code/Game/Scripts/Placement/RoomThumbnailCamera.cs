using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// Renders a thumbnail of a room while the game runs, for a room the player has just made.
    /// </summary>
    /// <remarks>
    /// The same view as the pack rooms' thumbnails, which the editor renders: orthographic, from a
    /// three-quarter angle a little above, framed to the room, on a clear background. Drawn by a
    /// camera made for the one frame and only seeing the layer placed pieces are on, so the UI,
    /// the characters and the placement highlight are not in the picture.
    /// </remarks>
    public static class RoomThumbnailCamera
    {
        const int Size = 256;
        const float Yaw = 135f;
        const float Pitch = 40f;
        const float Padding = 1.1f;

        /// <summary>A PNG of everything drawn under <paramref name="root"/> on <paramref name="layer"/>, or null.</summary>
        public static byte[] Render(Transform root, int layer)
        {
            Bounds bounds;

            if (root == null || !TryBounds(root, layer, out bounds))
            {
                return null;
            }

            var go = new GameObject("RoomThumbnailCamera");
            var camera = go.AddComponent<Camera>();
            var target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32);
            var read = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;

            try
            {
                var direction = Quaternion.Euler(Pitch, Yaw, 0f) * Vector3.forward;
                var extent = Mathf.Max(bounds.extents.x, Mathf.Max(bounds.extents.y, bounds.extents.z));

                camera.enabled = false;
                camera.orthographic = true;
                camera.orthographicSize = extent * Padding;
                camera.transform.SetPositionAndRotation(bounds.center - direction * (extent * 4f), Quaternion.LookRotation(direction));
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = extent * 20f;
                camera.cullingMask = 1 << layer;
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
                read.ReadPixels(new Rect(0f, 0f, Size, Size), 0, 0);
                read.Apply(false, false);

                return read.EncodeToPNG();
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                Object.Destroy(go);
                Object.Destroy(read);
                target.Release();
                Object.Destroy(target);
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
