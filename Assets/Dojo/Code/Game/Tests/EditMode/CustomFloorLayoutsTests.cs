using System;
using System.IO;
using System.Threading;
using Dojo.Game.Placement;
using NUnit.Framework;
using UnityEngine;

namespace Dojo.Game.Tests.EditMode
{
    /// <summary>
    /// The floor layouts made here: one world file and one picture each, named from the layout's
    /// name, listed oldest first, deleted whole, and promoted into the pack by renaming.
    /// On a folder of the test's own, never the project's.
    /// </summary>
    public sealed class CustomFloorLayoutsTests
    {
        string folder;
        CustomFloorLayouts store;

        [SetUp]
        public void SetUp()
        {
            folder = Path.Combine(Path.GetTempPath(), "DojoCustomFloorLayoutsTests_" + Guid.NewGuid().ToString("N")).Replace('\\', '/');
            Directory.CreateDirectory(folder);
            store = new CustomFloorLayouts(null, folder);
        }

        [TearDown]
        public void TearDown()
        {
            store.DeleteAll();

            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }

        static WorldSave Layout(string name, int tiles = 2)
        {
            var layout = new WorldSave { name = name };

            for (var i = 0; i < tiles; i++)
            {
                layout.FloorAt(0).pieces.Add(new WorldSave.SavedPiece
                {
                    resource = "default/test_floor",
                    position = new Vector3(i * 1.2f, 0f, 0f),
                    isFloor = true,
                });
            }

            return layout;
        }

