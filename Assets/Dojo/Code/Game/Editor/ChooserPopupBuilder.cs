using Dojo.Framework.UI;
using Dojo.Game.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Game.Editor
{
    /// <summary>
    /// Builds <c>ChooserPopup.prefab</c> from the Prompt popup and registers it with the root scope
    /// (ClickUp 14ze67xg5hb): the Prompt's card with a dropdown of the choices in place of its box,
    /// for the questions the old chooser dialog asked ("Which conference room?", "Load a world").
    /// </summary>
    /// <remarks>
    /// The same recipe as <see cref="DepartmentPopupBuilder"/>, with the dropdown alone.
    /// <see cref="ChoicePicker"/> runs it. Safe to run again: the popup is rebuilt from the Prompt
    /// each time, keeping its asset id.
    /// </remarks>
    public static class ChooserPopupBuilder
    {
        const string PromptPath = "Assets/Dojo/Prefabs/UI/Popups/PromptPopup.prefab";
        const string PopupPath = "Assets/Dojo/Prefabs/UI/Popups/ChooserPopup.prefab";
        const string ScopePath = "Assets/Dojo/Prefabs/RootLifetimeScope.prefab";

        [MenuItem("Tools/Dojo/Build Chooser Popup")]
        public static void Build()
        {
            var popup = BuildPopup();
            Register(popup);
            AssetDatabase.SaveAssets();
            Debug.Log("[Popups] built " + PopupPath + " and registered it as the root scope's chooser.");
        }

        static MessagePopup BuildPopup()
        {
            var root = PrefabUtility.LoadPrefabContents(PromptPath);

            try
            {
                root.name = "ChooserPopup";
                var card = root.transform.Find("Card");
                var answer = card.Find("Answer");
                var field = answer.Find("Field");
                var fieldImage = field.GetComponent<Image>();
                var fieldLayout = field.GetComponent<LayoutElement>();
                var fieldText = field.Find("Text Area/Text").GetComponent<TextMeshProUGUI>();
                var placeholder = field.Find("Text Area/Placeholder").GetComponent<TextMeshProUGUI>();

                // No box to type in: the picker gates the primary button instead of the box.
                var message = root.GetComponent<MessagePopup>();
                var serialized = new SerializedObject(message);
                serialized.FindProperty("answer").objectReferenceValue = null;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                // The dropdown takes the box's place in its row, styled from the box.
                var dropdown = KitControls.Dropdown(answer, fieldLayout, fieldImage, fieldText, placeholder, "Choose one");
                dropdown.transform.SetSiblingIndex(field.GetSiblingIndex());
                Object.DestroyImmediate(field.gameObject);
                answer.name = "Choice";

                var primary = card.Find("Footer/Buttons/Primary").GetComponent<Button>();

                var picker = root.GetComponent<ChoicePicker>() ?? root.AddComponent<ChoicePicker>();
                var wiring = new SerializedObject(picker);
                wiring.FindProperty("dropdown").objectReferenceValue = dropdown;
                wiring.FindProperty("primary").objectReferenceValue = primary;
                wiring.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, PopupPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return AssetDatabase.LoadAssetAtPath<MessagePopup>(PopupPath);
        }

        /// <summary>
        /// The creator keeps its popups on RuntimeMVP's RuntimeLifetimeScope, not on the game's
        /// RootLifetimeScope prefab, so the Chooser is registered there.
        /// </summary>
        static void Register(MessagePopup popup)
        {
            var path = MvpKitImporter.MvpScene;
            var already = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path);
            var opened = !(already.IsValid() && already.isLoaded);
            var scene = opened
                ? UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path, UnityEditor.SceneManagement.OpenSceneMode.Additive)
                : already;

            try
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var scope in root.GetComponentsInChildren<Dojo.Game.Systems.RuntimeLifetimeScope>(true))
                    {
                        var serialized = new SerializedObject(scope);
                        serialized.FindProperty("popupPrefabs.chooser").objectReferenceValue = popup;
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                    }
                }

                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
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
