using Dojo.Game.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Game.Editor
{
    /// <summary>
    /// Adds RESET WORLD to the in-game menu, just under SAVE WORLD.
    /// </summary>
    /// <remarks>
    /// The menu is arranged by hand in the scene rather than built, so this changes only what the
    /// new entry needs: SAVE WORLD is cloned, relabelled and given the restore icon, it takes SAVE
    /// WORLD's place, and SAVE WORLD and everything above it move up one place. It is appended to
    /// the entry point's lists rather than inserted, so every other entry keeps the index
    /// <see cref="InGameCanvasUI"/> knows it by. Safe to run again.
    /// </remarks>
    public static class ResetWorldEntryBuilder
    {
        const string SourceName = "SaveWorld";
        const string EntryName = "ResetWorld";
        const string Caption = "RESET WORLD";
        const string IconPath = "Assets/Dojo/Art/2D/UI/icon/dojo_icon_restore_flat_idle_base@3x.png";

        [MenuItem("Tools/Dojo/Add Reset World Entry")]
        public static void Add()
        {
            var entryPoint = Object.FindAnyObjectByType<InGameEntryPoint>(FindObjectsInactive.Include);

            if (entryPoint == null)
            {
                Debug.LogError("[ResetWorldEntry] No InGameEntryPoint in the open scenes. Open the Runtime scene first.");
                return;
            }

            var so = new SerializedObject(entryPoint);
            var entries = so.FindProperty("entries");
            var buttons = so.FindProperty("entryButtons");

            RectTransform source = null;
            RectTransform below = null;

            for (var i = 0; i < entries.arraySize; i++)
            {
                var rect = entries.GetArrayElementAtIndex(i).objectReferenceValue as RectTransform;

                if (rect == null)
                {
                    continue;
                }

                if (rect.name == EntryName)
                {
                    Debug.Log("[ResetWorldEntry] The menu already has " + Caption + ".", rect);
                    return;
                }

                if (rect.name == SourceName)
                {
                    source = rect;
                    below = i > 0 ? entries.GetArrayElementAtIndex(i - 1).objectReferenceValue as RectTransform : null;
                }
            }

            if (source == null)
            {
                Debug.LogError("[ResetWorldEntry] The menu has no entry called '" + SourceName + "' to put "
                    + Caption + " under.", entryPoint);
                return;
            }

            // The spacing the menu already has, so the new entry keeps its rhythm.
            var step = below != null ? source.anchoredPosition.y - below.anchoredPosition.y : 94f;
            var slot = source.anchoredPosition;

            var clone = Object.Instantiate(source.gameObject, source.parent);
            clone.name = EntryName;
            clone.transform.SetSiblingIndex(source.GetSiblingIndex());
            Undo.RegisterCreatedObjectUndo(clone, "Add Reset World Entry");

            var text = clone.transform.Find("Label/Text");
            if (text != null && text.GetComponent<TMP_Text>() != null)
            {
                text.GetComponent<TMP_Text>().text = Caption;
            }

            var icon = clone.transform.Find("Plate/Icon");
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(IconPath);
            if (icon != null && icon.GetComponent<Image>() != null && sprite != null)
            {
                icon.GetComponent<Image>().sprite = sprite;
            }

            // SAVE WORLD and everything above it go up a place; the new entry takes SAVE WORLD's.
            for (var i = 0; i < entries.arraySize; i++)
            {
                var rect = entries.GetArrayElementAtIndex(i).objectReferenceValue as RectTransform;

                if (rect != null && rect.anchoredPosition.y >= slot.y - 0.5f)
                {
                    Undo.RecordObject(rect, "Add Reset World Entry");
                    rect.anchoredPosition += new Vector2(0f, step);
                }
            }

            ((RectTransform)clone.transform).anchoredPosition = slot;

            entries.arraySize++;
            entries.GetArrayElementAtIndex(entries.arraySize - 1).objectReferenceValue = clone.transform;

            buttons.arraySize++;
            buttons.GetArrayElementAtIndex(buttons.arraySize - 1).objectReferenceValue = clone.GetComponent<Button>();

            so.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(entryPoint.gameObject.scene);

            Debug.Log("[ResetWorldEntry] Added " + Caption + " under SAVE WORLD. Save the scene to keep it.", clone);
        }
    }
}
