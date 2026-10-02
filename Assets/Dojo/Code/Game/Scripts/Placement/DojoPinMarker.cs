// DojoPinMarker.cs — drop on the root of the imported dojo-pin.fbx prefab.
// Plays the "Orb_Spin" clip from the FBX and makes the blue orb glow (emission + point light).
//
// Call from anywhere:
//   var pin = GetComponent<DojoPinMarker>();
//   pin.Play();  pin.Stop();  pin.Pause();  pin.Resume();
//   pin.SetSpinSpeed(2f);     // 2 = twice as fast, negative = reverse
//   pin.SetGlow(true/false);  pin.SetGlowIntensity(4f);
//
// Works with the Built-in Standard shader and URP Lit (both use _EmissionColor / _EMISSION).

using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Dojo.Game.Placement
{
    [DisallowMultipleComponent]
    public class DojoPinMarker : MonoBehaviour
    {
        [Header("Spin")]
        [Tooltip("The 'Orb_Spin' clip from dojo-pin.fbx (expand the FBX in the Project window). Loop Time must be ticked.")]
        public AnimationClip spinClip;
        public bool playOnEnable = true;
        [Tooltip("1 = one turn every 4 s (as authored).")]
        public float spinSpeed = 1f;

        [Header("Glow")]
        [Tooltip("Child renderer holding the orb. Auto-found by name 'LocationPin_Orb' if empty.")]
        public Renderer orbRenderer;
        [Tooltip("Material on the orb that should glow. Matched by name.")]
        public string glowMaterialName = "Pin_Orb_Blue";
        public bool glowOnEnable = true;
        [ColorUsage(false, true)] public Color glowColor = new Color(0f, 0.5f, 0.95f);
        [Min(0f)] public float glowIntensity = 3f;

        [Header("Glow light")]
        public bool usePointLight = true;
        public Color lightColor = new Color(0.2f, 0.65f, 1f);
        [Min(0f)] public float lightIntensity = 2f;
        [Min(0f)] public float lightRange = 1.5f;

        public bool IsPlaying => _graph.IsValid() && _graph.IsPlaying() && !_paused;

        PlayableGraph _graph;
        AnimationClipPlayable _clipPlayable;
        Material _glowMat;
        Light _light;
        bool _paused;

        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        void Awake()
        {
            if (orbRenderer == null)
            {
                foreach (var r in GetComponentsInChildren<Renderer>(true))
                    if (r.name.StartsWith("LocationPin_Orb")) { orbRenderer = r; break; }
            }
            if (orbRenderer != null)
            {
                var mats = orbRenderer.materials; // instances, so other pins aren't affected
                foreach (var m in mats)
                    if (m.name.StartsWith(glowMaterialName)) { _glowMat = m; break; }
            }
        }

        void OnEnable()
        {
            if (glowOnEnable) SetGlow(true);
            if (playOnEnable) Play();
        }

        void OnDisable() => DestroyGraph();
        void OnDestroy() => DestroyGraph();

        // ---------- Spin ----------

        [ContextMenu("Play")]
        public void Play()
        {
            if (spinClip == null) { Debug.LogWarning($"{name}: DojoPinMarker has no spinClip assigned.", this); return; }
            var animator = GetComponent<Animator>();
            if (animator == null) animator = gameObject.AddComponent<Animator>();

            DestroyGraph();
            _clipPlayable = AnimationPlayableUtilities.PlayClip(animator, spinClip, out _graph);
            _clipPlayable.SetSpeed(spinSpeed);
            _paused = false;
        }

        [ContextMenu("Stop")]
        public void Stop()
        {
            if (_clipPlayable.IsValid()) { _clipPlayable.SetTime(0); _graph.Evaluate(); }
            DestroyGraph();
            _paused = false;
        }

        public void Pause()
        {
            if (!_clipPlayable.IsValid()) return;
            _clipPlayable.SetSpeed(0); _paused = true;
        }

        public void Resume()
        {
            if (!_clipPlayable.IsValid()) { Play(); return; }
            _clipPlayable.SetSpeed(spinSpeed); _paused = false;
        }

        public void SetSpinSpeed(float speed)
        {
            spinSpeed = speed;
            if (_clipPlayable.IsValid() && !_paused) _clipPlayable.SetSpeed(speed);
        }

        void DestroyGraph()
        {
            if (_graph.IsValid()) _graph.Destroy();
        }

        // ---------- Glow ----------

        public void SetGlow(bool on)
        {
            if (_glowMat != null)
            {
                if (on)
                {
                    _glowMat.EnableKeyword("_EMISSION");
                    _glowMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    _glowMat.SetColor(EmissionColorId, glowColor * glowIntensity);
                }
                else
                {
                    _glowMat.SetColor(EmissionColorId, Color.black);
                    _glowMat.DisableKeyword("_EMISSION");
                }
            }
            else if (on)
            {
                Debug.LogWarning($"{name}: no material named '{glowMaterialName}' found on the orb.", this);
            }

            if (usePointLight)
            {
                if (on && _light == null) CreateLight();
                if (_light != null) _light.enabled = on;
            }
        }

        public void SetGlowIntensity(float intensity)
        {
            glowIntensity = intensity;
            if (_glowMat != null && _glowMat.IsKeywordEnabled("_EMISSION"))
                _glowMat.SetColor(EmissionColorId, glowColor * glowIntensity);
            if (_light != null) _light.intensity = lightIntensity * (intensity / 3f);
        }

        void CreateLight()
        {
            var go = new GameObject("OrbGlowLight");
            // Parent to the pin root (not the orb) so the light doesn't spin with it.
            go.transform.SetParent(transform, false);
            go.transform.position = orbRenderer != null ? orbRenderer.bounds.center : transform.position + Vector3.up * 0.677f;
            _light = go.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = lightColor;
            _light.intensity = lightIntensity;
            _light.range = lightRange;
            _light.shadows = LightShadows.None;
        }
    }
}
