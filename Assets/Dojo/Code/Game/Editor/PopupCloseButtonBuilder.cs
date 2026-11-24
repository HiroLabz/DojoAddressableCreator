using System.IO;
using Dojo.Framework.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Game.Editor
{
    /// <summary>
    /// Puts the close button (an X at the card's top-right corner) on every popup prefab in
    /// <c>Prefabs/UI/Popups</c> and wires it to <see cref="MessagePopup"/>'s <c>close</c>
    /// (ClickUp 14ze67xg5my: every popup can be dismissed without choosing, the sign-in lock aside).
    /// </summary>
    /// <remarks>
    /// Drawn from the kit's own parts, no icon asset: a faint square that brightens under the
    /// pointer, with two bars crossed over it in the kit's soft ink. Safe to run again: a popup
    /// that has its Close is only rewired.
    /// </remarks>
    public static class PopupCloseButtonBuilder
    {
        const string Folder = "Assets/Dojo/Prefabs/UI/Popups";
        const string CloseName = "Close";

        static readonly Color SoftInk = new Color32(0x9f, 0xb2, 0xcc, 0xff);

        [MenuItem("Tools/Dojo/Build Popup Close Buttons")]
        public static void Build()
        {
            var built = 0;

            foreach (var path in Directory.GetFiles(Folder, "*.prefab"))
            {
                var assetPath = path.Replace('\\', '/');
                var root = PrefabUtility.LoadPrefabContents(assetPath);

                try
                {
                    var message = root.GetComponent<MessagePopup>();
                    var card = root.transform.Find("Card");

                    if (message == null || card == null)
                    {
                        continue;
                    }

                    var close = card.Find(CloseName) != null ? card.Find(CloseName).GetComponent<Button>() : null;

                    if (close == null)
                    {
                        close = MakeClose(card);
                    }

                    var wiring = new SerializedObject(message);
                    wiring.FindProperty("close").objectReferenceValue = close;
                    wiring.ApplyModifiedPropertiesWithoutUndo();

                    PrefabUtility.SaveAsPrefabAsset(root, assetPath);
                    built++;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[Popups] close buttons on " + built + " popup prefab(s) in " + Folder + ".");
        }

        /// <summary>A 40 x 40 button pinned to the card's top-right corner, outside the card's layout.</summary>
        static Button MakeClose(Transform card)
        {
            var go = new GameObject(CloseName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(card, false);
            go.transform.SetAsLastSibling();

            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-14f, -14f);
            rect.sizeDelta = new Vector2(40f, 40f);

            go.GetComponent<LayoutElement>().ignoreLayout = true;

            // The face is what the button tints: faint at rest, brighter under the pointer.
            var face = go.GetComponent<Image>();
            face.color = Color.white;
            face.raycastTarget = true;

            Bar(go.transform, 45f);
            Bar(go.transform, -45f);

            var button = go.GetComponent<Button>();
            button.targetGraphic = face;
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.normalColor = new Color(1f, 1f, 1f, 0.05f);
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.18f);
            colors.pressedColor = new Color(1f, 1f, 1f, 0.28f);
            colors.selectedColor = colors.normalColor;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.02f);
            colors.colorMultiplier = 1f;
            button.colors = colors;

            return button;
        }

        static void Bar(Transform under, float degrees)
        {
            var bar = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(under, false);

            var rect = (RectTransform)bar.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(16f, 2f);
            rect.localRotation = Quaternion.Euler(0f, 0f, degrees);

            var image = bar.GetComponent<Image>();
            image.color = SoftInk;
            image.raycastTarget = false;
        }
    }
}
