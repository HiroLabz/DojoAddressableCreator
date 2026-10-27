using System;
using System.Collections.Generic;
using Dojo.Game.Placement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Dojo.Game.UI
{
    /// <summary>
    /// A picture of an agent: its model, rendered once to a small sprite and kept.
    /// </summary>
    /// <remarks>
    /// The game had no agent picture at all - the roster, the chat and the agent card show tinted
    /// initials - so this renders the model the agent wears in the world. Every agent wears the
    /// same one today (<see cref="WorldSettings.AgentPrefab"/>), so every agent gets the same
    /// picture; it is asked for by agent all the same, so a per-agent look later changes one
    /// lookup here and nothing at the callers.
    /// <para>
    /// The model is copied with its scripts, physics and navigation stripped before it is ever
    /// switched on, so nothing on it wakes up, registers itself or walks off. It is rendered on a
    /// layer nothing else uses, by a camera that sees only that layer, and then destroyed.
    /// </para>
    /// </remarks>
    public sealed class AgentPortraits : IDisposable
    {
        /// <summary>Pixels along each side of a picture.</summary>
        const int Size = 256;

        /// <summary>An unnamed layer: the picture's camera sees this and nothing else.</summary>
        const int PortraitLayer = 31;

        /// <summary>Somewhere far from any world, so no world's lights or shadows reach it.</summary>
        static readonly Vector3 Studio = new Vector3(0f, -5000f, 0f);

        readonly WorldSettings settings;
        readonly Dictionary<GameObject, Sprite> made = new Dictionary<GameObject, Sprite>();

        public AgentPortraits(WorldSettings settings)
        {
            this.settings = settings;
        }

        /// <summary>The picture of agent <paramref name="credentialId"/>, or null when there is no model to draw.</summary>
        public Sprite For(int credentialId)
        {
            var model = settings != null ? settings.AgentPrefab : null;

            if (model == null)
            {
                return null;
            }

            if (made.TryGetValue(model, out var sprite) && sprite != null)
            {
                return sprite;
            }

            sprite = Render(model);
            made[model] = sprite;
            return sprite;
        }

        public void Dispose()
        {
            foreach (var sprite in made.Values)
            {
                if (sprite != null)
                {
                    Object.Destroy(sprite.texture);
                    Object.Destroy(sprite);
                }
            }

            made.Clear();
        }

        /// <summary>Renders <paramref name="model"/> facing the camera, full length, on a clear background.</summary>
        static Sprite Render(GameObject model)
        {
            // Built under a switched-off holder, so no Awake runs before the scripts are gone.
            var holder = new GameObject("AgentPortraitStudio");
            holder.SetActive(false);
            holder.transform.position = Studio;

            var copy = Object.Instantiate(model, holder.transform, false);
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            Strip(copy);

            foreach (var t in copy.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = PortraitLayer;
            }

            var target = RenderTexture.GetTemporary(Size, Size, 24, RenderTextureFormat.ARGB32);
            var cameraObject = new GameObject("AgentPortraitCamera");
            var lightObject = new GameObject("AgentPortraitLight");

            try
            {
                holder.SetActive(true);

                var bounds = BoundsOf(copy);
                var camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.cullingMask = 1 << PortraitLayer;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                camera.fieldOfView = 24f;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = 100f;
                camera.targetTexture = target;

                // In front of the model, a little above its middle, looking slightly down at it.
                var height = Mathf.Max(bounds.size.y, 0.01f);
                var distance = height * 0.5f / Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.15f;
                var look = (Vector3.forward + Vector3.up * 0.2f).normalized;
                camera.transform.position = bounds.center + look * distance;
                camera.transform.LookAt(bounds.center);

                // Its own light, for this layer only, so the picture does not depend on the world's.
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.2f;
                light.cullingMask = 1 << PortraitLayer;
                light.transform.rotation = Quaternion.LookRotation(-look + Vector3.right * 0.4f + Vector3.down * 0.6f);

                var request = new RenderPipeline.StandardRequest { destination = target };
                if (RenderPipeline.SupportsRenderRequest(camera, request))
                {
                    RenderPipeline.SubmitRenderRequest(camera, request);
                }
                else
                {
                    camera.Render();
                }

                var previous = RenderTexture.active;
                RenderTexture.active = target;
                var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = model.name + " portrait" };
                texture.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                texture.Apply();
                RenderTexture.active = previous;

                var sprite = Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
                sprite.name = texture.name;
                return sprite;
            }
            finally
            {
                RenderTexture.ReleaseTemporary(target);
                Object.Destroy(cameraObject);
                Object.Destroy(lightObject);
                Object.Destroy(holder);
            }
        }

        /// <summary>Everything off the copy but its transforms and what draws it.</summary>
        static void Strip(GameObject copy)
        {
            // Scripts first: some require the physics or navigation below and would block them.
            foreach (var script in copy.GetComponentsInChildren<MonoBehaviour>(true))
            {
                Object.DestroyImmediate(script);
            }

            foreach (var part in copy.GetComponentsInChildren<Component>(true))
            {
                if (part is Transform || part is Renderer || part is MeshFilter)
                {
                    continue;
                }

                Object.DestroyImmediate(part);
            }
        }

        static Bounds BoundsOf(GameObject copy)
        {
            var renderers = copy.GetComponentsInChildren<Renderer>(true);

            if (renderers.Length == 0)
            {
                return new Bounds(copy.transform.position, Vector3.one);
            }

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }
    }
}
