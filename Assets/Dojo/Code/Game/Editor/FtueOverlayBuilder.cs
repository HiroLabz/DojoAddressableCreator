using Dojo.Framework.UI;
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
    /// Builds the first-time tour's overlay into the Game scene, to the FTUE mock-ups: a canvas of
    /// its own under the popup layer, four dim panels for the spotlight, a glowing frame, and the
    /// card along the bottom.
    /// </summary>
    /// <remarks>
    /// Re-runnable: each run removes the overlay it built last time and builds it again. Every part
    /// is from the UI kit - the stat-card panel, the primary and ghost buttons, the icon plate, the
    /// popup glow - and the rounded dot is the <see cref="KitStandIns"/> box.
    /// </remarks>
    public static class FtueOverlayBuilder
    {
        const string RootName = "FtueCanvas";
        const string Kit = "Assets/Dojo/Art/2D/UI/";
        const string Fonts = "Assets/Dojo/Fonts/TMP/";

        // Sized for the larger heading and body, tall enough for both - and no wider than it has to
        // be, so beside the menu's entries it still leaves a clear gap to the one it points at.
        const float CardWidth = 1160f;
        const float CardHeight = 184f;
        const float Inset = 28f;
        const float TextLeft = 112f;
        const float TextWidth = 650f;

        static readonly Color Dim = new Color(0.024f, 0.035f, 0.063f, 0.78f);   // the kit's void, #060910
        static readonly Color Ink = Color.white;
        static readonly Color InkSoft = Hex("#9fb2cc");
        static readonly Color Signal = Hex("#2f8fff");
        static readonly Color PanelTint = Hex("#0e1729");

        [MenuItem("Tools/Dojo/Build FTUE Overlay")]
        public static void BuildInOpenScene()
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);

                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root.GetComponentInChildren<InGameCanvasUI>(true) != null)
                    {
                        Debug.Log("[FTUE] " + Build(scene));
                        return;
                    }
                }
            }

            EditorUtility.DisplayDialog("Build FTUE Overlay", "Open the Game scene first.", "OK");
        }

        /// <summary>Builds the overlay into <paramref name="scene"/>. Returns a line saying what was done.</summary>
        public static string Build(Scene scene)
        {
            foreach (var old in scene.GetRootGameObjects())
            {
                if (old.name == RootName)
                {
                    Object.DestroyImmediate(old);
                }
            }

            var go = new GameObject(RootName, typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(go, scene);
            go.layer = LayerMask.NameToLayer("UI");

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = PopupManager.SortingOrder - 2;   // under popups, over dialogs and the game

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var raycaster = go.AddComponent<GraphicRaycaster>();
            var root = Stretched("Tour", (RectTransform)go.transform);

            // Spotlight: four dim panels around a hole, and the frame that rings it.
            var dimTop = DimPanel("DimTop", root);
            var dimBottom = DimPanel("DimBottom", root);
            var dimLeft = DimPanel("DimLeft", root);
            var dimRight = DimPanel("DimRight", root);

            var frame = BottomLeft("Frame", root).gameObject.AddComponent<Image>();
            frame.sprite = KitSprite("popup/dojo_popup_glowbox_idle_base");
            frame.type = Image.Type.Sliced;
            frame.color = Signal;
            frame.raycastTarget = false;

            // The card.
            var card = BottomLeft("Card", root);
            card.sizeDelta = new Vector2(CardWidth, CardHeight);

            // The notch first, so the panel covers its inner half and only the point shows.
            var notch = Rect("Notch", card);
            notch.anchorMin = notch.anchorMax = Vector2.zero;
            notch.pivot = new Vector2(0.5f, 0.5f);
            notch.sizeDelta = new Vector2(24f, 24f);
            notch.localRotation = Quaternion.Euler(0f, 0f, 45f);
            var notchImage = notch.gameObject.AddComponent<Image>();
            notchImage.sprite = KitStandIns.Get("account", "box");
            notchImage.type = Image.Type.Sliced;
            notchImage.color = PanelTint;
            notchImage.raycastTarget = false;

            var panel = Stretched("Panel", card).gameObject.AddComponent<Image>();
            panel.sprite = KitSprite("panel/dojo_panel_statcard_9slice_idle_base");
            panel.type = Image.Type.Sliced;
            panel.raycastTarget = true;

            var accent = Stretched("Accent", card).gameObject.AddComponent<Image>();
            accent.sprite = KitSprite("btn/dojo_btn_ghost_9slice_idle_base");
            accent.type = Image.Type.Sliced;
            accent.color = new Color(Signal.r, Signal.g, Signal.b, 0.35f);
            accent.raycastTarget = false;

            var plate = TopLeft("IconPlate", card, Inset, (CardHeight - 64f) * 0.5f, 64f, 64f).gameObject.AddComponent<Image>();
            plate.sprite = KitSprite("btn/dojo_btn_iconplate_9slice_idle_base");
            plate.type = Image.Type.Sliced;
            plate.raycastTarget = false;

            var iconRect = Rect("Icon", plate.rectTransform);
            iconRect.anchorMin = iconRect.anchorMax = iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.sizeDelta = new Vector2(30f, 30f);
            var icon = iconRect.gameObject.AddComponent<Image>();
            icon.sprite = KitSprite("icon/dojo_icon_inventory_flat_idle_base");
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            var eyebrow = Label(card, "Eyebrow", TextLeft, 22f, 520f, 20f, Font("JetBrainsMono-Medium SDF"), 13f, Signal);
            eyebrow.characterSpacing = 25f;
            eyebrow.text = "STEP 1 OF 3";

            var title = Label(card, "Title", TextLeft, 44f, TextWidth, 46f, Font("Poppins-SemiBold SDF"), 34f, Ink);
            title.text = "Welcome to the studio floor";

            var body = Label(card, "Body", TextLeft, 94f, TextWidth, 64f, Font("Poppins-Regular SDF"), 21f, InkSoft);
            body.textWrappingMode = TextWrappingModes.Normal;
            body.alignment = TextAlignmentOptions.TopLeft;
            body.text = "This is your world.";

            var dots = new Image[3];
            for (var i = 0; i < dots.Length; i++)
            {
                var dot = Rect("Dot" + (i + 1), card);
                dot.anchorMin = dot.anchorMax = dot.pivot = new Vector2(1f, 1f);
                dot.anchoredPosition = new Vector2(-(Inset + (dots.Length - 1 - i) * 16f), -22f);
                dot.sizeDelta = new Vector2(9f, 9f);
                dots[i] = dot.gameObject.AddComponent<Image>();
                dots[i].sprite = KitStandIns.Get("account", "box");
                dots[i].type = Image.Type.Sliced;
                dots[i].raycastTarget = false;
            }

            TMP_Text primaryLabel;
            var primary = Primary(card, 180f, 56f, out primaryLabel);

            TMP_Text secondaryLabel;
            var secondary = Ghost(card, 150f, 52f, -(Inset + 180f + 16f), out secondaryLabel);

            var tour = go.AddComponent<FtueTour>();
            Wire(tour,
                ("canvas", canvas), ("raycaster", raycaster), ("root", root),
                ("dimTop", dimTop), ("dimBottom", dimBottom), ("dimLeft", dimLeft), ("dimRight", dimRight),
                ("frame", frame), ("card", card), ("cardAccent", accent), ("notch", notch),
                ("iconPlate", plate), ("icon", icon), ("eyebrow", eyebrow), ("title", title), ("body", body),
                ("secondary", secondary), ("secondaryLabel", secondaryLabel),
                ("primary", primary), ("primaryLabel", primaryLabel), ("primaryGlow", primaryGlow),
                ("inventoryIcon", KitSprite("icon/dojo_icon_inventory_flat_idle_base")),
                ("cameraIcon", KitSprite("icon/dojo_icon_maximise_flat_idle_base")),
                ("agentsIcon", KitSprite("icon/dojo_icon_agents_flat_idle_base")),
                ("menuIcon", KitSprite("icon/dojo_icon_menu_flat_idle_base")),
                ("placeIcon", KitSprite("icon/dojo_icon_furniture_flat_idle_base")),
                ("saveIcon", KitSprite("icon/dojo_icon_save_flat_idle_base")),
                ("shopIcon", KitSprite("shop/shop-icon-glyph-pink")));

            var so = new SerializedObject(tour);
            var list = so.FindProperty("dots");
            list.arraySize = dots.Length;
            for (var i = 0; i < dots.Length; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = dots[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            // Hidden until the tour has something to show.
            canvas.enabled = false;
            raycaster.enabled = false;

            EditorSceneManager.MarkSceneDirty(scene);
            return "built the FTUE overlay into '" + scene.name + "' at sort order " + canvas.sortingOrder + ".";
        }

        // ── Pieces ────────────────────────────────────────────────────────────────────────

        static RectTransform DimPanel(string name, RectTransform root)
        {
            var rect = BottomLeft(name, root);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = Dim;
            image.raycastTarget = true;   // the world behind the spotlight is not for clicking
            return rect;
        }

        /// <summary>The glow the last <see cref="Primary"/> made, for the tour to switch off with the button.</summary>
        static Image primaryGlow;

        static Button Primary(RectTransform card, float width, float height, out TMP_Text label)
        {
            const float halo = 26.67f;
            var glow = RightMiddle("Primary Glow", card, -(Inset - halo), width + halo * 2f, height + halo * 2f)
                .gameObject.AddComponent<Image>();
            glow.sprite = KitSprite("btn/dojo_btn_primary_9slice_idle_glow");
            glow.type = Image.Type.Sliced;
            glow.color = Signal;
            glow.raycastTarget = false;

            var face = RightMiddle("Primary", card, -Inset, width, height).gameObject.AddComponent<Image>();
            face.sprite = KitSprite("btn/dojo_btn_primary_9slice_idle_base");
            face.type = Image.Type.Sliced;

            var button = face.gameObject.AddComponent<Button>();
            button.targetGraphic = face;
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState
            {
                highlightedSprite = KitSprite("btn/dojo_btn_primary_9slice_hover_base"),
                pressedSprite = KitSprite("btn/dojo_btn_primary_9slice_pressed_base"),
                selectedSprite = face.sprite,
                disabledSprite = KitSprite("btn/dojo_btn_disabled_9slice_idle_base"),
            };

            label = Text(face.rectTransform, "Label", "NEXT", Font("Poppins-SemiBold SDF"), 15f, Ink);
            label.characterSpacing = 12f;
            primaryGlow = glow;
            return button;
        }

        static Button Ghost(RectTransform card, float width, float height, float right, out TMP_Text label)
        {
            var face = RightMiddle("Secondary", card, right, width, height).gameObject.AddComponent<Image>();
            face.sprite = KitSprite("btn/dojo_btn_ghost_9slice_idle_base");
            face.type = Image.Type.Sliced;

            var button = face.gameObject.AddComponent<Button>();
            button.targetGraphic = face;
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState
            {
                highlightedSprite = KitSprite("btn/dojo_btn_ghost_9slice_hover_base"),
                pressedSprite = KitSprite("btn/dojo_btn_ghost_9slice_pressed_base"),
                selectedSprite = face.sprite,
            };

            label = Text(face.rectTransform, "Label", "SKIP TOUR", Font("Poppins-SemiBold SDF"), 13f, InkSoft);
            label.characterSpacing = 12f;
            return button;
        }

        // ── Layout helpers ────────────────────────────────────────────────────────────────

        static TextMeshProUGUI Label(RectTransform parent, string name, float left, float top, float width,
            float height, TMP_FontAsset font, float size, Color colour)
        {
            var rect = TopLeft(name, parent, left, top, width, height);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = size;
            text.color = colour;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.richText = true;
            text.raycastTarget = false;
            return text;
        }

        static TextMeshProUGUI Text(RectTransform parent, string name, string words, TMP_FontAsset font, float size, Color colour)
        {
            var rect = Stretched(name, parent);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = size;
            text.color = colour;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.text = words;
            text.raycastTarget = false;
            return text;
        }

        static RectTransform TopLeft(string name, RectTransform parent, float left, float top, float width, float height)
        {
            var rect = Rect(name, parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(left, -top);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        static RectTransform RightMiddle(string name, RectTransform parent, float x, float width, float height)
        {
            var rect = Rect(name, parent);
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(x, 0f);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        static RectTransform BottomLeft(string name, RectTransform parent)
        {
            var rect = Rect(name, parent);
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
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
                    Debug.LogError("[FTUE] " + target.GetType().Name + " has no field '" + field + "'.");
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
                Debug.LogError("[FTUE] The UI kit has no " + name + ".");
            }

            return sprite;
        }

        static TMP_FontAsset Font(string name)
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Fonts + name + ".asset");
            if (font == null)
            {
                Debug.LogError("[FTUE] Missing font " + name + ".");
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
