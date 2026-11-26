using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Dojo.Framework.Content;
using Dojo.Game.Placement;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Dojo.Game.Tests.EditMode
{
    /// <summary>
    /// The Layouts tab lists the floor layouts of the packs the player has, in each pack's order,
    /// then the layouts made here; a made layout is read from its own file, a pack's from the content.
    /// Modelled on the game's RoomCatalogTests.
    /// </summary>
    public sealed class FloorLayoutCatalogTests
    {
        const string Index = "{\"floors\":["
            + "{\"name\":\"Open Plan\",\"address\":\"default/Floors/default_floor_open_plan\",\"icon\":\"default/Floors/Thumbnails/floor_open_plan\",\"tag\":\"default\"},"
            + "{\"name\":\"Studio\",\"address\":\"default/Floors/default_floor_studio\",\"icon\":\"default/Floors/Thumbnails/floor_studio\",\"tag\":\"default\"},"
            + "{\"name\":\"\",\"address\":\"\",\"icon\":\"\"}"
            + "]}";

        readonly List<Object> made = new List<Object>();
        string folder;
        WorldSettings activeBefore;

        [SetUp]
        public void SetUp()
        {
            folder = Path.Combine(Path.GetTempPath(), "DojoFloorLayoutCatalogTests_" + Guid.NewGuid().ToString("N")).Replace('\\', '/');
            Directory.CreateDirectory(folder);

            // The open scene's kit filter, if a scene left one: these tests offer everything.
            activeBefore = WorldSettings.Active;
            WorldSettings.Active = null;
        }

        [TearDown]
        public void TearDown()
        {
            WorldSettings.Active = activeBefore;

            foreach (var o in made)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            made.Clear();

            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }

        FakeContent Content(params string[] packs)
        {
            var content = new FakeContent(packs);
            var index = new TextAsset(Index);
            made.Add(index);
            content.Assets["default/Floors/floors"] = index;
            return content;
        }

        CustomFloorLayouts Store() => new CustomFloorLayouts(null, folder);

        static WorldSave Layout(string name, int tiles)
        {
            var layout = new WorldSave { name = name };

            for (var i = 0; i < tiles; i++)
            {
                layout.FloorAt(0).pieces.Add(new WorldSave.SavedPiece
                {
                    resource = "default/test_floor",
                    position = new Vector3(i * 1.2f, 0f, 0f),
                    isFloor = true,
                });
            }

            return layout;
        }

        static string LayoutJson(string name, int tiles)
        {
            var library = new WorldLibrary();
            library.worlds.Add(Layout(name, tiles));
            library.current = name;
            return JsonUtility.ToJson(library, true);
        }

        // ── The packs' layouts ──────────────────────────────────────────────────────────────

        [Test]
        public void All_ListsThePackIndexInOrder_AndSkipsEntriesWithoutAnAddress()
        {
            var layouts = new FloorLayoutCatalog(Content("default"), null).All();

            Assert.That(layouts.Count, Is.EqualTo(2));
            Assert.That(layouts[0].name, Is.EqualTo("Open Plan"));
            Assert.That(layouts[1].address, Is.EqualTo("default/Floors/default_floor_studio"));
            Assert.That(layouts[1].tag, Is.EqualTo(FloorLayoutEntry.DefaultTag));
        }

        [Test]
        public void APackWithoutAnIndex_AddsNoLayouts()
        {
            var layouts = new FloorLayoutCatalog(Content("default", "strawberry"), null).All();

            Assert.That(layouts.Count, Is.EqualTo(2), "only default has an index");
        }

        [Test]
        public void APackThatIsNotLoaded_OffersNothing()
        {
            Assert.That(new FloorLayoutCatalog(Content(), null).All(), Is.Empty);
        }

        [Test]
        public void IndexAddressFor_IsThePacksFloorsIndex()
        {
            Assert.That(FloorLayoutCatalog.IndexAddressFor("strawberry"), Is.EqualTo("strawberry/Floors/floors"));
        }

        [Test]
        public void IconOf_APackLayout_IsTheSpriteTheContentHolds()
        {
            var content = Content("default");
            var texture = new Texture2D(8, 8);
            made.Add(texture);
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, 8f, 8f), new Vector2(0.5f, 0.5f));
            made.Add(sprite);
            content.Assets["default/Floors/Thumbnails/floor_studio"] = sprite;

            var catalog = new FloorLayoutCatalog(content, null);
            var layouts = catalog.All();

            Assert.That(catalog.IconOf(layouts[1]), Is.SameAs(sprite));
            Assert.That(catalog.IconOf(layouts[0]), Is.Null, "Open Plan has no thumbnail here");
        }

        [Test]
        public void Read_APackLayout_ComesFromTheContent()
        {
            var content = Content("default");
            var file = new TextAsset(LayoutJson("Studio", 4));
            made.Add(file);
            content.Assets["default/Floors/default_floor_studio"] = file;

            var catalog = new FloorLayoutCatalog(content, null);
            var layout = catalog.Read(catalog.All()[1]);

            Assert.That(layout, Is.Not.Null);
            Assert.That(layout.name, Is.EqualTo("Studio"));
            Assert.That(layout.floors[0].pieces.Count, Is.EqualTo(4));
            Assert.That(layout.rooms, Is.Not.Null, "not normalised");
        }

        // ── The layouts made here ───────────────────────────────────────────────────────────

        [Test]
        public void All_AppendsTheLayoutsMadeHere_AfterThePacks()
        {
            var store = Store();
            store.Save("Corner", Layout("Corner", 2), null);

            var layouts = new FloorLayoutCatalog(Content("default"), store).All();

            Assert.That(layouts.Count, Is.EqualTo(3));
            Assert.That(layouts[0].name, Is.EqualTo("Open Plan"), "the pack's layouts come first");
            Assert.That(layouts[2].name, Is.EqualTo("Corner"));
            Assert.That(layouts[2].address, Is.EqualTo("custom/floor_corner"));
            Assert.IsTrue(CustomFloorLayouts.IsCustom(layouts[2].address));
            Assert.IsFalse(CustomFloorLayouts.IsCustom(layouts[0].address), "a pack's layout was taken for a made one");
        }

        [Test]
        public void AMadeLayoutThePackAlsoLists_IsOfferedOnce_FromItsFile()
        {
            var content = new FakeContent(new[] { "default" });
            var index = new TextAsset("{\"floors\":[{\"name\":\"Corner (pack copy)\",\"address\":\"default/Floors/floor_corner\",\"icon\":\"default/Floors/Thumbnails/floor_corner\",\"tag\":\"promoted\"}]}");
            made.Add(index);
            content.Assets["default/Floors/floors"] = index;

            var store = Store();
            store.Save("Corner", Layout("Corner", 2), null);

            var layouts = new FloorLayoutCatalog(content, store).All();

            Assert.That(layouts.Count, Is.EqualTo(1));
            Assert.That(layouts[0].address, Is.EqualTo("custom/floor_corner"), "read from its own file, not the pack");
        }

        [Test]
        public void Read_AMadeLayout_ComesFromItsFile()
        {
            var store = Store();
            store.Save("Corner", Layout("Corner", 3), null);

            var catalog = new FloorLayoutCatalog(Content(), store);
            var layout = catalog.Read(catalog.All()[0]);

            Assert.That(layout, Is.Not.Null);
            Assert.That(layout.name, Is.EqualTo("Corner"));
            Assert.That(layout.floors[0].pieces.Count, Is.EqualTo(3));
        }

        [Test]
        public void ADeletedMadeLayout_IsNoLongerListed()
        {
            var store = Store();
            store.Save("Corner", Layout("Corner", 1), null);
            Thread.Sleep(20);
            store.Save("Board", Layout("Board", 1), null);

            var catalog = new FloorLayoutCatalog(Content(), store);
            Assert.That(catalog.All().Count, Is.EqualTo(2));

            string problem;
            Assert.IsTrue(store.Delete("custom/floor_corner", out problem), problem);

            var layouts = catalog.All();
            Assert.That(layouts.Count, Is.EqualTo(1));
            Assert.That(layouts[0].name, Is.EqualTo("Board"));
        }

        [Test]
        public void TheListKeptHere_ReplacesTheDefaultPacksIndex()
        {
            File.WriteAllText(Path.Combine(folder, "floors.json"),
                "{\"floors\":[{\"name\":\"Mine\",\"address\":\"default/Floors/default_floor_mine\",\"icon\":\"default/Floors/Thumbnails/floor_mine\",\"tag\":\"promoted\"}]}");

            var layouts = new FloorLayoutCatalog(Content("default"), Store()).All();

            Assert.That(layouts.Count, Is.EqualTo(1), "the pack's own index is read only until there is a list kept here");
            Assert.That(layouts[0].name, Is.EqualTo("Mine"));
        }

        sealed class FakeContent : IContentService
        {
            readonly List<string> packs;
            public readonly Dictionary<string, Object> Assets = new Dictionary<string, Object>();

            public FakeContent(string[] loaded) => packs = new List<string>(loaded);

            public bool IsReady => true;
            public ContentProgress CurrentProgress => default;
            public event Action<ContentProgress> ProgressChanged { add { } remove { } }
            public Awaitable InitializeAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public IReadOnlyCollection<string> LoadedKeys => packs;
            public bool IsLoaded(string key) => packs.Contains(key);
            public Awaitable<long> GetDownloadSizeAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Awaitable PreloadAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Awaitable PreloadAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Awaitable<T> LoadAsync<T>(string key, CancellationToken cancellationToken = default) where T : Object => throw new NotSupportedException();

            public bool TryGet<T>(string key, out T asset) where T : Object
            {
                Object found;
                asset = key != null && Assets.TryGetValue(key, out found) ? found as T : null;
                return asset != null;
            }

            public void Release<T>(T asset) where T : Object { }
        }
    }
}
