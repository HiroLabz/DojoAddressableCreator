using System;
using System.Collections.Generic;
using System.IO;
using Dojo.Game.Editor;
using NUnit.Framework;

namespace Dojo.Game.Tests.EditMode
{
    /// <summary>
    /// The Addressable Builder's release layout: one flat folder holding version.json, the
    /// catalogue, its hash and every bundle of exactly one release; the next number; the pack
    /// folders it processes; and that version.json only moves once every file is in place.
    /// </summary>
    public sealed class ContentReleaseTests
    {
        static readonly DateTime Built = new DateTime(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc);

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
            foreach (var file in Directory.GetFiles(build))
            {
                File.Delete(file);
            }

            File.WriteAllText(Path.Combine(build, "catalog_" + version + ".bin"), "catalog " + version);
            File.WriteAllText(Path.Combine(build, "catalog_" + version + ".hash"), "hash " + version);

            var bundles = new List<string>();
            foreach (var name in bundleNames)
            {
                var path = Path.Combine(build, name);
                File.WriteAllText(path, "bundle " + name);
                bundles.Add(path);
            }

            return bundles;
        }

        string[] OutputFiles() => Array.ConvertAll(Directory.GetFiles(output), Path.GetFileName);

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
        public void Install_PutsEveryFileStraightInTheFolder_NoVersionFolder()
        {
            var installed = ContentRelease.Install(output, 0, build, FakeBuild(0, "a.bundle", "b.bundle"), Built);

            Assert.AreEqual(2, installed.Bundles);
            CollectionAssert.AreEquivalent(
                new[] { "a.bundle", "b.bundle", "catalog_0.bin", "catalog_0.hash", "version.json" }, OutputFiles());
            CollectionAssert.IsEmpty(Directory.GetDirectories(output));
            Assert.AreEqual(ContentRelease.VersionJson(0, "catalog_0.bin", Built),
                File.ReadAllText(Path.Combine(output, "version.json")));
        }

        [Test]
        public void Install_OverAnEarlierBuild_LeavesExactlyOneRelease()
        {
            ContentRelease.Install(output, 0, build, FakeBuild(0, "kept.bundle", "old.bundle"), Built);

            var installed = ContentRelease.Install(output, 1, build, FakeBuild(1, "kept.bundle", "new.bundle"), Built);

            Assert.AreEqual(1, ContentRelease.CurrentVersion(output));
            CollectionAssert.AreEquivalent(
                new[] { "kept.bundle", "new.bundle", "catalog_1.bin", "catalog_1.hash", "version.json" }, OutputFiles());
            CollectionAssert.AreEquivalent(new[] { "catalog_0.bin", "catalog_0.hash", "old.bundle" }, installed.Removed);
        }

        [Test]
        public void Install_ClearsOutTheOldVersionFoldersAndLooseFilesOfEarlierBuilds()
        {
            File.WriteAllText(Path.Combine(output, "version.json"), ContentRelease.VersionJson(2, "catalog_2.bin", Built));
            File.WriteAllText(Path.Combine(output, "catalog_0.1.0.bin"), "old");
            File.WriteAllText(Path.Combine(output, "catalog_0.1.0.hash"), "old");
            File.WriteAllText(Path.Combine(output, "stale_abc.bundle"), "old");
            Directory.CreateDirectory(Path.Combine(output, "v2"));
            File.WriteAllText(Path.Combine(output, "v2", "catalog_2.bin"), "old");

            ContentRelease.Install(output, 3, build, FakeBuild(3, "a.bundle"), Built);

            CollectionAssert.AreEquivalent(new[] { "a.bundle", "catalog_3.bin", "catalog_3.hash", "version.json" }, OutputFiles());
            CollectionAssert.IsEmpty(Directory.GetDirectories(output));
        }

        [Test]
        public void Install_LeavesFilesThatAreNotBuildFilesAlone()
        {
            File.WriteAllText(Path.Combine(output, "README.txt"), "notes");
            Directory.CreateDirectory(Path.Combine(output, "notes"));

            ContentRelease.Install(output, 0, build, FakeBuild(0, "a.bundle"), Built);

            Assert.IsTrue(File.Exists(Path.Combine(output, "README.txt")));
            Assert.IsTrue(Directory.Exists(Path.Combine(output, "notes")));
            CollectionAssert.AreEqual(new[] { "notes/", "README.txt" }, ContentRelease.OtherFiles(output));
        }

        [Test]
        public void Install_RefusesAVersionThatIsNotTheNextOne_AndChangesNothing()
        {
            File.WriteAllText(Path.Combine(output, "version.json"), ContentRelease.VersionJson(1, "catalog_1.bin", Built));
            File.WriteAllText(Path.Combine(output, "catalog_1.bin"), "live");

            Assert.Throws<ContentReleaseException>(() =>
                ContentRelease.Install(output, 3, build, FakeBuild(3, "a.bundle"), Built));

            Assert.AreEqual(1, ContentRelease.CurrentVersion(output));
            CollectionAssert.AreEquivalent(new[] { "catalog_1.bin", "version.json" }, OutputFiles());
        }

        [Test]
        public void Install_WithABundleMissing_LeavesTheLiveReleaseUntouched()
        {
            File.WriteAllText(Path.Combine(output, "version.json"), ContentRelease.VersionJson(1, "catalog_1.bin", Built));
            File.WriteAllText(Path.Combine(output, "catalog_1.bin"), "live");
            File.WriteAllText(Path.Combine(output, "live.bundle"), "live");
            var bundles = FakeBuild(2, "a.bundle");
            bundles.Add(Path.Combine(build, "never_written.bundle"));

            var refused = Assert.Throws<ContentReleaseException>(() => ContentRelease.Install(output, 2, build, bundles, Built));

            StringAssert.Contains("never_written.bundle", refused.Message);
            Assert.AreEqual(1, ContentRelease.CurrentVersion(output));
            CollectionAssert.AreEquivalent(new[] { "catalog_1.bin", "live.bundle", "version.json" }, OutputFiles());
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
        public void BuildFiles_AreBundlesCataloguesHashesAndOldVersionFolders()
        {
            File.WriteAllText(Path.Combine(output, "version.json"), "{}");
            File.WriteAllText(Path.Combine(output, "catalog_0.1.0.bin"), "old");
            File.WriteAllText(Path.Combine(output, "catalog_4.hash"), "old");
            File.WriteAllText(Path.Combine(output, "x.bundle"), "old");
            File.WriteAllText(Path.Combine(output, "README.txt"), "notes");
            Directory.CreateDirectory(Path.Combine(output, "v12"));
            Directory.CreateDirectory(Path.Combine(output, "notes"));

            CollectionAssert.AreEqual(new[] { "catalog_0.1.0.bin", "catalog_4.hash", "v12/", "x.bundle" },
                ContentRelease.BuildFiles(output));
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
