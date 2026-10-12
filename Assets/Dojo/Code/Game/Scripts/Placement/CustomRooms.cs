using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// The rooms the player has made: each a world file and a thumbnail on disk, beside the save.
    /// </summary>
    /// <remarks>
    /// <c>persistentDataPath/Rooms/&lt;name&gt;.json</c> in the same one-floor shape as the pack's
    /// rooms, and <c>&lt;name&gt;.png</c> rendered when it was saved. Addressed as
    /// <c>custom/&lt;name&gt;</c> so <see cref="RoomCatalog"/> can tell them from the pack's, and
    /// listed oldest first, so a room just made is the last in the tab. Not Addressables: those are
    /// built in the editor and read-only at runtime, and these are made while the game runs.
    /// </remarks>
    public sealed class CustomRooms
    {
        public const string AddressPrefix = "custom/";

        readonly Dictionary<string, Sprite> icons = new Dictionary<string, Sprite>(StringComparer.Ordinal);

        /// <summary>Where they are kept.</summary>
        public static string Folder => Path.Combine(Application.persistentDataPath, "Rooms").Replace('\\', '/');

        public static bool IsCustom(string address)
            => !string.IsNullOrEmpty(address) && address.StartsWith(AddressPrefix, StringComparison.Ordinal);

        /// <summary>Every room made so far, oldest first.</summary>
        public List<RoomEntry> All()
        {
            var found = new List<RoomEntry>();

            if (!Directory.Exists(Folder))
            {
                return found;
            }

            var files = new List<string>(Directory.GetFiles(Folder, "*.json"));
            files.Sort((a, b) =>
            {
                var byAge = File.GetCreationTimeUtc(a).CompareTo(File.GetCreationTimeUtc(b));
                return byAge != 0 ? byAge : string.CompareOrdinal(a, b);
            });

            foreach (var file in files)
            {
                var slug = Path.GetFileNameWithoutExtension(file);
                var world = ReadFile(file);

                if (world == null)
                {
                    continue;
                }

                found.Add(new RoomEntry
                {
                    name = string.IsNullOrWhiteSpace(world.name) ? slug : world.name,
                    address = AddressPrefix + slug,
                    icon = AddressPrefix + slug,
                });
            }

            return found;
        }

        /// <summary>A made room's world, or null.</summary>
        public WorldSave Read(string address)
        {
            return IsCustom(address) ? ReadFile(PathFor(address, ".json")) : null;
        }

        /// <summary>A made room's thumbnail, or null when it has none.</summary>
        public Sprite Icon(string address)
        {
            if (!IsCustom(address))
            {
                return null;
            }

            Sprite sprite;
            if (icons.TryGetValue(address, out sprite) && sprite != null)
            {
                return sprite;
            }

            var path = PathFor(address, ".png");

            if (!File.Exists(path))
            {
                return null;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

            if (!texture.LoadImage(File.ReadAllBytes(path)))
            {
                UnityEngine.Object.Destroy(texture);
                return null;
            }

            sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            sprite.name = Path.GetFileNameWithoutExtension(path);
            icons[address] = sprite;

            return sprite;
        }

        /// <summary>True when a room of this name has been made, so saving it again replaces it.</summary>
        public bool Exists(string name)
        {
            return File.Exists(Path.Combine(Folder, Slug(name) + ".json"));
        }

        /// <summary>
        /// Writes a room and its thumbnail under <paramref name="name"/>, replacing one of that name.
        /// Returns its address.
        /// </summary>
        public string Save(string name, WorldSave room, byte[] thumbnail)
        {
            Directory.CreateDirectory(Folder);

            var slug = Slug(name);
            room.name = name.Trim();
            room.savedUtc = DateTime.UtcNow.ToString("o");

            var library = new WorldLibrary();
            library.worlds.Add(room);
            library.current = room.name;

            File.WriteAllText(Path.Combine(Folder, slug + ".json"), JsonUtility.ToJson(library, true));

            var png = Path.Combine(Folder, slug + ".png");

            if (thumbnail != null && thumbnail.Length > 0)
            {
                File.WriteAllBytes(png, thumbnail);
            }

            var address = AddressPrefix + slug;
            icons.Remove(address);

            return address;
        }

        static WorldSave ReadFile(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                var library = JsonUtility.FromJson<WorldLibrary>(File.ReadAllText(path));

                if (library == null || library.worlds == null || library.worlds.Count == 0)
                {
                    return null;
                }

                var world = library.Find(library.CurrentName()) ?? library.worlds[0];
                return world != null ? world.Normalised() : null;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Rooms] '" + path + "' could not be read: " + exception.Message);
                return null;
            }
        }

        static string PathFor(string address, string extension)
            => Path.Combine(Folder, address.Substring(AddressPrefix.Length) + extension);

        /// <summary>A file name for a room name: lower case, letters and digits, the rest underscores.</summary>
        static string Slug(string name)
        {
            var built = new StringBuilder();

            foreach (var c in (name ?? string.Empty).Trim().ToLowerInvariant())
            {
                built.Append(char.IsLetterOrDigit(c) ? c : '_');
            }

            var slug = built.ToString().Trim('_');
            return string.IsNullOrEmpty(slug) ? "room" : slug;
        }
    }
}
