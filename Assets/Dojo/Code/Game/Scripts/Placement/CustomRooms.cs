using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// The rooms the player has made: each a world file and a thumbnail on disk.
    /// </summary>
    /// <remarks>
    /// <c>room_&lt;name&gt;.json</c> in the same one-floor shape as the pack's rooms, and
    /// <c>room_&lt;name&gt;.png</c> rendered when it was saved, kept in <see cref="Folder"/>:
    /// <c>Assets/Process/default/Rooms</c>, beside the pack's own <c>default_room_&lt;name&gt;</c>
    /// files, so the Addressable Generator can take them into the pack. Addressed here as
    /// <c>custom/room_&lt;name&gt;</c> so <see cref="RoomCatalog"/> can tell them from the pack's,
    /// and listed oldest first, so a room just made is the last in the tab.
    /// <para>
    /// The project's Assets folder only exists in the editor. A built game has none to write to,
    /// so there they are kept in <c>persistentDataPath/Rooms</c> instead.
    /// </para>
    /// </remarks>
    public sealed class CustomRooms
    {
        public const string AddressPrefix = "custom/";

        readonly Dictionary<string, Sprite> icons = new Dictionary<string, Sprite>(StringComparer.Ordinal);

        /// <summary>Every made room's file name starts with this: <c>room_&lt;name&gt;</c>.</summary>
        const string FilePrefix = "room_";

        /// <summary>
        /// Where they are kept: the pack's rooms folder in the editor, and beside the save in a
        /// built game, which has no Assets folder.
        /// </summary>
        public static string Folder
        {
            get
            {
#if UNITY_EDITOR
                return Path.Combine(Application.dataPath, "Process/default/Rooms").Replace('\\', '/');
#else
                return Path.Combine(Application.persistentDataPath, "Rooms").Replace('\\', '/');
#endif
            }
        }

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

            // Only room_ files: the pack's default_room_ files and its rooms index share the folder.
            var files = new List<string>(Directory.GetFiles(Folder, FilePrefix + "*.json"));
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

        /// <summary>
        /// Where their thumbnails are kept: the pack's <c>Rooms/Thumbnails</c> in the editor, beside
        /// the pack rooms' own; beside the rooms themselves in a built game.
        /// </summary>
        public static string ThumbnailFolder
        {
            get
            {
#if UNITY_EDITOR
                return Folder + "/Thumbnails";
#else
                return Folder;
#endif
            }
        }

        /// <summary>True when a room of this name has been made, so saving it again replaces it.</summary>
        public bool Exists(string name)
        {
            return File.Exists(Path.Combine(Folder, Slug(name) + ".json"));
        }

        /// <summary>
        /// False, with <paramref name="problem"/> saying why, when a room of this name would be
        /// written over one of the pack's: its thumbnail shares the pack rooms' folder and naming,
        /// so <c>room_marketing.png</c> is the pack's Marketing thumbnail. A room already made
        /// under the name is fine - saving it again is how it is replaced.
        /// </summary>
        public bool IsNameFree(string name, out string problem)
        {
            problem = null;

            var slug = Slug(name);

            if (Exists(name))
            {
                return true;
            }

            var thumbnailTaken = File.Exists(Path.Combine(ThumbnailFolder, slug + ".png"));
            var prefabTaken = false;
#if UNITY_EDITOR
            prefabTaken = File.Exists(Path.Combine(Folder, "Editor/Prefabs", slug + ".prefab"));
#endif

            if (thumbnailTaken || prefabTaken)
            {
                problem = "'" + name.Trim() + "' is taken by one of the pack's rooms. Give the room another name.";
                return false;
            }

            return true;
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

            var png = Path.Combine(ThumbnailFolder, slug + ".png");

            if (thumbnail != null && thumbnail.Length > 0)
            {
                Directory.CreateDirectory(ThumbnailFolder);
                File.WriteAllBytes(png, thumbnail);
            }

            Import(slug);

            var address = AddressPrefix + slug;
            icons.Remove(address);

            return address;
        }

        /// <summary>
        /// In the editor, has the project take in the files just written, so they show in the
        /// Project window with their own metas rather than waiting for the next refresh.
        /// </summary>
        static void Import(string slug)
        {
#if UNITY_EDITOR
            if (File.Exists(Path.Combine(Folder, slug + ".json")))
            {
                UnityEditor.AssetDatabase.ImportAsset("Assets/Process/default/Rooms/" + slug + ".json");
            }

            if (File.Exists(Path.Combine(ThumbnailFolder, slug + ".png")))
            {
                var thumbnail = "Assets/Process/default/Rooms/Thumbnails/" + slug + ".png";
                UnityEditor.AssetDatabase.ImportAsset(thumbnail);

                // As a single sprite, as every room thumbnail is: a PNG imports as a plain texture,
                // and a card asking for a sprite would get nothing.
                var importer = UnityEditor.AssetImporter.GetAtPath(thumbnail) as UnityEditor.TextureImporter;

                if (importer != null)
                {
                    importer.textureType = UnityEditor.TextureImporterType.Sprite;
                    importer.spriteImportMode = UnityEditor.SpriteImportMode.Single;
                    importer.alphaIsTransparency = true;
                    importer.mipmapEnabled = false;
                    importer.SaveAndReimport();
                }
            }
#endif
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

        /// <summary>The room's file for an address: its world beside the pack's rooms, its picture in Thumbnails.</summary>
        static string PathFor(string address, string extension)
            => Path.Combine(extension == ".png" ? ThumbnailFolder : Folder, address.Substring(AddressPrefix.Length) + extension);

        /// <summary>
        /// A file name for a room name: <c>room_</c>, then the name in lower case, letters and
        /// digits, the rest underscores. A name that already starts "room" is not given it twice.
        /// </summary>
        static string Slug(string name)
        {
            var built = new StringBuilder();

            foreach (var c in (name ?? string.Empty).Trim().ToLowerInvariant())
            {
                built.Append(char.IsLetterOrDigit(c) ? c : '_');
            }

            var slug = built.ToString().Trim('_');

            if (slug.StartsWith(FilePrefix, StringComparison.Ordinal))
            {
                slug = slug.Substring(FilePrefix.Length).Trim('_');
            }

            return FilePrefix + (string.IsNullOrEmpty(slug) ? "unnamed" : slug);
        }
    }
}