        static byte[] Png(Color colour)
        {
            var texture = new Texture2D(4, 4);

            try
            {
                var pixels = new Color[16];
                for (var i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = colour;
                }

                texture.SetPixels(pixels);
                texture.Apply();
                return texture.EncodeToPNG();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        // ── Names ───────────────────────────────────────────────────────────────────────────

        [Test]
        public void Slug_IsFloorThenTheName_LettersAndDigitsOnly()
        {
            Assert.That(CustomFloorLayouts.Slug("Open Plan!"), Is.EqualTo("floor_open_plan"));
            Assert.That(CustomFloorLayouts.Slug("  Studio 2  "), Is.EqualTo("floor_studio_2"));
            Assert.That(CustomFloorLayouts.Slug("Floor Two"), Is.EqualTo("floor_two"), "a name that starts with floor is not given it twice");
            Assert.That(CustomFloorLayouts.Slug("floor_two"), Is.EqualTo("floor_two"));
            Assert.That(CustomFloorLayouts.Slug("Floor"), Is.EqualTo("floor_unnamed"));
            Assert.That(CustomFloorLayouts.Slug(""), Is.EqualTo("floor_unnamed"));
            Assert.That(CustomFloorLayouts.Slug(null), Is.EqualTo("floor_unnamed"));
        }

        [Test]
        public void IsCustom_OnlyForALayoutMadeHere()
        {
            Assert.IsTrue(CustomFloorLayouts.IsCustom("custom/floor_corner"));
            Assert.IsFalse(CustomFloorLayouts.IsCustom("custom/room_corner"), "a made room is not a layout");
            Assert.IsFalse(CustomFloorLayouts.IsCustom("default/Floors/floor_corner"));
            Assert.IsFalse(CustomFloorLayouts.IsCustom(null));
        }

        [Test]
        public void SlugOf_IsTheFileNameAnAddressEndsIn()
        {
            Assert.That(CustomFloorLayouts.SlugOf("custom/floor_corner"), Is.EqualTo("floor_corner"));
            Assert.That(CustomFloorLayouts.SlugOf("default/Floors/floor_corner"), Is.EqualTo("floor_corner"));
            Assert.That(CustomFloorLayouts.SlugOf("floor_corner"), Is.EqualTo("floor_corner"));
        }

        // ── Saving ──────────────────────────────────────────────────────────────────────────

        [Test]
        public void Save_WritesTheFileAndPicture_AndListsIt()
        {
            var announced = 0;
            store.Changed += () => announced++;

            var address = store.Save("Corner Office", Layout("Corner Office", 3), Png(Color.red));

            Assert.That(address, Is.EqualTo("custom/floor_corner_office"));
            Assert.IsTrue(File.Exists(Path.Combine(folder, "floor_corner_office.json")), "no world file");
            Assert.IsTrue(File.Exists(Path.Combine(store.ThumbnailFolder, "floor_corner_office.png")), "no picture");
            Assert.That(announced, Is.EqualTo(1));

            var all = store.All();
            Assert.That(all.Count, Is.EqualTo(1));
            Assert.That(all[0].name, Is.EqualTo("Corner Office"));
            Assert.That(all[0].address, Is.EqualTo(address));
            Assert.That(all[0].icon, Is.EqualTo(address));
            Assert.IsTrue(store.Exists("corner office"), "found by name, whatever the case");

            var world = store.Read(address);
            Assert.That(world, Is.Not.Null);
            Assert.That(world.floors[0].pieces.Count, Is.EqualTo(3));

            var icon = store.Icon(address);
            Assert.That(icon, Is.Not.Null);
            Assert.That(icon.texture.width, Is.EqualTo(4));
            Assert.That(store.Icon(address), Is.SameAs(icon), "made once, then kept");
        }

        [Test]
        public void SavingAgain_ReplacesIt_AndListsItOnce()
        {
            store.Save("Corner", Layout("Corner", 1), null);
            store.Save("Corner", Layout("Corner", 5), null);

            var all = store.All();
            Assert.That(all.Count, Is.EqualTo(1));
            Assert.That(store.Read(all[0].address).floors[0].pieces.Count, Is.EqualTo(5));
        }

        [Test]
        public void All_IsOldestFirst()
        {
            store.Save("Zebra", Layout("Zebra"), null);
            Thread.Sleep(30);
            store.Save("Apple", Layout("Apple"), null);

            var all = store.All();
            Assert.That(all[0].name, Is.EqualTo("Zebra"));
            Assert.That(all[1].name, Is.EqualTo("Apple"));
        }

        [Test]
        public void IsNameFree_RefusesANameThePackHolds_AndAllowsARepeatOfOurOwn()
        {
            File.WriteAllText(Path.Combine(folder, "default_floor_open_plan.json"), "{}");
            string problem;

            Assert.IsFalse(store.IsNameFree("Open Plan", out problem));
            Assert.That(problem, Does.Contain("taken by one of the pack's floor plans"));

            Assert.IsTrue(store.IsNameFree("Corner", out problem));
            store.Save("Corner", Layout("Corner"), null);
            Assert.IsTrue(store.IsNameFree("Corner", out problem), "saving it again is how it is replaced");
        }

        // ── Deleting ────────────────────────────────────────────────────────────────────────

        [Test]
        public void Delete_RemovesTheFileAndPicture_AndRefusesThePacks()
        {
            var address = store.Save("Corner", Layout("Corner"), Png(Color.blue));
            string problem;

            Assert.IsFalse(store.Delete("default/Floors/default_floor_open_plan", out problem));
            Assert.That(problem, Does.Contain("pack"));

            Assert.IsTrue(store.Delete(address, out problem), problem);
            Assert.IsFalse(File.Exists(Path.Combine(folder, "floor_corner.json")));
            Assert.IsFalse(File.Exists(Path.Combine(store.ThumbnailFolder, "floor_corner.png")));
            Assert.That(store.All(), Is.Empty);

            Assert.IsFalse(store.Delete(address, out problem));
            Assert.That(problem, Does.Contain("already gone"));
        }

        // ── Promoting ───────────────────────────────────────────────────────────────────────

        [Test]
        public void Promote_RenamesIntoThePack_AndListsItLast()
        {
            File.WriteAllText(Path.Combine(folder, "floors.json"),
                "{\"floors\":[{\"name\":\"Open Plan\",\"address\":\"default/Floors/default_floor_open_plan\",\"icon\":\"default/Floors/Thumbnails/floor_open_plan\",\"tag\":\"default\"}]}");
            var address = store.Save("Corner", Layout("Corner", 2), Png(Color.green));
            string problem;

            Assert.IsTrue(store.Promote(address, out problem), problem);

            Assert.IsFalse(File.Exists(Path.Combine(folder, "floor_corner.json")), "the made file was left behind");
            Assert.IsTrue(File.Exists(Path.Combine(folder, "default_floor_corner.json")), "no pack file");
            Assert.IsTrue(File.Exists(Path.Combine(store.ThumbnailFolder, "floor_corner.png")), "the picture keeps its name");
            Assert.That(store.All(), Is.Empty, "no longer one of the made layouts");

            var listed = store.ListedLayouts();
            Assert.That(listed.Count, Is.EqualTo(2));
            Assert.That(listed[1].name, Is.EqualTo("Corner"));
            Assert.That(listed[1].address, Is.EqualTo("default/Floors/default_floor_corner"));
            Assert.That(listed[1].icon, Is.EqualTo("default/Floors/Thumbnails/floor_corner"));
            Assert.That(listed[1].tag, Is.EqualTo(FloorLayoutEntry.PromotedTag));

            Assert.IsFalse(store.Promote(address, out problem), "promoted twice");
            Assert.IsFalse(store.Promote("default/Floors/default_floor_corner", out problem), "a pack layout was promoted");
        }

        [Test]
        public void Promote_RefusesANameThePackAlreadyHas()
        {
            File.WriteAllText(Path.Combine(folder, "default_floor_corner.json"), "{}");
            var address = store.Save("Corner", Layout("Corner"), null);
            string problem;

            Assert.IsFalse(store.Promote(address, out problem));
            Assert.That(problem, Does.Contain("already has a floor plan called default_floor_corner"));
            Assert.IsTrue(File.Exists(Path.Combine(folder, "floor_corner.json")), "the made file was touched");
        }

        [Test]
        public void ListedLayouts_IsNull_UntilThereIsAListHere()
        {
            Assert.That(store.ListedLayouts(), Is.Null);

            File.WriteAllText(Path.Combine(folder, "floors.json"), "{\"floors\":[]}");

            Assert.That(store.ListedLayouts(), Is.Empty);
        }
    }
}
