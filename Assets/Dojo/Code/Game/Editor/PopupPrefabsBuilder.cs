using System.Collections.Generic;
using System.IO;
using Dojo.Framework.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Choice = Dojo.Framework.UI.MessagePopup.Choice;
using Tone = Dojo.Framework.UI.MessagePopup.Tone;

namespace Dojo.Game.Editor
{
    /// <summary>
    /// Makes the four popup prefabs in <c>Prefabs/UI/Popups</c> to the Dialogs mock-up: Notice,
    /// Affirmation, Confirmation and Three-way.
    /// </summary>
    /// <remarks>
    /// Same shell every time - the kit's panel, a top edge and corner markers in the popup's tone,
    /// an icon badge, the title and explanation, and the buttons right-aligned in ascending
    /// commitment. Only the tone, the icon and the buttons differ. The words baked in are the
    /// mock-up's samples; whoever shows a popup replaces them through <see cref="MessagePopup"/>.
    /// <para>
    /// Every size below is the mock-up's own, in its pixels, times <see cref="S"/> - it is drawn 1440
    /// wide for the game's 1920 - so this file can be checked against the picture. The card's
    /// height and each button's width are left to layout, so they follow the words a popup is
    /// given, not the samples.
    /// </para>
    /// <para>
    /// The badge's glow, the top edge's glow, and the warning and info marks are stand-ins drawn by
    /// <see cref="KitStandIns"/> into <c>Art/2D/UI/popup</c>; the box, its edge, the corner marker and
    /// the tick are the account cards' own.
    /// </para>
    /// <para>
    /// Re-runnable: each run overwrites the four prefabs in place, keeping their GUIDs, so anything
    /// that already points at one keeps working.
    /// </para>
    /// </remarks>
    public static class PopupPrefabsBuilder
    {
        public const string Folder = "Assets/Dojo/Prefabs/UI/Popups/";

        const string Kit = "Assets/Dojo/Art/2D/UI/";
        const string Fonts = "Assets/Dojo/Fonts/TMP/";

        /// <summary>Mock-up pixels to canvas units: drawn at 1440 wide, shown at 1920.</summary>
        const float S = 1920f / 1440f;

        // The kit's palette (_palette.json).
        static readonly Color Void = Hex("#060910");
        static readonly Color Edge = Hex("#2a4570");
        static readonly Color Ink = Hex("#ffffff");
        static readonly Color InkSoft = Hex("#9fb2cc");
        static readonly Color InkMute = Hex("#6d809c");

        // The tones come from the popup itself, so the colour a prefab is built in and the colour
        // Tint gives it at run time cannot drift apart.
        static readonly Color Signal = MessagePopup.ColorOf(Tone.Info);
        static readonly Color Danger = MessagePopup.ColorOf(Tone.Alert);

        // In the mock-up's pixels.
        const float Width = 620f;
        const float Inset = 26f;
        const float Badge = 46f;
        const float ButtonHeight = 46f;
        const float Corner = 15f;

        /// <summary>
        /// One popup: its default tone, its sample words and its buttons, left to right. The mark
        /// follows the tone.
        /// </summary>
        sealed class Spec
        {
            public string Name;
            public Tone Tone;
            public string Title;
            public string Body;
            public string Detail;
            public (Choice choice, string caption)[] Buttons;

            /// <summary>Whether it has a box to type an answer in, under the words.</summary>
            public bool Answer;
        }

        static IEnumerable<Spec> Specs()
        {
            yield return new Spec
            {
                Name = "NoticePopup",
                Tone = Tone.Alert,
                Title = "Connection lost",
                Body = "Dojo can't reach the server, so nothing you build from here would be saved. Close the app and start it again.",
                Detail = "spacetime · ECONNRESET · 16:04:22",
                Buttons = new (Choice, string)[0],
            };

            yield return new Spec
            {
                Name = "AffirmationPopup",
                Tone = Tone.Go,
                Title = "World saved",
                Body = "Studio Floor is saved with 34 pieces and 3 agents.",
                Buttons = new[] { (Choice.Primary, "OK") },
            };

            yield return new Spec
            {
                Name = "ConfirmationPopup",
                Tone = Tone.Info,
                Title = "Load Ground Floor?",
                Body = "The floor you have open has changes that are not saved. Loading replaces it.",
                Buttons = new[] { (Choice.Cancel, "CANCEL"), (Choice.Primary, "LOAD WORLD") },
            };

            yield return new Spec
            {
                // A warning, and warnings are red: there is no amber any more.
                Name = "ThreeWayPopup",
                Tone = Tone.Alert,
                Title = "You have unsaved changes",
                Body = "Studio Floor has 6 pieces placed since the last save. What should happen to them?",
                Buttons = new[] { (Choice.Cancel, "CANCEL"), (Choice.Destructive, "DISCARD"), (Choice.Primary, "SAVE AND EXIT") },
            };

            yield return new Spec
            {
                // A question with a typed answer: the name of something just made.
                Name = "PromptPopup",
                Tone = Tone.Info,
                Title = "Name this area",
                Body = "What should this area be called? It's how agents and the elevator will know it.",
                Buttons = new[] { (Choice.Cancel, "CANCEL"), (Choice.Primary, "SAVE") },
                Answer = true,
            };
        }

