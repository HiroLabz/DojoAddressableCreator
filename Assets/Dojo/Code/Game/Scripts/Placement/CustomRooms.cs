using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Dojo.Framework.Content;
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

        // Where a built game finds the pack's room list and its backup, to start its own from.
        readonly IContentService content;

        /// <summary>
        /// Raised when the rooms on offer have changed - one saved or deleted, or the list put back
        /// to the defaults - so whatever lists them can do so again.
        /// </summary>
        public event Action Changed;

        // Held while DeleteAll works through them, so it is said once at the end, not per room.
        bool quiet;

        void Announce()
        {
            var handler = Changed;
            if (!quiet && handler != null)
            {
                handler();
            }
        }

        public CustomRooms(IContentService content)
        {
            this.content = content;
        }

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
        /// Deletes a room the player made, and everything of it: its file - which holds its area
        /// too - its thumbnail, and both metas. The pack's own rooms cannot be deleted from here.
        /// False, with <paramref name="problem"/> saying why, when nothing was deleted.
        /// </summary>
        /// <remarks>
        /// Only an address of this store, naming a <c>room_</c> file, is taken: the pack's rooms
        /// are <c>default_room_</c> files reached through Addressables, and never match. In the
        /// editor the files go through the asset database, so the metas go with them and any
        /// Addressables entry for them is dropped; in a built game they are simply removed.
        /// Worlds the room was placed in keep their own copy of its pieces and area.
        /// </remarks>
        public bool Delete(string address, out string problem)
        {
            problem = null;

            if (!IsCustom(address))
            {
                problem = "Only rooms you made can be deleted. The pack's own rooms stay.";
                return false;
            }

            var slug = address.Substring(AddressPrefix.Length);

            if (!slug.StartsWith(FilePrefix, StringComparison.Ordinal) || slug.IndexOfAny(new[] { '/', '\\', '.' }) >= 0)
            {
                problem = "'" + address + "' is not one of the rooms you made.";
                return false;
            }

            var json = Path.Combine(Folder, slug + ".json");
            var png = Path.Combine(ThumbnailFolder, slug + ".png");

            if (!File.Exists(json))
            {
                problem = "That room is already gone.";
                return false;
            }

            Remove(json, "Assets/Process/default/Rooms/" + slug + ".json");
            Remove(png, "Assets/Process/default/Rooms/Thumbnails/" + slug + ".png");

            // And out of the pack's room list, which saving it put it on.
            RemoveFromIndex(slug);

            Sprite sprite;
            if (icons.TryGetValue(address, out sprite))
            {
                icons.Remove(address);

                if (sprite != null)
                {
                    UnityEngine.Object.Destroy(sprite.texture);
                    UnityEngine.Object.Destroy(sprite);
                }
            }

            Announce();
            return true;
        }

        /// <summary>
        /// Deletes every room the player made, each as <see cref="Delete"/> does: its file and
        /// area, its thumbnail, both metas, its entry on the room list and - in the editor - its
        /// Addressables entries. The pack's own rooms are not touched. Returns how many went.
        /// </summary>
        public int DeleteAll()
        {
            var deleted = 0;
            quiet = true;

            try
            {
                foreach (var room in All())
                {
                    string problem;

                    if (Delete(room.address, out problem))
                    {
                        deleted++;
                    }
                    else
                    {
                        Debug.LogWarning("[Rooms] '" + room.name + "' was not deleted: " + problem);
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

        /// <summary>One of a room's files, and its meta: through the asset database in the editor.</summary>
        static void Remove(string path, string assetPath)
        {
#if UNITY_EDITOR
            if (UnityEditor.AssetDatabase.DeleteAsset(assetPath))
            {
                return;
            }
#endif
            // Not an asset the editor knows - a build, or a file not imported yet: by hand.
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

            // Not on the pack's room list: a made room is the player's own until it is promoted
            // (see Promote), which is what puts it on the list as one of the pack's rooms.
            Import(slug);

            var address = AddressPrefix + slug;
            icons.Remove(address);

            Announce();
            return address;
        }

        /// <summary>
        /// In the editor, has the project take in the files just written, so they show in the
        /// Project window with their own metas rather than waiting for the next refresh.
        /// </summary>
        static void Import(string slug)
        {
#if UNITY_EDITOR
            if (File.Exists(Path.Combine(Folder, IndexFile)))
            {
                UnityEditor.AssetDatabase.ImportAsset(IndexAssetPath);
            }

            if (File.Exists(Path.Combine(Folder, slug + ".json")))
            {
                UnityEditor.AssetDatabase.ImportAsset("Assets/Process/default/Rooms/" + slug + ".json");
            }

            if (File.Exists(Path.Combine(ThumbnailFolder, slug + ".png")))
            {
                ImportSprite("Assets/Process/default/Rooms/Thumbnails/" + slug + ".png");
            }
#endif
        }

#if UNITY_EDITOR
        /// <summary>
        /// Imports a thumbnail as a single sprite, as every room thumbnail is: a PNG imports as a
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

        /// <summary>
        /// The pack addresses - room files and thumbnails - written over this session by
        /// <see cref="SavePackRoom"/>. Read from the project rather than Addressables from then on,
        /// so what was just saved is what is shown and placed.
        /// </summary>
        static readonly HashSet<string> rewritten = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>True when <paramref name="address"/> was written over by <see cref="SavePackRoom"/> this session.</summary>
        public static bool WasRewritten(string address) => !string.IsNullOrEmpty(address) && rewritten.Contains(address);

        /// <summary>
        /// Writes an edited room over one of the pack's own - an original or a promoted one - in
        /// the editor: its file, its thumbnail, and its prefab in <c>Rooms/Editor/Prefabs</c>, made
        /// again from it. It keeps its name, address and place on the room list. False, with
        /// <paramref name="problem"/> saying why, when nothing was written.
        /// </summary>
        /// <remarks>
        /// The pack ships inside the game, so a built game cannot change it: there, only the rooms
        /// the player made can be edited. Worlds the room was placed in keep their own copy.
        /// </remarks>
        public bool SavePackRoom(RoomEntry entry, WorldSave room, byte[] thumbnail, out string problem)
        {
#if UNITY_EDITOR
            problem = null;

            if (entry == null || string.IsNullOrEmpty(entry.address) || IsCustom(entry.address))
            {
                problem = "That is not one of the pack's rooms.";
                return false;
            }

            var jsonAsset = "Assets/Process/" + entry.address + ".json";
            var jsonPath = Path.Combine(Application.dataPath, "Process", entry.address + ".json");

            if (!File.Exists(jsonPath))
            {
                problem = "The file for '" + entry.name + "' is not at " + jsonAsset + ", so it was not saved.";
                return false;
            }

            // The name inside the file stays as it was - Room_Reception, say - since nothing lists by it.
            var before = ReadFile(jsonPath);
            room.name = before != null && !string.IsNullOrWhiteSpace(before.name) ? before.name : entry.name;
            room.savedUtc = DateTime.UtcNow.ToString("o");

            var library = new WorldLibrary();
            library.worlds.Add(room);
            library.current = room.name;

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

            // default_room_x's prefab is room_x, as the originals' and the promoted rooms' are.
            var file = entry.address.Substring(entry.address.LastIndexOf('/') + 1);
            var prefabSlug = file.StartsWith(PackFilePrefix, StringComparison.Ordinal)
                ? FilePrefix + file.Substring(PackFilePrefix.Length)
                : file;

            MakePrefab(prefabSlug, room);

            Debug.Log("[Rooms] Saved the pack's room '" + entry.name + "' over " + jsonAsset
                + (thumbnail != null && thumbnail.Length > 0 ? ", with a new thumbnail" : "") + ", and made its prefab again.");

            Announce();
            return true;
#else
            problem = "The pack's own rooms can only be edited in the editor. Save it under a new name to keep a copy.";
            return false;
#endif
        }

        // ── The room list ───────────────────────────────────────────────────────────────────

        /// <summary>
        /// The room list the Rooms tab reads, beside the rooms: <c>Assets/Process/default/Rooms/rooms.json</c>
        /// in the editor, which ships in the pack; a copy of it on the device in a built game,
        /// where the pack's own is read-only. Saving a room puts it on the end; deleting one takes
        /// it off.
        /// </summary>
        const string IndexFile = "rooms.json";
        const string IndexAssetPath = "Assets/Process/default/Rooms/" + IndexFile;

        /// <summary>
        /// The pack's own rooms and nothing else: what <see cref="ResetList"/> puts the list back
        /// to. Never written by saving or deleting.
        /// </summary>
        const string DefaultIndexFile = "rooms_default.json";

        /// <summary>The pack's room list and its backup, as content addresses.</summary>
        const string PackIndexAddress = "default/Rooms/rooms";
        const string PackDefaultIndexAddress = "default/Rooms/rooms_default";

        /// <summary>Where the room list is kept: in the project in the editor, on the device in a build.</summary>
        static string ListPath => Path.Combine(Folder, IndexFile);

        /// <summary>The address the Addressable Generator gives a room's file in the pack.</summary>
        static string PackAddressFor(string slug) => "default/Rooms/" + slug;

        /// <summary>The address the Addressable Generator gives a room's thumbnail in the pack.</summary>
        static string PackIconFor(string slug) => "default/Rooms/Thumbnails/" + slug;

        [Serializable]
        sealed class RoomIndex
        {
            public List<RoomEntry> rooms = new List<RoomEntry>();
        }

        /// <summary>
        /// The room list kept here, or null when there is none of its own yet - a built game that
        /// has not saved or deleted a room - and the pack's is the one to read.
        /// </summary>
        public List<RoomEntry> ListedRooms()
        {
            if (!File.Exists(ListPath))
            {
                return null;
            }

            var index = ReadIndex();
            return index != null ? index.rooms : null;
        }

        // ── Promoting a made room into the pack ─────────────────────────────────────────────

        /// <summary>The pack's own rooms' file names start with this: <c>default_room_&lt;name&gt;</c>.</summary>
        const string PackFilePrefix = "default_room_";

        /// <summary>
        /// Makes a room the player made one of the pack's: <c>room_&lt;x&gt;.json</c> becomes
        /// <c>default_room_&lt;x&gt;.json</c>, its thumbnail stays <c>Thumbnails/room_&lt;x&gt;.png</c>,
        /// it goes on the end of <c>rooms.json</c> tagged <see cref="RoomEntry.PromotedTag"/>, and
        /// both files are imported again. From then on the Rooms tab offers it once, as a pack room.
        /// Only in the editor: a built game's pack cannot be changed.
        /// </summary>
        /// <returns>False, with <paramref name="problem"/> saying why, when it was not promoted.</returns>
        public bool Promote(string address, out string problem)
        {
            if (!IsCustom(address))
            {
                problem = "Only a room you made can be promoted; this one is already the pack's.";
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
        /// The promotion itself, on the project's files, for <see cref="Promote"/> and for the
        /// editor's own Promote Room To Pack. <paramref name="slug"/> is the room's file name,
        /// <c>room_&lt;x&gt;</c>. Returns null when done, otherwise why it was not.
        /// </summary>
        public static string PromoteInProject(string slug, out string name)
        {
            name = null;

#if UNITY_EDITOR
            if (string.IsNullOrEmpty(slug) || !slug.StartsWith(FilePrefix, StringComparison.Ordinal)
                || slug.IndexOfAny(new[] { '/', '\\', '.' }) >= 0)
            {
                return "Only a room made in the room editor - a room_<name> file - can be promoted.";
            }

            var x = slug.Substring(FilePrefix.Length);
            const string assets = "Assets/Process/default/Rooms/";
            var from = assets + slug + ".json";
            var to = assets + PackFilePrefix + x + ".json";
            var thumbnail = assets + "Thumbnails/" + slug + ".png";

            if (!File.Exists(Path.Combine(Folder, slug + ".json")))
            {
                return "That room is gone.";
            }

            if (File.Exists(Path.Combine(Folder, PackFilePrefix + x + ".json")))
            {
                return "The pack already has a room called " + PackFilePrefix + x + ".";
            }

            var room = ReadFile(Path.Combine(Folder, slug + ".json"));
            name = room != null && !string.IsNullOrWhiteSpace(room.name) ? room.name.Trim() : x;

            var moved = UnityEditor.AssetDatabase.MoveAsset(from, to);
            if (!string.IsNullOrEmpty(moved))
            {
                return "Could not rename " + from + ": " + moved;
            }

            var entry = new RoomEntry
            {
                name = name,
                address = PackAddressFor(PackFilePrefix + x),
                icon = PackIconFor(slug),
                tag = RoomEntry.PromotedTag,
            };

            // Onto the end of the list, off it under the made room's own address - a room saved
            // before made rooms stopped being listed is on there by it. Not onto rooms_default.json:
            // that stays the pack's 14 originals, and the promoted tag is what a reset goes by.
            ListAtEnd(Path.Combine(Folder, IndexFile), entry, PackAddressFor(slug));

            // A prefab of it beside the pack's original rooms' own, which the thumbnail tool renders
            // from and which is how a room is looked at in the editor.
            if (room != null)
            {
                MakePrefab(slug, room);
            }

            UnityEditor.AssetDatabase.ImportAsset(to, UnityEditor.ImportAssetOptions.ForceUpdate);
            UnityEditor.AssetDatabase.ImportAsset(IndexAssetPath, UnityEditor.ImportAssetOptions.ForceUpdate);

            if (File.Exists(Path.Combine(ThumbnailFolder, slug + ".png")))
            {
                UnityEditor.AssetDatabase.ImportAsset(thumbnail, UnityEditor.ImportAssetOptions.ForceUpdate);
            }

            Debug.Log("[Rooms] Promoted '" + name + "' into the pack: " + to + ", thumbnail " + thumbnail
                + ", listed last in rooms.json. Run the Addressable Generator on the default pack to register it.");

            return null;
#else
            return "Rooms can only be promoted to the pack in the editor.";
#endif
        }

        /// <summary>Where the pack's rooms' prefabs are: editor-only, so the generator leaves them out.</summary>
        const string PrefabAssetFolder = "Assets/Process/default/Rooms/Editor/Prefabs";

        /// <summary>
        /// Builds <c>Rooms/Editor/Prefabs/&lt;slug&gt;.prefab</c> from a room's ground floor, laid out
        /// as the original rooms' prefabs are: a root named for it, and under it Floor, Walls and
        /// Furniture holding an instance of each piece's own prefab - linked, so a change to a floor
        /// tile shows in every room - where the room file puts it. Returns its path, or null.
        /// </summary>
        static string MakePrefab(string slug, WorldSave room)
        {
#if UNITY_EDITOR
            var folderOnDisk = Path.Combine(Application.dataPath, "Process/default/Rooms/Editor/Prefabs");

            if (!Directory.Exists(folderOnDisk))
            {
                Directory.CreateDirectory(folderOnDisk);
                UnityEditor.AssetDatabase.Refresh();
            }

            var root = new GameObject(slug);
            var groups = new Dictionary<string, Transform>();
            var placed = 0;
            var missing = new List<string>();

            try
            {
                var ground = room.floors.Find(f => f != null && f.index == 0);

                foreach (var piece in ground != null ? ground.pieces : new List<WorldSave.SavedPiece>())
                {
                    var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PiecePrefabPath(piece.resource));

                    if (prefab == null)
                    {
                        missing.Add(piece.resource);
                        continue;
                    }

                    var groupName = piece.isFloor ? "Floor"
                        : piece.resource != null && piece.resource.IndexOf("wall", StringComparison.OrdinalIgnoreCase) >= 0 ? "Walls"
                        : "Furniture";

                    Transform group;
                    if (!groups.TryGetValue(groupName, out group))
                    {
                        group = new GameObject(groupName).transform;
                        group.SetParent(root.transform, false);
                        groups[groupName] = group;
                    }

                    var instance = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, group);
                    instance.transform.localPosition = piece.position;
                    instance.transform.localRotation = Quaternion.Euler(0f, piece.rotationSteps * 90f, 0f);
                    instance.transform.localScale = piece.scale == Vector3.zero ? Vector3.one : piece.scale;
                    placed++;
                }

                // Floor, Walls, Furniture, whichever there are, in that order, as the originals have them.
                var order = 0;
                foreach (var name in new[] { "Floor", "Walls", "Furniture" })
                {
                    Transform group;
                    if (groups.TryGetValue(name, out group))
                    {
                        group.SetSiblingIndex(order++);
                    }
                }

                var path = PrefabAssetFolder + "/" + slug + ".prefab";
                UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path);

                Debug.Log("[Rooms] Made " + path + " - " + placed + " piece(s)"
                    + (missing.Count > 0 ? "; no prefab found for " + string.Join(", ", missing) : "") + ".");

                return path;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
#else
            return null;
#endif
        }

        /// <summary>
        /// The prefab a piece's content address names: <c>&lt;pack&gt;/&lt;name&gt;</c> is
        /// <c>Assets/Process/&lt;pack&gt;/Prefabs/&lt;name&gt;.prefab</c>, as the generator addresses them.
        /// </summary>
        static string PiecePrefabPath(string address)
        {
            if (string.IsNullOrEmpty(address))
            {
                return string.Empty;
            }

            var slash = address.IndexOf('/');
            return slash < 0
                ? string.Empty
                : "Assets/Process/" + address.Substring(0, slash) + "/Prefabs/" + address.Substring(slash + 1) + ".prefab";
        }

        /// <summary>
        /// Puts <paramref name="entry"/> on the end of a room list file, taking off any entry at its
        /// address or at <paramref name="replaces"/> first. Nothing happens to a file that is not there.
        /// </summary>
        static void ListAtEnd(string path, RoomEntry entry, string replaces)
        {
            if (!File.Exists(path))
            {
                return;
            }

            RoomIndex index;

            try
            {
                index = JsonUtility.FromJson<RoomIndex>(File.ReadAllText(path)) ?? new RoomIndex();
                index.rooms = index.rooms ?? new List<RoomEntry>();
            }
            catch (Exception exception)
            {
                Debug.LogError("[Rooms] " + path + " could not be read, so it was left as it is: " + exception.Message);
                return;
            }

            index.rooms.RemoveAll(r => r != null
                && (string.Equals(r.address, entry.address, StringComparison.Ordinal)
                    || string.Equals(r.address, replaces, StringComparison.Ordinal)));
            index.rooms.Add(entry);

            File.WriteAllText(path, JsonUtility.ToJson(index, true));
        }

        /// <summary>Takes a room off the room list, and has the editor take the list in again.</summary>
        void RemoveFromIndex(string slug)
        {
            var index = ReadIndex();
            if (index == null)
            {
                return;
            }

            var address = PackAddressFor(slug);

            if (index.rooms.RemoveAll(r => r != null && string.Equals(r.address, address, StringComparison.Ordinal)) == 0)
            {
                return;
            }

            WriteIndex(index);
            ImportList();
        }

        /// <summary>
        /// Puts the room list back to the pack's 14 original rooms, from <c>rooms_default.json</c>,
        /// and - when <paramref name="keepPromoted"/> - the rooms promoted into the pack after them,
        /// in the order they were promoted. Rooms made here are never on it. False, with
        /// <paramref name="problem"/> saying why, when the list could not be put back.
        /// </summary>
        public bool ResetList(bool keepPromoted, out string problem)
        {
            problem = null;

            var current = ReadIndex();
            var promoted = keepPromoted && current != null ? Promoted(current) : new List<RoomEntry>();
            var defaults = ReadDefaults();

            if (defaults == null)
            {
                // No backup - a build whose pack does not have it yet. What the list itself tags as
                // the originals stays, and the promoted rooms when they are being kept.
                if (current == null)
                {
                    problem = "There is no " + DefaultIndexFile + " to put the room list back from, and the "
                        + "room list could not be read, so it was left as it is.";
                    return false;
                }

                defaults = new RoomIndex();
                defaults.rooms.AddRange(current.rooms.FindAll(r => r != null
                    && string.Equals(r.tag, RoomEntry.DefaultTag, StringComparison.OrdinalIgnoreCase)));

                Debug.LogWarning("[Rooms] There is no " + DefaultIndexFile + " to put the room list back from; "
                    + "kept the " + defaults.rooms.Count + " room(s) the list itself tags as the pack's originals.");
            }

            foreach (var room in promoted)
            {
                if (!defaults.rooms.Exists(r => r != null && string.Equals(r.address, room.address, StringComparison.Ordinal)))
                {
                    defaults.rooms.Add(room);
                }
            }

            WriteIndex(defaults);
            ImportList();

            Debug.Log("[Rooms] The room list is back to the pack's original rooms"
                + (keepPromoted ? ", with " + promoted.Count + " promoted room(s) kept." : ", promoted rooms dropped."));
            Announce();
            return true;
        }

        /// <summary>
        /// Deletes every room promoted into the pack, and everything of it: its file and area, its
        /// thumbnail, both metas, its entry on the room list and its Addressables entries. Only in
        /// the editor, where the pack's files are; a built game's pack cannot be changed. The pack's
        /// original rooms - everything in <c>rooms_default.json</c> - are never touched, whatever
        /// the list tags them. Returns how many went.
        /// </summary>
        public int DeletePromoted()
        {
            var current = ReadIndex();

            if (current == null)
            {
                return 0;
            }

            var promoted = Promoted(current);

#if UNITY_EDITOR
            var deleted = 0;

            foreach (var room in promoted)
            {
                // Only ever a default_room_ file, and only one the originals do not list.
                if (!SlugOf(room.address).StartsWith(PackFilePrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                Remove(Path.Combine(Application.dataPath, "Process", room.address + ".json"), "Assets/Process/" + room.address + ".json");

                if (!string.IsNullOrEmpty(room.icon) && SlugOf(room.icon).StartsWith(FilePrefix, StringComparison.Ordinal))
                {
                    Remove(Path.Combine(Application.dataPath, "Process", room.icon + ".png"), "Assets/Process/" + room.icon + ".png");

                    // And the prefab promoting it made, named as its thumbnail is.
                    var prefabName = SlugOf(room.icon) + ".prefab";
                    Remove(Path.Combine(Application.dataPath, "Process/default/Rooms/Editor/Prefabs", prefabName),
                        PrefabAssetFolder + "/" + prefabName);
                }

                current.rooms.RemoveAll(r => r != null && string.Equals(r.address, room.address, StringComparison.Ordinal));
                deleted++;

                Debug.Log("[Rooms] Deleted promoted room '" + room.name + "' (" + room.address + "): its file, area, thumbnail and metas.");
            }

            if (deleted > 0)
            {
                WriteIndex(current);
                ImportList();
                Announce();
            }

            return deleted;
#else
            if (promoted.Count > 0)
            {
                Debug.Log("[Rooms] " + promoted.Count + " promoted room(s) are part of the downloaded pack, which a "
                    + "built game cannot change; they only come off the room list.");
            }

            return 0;
#endif
        }

        /// <summary>
        /// The rooms on a list that are promoted ones: tagged so, and not among the pack's
        /// originals in <c>rooms_default.json</c> - an original is never treated as promoted.
        /// </summary>
        List<RoomEntry> Promoted(RoomIndex index)
        {
            var originals = new HashSet<string>(StringComparer.Ordinal);
            var defaults = ReadDefaults();

            if (defaults != null)
            {
                foreach (var room in defaults.rooms)
                {
                    if (room != null && !string.IsNullOrEmpty(room.address))
                    {
                        originals.Add(room.address);
                    }
                }
            }

            return index.rooms.FindAll(r => r != null
                && string.Equals(r.tag, RoomEntry.PromotedTag, StringComparison.OrdinalIgnoreCase)
                && !originals.Contains(r.address));
        }

        /// <summary>The pack's 14 original rooms, from <c>rooms_default.json</c>; null when there is none.</summary>
        RoomIndex ReadDefaults()
        {
            string text = null;

#if UNITY_EDITOR
            var backup = Path.Combine(Folder, DefaultIndexFile);
            if (File.Exists(backup))
            {
                text = File.ReadAllText(backup);
            }
#else
            TextAsset asset;
            if (content != null && content.TryGet(PackDefaultIndexAddress, out asset) && asset != null)
            {
                text = asset.text;
            }
#endif

            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            try
            {
                var index = JsonUtility.FromJson<RoomIndex>(text);

                if (index == null)
                {
                    return null;
                }

                index.rooms = index.rooms ?? new List<RoomEntry>();
                return index;
            }
            catch (Exception exception)
            {
                Debug.LogError("[Rooms] " + DefaultIndexFile + " could not be read: " + exception.Message);
                return null;
            }
        }

        /// <summary>
        /// The room list as kept here; the pack's, when a built game has none of its own yet, so
        /// the first save or delete starts from it. Null - said in the log - when it cannot be read.
        /// </summary>
        RoomIndex ReadIndex()
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
                return new RoomIndex();
            }

            try
            {
                var index = JsonUtility.FromJson<RoomIndex>(text) ?? new RoomIndex();
                index.rooms = index.rooms ?? new List<RoomEntry>();
                return index;
            }
            catch (Exception exception)
            {
                Debug.LogError("[Rooms] The room list could not be read, so it was left as it is: " + exception.Message);
                return null;
            }
        }

        static void WriteIndex(RoomIndex index)
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(ListPath, JsonUtility.ToJson(index, true));
        }

        /// <summary>In the editor, has the project take the room list in again.</summary>
        static void ImportList()
        {
#if UNITY_EDITOR
            UnityEditor.AssetDatabase.ImportAsset(IndexAssetPath);
#endif
        }

        /// <summary>
        /// The file name an address ends in - <c>room_kitchen</c> for both <c>custom/room_kitchen</c>
        /// and <c>default/Rooms/room_kitchen</c> - so <see cref="RoomCatalog"/> can tell a listed room
        /// that was made here, and offer it once.
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
