using System;
using System.Collections.Generic;
using System.IO;
using Dojo.Game.Editor;
using NUnit.Framework;

namespace Dojo.Game.Tests.EditMode
{
    /// <summary>
    /// One content version count for every machine: the next version is GitHub's plus one, a
    /// checkout that is behind or has stray changes cannot build, and when two machines build the
    /// same number only the first push publishes it. The git half runs against a throwaway local
    /// repository standing in for GitHub, never the real one.
    /// </summary>
    public sealed class ContentRepoTests
    {
        // ---- The rules, without git ----

        static ContentRepo.State Ready(int? published) => new ContentRepo.State
        {
            Root = "C:\\x",
            CurrentBranch = ContentRepo.Branch,
            Published = published,
            Local = published,
        };

        [Test]
        public void UpToDate_NextIsGitHubsPlusOne()
        {
            var verdict = ContentRepo.Decide(Ready(2));

            Assert.AreEqual(ContentRepo.Step.Build, verdict.Step);
            Assert.AreEqual(3, verdict.Next);
        }

        [Test]
        public void NothingPublishedYet_FirstVersionIsZero()
        {
            Assert.AreEqual(0, ContentRepo.Decide(Ready(null)).Next);
        }

        [Test]
        public void Behind_MustUpdateFirst_NoBuild()
        {
            var state = Ready(3);
            state.Local = 2;
            state.Behind = 1;

            var verdict = ContentRepo.Decide(state);

            Assert.AreEqual(ContentRepo.Step.Update, verdict.Step);
            Assert.AreEqual(-1, verdict.Next);
        }

        [Test]
        public void UnpublishedBuild_PublishOrDiscard_NoBuild()
        {
            var state = Ready(2);
            state.Local = 3;
            state.Ahead = 1;

            var verdict = ContentRepo.Decide(state);

            Assert.AreEqual(ContentRepo.Step.PublishOrDiscard, verdict.Step);
            Assert.AreEqual(-1, verdict.Next);
        }

        [Test]
        public void SomeonePublishedMeanwhile_DiscardThisBuild()
        {
            var state = Ready(3);
            state.Local = 3;
            state.Ahead = 1;
            state.Behind = 1;

            Assert.AreEqual(ContentRepo.Step.DiscardBuild, ContentRepo.Decide(state).Step);
        }

        [Test]
        public void StrayChangesInTheReleaseFolder_DiscardFirst()
        {
            var state = Ready(2);
            state.Uncommitted.Add("?? development/stray.bundle");

            Assert.AreEqual(ContentRepo.Step.DiscardChanges, ContentRepo.Decide(state).Step);
        }

        [Test]
        public void NotOnMain_Blocked()
        {
            var state = Ready(2);
            state.CurrentBranch = "my_branch";

            Assert.AreEqual(ContentRepo.Step.Blocked, ContentRepo.Decide(state).Step);
        }

        [Test]
        public void GitHubUnreachable_Blocked()
        {
            var state = Ready(2);
            state.FetchProblem = "Could not resolve host: github.com";

            Assert.AreEqual(ContentRepo.Step.Blocked, ContentRepo.Decide(state).Step);
        }

        [Test]
        public void LocalVersionEditedByHand_Blocked()
        {
            var state = Ready(2);
            state.Local = 7;

            Assert.AreEqual(ContentRepo.Step.Blocked, ContentRepo.Decide(state).Step);
        }

        [Test]
        public void OldVersionFolder_IsRefused_AndSaysWhy()
        {
            var problem = ContentRepo.FolderShapeProblem("E:\\hirolabz\\DojoAddressables\\development\\v2");

            Assert.IsNotNull(problem);
            StringAssert.Contains("v<version>", problem);
        }

        [Test]
        public void EmptyOrRelativeFolder_IsRefused()
        {
            Assert.IsNotNull(ContentRepo.FolderShapeProblem(string.Empty));
            Assert.IsNotNull(ContentRepo.FolderShapeProblem("DojoAddressables\\development"));
        }

        // ---- Against a throwaway "GitHub" ----

        string temp;
        string origin;

