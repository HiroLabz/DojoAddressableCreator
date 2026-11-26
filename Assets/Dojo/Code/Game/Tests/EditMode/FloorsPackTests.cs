using System;
using System.IO;
using Dojo.Game.Editor;
using NUnit.Framework;

namespace Dojo.Game.Tests.EditMode
{
    /// <summary>
    /// <c>floors.json</c> is checked against the files beside it, as <c>rooms.json</c> is: every
    /// problem is named, nothing is rewritten. On a folder of the test's own.
    /// </summary>
    public sealed class FloorsPackTests
    {
        string folder;

        [SetUp]
        public void SetUp()
        {
            folder = Path.Combine(Path.GetTempPath(), "DojoFloorsPackTests_" + Guid.NewGuid().ToString("N")).Replace('\\', '/');
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

        static string Entry(string name, string address, string icon)
            => "{\"name\":\"" + name + "\",\"address\":\"" + address + "\",\"icon\":\"" + icon + "\",\"tag\":\"default\"}";

        void Index(params string[] entries)
            => File.WriteAllText(folder + "/floors.json", "{\"floors\":[" + string.Join(",", entries) + "]}");

        void PackLayout(string x) => File.WriteAllText(folder + "/default_floor_" + x + ".json", "{}");

        void Thumbnail(string x)
        {
            Directory.CreateDirectory(folder + "/Thumbnails");
            File.WriteAllBytes(folder + "/Thumbnails/floor_" + x + ".png", new byte[] { 1 });
        }

        [Test]
        public void AnEmptyFolder_HasNoProblems()
        {
            Assert.That(FloorsPack.CheckIndex(folder, "default"), Is.Empty);
        }

        [Test]
        public void PackLayoutsWithNoIndex_AreNamed()
        {
            PackLayout("open_plan");

            var problems = FloorsPack.CheckIndex(folder, "default");

            Assert.That(problems.Count, Is.EqualTo(1));
            Assert.That(problems[0], Does.Contain("floors.json is missing"));
        }

        [Test]
        public void AListedLayoutWithItsFileAndThumbnail_IsFine()
        {
            PackLayout("open_plan");
            Thumbnail("open_plan");
            Index(Entry("Open Plan", "default/Floors/default_floor_open_plan", "default/Floors/Thumbnails/floor_open_plan"));

            Assert.That(FloorsPack.CheckIndex(folder, "default"), Is.Empty);
        }

        [Test]
        public void AnEntryWhoseFileIsMissing_IsNamed()
        {
            Thumbnail("studio");
            Index(Entry("Studio", "default/Floors/default_floor_studio", "default/Floors/Thumbnails/floor_studio"));

            var problems = FloorsPack.CheckIndex(folder, "default");

            Assert.That(problems.Count, Is.EqualTo(1));
            Assert.That(problems[0], Does.Contain("'Studio' points to layout 'default/Floors/default_floor_studio', but there is no"));
        }

        [Test]
        public void AnEntryUnderAnotherPack_IsNamed()
        {
            Index(Entry("Studio", "other/Floors/default_floor_studio", "other/Floors/Thumbnails/floor_studio"));

            var problems = FloorsPack.CheckIndex(folder, "default");

            Assert.That(problems.Count, Is.EqualTo(2), "the file and the thumbnail");
            Assert.That(problems[0], Does.Contain("is not a default/Floors/"));
        }

        [Test]
        public void AMissingThumbnail_IsNamed()
        {
            PackLayout("studio");
            Index(Entry("Studio", "default/Floors/default_floor_studio", "default/Floors/Thumbnails/floor_studio"));

            var problems = FloorsPack.CheckIndex(folder, "default");

            Assert.That(problems.Count, Is.EqualTo(1));
            Assert.That(problems[0], Does.Contain("points to thumbnail 'default/Floors/Thumbnails/floor_studio', but there is no such image"));
        }

        [Test]
        public void APackLayoutTheIndexDoesNotList_IsNamed()
        {
            PackLayout("open_plan");
            Thumbnail("open_plan");
            PackLayout("studio");
            Index(Entry("Open Plan", "default/Floors/default_floor_open_plan", "default/Floors/Thumbnails/floor_open_plan"));

            var problems = FloorsPack.CheckIndex(folder, "default");

            Assert.That(problems.Count, Is.EqualTo(1));
            Assert.That(problems[0], Does.Contain("default_floor_studio.json is a pack layout but floors.json does not list it"));
        }

        [Test]
        public void AnIndexThatCannotBeRead_IsNamed()
        {
            File.WriteAllText(folder + "/floors.json", "{not json");

            var problems = FloorsPack.CheckIndex(folder, "default");

            Assert.That(problems.Count, Is.EqualTo(1));
            Assert.That(problems[0], Does.Contain("could not be read"));
        }

        [Test]
        public void TheGroupAndFolder_AreTheFloorsOwn()
        {
            Assert.That(FloorsPack.GroupName, Is.EqualTo("Content_Floors"));
            Assert.That(FloorsPack.Folder, Is.EqualTo("Assets/Process/default/Floors"));
            Assert.That(FloorsPack.IndexPath, Is.EqualTo("Assets/Process/default/Floors/floors.json"));
        }
    }
}
