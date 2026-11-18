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
    /// One flat folder holds the whole release: <c>version.json</c>, <c>catalog_&lt;version&gt;.bin</c>
    /// with its <c>.hash</c>, and every bundle. The game reads <c>version.json</c> first, then the
    /// catalogue it names, then the bundles, all from that one folder.
    /// <para>
    /// <c>version.json</c> is written only after every file of the release is copied and checked, so
    /// a build that fails part-way leaves the game on the release it already had. Once it points at
    /// the new release, the files of earlier builds are removed, so the folder only ever holds one
    /// release.
    /// </para>
    /// </remarks>
    public static class ContentRelease
    {
        /// <summary>The file the game reads first. Matches the game's <c>ContentVersion.FileName</c>.</summary>
        public const string VersionFileName = "version.json";

        /// <summary>The folder every pack lives in, one folder per pack.</summary>
        public const string ProcessFolder = "Assets/Process";

        /// <summary>A release folder from the earlier layout, which put each release in v&lt;version&gt;/.</summary>
        static readonly Regex OldReleaseFolder = new Regex(@"^v\d+$", RegexOptions.CultureInvariant);

        /// <summary>The files an Addressables build writes: bundles, and catalogues with their hashes.</summary>
        static readonly Regex BuildFile = new Regex(@"(\.bundle$)|(^catalog.*\.(bin|hash|json)$)",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>
        /// The name prefix of the bundles Addressables names from the project rather than their
        /// contents (script references, built-in assets): the version, so no release reuses one.
        /// </summary>
        public static string BundlePrefix(int version) => "dojo_v" + version.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// A bundle's file name with the version's <paramref name="prefix"/> in front, once: a name
        /// that already starts with it (the script-references and built-in bundles) is kept.
        /// Used by <c>VersionedPackedMode</c> for every bundle.
        /// </summary>
        public static string Prefixed(string prefix, string bundleName)
        {
            if (string.IsNullOrEmpty(prefix) || string.IsNullOrEmpty(bundleName))
            {
                return bundleName;
            }

            var cut = bundleName.LastIndexOfAny(new[] { '/', '\\' }) + 1;
            var folder = bundleName.Substring(0, cut);
            var file = bundleName.Substring(cut);

            return file.StartsWith(prefix + "_", StringComparison.Ordinal) || file.StartsWith(prefix + ".", StringComparison.Ordinal)
                ? bundleName
                : folder + prefix + "_" + file;
        }

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

            return ParseVersion(File.ReadAllText(path), path);
        }

        /// <summary>The version in the text of a version.json.</summary>
        /// <param name="source">Where the text came from, for the error message.</param>
        /// <exception cref="InvalidDataException">There is no whole-number version in it.</exception>
        public static int ParseVersion(string json, string source)
        {
            var match = Regex.Match(json ?? string.Empty, "\"version\"\\s*:\\s*(\\d+)\\s*[,}]");
            if (!match.Success)
            {
                throw new InvalidDataException(source + " has no \"version\" that is a whole number. "
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
        /// The build files now in the folder (bundles, catalogues, hashes, and any v&lt;version&gt;
        /// folders from the earlier layout). The next build replaces all of them.
        /// </summary>
        public static List<string> BuildFiles(string outputFolder)
        {
            var found = new List<string>();
            if (!Directory.Exists(outputFolder))
            {
                return found;
            }

            foreach (var file in Directory.GetFiles(outputFolder))
            {
                var name = Path.GetFileName(file);
                if (BuildFile.IsMatch(name))
                {
                    found.Add(name);
                }
            }

            foreach (var directory in Directory.GetDirectories(outputFolder))
            {
                var name = Path.GetFileName(directory);
                if (OldReleaseFolder.IsMatch(name))
                {
                    found.Add(name + "/");
                }
            }

            found.Sort(StringComparer.OrdinalIgnoreCase);
            return found;
        }

        /// <summary>
        /// Everything else in the output folder: not version.json and not a build file. Listed in
        /// the window, never touched.
        /// </summary>
        public static List<string> OtherFiles(string outputFolder)
        {
            var other = new List<string>();
            if (!Directory.Exists(outputFolder))
            {
                return other;
            }

            foreach (var file in Directory.GetFiles(outputFolder))
            {
                var name = Path.GetFileName(file);
                if (!string.Equals(name, VersionFileName, StringComparison.OrdinalIgnoreCase) && !BuildFile.IsMatch(name))
                {
                    other.Add(name);
                }
            }

            foreach (var directory in Directory.GetDirectories(outputFolder))
            {
                var name = Path.GetFileName(directory);
                if (!OldReleaseFolder.IsMatch(name))
                {
                    other.Add(name + "/");
                }
            }

            other.Sort(StringComparer.OrdinalIgnoreCase);
            return other;
        }

        /// <summary>What <see cref="Install"/> put in the folder and what it took out.</summary>
        public sealed class Installed
        {
            public int Bundles;
            public List<string> Removed = new List<string>();
        }

        /// <summary>
        /// Puts a finished build into the output folder as release <paramref name="version"/>:
        /// copies the catalogue, its hash and every bundle into the folder, checks each copy, writes
        /// <c>version.json</c>, then removes the files of earlier builds.
        /// </summary>
        /// <param name="outputFolder">The folder the game loads from.</param>
        /// <param name="version">The release's number; must be the folder's next one.</param>
        /// <param name="catalogFolder">Where the build wrote <c>catalog_&lt;version&gt;.bin</c> and its hash.</param>
        /// <param name="bundlePaths">Every bundle the build wrote.</param>
        /// <param name="builtUtc">When it was built, written into version.json.</param>
        /// <exception cref="ContentReleaseException">
        /// Anything is missing or did not copy whole. version.json and the earlier build's files are
        /// then left as they were.
        /// </exception>
        public static Installed Install(string outputFolder, int version, string catalogFolder,
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

            // A name the folder already has must hold the same bytes: the CDN caches every file
            // for a year by its name, so a changed file under an old name would reach players as
            // the old copy and fail its CRC check. The CDN's deploy refuses it too (content
            // version 3, 18 Nov); this says so before anything is committed or pushed.
            var reused = new List<string>();
            foreach (var source in sources)
            {
                var existing = Path.Combine(outputFolder, Path.GetFileName(source));
                if (File.Exists(existing) && !SameBytes(existing, source))
                {
                    reused.Add(Path.GetFileName(source));
                }
            }

            if (reused.Count > 0)
            {
                throw new ContentReleaseException(reused.Count + " file(s) keep a name the release folder already has, "
                    + "with different bytes, so players would get the old copy. Nothing was changed:\n  "
                    + string.Join("\n  ", reused));
            }

            // The new files go in beside the old ones. A bundle whose content did not change keeps
            // its name, so it is simply written again; the old release stays loadable until
            // version.json moves on.
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { VersionFileName };
            var problems = new List<string>();

            foreach (var source in sources)
            {
                var name = Path.GetFileName(source);
                var target = Path.Combine(outputFolder, name);
                keep.Add(name);
                File.Copy(source, target, true);

                if (!File.Exists(target) || new FileInfo(target).Length != new FileInfo(source).Length)
                {
                    problems.Add(name);
                }
            }

            if (problems.Count > 0)
            {
                throw new ContentReleaseException(problems.Count + " file(s) did not copy whole into " + outputFolder
                    + ", so version.json was left on the previous release:\n  " + string.Join("\n  ", problems));
            }

            WriteVersionFile(outputFolder, version, catalog, builtUtc);

            // Only now that the game is pointed at the new release: everything an earlier build left
            // is removed, so the folder holds exactly one release.
            var installed = new Installed { Bundles = bundlePaths.Count };
            foreach (var name in BuildFiles(outputFolder))
            {
                if (keep.Contains(name))
                {
                    continue;
                }

                if (name.EndsWith("/", StringComparison.Ordinal))
                {
                    Directory.Delete(Path.Combine(outputFolder, name.Substring(0, name.Length - 1)), true);
                }
                else
                {
                    File.Delete(Path.Combine(outputFolder, name));
                }

                installed.Removed.Add(name);
            }

            return installed;
        }

        /// <summary>True when both files have exactly the same bytes.</summary>
        static bool SameBytes(string a, string b)
        {
            var infoA = new FileInfo(a);
            var infoB = new FileInfo(b);
            if (infoA.Length != infoB.Length)
            {
                return false;
            }

            using (var streamA = File.OpenRead(a))
            using (var streamB = File.OpenRead(b))
            {
                var bufferA = new byte[81920];
                var bufferB = new byte[81920];
                int read;
                while ((read = streamA.Read(bufferA, 0, bufferA.Length)) > 0)
                {
                    var offset = 0;
                    while (offset < read)
                    {
                        var got = streamB.Read(bufferB, offset, read - offset);
                        if (got <= 0)
                        {
                            return false;
                        }

                        offset += got;
                    }

                    for (var i = 0; i < read; i++)
                    {
                        if (bufferA[i] != bufferB[i])
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
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
