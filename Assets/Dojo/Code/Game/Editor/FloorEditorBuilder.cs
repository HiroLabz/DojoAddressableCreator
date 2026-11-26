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
    /// Tools ▸ Dojo ▸ Build Floor Editor: puts the floor plan editor into RuntimeMVP from the room
    /// editor's own objects, so the two look the same: the bar (SIZE · CLEAR · SAVE FLOOR PLAN ·
    /// CANCEL, with the size strip under it) as a copy of the room bar, the FLOOR PLANS panel as a
    /// copy of the Rooms panel with the FloorLayoutCard prefab made from RoomCard, a TILES | FLOOR
    /// PLANS switch inside the FLOOR tab (no rail entry of its own: Bob and Jacob, 26 Nov), ROOMS
    /// given an icon of its own, and the inventory screen wired to them. Safe to run again: what it
    /// made before is made afresh, and an older FLOORS rail entry is taken off.
    /// </summary>
    public static class FloorEditorBuilder
    {
        const string ScenePath = "Assets/Scenes/RuntimeMVP.unity";
        const string RoomCardPath = "Assets/Dojo/Prefabs/UI/Inventory/RoomCard.prefab";
        const string CardPrefabPath = "Assets/Dojo/Prefabs/UI/Inventory/FloorLayoutCard.prefab";
        const string IconFolder = "Assets/Dojo/Art/2D/UI/icon";
        const string RoomsIconPath = IconFolder + "/dojo_icon_rooms_flat_idle_base@3x.png";

        /// <summary>The floor plans' own category, kept on the screen for a rail that has an entry for it. None does today.</summary>
        public const string Category = "layouts";

        /// <summary>The FLOOR tab's category: the tiles' grid, and now the switch to the plans.</summary>
        public const string FloorTab = "floors";

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
            var roomsIcon = ImportIcon(RoomsIconPath);

            var bar = BuildBar(inGameUI.transform, roomBar);
            var panel = BuildPanel(inventory, rooms, card);
            TidyRail(rail, roomsIcon);
            Button tiles, plans;
            var floorSwitch = BuildSwitch(inventory, rooms, out tiles, out plans);

            var so = new SerializedObject(screen);
            so.FindProperty("floorLayoutsHud").objectReferenceValue = panel;
            so.FindProperty("floorLayoutsCategory").stringValue = Category;
            so.FindProperty("floorTilesCategory").stringValue = FloorTab;
            so.FindProperty("floorSwitch").objectReferenceValue = floorSwitch;
            so.FindProperty("tilesButton").objectReferenceValue = tiles;
            so.FindProperty("plansButton").objectReferenceValue = plans;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[FloorEditorBuilder] Built " + bar.name + ", the FLOOR PLANS panel (" + panel.name + "), the FLOOR tab's switch and "
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

            SetText(go.transform, "Title", "FLOOR PLAN EDITOR");
            SetText(go.transform, "Status", "Lay out a whole floor with the inventory, then save it as a floor plan.");
            SetText(go.transform, "Save/Label", "SAVE FLOOR PLAN");

            var save = go.transform.Find("Save");
            var cancel = go.transform.Find("Cancel");

            // SAVE FLOOR PLAN is a longer word than SAVE ROOM: a wider button, and the others moved along.
            var saveRect = save.GetComponent<RectTransform>();
            saveRect.sizeDelta = new Vector2(150f, saveRect.sizeDelta.y);
            saveRect.anchoredPosition = new Vector2(-134f, saveRect.anchoredPosition.y);
            var clear = CloneButton(save, go.transform, "Clear", "CLEAR", -292f, 110f);
            var size = CloneButton(save, go.transform, "Size", "SIZE", -410f, 110f);

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

        static Transform CloneButton(Transform source, Transform parent, string name, string label, float x, float width)
        {
            var go = Object.Instantiate(source.gameObject, parent);
            go.name = name;
            go.transform.SetSiblingIndex(source.GetSiblingIndex());
            var rect = go.GetComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(x, rect.anchoredPosition.y);
            rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
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

            SetText(go.transform, "Title", "FLOOR PLANS");
            SetText(go.transform, "Subtitle", "1F · 0 FLOOR PLANS");
            SetText(go.transform, "CreateLayout/Label", "+ CREATE FLOOR PLAN");
            SetText(go.transform, "Hint", "Click a floor plan, then click on the floor to lay it; what is already there stays and the pieces over it are skipped. Scroll or middle-click turns it; right-click or Esc puts it back.");
            SetText(go.transform, "Status", string.Empty);
            SetText(go.transform, "List/Empty/Label", "No floor plans yet.\n+ CREATE FLOOR PLAN makes the first.");

            // Room for the switch at the right of the subtitle's row.
            var subtitle = go.transform.Find("Subtitle") as RectTransform;
            if (subtitle != null)
            {
                subtitle.sizeDelta = new Vector2(236f, subtitle.sizeDelta.y);
            }

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

        /// <summary>
        /// The rail as it was before a FLOORS entry: that entry taken off if an earlier build made
        /// it, the entries back at their spacing, and ROOMS given its own icon.
        /// </summary>
        static void TidyRail(InventoryRail rail, Sprite roomsIcon)
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

            so.ApplyModifiedPropertiesWithoutUndo();

            var old = rail.transform.Find("Entry_FLOORS");
            if (old != null)
            {
                Object.DestroyImmediate(old.gameObject);
            }

            var roomsEntry = rail.transform.Find("Entry_ROOMS");
            var roomsIconImage = roomsEntry != null ? roomsEntry.Find("Icon") : null;
            if (roomsIconImage != null && roomsIcon != null)
            {
                roomsIconImage.GetComponent<Image>().sprite = roomsIcon;
            }

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
        }

        // ── The FLOOR tab's switch ──────────────────────────────────────────────────────────

        /// <summary>
        /// TILES | FLOOR PLANS, two small buttons at the right of the panels' second row, shown
        /// with the FLOOR tab. Made from the Rooms panel's Close button so they share its look.
        /// </summary>
        static GameObject BuildSwitch(Transform inventory, Transform rooms, out Button tiles, out Button plans)
        {
            var existing = inventory.Find("FloorSwitch");
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            var go = new GameObject("FloorSwitch", typeof(RectTransform));
            go.transform.SetParent(inventory, false);
            go.transform.SetAsLastSibling();

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(176f + 432f - 16f, -88f - 42f);
            rect.sizeDelta = new Vector2(170f, 26f);

            var close = rooms.Find("Close");
            var save = GameObject.Find("GameCanvas/InGameUI/RoomEditor/Save");
            tiles = SwitchButton(go.transform, save != null ? save.transform : close, "Tiles", "TILES", -108f, 60f);
            plans = SwitchButton(go.transform, save != null ? save.transform : close, "Plans", "FLOOR PLANS", 0f, 104f);
            go.SetActive(false);

            return go;
        }

        static Button SwitchButton(Transform parent, Transform source, string name, string label, float x, float width)
        {
            var go = Object.Instantiate(source.gameObject, parent);
            go.name = name;

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(x, 0f);
            rect.sizeDelta = new Vector2(width, 26f);

            var text = go.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.text = label;
                text.fontSize = 10f;
            }

            return go.GetComponent<Button>();
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
