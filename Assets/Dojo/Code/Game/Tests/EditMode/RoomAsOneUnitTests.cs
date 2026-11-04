using System.Collections.Generic;
using Dojo.Game.Placement;
using NUnit.Framework;
using UnityEngine;

namespace Dojo.Game.Tests.EditMode
{
    /// <summary>
    /// A placed room as one unit, as in dojo-game-app: its pieces grouped under one container, saved
    /// and loaded as a room with its order, areas and entrances, and the shared-wall rule - where
    /// two rooms meet one wall shows, and the other comes back on when that one goes.
    /// </summary>
    public sealed class RoomAsOneUnitTests
    {
        readonly List<GameObject> made = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in made)
            {
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                }
            }

            made.Clear();
        }

        GameObject Made(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            if (parent == null)
            {
                made.Add(go);
            }

            return go;
        }

        // ── The container ───────────────────────────────────────────────────────────────────

        [Test]
        public void APlacedRoom_StandsUnderOneContainer_OnItsFloor()
        {
            var floor = Made("Floor 1").transform;

            var room = RoomContainer.Create(floor, "room-1", "Sales Room", "rooms/sales_room", 0, new Vector3(4f, 0f, 2f));

            Assert.AreEqual("Room - Sales Room", room.name);
            Assert.AreSame(floor, room.transform.parent);
            Assert.AreEqual(new Vector3(4f, 0f, 2f), room.transform.position);
            Assert.AreEqual("rooms/sales_room", room.Address);
        }

        [Test]
        public void ThePlacementOrder_CountsOn()
        {
            var world = Made("World").transform;
            var floor = Made("Floor 1", world).transform;

            var first = RoomContainer.Create(floor, "a", "A", string.Empty, Vector3.zero);
            var second = RoomContainer.Create(floor, "b", "B", string.Empty, Vector3.zero);

            Assert.AreEqual(0, first.Order);
            Assert.AreEqual(1, second.Order, "placed later: the one that turns off its shared walls");
        }

        [Test]
        public void APiece_KnowsItsRoom_AndALoosePieceHasNone()
        {
            var floor = Made("Floor 1").transform;
            var room = RoomContainer.Create(floor, "room-1", "Sales Room", string.Empty, 0, Vector3.zero);
            var desk = Made("Desk", room.transform);
            var lamp = Made("Lamp", floor);

            Assert.AreSame(room, RoomContainer.Of(desk.transform));
            Assert.IsNull(RoomContainer.Of(lamp.transform));
        }

        [Test]
        public void ARoom_RemembersItsAreas_AndEntrances()
        {
            var room = RoomContainer.Create(Made("Floor 1").transform, "room-1", "Sales Room", string.Empty, 0, Vector3.zero);

            room.SetAreas(new[] { 4, 9 });
            room.SetEntrances(new[] { Entrance(1f, 5f, 0, 1, 1.2f) });

            CollectionAssert.AreEqual(new[] { 4, 9 }, room.AreaIds);
            Assert.AreEqual(1, room.Entrances.Count);
        }

        // ── Shared walls ────────────────────────────────────────────────────────────────────

        // A wall 2.68 m long along x, 0.2 m thick, 3 m tall, at height base.
        static Bounds WallAlongX(float x, float z, float floorBase = 0f) =>
            new Bounds(new Vector3(x, floorBase + 1.5f, z), new Vector3(2.68f, 3f, 0.2f));

        static RoomContainer.Entrance Entrance(float x, float z, int facingX, int facingZ, float width) =>
            new RoomContainer.Entrance { Centre = new Vector2(x, z), Facing = new Vector2Int(facingX, facingZ), Width = width };

        [Test]
        public void TwoWallsOnOneLine_AreOneWallTooMany()
        {
            Assert.IsTrue(RoomWalls.Duplicates(WallAlongX(0f, 5f), WallAlongX(0.1f, 5.05f)), "overlapping");
            Assert.IsTrue(RoomWalls.Duplicates(WallAlongX(0f, 5f), WallAlongX(0f, 5.24f)), "a few centimetres apart");
            Assert.IsFalse(RoomWalls.Duplicates(WallAlongX(0f, 5f), WallAlongX(0f, 6f)), "a metre apart");
            Assert.IsFalse(RoomWalls.Duplicates(WallAlongX(0f, 5f), WallAlongX(2.5f, 5f)), "end to end");
        }

        [Test]
        public void AWallOnTheFloorAbove_OrAcross_IsNotTheSameWall()
        {
            Assert.IsFalse(RoomWalls.Duplicates(WallAlongX(0f, 5f), WallAlongX(0f, 5f, 3.5f)));

            var across = new Bounds(new Vector3(0f, 1.5f, 5f), new Vector3(0.2f, 3f, 2.68f));
            Assert.IsFalse(RoomWalls.Duplicates(WallAlongX(0f, 5f), across));
        }

        [Test]
        public void AnEntrance_CutsTheWallItOpensThrough_AndNoOther()
        {
            var door = Entrance(1.34f, 5.1f, 0, 1, 1.2f);

            Assert.IsTrue(RoomWalls.Across(WallAlongX(1.34f, 5f), door), "the wall along its edge");
            Assert.IsFalse(RoomWalls.Across(WallAlongX(1.34f, 6f), door), "a wall a metre away");
            Assert.IsFalse(RoomWalls.Across(WallAlongX(6f, 5f), door), "a wall further along");
            Assert.IsFalse(RoomWalls.Across(new Bounds(new Vector3(1.34f, 1.5f, 5f), new Vector3(0.2f, 3f, 2.68f)), door),
                "a wall running across the doorway");
        }

        [Test]
        public void ATurnedOffWall_StaysOff_WhileAnotherStandsInItsPlace()
        {
            var mine = WallAlongX(0f, 5f);
            var none = new List<RoomWalls.Opening>();

            Assert.IsTrue(RoomWalls.StaysHidden(mine, 0, new[] { WallAlongX(0.1f, 5.05f) }, none));
            Assert.IsFalse(RoomWalls.StaysHidden(mine, 0, new Bounds[0], none), "the wall in its place is gone: back on");
        }

        [Test]
        public void ATurnedOffWall_StaysOff_WhileAnEntranceRunsThroughIt()
        {
            var cut = WallAlongX(1.34f, 5f);
            var openings = new[] { new RoomWalls.Opening { Floor = 0, Entrance = Entrance(1.34f, 5.1f, 0, 1, 1.2f) } };

            Assert.IsTrue(RoomWalls.StaysHidden(cut, 0, new Bounds[0], openings));
            Assert.IsFalse(RoomWalls.StaysHidden(cut, 1, new Bounds[0], openings), "an entrance on another floor");
            Assert.IsFalse(RoomWalls.StaysHidden(cut, 0, new Bounds[0], new RoomWalls.Opening[0]),
                "the room with the entrance is gone: back on");
        }

        // ── Saving and loading ──────────────────────────────────────────────────────────────

        [Test]
        public void TheSave_KeepsTheRoom_ItsOrderAreasAndEntrances_AndTurnedOffWalls()
        {
            var save = new WorldSave { name = "World1" };
            var room = new WorldSave.SavedRoom
            {
                id = "room-abc",
                name = "Sales Room",
                address = "rooms/sales_room",
                floor = 1,
                position = new Vector3(4f, 0f, 2f),
                order = 3,
            };
            room.areaIds.Add(7);
            room.entrances.Add(new WorldSave.SavedOpening
            {
                kind = RoomOpenings.Entrance,
                centre = new WorldSave.GroundPoint(1.34f, 5.1f),
                facing = new WorldSave.GroundPoint(0f, 1f),
                width = 1.2f,
            });
            save.rooms.Add(room);
            save.FloorAt(1).pieces.Add(new WorldSave.SavedPiece { resource = "default/desk", room = "room-abc", scale = Vector3.one });
            save.FloorAt(1).pieces.Add(new WorldSave.SavedPiece { resource = "default/wall", room = "room-abc", hidden = true, scale = Vector3.one });

            var back = JsonUtility.FromJson<WorldSave>(JsonUtility.ToJson(save)).Normalised();

            Assert.AreEqual(1, back.rooms.Count);
            Assert.AreEqual(3, back.rooms[0].order);
            CollectionAssert.AreEqual(new[] { 7 }, back.rooms[0].areaIds);
            Assert.AreEqual("entrance", back.rooms[0].entrances[0].kind);
            Assert.AreEqual(1.34f, back.rooms[0].entrances[0].centre.x, 0.0001f);
            Assert.AreEqual(1.2f, back.rooms[0].entrances[0].width, 0.0001f);
            Assert.IsTrue(back.FloorAt(1).pieces[1].hidden, "a turned-off wall is saved as one");
        }

        [Test]
        public void AWorldSavedBeforeThis_LoadsWithNoOrderAreasOrTurnedOffWalls()
        {
            var back = JsonUtility.FromJson<WorldSave>(
                "{\"name\":\"Old\",\"rooms\":[{\"id\":\"r\",\"name\":\"Room\",\"floor\":0}],"
                + "\"floors\":[{\"index\":0,\"pieces\":[{\"resource\":\"default/wall\",\"room\":\"r\"}]}]}").Normalised();

            Assert.AreEqual(1, back.rooms.Count);
            Assert.AreEqual(0, back.rooms[0].areaIds.Count, "no areas remembered: worked out by shape when lifted");
            Assert.AreEqual(0, back.rooms[0].entrances.Count);
            Assert.IsFalse(back.FloorAt(0).pieces[0].hidden);
        }

        [Test]
        public void Rooms_AreKeptInTheOrderTheyWerePlaced()
        {
            var save = new WorldSave();
            save.rooms.Add(new WorldSave.SavedRoom { id = "b", order = 2 });
            save.rooms.Add(null);
            save.rooms.Add(new WorldSave.SavedRoom { id = "a", order = 0 });
            save.rooms.Add(new WorldSave.SavedRoom { id = string.Empty, order = 1 });

            save.Normalised();

            CollectionAssert.AreEqual(new[] { "a", "b" }, save.rooms.ConvertAll(r => r.id), "no blanks, earliest first");
        }
    }
}
