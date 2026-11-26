using System;
using System.Collections.Generic;
using System.IO;
using Dojo.Game.Placement;
using NUnit.Framework;
using UnityEngine;

namespace Dojo.Game.Tests.EditMode
{
    /// <summary>
    /// Placing a floor layout: its footprint from its file and at every turn, and the rule for what
    /// a layout leaves alone - a named area or furniture, never a floor tile or a wall it may reach
    /// over. The pure parts, with no scene.
    /// </summary>
    public sealed class FloorLayoutPlacerTests
    {
        string folder;

        [SetUp]
        public void SetUp()
        {
            folder = Path.Combine(Path.GetTempPath(), "DojoFloorLayoutPlacerTests_" + Guid.NewGuid().ToString("N")).Replace('\\', '/');
            Directory.CreateDirectory(folder);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }

        static WorldSave Layout(string name, params Vector3[] at)
        {
            var layout = new WorldSave { name = name };

            foreach (var position in at)
            {
                layout.FloorAt(0).pieces.Add(new WorldSave.SavedPiece { resource = "default/test_floor", position = position, isFloor = true });
            }

            return layout;
        }

        [Test]
        public void Prepare_ReadsTheLayout_AndMeasuresItsFootprint_WithTheWallMargin()
        {
            var store = new CustomFloorLayouts(null, folder);
            var address = store.Save("Corner", Layout("Corner", new Vector3(-1.2f, 0f, 0f), new Vector3(1.2f, 0f, 0.6f)), null);
            var catalog = new FloorLayoutCatalog(null, store);
            var placer = new FloorLayoutPlacer(catalog, null, null, null, null, null, null);

            string problem;
            var plan = placer.Prepare(catalog.All()[0], out problem);

            Assert.That(plan, Is.Not.Null, problem);
            Assert.That(plan.Entry.address, Is.EqualTo(address));
            Assert.That(plan.Save.floors[0].pieces.Count, Is.EqualTo(2));
            Assert.That(plan.Footprint.xMin, Is.EqualTo(-1.28f).Within(0.001f), "half a wall past the outermost piece");
            Assert.That(plan.Footprint.xMax, Is.EqualTo(1.28f).Within(0.001f));
            Assert.That(plan.Footprint.yMin, Is.EqualTo(-0.08f).Within(0.001f));
            Assert.That(plan.Footprint.yMax, Is.EqualTo(0.68f).Within(0.001f));
        }

        [Test]
        public void Prepare_RefusesALayoutThatCannotBeRead_OrIsEmpty()
        {
            var store = new CustomFloorLayouts(null, folder);
            var catalog = new FloorLayoutCatalog(null, store);
            var placer = new FloorLayoutPlacer(catalog, null, null, null, null, null, null);
            string problem;

            Assert.That(placer.Prepare(new FloorLayoutEntry { name = "Gone", address = "custom/floor_gone" }, out problem), Is.Null);
            Assert.That(problem, Does.Contain("could not be read"));

            store.Save("Empty", Layout("Empty"), null);
            Assert.That(placer.Prepare(catalog.All()[0], out problem), Is.Null);
            Assert.That(problem, Does.Contain("nothing in it"));
        }

        [Test]
        public void FootprintAt_TurnsTheRect_AboutTheOrigin()
        {
            var footprint = Rect.MinMaxRect(0f, 0f, 2f, 1f);

            var quarter = FloorLayoutPlacer.FootprintAt(footprint, 1);
            Assert.That(quarter.width, Is.EqualTo(1f).Within(0.001f));
            Assert.That(quarter.height, Is.EqualTo(2f).Within(0.001f));

            var half = FloorLayoutPlacer.FootprintAt(footprint, 2);
            Assert.That(half.xMin, Is.EqualTo(-2f).Within(0.001f));
            Assert.That(half.yMin, Is.EqualTo(-1f).Within(0.001f));

            Assert.That(FloorLayoutPlacer.FootprintAt(footprint, 4), Is.EqualTo(footprint), "four quarters is no turn");
        }

        [Test]
        public void CellIsTaken_ByAnAreaOrFurniture_NotByAFloorTileOrAWall()
        {
            var taken = new List<RoomPlacer.Taken>
            {
                new RoomPlacer.Taken(Rect.MinMaxRect(0f, 0f, 1.2f, 1.2f), "default_wood_floor_tile", PlacementGrid.Step),
                new RoomPlacer.Taken(Rect.MinMaxRect(2f, 0f, 2.1f, 1.2f), "default_divider_wall_large", PlacementGrid.Step),
                new RoomPlacer.Taken(Rect.MinMaxRect(4f, 0f, 5f, 1f), "default_dual_workstation_desk"),
                new RoomPlacer.Taken(Rect.MinMaxRect(6f, 0f, 8f, 2f), "the Sales area", PlacementGrid.Step),
            };

            Assert.IsFalse(FloorLayoutPlacer.CellIsTaken(new Vector2(0.6f, 0.6f), taken), "a floor tile is laid over");
            Assert.IsFalse(FloorLayoutPlacer.CellIsTaken(new Vector2(2.05f, 0.6f), taken), "a wall is reached over");
            Assert.IsTrue(FloorLayoutPlacer.CellIsTaken(new Vector2(4.5f, 0.5f), taken), "furniture is in the way");
            Assert.IsTrue(FloorLayoutPlacer.CellIsTaken(new Vector2(7f, 1f), taken), "an area already named is in the way");
            Assert.IsFalse(FloorLayoutPlacer.CellIsTaken(new Vector2(9f, 9f), taken), "nothing there");
            Assert.IsFalse(FloorLayoutPlacer.CellIsTaken(new Vector2(0f, 0f), null));
        }
    }
}
