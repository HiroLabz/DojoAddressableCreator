using System.Collections.Generic;
using UnityEngine;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// Where two rooms meet, one wall shows: the other is turned off, not destroyed, and comes back
    /// on when the wall in its place goes. As in dojo-game-app.
    /// </summary>
    /// <remarks>
    /// Every room keeps all of its own walls, so it is whole on its own:
    /// <list type="bullet">
    /// <item>A room put down against a wall already standing turns off its own wall there
    /// (<see cref="HideShared"/>).</item>
    /// <item>A room's entrance turns off the wall it opens through, a neighbour's or a loose one
    /// (<see cref="CutAt"/>).</item>
    /// <item>When a wall or a room is deleted, and when a room is lifted, every turned-off wall is
    /// looked at again (<see cref="Reconsider"/>): one with nothing standing in its place and no
    /// entrance through it comes back on.</item>
    /// </list>
    /// A turned-off wall gives up its cells and is switched off, so it blocks nobody, is not in the
    /// navigation and cannot be clicked. It is still saved, as hidden, with its room. The room
    /// editor is separate: cutting an opening there destroys the room's own wall, which is editing
    /// the room itself.
    /// </remarks>
    public static class RoomWalls
    {
        /// <summary>Whether a piece is a wall, as the room file format tells.</summary>
        public static bool IsWall(FurniturePiece piece) => piece != null && RoomEditor.IsWall(piece.transform);

        /// <summary>Whether a piece is turned off by this class.</summary>
        public static bool IsHidden(FurniturePiece piece) =>
            piece != null && !piece.gameObject.activeSelf && piece.GetComponent<HiddenWall>() != null;

        /// <summary>
        /// Whether two wall pieces run the same way, overlapping, for half the first one's length,
        /// on the same level: two walls where one is meant.
        /// </summary>
        public static bool Duplicates(Bounds a, Bounds b)
        {
            // Not a wall on the floor above or below.
            if (a.min.y >= b.max.y - 0.05f || b.min.y >= a.max.y - 0.05f)
            {
                return false;
            }

            var aAlongZ = a.size.z > a.size.x;
            if (aAlongZ != (b.size.z > b.size.x))
            {
                return false;
            }

            var across = aAlongZ
                ? Mathf.Min(a.max.x, b.max.x) - Mathf.Max(a.min.x, b.min.x)
                : Mathf.Min(a.max.z, b.max.z) - Mathf.Max(a.min.z, b.min.z);
            var along = aAlongZ
                ? Mathf.Min(a.max.z, b.max.z) - Mathf.Max(a.min.z, b.min.z)
                : Mathf.Min(a.max.x, b.max.x) - Mathf.Max(a.min.x, b.min.x);
            var length = aAlongZ ? a.size.z : a.size.x;

            // Overlapping, or standing a few centimetres apart: either way two walls where one is meant.
            return across > -0.06f && along >= length * 0.5f;
        }

        /// <summary>
        /// Whether a standing wall crosses an entrance: it runs along the doorway's edge, stands
        /// within a few centimetres of it, and reaches into the doorway by more than the slack wall
        /// pieces have at their ends.
        /// </summary>
        public static bool Across(Bounds wall, RoomContainer.Entrance entrance)
        {
            const float nearEdge = 0.4f;
            const float endSlack = 0.15f;

            var alongX = entrance.Facing.y != 0;
            if (alongX != (wall.size.x >= wall.size.z))
            {
                return false;
            }

            var doorAlong = alongX ? entrance.Centre.x : entrance.Centre.y;
            var doorAcross = alongX ? entrance.Centre.y : entrance.Centre.x;
            var wallAcross = alongX ? wall.center.z : wall.center.x;

            if (Mathf.Abs(wallAcross - doorAcross) > nearEdge)
            {
                return false;
            }

            var from = (alongX ? wall.min.x : wall.min.z) + endSlack;
            var to = (alongX ? wall.max.x : wall.max.z) - endSlack;
            var half = entrance.Width * 0.5f;

            return Mathf.Min(to, doorAlong + half) - Mathf.Max(from, doorAlong - half) > 0.01f;
        }

        /// <summary>
        /// Whether the walls in <paramref name="standing"/> on the same line as <paramref name="wall"/>
        /// cover the whole of its length, together, bar the slack wall pieces have at their ends.
        /// </summary>
        /// <remarks>
        /// What decides that a wall can be turned off. "Overlaps at least half its length" turned a
        /// wall off where another only partly covered it, and left a gap for the rest. A wall only
        /// partly covered stays on: a slight overlap, never a hole. As in dojo-game-app.
        /// </remarks>
        public static bool Covered(Bounds wall, IEnumerable<Bounds> standing)
        {
            const float endSlack = 0.15f;
            const float join = 0.05f;

            var alongZ = wall.size.z > wall.size.x;
            var from = (alongZ ? wall.min.z : wall.min.x) + endSlack;
            var to = (alongZ ? wall.max.z : wall.max.x) - endSlack;

            var spans = new List<Vector2>();

            foreach (var other in standing)
            {
                if (OnSameLine(wall, other))
                {
                    spans.Add(alongZ ? new Vector2(other.min.z, other.max.z) : new Vector2(other.min.x, other.max.x));
                }
            }

            if (spans.Count == 0)
            {
                return false;
            }

            spans.Sort((a, b) => a.x.CompareTo(b.x));

            // Walk along the wall from its start, through every span that reaches it.
            var reached = from;

            foreach (var span in spans)
            {
                if (span.x > reached + join)
                {
                    break;
                }

                reached = Mathf.Max(reached, span.y);

                if (reached >= to)
                {
                    return true;
                }
            }

            return reached >= to;
        }

        /// <summary>Two walls running the same way, on the same level, overlapping or a few centimetres apart across their thickness.</summary>
        static bool OnSameLine(Bounds a, Bounds b)
        {
            if (a.min.y >= b.max.y - 0.05f || b.min.y >= a.max.y - 0.05f)
            {
                return false;
            }

            var aAlongZ = a.size.z > a.size.x;
            if (aAlongZ != (b.size.z > b.size.x))
            {
                return false;
            }

            var across = aAlongZ
                ? Mathf.Min(a.max.x, b.max.x) - Mathf.Max(a.min.x, b.min.x)
                : Mathf.Min(a.max.z, b.max.z) - Mathf.Max(a.min.z, b.min.z);

            return across > -0.06f;
        }

        /// <summary>One room's entrance on a floor.</summary>
        public struct Opening
        {
            public int Floor;
            public RoomContainer.Entrance Entrance;
        }

        /// <summary>
        /// Whether a turned-off wall on <paramref name="floor"/> has to stay off: another wall stands
        /// in its place, or one of the <paramref name="openings"/> on its floor runs through it.
        /// </summary>
        public static bool StaysHidden(Bounds wall, int floor, IEnumerable<Bounds> standing, IEnumerable<Opening> openings)
        {
            if (Covered(wall, standing))
            {
                return true;
            }

            foreach (var opening in openings)
            {
                if (opening.Floor == floor && Across(wall, opening.Entrance))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>A piece's drawn bounds, or the ones it had when it was turned off.</summary>
        public static bool TryBoundsOf(FurniturePiece piece, out Bounds bounds)
        {
            bounds = default;

            if (piece == null)
            {
                return false;
            }

            var hidden = piece.GetComponent<HiddenWall>();
            if (hidden != null && !piece.gameObject.activeSelf)
            {
                bounds = hidden.Bounds;
                return true;
            }

            var any = false;

            foreach (var drawn in piece.GetComponentsInChildren<Renderer>())
            {
                if (!any)
                {
                    bounds = drawn.bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(drawn.bounds);
                }
            }

            return any;
        }

        /// <summary>Turns a wall off: measured first, then its cells given up and the object switched off.</summary>
        public static void Hide(FurniturePiece piece, FurniturePlacementService service)
        {
            if (piece == null || IsHidden(piece))
            {
                return;
            }

            TryBoundsOf(piece, out var bounds);

            var marker = piece.GetComponent<HiddenWall>();
            if (marker == null)
            {
                marker = piece.gameObject.AddComponent<HiddenWall>();
            }

            marker.Bounds = bounds;
            service?.Release(piece);
            piece.gameObject.SetActive(false);
        }

        /// <summary>Switches a turned-off wall back on without claiming cells: for a room being picked up.</summary>
        public static void Unhide(FurniturePiece piece)
        {
            if (piece == null)
            {
                return;
            }

            var marker = piece.GetComponent<HiddenWall>();
            if (marker != null)
            {
                Object.DestroyImmediate(marker);
            }

            piece.gameObject.SetActive(true);
        }

        /// <summary>Turns a wall back on and has it hold its cells again.</summary>
        public static void Show(FurniturePiece piece, FurniturePlacementService service)
        {
            if (piece == null)
            {
                return;
            }

            Unhide(piece);
            piece.Settle();
            service?.Register(piece);
        }

        /// <summary>
        /// Turns off <paramref name="room"/>'s own walls that run along a wall already standing, so
        /// two rooms side by side show one wall instead of two through each other. How many.
        /// </summary>
        public static int HideShared(RoomContainer room, FurniturePlacementService service)
        {
            if (room == null)
            {
                return 0;
            }

            var standing = new List<Bounds>();

            foreach (var piece in Object.FindObjectsByType<FurniturePiece>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!piece.IsFloor && !piece.transform.IsChildOf(room.transform) && IsWall(piece) && TryBoundsOf(piece, out var bounds))
                {
                    standing.Add(bounds);
                }
            }

            var hidden = 0;

            foreach (var piece in room.Pieces(false))
            {
                if (!IsWall(piece) || !TryBoundsOf(piece, out var wall))
                {
                    continue;
                }

                // Only where the walls already standing cover it along its whole length.
                if (Covered(wall, standing))
                {
                    Hide(piece, service);
                    hidden++;
                }
            }

            if (hidden > 0)
            {
                Debug.Log("[Rooms] '" + room.RoomName + "' shares " + hidden + " wall piece(s) with what was already there; its own are turned off until that goes.");
            }

            return hidden;
        }

        /// <summary>
        /// Turns off the standing walls, other than <paramref name="room"/>'s own, that its entrances
        /// open through, so it opens into what it was put against. How many.
        /// </summary>
        public static int CutAt(RoomContainer room, FurniturePlacementService service)
        {
            if (room == null || room.Entrances.Count == 0)
            {
                return 0;
            }

            var floor = Storeys.StoreyOf(room.transform);
            var cut = 0;

            foreach (var piece in Object.FindObjectsByType<FurniturePiece>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (piece.IsFloor || piece.transform.IsChildOf(room.transform) || !IsWall(piece)
                    || Storeys.StoreyOf(piece.transform) != floor || !TryBoundsOf(piece, out var wall))
                {
                    continue;
                }

                foreach (var entrance in room.Entrances)
                {
                    if (Across(wall, entrance))
                    {
                        Hide(piece, service);
                        cut++;
                        break;
                    }
                }
            }

            if (cut > 0)
            {
                Debug.Log("[Rooms] '" + room.RoomName + "' opens into what it was placed against: " + cut + " wall piece(s) turned off at its entrance.");
            }

            return cut;
        }

        /// <summary>
        /// Turns off <paramref name="room"/>'s own walls that stand across another room's entrance
        /// on its floor, so that room still opens through. How many.
        /// </summary>
        /// <remarks>
        /// For a room put down - or put back - where a neighbour's entrance runs through its wall.
        /// Lifting it switched its own walls back on, and its own entrance cuts only ever reach
        /// other rooms' walls, so without this a neighbour's doorway closed again. As in dojo-game-app.
        /// </remarks>
        public static int CutByNeighbours(RoomContainer room, Transform world, FurniturePlacementService service)
        {
            if (room == null || world == null)
            {
                return 0;
            }

            var floor = Storeys.StoreyOf(room.transform);
            var entrances = new List<RoomContainer.Entrance>();

            foreach (var other in world.GetComponentsInChildren<RoomContainer>(false))
            {
                if (other != room && Storeys.StoreyOf(other.transform) == floor)
                {
                    entrances.AddRange(other.Entrances);
                }
            }

            if (entrances.Count == 0)
            {
                return 0;
            }

            var cut = 0;

            foreach (var piece in room.Pieces(false))
            {
                if (!IsWall(piece) || !TryBoundsOf(piece, out var wall))
                {
                    continue;
                }

                foreach (var entrance in entrances)
                {
                    if (Across(wall, entrance))
                    {
                        Hide(piece, service);
                        cut++;
                        break;
                    }
                }
            }

            if (cut > 0)
            {
                Debug.Log("[Rooms] '" + room.RoomName + "': " + cut + " wall piece(s) turned off where a neighbour's entrance opens through.");
            }

            return cut;
        }

        /// <summary>
        /// Looks again at every turned-off wall under <paramref name="world"/>, earlier rooms first,
        /// and turns back on each one nothing now stands in place of. How many came back on.
        /// Nothing under <paramref name="ignore"/> (a room being carried) counts, either way.
        /// </summary>
        /// <remarks>
        /// Run after a wall or a room is deleted, and when a room is picked up to be moved. A wall
        /// turned back on counts as standing for the ones after it, so two rooms that both hid
        /// against a wall now gone do not both come back on through each other.
        /// </remarks>
        public static int Reconsider(Transform world, FurniturePlacementService service, Transform ignore = null)
        {
            if (world == null)
            {
                return 0;
            }

            var hidden = new List<FurniturePiece>();
            var standing = new List<Bounds>();

            foreach (var piece in world.GetComponentsInChildren<FurniturePiece>(true))
            {
                if (piece == null || piece.IsFloor || (ignore != null && piece.transform.IsChildOf(ignore)))
                {
                    continue;
                }

                if (IsHidden(piece))
                {
                    // Not one inside a room on its way out.
                    var parent = piece.transform.parent;
                    if (parent == null || parent.gameObject.activeInHierarchy)
                    {
                        hidden.Add(piece);
                    }
                }
                else if (piece.gameObject.activeInHierarchy && IsWall(piece) && TryBoundsOf(piece, out var bounds))
                {
                    standing.Add(bounds);
                }
            }

            if (hidden.Count == 0)
            {
                return 0;
            }

            var openings = new List<Opening>();
            foreach (var room in world.GetComponentsInChildren<RoomContainer>(true))
            {
                if (!room.gameObject.activeInHierarchy || (ignore != null && room.transform.IsChildOf(ignore)))
                {
                    continue;   // on its way out, or in the air
                }

                var floor = Storeys.StoreyOf(room.transform);
                foreach (var entrance in room.Entrances)
                {
                    openings.Add(new Opening { Floor = floor, Entrance = entrance });
                }
            }

            hidden.Sort((a, b) => OrderOf(a).CompareTo(OrderOf(b)));

            var shown = 0;

            foreach (var piece in hidden)
            {
                var bounds = piece.GetComponent<HiddenWall>().Bounds;

                if (StaysHidden(bounds, Storeys.StoreyOf(piece.transform), standing, openings))
                {
                    continue;
                }

                Show(piece, service);
                standing.Add(bounds);
                shown++;
            }

            if (shown > 0)
            {
                Debug.Log("[Rooms] " + shown + " wall piece(s) back on: what stood in their place is gone.");
            }

            return shown;
        }

        /// <summary>The placement order of a piece's room, with loose pieces first.</summary>
        static int OrderOf(FurniturePiece piece)
        {
            var room = RoomContainer.Of(piece);
            return room != null ? room.Order : -1;
        }
    }
}
