using System.Collections.Generic;
using Dojo.Game.Placement;
using NUnit.Framework;
using UnityEngine;

namespace Dojo.Game.Tests.EditMode
{
    /// <summary>
    /// No two areas share a name (QA issue 25): a name already taken, on any floor and in any case,
    /// is numbered "Sales 2", "Sales 3"... when an area is painted or renamed, as rooms' areas were.
    /// </summary>
    public sealed class AreaNamesTests
    {
        static List<Vector3> Cells() => new List<Vector3> { Vector3.zero, Vector3.right };

        [Test]
        public void ANewName_IsKept()
        {
            var areas = new BlockAreas(null);

            Assert.AreEqual("Sales", areas.Add("Sales", 0, Cells(), 0.3f).Name);
        }

        [Test]
        public void ASecondSales_IsSales2_AndAThirdSales3()
        {
            var areas = new BlockAreas(null);
            areas.Add("Sales", 0, Cells(), 0.3f);

            Assert.AreEqual("Sales 2", areas.Add("Sales", 0, Cells(), 0.3f).Name);
            Assert.AreEqual("Sales 3", areas.Add("Sales", 0, Cells(), 0.3f).Name);
        }

        [Test]
        public void ANameOnAnotherFloor_StillCounts()
        {
            var areas = new BlockAreas(null);
            areas.Add("Sales", 0, Cells(), 0.3f);

            Assert.AreEqual("Sales 2", areas.Add("Sales", 1, Cells(), 0.3f).Name,
                "'walk to Sales' and the elevator look areas up by name across floors");
        }

        [Test]
        public void Case_DoesNotMakeANameNew()
        {
            var areas = new BlockAreas(null);
            areas.Add("Sales", 0, Cells(), 0.3f);

            Assert.AreEqual("sales 2", areas.Add("  sales ", 0, Cells(), 0.3f).Name);
        }

        [Test]
        public void RenamingToAnotherAreasName_Numbers()
        {
            var areas = new BlockAreas(null);
            areas.Add("Sales", 0, Cells(), 0.3f);
            var pantry = areas.Add("Pantry", 0, Cells(), 0.3f);

            Assert.IsTrue(areas.Rename(pantry.Id, "Sales"));
            Assert.AreEqual("Sales 2", pantry.Name);
        }

        [Test]
        public void RenamingToItsOwnName_KeepsIt()
        {
            var areas = new BlockAreas(null);
            var sales = areas.Add("Sales", 0, Cells(), 0.3f);

            Assert.IsTrue(areas.Rename(sales.Id, "SALES"));
            Assert.AreEqual("SALES", sales.Name, "an area's own name is not a clash");
        }

        [Test]
        public void ALongName_StaysWithinTheLimit_WithItsNumber()
        {
            var areas = new BlockAreas(null);
            var longName = new string('x', 60);
            areas.Add(longName, 0, Cells(), 0.3f);

            var second = areas.Add(longName, 0, Cells(), 0.3f).Name;

            Assert.LessOrEqual(second.Length, BlockAreas.MaxNameLength);
            Assert.IsTrue(second.EndsWith(" 2"));
        }

        [Test]
        public void ABlankName_IsArea_ThenArea2()
        {
            var areas = new BlockAreas(null);

            Assert.AreEqual("Area", areas.Add("  ", 0, Cells(), 0.3f).Name);
            Assert.AreEqual("Area 2", areas.Add(null, 0, Cells(), 0.3f).Name);
        }
    }
}
