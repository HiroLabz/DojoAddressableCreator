using System.IO;
using UnityEngine;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// The library of saved worlds on disk. Reads it, writes it, and knows where it lives.
    /// </summary>
    /// <remarks>
    /// The half of world saving that touches no scene at all: no GameObject, no Transform, no
    /// component. That is what makes it worth having on its own — it can be exercised against a
    /// temporary folder with no scene loaded, which is not true of anything that rebuilds a world.
    /// <see cref="WorldService"/> is the half that does the rebuilding, and it owns one of these.
    /// </remarks>
    public sealed class WorldLibraryFile
    {
        /// <summary>What the library of saved worlds is called on disk.</summary>
        const string FileName = "world.json";

        readonly bool prettyPrint;

        public WorldLibraryFile(WorldSettings settings)
        {
            prettyPrint = settings == null || settings.PrettyPrint;
        }

        /// <summary>
        /// Absolute path the library is read from and written to.
        /// </summary>
        /// <remarks>
        /// Fixed rather than configurable — see <see cref="WorldSettings"/> for why the folder is
        /// not something a scene gets to choose.
        /// </remarks>
        public string FullPath
            => Path.Combine(Application.persistentDataPath, FileName).Replace('\\', '/');

        /// <summary>True when there is something to load.</summary>
        public bool Exists => File.Exists(FullPath);

        /// <summary>
        /// The library on disk, or an empty one.
        /// </summary>
        /// <remarks>
        /// A file from before worlds had names holds a single world at its root rather than a list.
        /// Rather than refuse it, it is taken in as one world called after the file — losing
        /// somebody's built world to a format change would be a poor trade for a few lines saved.
        /// <para>
        /// Read from disk each time rather than cached. The list is short, it is only wanted when a
        /// dialog is about to be opened, and a cache would go stale the moment anything else wrote
        /// to the same file.
        /// </para>
        /// </remarks>
        public WorldLibrary Read()
        {
            if (!Exists)
            {
                return new WorldLibrary();
            }

            var text = File.ReadAllText(FullPath);
            var library = JsonUtility.FromJson<WorldLibrary>(text);

            if (library != null && library.worlds != null && library.worlds.Count > 0)
            {
                return library;
            }

            var single = JsonUtility.FromJson<WorldSave>(text);

            if (single != null && single.pieces != null && single.pieces.Count > 0)
            {
                single.name = string.IsNullOrEmpty(single.name)
                    ? Path.GetFileNameWithoutExtension(FullPath)
                    : single.name;

                Debug.Log("[World] " + FullPath + " is from before worlds had names; taking it in as '"
                    + single.name + "'.");

                var upgraded = new WorldLibrary();
                upgraded.Put(single);

                return upgraded;
            }

            return library ?? new WorldLibrary();
        }

        /// <summary>Writes the library back over whatever was there.</summary>
        public void Write(WorldLibrary library)
        {
            var path = FullPath;
            var folder = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            // No AssetDatabase.Refresh: with the folder fixed, the file lives outside the
            // project, so it is none of the asset database's business.
            File.WriteAllText(path, JsonUtility.ToJson(library, prettyPrint));
        }
    }
}
