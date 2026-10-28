using Dojo.Framework.UI;
using Dojo.Game.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Game.Editor
{
    /// <summary>
    /// Builds <c>DepartmentPopup.prefab</c> from the Prompt popup, adds a Department button to the
    /// Areas tab's row, and registers the popup with the root scope (ClickUp 14ze67xcgzf).
    /// </summary>
    /// <remarks>
    /// The popup is the Prompt popup's card with three rows in place of its one: a department
    /// dropdown, the box for a new department's name (the Prompt's own box, shown only while
    /// creating one), a No department checkbox and a Conference room checkbox.
    /// <see cref="DepartmentPicker"/> runs them. The dropdown and the ticks are the UI kit's art,
    /// from <see cref="KitControls"/>.
    /// Safe to run again: the popup is rebuilt from the Prompt each time, keeping its asset id, and
    /// the row only gains its button once.
    /// </remarks>
    public static class DepartmentPopupBuilder
    {
        const string PromptPath = "Assets/Dojo/Prefabs/UI/Popups/PromptPopup.prefab";
        const string PopupPath = "Assets/Dojo/Prefabs/UI/Popups/DepartmentPopup.prefab";
        const string RowPath = "Assets/Dojo/Prefabs/UI/Inventory/AreaRow.prefab";
        const string ScenePath = "Assets/Scenes/Runtime.unity";

        [MenuItem("Tools/Dojo/Build Department Popup")]
        public static void Build()
        {
            var popup = BuildPopup();
            AddRowButton();
            Register(popup);
            AssetDatabase.SaveAssets();
            Debug.Log("[Departments] built " + PopupPath + ", the area row's Department button, and registered the popup.");
        }

        // ── The popup ───────────────────────────────────────────────────────────────────────

        static MessagePopup BuildPopup()
        {
            var root = PrefabUtility.LoadPrefabContents(PromptPath);

            try
            {
                root.name = "DepartmentPopup";
                var card = root.transform.Find("Card");
                var answer = card.Find("Answer");
                var field = answer.Find("Field");
                var fieldImage = field.GetComponent<Image>();
                var fieldText = field.Find("Text Area/Text").GetComponent<TextMeshProUGUI>();
                var body = card.Find("Head/Words/Body").GetComponent<TextMeshProUGUI>();

                // The Prompt's box becomes the new department's name. It no longer gates SAVE: the
                // picker does, since SAVE also goes on for a picked department or No department.
                var message = root.GetComponent<MessagePopup>();
                var serialized = new SerializedObject(message);
                serialized.FindProperty("answer").objectReferenceValue = null;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                answer.name = "NewName";
                var placeholder = field.Find("Text Area/Placeholder").GetComponent<TextMeshProUGUI>();
                placeholder.text = "Name the new department";

                // The dropdown row, a copy of the box's row with a dropdown in the box's place.
                var departmentRow = Object.Instantiate(answer.gameObject, card).transform;
                departmentRow.name = "Department";
                departmentRow.SetSiblingIndex(answer.GetSiblingIndex());
                Object.DestroyImmediate(departmentRow.Find("Field").gameObject);
                var dropdown = KitControls.Dropdown(departmentRow, field.GetComponent<LayoutElement>(), fieldImage, fieldText, placeholder,
                    "Choose a department");

                // The checkbox rows, under the box: No department, then Conference room.
                var noRow = BuildCheckbox("NoDepartment", "No department (a pantry, a bathroom)", card, answer, fieldImage, body);
                var conferenceRow = BuildCheckbox("ConferenceRoom", "Conference room (where meetings are held)", card,
                    noRow.transform, fieldImage, body);

                // One gap under the pair, not one under each.
                noRow.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(35, 35, 0, 14);

                var save = card.Find("Footer/Buttons/Primary").GetComponent<Button>();

                var picker = root.GetComponent<DepartmentPicker>() ?? root.AddComponent<DepartmentPicker>();
                var wiring = new SerializedObject(picker);
                wiring.FindProperty("dropdown").objectReferenceValue = dropdown;
                wiring.FindProperty("newNameRow").objectReferenceValue = answer.gameObject;
                wiring.FindProperty("newName").objectReferenceValue = field.GetComponent<TMP_InputField>();
                wiring.FindProperty("noDepartment").objectReferenceValue = noRow;
                wiring.FindProperty("conferenceRoom").objectReferenceValue = conferenceRow;
                wiring.FindProperty("save").objectReferenceValue = save;
                wiring.ApplyModifiedPropertiesWithoutUndo();

                answer.gameObject.SetActive(false);

                PrefabUtility.SaveAsPrefabAsset(root, PopupPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return AssetDatabase.LoadAssetAtPath<MessagePopup>(PopupPath);
        }

        static Toggle BuildCheckbox(string name, string words, Transform card, Transform under, Image fieldImage,
            TextMeshProUGUI body)
        {
            var row = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(Toggle));
            row.transform.SetParent(card, false);
            row.transform.SetSiblingIndex(under.GetSiblingIndex() + 1);

            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(35, 35, 0, 29);
            layout.spacing = 14f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var box = new GameObject("Box", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            box.transform.SetParent(row.transform, false);
            var boxImage = box.GetComponent<Image>();
            boxImage.sprite = fieldImage.sprite;
            boxImage.type = fieldImage.type;
            boxImage.color = fieldImage.color;
            var boxLayout = box.GetComponent<LayoutElement>();
            boxLayout.minWidth = boxLayout.preferredWidth = 36f;
            boxLayout.minHeight = boxLayout.preferredHeight = 36f;

            var check = new GameObject("Check", typeof(RectTransform), typeof(Image));
            check.transform.SetParent(box.transform, false);
            var checkRect = (RectTransform)check.transform;
            checkRect.anchorMin = Vector2.zero;
            checkRect.anchorMax = Vector2.one;
            checkRect.offsetMin = new Vector2(6f, 6f);
            checkRect.offsetMax = new Vector2(-6f, -6f);
            var checkImage = check.GetComponent<Image>();
            checkImage.sprite = KitControls.Check;
            checkImage.preserveAspect = true;
            checkImage.color = KitControls.Accent;

            var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            label.transform.SetParent(row.transform, false);
            var text = label.GetComponent<TextMeshProUGUI>();
            text.font = body.font;
            text.fontSize = body.fontSize;
            text.color = body.color;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.text = words;
            label.GetComponent<LayoutElement>().flexibleWidth = 1f;

            var toggle = row.GetComponent<Toggle>();
            toggle.targetGraphic = boxImage;
            toggle.graphic = checkImage;
            toggle.isOn = false;

            return toggle;
        }

        // ── The area row's button ───────────────────────────────────────────────────────────

        static void AddRowButton()
        {
            var root = PrefabUtility.LoadPrefabContents(RowPath);

            try
            {
                var buttons = root.transform.Find("Buttons");
                var existing = buttons.Find("Department");

                if (existing == null)
                {
                    var rename = buttons.Find("Rename");
                    existing = Object.Instantiate(rename.gameObject, buttons).transform;
                    existing.name = "Department";
                    existing.SetSiblingIndex(rename.GetSiblingIndex() + 1);
                }

                // Short, so four buttons fit the row at the row's font size.
                var label = existing.GetComponentInChildren<TextMeshProUGUI>(true);
                if (label != null)
                {
                    label.text = "DEPT";
                }

                var row = new SerializedObject(root.GetComponent<AreaRow>());
                row.FindProperty("departmentButton").objectReferenceValue = existing.GetComponent<Button>();
                row.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, RowPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ── Registering it ──────────────────────────────────────────────────────────────────

        /// <remarks>
        /// The creator keeps its popups on the Runtime scene's <c>RuntimeLifetimeScope</c>, where the
        /// game keeps them on the RootLifetimeScope prefab: the scene is opened, set and saved.
        /// </remarks>
        static void Register(MessagePopup popup)
        {
            var path = ScenePath;
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetSceneByPath(path);
            var opened = !scene.isLoaded;

            if (opened)
            {
                scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path, UnityEditor.SceneManagement.OpenSceneMode.Additive);
            }

            try
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    var scope = root.GetComponentInChildren<Dojo.Game.Systems.RuntimeLifetimeScope>(true);

                    if (scope == null)
                    {
                        continue;
                    }

                    var serialized = new SerializedObject(scope);
                    serialized.FindProperty("popupPrefabs.department").objectReferenceValue = popup;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
                    UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
                    return;
                }

                Debug.LogWarning("[Departments] No RuntimeLifetimeScope in " + path + ": the popup is built but not registered.");
            }
            finally
            {
                if (opened)
                {
                    UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
                }
            }
        }
    }
}
