using Dojo.Game.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Dojo.Game.Editor
{
    /// <summary>
    /// Builds the floor stepper - ▲ 2F ▼, with + ADD FLOOR (while editing) or GO TO 2F (in Play)
    /// beside it - into the Game scene's in-game canvas, just to the right of the TAB button.
    /// </summary>
    /// <remarks>
    /// Re-runnable: each run removes the stepper it built last time and builds it again. Every part
    /// is from the UI kit - the stat-card panel and the ghost buttons - and the arrows are drawn in
    /// JetBrains Mono, which has them.
    /// <para>
    /// Opens the Game scene for the build if it is not open already, and closes it again after.
    /// </para>
    /// </remarks>
    public static class FloorSwitcherBuilder
    {
        const string RootName = "FloorSwitcher";
        const string GameScene = "Assets/Dojo/Scenes/Game.unity";
        const string Kit = "Assets/Dojo/Art/2D/UI/";
        const string Fonts = "Assets/Dojo/Fonts/TMP/";
        const string ShadeName = "ShadeBelow";
        const string ShadeMaterialPath = "Assets/Dojo/Art/Effects/FloorShade.mat";

        // Metres. Wide enough that its edge is never on screen over any world the grid allows.
        const float ShadeSize = 400f;

        // In the canvas's 1920x1080 units. The TAB button is 80 square at (61, 62).
        const float Left = 157f;
        const float Bottom = 62f;
        const float StepperWidth = 184f;
        const float StepperHeight = 80f;
        const float ArrowSize = 48f;
        const float AddWidth = 196f;
        const float AddHeight = 48f;
        const float Gap = 12f;

        static readonly Color Ink = Color.white;
        static readonly Color InkSoft = Hex("#9fb2cc");

        [MenuItem("Tools/Dojo/Build Floor Switcher")]
        public static void BuildFromMenu()
        {
            Debug.Log("[Floors] " + BuildInGameScene());
        }

        /// <summary>Builds the stepper into the Game scene and saves it. Returns a line saying what was done.</summary>
        public static string BuildInGameScene()
        {
            var scene = SceneManager.GetSceneByPath(GameScene);
            var opened = !scene.isLoaded;

            if (opened)
            {
                scene = EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Additive);
            }

            try
            {
                var result = Build(scene);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                return result;
            }
            finally
            {
                if (opened)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        static string Build(Scene scene)
        {
            InGameCanvasUI canvasUI = null;

            foreach (var root in scene.GetRootGameObjects())
            {
                canvasUI = root.GetComponentInChildren<InGameCanvasUI>(true);
                if (canvasUI != null)
                {
                    break;
                }
            }

            if (canvasUI == null)
            {
                return "no InGameCanvasUI in " + scene.path + ", so nothing was built.";
            }

            var parent = (RectTransform)canvasUI.transform;

            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                if (parent.GetChild(i).name == RootName)
                {
                    Object.DestroyImmediate(parent.GetChild(i).gameObject);
                }
            }

            var holder = BottomLeft(RootName, parent, Left, Bottom, StepperWidth + Gap + AddWidth, StepperHeight);
            var switcher = holder.gameObject.AddComponent<FloorSwitcher>();

            // ▲ 2F ▼ on one panel.
            var stepper = BottomLeft("Stepper", holder, 0f, 0f, StepperWidth, StepperHeight);
            var panel = stepper.gameObject.AddComponent<Image>();
            panel.sprite = KitSprite("panel/dojo_panel_statcard_9slice_idle_base");
            panel.type = Image.Type.Sliced;
            panel.raycastTarget = true;

            var up = Arrow("Up", stepper, "▲", new Vector2(0f, 0.5f), new Vector2(10f, 0f));
            var down = Arrow("Down", stepper, "▼", new Vector2(1f, 0.5f), new Vector2(-10f, 0f));

            var labelRect = Rect("Floor", stepper);
            labelRect.anchorMin = labelRect.anchorMax = labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.sizeDelta = new Vector2(StepperWidth - 2f * (ArrowSize + 10f), StepperHeight);
            var label = Text(labelRect, "1F", Font("Poppins-SemiBold SDF"), 28f, Ink);

            // + ADD FLOOR, beside it, centred on the stepper's height.
            var add = GhostButton("AddFloor", holder, StepperWidth + Gap, (StepperHeight - AddHeight) * 0.5f, AddWidth, AddHeight);
            var addLabelRect = Stretched("Label", (RectTransform)add.transform);
            var addLabel = Text(addLabelRect, "+ ADD FLOOR", Font("Poppins-SemiBold SDF"), 18f, Ink);
            addLabel.characterSpacing = 8f;

            // GO TO 3F, in the same place: Add floor only shows while editing and this only in Play,
            // so the two never need room at once.
            var goTo = GhostButton("GoTo", holder, StepperWidth + Gap, (StepperHeight - AddHeight) * 0.5f, AddWidth, AddHeight);
            var goToLabelRect = Stretched("Label", (RectTransform)goTo.transform);
            var goToLabel = Text(goToLabelRect, "GO TO 2F", Font("Poppins-SemiBold SDF"), 18f, Ink);
            goToLabel.characterSpacing = 8f;
            goTo.gameObject.SetActive(false);

            var shade = BuildShade(scene);

            Wire(switcher,
                ("stepper", stepper.gameObject), ("shade", shade),
                ("up", up), ("down", down), ("label", label),
                ("add", add), ("addLabel", addLabel),
                ("goTo", goTo), ("goToLabel", goToLabel));

            return "built the floor switcher beside the TAB button in " + scene.path + ".";
        }

        /// <summary>
        /// The dark see-through sheet the switcher lays under the floor being viewed, at the scene's
        /// root, replacing the one from the last run.
        /// </summary>
        /// <remarks>
        /// Every choice here keeps it out of the game's way. No collider, and on Ignore Raycast,
        /// so no click, drag or ground probe meets it. At the scene root, outside the world, so the
        /// NavMesh (which collects the world's children) never walks on it and the floors never
        /// adopt it. A name without "floor" in it, so placement never takes it for ground. No
        /// shadows either way.
        /// </remarks>
        static Renderer BuildShade(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == ShadeName)
                {
                    Object.DestroyImmediate(root);
                }
            }

            var sheet = GameObject.CreatePrimitive(PrimitiveType.Quad);
            sheet.name = ShadeName;
            Object.DestroyImmediate(sheet.GetComponent<Collider>());
            SceneManager.MoveGameObjectToScene(sheet, scene);

            sheet.layer = LayerMask.NameToLayer("Ignore Raycast");
            sheet.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(90f, 0f, 0f));
            sheet.transform.localScale = new Vector3(ShadeSize, ShadeSize, 1f);

            var renderer = sheet.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = ShadeMaterial();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            // Clear until a floor above the ground is viewed; the switcher turns it on.
            renderer.enabled = false;

            return renderer;
        }

        /// <summary>URP Unlit, black, alpha-blended, not writing depth. Made once, then reused.</summary>
        static Material ShadeMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(ShadeMaterialPath);

            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(material, ShadeMaterialPath);
            }

            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.SetColor("_BaseColor", new Color(0f, 0f, 0f, 0.55f));

            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return material;
        }

        static Button Arrow(string name, RectTransform parent, string glyph, Vector2 anchor, Vector2 offset)
        {
            var host = Rect(name, parent);
            host.anchorMin = host.anchorMax = host.pivot = anchor;
            host.anchoredPosition = offset;
            host.sizeDelta = new Vector2(ArrowSize, ArrowSize);

            var button = Ghost(host);
            var glyphRect = Stretched("Glyph", host);
            Text(glyphRect, glyph, Font("JetBrainsMono-Regular SDF"), 20f, InkSoft);

            return button;
        }

        static Button GhostButton(string name, RectTransform parent, float x, float y, float width, float height)
        {
            var host = BottomLeft(name, parent, x, y, width, height);
            return Ghost(host);
        }

        /// <summary>The kit's ghost button on <paramref name="host"/>, with its hover, press and disabled faces.</summary>
        static Button Ghost(RectTransform host)
        {
            var face = host.gameObject.AddComponent<Image>();
            face.sprite = KitSprite("btn/dojo_btn_ghost_9slice_idle_base");
            face.type = Image.Type.Sliced;
            face.raycastTarget = true;

            var button = host.gameObject.AddComponent<Button>();
            button.targetGraphic = face;
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState
            {
                highlightedSprite = KitSprite("btn/dojo_btn_ghost_9slice_hover_base"),
                pressedSprite = KitSprite("btn/dojo_btn_ghost_9slice_pressed_base"),
                selectedSprite = face.sprite,
                disabledSprite = KitSprite("btn/dojo_btn_disabled_9slice_idle_base"),
            };

            return button;
        }

        static TextMeshProUGUI Text(RectTransform rect, string words, TMP_FontAsset font, float size, Color colour)
        {
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = size;
            text.color = colour;
            text.alignment = TextAlignmentOptions.Center;
            text.text = words;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            return text;
        }

        static RectTransform BottomLeft(string name, RectTransform parent, float x, float y, float width, float height)
        {
            var rect = Rect(name, parent);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        static RectTransform Stretched(string name, RectTransform parent)
        {
            var rect = Rect(name, parent);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        static RectTransform Rect(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        static void Wire(Object target, params (string field, Object value)[] references)
        {
            var so = new SerializedObject(target);

            foreach (var (field, value) in references)
            {
                var property = so.FindProperty(field);
                if (property == null)
                {
                    Debug.LogError("[Floors] " + target.GetType().Name + " has no field '" + field + "'.");
                    continue;
                }

                property.objectReferenceValue = value;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static Sprite KitSprite(string name)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Kit + name + "@3x.png");
            if (sprite == null)
            {
                Debug.LogError("[Floors] The UI kit has no " + name + ".");
            }

            return sprite;
        }

        static TMP_FontAsset Font(string name)
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Fonts + name + ".asset");
            if (font == null)
            {
                Debug.LogError("[Floors] Missing font " + name + ".");
            }

            return font;
        }

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var colour);
            return colour;
        }
    }
}
