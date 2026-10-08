using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Framework.UI
{
    /// <summary>
    /// The furniture a generated modal is made of: an overlay canvas, a dimmed sheet, a panel,
    /// labels and buttons.
    /// </summary>
    /// <remarks>
    /// Shared by every dialog that builds itself, so the three of them look like one family rather
    /// than three people's guesses, and so the awkward parts — a canvas that really does sit above
    /// everything, a scrim that really does block the game behind it — are solved once.
    /// </remarks>
    public static class DialogChrome
    {
        /// <summary>Well clear of anything the game's own canvases are likely to use.</summary>
        /// <remarks>
        /// One below the popup layer, which is the only thing allowed over a question: a popup such
        /// as "connection lost" has to reach the player whatever else is on screen.
        /// </remarks>
        public const int TopMostSortingOrder = PopupManager.SortingOrder - 1;

        /// <summary>Panel background.</summary>
        public static readonly Color Panel = new Color(0.13f, 0.14f, 0.17f, 0.98f);

        /// <summary>The accepting button.</summary>
        public static readonly Color Accept = new Color(0.20f, 0.52f, 0.90f, 1f);

        /// <summary>The dismissing button.</summary>
        public static readonly Color Dismiss = new Color(0.32f, 0.34f, 0.38f, 1f);

        /// <summary>A row in a list, unselected.</summary>
        public static readonly Color Row = new Color(0.20f, 0.22f, 0.26f, 1f);

        /// <summary>A row in a list, selected.</summary>
        public static readonly Color RowChosen = new Color(0.20f, 0.52f, 0.90f, 1f);

        /// <summary>
        /// Turns a bare GameObject into a canvas that draws over everything.
        /// </summary>
        /// <remarks>
        /// Screen Space - Overlay on purpose. Overlay draws after every camera, including stacked
        /// overlay cameras, so the question is on top of the world, the UI, and anything being
        /// dragged — which is the one thing a modal has to get right.
        /// </remarks>
        public static void MakeOverlay(GameObject host)
        {
            var canvas = host.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = host.AddComponent<Canvas>();
            }

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = TopMostSortingOrder;

            if (host.GetComponent<CanvasScaler>() == null)
            {
                var scaler = host.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
            }

            if (host.GetComponent<GraphicRaycaster>() == null)
            {
                host.AddComponent<GraphicRaycaster>();
            }
        }

        /// <summary>
        /// A dimmed sheet over the whole screen. It is a raycast target, which is what makes a
        /// dialog modal: clicks meant for the game behind it stop here.
        /// </summary>
        public static Image AddScrim(RectTransform parent)
        {
            var scrim = Stretched("Scrim", parent).gameObject.AddComponent<Image>();
            scrim.color = new Color(0f, 0f, 0f, 0.55f);

            return scrim;
        }

        public static RectTransform Stretched(string name, RectTransform parent)
        {
            var rect = New(name, parent);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            return rect;
        }

        public static RectTransform Centred(string name, RectTransform parent, Vector2 size)
        {
            var rect = New(name, parent);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;

            return rect;
        }

        public static RectTransform New(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);

            return rect;
        }

        public static TMP_Text AddLabel(
            string name, RectTransform parent, Vector2 position, Vector2 size, float fontSize)
        {
            var rect = Centred(name, parent, size);
            rect.anchoredPosition = position;

            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;

            // Only needed where the project has no default font configured; TMP assigns one itself
            // when TMP Settings exist, and a label with no font draws nothing at all.
            if (text.font == null)
            {
                text.font = TMP_Settings.defaultFontAsset;
            }

            return text;
        }

        public static Button AddButton(
            string name, RectTransform parent, Vector2 position, Vector2 size, string caption, Color colour)
        {
            var rect = Centred(name, parent, size);
            rect.anchoredPosition = position;

            var image = rect.gameObject.AddComponent<Image>();
            image.color = colour;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            AddLabel("Text", rect, Vector2.zero, size, 28f).text = caption;

            return button;
        }
    }
}
