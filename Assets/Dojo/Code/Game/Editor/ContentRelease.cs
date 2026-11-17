using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Dojo.Game.Editor
{
    /// <summary>
    /// How a built content release is laid out in the folder the game loads from, and the steps
    /// that put one there. No Unity calls, so every rule here is tested on plain folders.
    /// </summary>
    /// <remarks>
    /// One folder holds everything: <c>version.json</c> at the top, naming the newest release, and
    /// one <c>v&lt;version&gt;</c> folder per release with its catalogue, hash and bundles. The game
    /// reads <c>version.json</c> first, then everything else from the folder it names.
    /// <para>
    /// <c>version.json</c> is written last and only after every file of the release is checked, so
    /// a build that fails part-way leaves the game on the release it already had.
    /// </para>
    /// </remarks>
    public static class ContentRelease
    {
        /// <summary>The file the game reads first. Matches the game's <c>ContentVersion.FileName</c>.</summary>
        public const string VersionFileName = "version.json";

        /// <summary>The folder every pack lives in, one folder per pack.</summary>
        public const string ProcessFolder = "Assets/Process";

        static readonly Regex ReleaseFolder = new Regex(@"^v\d+$", RegexOptions.CultureInvariant);

        /// <summary>The folder one release is served from. Matches the game's <c>ReleaseRootFor</c>.</summary>
        public static string ReleaseFolderName(int version) => "v" + version.ToString(CultureInfo.InvariantCulture);

        /// <summary>The catalogue a release is named by.</summary>
        public static string CatalogFileName(int version) => "catalog_" + version.ToString(CultureInfo.InvariantCulture) + ".bin";

        /// <summary>The version in the folder's <c>version.json</c>, or null when it has none yet.</summary>
        /// <exception cref="InvalidDataException">There is a version.json with no whole-number version.</exception>
        public static int? CurrentVersion(string outputFolder)
        {
            var path = Path.Combine(outputFolder, VersionFileName);
            if (!File.Exists(path))
            {
                return null;
            }

            var match = Regex.Match(File.ReadAllText(path), "\"version\"\\s*:\\s*(\\d+)\\s*[,}]");
            if (!match.Success)
            {
                throw new InvalidDataException(path + " has no \"version\" that is a whole number. "
                    + "Fix or remove it before building.");
            }

            return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        /// <summary>The version the next build gets: 0 for the first, then one more each time.</summary>
        public static int NextVersion(int? current) => current.HasValue ? current.Value + 1 : 0;

        /// <summary>
        /// The pack folders inside <paramref name="processFolder"/>, by name: 'default' first, as the
        /// pack every other inherits categories from, then the rest in name order. Hidden folders and
        /// Unity's ignored ones (ending in <c>~</c>) are left out.
        /// </summary>
        public static List<string> PackNames(string processFolder)
        {
            var names = new List<string>();
            if (!Directory.Exists(processFolder))
            {
                return names;
            }

            foreach (var directory in Directory.GetDirectories(processFolder))
            {
                var name = Path.GetFileName(directory);
                if (name.StartsWith(".", StringComparison.Ordinal) || name.EndsWith("~", StringComparison.Ordinal))
                {
                    continue;
                }

                names.Add(name);
            }

            names.Sort((a, b) =>
            {
                var aDefault = string.Equals(a, AddressableGenerator.DefaultPackName, StringComparison.OrdinalIgnoreCase);
                var bDefault = string.Equals(b, AddressableGenerator.DefaultPackName, StringComparison.OrdinalIgnoreCase);
                return aDefault == bDefault ? StringComparer.OrdinalIgnoreCase.Compare(a, b) : aDefault ? -1 : 1;
            });

            return names;
        }

        /// <summary>
        /// Everything in the output folder the game does not read: anything but <c>version.json</c>
        /// and the <c>v&lt;version&gt;</c> folders. Listed, never deleted.
        /// </summary>
        public static List<string> Leftovers(string outputFolder)
        {
            var leftovers = new List<string>();
            if (!Directory.Exists(outputFolder))
            {
                return leftovers;
            }

            foreach (var file in Directory.GetFiles(outputFolder))
            {
                var name = Path.GetFileName(file);
                if (!string.Equals(name, VersionFileName, StringComparison.OrdinalIgnoreCase))
                {
                    leftovers.Add(name);
                }
            }

            foreach (var directory in Directory.GetDirectories(outputFolder))
            {
                var name = Path.GetFileName(directory);
                if (!ReleaseFolder.IsMatch(name))
                {
                    leftovers.Add(name + "/");
                }
            }

            leftovers.Sort(StringComparer.OrdinalIgnoreCase);
            return leftovers;
        }

        /// <summary>
        /// Puts a finished build into the output folder as release <paramref name="version"/>:
        /// copies the catalogue, its hash and every bundle into <c>v&lt;version&gt;</c>, checks each
        /// copy, then writes <c>version.json</c>.
        /// </summary>
        /// <param name="outputFolder">The folder the game loads from.</param>
        /// <param name="version">The release's number; must be the folder's next one.</param>
        /// <param name="catalogFolder">Where the build wrote <c>catalog_&lt;version&gt;.bin</c> and its hash.</param>
        /// <param name="bundlePaths">Every bundle the build wrote.</param>
        /// <param name="builtUtc">When it was built, written into version.json.</param>
        /// <returns>How many bundles went in.</returns>
        /// <exception cref="ContentReleaseException">
        /// Anything is missing or did not copy whole. version.json is then left as it was.
        /// </exception>
        public static int Install(string outputFolder, int version, string catalogFolder,
            IList<string> bundlePaths, DateTime builtUtc)
        {
            var current = CurrentVersion(outputFolder);
            if (version != NextVersion(current))
            {
                throw new ContentReleaseException("This folder is on "
                    + (current.HasValue ? "version " + current.Value : "no version yet") + ", so the next build is version "
                    + NextVersion(current) + ", not " + version + ". Nothing was changed.");
            }

            if (bundlePaths == null || bundlePaths.Count == 0)
            {
                throw new ContentReleaseException("The build wrote no bundles. Nothing was changed.");
            }

            var catalog = CatalogFileName(version);
            var hash = Path.ChangeExtension(catalog, ".hash");
            var sources = new List<string> { Path.Combine(catalogFolder, catalog), Path.Combine(catalogFolder, hash) };
            sources.AddRange(bundlePaths);

            var missing = sources.FindAll(path => !File.Exists(path));
            if (missing.Count > 0)
            {
                throw new ContentReleaseException("The build did not write " + missing.Count + " file(s), so nothing was "
                    + "changed:\n  " + string.Join("\n  ", missing));
            }

            // Nothing points at this folder yet - version.json still names the current release - so
            // anything in it is from a build that never finished, and is replaced.
            var release = Path.Combine(outputFolder, ReleaseFolderName(version));
            if (Directory.Exists(release))
            {
                Directory.Delete(release, true);
            }

            Directory.CreateDirectory(release);

            var problems = new List<string>();
            foreach (var source in sources)
            {
                var target = Path.Combine(release, Path.GetFileName(source));
                File.Copy(source, target, true);

                if (!File.Exists(target) || new FileInfo(target).Length != new FileInfo(source).Length)
                {
                    problems.Add(Path.GetFileName(source));
                }
            }

            if (problems.Count > 0)
            {
                throw new ContentReleaseException(problems.Count + " file(s) did not copy whole into " + release
                    + ", so version.json was left on the previous release:\n  " + string.Join("\n  ", problems));
            }

            WriteVersionFile(outputFolder, version, catalog, builtUtc);
            return bundlePaths.Count;
        }

        /// <summary>The text of version.json for a release.</summary>
        public static string VersionJson(int version, string catalog, DateTime builtUtc)
            => "{\n"
               + "  \"version\": " + version.ToString(CultureInfo.InvariantCulture) + ",\n"
               + "  \"catalog\": \"" + catalog + "\",\n"
               + "  \"built\": \"" + builtUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) + "\"\n"
               + "}\n";

        /// <summary>
        /// Writes version.json through a temporary file and a rename, so a server never hands the
        /// game half a file.
        /// </summary>
        static void WriteVersionFile(string outputFolder, int version, string catalog, DateTime builtUtc)
        {
            var path = Path.Combine(outputFolder, VersionFileName);
            var temporary = path + ".tmp";

            File.WriteAllText(temporary, VersionJson(version, catalog, builtUtc));

            if (File.Exists(path))
            {
                File.Replace(temporary, path, null);
            }
            else
            {
                File.Move(temporary, path);
            }
        }
    }

    /// <summary>A content release could not be put in place. The message says why, for a popup.</summary>
    public sealed class ContentReleaseException : Exception
    {
        public ContentReleaseException(string message) : base(message)
        {
        }
    }
}
