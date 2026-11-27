using System.IO;
using Dojo.Game.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Game.Editor
{
    /// <summary>
    /// Tools ▸ Dojo ▸ Build Floor Editor: puts the floor editor into RuntimeMVP from the room
    /// editor's own objects, so the two look the same: the floor bar (SIZE · CLEAR · SAVE LAYOUT ·
    /// CANCEL, with the size strip under it) as a copy of the room bar, the FLOORS tab as a copy of
    /// the Rooms panel with the FloorLayoutCard prefab made from RoomCard, a FLOORS entry on the
    /// rail after ROOMS with its own icon (and ROOMS given one of its own), and the inventory
    /// screen wired to them. Safe to run again: what it made before is made afresh.
    /// </summary>
    public static class FloorEditorBuilder
    {
        const string ScenePath = "Assets/Scenes/RuntimeMVP.unity";
        const string RoomCardPath = "Assets/Dojo/Prefabs/UI/Inventory/RoomCard.prefab";
        const string CardPrefabPath = "Assets/Dojo/Prefabs/UI/Inventory/FloorLayoutCard.prefab";
        const string IconFolder = "Assets/Dojo/Art/2D/UI/icon";
        const string FloorsIconPath = IconFolder + "/dojo_icon_floors_flat_idle_base@3x.png";
        const string RoomsIconPath = IconFolder + "/dojo_icon_rooms_flat_idle_base@3x.png";

        /// <summary>The rail category of the FLOORS tab. Not "floors": that is the single tiles' tab.</summary>
        public const string Category = "layouts";

        [MenuItem("Tools/Dojo/Build Floor Editor")]
        public static void Build()
        {
            var scene = EditorSceneManager.GetActiveScene();

            if (scene.path != ScenePath)
            {
                if (!EditorUtility.DisplayDialog("Build Floor Editor", "The floor editor is built into " + ScenePath
                        + ". Open it now? Unsaved changes in the open scene are kept by Unity's own prompt.", "Open", "Cancel"))
                {
                    return;
                }

                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    return;
                }

                scene = EditorSceneManager.OpenScene(ScenePath);
            }

            var inGameUI = GameObject.Find("GameCanvas/InGameUI");
            var roomBar = inGameUI != null ? inGameUI.transform.Find("RoomEditor") : null;
            var inventory = inGameUI != null ? inGameUI.transform.Find("Inventory") : null;
            var rooms = inventory != null ? inventory.Find("Rooms") : null;
            var rail = inventory != null ? inventory.GetComponentInChildren<InventoryRail>(true) : null;
            var screen = inventory != null ? inventory.GetComponent<InventoryScreen>() : null;

            if (roomBar == null || rooms == null || rail == null || screen == null)
            {
                Debug.LogError("[FloorEditorBuilder] The scene needs GameCanvas/InGameUI with RoomEditor, Inventory/Rooms, the rail and the "
                    + "InventoryScreen to copy from; one of them is missing.");
                return;
            }

            var card = EnsureCardPrefab();
            var floorsIcon = ImportIcon(FloorsIconPath);
            var roomsIcon = ImportIcon(RoomsIconPath);

            var bar = BuildBar(inGameUI.transform, roomBar);
            var panel = BuildPanel(inventory, rooms, card);
            BuildRailEntry(rail, floorsIcon, roomsIcon);

            var so = new SerializedObject(screen);
            so.FindProperty("floorLayoutsHud").objectReferenceValue = panel;
            so.FindProperty("floorLayoutsCategory").stringValue = Category;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[FloorEditorBuilder] Built " + bar.name + ", the FLOORS tab (" + panel.name + "), the rail entry and "
                + (card != null ? card.name + ".prefab" : "no card prefab") + " into " + ScenePath + ", and saved it.");
        }

        // ── The bar ─────────────────────────────────────────────────────────────────────────

        static FloorLayoutEditorBar BuildBar(Transform parent, Transform roomBar)
        {
            var existing = parent.Find("FloorEditor");
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            var go = Object.Instantiate(roomBar.gameObject, parent);
            go.name = "FloorEditor";
            go.transform.SetSiblingIndex(roomBar.GetSiblingIndex() + 1);
            go.SetActive(false);

            foreach (var old in go.GetComponents<MonoBehaviour>())
            {
                if (old is RoomEditorBar || old.GetType().Name == "RoomOpeningTool")
                {
                    Object.DestroyImmediate(old);
                }
            }

            foreach (var name in new[] { "Entrance", "Exit", "Load" })
            {
                var child = go.transform.Find(name);
                if (child != null)
                {
                    Object.DestroyImmediate(child.gameObject);
                }
            }

            SetText(go.transform, "Title", "FLOOR EDITOR");
            SetText(go.transform, "Status", "Lay out a whole floor with the inventory, then save it.");
            SetText(go.transform, "Save/Label", "SAVE LAYOUT");

            var save = go.transform.Find("Save");
            var cancel = go.transform.Find("Cancel");
            var clear = CloneButton(save, go.transform, "Clear", "CLEAR", -370f);
            var size = CloneButton(save, go.transform, "Size", "SIZE", -488f);

            // The size strip hangs under the bar's right end: COLUMNS − +  ROWS − +  8 × 6 tiles.
            var strip = new GameObject("SizeStrip", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
            strip.transform.SetParent(go.transform, false);
            var stripRect = strip.GetComponent<RectTransform>();
            stripRect.anchorMin = new Vector2(1f, 0f);
            stripRect.anchorMax = new Vector2(1f, 0f);
            stripRect.pivot = new Vector2(1f, 1f);
            stripRect.anchoredPosition = new Vector2(-16f, -8f);
            stripRect.sizeDelta = new Vector2(560f, 48f);

            var barImage = go.GetComponent<Image>();
            var stripImage = strip.GetComponent<Image>();
            if (barImage != null)
            {
                stripImage.sprite = barImage.sprite;
                stripImage.type = barImage.type;
                stripImage.color = barImage.color;
            }

            var layout = strip.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 6, 6);
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var title = go.transform.Find("Title").GetComponent<TMP_Text>();
            StripLabel(strip.transform, title, "Columns", "COLUMNS", 70f);
            var columnsDown = StripButton(strip.transform, cancel, "ColumnsDown", "−");
            var columnsUp = StripButton(strip.transform, cancel, "ColumnsUp", "+");
            StripLabel(strip.transform, title, "Rows", "ROWS", 48f);
            var rowsDown = StripButton(strip.transform, cancel, "RowsDown", "−");
            var rowsUp = StripButton(strip.transform, cancel, "RowsUp", "+");
            var sizeLabel = StripLabel(strip.transform, title, "SizeLabel", "8 × 6 tiles", 110f);
            strip.SetActive(false);

            var bar = go.AddComponent<FloorLayoutEditorBar>();
            var so = new SerializedObject(bar);
            so.FindProperty("root").objectReferenceValue = go;
            so.FindProperty("statusLabel").objectReferenceValue = go.transform.Find("Status").GetComponent<TMP_Text>();
            so.FindProperty("saveButton").objectReferenceValue = save.GetComponent<Button>();
            so.FindProperty("cancelButton").objectReferenceValue = cancel.GetComponent<Button>();
            so.FindProperty("sizeButton").objectReferenceValue = size.GetComponent<Button>();
            so.FindProperty("clearButton").objectReferenceValue = clear.GetComponent<Button>();
            so.FindProperty("sizeStrip").objectReferenceValue = strip;
            so.FindProperty("sizeLabel").objectReferenceValue = sizeLabel;
            so.FindProperty("columnsDown").objectReferenceValue = columnsDown;
            so.FindProperty("columnsUp").objectReferenceValue = columnsUp;
            so.FindProperty("rowsDown").objectReferenceValue = rowsDown;
            so.FindProperty("rowsUp").objectReferenceValue = rowsUp;
            so.ApplyModifiedPropertiesWithoutUndo();

            return bar;
        }

        static Transform CloneButton(Transform source, Transform parent, string name, string label, float x)
        {
            var go = Object.Instantiate(source.gameObject, parent);
            go.name = name;
            go.transform.SetSiblingIndex(source.GetSiblingIndex());
            var rect = go.GetComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(x, rect.anchoredPosition.y);
            SetText(go.transform, "Label", label);
            return go.transform;
        }

        static TMP_Text StripLabel(Transform strip, TMP_Text style, string name, string text, float width)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            go.transform.SetParent(strip, false);
            var label = go.GetComponent<TextMeshProUGUI>();
            label.font = style.font;
            label.fontSharedMaterial = style.fontSharedMaterial;
            label.color = style.color;
            label.fontSize = 11f;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.text = text;
            var element = go.GetComponent<LayoutElement>();
            element.preferredWidth = width;
            element.preferredHeight = 32f;
            return label;
        }

        static Button StripButton(Transform strip, Transform source, string name, string label)
        {
            var go = Object.Instantiate(source.gameObject, strip);
            go.name = name;
            SetText(go.transform, "Label", label);

            var text = go.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.fontSize = 16f;
            }

            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = 36f;
            element.preferredHeight = 32f;
            return go.GetComponent<Button>();
        }

        // ── The FLOORS tab ──────────────────────────────────────────────────────────────────

        static FloorLayoutsDisplayUI BuildPanel(Transform inventory, Transform rooms, FloorLayoutCard card)
        {
            var existing = inventory.Find("Floors");
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            var go = Object.Instantiate(rooms.gameObject, inventory);
            go.name = "Floors";
            go.transform.SetSiblingIndex(rooms.GetSiblingIndex() + 1);
            go.SetActive(false);

            var old = go.GetComponent<RoomsDisplayUI>();
            if (old != null)
            {
                Object.DestroyImmediate(old);
            }

            var create = go.transform.Find("CreateRoom");
            if (create != null)
            {
                create.name = "CreateLayout";
            }

            SetText(go.transform, "Title", "FLOORS");
            SetText(go.transform, "Subtitle", "1F · 0 LAYOUTS");
            SetText(go.transform, "CreateLayout/Label", "+ CREATE LAYOUT");
            SetText(go.transform, "Hint", "Whole floors for the pack. EDIT opens a layout in the floor editor; laying one on this floor comes next.");
            SetText(go.transform, "Status", string.Empty);
            SetText(go.transform, "List/Empty/Label", "No floor layouts yet.\n+ CREATE LAYOUT makes the first.");

            var ui = go.AddComponent<FloorLayoutsDisplayUI>();
            var so = new SerializedObject(ui);
            so.FindProperty("subtitleLabel").objectReferenceValue = go.transform.Find("Subtitle").GetComponent<TMP_Text>();
            so.FindProperty("statusLabel").objectReferenceValue = go.transform.Find("Status").GetComponent<TMP_Text>();
            so.FindProperty("listContent").objectReferenceValue = go.transform.Find("List/Viewport/Content");
            so.FindProperty("cardPrefab").objectReferenceValue = card;
            so.FindProperty("emptyState").objectReferenceValue = go.transform.Find("List/Empty").gameObject;
            so.FindProperty("createButton").objectReferenceValue = go.transform.Find("CreateLayout").GetComponent<Button>();
            so.ApplyModifiedPropertiesWithoutUndo();

            return ui;
        }

        /// <summary>FloorLayoutCard.prefab, made once from RoomCard.prefab with the card script swapped.</summary>
        static FloorLayoutCard EnsureCardPrefab()
        {
            var made = AssetDatabase.LoadAssetAtPath<GameObject>(CardPrefabPath);

            if (made == null)
            {
                if (!AssetDatabase.CopyAsset(RoomCardPath, CardPrefabPath))
                {
                    Debug.LogError("[FloorEditorBuilder] Could not copy " + RoomCardPath + " to " + CardPrefabPath + ".");
                    return null;
                }

                var root = PrefabUtility.LoadPrefabContents(CardPrefabPath);

                try
                {
                    var old = root.GetComponent<RoomCard>();
                    if (old != null)
                    {
                        Object.DestroyImmediate(old);
                    }

                    root.name = "FloorLayoutCard";
                    var card = root.AddComponent<FloorLayoutCard>();
                    var so = new SerializedObject(card);
                    so.FindProperty("button").objectReferenceValue = root.GetComponent<Button>();
                    so.FindProperty("icon").objectReferenceValue = root.transform.Find("Icon").GetComponent<Image>();
                    so.FindProperty("nameLabel").objectReferenceValue = root.transform.Find("Name").GetComponent<TextMeshProUGUI>();
                    so.FindProperty("sizeLabel").objectReferenceValue = root.transform.Find("Size").GetComponent<TextMeshProUGUI>();
                    so.FindProperty("selectedState").objectReferenceValue = root.transform.Find("Selected").gameObject;
                    so.FindProperty("deleteButton").objectReferenceValue = root.transform.Find("Delete").GetComponent<Button>();
                    so.FindProperty("promoteButton").objectReferenceValue = root.transform.Find("Promote").GetComponent<Button>();
                    so.FindProperty("editButton").objectReferenceValue = root.transform.Find("Edit").GetComponent<Button>();
                    so.ApplyModifiedPropertiesWithoutUndo();

                    PrefabUtility.SaveAsPrefabAsset(root, CardPrefabPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }

                made = AssetDatabase.LoadAssetAtPath<GameObject>(CardPrefabPath);
            }

            return made != null ? made.GetComponent<FloorLayoutCard>() : null;
        }

        // ── The rail ────────────────────────────────────────────────────────────────────────

        static void BuildRailEntry(InventoryRail rail, Sprite floorsIcon, Sprite roomsIcon)
        {
            var so = new SerializedObject(rail);
            var entries = so.FindProperty("entries");

            for (var i = entries.arraySize - 1; i >= 0; i--)
            {
                if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("category").stringValue == Category)
                {
                    entries.DeleteArrayElementAtIndex(i);
                }
            }

            var existing = rail.transform.Find("Entry_FLOORS");
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            var roomsEntry = rail.transform.Find("Entry_ROOMS");
            if (roomsEntry == null)
            {
                Debug.LogError("[FloorEditorBuilder] The rail has no Entry_ROOMS to copy the FLOORS entry from.");
                so.ApplyModifiedPropertiesWithoutUndo();
                return;
            }

            var go = Object.Instantiate(roomsEntry.gameObject, rail.transform);
            go.name = "Entry_FLOORS";
            go.transform.SetSiblingIndex(roomsEntry.GetSiblingIndex() + 1);
            SetText(go.transform, "Label", "FLOORS");

            var icon = go.transform.Find("Icon");
            if (icon != null && floorsIcon != null)
            {
                icon.GetComponent<Image>().sprite = floorsIcon;
            }

            // ROOMS has borrowed the walls icon so far; now it has its own.
            var roomsIconImage = roomsEntry.Find("Icon");
            if (roomsIconImage != null && roomsIcon != null)
            {
                roomsIconImage.GetComponent<Image>().sprite = roomsIcon;
            }

            // Thirteen entries where twelve fitted: everyone moves up a little.
            var count = rail.transform.childCount;
            var spacing = count <= 12 ? 70f : 64f;
            var height = count <= 12 ? 66f : 60f;

            for (var i = 0; i < count; i++)
            {
                var entry = rail.transform.GetChild(i).GetComponent<RectTransform>();
                entry.sizeDelta = new Vector2(96f, height);
                entry.anchoredPosition = new Vector2(0f, -spacing * i);

                var entryIcon = entry.Find("Icon") as RectTransform;
                if (entryIcon != null)
                {
                    entryIcon.anchoredPosition = new Vector2(0f, -26f + (66f - height) * 0.5f);
                }

                var label = entry.Find("Label") as RectTransform;
                if (label != null)
                {
                    label.anchoredPosition = new Vector2(0f, 15f - (66f - height) * 0.5f);
                }
            }

            var roomsIndex = -1;
            var roomsButton = roomsEntry.GetComponent<Button>();

            for (var i = 0; i < entries.arraySize; i++)
            {
                if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("button").objectReferenceValue == roomsButton)
                {
                    roomsIndex = i;
                }
            }

            var at = roomsIndex >= 0 ? roomsIndex + 1 : entries.arraySize;
            entries.InsertArrayElementAtIndex(at);
            var added = entries.GetArrayElementAtIndex(at);
            added.FindPropertyRelative("button").objectReferenceValue = go.GetComponent<Button>();
            added.FindPropertyRelative("category").stringValue = Category;
            added.FindPropertyRelative("selected").objectReferenceValue = go.transform.Find("Selected") != null ? go.transform.Find("Selected").gameObject : null;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────────────

        static Sprite ImportIcon(string path)
        {
            if (!File.Exists(path))
            {
                Debug.LogWarning("[FloorEditorBuilder] No icon at " + path + "; the entry keeps the icon it was copied with.");
                return null;
            }

            AssetDatabase.ImportAsset(path);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;

            if (importer != null && (importer.textureType != TextureImporterType.Sprite || importer.mipmapEnabled || !importer.alphaIsTransparency))
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static void SetText(Transform root, string path, string text)
        {
            var found = root.Find(path);
            var label = found != null ? found.GetComponent<TMP_Text>() : null;

            if (label != null)
            {
                label.text = text;
            }
        }
    }
}
