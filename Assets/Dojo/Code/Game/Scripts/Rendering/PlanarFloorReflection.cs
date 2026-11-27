using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Dojo.Game.Rendering
{
    /// <summary>
    /// Mirrors the room in the ground floor's tiles: a planar reflection, which reflection probes
    /// cannot give - a probe is one picture of the room from one point, so a floor never shows the
    /// desk standing on it.
    /// </summary>
    /// <remarks>
    /// Each frame, once the camera has moved, a hidden camera renders the main camera's view
    /// mirrored under the floor into a small texture. A pass added to the main camera alone then
    /// draws the floor tiles a second time, adding that picture on top of what their own material
    /// drew. Nothing about the tiles is changed but a rendering-layer bit on their renderers while
    /// this runs, which is how the pass finds them; the materials, textures and renderer assets are
    /// left as they are.
    /// <para>
    /// The tiles are found by material, among whatever the world has placed, every second, since
    /// the world loads after the scene and the player adds floor as they build. Only the lowest
    /// level of tiles is mirrored: a floor above it is not in the same plane.
    /// </para>
    /// </remarks>
    [DefaultExecutionOrder(10000)] // after the Cinemachine brain has placed the camera this frame
    public sealed class PlanarFloorReflection : MonoBehaviour
    {
        [Tooltip("The camera whose view is mirrored and which shows the reflection.")]
        [SerializeField] Camera targetCamera;

        [Tooltip("Hidden/Dojo/PlanarReflectionOverlay.")]
        [SerializeField] Shader overlayShader;

        [Tooltip("The floor materials whose tiles reflect.")]
        [SerializeField] List<Material> floorMaterials = new List<Material>();

        [Tooltip("What appears in the reflection.")]
        [SerializeField] LayerMask reflectedLayers = ~((1 << 2) | (1 << 5) | (1 << 8));

        [Tooltip("The rendering layer (0-31) the tiles are tagged with while this runs. Keep it one no light or decal uses.")]
        [SerializeField, Range(8, 31)] int renderingLayer = 12;

        [Tooltip("The reflection's size against the screen's.")]
        [SerializeField, Range(0.25f, 1f)] float resolutionScale = 0.5f;

        [Tooltip("How much of the mirrored picture is added to the tiles.")]
        [SerializeField, Range(0f, 1f)] float intensity = 0.25f;

        [Tooltip("How much stronger it is where the floor is seen at a low angle.")]
        [SerializeField, Range(0f, 4f)] float fresnel = 1f;

        [Tooltip("How soft the reflection is (mip level): the tiles are satin, not mirrors.")]
        [SerializeField, Range(0f, 6f)] float blur = 1.5f;

        [Tooltip("Shadows in the reflection. Off saves a shadow pass every frame.")]
        [SerializeField] bool reflectShadows;

        [Tooltip("Height above the tiles' top where the mirror clips, so their own surface is not in it.")]
        [SerializeField] float clipOffset = 0.02f;

        static readonly int ReflectionTex = Shader.PropertyToID("_PlanarReflectionTex");
        static readonly int IntensityId = Shader.PropertyToID("_PlanarIntensity");
        static readonly int FresnelId = Shader.PropertyToID("_PlanarFresnel");
        static readonly int BlurId = Shader.PropertyToID("_PlanarBlur");

        readonly List<Renderer> tagged = new List<Renderer>();
        readonly HashSet<Material> floors = new HashSet<Material>();
        Camera mirror;
        RenderTexture target;
        Material overlay;
        OverlayPass pass;
        float planeHeight;
        bool hasPlane;
        bool rendered;
        float nextScan;

        uint Bit => 1u << renderingLayer;

        void OnEnable()
        {
            if (overlayShader == null || !overlayShader.isSupported)
            {
                Debug.LogWarning("[PlanarFloorReflection] No usable overlay shader, so the floors don't reflect.", this);
                enabled = false;
                return;
            }

            overlay = new Material(overlayShader) { hideFlags = HideFlags.HideAndDontSave };
            pass = new OverlayPass();
            nextScan = 0f;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;

            foreach (var renderer in tagged)
            {
                if (renderer != null)
                {
                    renderer.renderingLayerMask &= ~Bit;
                }
            }

            tagged.Clear();
            hasPlane = false;
            rendered = false;

            if (mirror != null) Destroy(mirror.gameObject);
            if (target != null) { target.Release(); Destroy(target); }
            if (overlay != null) Destroy(overlay);
            mirror = null;
            target = null;
            overlay = null;
        }

        void LateUpdate()
        {
            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + 1f;
                Scan();
            }

            rendered = hasPlane && targetCamera != null && targetCamera.isActiveAndEnabled && RenderMirror();
        }

        /// <summary>Tags the lowest level of floor tiles, and finds the height of their top.</summary>
        void Scan()
        {
            floors.Clear();
            foreach (var material in floorMaterials)
            {
                if (material != null) floors.Add(material);
            }

            var candidates = new List<Renderer>();
            var lowest = float.MaxValue;

            foreach (var renderer in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || !UsesFloor(renderer))
                {
                    continue;
                }

                candidates.Add(renderer);
                lowest = Mathf.Min(lowest, renderer.bounds.max.y);
            }

            foreach (var renderer in tagged)
            {
                if (renderer != null) renderer.renderingLayerMask &= ~Bit;
            }

            tagged.Clear();
            hasPlane = candidates.Count > 0;
            if (!hasPlane)
            {
                return;
            }

            planeHeight = lowest;

            foreach (var renderer in candidates)
            {
                if (renderer.bounds.max.y <= lowest + 0.05f)
                {
                    renderer.renderingLayerMask |= Bit;
                    tagged.Add(renderer);
                }
            }
        }

        bool UsesFloor(Renderer renderer)
        {
            foreach (var material in renderer.sharedMaterials)
            {
                if (material != null && floors.Contains(material)) return true;
            }

            return false;
        }

        bool RenderMirror()
        {
            var width = Mathf.Max(64, Mathf.RoundToInt(targetCamera.pixelWidth * resolutionScale));
            var height = Mathf.Max(64, Mathf.RoundToInt(targetCamera.pixelHeight * resolutionScale));

            if (target == null || target.width != width || target.height != height)
            {
                if (target != null) { target.Release(); Destroy(target); }
                target = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR)
                {
                    name = "PlanarFloorReflection",
                    useMipMap = true,
                    autoGenerateMips = true,
                    filterMode = FilterMode.Trilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                target.Create();
            }

            if (mirror == null)
            {
                var go = new GameObject("PlanarFloorReflection Camera") { hideFlags = HideFlags.HideAndDontSave };
                mirror = go.AddComponent<Camera>();
                mirror.enabled = false;
                var data = go.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = false;
                data.antialiasing = AntialiasingMode.None;
                data.requiresDepthOption = CameraOverrideOption.Off;
                data.requiresColorOption = CameraOverrideOption.Off;
            }

            mirror.GetUniversalAdditionalCameraData().renderShadows = reflectShadows;
            mirror.orthographic = targetCamera.orthographic;
            mirror.orthographicSize = targetCamera.orthographicSize;
            mirror.fieldOfView = targetCamera.fieldOfView;
            mirror.aspect = targetCamera.aspect;
            mirror.nearClipPlane = targetCamera.nearClipPlane;
            mirror.farClipPlane = targetCamera.farClipPlane;
            mirror.clearFlags = CameraClearFlags.SolidColor;
            mirror.backgroundColor = Color.clear; // nothing reflected adds nothing
            mirror.cullingMask = targetCamera.cullingMask & reflectedLayers;
            mirror.allowHDR = true;
            mirror.allowMSAA = false;

            // Mirror about the plane of the tiles' tops.
            var normal = Vector3.up;
            var point = new Vector3(0f, planeHeight, 0f);
            var reflection = Reflection(new Vector4(normal.x, normal.y, normal.z, -Vector3.Dot(normal, point)));

            mirror.transform.SetPositionAndRotation(
                reflection.MultiplyPoint(targetCamera.transform.position),
                Quaternion.LookRotation(Vector3.Reflect(targetCamera.transform.forward, normal), Vector3.Reflect(targetCamera.transform.up, normal)));
            mirror.worldToCameraMatrix = targetCamera.worldToCameraMatrix * reflection;

            // Clip everything under the floor, so what is beneath it is not mirrored up through it.
            mirror.projectionMatrix = targetCamera.projectionMatrix;
            mirror.projectionMatrix = mirror.CalculateObliqueMatrix(CameraSpacePlane(mirror.worldToCameraMatrix, point + normal * clipOffset, normal));
            mirror.cullingMatrix = targetCamera.projectionMatrix * mirror.worldToCameraMatrix;

            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            if (!RenderPipeline.SupportsRenderRequest(mirror, request))
            {
                return false;
            }

            // No GL.invertCulling: URP already flips the winding for a mirrored view matrix, and
            // inverting it again draws only back faces (checked 27 Nov).
            RenderPipeline.SubmitRenderRequest(mirror, request);

            Shader.SetGlobalTexture(ReflectionTex, target);
            return true;
        }

        void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!rendered || camera != targetCamera || overlay == null)
            {
                return;
            }

            overlay.SetFloat(IntensityId, intensity);
            overlay.SetFloat(FresnelId, fresnel);
            overlay.SetFloat(BlurId, blur);
            pass.Setup(overlay, Bit);
            camera.GetUniversalAdditionalCameraData().scriptableRenderer.EnqueuePass(pass);
        }

        static Matrix4x4 Reflection(Vector4 plane)
        {
            var m = Matrix4x4.identity;
            m.m00 = 1f - 2f * plane.x * plane.x;
            m.m01 = -2f * plane.x * plane.y;
            m.m02 = -2f * plane.x * plane.z;
            m.m03 = -2f * plane.w * plane.x;
            m.m10 = -2f * plane.y * plane.x;
            m.m11 = 1f - 2f * plane.y * plane.y;
            m.m12 = -2f * plane.y * plane.z;
            m.m13 = -2f * plane.w * plane.y;
            m.m20 = -2f * plane.z * plane.x;
            m.m21 = -2f * plane.z * plane.y;
            m.m22 = 1f - 2f * plane.z * plane.z;
            m.m23 = -2f * plane.w * plane.z;
            return m;
        }

        static Vector4 CameraSpacePlane(Matrix4x4 worldToCamera, Vector3 point, Vector3 normal)
        {
            var p = worldToCamera.MultiplyPoint(point);
            var n = worldToCamera.MultiplyVector(normal).normalized;
            return new Vector4(n.x, n.y, n.z, -Vector3.Dot(p, n));
        }

        /// <summary>Draws the tagged tiles again with the overlay, adding the reflection.</summary>
        sealed class OverlayPass : ScriptableRenderPass
        {
            static readonly ShaderTagId Forward = new ShaderTagId("UniversalForward");

            Material material;
            uint layerMask;

            sealed class PassData
            {
                public RendererListHandle List;
            }

            public OverlayPass()
            {
                // Over the opaque floor, under the glass and anything else see-through.
                renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
            }

            public void Setup(Material overlay, uint renderingLayers)
            {
                material = overlay;
                layerMask = renderingLayers;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                var rendering = frameData.Get<UniversalRenderingData>();
                var camera = frameData.Get<UniversalCameraData>();
                var lights = frameData.Get<UniversalLightData>();

                var drawing = RenderingUtils.CreateDrawingSettings(Forward, rendering, camera, lights, camera.defaultOpaqueSortFlags);
                drawing.overrideMaterial = material;
                drawing.overrideMaterialPassIndex = 0;

                var filtering = new FilteringSettings(RenderQueueRange.opaque, -1, layerMask);
                var list = renderGraph.CreateRendererList(new RendererListParams(rendering.cullResults, drawing, filtering));

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Planar Floor Reflection", out var data))
                {
                    data.List = list;
                    builder.UseRendererList(list);
                    builder.SetRenderAttachment(resources.activeColorTexture, 0);
                    builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                    builder.SetRenderFunc((PassData d, RasterGraphContext context) => context.cmd.DrawRendererList(d.List));
                }
            }
        }
    }
}
