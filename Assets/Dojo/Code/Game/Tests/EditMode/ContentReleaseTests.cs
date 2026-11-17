using System;
using System.Collections.Generic;
using System.IO;
using Dojo.Game.Editor;
using NUnit.Framework;

namespace Dojo.Game.Tests.EditMode
{
    /// <summary>
    /// The Addressable Builder's release layout: version.json and v&lt;version&gt;/ in the one folder
    /// the game loads from, the next number, the pack folders it processes, and that version.json
    /// only moves once every file of the release is in place.
    /// </summary>
    public sealed class ContentReleaseTests
    {
        static readonly DateTime Built = new DateTime(2026, 11, 17, 16, 0, 0, DateTimeKind.Utc);

        string root;
        string output;
        string build;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "ContentReleaseTests_" + Guid.NewGuid().ToString("N"));
            output = Path.Combine(root, "development");
            build = Path.Combine(root, "build");
            Directory.CreateDirectory(output);
            Directory.CreateDirectory(build);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }

        /// <summary>Writes what an Addressables build of one version leaves: catalogue, hash and bundles.</summary>
        List<string> FakeBuild(int version, params string[] bundleNames)
        {
            File.WriteAllText(Path.Combine(build, "catalog_" + version + ".bin"), "catalog " + version);
            File.WriteAllText(Path.Combine(build, "catalog_" + version + ".hash"), "hash " + version);

            var bundles = new List<string>();
            foreach (var name in bundleNames)
            {
                var path = Path.Combine(build, name);
                File.WriteAllText(path, "bundle " + name + " " + version);
                bundles.Add(path);
            }

            return bundles;
        }

        [Test]
        public void AFolderWithNoVersionFile_BuildsVersionZero()
        {
            Assert.IsNull(ContentRelease.CurrentVersion(output));
            Assert.AreEqual(0, ContentRelease.NextVersion(ContentRelease.CurrentVersion(output)));
        }

        [Test]
        public void TheNextVersion_IsOneMoreThanTheFolders()
        {
            File.WriteAllText(Path.Combine(output, "version.json"), ContentRelease.VersionJson(1, "catalog_1.bin", Built));

            Assert.AreEqual(1, ContentRelease.CurrentVersion(output));
            Assert.AreEqual(2, ContentRelease.NextVersion(1));
        }

        [Test]
        public void AVersionFileWithNoNumber_IsRefused()
        {
            File.WriteAllText(Path.Combine(output, "version.json"), "{ \"catalog\": \"x\" }");

            Assert.Throws<InvalidDataException>(() => ContentRelease.CurrentVersion(output));
        }

        [Test]
        public void Install_PutsTheReleaseInItsOwnFolder_AndVersionJsonNextToIt()
        {
            var bundles = FakeBuild(0, "a.bundle", "b.bundle");

            var installed = ContentRelease.Install(output, 0, build, bundles, Built);

            Assert.AreEqual(2, installed);
            var release = Path.Combine(output, "v0");
            CollectionAssert.AreEquivalent(
                new[] { "a.bundle", "b.bundle", "catalog_0.bin", "catalog_0.hash" },
                Array.ConvertAll(Directory.GetFiles(release), Path.GetFileName));
            Assert.AreEqual(ContentRelease.VersionJson(0, "catalog_0.bin", Built),
                File.ReadAllText(Path.Combine(output, "version.json")));
            Assert.IsFalse(File.Exists(Path.Combine(output, "version.json.tmp")));
        }

        [Test]
        public void Install_OverAnOlderRelease_MovesVersionJsonOn_AndKeepsTheOlderFolder()
        {
            ContentRelease.Install(output, 0, build, FakeBuild(0, "a.bundle"), Built);

            ContentRelease.Install(output, 1, build, FakeBuild(1, "a.bundle", "c.bundle"), Built);

            Assert.AreEqual(1, ContentRelease.CurrentVersion(output));
            Assert.IsTrue(Directory.Exists(Path.Combine(output, "v0")), "the game may still be on v0 until it reads the new version.json");
            Assert.IsTrue(File.Exists(Path.Combine(output, "v1", "c.bundle")));
            Assert.IsTrue(File.Exists(Path.Combine(output, "v1", "catalog_1.bin")));
        }

        [Test]
        public void Install_RefusesAVersionThatIsNotTheNextOne_AndChangesNothing()
        {
            File.WriteAllText(Path.Combine(output, "version.json"), ContentRelease.VersionJson(1, "catalog_1.bin", Built));

            Assert.Throws<ContentReleaseException>(() =>
                ContentRelease.Install(output, 3, build, FakeBuild(3, "a.bundle"), Built));

            Assert.AreEqual(1, ContentRelease.CurrentVersion(output));
            Assert.IsFalse(Directory.Exists(Path.Combine(output, "v3")));
        }

        [Test]
        public void Install_WithABundleMissing_LeavesVersionJsonOnThePreviousRelease()
        {
            File.WriteAllText(Path.Combine(output, "version.json"), ContentRelease.VersionJson(1, "catalog_1.bin", Built));
            var bundles = FakeBuild(2, "a.bundle");
            bundles.Add(Path.Combine(build, "never_written.bundle"));

            var refused = Assert.Throws<ContentReleaseException>(() => ContentRelease.Install(output, 2, build, bundles, Built));

            StringAssert.Contains("never_written.bundle", refused.Message);
            Assert.AreEqual(1, ContentRelease.CurrentVersion(output));
        }

        [Test]
        public void Install_WithTheCatalogueMissing_LeavesVersionJsonAlone()
        {
            var bundles = FakeBuild(0, "a.bundle");
            File.Delete(Path.Combine(build, "catalog_0.bin"));

            Assert.Throws<ContentReleaseException>(() => ContentRelease.Install(output, 0, build, bundles, Built));

            Assert.IsFalse(File.Exists(Path.Combine(output, "version.json")));
        }

        [Test]
        public void Install_WithNoBundles_IsRefused()
        {
            FakeBuild(0);

            Assert.Throws<ContentReleaseException>(() => ContentRelease.Install(output, 0, build, new List<string>(), Built));
            Assert.IsFalse(File.Exists(Path.Combine(output, "version.json")));
        }

        [Test]
        public void Install_ReplacesAHalfBuiltFolder_ThatNothingPointsAt()
        {
            Directory.CreateDirectory(Path.Combine(output, "v0"));
            File.WriteAllText(Path.Combine(output, "v0", "stale.bundle"), "from a build that never finished");

            ContentRelease.Install(output, 0, build, FakeBuild(0, "a.bundle"), Built);

            Assert.IsFalse(File.Exists(Path.Combine(output, "v0", "stale.bundle")));
            Assert.IsTrue(File.Exists(Path.Combine(output, "v0", "a.bundle")));
        }

        [Test]
        public void Leftovers_AreEverythingButVersionJsonAndTheReleaseFolders()
        {
            File.WriteAllText(Path.Combine(output, "version.json"), "{}");
            File.WriteAllText(Path.Combine(output, "catalog_0.1.0.bin"), "old");
            File.WriteAllText(Path.Combine(output, "old.bundle"), "old");
            Directory.CreateDirectory(Path.Combine(output, "v0"));
            Directory.CreateDirectory(Path.Combine(output, "v12"));
            Directory.CreateDirectory(Path.Combine(output, "notes"));

            CollectionAssert.AreEqual(new[] { "catalog_0.1.0.bin", "notes/", "old.bundle" }, ContentRelease.Leftovers(output));
        }

        [Test]
        public void PackNames_AreEveryFolder_DefaultFirst_ThenByName()
        {
            var process = Path.Combine(root, "Process");
            foreach (var name in new[] { "strawberry", "apple", "default", ".hidden", "Ignored~" })
            {
                Directory.CreateDirectory(Path.Combine(process, name));
            }

            File.WriteAllText(Path.Combine(process, "default.meta"), "meta");

            CollectionAssert.AreEqual(new[] { "default", "apple", "strawberry" }, ContentRelease.PackNames(process));
        }

        [Test]
        public void PackNames_WithNoProcessFolder_IsEmpty()
        {
            CollectionAssert.IsEmpty(ContentRelease.PackNames(Path.Combine(root, "nowhere")));
        }
    }
}
