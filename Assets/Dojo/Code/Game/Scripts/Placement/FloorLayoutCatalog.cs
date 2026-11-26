using System;
using System.Collections.Generic;
using Dojo.Framework.Content;
using UnityEngine;

namespace Dojo.Game.Placement
{
    /// <summary>A floor layout the Layouts tab offers: its name, and the world file it is built from.</summary>
    /// <remarks>The same four fields as <see cref="RoomEntry"/>, kept apart so a layout index can never be read as a room index.</remarks>
    [Serializable]
    public sealed class FloorLayoutEntry
    {
        public string name;

        /// <summary>
        /// Address of the layout's world file: <c>&lt;pack&gt;/Floors/default_floor_&lt;name&gt;</c> for
        /// the pack's layouts, <c>custom/floor_&lt;name&gt;</c> for the ones made here.
        /// </summary>
        public string address;

        /// <summary>
        /// Content address of the layout's thumbnail: <c>&lt;pack&gt;/Floors/Thumbnails/floor_&lt;name&gt;</c>.
        /// Rendered by <c>Tools ▸ Dojo ▸ Content ▸ Floors ▸ Generate Floor Thumbnails</c>.
        /// </summary>
        public string icon;

        /// <summary>
        /// What kind of pack layout it is: <see cref="DefaultTag"/> for the pack's original layouts,
        /// <see cref="PromotedTag"/> for one made in the floor editor and promoted into the pack.
        /// </summary>
        public string tag;

        /// <summary>One of the pack's original layouts.</summary>
        public const string DefaultTag = "default";

        /// <summary>A layout made in the floor editor and promoted into the pack.</summary>
        public const string PromotedTag = "promoted";
    }

    /// <summary>
    /// The floor layouts each loaded pack offers, and the world files they are built from.
    /// </summary>
    /// <remarks>
    /// <see cref="RoomCatalog"/> for floors (ClickUp 14ze67xgf0v): every pack may carry a
    /// <c>&lt;pack&gt;/Floors/floors</c> index naming its layouts in the order the tab lists them,
    /// and a layout itself is a world library file of one floor, the same shape as a room file,
    /// authored under <c>Process/&lt;pack&gt;/Floors</c>. The layouts made here come after the
    /// packs', oldest first, from <see cref="CustomFloorLayouts"/>.
    /// </remarks>
    public sealed class FloorLayoutCatalog
    {
        [Serializable]
        sealed class FloorIndex
        {
            public List<FloorLayoutEntry> floors = new List<FloorLayoutEntry>();
        }

        readonly IContentService content;
        readonly CustomFloorLayouts custom;

        public FloorLayoutCatalog(IContentService content, CustomFloorLayouts custom)
        {
            this.content = content;
            this.custom = custom;
        }

        /// <summary>The layouts made here, kept on disk.</summary>
        public CustomFloorLayouts Custom => custom;

        /// <summary>The pack whose layout list the layouts made here are added to.</summary>
        const string DefaultPack = "default";

        /// <summary>The pack folder the layouts live in, and the second segment of every pack address.</summary>
        public const string FolderName = "Floors";

        /// <summary>Content address of a pack's layout index.</summary>
        public static string IndexAddressFor(string pack) => pack + "/" + FolderName + "/floors";

        /// <summary>Every layout in every loaded pack, in pack order and then index order, then the ones made here.</summary>
        public List<FloorLayoutEntry> All()
        {
            var found = new List<FloorLayoutEntry>();

            var madeLayouts = custom != null ? custom.All() : new List<FloorLayoutEntry>();

            if (content == null)
            {
                found.AddRange(madeLayouts);
                return found;
            }

            foreach (var pack in content.LoadedKeys)
            {
                FloorIndex index;

                // The default pack's list as kept here - edited by saving and deleting layouts -
                // once there is one; every other pack's, and the default's until then, as shipped.
                var kept = pack == DefaultPack && custom != null ? custom.ListedLayouts() : null;

                if (kept != null)
                {
                    index = new FloorIndex { floors = kept };
                }
                else
                {
                    TextAsset asset;

                    if (!content.TryGet(IndexAddressFor(pack), out asset) || asset == null)
                    {
                        continue;
                    }

                    try
                    {
                        index = JsonUtility.FromJson<FloorIndex>(asset.text);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning("[Floors] The layout index at '" + IndexAddressFor(pack)
                            + "' could not be read: " + exception.Message);
                        continue;
                    }
                }

                if (index == null || index.floors == null)
                {
                    continue;
                }

                foreach (var layout in index.floors)
                {
                    // A promoted layout is on this list under its pack address and its made file is
                    // gone, so nothing is offered twice. Not filtered by the scene's kit as rooms
                    // are: a layout is a whole floor of the pack's own, and the kit list would have
                    // to name each one by hand.
                    if (layout != null && !string.IsNullOrWhiteSpace(layout.address))
                    {
                        found.Add(layout);
                    }
                }
            }

            // The layouts made here come after the packs', oldest first.
            found.AddRange(madeLayouts);

            return found;
        }

