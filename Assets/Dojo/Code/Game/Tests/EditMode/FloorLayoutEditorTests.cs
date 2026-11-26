using System.Collections.Generic;
using Dojo.Game.Placement;
using NUnit.Framework;
using UnityEngine;

namespace Dojo.Game.Tests.EditMode
{
    /// <summary>
    /// What the floor editor writes: the ground floor centred on the layout's origin in whole
    /// cells, rooms kept as rooms and moved with it, pieces outside the frame counted and dropped
    /// on request, and the frame that fits a layout. The pure parts, with no scene.
    /// </summary>
    public sealed class FloorLayoutEditorTests
    {
        static float Tile => FloorLayoutEditor.Tile;

        static WorldSave.SavedPiece Piece(float x, float z, string room = "", bool floor = true)
            => new WorldSave.SavedPiece { resource = "default/test_floor", position = new Vector3(x, 0f, z), isFloor = floor, room = room ?? string.Empty };

        static WorldSave Built(params WorldSave.SavedPiece[] pieces)
        {
            var built = new WorldSave { name = "built" };
            built.FloorAt(0).pieces.AddRange(pieces);
            return built;
        }

        static WorldSave.SavedRoom Room(string id, int floor, Vector3 position, float entranceX, float entranceZ)
        {
            var room = new WorldSave.SavedRoom { id = id, name = id, address = "default/Rooms/" + id, floor = floor, position = position };
            room.entrances.Add(new WorldSave.SavedOpening
            {
                kind = "entrance",
                centre = new WorldSave.GroundPoint(entranceX, entranceZ),
                facing = new WorldSave.GroundPoint(0f, -1f),
                width = 2.4f,
            });
            return room;
        }

        // ── The frame ───────────────────────────────────────────────────────────────────────

        [Test]
        public void FrameFor_IsCentredOnTheOrigin_InTiles()
        {
            var frame = FloorLayoutEditor.FrameFor(8, 6);

            Assert.That(frame.xMin, Is.EqualTo(-4f * Tile).Within(0.001f));
            Assert.That(frame.xMax, Is.EqualTo(4f * Tile).Within(0.001f));
            Assert.That(frame.yMin, Is.EqualTo(-3f * Tile).Within(0.001f));
            Assert.That(frame.yMax, Is.EqualTo(3f * Tile).Within(0.001f));
        }

        [Test]
        public void Inside_GivesHalfACellOfGrace_AtTheEdge()
        {
            var frame = FloorLayoutEditor.FrameFor(4, 4);
            var edge = 2f * Tile;

            Assert.IsTrue(FloorLayoutEditor.Inside(frame, new Vector3(edge, 0f, 0f)), "on the edge");
            Assert.IsTrue(FloorLayoutEditor.Inside(frame, new Vector3(edge + PlacementGrid.Step * 0.4f, 0f, 0f)), "within the grace");
            Assert.IsFalse(FloorLayoutEditor.Inside(frame, new Vector3(edge + PlacementGrid.Step, 0f, 0f)), "a cell out");
            Assert.IsFalse(FloorLayoutEditor.Inside(frame, new Vector3(0f, 0f, -edge - PlacementGrid.Step)), "a cell out, in depth");
        }

        [Test]
        public void FitSize_IsTheDefault_ForAnEmptyLayout()
        {
            int columns, rows;
            FloorLayoutEditor.FitSize(Built(), out columns, out rows);

            Assert.That(columns, Is.EqualTo(FloorLayoutEditor.DefaultColumns));
            Assert.That(rows, Is.EqualTo(FloorLayoutEditor.DefaultRows));
        }

        [Test]
        public void FitSize_CoversThePieces_InWholeTiles_NotBelowTheMinimum()
        {
            // The example layout: 8 tiles across, 6 deep, centred.
            var pieces = new List<WorldSave.SavedPiece>();
            for (var x = -3.5f; x <= 3.5f; x += 1f)
            {
                for (var z = -2.5f; z <= 2.5f; z += 1f)
                {
                    pieces.Add(Piece(x * Tile, z * Tile));
                }
            }

            int columns, rows;
            FloorLayoutEditor.FitSize(Built(pieces.ToArray()), out columns, out rows);
            Assert.That(columns, Is.EqualTo(8));
            Assert.That(rows, Is.EqualTo(6));

            FloorLayoutEditor.FitSize(Built(Piece(0f, 0f)), out columns, out rows);
            Assert.That(columns, Is.EqualTo(FloorLayoutEditor.MinSize), "one tile still gets the smallest frame");
            Assert.That(rows, Is.EqualTo(FloorLayoutEditor.MinSize));
        }

        // ── The layout file ─────────────────────────────────────────────────────────────────

        [Test]
        public void ToLayout_CentresThePieces_InWholeCells()
        {
            var built = Built(Piece(Tile, 0f), Piece(2f * Tile, 0f), Piece(3f * Tile, 0f));

            Vector3 shift;
            int outside;
            var layout = FloorLayoutEditor.ToLayout(built, FloorLayoutEditor.FrameFor(24, 24), false, out shift, out outside);

            Assert.That(shift, Is.EqualTo(new Vector3(2f * Tile, 0f, 0f)));
            Assert.That(layout.floors.Count, Is.EqualTo(1), "only the ground floor");
            Assert.That(layout.floors[0].pieces.Count, Is.EqualTo(3));
            Assert.That(layout.floors[0].pieces[0].position.x, Is.EqualTo(-Tile).Within(0.001f));
            Assert.That(layout.floors[0].pieces[1].position.x, Is.EqualTo(0f).Within(0.001f));
            Assert.That(layout.floors[0].pieces[2].position.x, Is.EqualTo(Tile).Within(0.001f));
            Assert.That(outside, Is.EqualTo(0));
        }

