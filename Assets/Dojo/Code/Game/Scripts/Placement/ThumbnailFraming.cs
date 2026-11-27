using UnityEngine;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// Where an orthographic camera stands, and how wide it looks, so that the whole of a box is in
    /// the picture when seen from a given angle.
    /// </summary>
    /// <remarks>
    /// Every thumbnail goes through this: the Addressable Generator's item icons, the room
    /// thumbnails (editor and in-game), and the Departments tab's snapshots. They used to size the
    /// view to the box's largest half-width, which cuts off anything square-ish: from the
    /// three-quarter angle (yaw 135) a box's footprint shows across its diagonal, up to 1.4 times
    /// as wide, and its height adds to that (Jacob, 27 Nov: "the thumbnail is cut").
    /// <para>
    /// Here the box's eight corners are projected onto the camera's view, so the picture is
    /// exactly as wide and as tall as what the camera sees, centred on it, with
    /// <c>padding</c> as the margin (1.1 leaves 5% on each side).
    /// </para>
    /// </remarks>
    public static class ThumbnailFraming
    {
        /// <summary>The camera's place and view for <paramref name="bounds"/> seen along <paramref name="rotation"/>.</summary>
        /// <param name="aspect">The picture's width over its height; 1 for a square thumbnail.</param>
        /// <param name="padding">How much bigger than a tight fit the view is; at least 1.</param>
        public static Shot Fit(Bounds bounds, Quaternion rotation, float padding = 1.1f, float aspect = 1f)
        {
            var toView = Quaternion.Inverse(rotation);
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            var e = bounds.extents;

            for (var i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? -e.x : e.x,
                    (i & 2) == 0 ? -e.y : e.y,
                    (i & 4) == 0 ? -e.z : e.z);

                var seen = toView * corner;
                min = Vector3.Min(min, seen);
                max = Vector3.Max(max, seen);
            }

            var halfWidth = (max.x - min.x) * 0.5f;
            var halfHeight = (max.y - min.y) * 0.5f;
            var depth = max.z - min.z;
            var margin = Mathf.Max(0.1f, depth * 0.1f);

            if (aspect <= 0f)
            {
                aspect = 1f;
            }

            // Centred on what is seen, not on the box: a tall cabinet seen from above sits lower in
            // its box's projection than its centre does.
            var centre = new Vector3((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f, min.z - margin);

            return new Shot
            {
                Position = bounds.center + rotation * centre,
                Rotation = rotation,
                OrthographicSize = Mathf.Max(0.01f, Mathf.Max(halfHeight, halfWidth / aspect) * Mathf.Max(1f, padding)),
                Near = 0.01f,
                Far = depth + margin * 2f,
            };
        }

        /// <summary>The same, for a camera looking along <c>Euler(pitch, yaw, 0)</c>, as every thumbnail does.</summary>
        public static Shot Fit(Bounds bounds, float pitch, float yaw, float padding = 1.1f, float aspect = 1f)
            => Fit(bounds, Quaternion.LookRotation(Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward), padding, aspect);

        /// <summary>Puts <paramref name="camera"/> where <paramref name="shot"/> says, orthographic.</summary>
        public static void Apply(Camera camera, Shot shot)
        {
            camera.orthographic = true;
            camera.orthographicSize = shot.OrthographicSize;
            camera.transform.SetPositionAndRotation(shot.Position, shot.Rotation);
            camera.nearClipPlane = shot.Near;
            camera.farClipPlane = shot.Far;
        }

        /// <summary>An orthographic camera's place and view.</summary>
        public struct Shot
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public float OrthographicSize;
            public float Near;
            public float Far;
        }
    }
}
