using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Dojo.Framework.Content;
using UnityEngine;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// The floor layouts made here: each a world file and a thumbnail on disk.
    /// </summary>
    /// <remarks>
    /// <see cref="CustomRooms"/> for floors (ClickUp 14ze67xgf0v), copied and trimmed rather than
    /// shared through a base class, so the rooms' behaviour is untouched: <c>floor_&lt;name&gt;.json</c>
    /// in the same one-floor shape as the pack's layouts, and <c>floor_&lt;name&gt;.png</c> rendered
    /// when it was saved, kept in <see cref="Folder"/>: <c>Assets/Process/default/Floors</c>, beside
    /// the pack's own <c>default_floor_&lt;name&gt;</c> files, so the Addressable Generator can take
    /// them into the pack. Addressed here as <c>custom/floor_&lt;name&gt;</c> so
    /// <see cref="FloorLayoutCatalog"/> can tell them from the pack's, and listed oldest first.
    /// <para>
    /// Left out of the rooms' store on purpose: the prefab a promoted room gets (a layout is laid
    /// from its file, never from a prefab), and the room list's reset and promoted-delete, which
    /// RESET WORLD runs; a layout list has no reset yet.
    /// </para>
    /// <para>
    /// The folder is an instance field, not a static, so a test can point the store at a folder of
    /// its own instead of the project's. The game's container gives it the default.
    /// </para>
    /// </remarks>
    public sealed class CustomFloorLayouts
    {
        public const string AddressPrefix = "custom/";

        /// <summary>Every made layout's file name starts with this: <c>floor_&lt;name&gt;</c>.</summary>
        public const string FilePrefix = "floor_";

        /// <summary>The pack's own layouts' file names start with this: <c>default_floor_&lt;name&gt;</c>.</summary>
        public const string PackFilePrefix = "default_floor_";

        const string Pack = "default";
        const string IndexFile = "floors.json";

        /// <summary>The pack's layout list, as a content address.</summary>
        const string PackIndexAddress = "default/Floors/floors";

        readonly Dictionary<string, Sprite> icons = new Dictionary<string, Sprite>(StringComparer.Ordinal);

        // Where a built game finds the pack's layout list, to start its own from.
        readonly IContentService content;

        readonly string folder;

        /// <summary>
        /// Raised when the layouts on offer have changed - one saved, deleted or promoted - so
        /// whatever lists them can do so again.
        /// </summary>
        public event Action Changed;

        // Held while DeleteAll works through them, so it is said once at the end, not per layout.
        bool quiet;

        void Announce()
        {
            var handler = Changed;
            if (!quiet && handler != null)
            {
                handler();
            }
        }

        /// <summary>The store on the project's own folder, as the game's container builds it.</summary>
        [VContainer.Inject]
        public CustomFloorLayouts(IContentService content) : this(content, null)
        {
        }

        /// <summary>The store on <paramref name="folder"/>; the project's own when null or empty.</summary>
        public CustomFloorLayouts(IContentService content, string folder)
        {
            this.content = content;
            this.folder = (string.IsNullOrEmpty(folder) ? DefaultFolder : folder).Replace('\\', '/').TrimEnd('/');
        }

        /// <summary>
        /// Where they are kept by default: the pack's floors folder in the editor, and beside the
        /// save in a built game, which has no Assets folder.
        /// </summary>
        public static string DefaultFolder
        {
            get
            {
#if UNITY_EDITOR
                return Path.Combine(Application.dataPath, "Process/default/Floors").Replace('\\', '/');
#else
                return Path.Combine(Application.persistentDataPath, "Floors").Replace('\\', '/');
#endif
            }
        }

        /// <summary>Where this store keeps its layouts.</summary>
        public string Folder => folder;

        /// <summary>
        /// Where their thumbnails are kept: the folder's <c>Thumbnails</c> in the editor, beside
        /// the pack layouts' own; beside the layouts themselves in a built game.
        /// </summary>
        public string ThumbnailFolder
        {
            get
            {
#if UNITY_EDITOR
                return folder + "/Thumbnails";
#else
                return folder;
#endif
            }
        }

        /// <summary>True for an address of a layout made here: <c>custom/floor_&lt;name&gt;</c>.</summary>
        public static bool IsCustom(string address)
            => !string.IsNullOrEmpty(address) && address.StartsWith(AddressPrefix + FilePrefix, StringComparison.Ordinal);

        /// <summary>Every layout made so far, oldest first.</summary>
        public List<FloorLayoutEntry> All()
        {
            var found = new List<FloorLayoutEntry>();

            if (!Directory.Exists(folder))
            {
                return found;
            }

            // Only floor_ files: the pack's default_floor_ files and its index share the folder.
            var files = new List<string>(Directory.GetFiles(folder, FilePrefix + "*.json"));
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

                found.Add(new FloorLayoutEntry
                {
                    name = string.IsNullOrWhiteSpace(world.name) ? slug : world.name,
                    address = AddressPrefix + slug,
                    icon = AddressPrefix + slug,
                });
            }

            return found;
        }

        /// <summary>A made layout's world, or null.</summary>
        public WorldSave Read(string address)
        {
            return IsCustom(address) ? ReadFile(PathFor(address, ".json")) : null;
        }

        /// <summary>A made layout's thumbnail, or null when it has none.</summary>
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
                Dispose(texture);
                return null;
            }

            sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            sprite.name = Path.GetFileNameWithoutExtension(path);
            icons[address] = sprite;

            return sprite;
        }

        /// <summary>True when a layout of this name has been made, so saving it again replaces it.</summary>
        public bool Exists(string name)
        {
            return File.Exists(Path.Combine(folder, Slug(name) + ".json"));
        }

        /// <summary>
        /// False, with <paramref name="problem"/> saying why, when a layout of this name would be
        /// written over one of the pack's: its thumbnail shares the pack layouts' folder and
        /// naming, so <c>floor_open_plan.png</c> is the pack's Open Plan thumbnail. A layout already
        /// made under the name is fine - saving it again is how it is replaced.
        /// </summary>
        public bool IsNameFree(string name, out string problem)
        {
            problem = null;

            var slug = Slug(name);

            if (Exists(name))
            {
                return true;
            }

            if (File.Exists(Path.Combine(ThumbnailFolder, slug + ".png"))
                || File.Exists(Path.Combine(folder, PackFilePrefix + slug.Substring(FilePrefix.Length) + ".json")))
            {
                problem = "'" + name.Trim() + "' is taken by one of the pack's floor plans. Give the floor plan another name.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Deletes a layout made here, and everything of it: its file, its thumbnail, and both
        /// metas. The pack's own layouts cannot be deleted from here. False, with
        /// <paramref name="problem"/> saying why, when nothing was deleted.
        /// </summary>
        public bool Delete(string address, out string problem)
        {
            problem = null;

            if (!IsCustom(address))
            {
                problem = "Only floor plans you made can be deleted. The pack's own floor plans stay.";
                return false;
            }

            var slug = address.Substring(AddressPrefix.Length);

            if (slug.IndexOfAny(new[] { '/', '\\', '.' }) >= 0)
            {
                problem = "'" + address + "' is not one of the floor plans you made.";
                return false;
            }

            var json = Path.Combine(folder, slug + ".json");
            var png = Path.Combine(ThumbnailFolder, slug + ".png");

            if (!File.Exists(json))
            {
                problem = "That floor plan is already gone.";
                return false;
            }

            Remove(json);
            Remove(png);

            // And off the pack's layout list, in case an earlier promotion put it there under this name.
            RemoveFromIndex(slug);

            Sprite sprite;
            if (icons.TryGetValue(address, out sprite))
            {
                icons.Remove(address);

                if (sprite != null)
                {
                    Dispose(sprite.texture);
                    Dispose(sprite);
                }
            }

            Announce();
            return true;
        }

        /// <summary>Deletes every layout made here, each as <see cref="Delete"/> does. Returns how many went.</summary>
        public int DeleteAll()
        {
            var deleted = 0;
            quiet = true;

            try
            {
                foreach (var layout in All())
                {
                    string problem;

                    if (Delete(layout.address, out problem))
                    {
                        deleted++;
                    }
                    else
                    {
                        Debug.LogWarning("[Floors] '" + layout.name + "' was not deleted: " + problem);
                    }
                }
            }
            finally
            {
                quiet = false;
            }

            if (deleted > 0)
            {
                Announce();
            }

            return deleted;
        }

        /// <summary>
        /// Writes a layout and its thumbnail under <paramref name="name"/>, replacing one of that
        /// name. Returns its address.
        /// </summary>
        public string Save(string name, WorldSave layout, byte[] thumbnail)
        {
            Directory.CreateDirectory(folder);

            var slug = Slug(name);
            layout.name = name.Trim();
            layout.savedUtc = DateTime.UtcNow.ToString("o");

            var library = new WorldLibrary();
            library.worlds.Add(layout);
            library.current = layout.name;

            File.WriteAllText(Path.Combine(folder, slug + ".json"), JsonUtility.ToJson(library, true));

            var png = Path.Combine(ThumbnailFolder, slug + ".png");

            if (thumbnail != null && thumbnail.Length > 0)
            {
                Directory.CreateDirectory(ThumbnailFolder);
                File.WriteAllBytes(png, thumbnail);
            }

            // Not on the pack's layout list: a made layout is this project's own until it is
            // promoted (see Promote), which is what puts it on the list as one of the pack's.
            Import(slug);

            var address = AddressPrefix + slug;
            icons.Remove(address);

            Announce();
            return address;
        }

        /// <summary>
        /// The pack addresses - layout files and thumbnails - written over this session by
        /// <see cref="SavePackLayout"/>. Read from the project rather than Addressables from then on.
        /// </summary>
        static readonly HashSet<string> rewritten = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>True when <paramref name="address"/> was written over by <see cref="SavePackLayout"/> this session.</summary>
        public static bool WasRewritten(string address) => !string.IsNullOrEmpty(address) && rewritten.Contains(address);

        /// <summary>
        /// Writes an edited layout over one of the pack's own - an original or a promoted one - in
        /// the editor: its file and its thumbnail. It keeps its name, address and place on the
        /// layout list. False, with <paramref name="problem"/> saying why, when nothing was written.
        /// </summary>
        public bool SavePackLayout(FloorLayoutEntry entry, WorldSave layout, byte[] thumbnail, out string problem)
        {
#if UNITY_EDITOR
            problem = null;

            if (entry == null || string.IsNullOrEmpty(entry.address) || IsCustom(entry.address))
            {
                problem = "That is not one of the pack's floor plans.";
                return false;
            }

            var jsonAsset = "Assets/Process/" + entry.address + ".json";
            var jsonPath = Path.Combine(Application.dataPath, "Process", entry.address + ".json");

            if (!File.Exists(jsonPath))
            {
                problem = "The file for '" + entry.name + "' is not at " + jsonAsset + ", so it was not saved.";
                return false;
            }

            // The name inside the file stays as it was, since nothing lists by it.
            var before = ReadFile(jsonPath);
            layout.name = before != null && !string.IsNullOrWhiteSpace(before.name) ? before.name : entry.name;
            layout.savedUtc = DateTime.UtcNow.ToString("o");

            var library = new WorldLibrary();
            library.worlds.Add(layout);
            library.current = layout.name;

            File.WriteAllText(jsonPath, JsonUtility.ToJson(library, true));
            UnityEditor.AssetDatabase.ImportAsset(jsonAsset, UnityEditor.ImportAssetOptions.ForceUpdate);
            rewritten.Add(entry.address);

            if (thumbnail != null && thumbnail.Length > 0 && !string.IsNullOrEmpty(entry.icon))
            {
                var pngPath = Path.Combine(Application.dataPath, "Process", entry.icon + ".png");
                Directory.CreateDirectory(Path.GetDirectoryName(pngPath));
                File.WriteAllBytes(pngPath, thumbnail);
                ImportSprite("Assets/Process/" + entry.icon + ".png");
                rewritten.Add(entry.icon);
            }

            Debug.Log("[Floors] Saved the pack's layout '" + entry.name + "' over " + jsonAsset
                + (thumbnail != null && thumbnail.Length > 0 ? ", with a new thumbnail." : "."));

            Announce();
            return true;
#else
            problem = "The pack's own floor plans can only be edited in the editor. Save it under a new name to keep a copy.";
            return false;
#endif
        }

        // ── The layout list ─────────────────────────────────────────────────────────────────

        /// <summary>Where the layout list is kept: in the folder, beside the layouts.</summary>
        string ListPath => Path.Combine(folder, IndexFile);

        /// <summary>The address the Addressable Generator gives a layout's file in the pack.</summary>
        static string PackAddressFor(string slug) => Pack + "/Floors/" + slug;

        /// <summary>The address the Addressable Generator gives a layout's thumbnail in the pack.</summary>
        static string PackIconFor(string slug) => Pack + "/Floors/Thumbnails/" + slug;

        [Serializable]
        sealed class FloorIndex
        {
            public List<FloorLayoutEntry> floors = new List<FloorLayoutEntry>();
        }

        /// <summary>
        /// The layout list kept here, or null when there is none of its own yet - a built game
        /// that has not promoted or deleted a layout - and the pack's is the one to read.
        /// </summary>
        public List<FloorLayoutEntry> ListedLayouts()
        {
            if (!File.Exists(ListPath))
            {
                return null;
            }

            var index = ReadIndex();
            return index != null ? index.floors : null;
        }

        // ── Promoting a made layout into the pack ───────────────────────────────────────────

        /// <summary>
        /// Makes a layout made here one of the pack's: <c>floor_&lt;x&gt;.json</c> becomes
        /// <c>default_floor_&lt;x&gt;.json</c>, its thumbnail stays <c>Thumbnails/floor_&lt;x&gt;.png</c>,
        /// it goes on the end of <c>floors.json</c> tagged <see cref="FloorLayoutEntry.PromotedTag"/>,
        /// and both files are imported again. Only in the editor: a built game's pack cannot be changed.
        /// </summary>
        /// <returns>False, with <paramref name="problem"/> saying why, when it was not promoted.</returns>
        public bool Promote(string address, out string problem)
        {
            if (!IsCustom(address))
            {
                problem = "Only a floor plan you made can be promoted; this one is already the pack's.";
                return false;
            }

            string name;
            problem = PromoteInProject(address.Substring(AddressPrefix.Length), out name);

            if (problem != null)
            {
                return false;
            }

            icons.Remove(address);
            Announce();
            return true;
        }

        /// <summary>
        /// The promotion itself, on the files, for <see cref="Promote"/> and for the editor's own
        /// Promote Floor Layout To Pack. <paramref name="slug"/> is the layout's file name,
        /// <c>floor_&lt;x&gt;</c>. Returns null when done, otherwise why it was not.
        /// </summary>
        public string PromoteInProject(string slug, out string name)
        {
            name = null;

#if UNITY_EDITOR
            if (string.IsNullOrEmpty(slug) || !slug.StartsWith(FilePrefix, StringComparison.Ordinal)
                || slug.IndexOfAny(new[] { '/', '\\', '.' }) >= 0)
            {
                return "Only a floor plan made in the floor plan editor - a floor_<name> file - can be promoted.";
            }

            var x = slug.Substring(FilePrefix.Length);
            var from = Path.Combine(folder, slug + ".json");
            var to = Path.Combine(folder, PackFilePrefix + x + ".json");

            if (!File.Exists(from))
            {
                return "That floor plan is gone.";
            }

            if (File.Exists(to))
            {
                return "The pack already has a floor plan called " + PackFilePrefix + x + ".";
            }

            var layout = ReadFile(from);
            name = layout != null && !string.IsNullOrWhiteSpace(layout.name) ? layout.name.Trim() : x;

            var fromAsset = AssetPathFor(from);
            var toAsset = AssetPathFor(to);

            if (fromAsset != null && toAsset != null && UnityEditor.AssetDatabase.AssetPathToGUID(fromAsset) != string.Empty)
            {
                var moved = UnityEditor.AssetDatabase.MoveAsset(fromAsset, toAsset);
                if (!string.IsNullOrEmpty(moved))
                {
                    return "Could not rename " + fromAsset + ": " + moved;
                }
            }
            else
            {
                // Not an asset the editor knows - a folder outside the project, as a test's.
                File.Move(from, to);

                if (File.Exists(from + ".meta"))
                {
                    File.Move(from + ".meta", to + ".meta");
                }
            }

            var entry = new FloorLayoutEntry
            {
                name = name,
                address = PackAddressFor(PackFilePrefix + x),
                icon = PackIconFor(slug),
                tag = FloorLayoutEntry.PromotedTag,
            };

            // Onto the end of the list, off it under the made layout's own address first.
            ListAtEnd(ListPath, entry, PackAddressFor(slug));

            if (toAsset != null)
            {
                UnityEditor.AssetDatabase.ImportAsset(toAsset, UnityEditor.ImportAssetOptions.ForceUpdate);
            }

            ImportList();

            var thumbnailAsset = AssetPathFor(Path.Combine(ThumbnailFolder, slug + ".png"));
            if (thumbnailAsset != null && File.Exists(Path.Combine(ThumbnailFolder, slug + ".png")))
            {
                UnityEditor.AssetDatabase.ImportAsset(thumbnailAsset, UnityEditor.ImportAssetOptions.ForceUpdate);
            }

            Debug.Log("[Floors] Promoted '" + name + "' into the pack: " + to + ", thumbnail Thumbnails/" + slug
                + ".png, listed last in " + IndexFile + ". Run the Addressable Generator on the default pack to register it.");

            return null;
#else
            return "Floor plans can only be promoted to the pack in the editor.";
#endif
        }

        /// <summary>
        /// Puts <paramref name="entry"/> on the end of a layout list file, taking off any entry at
        /// its address or at <paramref name="replaces"/> first. A list that is not there yet is made.
        /// </summary>
        static void ListAtEnd(string path, FloorLayoutEntry entry, string replaces)
        {
            FloorIndex index;

            try
            {
                index = File.Exists(path) ? JsonUtility.FromJson<FloorIndex>(File.ReadAllText(path)) ?? new FloorIndex() : new FloorIndex();
                index.floors = index.floors ?? new List<FloorLayoutEntry>();
            }
            catch (Exception exception)
            {
                Debug.LogError("[Floors] " + path + " could not be read, so it was left as it is: " + exception.Message);
                return;
            }

            index.floors.RemoveAll(f => f != null
                && (string.Equals(f.address, entry.address, StringComparison.Ordinal)
                    || string.Equals(f.address, replaces, StringComparison.Ordinal)));
            index.floors.Add(entry);

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(index, true));
        }

        /// <summary>Takes a layout off the layout list, and has the editor take the list in again.</summary>
        void RemoveFromIndex(string slug)
        {
            if (!File.Exists(ListPath))
            {
                return;
            }

            var index = ReadIndex();
            if (index == null)
            {
                return;
            }

            var address = PackAddressFor(slug);

            if (index.floors.RemoveAll(f => f != null && string.Equals(f.address, address, StringComparison.Ordinal)) == 0)
            {
                return;
            }

            File.WriteAllText(ListPath, JsonUtility.ToJson(index, true));
            ImportList();
        }

        /// <summary>
        /// The layout list as kept here; the pack's, when a built game has none of its own yet.
        /// Null - said in the log - when it cannot be read.
        /// </summary>
        FloorIndex ReadIndex()
        {
            string text = null;

            if (File.Exists(ListPath))
            {
                text = File.ReadAllText(ListPath);
            }
            else
            {
                TextAsset asset;
                if (content != null && content.TryGet(PackIndexAddress, out asset) && asset != null)
                {
                    text = asset.text;
                }
            }

            if (string.IsNullOrEmpty(text))
            {
                return new FloorIndex();
            }

            try
            {
                var index = JsonUtility.FromJson<FloorIndex>(text) ?? new FloorIndex();
                index.floors = index.floors ?? new List<FloorLayoutEntry>();
                return index;
            }
            catch (Exception exception)
            {
                Debug.LogError("[Floors] The layout list could not be read, so it was left as it is: " + exception.Message);
                return null;
            }
        }

        // ── Files ───────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The project-relative asset path of a file under <c>Assets</c>, or null for one outside
        /// the project - a test's folder - which the asset database must not be asked about.
        /// </summary>
        static string AssetPathFor(string fullPath)
        {
#if UNITY_EDITOR
            var normalised = fullPath.Replace('\\', '/');
            var dataPath = Application.dataPath.Replace('\\', '/');

            if (normalised.StartsWith(dataPath + "/", StringComparison.OrdinalIgnoreCase))
            {
                return "Assets" + normalised.Substring(dataPath.Length);
            }
#endif
            return null;
        }

        /// <summary>One of a layout's files, and its meta: through the asset database in the editor.</summary>
        static void Remove(string path)
        {
#if UNITY_EDITOR
            var assetPath = AssetPathFor(path);
            if (assetPath != null && UnityEditor.AssetDatabase.DeleteAsset(assetPath))
            {
                return;
            }
#endif
            // Not an asset the editor knows - a build, a test's folder, or a file not imported yet: by hand.
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            if (File.Exists(path + ".meta"))
            {
                File.Delete(path + ".meta");
            }
        }

        /// <summary>
        /// In the editor, has the project take in the files just written, so they show in the
        /// Project window with their own metas rather than waiting for the next refresh.
        /// </summary>
        void Import(string slug)
        {
#if UNITY_EDITOR
            ImportList();

            var json = AssetPathFor(Path.Combine(folder, slug + ".json"));
            if (json != null && File.Exists(Path.Combine(folder, slug + ".json")))
            {
                UnityEditor.AssetDatabase.ImportAsset(json);
            }

            var png = AssetPathFor(Path.Combine(ThumbnailFolder, slug + ".png"));
            if (png != null && File.Exists(Path.Combine(ThumbnailFolder, slug + ".png")))
            {
                ImportSprite(png);
            }
#endif
        }

        /// <summary>In the editor, has the project take the layout list in again.</summary>
        void ImportList()
        {
#if UNITY_EDITOR
            var list = AssetPathFor(ListPath);
            if (list != null && File.Exists(ListPath))
            {
                UnityEditor.AssetDatabase.ImportAsset(list);
            }
#endif
        }

#if UNITY_EDITOR
        /// <summary>
        /// Imports a thumbnail as a single sprite, as every layout thumbnail is: a PNG imports as a
        /// plain texture, and a card asking for a sprite would get nothing.
        /// </summary>
        static void ImportSprite(string assetPath)
        {
            UnityEditor.AssetDatabase.ImportAsset(assetPath, UnityEditor.ImportAssetOptions.ForceUpdate);

            var importer = UnityEditor.AssetImporter.GetAtPath(assetPath) as UnityEditor.TextureImporter;

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

        /// <summary>Lets go of a texture or sprite: at once outside Play, where Destroy is refused.</summary>
        static void Dispose(UnityEngine.Object o)
        {
            if (o == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(o);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(o);
            }
        }

        /// <summary>
        /// The file name an address ends in - <c>floor_open_plan</c> for both <c>custom/floor_open_plan</c>
        /// and <c>default/Floors/floor_open_plan</c> - so <see cref="FloorLayoutCatalog"/> can tell a
        /// listed layout that was made here, and offer it once.
        /// </summary>
        public static string SlugOf(string address)
        {
            if (string.IsNullOrEmpty(address))
            {
                return string.Empty;
            }

            var slash = address.LastIndexOf('/');
            return slash >= 0 ? address.Substring(slash + 1) : address;
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
                Debug.LogWarning("[Floors] '" + path + "' could not be read: " + exception.Message);
                return null;
            }
        }

        /// <summary>The layout's file for an address: its world beside the pack's layouts, its picture in Thumbnails.</summary>
        string PathFor(string address, string extension)
            => Path.Combine(extension == ".png" ? ThumbnailFolder : folder, address.Substring(AddressPrefix.Length) + extension);

        /// <summary>
        /// A file name for a layout name: <c>floor_</c>, then the name in lower case, letters and
        /// digits, the rest underscores. A name that already starts "floor" is not given it twice.
        /// </summary>
        public static string Slug(string name)
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
            else if (slug == "floor")
            {
                slug = string.Empty;
            }

            return FilePrefix + (string.IsNullOrEmpty(slug) ? "unnamed" : slug);
        }
    }
}
