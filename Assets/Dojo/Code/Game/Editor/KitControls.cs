using Dojo.Framework.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Game.Editor
{
    /// <summary>
    /// The popups' dropdown and checkbox, drawn with the UI kit's art rather than Unity's default
    /// sprites (ClickUp 14ze67xch6g): the kit's chevron for the arrow, its check for the ticks, its
    /// scroll track and handle for the list's scrollbar, and its selected roster row for the line
    /// under the pointer.
    /// </summary>
    /// <remarks>
    /// Shared by <see cref="DepartmentPopupBuilder"/> and <see cref="BusinessPopupBuilder"/>, so both
    /// popups' controls look the same. Unity's <c>TMP_DefaultControls</c> still lays the dropdown out;
    /// every sprite it was given is then replaced here, and nothing of the stock skin is left showing.
    /// </remarks>
    public static class KitControls
    {
        const string Art = "Assets/Dojo/Art/2D/UI/";
        const string ChevronPath = Art + "icon/dojo_icon_chevron_flat_idle_base@3x.png";
        const string CheckPath = Art + "account/dojo_account_check_idle_base@3x.png";
        const string TrackPath = Art + "bar/dojo_bar_scroll_track_idle_base@3x.png";
        const string HandlePath = Art + "bar/dojo_bar_scroll_handle_idle_base@3x.png";
        const string HighlightPath = Art + "row/dojo_row_roster_9slice_selected_base@3x.png";

        /// <summary>The kit's signal blue.</summary>
        public static readonly Color Accent = new Color32(0x2f, 0x8f, 0xff, 0xff);

        /// <summary>The kit's ink soft: the arrow at rest.</summary>
        static readonly Color InkSoft = new Color32(0x9f, 0xb2, 0xcc, 0xff);

        static readonly Color ListBack = new Color32(0x0d, 0x18, 0x2b, 0xf8);

        /// <summary>The width of the list's scrollbar, as the Inventory's lists have it.</summary>
        const float ScrollbarWidth = 8f;

        /// <summary>The kit's check, for a ticked box.</summary>
        public static Sprite Check => Load(CheckPath);

        static Sprite Load(string path)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);

            if (sprite == null)
            {
                Debug.LogError("[Popups] The UI kit sprite '" + path + "' is missing.");
            }

            return sprite;
        }

        /// <summary>
        /// A dropdown in <paramref name="row"/>, styled like the box it stands beside: its face, its
        /// font, and <paramref name="hint"/> while nothing is picked. Its list opens over the popup.
        /// </summary>
        public static TMP_Dropdown Dropdown(Transform row, LayoutElement fieldLayout, Image fieldImage,
            TextMeshProUGUI fieldText, TextMeshProUGUI fieldPlaceholder, string hint)
        {
            // Only the layout is wanted from the defaults; every sprite is replaced below.
            var go = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources());
            go.name = "Field";
            go.transform.SetParent(row, false);

            var layout = go.AddComponent<LayoutElement>();
            layout.minHeight = fieldLayout.minHeight;
            layout.preferredHeight = fieldLayout.preferredHeight;
            layout.flexibleWidth = 1f;

            CopyFace(go.GetComponent<Image>(), fieldImage);

            var dropdown = go.GetComponent<TMP_Dropdown>();

            var label = (RectTransform)go.transform.Find("Label");
            label.offsetMin = new Vector2(18.67f, 5.33f);
            label.offsetMax = new Vector2(-56f, -5.33f);
            Style(label.GetComponent<TextMeshProUGUI>(), fieldText);

            // Shown while nothing is picked.
            var placeholder = Object.Instantiate(label.gameObject, go.transform);
            placeholder.name = "Placeholder";
            var placeholderText = placeholder.GetComponent<TextMeshProUGUI>();
            Style(placeholderText, fieldPlaceholder);
            placeholderText.text = hint;
            var wiring = new SerializedObject(dropdown);
            wiring.FindProperty("m_Placeholder").objectReferenceValue = placeholderText;
            wiring.ApplyModifiedPropertiesWithoutUndo();

            var arrow = (RectTransform)go.transform.Find("Arrow");
            arrow.anchoredPosition = new Vector2(-28f, 0f);
            arrow.sizeDelta = new Vector2(24f, 24f);
            var arrowImage = arrow.GetComponent<Image>();
            arrowImage.sprite = Load(ChevronPath);
            arrowImage.type = Image.Type.Simple;
            arrowImage.preserveAspect = true;
            arrowImage.color = InkSoft;

            StyleList((RectTransform)go.transform.Find("Template"), fieldImage, fieldText);

            dropdown.options.Clear();
            return AsPopupDropdown(go, dropdown);
        }

        /// <summary>The list that opens: the field's face, the kit's scrollbar, highlight and check.</summary>
        static void StyleList(RectTransform template, Image fieldImage, TextMeshProUGUI fieldText)
        {
            template.sizeDelta = new Vector2(0f, 340f);
            var back = template.GetComponent<Image>();
            CopyFace(back, fieldImage);
            back.color = ListBack;

            // A mask needs a graphic, not a picture: the stock mask sprite goes, and the viewport
            // clips to its own rectangle.
            var viewport = (RectTransform)template.Find("Viewport");
            var mask = viewport.GetComponent<Image>();
            mask.sprite = null;
            mask.type = Image.Type.Simple;
            viewport.offsetMax = new Vector2(-(ScrollbarWidth + 6f), viewport.offsetMax.y);

            var content = (RectTransform)viewport.Find("Content");
            content.sizeDelta = new Vector2(content.sizeDelta.x, 44f);

            var item = (RectTransform)content.Find("Item");
            item.sizeDelta = new Vector2(item.sizeDelta.x, 44f);

            // The line under the pointer is lit with the kit's selected row, and dark otherwise.
            var highlight = item.Find("Item Background").GetComponent<Image>();
            highlight.sprite = Load(HighlightPath);
            highlight.type = Image.Type.Sliced;
            highlight.color = Color.white;
            var toggle = item.GetComponent<Toggle>();
            toggle.targetGraphic = highlight;
            var colours = toggle.colors;
            colours.normalColor = new Color(1f, 1f, 1f, 0f);
            colours.highlightedColor = Color.white;
            colours.selectedColor = Color.white;
            colours.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colours.disabledColor = new Color(1f, 1f, 1f, 0f);
            colours.colorMultiplier = 1f;
            colours.fadeDuration = 0.08f;
            toggle.colors = colours;

            // The tick in its own column, clear of the words, where the field's text starts.
            var check = item.Find("Item Checkmark").GetComponent<Image>();
            check.sprite = Check;
            check.type = Image.Type.Simple;
            check.preserveAspect = true;
            check.color = Accent;
            var checkRect = (RectTransform)check.transform;
            checkRect.anchoredPosition = new Vector2(20f, 0f);
            checkRect.sizeDelta = new Vector2(18f, 18f);

            var itemLabel = (RectTransform)item.Find("Item Label");
            itemLabel.offsetMin = new Vector2(42f, itemLabel.offsetMin.y);
            itemLabel.offsetMax = new Vector2(-12f, itemLabel.offsetMax.y);
            Style(itemLabel.GetComponent<TextMeshProUGUI>(), fieldText);

            // The Inventory's scrollbar: the kit's track, its handle filling the sliding area.
            var bar = (RectTransform)template.Find("Scrollbar");
            bar.sizeDelta = new Vector2(ScrollbarWidth, bar.sizeDelta.y);
            var track = bar.GetComponent<Image>();
            track.sprite = Load(TrackPath);
            track.type = Image.Type.Sliced;
            track.color = Color.white;

            var sliding = (RectTransform)bar.Find("Sliding Area");
            sliding.offsetMin = Vector2.zero;
            sliding.offsetMax = Vector2.zero;

            var handle = (RectTransform)sliding.Find("Handle");
            handle.offsetMin = Vector2.zero;
            handle.offsetMax = Vector2.zero;
            var handleImage = handle.GetComponent<Image>();
            handleImage.sprite = Load(HandlePath);
            handleImage.type = Image.Type.Sliced;
            handleImage.color = Color.white;

            var scroll = template.GetComponent<ScrollRect>();
            scroll.verticalScrollbarSpacing = 0f;
        }

        static void CopyFace(Image face, Image like)
        {
            face.sprite = like.sprite;
            face.type = like.type;
            face.color = like.color;
            face.pixelsPerUnitMultiplier = like.pixelsPerUnitMultiplier;
        }

        /// <summary>A text styled like another, on one line, cut with an ellipsis.</summary>
        public static void Style(TextMeshProUGUI text, TextMeshProUGUI like)
        {
            text.font = like.font;
            text.fontSize = like.fontSize;
            text.color = like.color;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
        }

        /// <summary>
        /// The same dropdown, made a <see cref="PopupDropdown"/> so its list opens over the popup
        /// rather than behind it. The script is swapped in place, which keeps every setting and
        /// reference the default controls made.
        /// </summary>
        static TMP_Dropdown AsPopupDropdown(GameObject go, TMP_Dropdown dropdown)
        {
            MonoScript script = null;
            foreach (var candidate in MonoImporter.GetAllRuntimeMonoScripts())
            {
                if (candidate != null && candidate.GetClass() == typeof(PopupDropdown))
                {
                    script = candidate;
                    break;
                }
            }

            if (script == null)
            {
                Debug.LogError("[Popups] PopupDropdown's script was not found; the list will open behind the popup.");
                return dropdown;
            }

            var serialized = new SerializedObject(dropdown);
            serialized.FindProperty("m_Script").objectReferenceValue = script;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return go.GetComponent<PopupDropdown>();
        }
    }
}
