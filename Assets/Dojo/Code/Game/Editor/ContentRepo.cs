using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace Dojo.Game.Editor
{
    /// <summary>
    /// The DojoAddressables checkout the Addressable Builder builds into, and the one version count
    /// every machine shares: <c>development/version.json</c> on GitHub's <c>main</c>.
    /// </summary>
    /// <remarks>
    /// A push to DojoAddressables' <c>main</c> is what publishes content (its deploy-to-s3 workflow), so
    /// GitHub's <c>main</c> is the one place every machine agrees on. The next version is always the
    /// version published there plus one, never what a machine's own folder says. The push itself
    /// claims the number: when two people build the same version, GitHub takes the first push and
    /// rejects the second, which is then discarded and built again as the next one.
    /// <para>
    /// Plain git through <c>git.exe</c>, no Unity calls, so the whole flow is tested against a
    /// throwaway local repository (<c>ContentRepoTests</c>).
    /// </para>
    /// </remarks>
    public static class ContentRepo
    {
        /// <summary>The GitHub repository content is published from.</summary>
        public const string Remote = "HiroLabz/DojoAddressables";

        /// <summary>The branch whose push publishes, and whose version.json is the shared count.</summary>
        public const string Branch = "main";

        /// <summary>The folder inside the checkout that holds the development release.</summary>
        public const string ReleaseFolder = "development";

        /// <summary>
        /// The address the game loads Localhost content from (the game's <c>ContentSettings</c>
        /// Local Root, in <c>RootLifetimeScope.prefab</c>). The same on every machine.
        /// </summary>
        public const string LocalhostRoot = "http://localhost/addressableTest/DojoAddressables/development";

        /// <summary>What git said about the checkout. Filled in by <see cref="Read"/>.</summary>
        public sealed class State
        {
            /// <summary>Why the folder cannot be used at all; null when it is a release folder of the right checkout.</summary>
            public string FolderProblem;

            /// <summary>The checkout's root: the folder above <see cref="ReleaseFolder"/>.</summary>
            public string Root;

            /// <summary>Why GitHub could not be read; null when the fetch worked.</summary>
            public string FetchProblem;

            public string CurrentBranch;
            public int Behind;
            public int Ahead;

            /// <summary>Uncommitted changes under <see cref="ReleaseFolder"/>, as git lists them.</summary>
            public List<string> Uncommitted = new List<string>();

            /// <summary>The version on GitHub's main; null when it has none yet.</summary>
            public int? Published;

            /// <summary>The version in this machine's folder; null when it has none yet.</summary>
            public int? Local;
        }

        /// <summary>What the window may offer, decided from a <see cref="State"/>.</summary>
        public enum Step
        {
            /// <summary>Nothing can be done until the problem is fixed by hand.</summary>
            Blocked,

            /// <summary>GitHub has a newer release: update this checkout first.</summary>
            Update,

            /// <summary>The release folder has uncommitted changes: discard them first.</summary>
            DiscardChanges,

            /// <summary>A version built here is not on GitHub yet: publish it or discard it.</summary>
            PublishOrDiscard,

            /// <summary>Someone else published while this machine's build waited: discard it, then build again.</summary>
            DiscardBuild,

            /// <summary>Up to date with GitHub: build the next version.</summary>
            Build,
        }

        /// <summary>The verdict for the window: what to offer, what to say, and the next version.</summary>
        public sealed class Verdict
        {
            public Step Step;
            public string Message;

            /// <summary>The version a build makes now; -1 unless <see cref="Step"/> is Build.</summary>
            public int Next = -1;
        }

        /// <summary>
        /// Decides what the window may do. Pure: every rule is tested without git.
        /// </summary>
        public static Verdict Decide(State state)
        {
            if (state == null)
            {
                return Blocked("The DojoAddressables checkout has not been read yet.");
            }

            if (state.FolderProblem != null)
            {
                return Blocked(state.FolderProblem);
            }

            if (state.FetchProblem != null)
            {
                return Blocked("GitHub could not be read, so the shared version is unknown and Build is off:\n"
                    + state.FetchProblem);
            }

            if (!string.Equals(state.CurrentBranch, Branch, StringComparison.Ordinal))
            {
                return Blocked("The DojoAddressables checkout is on branch '" + state.CurrentBranch + "'. Switch it to '"
                    + Branch + "': only main publishes, and its version is the one every machine counts from.");
            }

            var published = Describe(state.Published);

            if (state.Ahead > 0 && state.Behind > 0)
            {
                return new Verdict
                {
                    Step = Step.DiscardBuild,
                    Message = "GitHub moved on to " + published + " while " + Describe(state.Local)
                        + " waited on this machine unpublished. Discard it, then build again as the next version.",
                };
            }

            if (state.Ahead > 0)
            {
                return new Verdict
                {
                    Step = Step.PublishOrDiscard,
                    Message = Capital(Describe(state.Local)) + " is built on this machine and not published. GitHub has "
                        + published + ". Publish it, or discard it to go back to " + published + ".",
                };
            }

            if (state.Uncommitted.Count > 0)
            {
                return new Verdict
                {
                    Step = Step.DiscardChanges,
                    Message = state.Uncommitted.Count + " uncommitted change(s) in " + ReleaseFolder + "/, which a build "
                        + "would mix into its release:\n  " + string.Join("\n  ", First(state.Uncommitted, 5))
                        + (state.Uncommitted.Count > 5 ? "\n  …" : string.Empty),
                };
            }

            if (state.Behind > 0)
            {
                return new Verdict
                {
                    Step = Step.Update,
                    Message = "GitHub has " + published + "; this machine has " + Describe(state.Local)
                        + ". Update before building, so the next version counts from GitHub's.",
                };
            }

            if (state.Local != state.Published)
            {
                // Same commit as GitHub but a different number: the folder was edited by hand.
                return Blocked("This machine's version.json says " + Describe(state.Local) + " but GitHub's says "
                    + published + ", on the same commit. Discard the change to version.json first.");
            }

            var next = ContentRelease.NextVersion(state.Published);
            return new Verdict
            {
                Step = Step.Build,
                Next = next,
                Message = "Published on GitHub: " + published + "    →    Build makes version " + next + ".",
            };
        }

        /// <summary>
        /// Checks that <paramref name="folder"/> is the release folder of a checkout, without git:
        /// a full path, there, and named <see cref="ReleaseFolder"/>. Null when it is; why not, when not.
        /// </summary>
        public static string FolderShapeProblem(string folder)
        {
            folder = (folder ?? string.Empty).Trim();

            if (folder.Length == 0)
            {
                return "Choose the '" + ReleaseFolder + "' folder of your DojoAddressables checkout.";
            }

            if (!Path.IsPathRooted(folder))
            {
                return "Use a full path to the '" + ReleaseFolder + "' folder of your DojoAddressables checkout.";
            }

            var name = Path.GetFileName(folder.TrimEnd('\\', '/'));
            if (!string.Equals(name, ReleaseFolder, StringComparison.OrdinalIgnoreCase))
            {
                return "'" + folder + "' is not a '" + ReleaseFolder + "' folder. Choose the '" + ReleaseFolder
                    + "' folder of your DojoAddressables checkout itself"
                    + (name.Length > 1 && name[0] == 'v' && char.IsDigit(name[1])
                        ? " - releases no longer go in v<version> folders."
                        : ".");
            }

            if (!Directory.Exists(folder))
            {
                return "'" + folder + "' does not exist. Clone " + Remote + " and choose its '" + ReleaseFolder + "' folder.";
            }

            return null;
        }

        /// <summary>
        /// Reads the checkout <paramref name="folder"/> belongs to: fetches GitHub, then compares.
        /// </summary>
        /// <param name="folder">The release folder (the checkout's <see cref="ReleaseFolder"/>).</param>
        /// <param name="remote">What origin's URL must contain; <see cref="Remote"/> outside tests.</param>
        public static State Read(string folder, string remote = Remote)
        {
            var state = new State { FolderProblem = FolderShapeProblem(folder) };
            if (state.FolderProblem != null)
            {
                return state;
            }

            folder = Path.GetFullPath(folder.Trim().TrimEnd('\\', '/'));
            state.Root = Path.GetDirectoryName(folder);

            // An empty prefix means the folder above the release folder is the checkout's root.
            // (Asked of git rather than compared as paths: Windows short names such as BOBHIR~1
            // would make the same folder look different.)
            var prefix = Git(state.Root, "rev-parse --show-prefix");
            if (prefix.Code != 0 || prefix.Out.Length != 0)
            {
                state.FolderProblem = "'" + state.Root + "' is not a git checkout. Clone " + Remote
                    + " there and choose its '" + ReleaseFolder + "' folder.";
                return state;
            }

            var url = Git(state.Root, "remote get-url origin");
            if (url.Code != 0 || url.Out.IndexOf(remote, StringComparison.OrdinalIgnoreCase) < 0)
            {
                state.FolderProblem = "'" + state.Root + "' is not a checkout of " + Remote + " (its origin is "
                    + (url.Code == 0 ? url.Out : "not set") + ").";
                return state;
            }

            var fetch = Git(state.Root, "fetch --quiet origin " + Branch, 60000);
            if (fetch.Code != 0)
            {
                state.FetchProblem = fetch.Error;
                return state;
            }

            state.CurrentBranch = Git(state.Root, "rev-parse --abbrev-ref HEAD").Out;

            var counts = Git(state.Root, "rev-list --left-right --count HEAD...origin/" + Branch);
            var parts = counts.Out.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (counts.Code != 0 || parts.Length != 2)
            {
                state.FetchProblem = "git could not compare this checkout with GitHub: " + counts.Error;
                return state;
            }

            state.Ahead = int.Parse(parts[0], CultureInfo.InvariantCulture);
            state.Behind = int.Parse(parts[1], CultureInfo.InvariantCulture);

            foreach (var line in Lines(Git(state.Root, "status --porcelain --untracked-files=all -- " + ReleaseFolder).Out))
            {
                state.Uncommitted.Add(line.Trim());
            }

            var remoteVersion = Git(state.Root, "show origin/" + Branch + ":" + ReleaseFolder + "/" + ContentRelease.VersionFileName);
            try
            {
                state.Published = remoteVersion.Code == 0
                    ? ContentRelease.ParseVersion(remoteVersion.Out, "GitHub's " + ReleaseFolder + "/" + ContentRelease.VersionFileName)
                    : (int?)null;
                state.Local = ContentRelease.CurrentVersion(folder);
            }
            catch (InvalidDataException exception)
            {
                state.FolderProblem = exception.Message;
            }

            return state;
        }

        /// <summary>Brings the checkout up to GitHub's main. Only ever a fast-forward.</summary>
        public static void Update(string root)
        {
            Require(Git(root, "pull --ff-only --quiet origin " + Branch, 120000), "update the checkout");
        }

        /// <summary>
        /// Commits the release folder as "Content version N". Nothing is pushed: that is
        /// <see cref="Publish"/>, so a build can be tried on Localhost first.
        /// </summary>
        public static void Commit(string root, int version)
        {
            Require(Git(root, "add --all -- " + ReleaseFolder), "stage the build");
            Require(Git(root, "commit --quiet -m \"Content version " + version.ToString(CultureInfo.InvariantCulture)
                + "\" -- " + ReleaseFolder), "commit the build");
        }

        /// <summary>
        /// Pushes this machine's release to GitHub's main, which publishes it. False when GitHub
        /// already has a newer release (someone else pushed first): nothing was published.
        /// </summary>
        /// <exception cref="ContentReleaseException">The push failed for any other reason.</exception>
        public static bool Publish(string root)
        {
            var push = Git(root, "push --quiet origin HEAD:" + Branch, 300000);
            if (push.Code == 0)
            {
                return true;
            }

            if (push.Error.IndexOf("rejected", StringComparison.OrdinalIgnoreCase) >= 0
                || push.Error.IndexOf("fetch first", StringComparison.OrdinalIgnoreCase) >= 0
                || push.Error.IndexOf("non-fast-forward", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            throw new ContentReleaseException("The push to GitHub failed, so nothing was published:\n" + push.Error);
        }

        /// <summary>
        /// Drops this machine's unpublished release: the checkout goes back to GitHub's main. Other
        /// folders' uncommitted changes are kept (git refuses rather than overwrite one).
        /// </summary>
        public static void DiscardBuild(string root)
        {
            Require(Git(root, "fetch --quiet origin " + Branch, 60000), "read GitHub");
            Require(Git(root, "reset --quiet --keep origin/" + Branch), "go back to GitHub's release");
        }

        /// <summary>Throws away uncommitted changes in the release folder only.</summary>
        public static void DiscardChanges(string root)
        {
            Require(Git(root, "checkout --quiet HEAD -- " + ReleaseFolder), "restore " + ReleaseFolder);
            Require(Git(root, "clean --quiet -fd -- " + ReleaseFolder), "remove new files in " + ReleaseFolder);
        }

        /// <summary>"version 3", or "no version yet".</summary>
        static string Describe(int? version) => version.HasValue
            ? "version " + version.Value.ToString(CultureInfo.InvariantCulture)
            : "no version yet";

        static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);

        static Verdict Blocked(string message) => new Verdict { Step = Step.Blocked, Message = message };

        static IEnumerable<string> First(List<string> items, int count) => items.GetRange(0, Math.Min(count, items.Count));

        static IEnumerable<string> Lines(string text)
        {
            foreach (var line in (text ?? string.Empty).Split('\n'))
            {
                if (line.Trim().Length > 0)
                {
                    yield return line;
                }
            }
        }

        static void Require(Result result, string what)
        {
            if (result.Code != 0)
            {
                throw new ContentReleaseException("git could not " + what + ":\n" + result.Error);
            }
        }

        /// <summary>What a git command printed and returned.</summary>
        public struct Result
        {
            public int Code;
            public string Out;
            public string Error;
        }

        /// <summary>Runs git in <paramref name="directory"/>. Never prompts: a missing login fails at once.</summary>
        public static Result Git(string directory, string arguments, int timeoutMs = 30000)
        {
            var info = new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = directory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            info.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";

            try
            {
                using (var process = Process.Start(info))
                {
                    var error = process.StandardError.ReadToEndAsync();
                    var output = process.StandardOutput.ReadToEnd();

                    if (!process.WaitForExit(timeoutMs))
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch (Exception)
                        {
                            // Already gone.
                        }

                        return new Result { Code = -1, Out = output.Trim(), Error = "git " + arguments + " took longer than "
                            + (timeoutMs / 1000) + " s and was stopped." };
                    }

                    process.WaitForExit();
                    var message = error.Result.Trim();
                    return new Result
                    {
                        Code = process.ExitCode,
                        Out = output.Trim(),
                        Error = message.Length > 0 ? message : "git " + arguments + " exited with " + process.ExitCode + ".",
                    };
                }
            }
            catch (Exception exception)
            {
                return new Result { Code = -1, Out = string.Empty, Error = "git could not be started (is it installed and on PATH?): "
                    + exception.Message };
            }
        }
    }
}
