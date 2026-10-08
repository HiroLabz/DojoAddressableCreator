using UnityEngine;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// Renders a camera into a picture, leaving some layers out.
    /// </summary>
    public static class WorldPicture
    {
        /// <summary>
        /// What <paramref name="camera"/> sees, at the size asked for, without
        /// <paramref name="hiddenLayers"/>. The caller owns the texture and must destroy it.
        /// </summary>
        /// <remarks>
        /// A render of its own rather than a copy of the screen, so the menus drawn on the screen
        /// are not in it and the picture can be the shape of the card it goes on. A picture of a
        /// different shape to the screen shows the middle of the view: the camera keeps its height
        /// and loses some width.
        /// <para>
        /// The camera is handed back exactly as it was - its target, its layers, and the texture
        /// that was active - whatever happens, because it is the game's camera and is on the
        /// screen again next frame.
        /// </para>
        /// </remarks>
        public static Texture2D Render(Camera camera, int width, int height, int hiddenLayers)
        {
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);

            var targetBefore = camera.targetTexture;
            var layersBefore = camera.cullingMask;
            var activeBefore = RenderTexture.active;

            try
            {
                camera.cullingMask = layersBefore & ~hiddenLayers;
                camera.targetTexture = target;
                camera.Render();

                RenderTexture.active = target;

                var picture = new Texture2D(width, height, TextureFormat.RGB24, false);
                picture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                picture.Apply(false);

                return picture;
            }
            finally
            {
                camera.targetTexture = targetBefore;
                camera.cullingMask = layersBefore;
                RenderTexture.active = activeBefore;
                RenderTexture.ReleaseTemporary(target);
            }
        }
    }
}
