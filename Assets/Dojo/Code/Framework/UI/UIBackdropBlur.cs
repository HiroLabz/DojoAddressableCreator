using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Framework.UI
{
    /// <summary>
    /// Takes a picture of the screen just before a dialog appears, and hands it to the blur shader
    /// as the thing to blur.
    /// </summary>
    /// <remarks>
    /// <b>Why a capture at all.</b> A shader cannot read pixels already drawn into the framebuffer
    /// it is drawing into — URP has no <c>GrabPass</c> — so blurring what is behind a UI element is
    /// never a material on its own. Something has to put "behind" into a texture first.
    /// <para>
    /// <b>Why the composited frame rather than the camera.</b> Rendering the camera a second time
    /// captures the 3D scene and camera-space canvases, but <em>not</em> Screen Space Overlay ones —
    /// and the menus this sits over are Overlay. <c>CaptureScreenshotIntoRenderTexture</c> reads the
    /// back buffer, which is everything, whatever canvas drew it.
    /// </para>
    /// <para>
    /// <b>Why it lives on the dialog root.</b> The scrim it feeds is inside the dialog's own root,
    /// which is switched off until the dialog opens — and a component on an inactive object cannot
    /// run the coroutine this needs. The capture must also happen while that root is still off, or
    /// the picture contains the dialog it is about to sit behind.
    /// </para>
    /// </remarks>
    public sealed class UIBackdropBlur : MonoBehaviour
    {
        [Tooltip("The graphic whose material receives the captured backdrop — the dialog's scrim.")]
        [SerializeField] Graphic target;

        [Tooltip("Fraction of screen resolution the backdrop is kept at. Lower is cheaper and " +
                 "blurrier; 4 means quarter width and quarter height.")]
        [SerializeField, Range(1, 8)] int downsample = 4;

        [Tooltip("Hidden/Dojo/GaussianBlurPass. Serialized rather than found by name so it is " +
                 "certain to survive into a build.")]
        [SerializeField] Shader blurShader;

        [Tooltip("How many horizontal+vertical passes to run. Each one widens the kernel; four " +
                 "is already well past what a single-pass blur can reach.")]
        [SerializeField, Range(1, 8)] int iterations = 4;

        [Tooltip("How far the first pass reaches, in texels. Later passes step further out.")]
        [SerializeField, Range(0.5f, 4f)] float spread = 1.6f;

        RenderTexture backdrop;
        Material instanced;
        Material blitMaterial;

        /// <summary>
        /// Captures the screen, then runs <paramref name="onReady"/> — which is where the caller
        /// should switch the dialog on.
        /// </summary>
        /// <remarks>
        /// The callback rather than a plain call because the capture cannot happen until the end of
        /// the current frame: that is the only moment the back buffer holds a finished image. Outside
        /// play mode, or with nothing to draw into, it runs straight away so the dialog still opens.
        /// </remarks>
        public void CaptureThen(Action onReady)
        {
            if (!Application.isPlaying || target == null || !isActiveAndEnabled)
            {
                if (onReady != null)
                {
                    onReady();
                }

                return;
            }

            StartCoroutine(CaptureRoutine(onReady));
        }

        IEnumerator CaptureRoutine(Action onReady)
        {
            // The frame has to finish drawing before there is anything to read.
            yield return new WaitForEndOfFrame();

            try
            {
                Grab();
            }
            catch (Exception exception)
            {
                // A failed capture costs the blur, not the dialog.
                Debug.LogWarning($"{nameof(UIBackdropBlur)} could not capture the screen: {exception.Message}", this);
            }

            if (onReady != null)
            {
                onReady();
            }
        }

        void Grab()
        {
            Release();

            int width = Mathf.Max(1, Screen.width / downsample);
            int height = Mathf.Max(1, Screen.height / downsample);

            // Read at full size, then shrink. CaptureScreenshotIntoRenderTexture writes the back
            // buffer at its own resolution, so handing it a small target would crop rather than
            // scale.
            RenderTexture full = RenderTexture.GetTemporary(Screen.width, Screen.height, 0);
            ScreenCapture.CaptureScreenshotIntoRenderTexture(full);

            backdrop = new RenderTexture(width, height, 0)
            {
                name = "UIBackdrop",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            // The back buffer arrives upside down wherever the graphics API puts UV origin at the
            // top, which is most of them. Corrected on the way down rather than in the shader, so
            // the shader stays a plain sampler.
            if (SystemInfo.graphicsUVStartsAtTop)
            {
                Graphics.Blit(full, backdrop, new Vector2(1f, -1f), new Vector2(0f, 1f));
            }
            else
            {
                Graphics.Blit(full, backdrop);
            }

            RenderTexture.ReleaseTemporary(full);

            Blur(backdrop, width, height);

            // A material instance, so one dialog's backdrop cannot overwrite another's through the
            // shared asset.
            if (instanced == null)
            {
                instanced = new Material(target.material);
                instanced.name = target.material.name + " (captured)";
                target.material = instanced;
            }

            instanced.SetTexture("_BackdropTex", backdrop);
        }

        /// <summary>
        /// Runs a separable Gaussian over the backdrop, in place.
        /// </summary>
        /// <remarks>
        /// Horizontal then vertical, ping-ponged through one scratch texture, repeated. Separating
        /// the axes turns an r² kernel into 2r, and repeating it compounds the reach — four
        /// iterations of a nine-tap reach far wider than any single pass could, with the falloff
        /// staying smooth instead of banding.
        /// <para>
        /// Affordable because this happens once, when the dialog opens, on a quarter-size texture.
        /// The earlier version blurred every frame at UI draw time, which is why it had to be a
        /// single cheap pass and why it looked like one.
        /// </para>
        /// </remarks>
        void Blur(RenderTexture texture, int width, int height)
        {
            if (blurShader == null)
            {
                return;
            }

            if (blitMaterial == null)
            {
                blitMaterial = new Material(blurShader) { name = "UIBackdropBlur (blit)" };
            }

            RenderTexture scratch = RenderTexture.GetTemporary(width, height, 0, texture.format);
            scratch.filterMode = FilterMode.Bilinear;
            scratch.wrapMode = TextureWrapMode.Clamp;

            for (int i = 0; i < iterations; i++)
            {
                // Each pass steps further out than the last, so the kernel widens instead of
                // simply deepening where it already reached.
                float reach = spread * (i + 1);

                blitMaterial.SetVector("_BlurAxis", new Vector4(reach, 0f, 0f, 0f));
                Graphics.Blit(texture, scratch, blitMaterial, 0);

                blitMaterial.SetVector("_BlurAxis", new Vector4(0f, reach, 0f, 0f));
                Graphics.Blit(scratch, texture, blitMaterial, 0);
            }

            RenderTexture.ReleaseTemporary(scratch);
        }

        void OnDisable()
        {
            Release();
        }

        void OnDestroy()
        {
            Release();

            if (instanced != null)
            {
                Destroy(instanced);
                instanced = null;
            }

            if (blitMaterial != null)
            {
                Destroy(blitMaterial);
                blitMaterial = null;
            }
        }

        void Release()
        {
            if (backdrop == null)
            {
                return;
            }

            if (instanced != null)
            {
                instanced.SetTexture("_BackdropTex", Texture2D.blackTexture);
            }

            backdrop.Release();
            Destroy(backdrop);
            backdrop = null;
        }
    }
}