        /// <summary>The layout's thumbnail, or null when it has none yet.</summary>
        public Sprite IconOf(FloorLayoutEntry layout)
        {
            if (layout != null && CustomFloorLayouts.IsCustom(layout.icon))
            {
                return custom != null ? custom.Icon(layout.icon) : null;
            }

            Sprite sprite;

            if (layout == null || string.IsNullOrEmpty(layout.icon))
            {
                return null;
            }

#if UNITY_EDITOR
            // Saved over from the floor editor this session: the thumbnail just rendered, from its file.
            if (CustomFloorLayouts.WasRewritten(layout.icon))
            {
                var rewritten = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(ProjectPathFor(layout.icon, ".png"));

                if (rewritten != null)
                {
                    return rewritten;
                }
            }
#endif

            if (content != null && content.TryGet(layout.icon, out sprite))
            {
                return sprite;
            }

#if UNITY_EDITOR
            // Not in Addressables yet - a layout just promoted, before the generator's next run -
            // so straight from the pack's folder, which is where the generator will find it.
            var fromFile = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(ProjectPathFor(layout.icon, ".png"));

            if (fromFile != null)
            {
                WarnFallback(layout, "thumbnail", layout.icon);
            }

            return fromFile;
#else
            return null;
#endif
        }

        /// <summary>
        /// Where a pack address's file is in the project: <c>&lt;pack&gt;/Floors/x</c> is
        /// <c>Assets/Process/&lt;pack&gt;/Floors/x</c>, as the Addressable Generator addresses it.
        /// </summary>
        static string ProjectPathFor(string address, string extension)
            => "Assets/Process/" + address + extension;

        /// <summary>The addresses already warned about this session, so each is said once.</summary>
        static readonly HashSet<string> warned = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Says that a layout's file or thumbnail came from the project folder, not Addressables.</summary>
        static void WarnFallback(FloorLayoutEntry layout, string what, string address)
        {
            if (!warned.Add(what + ":" + address))
            {
                return;
            }

            Debug.LogWarning("[Floors] '" + layout.name + "' " + what + " is not in Addressables ('" + address
                + "'); read from its file in the project instead. A built game would not find it - run the "
                + "Addressable Generator on the default pack.");
        }

        /// <summary>A piece's prefab, by the content address a layout file names it by.</summary>
        public bool TryPrefab(string address, out GameObject prefab)
        {
            prefab = null;
            return content != null && content.TryGet(address, out prefab) && prefab != null;
        }

        /// <summary>The layout's world, or null when its file is missing or cannot be read.</summary>
        public WorldSave Read(FloorLayoutEntry layout)
        {
            if (layout != null && CustomFloorLayouts.IsCustom(layout.address))
            {
                return custom != null ? custom.Read(layout.address) : null;
            }

            if (layout == null || string.IsNullOrEmpty(layout.address))
            {
                return null;
            }

            TextAsset asset;
            string text = null;

#if UNITY_EDITOR
            // Saved over from the floor editor this session: the file as just written.
            var rewrittenPath = System.IO.Path.Combine(Application.dataPath, "Process", layout.address + ".json");

            if (CustomFloorLayouts.WasRewritten(layout.address) && System.IO.File.Exists(rewrittenPath))
            {
                text = System.IO.File.ReadAllText(rewrittenPath);
            }
            else
#endif
            if (content != null && content.TryGet(layout.address, out asset) && asset != null)
            {
                text = asset.text;
            }
#if UNITY_EDITOR
            else
            {
                // Not in Addressables yet - a layout just promoted, before the generator's next run.
                var path = System.IO.Path.Combine(Application.dataPath, "Process", layout.address + ".json");

                if (System.IO.File.Exists(path))
                {
                    text = System.IO.File.ReadAllText(path);
                    WarnFallback(layout, "layout file", layout.address);
                }
            }
#endif

            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            WorldLibrary library;

            try
            {
                library = JsonUtility.FromJson<WorldLibrary>(text);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Floors] The layout at '" + layout.address + "' could not be read: "
                    + exception.Message);
                return null;
            }

            if (library == null || library.worlds == null || library.worlds.Count == 0)
            {
                return null;
            }

            var world = library.Find(library.CurrentName()) ?? library.worlds[0];

            return world != null ? world.Normalised() : null;
        }
    }
}
