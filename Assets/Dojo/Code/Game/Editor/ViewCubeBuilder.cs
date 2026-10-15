using Dojo.Game.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Game.Editor
{
    /// <summary>
    /// Builds the view cube, top right of <c>GameCanvas/InGameUI</c>: TOP, LEFT, PERSP, RIGHT,
    /// FRONT and BACK, and the note shown in the side views.
    /// </summary>
    /// <remarks>
    /// The same layout, sizes and art as dojo-game-app's Game scene, where it was arranged by hand.
    /// A builder here so the Runtime scene gets the same cube without copying scene YAML. Replaces a
    /// cube left by an earlier run; safe to run again. The views themselves - <c>CameraViews</c> -
    /// need no setting up: <c>GameLifetimeScope</c> puts them on the camera rig.
    /// </remarks>
    public static class ViewCubeBuilder
    {
        const string ParentPath = "GameCanvas/InGameUI";
        const string FontPath = "Assets/Dojo/Fonts/TMP/JetBrainsMono-Regular SDF.asset";
        const string Art = "Assets/Dojo/Art/2D/UI/";
        const string PanelSprite = Art + "panel/dojo_panel_statcard_9slice_idle_base@3x.png";
        const string IdleFace = Art + "btn/dojo_btn_ghost_9slice_idle_base@3x.png";
        const string SelectedFace = Art + "btn/dojo_btn_iconplate_9slice_selected_base@3x.png";

        static TMP_FontAsset font;

        [MenuItem("Tools/Dojo/Add View Cube")]
        public static void Build()
        {
            var parent = GameObject.Find(ParentPath);

            if (parent == null)
            {
                Debug.LogError("[ViewCubeBuilder] Could not find " + ParentPath + ". Open the Runtime scene and run this again.");
                return;
            }

            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);

            var old = parent.transform.Find("ViewCube");
            if (old != null)
            {
                Object.DestroyImmediate(old.gameObject);
            }

            // The cube's corner of the screen: top right, clear of the inventory on the left.
            var root = Node("ViewCube", parent.transform);
            Place(root.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-24f, -64f), new Vector2(182f, 190f));

            var panel = Node("Panel", root.transform);
            Place(panel.GetComponent<RectTransform>(), new Vector2(0f, 1f), Vector2.zero, new Vector2(182f, 150f));
            Sliced(panel, PanelSprite);

            var title = Text(panel.transform, "Title", "VIEW", 11f, TextAlignmentOptions.Left);
            Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(12f, -8f), new Vector2(158f, 16f));
            title.color = new Color(1f, 1f, 1f, 0.6f);

            // A cross: TOP above PERSP, LEFT and RIGHT either side, FRONT and BACK below.
            var top = Face(panel.transform, "Top", "TOP", new Vector2(66f, -30f));
            var left = Face(panel.transform, "Left", "LEFT", new Vector2(12f, -64f));
            var perspective = Face(panel.transform, "Perspective", "PERSP", new Vector2(66f, -64f));
            var right = Face(panel.transform, "Right", "RIGHT", new Vector2(120f, -64f));
            var front = Face(panel.transform, "Front", "FRONT", new Vector2(39f, -98f));
            var back = Face(panel.transform, "Back", "BACK", new Vector2(93f, -98f));

            var note = Text(root.transform, "LookOnly", "LOOK ONLY. Switch to TOP or PERSP to place or paint.", 10f,
                TextAlignmentOptions.TopLeft);
            Place(note.rectTransform, new Vector2(0f, 1f), new Vector2(0f, -154f), new Vector2(182f, 34f));
            note.textWrappingMode = TextWrappingModes.Normal;
            note.color = new Color(1f, 0.78f, 0.35f, 1f);
            note.gameObject.SetActive(false);

            var cube = root.AddComponent<ViewCube>();
            var so = new SerializedObject(cube);
            so.FindProperty("panel").objectReferenceValue = panel;
            so.FindProperty("perspectiveButton").objectReferenceValue = perspective;
            so.FindProperty("topButton").objectReferenceValue = top;
            so.FindProperty("frontButton").objectReferenceValue = front;
            so.FindProperty("backButton").objectReferenceValue = back;
            so.FindProperty("leftButton").objectReferenceValue = left;
            so.FindProperty("rightButton").objectReferenceValue = right;
            so.FindProperty("idleFace").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(IdleFace);
            so.FindProperty("selectedFace").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(SelectedFace);
            so.FindProperty("lookOnlyNote").objectReferenceValue = note;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Down until the inventory opens: the cube shows itself in the Edit phase.
            panel.SetActive(false);

            EditorUtility.SetDirty(parent);
            EditorSceneManager.MarkSceneDirty(parent.scene);

            Debug.Log("[ViewCubeBuilder] Added the view cube to " + ParentPath + ". Save the scene to keep it.", root);
        }

        /// <summary>One view's button: a ghost plate, lit when its view is in force.</summary>
        static Button Face(Transform parent, string name, string caption, Vector2 position)
        {
            var go = Node(name, parent);
            Place(go.GetComponent<RectTransform>(), new Vector2(0f, 1f), position, new Vector2(50f, 30f));
            var image = Sliced(go, IdleFace);

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;

            var label = Text(go.transform, "Label", caption, 10f, TextAlignmentOptions.Center);
            var rect = label.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            return button;
        }

        static GameObject Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            return go;
        }

        /// <summary>Anchors and pivots at <paramref name="corner"/>, then places and sizes.</summary>
        static void Place(RectTransform rect, Vector2 corner, Vector2 position, Vector2 size)
        {
            rect.anchorMin = corner;
            rect.anchorMax = corner;
            rect.pivot = corner;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        static Image Sliced(GameObject go, string spritePath)
        {
            var image = go.AddComponent<Image>();
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
            image.type = Image.Type.Sliced;
            return image;
        }

        static TextMeshProUGUI Text(Transform parent, string name, string value, float size, TextAlignmentOptions align)
        {
            var go = Node(name, parent);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = size;
            text.alignment = align;
            text.raycastTarget = false;

            if (font != null)
            {
                text.font = font;
            }

            return text;
        }
    }
}
