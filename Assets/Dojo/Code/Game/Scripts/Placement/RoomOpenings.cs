using System;
using System.Collections.Generic;
using UnityEngine;
using Opening = Dojo.Game.Placement.WorldSave.SavedOpening;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// A room's doors worked out on the ground: where a wall makes one, which gaps in the walls
    /// already are one, and whether a room's openings keep the rules of the room file format.
    /// </summary>
    /// <remarks>
    /// Follows <c>Documentation/Platform/Room-File-Format.md</c> in dojo-game-app, the one thing
    /// the two projects share. Everything here is plain geometry over the room's floor tiles and
    /// walls - rectangles and segments on the ground - so the room editor can feed it what stands
    /// in the scene, and an editor tool what a room file says, and both get the same answers.
    /// <para>
    /// Across the ground +z is north and +x is east, so an opening facing (0,-1) is on the south side.
    /// </para>
    /// </remarks>
    public static class RoomOpenings
    {
        public const string Entrance = "entrance";
        public const string Exit = "exit";

        /// <summary>How far an opening's centre may be from the floor's edge: rule 1.</summary>
        public const float EdgeTolerance = 0.1f;

        /// <summary>How far in and out from an edge is looked at, to tell floor from no floor.</summary>
        const float Probe = 0.15f;

        /// <summary>How far a wall's middle may stand from the floor's edge and still be a wall of that edge.</summary>
        const float WallReach = 0.3f;

        /// <summary>
        /// How much of each end of a wall may reach into an opening without crossing it. A wall
        /// piece is a little longer than the step it is laid at, so neighbours overlap at their ends.
        /// </summary>
        const float EndSlack = 0.15f;

        /// <summary>The smallest gap in the walls offered as an opening.</summary>
        const float SmallestGap = 1f;

        /// <summary>The grid rooms are built on. A wall piece's width is a whole number of cells.</summary>
        const float Grid = 0.25f;

        /// <summary>Within this, two sides or two ends are the same place.</summary>
        const float Same = 0.05f;

        /// <summary>A wall on the ground: its middle, which way it runs, and half its width as laid.</summary>
        public readonly struct Wall
        {
            public readonly Vector2 Centre;

            /// <summary>True when the wall runs along x - a north or south wall.</summary>
            public readonly bool AlongX;

            /// <summary>Half the width it is laid at: its length on the ground, down to the grid.</summary>
            public readonly float Half;

            /// <summary>Whatever the caller knows it by.</summary>
            public readonly int Id;

            public Wall(Vector2 centre, bool alongX, float half, int id)
            {
                Centre = centre;
                AlongX = alongX;
                Half = half;
                Id = id;
            }

            public float Along => AlongX ? Centre.x : Centre.y;
            public float Across => AlongX ? Centre.y : Centre.x;
        }

        /// <summary>A wall from what it covers on the ground.</summary>
        public static Wall WallFrom(Vector2 centre, Vector2 size, int id)
        {
            var alongX = size.x >= size.y;
            return new Wall(centre, alongX, LaidWidth(alongX ? size.x : size.y) * 0.5f, id);
        }

        /// <summary>
        /// The width a wall piece is laid at, from how long it measures: down to the grid. The
        /// default pack's wall measures 2.68 m and is laid every 2.5 m.
        /// </summary>
        public static float LaidWidth(float measured) => Mathf.Max(Grid, Mathf.Floor((measured + 0.01f) / Grid) * Grid);

        /// <summary>
        /// Fills <paramref name="tiles"/> and <paramref name="walls"/> from a room file's floor, with
        /// <paramref name="sizeOf"/> saying how much ground a piece's prefab covers unturned (zero
        /// when it is not known). Floor tiles are the pieces marked <c>isFloor</c>; walls are the
        /// pieces whose resource names a wall, as the format has it.
        /// </summary>
        public static void Measure(WorldSave.SavedFloor floor, Func<string, Vector2> sizeOf, List<Rect> tiles, List<Wall> walls)
        {
            if (floor == null || floor.pieces == null)
            {
                return;
            }

            for (var i = 0; i < floor.pieces.Count; i++)
            {
                var piece = floor.pieces[i];
                var wall = IsWall(piece.resource);

                if (!piece.isFloor && !wall)
                {
                    continue;
                }

                var size = sizeOf(piece.resource);

                if (size == Vector2.zero)
                {
                    continue;
                }

                if ((PlacementGrid.Normalise(piece.rotationSteps) & 1) == 1)
                {
                    size = new Vector2(size.y, size.x);
                }

                var centre = new Vector2(piece.position.x, piece.position.z);

                if (piece.isFloor)
                {
                    tiles.Add(new Rect(centre - size * 0.5f, size));
                }
                else
                {
                    walls.Add(WallFrom(centre, size, i));
                }
            }
        }

        /// <summary>True when a piece's resource names a wall: how the format tells walls apart.</summary>
        public static bool IsWall(string resource)
            => !string.IsNullOrEmpty(resource) && resource.IndexOf("wall", StringComparison.OrdinalIgnoreCase) >= 0;

        // ── Facing ──────────────────────────────────────────────────────────────────────────

        /// <summary>True for exactly one of (1,0), (-1,0), (0,1), (0,-1): rule 5.</summary>
        public static bool IsFacing(Vector2 facing)
        {
            var x = Mathf.Abs(facing.x);
            var z = Mathf.Abs(facing.y);
            return (Mathf.Abs(x - 1f) < 0.001f && z < 0.001f) || (Mathf.Abs(z - 1f) < 0.001f && x < 0.001f);
        }

        /// <summary>The side of the room a facing is on: north, south, east or west.</summary>
        public static string SideOf(Vector2 facing)
        {
            if (facing.y > 0.5f) return "north";
            if (facing.y < -0.5f) return "south";
            if (facing.x > 0.5f) return "east";
            return "west";
        }

        // An edge facing north or south runs along x.
        static bool RunsAlongX(Vector2 facing) => Mathf.Abs(facing.y) > 0.5f;
        static float AlongOf(Vector2 point, Vector2 facing) => RunsAlongX(facing) ? point.x : point.y;
        static float AcrossOf(Vector2 point, Vector2 facing) => RunsAlongX(facing) ? point.y : point.x;
        static Vector2 At(float along, float across, Vector2 facing)
            => RunsAlongX(facing) ? new Vector2(along, across) : new Vector2(across, along);

        static readonly Vector2[] Facings = { new Vector2(0f, -1f), new Vector2(0f, 1f), new Vector2(1f, 0f), new Vector2(-1f, 0f) };

        // ── The floor's edge ────────────────────────────────────────────────────────────────

        static bool OnFloor(List<Rect> tiles, Vector2 point)
        {
            foreach (var tile in tiles)
            {
                if (tile.Contains(point))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The coordinate across the ground of the tile side that faces <paramref name="facing"/>.</summary>
        static float SideOf(Rect tile, Vector2 facing)
        {
            if (RunsAlongX(facing))
            {
                return facing.y > 0f ? tile.yMax : tile.yMin;
            }

            return facing.x > 0f ? tile.xMax : tile.xMin;
        }

        /// <summary>
        /// The floor's edge on the side <paramref name="facing"/> points to, beside
        /// <paramref name="point"/>: where floor ends and none begins. False when there is no floor
        /// just inside the point, or there is floor just outside the edge.
        /// </summary>
        public static bool TryEdge(List<Rect> tiles, Vector2 point, Vector2 facing, out float edge)
        {
            edge = 0f;
            var inside = point - facing * Probe;

            foreach (var tile in tiles)
            {
                if (!tile.Contains(inside))
                {
                    continue;
                }

                edge = SideOf(tile, facing);
                var onEdge = At(AlongOf(point, facing), edge, facing);

                return !OnFloor(tiles, onEdge + facing * Probe);
            }

            return false;
        }

        // ── Making openings ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// The opening a wall on the floor's edge makes when it is taken out: as wide as the wall is
        /// laid, centred on it, on the edge, facing out. False for a wall that is not on the edge -
        /// one with floor on both sides, or none on either.
        /// </summary>
        public static bool TryAtWall(List<Rect> tiles, Wall wall, string kind, out Opening opening)
        {
            opening = null;

            foreach (var facing in Facings)
            {
                if (RunsAlongX(facing) != wall.AlongX)
                {
                    continue;
                }

                float edge;

                if (!TryEdge(tiles, wall.Centre, facing, out edge) || Mathf.Abs(wall.Across - edge) > WallReach)
                {
                    continue;
                }

                opening = new Opening
                {
                    kind = kind,
                    centre = WorldSave.GroundPoint.From(At(wall.Along, edge, facing)),
                    facing = WorldSave.GroundPoint.From(facing),
                    width = wall.Half * 2f,
                };

                return true;
            }

            return false;
        }

        /// <summary>
        /// True when <paramref name="opening"/> stands on an edge of <paramref name="tile"/>: on the
        /// side it faces out of, along some of that side.
        /// </summary>
        public static bool OnEdgeOf(Rect tile, Opening opening)
        {
            var facing = opening.facing.ToVector2();

            if (!IsFacing(facing))
            {
                return false;
            }

            var centre = opening.centre.ToVector2();

            if (Mathf.Abs(AcrossOf(centre, facing) - SideOf(tile, facing)) > EdgeTolerance)
            {
                return false;
            }

            var along = AlongOf(centre, facing);
            var from = RunsAlongX(facing) ? tile.xMin : tile.yMin;
            var to = RunsAlongX(facing) ? tile.xMax : tile.yMax;

            return Mathf.Min(along + opening.width * 0.5f, to) - Mathf.Max(along - opening.width * 0.5f, from) > 0.01f;
        }

        /// <summary>How far a point on the ground is from an opening's span.</summary>
        public static float DistanceTo(Opening opening, Vector2 point)
        {
            var centre = opening.centre.ToVector2();
            var along = new Vector2(Mathf.Abs(opening.facing.z), Mathf.Abs(opening.facing.x));
            var half = opening.width * 0.5f;
            var t = Mathf.Clamp(Vector2.Dot(point - centre, along), -half, half);

            return Vector2.Distance(point, centre + along * t);
        }

        /// <summary>
        /// An opening <paramref name="width"/> wide inside <paramref name="gap"/>, as near
        /// <paramref name="point"/> as it fits, on the grid. Centred on the gap when the gap is
        /// narrower than it.
        /// </summary>
        public static Opening Within(Opening gap, Vector2 point, float width)
        {
            var facing = gap.facing.ToVector2();
            var middle = AlongOf(gap.centre.ToVector2(), facing);
            var along = middle;

            if (gap.width >= width)
            {
                var room = (gap.width - width) * 0.5f;
                along = Mathf.Clamp(Mathf.Round(AlongOf(point, facing) / Grid) * Grid, middle - room, middle + room);
            }

            return new Opening
            {
                kind = gap.kind,
                centre = WorldSave.GroundPoint.From(At(along, AcrossOf(gap.centre.ToVector2(), facing), facing)),
                facing = gap.facing,
                width = width,
            };
        }

        /// <summary>True when <paramref name="wall"/> stands on the edge <paramref name="opening"/> is on.</summary>
        static bool OnSameEdge(Wall wall, Opening opening)
        {
            var facing = opening.facing.ToVector2();
            return wall.AlongX == RunsAlongX(facing)
                && Mathf.Abs(wall.Across - AcrossOf(opening.centre.ToVector2(), facing)) <= WallReach;
        }

        /// <summary>True when a wall stands across an opening: rule 2.</summary>
        public static bool Crosses(Wall wall, Opening opening)
        {
            if (!OnSameEdge(wall, opening))
            {
                return false;
            }

            var along = AlongOf(opening.centre.ToVector2(), opening.facing.ToVector2());
            var half = opening.width * 0.5f;
            var reach = Mathf.Max(0f, wall.Half - EndSlack);

            return Mathf.Min(along + half, wall.Along + reach) - Mathf.Max(along - half, wall.Along - reach) > 0.01f;
        }

        /// <summary>
        /// True when two openings may not both be where they are: rule 4, as the creator keeps it.
        /// Two of one kind may not share any of an edge; an entrance and an exit may, up to standing
        /// on the very same spot - one door, walked in and out.
        /// </summary>
        /// <remarks>
        /// <c>Room-File-Format.md</c> rule 4, as of version 2 (16 Oct 2026). Version 1 had no two
        /// openings overlap at all; it was changed to match this.
        /// </remarks>
        public static bool Clash(Opening a, Opening b) => a.kind == b.kind && Overlap(a, b);

        /// <summary>
        /// Why another opening of <paramref name="kind"/> may not be added, or null when it may: a
        /// room has exactly one entrance and one exit.
        /// </summary>
        public static string OneOfEach(List<Opening> openings, string kind)
        {
            if (!openings.Exists(o => o != null && o.kind == kind))
            {
                return null;
            }

            return kind == Entrance
                ? "The room has its entrance already - only one is allowed. Drag it to move it, or click it with ENTRANCE to take it off first."
                : "The room has its exit already - only one is allowed. Drag it to move it, or click it with EXIT to take it off first.";
        }

        /// <summary>True when two openings share some of one edge, whatever their kinds.</summary>
        public static bool Overlap(Opening a, Opening b)
        {
            var facing = a.facing.ToVector2();

            if (Vector2.Distance(facing, b.facing.ToVector2()) > 0.01f)
            {
                return false;
            }

            if (Mathf.Abs(AcrossOf(a.centre.ToVector2(), facing) - AcrossOf(b.centre.ToVector2(), facing)) > EdgeTolerance)
            {
                return false;
            }

            var alongA = AlongOf(a.centre.ToVector2(), facing);
            var alongB = AlongOf(b.centre.ToVector2(), facing);

            return Mathf.Min(alongA + a.width * 0.5f, alongB + b.width * 0.5f)
                - Mathf.Max(alongA - a.width * 0.5f, alongB - b.width * 0.5f) > 0.01f;
        }

        /// <summary>
        /// The gaps in the walls along the floor's edge that no opening covers yet, as openings
        /// without a kind: what the room editor offers, and what the default rooms were marked from.
        /// </summary>
        /// <remarks>
        /// Edge by edge: every tile side with no floor beyond it, joined where tiles meet, less what
        /// the walls on it cover as laid and what the openings already cover. What is left, a metre
        /// or more of it, is a gap, as wide as it is down to the grid.
        /// </remarks>
        public static List<Opening> Gaps(List<Rect> tiles, List<Wall> walls, List<Opening> openings)
        {
            var gaps = new List<Opening>();

            foreach (var facing in Facings)
            {
                // The open sides facing this way, by where across the ground their edge is.
                var lines = new List<KeyValuePair<float, List<Vector2>>>();

                foreach (var tile in tiles)
                {
                    var edge = SideOf(tile, facing);
                    var open = new List<Vector2>
                    {
                        RunsAlongX(facing) ? new Vector2(tile.xMin, tile.xMax) : new Vector2(tile.yMin, tile.yMax),
                    };

                    // Less where another tile carries on beyond this side.
                    foreach (var other in tiles)
                    {
                        if (other != tile && Mathf.Abs(SideOf(other, -facing) - edge) < Same)
                        {
                            Subtract(open, RunsAlongX(facing) ? new Vector2(other.xMin, other.xMax) : new Vector2(other.yMin, other.yMax));
                        }
                    }

                    var line = lines.Find(pair => Mathf.Abs(pair.Key - edge) < Same);

                    if (line.Value == null)
                    {
                        line = new KeyValuePair<float, List<Vector2>>(edge, new List<Vector2>());
                        lines.Add(line);
                    }

                    line.Value.AddRange(open);
                }

                foreach (var line in lines)
                {
                    var open = Merge(line.Value);

                    foreach (var wall in walls)
                    {
                        if (wall.AlongX == RunsAlongX(facing) && Mathf.Abs(wall.Across - line.Key) <= WallReach)
                        {
                            Subtract(open, new Vector2(wall.Along - wall.Half, wall.Along + wall.Half));
                        }
                    }

                    foreach (var opening in openings)
                    {
                        var f = opening.facing.ToVector2();

                        if (Vector2.Distance(f, facing) < 0.01f
                            && Mathf.Abs(AcrossOf(opening.centre.ToVector2(), f) - line.Key) <= EdgeTolerance)
                        {
                            var along = AlongOf(opening.centre.ToVector2(), f);
                            Subtract(open, new Vector2(along - opening.width * 0.5f, along + opening.width * 0.5f));
                        }
                    }

                    foreach (var span in open)
                    {
                        var length = span.y - span.x;

                        if (length < SmallestGap)
                        {
                            continue;
                        }

                        gaps.Add(new Opening
                        {
                            kind = string.Empty,
                            centre = WorldSave.GroundPoint.From(At((span.x + span.y) * 0.5f, line.Key, facing)),
                            facing = WorldSave.GroundPoint.From(facing),
                            width = Mathf.Round(length / Grid) * Grid,
                        });
                    }
                }
            }

            return gaps;
        }

        // ── The rules ───────────────────────────────────────────────────────────────────────

        /// <summary>
        /// True when the openings keep every rule of the room file format. Otherwise
        /// <paramref name="problem"/> says which rule failed, and for which opening.
        /// </summary>
        public static bool Validate(List<Rect> tiles, List<Wall> walls, List<Opening> openings, out string problem)
        {
            var failures = new List<string>();
            var entrances = 0;
            var exits = 0;

            for (var i = 0; i < openings.Count; i++)
            {
                var opening = openings[i];
                var facing = opening.facing.ToVector2();
                var named = Describe(opening);

                if (opening.kind == Entrance)
                {
                    entrances++;
                }
                else if (opening.kind == Exit)
                {
                    exits++;
                }
                else
                {
                    failures.Add(named + " is neither an entrance nor an exit.");
                }

                if (!IsFacing(facing))
                {
                    failures.Add(named + " does not face north, south, east or west (rule 5).");
                    continue;
                }

                if (opening.width <= 0f)
                {
                    failures.Add(named + " has no width.");
                    continue;
                }

                float edge;
                if (!TryEdge(tiles, opening.centre.ToVector2(), facing, out edge)
                    || Mathf.Abs(AcrossOf(opening.centre.ToVector2(), facing) - edge) > EdgeTolerance)
                {
                    failures.Add(named + " is not on the edge of the floor (rule 1). Lay floor up to it, or take it out.");
                }

                foreach (var wall in walls)
                {
                    if (Crosses(wall, opening))
                    {
                        failures.Add(named + " has a wall across it (rule 2). Take the wall out, or the opening.");
                        break;
                    }
                }

                for (var j = i + 1; j < openings.Count; j++)
                {
                    if (Clash(opening, openings[j]))
                    {
                        failures.Add(named + " overlaps " + Describe(openings[j]) + " (rule 4). An entrance and an exit may share a spot; two "
                            + opening.kind + "s may not.");
                    }
                }
            }

            // One of each, exactly: Room-File-Format.md rule 3, as of version 2 (version 1 asked only
            // for an entrance). They may share a spot - see Clash.
            if (exits == 0)
            {
                failures.Insert(0, "The room has no exit (rule 3). Use EXIT on a wall of the floor's edge, on a marked gap, or on the entrance to share its spot.");
            }
            else if (exits > 1)
            {
                failures.Insert(0, "The room has " + exits + " exits; a room has exactly one (rule 3). Take the others off with EXIT.");
            }

            if (entrances == 0)
            {
                failures.Insert(0, "The room has no entrance (rule 3). Use ENTRANCE on a wall of the floor's edge, or on a marked gap.");
            }
            else if (entrances > 1)
            {
                failures.Insert(0, "The room has " + entrances + " entrances; a room has exactly one (rule 3). Take the others off with ENTRANCE.");
            }

            problem = failures.Count > 0 ? string.Join(" ", failures) : null;
            return failures.Count == 0;
        }

        /// <summary>"The entrance on the south side", for messages.</summary>
        public static string Describe(Opening opening)
        {
            var kind = string.IsNullOrEmpty(opening.kind) ? "opening" : opening.kind;
            return "The " + kind + " on the " + SideOf(opening.facing.ToVector2()) + " side";
        }

        // ── Spans along a line ──────────────────────────────────────────────────────────────

        /// <summary>Joins spans (x from, y to) that touch or overlap.</summary>
        static List<Vector2> Merge(List<Vector2> spans)
        {
            spans.Sort((a, b) => a.x.CompareTo(b.x));
            var merged = new List<Vector2>();

            foreach (var span in spans)
            {
                if (merged.Count > 0 && span.x <= merged[merged.Count - 1].y + Same)
                {
                    var last = merged[merged.Count - 1];
                    merged[merged.Count - 1] = new Vector2(last.x, Mathf.Max(last.y, span.y));
                }
                else
                {
                    merged.Add(span);
                }
            }

            return merged;
        }

        /// <summary>Takes <paramref name="cut"/> out of every span in <paramref name="spans"/>.</summary>
        static void Subtract(List<Vector2> spans, Vector2 cut)
        {
            for (var i = spans.Count - 1; i >= 0; i--)
            {
                var span = spans[i];

                if (cut.y <= span.x || cut.x >= span.y)
                {
                    continue;
                }

                spans.RemoveAt(i);

                if (cut.y < span.y)
                {
                    spans.Insert(i, new Vector2(cut.y, span.y));
                }

                if (cut.x > span.x)
                {
                    spans.Insert(i, new Vector2(span.x, cut.x));
                }
            }
        }
    }
}
