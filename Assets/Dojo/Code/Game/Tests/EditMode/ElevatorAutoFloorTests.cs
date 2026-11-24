using System.Collections.Generic;
using System.IO;
using Dojo.Game.Placement;
using NUnit.Framework;
using UnityEngine;

namespace Dojo.Game.Tests.EditMode
{
    /// <summary>
    /// PROTOTYPE, waiting on Bob's approval (ClickUp 14ze67xf93f): an elevator placed on the top
    /// floor with no other floor to stop at adds the floor above, with a landing of tiles.
    /// </summary>
    public sealed class ElevatorAutoFloorTests
    {
        [Test]
        public void OnlyTheTopFloor_UnderTheLimit_AndOnlyWhenSwitchedOn_GrowsAFloor()
        {
            Assert.IsTrue(ElevatorShafts.GrowsAFloor(true, 0, 1, true), "one floor: the elevator on it gets a second");
            Assert.IsTrue(ElevatorShafts.GrowsAFloor(true, 2, 3, true), "on the top of three");
            Assert.IsFalse(ElevatorShafts.GrowsAFloor(true, 1, 3, true), "not below the top: the floor above is there already");
            Assert.IsFalse(ElevatorShafts.GrowsAFloor(true, 4, 5, false), "not at the floor limit");
            Assert.IsFalse(ElevatorShafts.GrowsAFloor(false, 0, 1, true), "not with the prototype off");
        }

        [Test]
        public void BelowTheTop_TheFloorAlreadyAbove_GetsTheLanding()
        {
            Assert.IsTrue(ElevatorShafts.LandsAbove(true, 0, 2), "1F of two: the landing goes on 2F (Jacob, 17 Nov)");
            Assert.IsTrue(ElevatorShafts.LandsAbove(true, 1, 4), "2F of four: on 3F");
            Assert.IsFalse(ElevatorShafts.LandsAbove(true, 1, 2), "the top floor grows a floor instead");
            Assert.IsFalse(ElevatorShafts.LandsAbove(false, 0, 2), "not with the prototype off");
        }

        [Test]
        public void TheLanding_CoversTheElevator_TwoTilesInFrontOfItsDoors_AndATileEitherSide()
        {
            // Harold's elevator turned: 3 x 8 cells (0.9 x 2.4 m), doors on -X. Tiles 4 cells (1.2 m).
            var tiles = ElevatorShafts.LandingTiles(new Vector2Int(10, 20), new Vector2Int(3, 8), 0, 4);

            // X from 2 (two tiles in front) to 13; Z from 16 to 32 (a tile either side): 3 x 4 tiles.
            Assert.AreEqual(12, tiles.Count);
            Assert.IsTrue(tiles.Contains(new Vector2Int(2, 16)), "front corner");
            Assert.IsTrue(tiles.Contains(new Vector2Int(10, 28)), "back corner, beside the elevator");
            foreach (var tile in tiles)
            {
                Assert.Less(tile.x, 13, "nothing behind the elevator");
            }
        }

        [Test]
        public void TheFrontRows_TurnWithTheElevator()
        {
            // Doors on -X turned a quarter about Y face +Z, so the rows are past the far Z edge.
            var tiles = ElevatorShafts.LandingTiles(new Vector2Int(0, 0), new Vector2Int(8, 3), 1, 4);

            // Block Z 0..3, two tiles past its doors to Z 11; X widened a tile either side to -4..12.
            Assert.AreEqual(12, tiles.Count);
            Assert.IsTrue(tiles.Contains(new Vector2Int(-4, 8)));
            Assert.IsTrue(tiles.Contains(new Vector2Int(8, 0)));
            foreach (var tile in tiles)
            {
                Assert.GreaterOrEqual(tile.y, 0, "nothing behind the elevator");
            }
        }

        [Test]
        public void APointOnTheSeamBetweenTwoTiles_IsOnFloor()
        {
            // Two 1.2 m tiles meeting at z = -2.4, as under Harold's elevator; its door point sat on
            // that seam and, a float hair outside both, was on neither.
            var north = new Bounds(new Vector3(3.3f, 0.025f, -1.8f), new Vector3(1.2f, 0.05f, 1.2f));
            var south = new Bounds(new Vector3(3.3f, 0.025f, -3.0f), new Vector3(1.2f, 0.05f, 1.2f));
            var seam = new Vector3(3.3f, 0f, -2.4000003f);

            Assert.IsTrue(FurniturePlacementService.OnSlab(seam, north) || FurniturePlacementService.OnSlab(seam, south));
            Assert.IsFalse(FurniturePlacementService.OnSlab(new Vector3(3.3f, 0f, -0.5f), south), "well off it is still off");
        }

        [Test]
        public void ThePrototypeIsOff_UntilBobApprovesIt_WithTheMvpTileReady()
        {
            // The creator's RuntimeMVP leaves it at the default, as the game's Game.unity does
            // (off until ClickUp 14ze67xfeu3 is approved); the tile is set so switching it on is the one change.
            var settings = new WorldSettings();
            Assert.IsFalse(settings.AutoFloorAboveElevator, "off unless a scene turns it on");
            StringAssert.DoesNotContain("autoFloorAboveElevator: 1", File.ReadAllText("Assets/Scenes/RuntimeMVP.unity"));
        }
    }
}
