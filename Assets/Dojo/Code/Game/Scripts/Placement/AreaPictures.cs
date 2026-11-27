using System.Collections.Generic;
using UnityEngine;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// A picture of what a set of areas looks like: the floor, walls and furniture inside them,
    /// from the thumbnails' angle. The Departments tab's cards show one for each department
    /// (Jacob, 27 Nov: the card's box was an empty grey square).
    /// </summary>
    /// <remarks>
    /// Taken by <see cref="RoomThumbnailCamera.RenderBounds"/>, so it is framed like every other
    /// thumbnail (<see cref="ThumbnailFraming"/>), and it sees only the floor and the placed pieces:
    /// no characters, no UI, no placement highlight. A department with areas on more than one
    /// floor is pictured on its lowest one, where the in-game menu's camera goes too.
    /// </remarks>
    public static class AreaPictures
    {
        /// <summary>How far above an area's cells the picture reaches: a wall's height and a little more.</summary>
        public const float Height = 2.7f;

        /// <summary>Pixels along each side.</summary>
        const int Size = 192;

        /// <summary>The layers a picture sees.</summary>
        static readonly string[] Layers = { "Floor", "Furniture" };

        /// <summary>
        /// The box the areas' cells cover on their lowest floor, from the cells up to
        /// <see cref="Height"/>; false when there are no cells.
        /// </summary>
        public static bool TryBounds(IEnumerable<BlockArea> areas, out Bounds bounds)
        {
            bounds = new Bounds();

            if (areas == null)
            {
                return false;
            }

            var lowest = int.MaxValue;

            foreach (var area in areas)
            {
                if (area != null && area.Cells != null && area.Cells.Count > 0 && area.Floor < lowest)
                {
                    lowest = area.Floor;
                }
            }

            if (lowest == int.MaxValue)
            {
                return false;
            }

            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            foreach (var area in areas)
            {
                if (area == null || area.Cells == null || area.Floor != lowest)
                {
                    continue;
                }

                var half = area.CellSize * 0.5f;

                foreach (var cell in area.Cells)
                {
                    min = Vector3.Min(min, new Vector3(cell.x - half, cell.y, cell.z - half));
                    max = Vector3.Max(max, new Vector3(cell.x + half, cell.y, cell.z + half));
                }
            }

            // From just under the cells (they sit a little above the floor) to the top of a wall.
            min.y -= 0.1f;
            max.y += Height;
            bounds.SetMinMax(min, max);
            return true;
        }

        /// <summary>
        /// A picture of the areas, or null when they have no cells or it could not be drawn. Hand it
        /// back to <see cref="Release"/> when done with it: it owns its texture.
        /// </summary>
        public static Sprite Picture(IEnumerable<BlockArea> areas)
        {
            Bounds bounds;

            if (!TryBounds(areas, out bounds))
            {
                return null;
            }

            var texture = RoomThumbnailCamera.RenderBounds(bounds, LayerMask.GetMask(Layers), Size, 1.05f);

            if (texture == null)
            {
                return null;
            }

            texture.name = "AreaPicture";
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            sprite.name = "AreaPicture";
            return sprite;
        }

        /// <summary>Destroys a picture made by <see cref="Picture"/>, and its texture.</summary>
        public static void Release(Sprite picture)
        {
            if (picture == null)
            {
                return;
            }

            var texture = picture.texture;
            Discard(picture);

            if (texture != null)
            {
                Discard(texture);
            }
        }

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
    }
}