        [MenuItem("Tools/Dojo/Build Popup Prefabs")]
        public static void BuildFromMenu()
        {
            Debug.Log("[Popups] " + Build());
        }

        /// <summary>Makes or remakes all four prefabs. Returns a line describing what was done.</summary>
        public static string Build()
        {
            var p = new Parts();
            Directory.CreateDirectory(Folder);

            // Put together in a preview scene, so the working copies never touch - or mark as
            // changed - whatever scene is open.
            var stage = EditorSceneManager.NewPreviewScene();
            var made = new List<string>();

            try
            {
                foreach (var spec in Specs())
                {
                    var root = Popup(spec, stage, p);
                    PrefabUtility.SaveAsPrefabAsset(root, Folder + spec.Name + ".prefab", out var saved);

                    if (saved)
                    {
                        made.Add(spec.Name);
                    }
                    else
                    {
                        Debug.LogError("[Popups] Could not save " + Folder + spec.Name + ".prefab.");
                    }
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(stage);
            }

            AssetDatabase.SaveAssets();
            return "made " + string.Join(", ", made) + " in " + Folder;
        }

        // ── The popup ─────────────────────────────────────────────────────────────────────

        static GameObject Popup(Spec spec, Scene stage, Parts p)
        {
            var root = new GameObject(spec.Name, typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(root, stage);
            root.layer = LayerMask.NameToLayer("UI");

            var layer = (RectTransform)root.transform;
            layer.anchorMin = Vector2.zero;
            layer.anchorMax = Vector2.one;
            layer.offsetMin = layer.offsetMax = Vector2.zero;

            root.AddComponent<CanvasGroup>();
            var popup = root.AddComponent<MessagePopup>();

            // Over the whole screen and catching clicks: every popup is modal, the Notice most of all.
            var scrim = Stretched("Scrim", layer).gameObject.AddComponent<Image>();
            scrim.color = new Color(Void.r, Void.g, Void.b, 0.7f);
            scrim.raycastTarget = true;

            var card = Rect("Card", layer);
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(Width * S, 0f);

            var column = card.gameObject.AddComponent<VerticalLayoutGroup>();
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Everything drawn in the tone, handed to the popup so Tint can recolour it.
            var toned = new List<Graphic>();

            Shell(card, MessagePopup.ColorOf(spec.Tone), p, toned);
            Head(card, spec, p, toned, out var title, out var body, out var detail, out var icon);

            var answer = spec.Answer ? AnswerField(card, p) : null;

            Button cancel = null, destructive = null, primary = null;

            if (spec.Buttons.Length > 0)
            {
                Footer(card, spec, p, out cancel, out destructive, out primary);
            }

            // Not laid out here: Unity saves whatever a layout controls as zero - the card's height,
            // the buttons' widths - and works it out again wherever the prefab is opened or shown.
            Wire(popup,
                ("title", title), ("body", body), ("detail", detail),
                ("cancel", cancel), ("destructive", destructive), ("primary", primary),
                ("answer", answer),
                ("icon", icon),
                ("alertMark", p.Mark(Tone.Alert)), ("goMark", p.Mark(Tone.Go)), ("infoMark", p.Mark(Tone.Info)));

            WireList(popup, "toned", toned);

            return root;
        }

        /// <summary>The panel, the glowing top edge and the corner markers, all outside the layout.</summary>
        static void Shell(RectTransform card, Color tone, Parts p, List<Graphic> toned)
        {
            var panel = Stretched("Panel", card).gameObject.AddComponent<Image>();
            panel.sprite = p.Panel;
            panel.type = Image.Type.Sliced;
            panel.raycastTarget = true;
            Ignore(panel);

            var glow = TopBand("Edge Glow", card, 16f * S, -1f * S);
            glow.sprite = p.GlowLine;
            glow.color = new Color(tone.r, tone.g, tone.b, 0.6f);
            Ignore(glow);
            toned.Add(glow);

            var edge = TopBand("Edge", card, 2f * S, -1f * S);
            edge.color = tone;
            Ignore(edge);
            toned.Add(edge);

            // Hollow squares tucked into each corner, flush with its two sides. The marker sprite's
            // square is 13.6 of its 24, the rest its halo, so the holder is sized up to match.
            var holderSize = Corner * S * 24f / 13.6f;
            var inward = Corner * S / 2f;
            var corners = new[] { new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(1f, 0f) };

            for (var i = 0; i < corners.Length; i++)
            {
                var holder = Rect("Corner" + (i + 1), card);
                holder.anchorMin = holder.anchorMax = corners[i];
                holder.pivot = new Vector2(0.5f, 0.5f);
                holder.anchoredPosition = new Vector2(
                    corners[i].x < 0.5f ? inward : -inward,
                    corners[i].y < 0.5f ? inward : -inward);
                holder.sizeDelta = new Vector2(holderSize, holderSize);

                var fill = Stretched("Fill", holder, holderSize * 0.28f).gameObject.AddComponent<Image>();
                fill.sprite = p.Box;
                fill.type = Image.Type.Sliced;
                fill.color = Void;
                fill.raycastTarget = false;

                var ring = Stretched("Ring", holder).gameObject.AddComponent<Image>();
                ring.sprite = p.Corner;
                ring.color = tone;
                ring.raycastTarget = false;
                toned.Add(ring);

                Ignore(holder.gameObject);
            }
        }

        /// <summary>The badge, then the title, the explanation and the detail line beside it.</summary>
        static void Head(RectTransform card, Spec spec, Parts p, List<Graphic> toned,
            out TMP_Text title, out TMP_Text body, out TMP_Text detail, out Image icon)
        {
            var head = Rect("Head", card);
            var row = head.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = Pad(Inset, Inset, 25f, 22f);
            row.spacing = 19f * S;
            row.childAlignment = TextAnchor.UpperLeft;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;

            icon = BadgeFor(head, spec, p, toned);

            var words = Rect("Words", head);
            var stack = words.gameObject.AddComponent<VerticalLayoutGroup>();
            stack.spacing = 5f * S;
            stack.childControlWidth = stack.childControlHeight = true;
            stack.childForceExpandWidth = true;
            stack.childForceExpandHeight = false;
            words.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            title = Text(words, "Title", spec.Title, p.Poppins600, 21f * S, Ink, TextAlignmentOptions.TopLeft, true);
            body = Text(words, "Body", spec.Body, p.Poppins400, 15f * S, InkSoft, TextAlignmentOptions.TopLeft, true);

            // Sat on the bottom of a taller box: the mock-up leaves more room above it than the
            // column's spacing gives.
            detail = Text(words, "Detail", spec.Detail ?? string.Empty, p.Mono400, 11f * S, InkMute, TextAlignmentOptions.BottomLeft, false);
            var detailBox = detail.gameObject.AddComponent<LayoutElement>();
            detailBox.minHeight = detailBox.preferredHeight = (16f + 7f) * S;
            detail.gameObject.SetActive(!string.IsNullOrEmpty(spec.Detail));
        }

        /// <summary>The icon badge. Returns the mark, which changes with the tone.</summary>
        static Image BadgeFor(RectTransform head, Spec spec, Parts p, List<Graphic> toned)
        {
            var tone = MessagePopup.ColorOf(spec.Tone);
            var badge = Rect("Badge", head);
            var size = badge.gameObject.AddComponent<LayoutElement>();
            size.minWidth = size.preferredWidth = Badge * S;
            size.minHeight = size.preferredHeight = Badge * S;
            size.flexibleWidth = 0f;

            // The stand-in boxes' corners drawn larger, for the mock-up's rounder badge. The glow
            // sprite's box sits 12 of its units in from its edge, 20 at this scale.
            const float rounder = 0.6f;

            var glow = Stretched("Glow", badge, -12f / rounder).gameObject.AddComponent<Image>();
            glow.sprite = p.GlowBox;
            glow.type = Image.Type.Sliced;
            glow.pixelsPerUnitMultiplier = rounder;
            glow.color = new Color(tone.r, tone.g, tone.b, 0.45f);
            glow.raycastTarget = false;
            toned.Add(glow);

            var fill = Stretched("Fill", badge).gameObject.AddComponent<Image>();
            fill.sprite = p.Box;
            fill.type = Image.Type.Sliced;
            fill.pixelsPerUnitMultiplier = rounder;
            fill.color = new Color(tone.r, tone.g, tone.b, 0.12f);
            fill.raycastTarget = false;
            toned.Add(fill);

            var ring = Stretched("Ring", badge).gameObject.AddComponent<Image>();
            ring.sprite = p.BoxEdge;
            ring.type = Image.Type.Sliced;
            ring.pixelsPerUnitMultiplier = rounder;
            ring.color = new Color(tone.r, tone.g, tone.b, 0.9f);
            ring.raycastTarget = false;
            toned.Add(ring);

            // The stand-in marks fill about 17 of their 24, so drawn at 22 they come out the
            // mock-up's 16 across.
            var icon = Rect("Icon", badge);
            icon.anchorMin = icon.anchorMax = icon.pivot = new Vector2(0.5f, 0.5f);
            icon.sizeDelta = new Vector2(22f * S, 22f * S);
            var mark = icon.gameObject.AddComponent<Image>();
            mark.sprite = p.Mark(spec.Tone);
            mark.color = tone;
            mark.raycastTarget = false;
            toned.Add(mark);

            return mark;
        }

        /// <summary>
        /// A box to type the answer in, full width under the words: the kit's text field, a hint
        /// in it while it is empty, one line, forty characters at most.
        /// </summary>
        static TMP_InputField AnswerField(RectTransform card, Parts p)
        {
            var holder = Rect("Answer", card);
            var row = holder.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = Pad(Inset, Inset, 0f, 22f);
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = true;
            row.childForceExpandHeight = false;

            var box = Rect("Field", holder);
            var size = box.gameObject.AddComponent<LayoutElement>();
            size.minHeight = size.preferredHeight = 44f * S;
            size.flexibleWidth = 1f;

            var face = box.gameObject.AddComponent<Image>();
            face.sprite = p.Field;
            face.type = Image.Type.Sliced;
            face.raycastTarget = true;

            var area = Stretched("Text Area", box);
            area.offsetMin = new Vector2(14f * S, 4f * S);
            area.offsetMax = new Vector2(-14f * S, -4f * S);
            area.gameObject.AddComponent<RectMask2D>();

            var hint = Text(area, "Placeholder", "Lobby, Labs, Market…", p.Poppins400, 16f * S, InkMute, TextAlignmentOptions.Left, false);
            Fill(hint.rectTransform);

            var typed = Text(area, "Text", string.Empty, p.Poppins500, 16f * S, Ink, TextAlignmentOptions.Left, false);
            Fill(typed.rectTransform);

            var field = box.gameObject.AddComponent<TMP_InputField>();
            field.textViewport = area;
            field.textComponent = typed;
            field.placeholder = hint;
            field.targetGraphic = face;
            field.characterLimit = 40;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.fontAsset = p.Poppins500;
            field.pointSize = 16f * S;

            return field;
        }

        static void Fill(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        /// <summary>A rule across the card, then the buttons, right-aligned.</summary>
        static void Footer(RectTransform card, Spec spec, Parts p, out Button cancel, out Button destructive, out Button primary)
        {
            var footer = Rect("Footer", card);
            var stack = footer.gameObject.AddComponent<VerticalLayoutGroup>();
            stack.childControlWidth = stack.childControlHeight = true;
            stack.childForceExpandWidth = true;
            stack.childForceExpandHeight = false;

            var divider = Rect("Divider", footer).gameObject.AddComponent<Image>();
            divider.color = new Color(Edge.r, Edge.g, Edge.b, 0.55f);
            divider.raycastTarget = false;
            var line = divider.gameObject.AddComponent<LayoutElement>();
            line.minHeight = line.preferredHeight = 1f * S;

            var row = Rect("Buttons", footer);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = Pad(Inset, 25f, 17f, 22f);
            layout.spacing = 11.5f * S;
            layout.childAlignment = TextAnchor.MiddleRight;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;

            cancel = destructive = primary = null;

            foreach (var (choice, caption) in spec.Buttons)
            {
                var button = PopupButton(row, choice, caption, p);

                switch (choice)
                {
                    case Choice.Cancel: cancel = button; break;
                    case Choice.Destructive: destructive = button; break;
                    default: primary = button; break;
                }
            }
        }

        /// <summary>
        /// One of the kit's buttons, as wide as its caption plus padding and never narrower than the
        /// mock-up's narrowest. The face and glow are stretched behind the caption, outside the
        /// layout, so only the caption decides the width.
        /// </summary>
        static Button PopupButton(RectTransform row, Choice choice, string caption, Parts p)
        {
            var host = Rect(choice.ToString(), row);

            var size = host.gameObject.AddComponent<LayoutElement>();
            size.minWidth = 140f * S;
            size.minHeight = size.preferredHeight = ButtonHeight * S;

            var fit = host.gameObject.AddComponent<HorizontalLayoutGroup>();
            fit.padding = Pad(34f, 34f, 0f, 0f);
            fit.childAlignment = TextAnchor.MiddleCenter;
            fit.childControlWidth = true;
            fit.childControlHeight = false;
            fit.childForceExpandWidth = fit.childForceExpandHeight = false;

            Sprite idle, hover, pressed;
            Color ink;
            TMP_FontAsset font;
            float spacing;

            switch (choice)
            {
                case Choice.Cancel:
                    idle = p.GhostIdle; hover = p.GhostHover; pressed = p.GhostPressed;
                    ink = InkSoft; font = p.Poppins500; spacing = 20f;
                    break;

                case Choice.Destructive:
                    idle = p.DangerIdle; hover = p.DangerHover; pressed = p.DangerPressed;
                    ink = Danger; font = p.Poppins600; spacing = 12f;
                    break;

                default:
                    idle = p.PrimaryIdle; hover = p.PrimaryHover; pressed = p.PrimaryPressed;
                    ink = Ink; font = p.Poppins600; spacing = 12f;
                    break;
            }

            if (choice == Choice.Primary)
            {
                // The kit's glow is 26.67 larger than its button all round.
                var glow = Stretched("Glow", host, -26.67f).gameObject.AddComponent<Image>();
                glow.sprite = p.PrimaryGlow;
                glow.type = p.PrimaryGlow != null && p.PrimaryGlow.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
                glow.color = Signal;
                glow.raycastTarget = false;
                Ignore(glow);
            }

            var face = Stretched("Face", host).gameObject.AddComponent<Image>();
            face.sprite = idle;
            face.type = Image.Type.Sliced;
            face.raycastTarget = true;
            Ignore(face);

            var label = Text(host, "Label", caption, font, 16f, ink, TextAlignmentOptions.Center, false);
            label.characterSpacing = spacing;
            label.rectTransform.sizeDelta = new Vector2(0f, 30f);

            var button = host.gameObject.AddComponent<Button>();
            button.targetGraphic = face;
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState
            {
                highlightedSprite = hover,
                pressedSprite = pressed,
                selectedSprite = idle,
                disabledSprite = p.Disabled,
            };

            return button;
        }

        // ── Layout helpers ────────────────────────────────────────────────────────────────

        /// <summary>A band along the card's top edge, full width, centred <paramref name="offset"/> below it.</summary>
        static Image TopBand(string name, RectTransform card, float height, float offset)
        {
            var rect = Rect(name, card);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, offset);
            rect.sizeDelta = new Vector2(0f, height);

            var image = rect.gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        static TextMeshProUGUI Text(RectTransform parent, string name, string words, TMP_FontAsset font,
            float size, Color colour, TextAlignmentOptions alignment, bool wrap)
        {
            var text = Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = size;
            text.color = colour;
            text.alignment = alignment;
            text.text = words;
            text.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>Left, right, top, bottom, in the mock-up's pixels.</summary>
        static RectOffset Pad(float left, float right, float top, float bottom)
        {
            return new RectOffset(
                Mathf.RoundToInt(left * S), Mathf.RoundToInt(right * S),
                Mathf.RoundToInt(top * S), Mathf.RoundToInt(bottom * S));
        }

        /// <summary>Kept out of its parent's layout group: decoration, placed by hand.</summary>
        static void Ignore(Component part) => Ignore(part.gameObject);

        static void Ignore(GameObject part) => part.AddComponent<LayoutElement>().ignoreLayout = true;

        static RectTransform Stretched(string name, RectTransform parent, float inset = 0f)
        {
            var rect = Rect(name, parent);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
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
                    Debug.LogError("[Popups] " + target.GetType().Name + " has no field '" + field + "'.");
                    continue;
                }

                property.objectReferenceValue = value;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void WireList<T>(Object target, string field, List<T> values) where T : Object
        {
            var so = new SerializedObject(target);
            var property = so.FindProperty(field);

            if (property == null || !property.isArray)
            {
                Debug.LogError("[Popups] " + target.GetType().Name + " has no list '" + field + "'.");
                return;
            }

            property.arraySize = values.Count;

            for (var i = 0; i < values.Count; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var colour);
            return colour;
        }

        // ── Sprites and fonts ─────────────────────────────────────────────────────────────

        /// <summary>Everything the popups are made of, loaded or drawn once per build.</summary>
        sealed class Parts
        {
            public readonly Sprite Panel = KitSprite("panel/dojo_panel_statcard_9slice_idle_base");
            public readonly Sprite PrimaryIdle = KitSprite("btn/dojo_btn_primary_9slice_idle_base");
            public readonly Sprite PrimaryHover = KitSprite("btn/dojo_btn_primary_9slice_hover_base");
            public readonly Sprite PrimaryPressed = KitSprite("btn/dojo_btn_primary_9slice_pressed_base");
            public readonly Sprite PrimaryGlow = KitSprite("btn/dojo_btn_primary_9slice_idle_glow");
            public readonly Sprite GhostIdle = KitSprite("btn/dojo_btn_ghost_9slice_idle_base");
            public readonly Sprite GhostHover = KitSprite("btn/dojo_btn_ghost_9slice_hover_base");
            public readonly Sprite GhostPressed = KitSprite("btn/dojo_btn_ghost_9slice_pressed_base");
            public readonly Sprite DangerIdle = KitSprite("btn/dojo_btn_danger_9slice_idle_base");
            public readonly Sprite DangerHover = KitSprite("btn/dojo_btn_danger_9slice_hover_base");
            public readonly Sprite DangerPressed = KitSprite("btn/dojo_btn_danger_9slice_pressed_base");
            public readonly Sprite Disabled = KitSprite("btn/dojo_btn_disabled_9slice_idle_base");
            public readonly Sprite Field = KitSprite("field/dojo_field_text_9slice_idle_base");

            public readonly Sprite Box = KitStandIns.Get("account", "box");
            public readonly Sprite BoxEdge = KitStandIns.Get("account", "boxedge");
            public readonly Sprite Corner = KitStandIns.Get("account", "corner");
            public readonly Sprite GlowBox = KitStandIns.Get("popup", "glowbox");
            public readonly Sprite GlowLine = KitStandIns.Get("popup", "glowline");

            readonly Sprite check = KitStandIns.Get("account", "check");
            readonly Sprite alert = KitStandIns.Get("popup", "alert");
            readonly Sprite info = KitStandIns.Get("popup", "info");

            public readonly TMP_FontAsset Poppins400 = Font("Poppins-Regular SDF");
            public readonly TMP_FontAsset Poppins500 = Font("Poppins-Medium SDF");
            public readonly TMP_FontAsset Poppins600 = Font("Poppins-SemiBold SDF");
            public readonly TMP_FontAsset Mono400 = Font("JetBrainsMono-Regular SDF");

            /// <summary>The badge's mark for a tone: a tick for a go, an "i" for information, "!" for the rest.</summary>
            public Sprite Mark(Tone tone)
            {
                switch (tone)
                {
                    case Tone.Go: return check;
                    case Tone.Info: return info;
                    default: return alert;
                }
            }
        }

        static Sprite KitSprite(string name)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Kit + name + "@3x.png");
            if (sprite == null)
            {
                Debug.LogError("[Popups] The UI kit has no " + name + ".");
            }

            return sprite;
        }

        static TMP_FontAsset Font(string name)
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Fonts + name + ".asset");
            if (font == null)
            {
                Debug.LogError("[Popups] Missing font " + name + ".");
            }

            return font;
        }
    }
}
