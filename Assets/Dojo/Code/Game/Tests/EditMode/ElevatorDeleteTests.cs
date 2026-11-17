using Dojo.Game.Placement;
using NUnit.Framework;
using UnityEngine;

namespace Dojo.Game.Tests.EditMode
{
    /// <summary>
    /// Deleting an elevator takes every stop of its shaft with it. A deleted stop is switched off at
    /// once and destroyed at the end of the frame; until then it must not count as standing, or the
    /// shaft keeps its other stops and a stop being taken down comes back as a new elevator (QA, 17 Nov).
    /// </summary>
    public sealed class ElevatorDeleteTests
    {
        GameObject floor;
        GameObject stop;

        [SetUp]
        public void SetUp()
        {
            floor = new GameObject("Floor 2F");
            stop = new GameObject("default_elevator_mvp");
            stop.transform.SetParent(floor.transform);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(floor);
        }

        [Test]
        public void AStopInTheWorld_IsStanding()
        {
            Assert.IsTrue(ElevatorShafts.IsStanding(stop.transform));
        }

        [Test]
        public void AStopSwitchedOffToBeDestroyed_IsNotStanding_BeforeTheDestroyLands()
        {
            // What the delete does: off now, destroyed at the end of the frame. Not null yet.
            stop.SetActive(false);

            Assert.IsFalse(stop.transform == null, "still there until the end of the frame");
            Assert.IsFalse(ElevatorShafts.IsStanding(stop.transform));
        }

        [Test]
        public void ADestroyedStop_IsNotStanding()
        {
            var gone = stop.transform;
            Object.DestroyImmediate(stop);

            Assert.IsFalse(ElevatorShafts.IsStanding(gone));
            Assert.IsFalse(ElevatorShafts.IsStanding(null));
        }

        [Test]
        public void AStopOnAFloorOutOfView_IsStillStanding()
        {
            // A floor switched off as a whole leaves its stops switched on themselves: they stay.
            floor.SetActive(false);

            Assert.IsTrue(ElevatorShafts.IsStanding(stop.transform));
        }
    }
}
