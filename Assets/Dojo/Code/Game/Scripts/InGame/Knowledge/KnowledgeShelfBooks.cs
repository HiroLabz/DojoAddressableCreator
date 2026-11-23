using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dojo.Game.InGame.Knowledge
{
    /// <summary>
    /// Books on the display shelf, one per document in the knowledge base: a visual cue of how much
    /// knowledge it holds (Jacob, 19 Nov). Each book is a random colour and stands in a random free
    /// place on the shelf; one more document is one more book somewhere, the others stay put.
    /// </summary>
    /// <remarks>
    /// Built in code because the content carries only the empty shelf. The places are the display
    /// shelf's own (<c>DisplayShelf.fbx</c>, 0.9 x 1.5 x 0.6 m): four boards, tops at 0.098, 0.449,
    /// 0.8 and 1.15 m, 0.818 m wide inside, open to the front (+z) with the back panel at z -0.1.
    /// <para>
    /// The books wear the shelf's own material, so they draw in a build exactly as the shelf does,
    /// with a property block that reads one white texel of the kit's atlas and tints it: no material
    /// of their own to create, keep or clean up. No colliders: a click on a book is a click on the
    /// shelf behind it.
    /// </para>
    /// <para>
    /// Every display shelf shows the same number, <see cref="Count"/>: in the game, the workspace's
    /// documents; in the creator, <see cref="PreviewCount"/>. Places and colours are seeded by where the shelf stands,
    /// so each shelf looks different and the same shelf looks the same every time.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class KnowledgeShelfBooks : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");

        /// <summary>The tops of the shelf's boards, in metres above its base.</summary>
        static readonly float[] BoardTops = { 0.098f, 0.449f, 0.8f, 1.15f };

        /// <summary>Clear height above each board, to the underside of the next.</summary>
        const float ClearHeight = 0.30f;

        /// <summary>Inside width, from the centre to either side.</summary>
        const float HalfWidth = 0.40f;

        /// <summary>The front face of the back panel: books stand against it.</summary>
        const float Back = -0.095f;

        /// <summary>One place on a board: a book with a little room either side.</summary>
        const float PlaceWidth = 0.057f;

        /// <summary>A white texel of the MVP kit's atlas (<c>BaseColor.png</c>'s middle band), as a UV.</summary>
        static readonly Vector4 WhiteTexel = new Vector4(0f, 0f, 0.5f, 0.55f);

        /// <summary>How many places the shelf has: no more books than this are shown.</summary>
        public static int Capacity => BoardTops.Length * PlacesPerBoard;

        static int PlacesPerBoard => Mathf.FloorToInt(HalfWidth * 2f / PlaceWidth);

        /// <summary>
        /// What the creator's shelves show: the creator reads no knowledge base, so a sample, to see
        /// the books as the game shows them. The game's copy starts at zero and is set from Hiro.
        /// </summary>
        public const int PreviewCount = 12;

        static int count = PreviewCount;
        static Mesh box;

        /// <summary>Raised when <see cref="Count"/> changes.</summary>
        public static event Action CountChanged;

        /// <summary>
        /// How many documents the knowledge base holds: the number of books every display shelf shows,
        /// up to <see cref="Capacity"/>.
        /// </summary>
        public static int Count
        {
            get => count;
            set
            {
                value = Mathf.Max(0, value);
                if (value == count)
                {
                    return;
                }

                count = value;
                CountChanged?.Invoke();
            }
        }

        readonly List<GameObject> books = new List<GameObject>();
        MaterialPropertyBlock block;
        int shown = -1;

        /// <summary>The books standing on this shelf now.</summary>
        public int Shown => books.Count;

        void OnEnable()
        {
            CountChanged += Rebuild;
            Rebuild();
        }

        void OnDisable() => CountChanged -= Rebuild;

        void OnDestroy() => Clear();

        /// <summary>
        /// The places on a shelf in the order books fill them: every place once, shuffled by
        /// <paramref name="seed"/>. Each entry is (board, place on that board).
        /// </summary>
        public static List<Vector2Int> PlaceOrder(int seed)
        {
            var order = new List<Vector2Int>(Capacity);
            for (var board = 0; board < BoardTops.Length; board++)
            {
                for (var place = 0; place < PlacesPerBoard; place++)
                {
                    order.Add(new Vector2Int(board, place));
                }
            }

            var random = new System.Random(seed);
            for (var i = order.Count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            return order;
        }

        /// <summary>Puts as many books on the shelf as <see cref="Count"/> says, if it doesn't have them already.</summary>
        public void Rebuild()
        {
            var wanted = Mathf.Min(Count, Capacity);
            if (wanted == shown && books.Count == wanted)
            {
                return;
            }

            Clear();
            shown = wanted;
            if (wanted == 0)
            {
                return;
            }

            var material = ShelfMaterial();
            if (material == null)
            {
                return;
            }

            var seed = Seed();
            var order = PlaceOrder(seed);
            block ??= new MaterialPropertyBlock();

            for (var i = 0; i < wanted; i++)
            {
                var place = order[i];

                // Each place has its own book: the same size and colour whichever order it came in.
                var random = new System.Random(seed ^ ((place.x + 1) * 7919) ^ ((place.y + 1) * 104729));
                float Between(float min, float max) => min + (float)random.NextDouble() * (max - min);

                var thickness = Between(0.03f, 0.048f);
                var height = Between(0.19f, 0.26f);
                var depth = Between(0.15f, 0.19f);
                var x = -HalfWidth + (place.y + 0.5f) * PlaceWidth + Between(-0.004f, 0.004f);
                var color = Color.HSVToRGB(Between(0f, 1f), Between(0.45f, 0.75f), Between(0.55f, 0.9f));

                var book = new GameObject("Book " + (i + 1));
                book.layer = gameObject.layer;
                book.transform.SetParent(transform, false);
                book.transform.localPosition = new Vector3(x, BoardTops[place.x] + height / 2f, Back + depth / 2f);
                book.transform.localScale = new Vector3(thickness, Mathf.Min(height, ClearHeight - 0.02f), depth);

                book.AddComponent<MeshFilter>().sharedMesh = Box();
                var renderer = book.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;

                block.Clear();
                block.SetColor(BaseColorId, color);
                block.SetVector(BaseMapStId, WhiteTexel);
                renderer.SetPropertyBlock(block);

                books.Add(book);
            }
        }

        void Clear()
        {
            foreach (var book in books)
            {
                if (book == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(book);
                }
                else
                {
                    DestroyImmediate(book);
                }
            }

            books.Clear();
            shown = -1;
        }

        /// <summary>The shelf's own material, from its model: never a book's.</summary>
        Material ShelfMaterial()
        {
            foreach (var part in GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!books.Contains(part.gameObject) && part.sharedMaterial != null)
                {
                    return part.sharedMaterial;
                }
            }

            return null;
        }

        /// <summary>Where the shelf stands, to the grid: the same shelf gets the same books.</summary>
        int Seed()
        {
            var at = transform.position;
            unchecked
            {
                return Mathf.RoundToInt(at.x * 10f) * 73856093 ^ Mathf.RoundToInt(at.y * 10f) * 19349663 ^ Mathf.RoundToInt(at.z * 10f) * 83492791;
            }
        }

        /// <summary>A unit cube, made once: its UVs don't matter, the block points them at one texel.</summary>
        static Mesh Box()
        {
            if (box != null)
            {
                return box;
            }

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            Vector3[] faces = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };

            foreach (var normal in faces)
            {
                var side = new Vector3(normal.y, normal.z, normal.x);
                var up = Vector3.Cross(normal, side);
                var start = vertices.Count;

                vertices.Add((normal - side - up) * 0.5f);
                vertices.Add((normal - side + up) * 0.5f);
                vertices.Add((normal + side + up) * 0.5f);
                vertices.Add((normal + side - up) * 0.5f);
                for (var i = 0; i < 4; i++)
                {
                    normals.Add(normal);
                }

                // Wound so Cross(b - a, c - a) is the outward normal: Unity's front face.
                triangles.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
            }

            box = new Mesh { name = "KnowledgeShelfBooks.box", hideFlags = HideFlags.DontSave };
            box.SetVertices(vertices);
            box.SetNormals(normals);
            box.SetUVs(0, new Vector2[vertices.Count]);
            box.SetTriangles(triangles, 0);
            box.RecalculateBounds();
            return box;
        }
    }
}
