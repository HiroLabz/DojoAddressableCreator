using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// The glowing panel drawn flat on the floor under a piece being placed, showing exactly which
    /// patch of ground it will occupy and whether it may go there — and the same panel in a second
    /// colour under every other piece the player has added to a selection.
    /// </summary>
    /// <remarks>
    /// Built in code rather than authored as a prefab: the mesh is one quad and the texture is a
    /// gradient, so there is nothing to keep in sync in the scene, and a placement grid dropped
    /// into any scene brings its own indicator with it.
    /// <para>
    /// The quad and the glow texture are made once and shared by every panel; only the material
    /// differs, because the two colours are the whole point of having two kinds.
    /// </para>
    /// </remarks>
    public sealed class PlacementHighlight : MonoBehaviour
    {
        /// <summary>One patch of floor to draw a selection panel on.</summary>
        public struct MarkedArea
        {
            /// <summary>Middle of the patch, in world space. Only X and Z are read.</summary>
            public Vector3 Centre;

            /// <summary>Height of the surface the panel lies on.</summary>
            public float Top;

            /// <summary>Size of the patch in metres, before rotation.</summary>
            public Vector2 Size;

            /// <summary>How the patch is turned.</summary>
            public Quaternion Rotation;
        }

        [Tooltip("Colour while the placement is legal.")]
        [SerializeField] Color validColour = new Color(0.35f, 1f, 0.55f, 0.85f);

        [Tooltip("Colour while the placement is refused.")]
        [SerializeField] Color invalidColour = new Color(1f, 0.33f, 0.33f, 0.85f);

        [Tooltip("Colour under a piece added to the selection. The same green as a good " +
                 "placement by default, so every piece the player has picked out is lit the same " +
                 "way whether it is the one in hand or one added to it.")]
        [SerializeField] Color selectedColour = new Color(0.35f, 1f, 0.55f, 0.85f);

        [Tooltip("Colour of floor the player has painted as an agent's roaming area. Dimmer than " +
                 "a placement panel, because a painted area covers a whole room rather than one " +
                 "piece and the same opacity over that much floor reads as a solid green carpet.")]
        [SerializeField] Color paintedColour = new Color(0.35f, 1f, 0.55f, 0.45f);

        [Tooltip("How far above the floor surface the panel sits. Enough to clear the floor without " +
                 "looking detached from it — a coplanar quad flickers as the two fight for depth.")]
        [SerializeField] float lift = 0.015f;

        [Tooltip("Width of the bright rim, as a fraction of the panel's half-extent.")]
        [SerializeField, Range(0.02f, 0.5f)] float rimWidth = 0.16f;

        [Tooltip("Opacity of the panel's interior, relative to the rim.")]
        [SerializeField, Range(0f, 1f)] float fillStrength = 0.35f;

        Transform panel;
        MeshRenderer panelRenderer;
        Material material;

        Material markMaterial;
        Material paintMaterial;
        Mesh quad;
        Texture2D glow;

        readonly List<Transform> marks = new List<Transform>();

        // Painted floor is pooled apart from the selection rather than sharing one pool with it.
        // The two answer to different gestures and one of them can be several hundred panels
        // long, so a shared pool would have a paint session leave that many spare panels behind
        // for every placement afterwards to walk over.
        readonly List<Transform> painted = new List<Transform>();

        /// <summary>True while the panel is being shown.</summary>
        public bool IsVisible => panel != null && panel.gameObject.activeSelf;

        void Awake()
        {
            Build();
            Hide();
        }

        void OnDestroy()
        {
            // Created at runtime, so it is this component's job to clean up.
            if (material != null)
            {
                Destroy(material);
            }

            if (markMaterial != null)
            {
                Destroy(markMaterial);
            }

            if (paintMaterial != null)
            {
                Destroy(paintMaterial);
            }

            if (glow != null)
            {
                Destroy(glow);
            }

            if (quad != null)
            {
                Destroy(quad);
            }
        }

        /// <summary>
        /// Places the panel under a piece. <paramref name="sizeMetres"/> is the piece's unrotated
        /// footprint in world units; <paramref name="rotation"/> turns the panel with the piece, so
        /// a quarter turn visibly swaps its width and depth exactly as the footprint does.
        /// </summary>
        public void Track(Vector3 centre, float floorTop, Vector2 sizeMetres, Quaternion rotation, bool valid)
        {
            if (panel == null)
            {
                Build();
            }

            panel.gameObject.SetActive(true);
            panel.SetPositionAndRotation(new Vector3(centre.x, floorTop + lift, centre.z), rotation);
            panel.localScale = new Vector3(sizeMetres.x, 1f, sizeMetres.y);

            material.SetColor(BaseColorId, valid ? validColour : invalidColour);
        }

        /// <summary>
        /// Draws a selection panel on each of <paramref name="areas"/>, and takes away any left
        /// over from a larger selection.
        /// </summary>
        /// <remarks>
        /// The panels are pooled rather than made and destroyed as the selection changes: a player
        /// adding and removing pieces would otherwise churn a GameObject and a mesh renderer on
        /// every click.
        /// </remarks>
        public void MarkAll(List<MarkedArea> areas)
            => Spread(marks, "PlacementSelectionPanel", MarkMaterial(), areas);

        /// <summary>Takes away every selection panel, leaving the placement one alone.</summary>
        public void ClearMarks() => Retire(marks);

        /// <summary>
        /// Draws a panel on each patch of floor the player has painted as an agent's roaming area,
        /// and takes away any left over from a larger area.
        /// </summary>
        /// <remarks>
        /// A third kind of panel rather than a reuse of the selection: an area is painted while no
        /// piece is in hand, it is committed instead of dropped, and it wants its own colour so a
        /// room being painted is never mistaken for a room-sized piece of furniture about to land.
        /// <para>
        /// Deliberately outside <see cref="Hide"/>, which every placement commit and cancel calls.
        /// A painted area outlives the gesture that drew it — it is what the player is editing —
        /// and a placement finishing has no business erasing it. <see cref="ClearPaint"/> is how it
        /// goes away.
        /// </para>
        /// </remarks>
        public void PaintAll(List<MarkedArea> areas)
            => Spread(painted, "AgentAreaPanel", PaintMaterial(), areas);

        /// <summary>Takes away every painted panel.</summary>
        public void ClearPaint() => Retire(painted);

        /// <summary>
        /// Lays <paramref name="areas"/> out over a pool of panels, growing it when the areas
        /// outnumber it and switching off whatever is left over.
        /// </summary>
        /// <remarks>
        /// Pooled rather than made and destroyed as the set changes: a player dragging a brush
        /// across a room would otherwise churn a GameObject and a mesh renderer on every cell they
        /// crossed, and the panels come and go in their hundreds.
        /// </remarks>
        void Spread(List<Transform> pool, string name, Material withMaterial, List<MarkedArea> areas)
        {
            var wanted = areas == null ? 0 : areas.Count;

            while (pool.Count < wanted)
            {
                pool.Add(BuildPanel(name, withMaterial));
            }

            for (var i = 0; i < pool.Count; i++)
            {
                if (i >= wanted)
                {
                    pool[i].gameObject.SetActive(false);
                    continue;
                }

                var area = areas[i];

                pool[i].gameObject.SetActive(true);
                pool[i].SetPositionAndRotation(
                    new Vector3(area.Centre.x, area.Top + lift, area.Centre.z), area.Rotation);
                pool[i].localScale = new Vector3(area.Size.x, 1f, area.Size.y);
            }
        }

        /// <summary>Switches a whole pool off, keeping the panels for next time.</summary>
        static void Retire(List<Transform> pool)
        {
            for (var i = 0; i < pool.Count; i++)
            {
                if (pool[i] != null)
                {
                    pool[i].gameObject.SetActive(false);
                }
            }
        }

        /// <summary>Takes the panels away. Called when a placement is committed or abandoned.</summary>
        public void Hide()
        {
            if (panel != null)
            {
                panel.gameObject.SetActive(false);
            }

            ClearMarks();
        }

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int MainTexId = Shader.PropertyToID("_BaseMap");

        void Build()
        {
            material = BuildMaterial(validColour);
            panel = BuildPanel("PlacementHighlightPanel", material);
            panelRenderer = panel.GetComponent<MeshRenderer>();
        }

        Material MarkMaterial()
        {
            if (markMaterial == null)
            {
                markMaterial = BuildMaterial(selectedColour);
            }

            return markMaterial;
        }

        Material PaintMaterial()
        {
            if (paintMaterial == null)
            {
                paintMaterial = BuildMaterial(paintedColour);
            }

            return paintMaterial;
        }

        /// <summary>One flat panel, ready to be positioned.</summary>
        Transform BuildPanel(string name, Material withMaterial)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.hideFlags = HideFlags.DontSave;

            var renderer = go.AddComponent<MeshRenderer>();
            go.AddComponent<MeshFilter>().sharedMesh = Quad();
            renderer.sharedMaterial = withMaterial;

            // A flat marker on the floor should not take part in lighting or cast anything.
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            go.SetActive(false);

            return go.transform;
        }

        Mesh Quad() => quad != null ? quad : (quad = BuildQuad());

        /// <summary>A unit quad lying in the XZ plane, facing up.</summary>
        static Mesh BuildQuad()
        {
            var mesh = new Mesh { name = "PlacementHighlightQuad" };

            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3(-0.5f, 0f,  0.5f),
                new Vector3( 0.5f, 0f,  0.5f),
                new Vector3( 0.5f, 0f, -0.5f),
            };

            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f),
            };

            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();

            return mesh;
        }

        /// <summary>
        /// An unlit transparent material. Unlit so the panel reads as light rather than as a painted
        /// surface, and depth-write off so it never occludes the piece hovering above it.
        /// </summary>
        Material BuildMaterial(Color colour)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var mat = new Material(shader) { name = "PlacementHighlight" };

            // URP's Unlit shader is opaque until told otherwise, and each of these has to be set:
            // the surface mode drives the keyword, the keyword drives the blend path.
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_Cull", (float)CullMode.Off);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.renderQueue = (int)RenderQueue.Transparent;

            mat.SetTexture(MainTexId, Glow());
            mat.SetColor(BaseColorId, colour);

            return mat;
        }

        Texture2D Glow() => glow != null ? glow : (glow = BuildGlowTexture(64));

        /// <summary>
        /// A soft-edged panel: bright at the border, fading to a translucent interior. Generated
        /// rather than imported so the look is tunable from the inspector and there is no asset to
        /// lose.
        /// </summary>
        Texture2D BuildGlowTexture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "PlacementHighlightGlow",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };

            var pixels = new Color32[size * size];

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var u = (x + 0.5f) / size;
                    var v = (y + 0.5f) / size;

                    // 0 at the border, 1 at the centre.
                    var toEdge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v)) * 2f;
                    var rim = 1f - Mathf.Clamp01(toEdge / Mathf.Max(rimWidth, 0.001f));
                    var alpha = Mathf.Clamp01(fillStrength + rim * (1f - fillStrength));

                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            return texture;
        }
    }
}