        [SetUp]
        public void SetUp()
        {
            temp = Path.Combine(Path.GetTempPath(), "ContentRepoTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            origin = Path.Combine(temp, "DojoAddressables.git");
        }

        [TearDown]
        public void TearDown()
        {
            if (!Directory.Exists(temp))
            {
                return;
            }

            // git marks its object files read-only.
            foreach (var file in Directory.GetFiles(temp, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(temp, true);
        }

        static void Git(string directory, string arguments)
        {
            var result = ContentRepo.Git(directory, arguments);
            Assert.AreEqual(0, result.Code, "git " + arguments + ": " + result.Error);
        }

        /// <summary>"GitHub" with version <paramref name="published"/> on main.</summary>
        void Publish(int published)
        {
            Directory.CreateDirectory(origin);
            Git(origin, "init --quiet --bare --initial-branch=main");

            var seed = Clone("seed");
            WriteRelease(seed, published);
            Git(seed, "add --all");
            Git(seed, "commit --quiet -m seed");
            Git(seed, "push --quiet origin HEAD:main");
        }

        /// <summary>One machine's checkout of "GitHub".</summary>
        string Clone(string machine)
        {
            var path = Path.Combine(temp, machine);
            Git(temp, "clone --quiet \"" + origin.Replace('\\', '/') + "\" " + machine);
            Git(path, "config user.name " + machine);
            Git(path, "config user.email " + machine + "@test.invalid");
            Git(path, "checkout --quiet -B main");
            return path;
        }

        /// <summary>What a build of <paramref name="version"/> leaves in the release folder.</summary>
        static void WriteRelease(string checkout, int version)
        {
            var folder = Path.Combine(checkout, ContentRepo.ReleaseFolder);
            Directory.CreateDirectory(folder);

            foreach (var file in Directory.GetFiles(folder, "catalog_*"))
            {
                File.Delete(file);
            }

            File.WriteAllText(Path.Combine(folder, ContentRelease.CatalogFileName(version)), "catalog " + version);
            File.WriteAllText(Path.Combine(folder, ContentRelease.VersionFileName),
                ContentRelease.VersionJson(version, ContentRelease.CatalogFileName(version), new DateTime(2026, 11, 18, 0, 0, 0, DateTimeKind.Utc)));
        }

        static ContentRepo.Verdict Decide(string checkout)
            => ContentRepo.Decide(ContentRepo.Read(Path.Combine(checkout, ContentRepo.ReleaseFolder), "DojoAddressables"));

        [Test]
        public void EveryMachine_CountsFromGitHub_NotItsOwnFolder()
        {
            Publish(2);
            var bob = Clone("bob");
            var jacob = Clone("jacob");

            Assert.AreEqual(3, Decide(bob).Next);
            Assert.AreEqual(3, Decide(jacob).Next);
        }

        [Test]
        public void TwoMachinesBuildTheSameNumber_OnlyTheFirstPushPublishes_TheOtherBuildsTheNext()
        {
            Publish(2);
            var bob = Clone("bob");
            var jacob = Clone("jacob");

            WriteRelease(bob, 3);
            ContentRepo.Commit(bob, 3);
            WriteRelease(jacob, 3);
            ContentRepo.Commit(jacob, 3);

            Assert.AreEqual(ContentRepo.Step.PublishOrDiscard, Decide(jacob).Step);

            Assert.IsTrue(ContentRepo.Publish(bob), "the first push publishes");
            Assert.IsFalse(ContentRepo.Publish(jacob), "the second push of the same number is refused");

            Assert.AreEqual(ContentRepo.Step.DiscardBuild, Decide(jacob).Step);
            ContentRepo.DiscardBuild(jacob);

            var after = Decide(jacob);
            Assert.AreEqual(ContentRepo.Step.Build, after.Step);
            Assert.AreEqual(4, after.Next, "Jacob's next build counts from Bob's published 3");
        }

        [Test]
        public void BehindGitHub_UpdateThenBuildTheNext()
        {
            Publish(2);
            var bob = Clone("bob");
            var jacob = Clone("jacob");

            WriteRelease(bob, 3);
            ContentRepo.Commit(bob, 3);
            Assert.IsTrue(ContentRepo.Publish(bob));

            Assert.AreEqual(ContentRepo.Step.Update, Decide(jacob).Step);
            ContentRepo.Update(jacob);
            Assert.AreEqual(4, Decide(jacob).Next);
        }

        [Test]
        public void StrayFileInTheReleaseFolder_DiscardChangesRemovesOnlyThat()
        {
            Publish(2);
            var bob = Clone("bob");
            File.WriteAllText(Path.Combine(bob, ContentRepo.ReleaseFolder, "stray.bundle"), "stray");
            File.WriteAllText(Path.Combine(bob, "notes.txt"), "kept");

            Assert.AreEqual(ContentRepo.Step.DiscardChanges, Decide(bob).Step);
            ContentRepo.DiscardChanges(bob);

            Assert.AreEqual(ContentRepo.Step.Build, Decide(bob).Step);
            Assert.IsTrue(File.Exists(Path.Combine(bob, "notes.txt")), "changes outside the release folder are kept");
        }

        [Test]
        public void ACheckoutOfAnotherRepository_IsRefused()
        {
            Publish(2);
            var bob = Clone("bob");

            var state = ContentRepo.Read(Path.Combine(bob, ContentRepo.ReleaseFolder), "HiroLabz/SomethingElse");

            Assert.IsNotNull(state.FolderProblem);
            Assert.AreEqual(ContentRepo.Step.Blocked, ContentRepo.Decide(state).Step);
        }

        [Test]
        public void ADevelopmentFolderOutsideAnyCheckout_IsRefused()
        {
            var loose = Path.Combine(temp, "loose", ContentRepo.ReleaseFolder);
            Directory.CreateDirectory(loose);

            Assert.IsNotNull(ContentRepo.Read(loose, "DojoAddressables").FolderProblem);
        }
    }
}
