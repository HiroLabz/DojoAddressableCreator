using Dojo.Game.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Game.Editor
{
    /// <summary>
    /// Adds the world entries to the in-game menu: RESET WORLD just under SAVE WORLD, and CLEAR
    /// WORLD just under RESET WORLD.
    /// </summary>
    /// <remarks>
    /// The menu is arranged by hand in the scene rather than built, so this changes only what a
    /// new entry needs: the entry it goes under is cloned, relabelled and given its icon, it takes
    /// that entry's place, and that entry and everything above it move up one place. It is appended
    /// to the entry point's lists rather than inserted, so every other entry keeps the index
    /// <see cref="InGameCanvasUI"/> knows it by - which is why each entry checks it lands on the
    /// index the canvas expects. Safe to run again.
    /// </remarks>
    public static class ResetWorldEntryBuilder
    {
        const string IconFolder = "Assets/Dojo/Art/2D/UI/icon/";

        [MenuItem("Tools/Dojo/Add Reset World Entry")]
        public static void AddResetWorld()
        {
            AddEntry("SaveWorld", "ResetWorld", "RESET WORLD", IconFolder + "dojo_icon_restore_flat_idle_base@3x.png", 5);
        }

        [MenuItem("Tools/Dojo/Add Clear World Entry")]
        public static void AddClearWorld()
        {
            AddEntry("ResetWorld", "ClearWorld", "CLEAR WORLD", IconFolder + "dojo_icon_delete_flat_idle_base@3x.png", 6);
        }

        /// <summary>
        /// Adds <paramref name="entryName"/> just under <paramref name="sourceName"/>, cloned from it.
        /// Nothing is added when it is there already, when the source is missing, or when it would not
        /// land at <paramref name="expectedIndex"/> - the index <see cref="InGameCanvasUI"/> answers to.
        /// </summary>
        static void AddEntry(string sourceName, string entryName, string caption, string iconPath, int expectedIndex)
        {
            var entryPoint = Object.FindAnyObjectByType<InGameEntryPoint>(FindObjectsInactive.Include);

            if (entryPoint == null)
            {
                Debug.LogError("[MenuEntry] No InGameEntryPoint in the open scenes. Open the Runtime scene first.");
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

                if (rect.name == entryName)
                {
                    Debug.Log("[MenuEntry] The menu already has " + caption + ".", rect);
                    return;
                }

                if (rect.name == sourceName)
                {
                    source = rect;
                }
            }

            if (source == null)
            {
                Debug.LogError("[MenuEntry] The menu has no entry called '" + sourceName + "' to put " + caption
                    + " under" + (sourceName == "ResetWorld" ? " - run Tools > Dojo > Add Reset World Entry first." : "."),
                    entryPoint);
                return;
            }

            if (entries.arraySize != expectedIndex)
            {
                Debug.LogError("[MenuEntry] " + caption + " would be menu entry " + entries.arraySize + ", but the game "
                    + "expects it at " + expectedIndex + ". Nothing was added.", entryPoint);
                return;
            }

            // The entry on screen just under the source, to measure the menu's spacing from.
            for (var i = 0; i < entries.arraySize; i++)
            {
                var rect = entries.GetArrayElementAtIndex(i).objectReferenceValue as RectTransform;

                if (rect != null && rect.anchoredPosition.y < source.anchoredPosition.y - 0.5f
                    && (below == null || rect.anchoredPosition.y > below.anchoredPosition.y))
                {
                    below = rect;
                }
            }

            // The spacing the menu already has, so the new entry keeps its rhythm.
            var step = below != null ? source.anchoredPosition.y - below.anchoredPosition.y : 94f;
            var slot = source.anchoredPosition;

            var clone = Object.Instantiate(source.gameObject, source.parent);
            clone.name = entryName;
            clone.transform.SetSiblingIndex(source.GetSiblingIndex());
            Undo.RegisterCreatedObjectUndo(clone, "Add " + caption + " Entry");

            var text = clone.transform.Find("Label/Text");
            if (text != null && text.GetComponent<TMP_Text>() != null)
            {
                text.GetComponent<TMP_Text>().text = caption;
            }

            var icon = clone.transform.Find("Plate/Icon");
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
            if (icon != null && icon.GetComponent<Image>() != null && sprite != null)
            {
                icon.GetComponent<Image>().sprite = sprite;
            }

            // The source and everything above it go up a place; the new entry takes the source's.
            for (var i = 0; i < entries.arraySize; i++)
            {
                var rect = entries.GetArrayElementAtIndex(i).objectReferenceValue as RectTransform;

                if (rect != null && rect.anchoredPosition.y >= slot.y - 0.5f)
                {
                    Undo.RecordObject(rect, "Add " + caption + " Entry");
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

            Debug.Log("[MenuEntry] Added " + caption + " under " + source.name + ". Save the scene to keep it.", clone);
        }
    }
}