        [Test]
        public void ToLayout_KeepsRoomsAsRooms_AndMovesThemWithThePieces()
        {
            var built = Built(Piece(Tile, 0f, "r1"), Piece(2f * Tile, 0f, "r1"), Piece(3f * Tile, 0f, "r1"));
            built.rooms.Add(Room("r1", 0, new Vector3(2f * Tile, 0f, 0f), 2f * Tile, -0.6f));

            Vector3 shift;
            int outside;
            var layout = FloorLayoutEditor.ToLayout(built, FloorLayoutEditor.FrameFor(24, 24), false, out shift, out outside);

            Assert.That(layout.rooms.Count, Is.EqualTo(1));
            Assert.That(layout.rooms[0].id, Is.EqualTo("r1"));
            Assert.That(layout.rooms[0].position, Is.EqualTo(Vector3.zero));
            Assert.That(layout.rooms[0].entrances[0].centre.x, Is.EqualTo(0f).Within(0.001f));
            Assert.That(layout.rooms[0].entrances[0].centre.z, Is.EqualTo(-0.6f).Within(0.001f));
            Assert.That(layout.floors[0].pieces[0].room, Is.EqualTo("r1"), "the pieces still name their room");
        }

        [Test]
        public void ToLayout_CountsPiecesOutsideTheFrame_AndDropsThemOnlyWhenAsked_WithTheirEmptiedRoom()
        {
            var frame = FloorLayoutEditor.FrameFor(4, 4);
            var built = Built(Piece(0f, 0f), Piece(5f * Tile, 0f, "r2"));
            built.rooms.Add(Room("r2", 0, new Vector3(5f * Tile, 0f, 0f), 5f * Tile, -0.6f));

            Vector3 shift;
            int outside;
            var kept = FloorLayoutEditor.ToLayout(built, frame, false, out shift, out outside);

            Assert.That(outside, Is.EqualTo(1));
            Assert.That(kept.floors[0].pieces.Count, Is.EqualTo(2), "nothing dropped until asked");
            Assert.That(kept.rooms.Count, Is.EqualTo(1));

            built = Built(Piece(0f, 0f), Piece(5f * Tile, 0f, "r2"));
            built.rooms.Add(Room("r2", 0, new Vector3(5f * Tile, 0f, 0f), 5f * Tile, -0.6f));

            var dropped = FloorLayoutEditor.ToLayout(built, frame, true, out shift, out outside);

            Assert.That(outside, Is.EqualTo(1));
            Assert.That(dropped.floors[0].pieces.Count, Is.EqualTo(1));
            Assert.That(dropped.rooms, Is.Empty, "a room with none of its pieces left goes with them");
            Assert.That(shift, Is.EqualTo(Vector3.zero), "the one piece left is at the origin already");
        }

        [Test]
        public void ToLayout_APieceNamingARoomOnAnotherFloor_StandsOnItsOwn()
        {
            var built = Built(Piece(0f, 0f, "upstairs"));
            built.rooms.Add(Room("upstairs", 1, Vector3.zero, 0f, 0f));

            Vector3 shift;
            int outside;
            var layout = FloorLayoutEditor.ToLayout(built, FloorLayoutEditor.FrameFor(8, 8), false, out shift, out outside);

            Assert.That(layout.rooms, Is.Empty);
            Assert.That(layout.floors[0].pieces[0].room, Is.Empty);
        }

        [Test]
        public void ToLayout_MovesTheAreas_WithThePieces()
        {
            var built = Built(Piece(Tile, 0f), Piece(3f * Tile, 0f));
            built.FloorAt(0).blockAreas.Add(new WorldSave.SavedBlockArea
            {
                id = 7,
                name = "Sales",
                area = new List<Vector3> { new Vector3(2f * Tile, 0f, 0f) },
                areaCellSize = PlacementGrid.Step,
            });

            Vector3 shift;
            int outside;
            var layout = FloorLayoutEditor.ToLayout(built, FloorLayoutEditor.FrameFor(24, 24), false, out shift, out outside);

            Assert.That(layout.floors[0].blockAreas.Count, Is.EqualTo(1));
            Assert.That(layout.floors[0].blockAreas[0].area[0], Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void ToLayout_LeavesOtherFloorsOut()
        {
            var built = Built(Piece(0f, 0f));
            built.FloorAt(1).pieces.Add(Piece(0f, 0f));

            Vector3 shift;
            int outside;
            var layout = FloorLayoutEditor.ToLayout(built, FloorLayoutEditor.FrameFor(8, 8), false, out shift, out outside);

            Assert.That(layout.floors.Count, Is.EqualTo(1));
            Assert.That(layout.PieceCount(), Is.EqualTo(1));
        }
    }
}
