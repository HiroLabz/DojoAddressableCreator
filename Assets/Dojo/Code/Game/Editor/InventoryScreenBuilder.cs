using System.Collections.Generic;
using System.IO;
using Dojo.Game.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Game.Editor
{
    /// <summary>
    /// Builds the in-game inventory screen under <c>GameCanvas/InGameUI/Inventory</c>: the category
    /// rail, the hud beside it, and the two prefabs they instantiate.
    /// </summary>
    /// <remarks>
    /// A builder rather than a hand-assembled scene because this screen is sixty-odd objects whose
    /// value comes from being consistent — every rail button the same size, every card the same
    /// cell. Arranging that by hand is where the drift starts, and re-running this is how the
    /// screen gets rebuilt after the layout changes rather than patched.
    /// <para>
    /// Destructive by design: it clears the children of <c>Inventory</c> before building. The
    /// object itself is kept, so anything already pointing at it keeps pointing at it.
    /// </para>
    /// <para>
    /// Several sprites the design calls for do not exist yet — the dropdown trigger, the menu panel
    /// and its rows, the chevron. Each falls back to the nearest existing nine-slice, named in
    /// <see cref="Art"/>, so the screen is built and wired now and the art swap is a sprite field
    /// per Image later.
    /// </para>
    /// </remarks>
    public static class InventoryScreenBuilder
    {
        const string InventoryPath = "GameCanvas/InGameUI/Inventory";
        const string PrefabFolder = "Assets/Dojo/Prefabs/UI/Inventory";
        const string FontPath = "Assets/Dojo/Fonts/TMP/JetBrainsMono-Regular SDF.asset";

        /// <summary>Where the sprites live, and what stands in for the ones not drawn yet.</summary>
        static class Art
        {
            const string Root = "Assets/Dojo/Art/2D/UI/";

            public static string Icon(string name) => Root + "icon/dojo_icon_" + name + "_flat_idle_base@3x.png";

            public const string PlateIdle = Root + "btn/dojo_btn_iconplate_9slice_idle_base@3x.png";
            public const string PlateSelected = Root + "btn/dojo_btn_iconplate_9slice_selected_base@3x.png";
            public const string PlateGlow = Root + "btn/dojo_btn_iconplate_9slice_selected_glow@3x.png";

            public const string Panel = Root + "panel/dojo_panel_statcard_9slice_idle_base@3x.png";
            public const string Ghost = Root + "btn/dojo_btn_ghost_9slice_idle_base@3x.png";

            public const string FieldIdle = Root + "field/dojo_field_text_9slice_idle_base@3x.png";

            // The progress bar's own two layers, turned on their side. The kit draws its bars
            // horizontally — 240x18 sliced left and right — so stretching one down a scroller would
            // smear its rounded caps into a streak. These are the same pixels rotated a quarter
            // turn and re-sliced top and bottom, so a scrollbar is the kit's bar rather than a
            // lookalike of it.
            public const string ScrollTrack = Root + "bar/dojo_bar_scroll_track_idle_base@3x.png";
            public const string ScrollHandle = Root + "bar/dojo_bar_scroll_handle_idle_base@3x.png";

            // Stand-ins. field_dropdown_9slice_{idle,hover,open}_base and _open_glow are not drawn
            // yet; the focused text field is the same geometry lit the same way.
            public const string TriggerIdle = FieldIdle;
            public const string TriggerOpen = Root + "field/dojo_field_text_9slice_focused_base@3x.png";

            // Stand-in for panel_menu_9slice_idle_base.
            public const string MenuPanel = Panel;

            // Stand-ins for row_menuitem_9slice_{idle,selected}_base and _selected_edge. The roster
            // row is the closest thing already drawn: same height, same selected treatment.
            public const string RowIdle = Root + "row/dojo_row_roster_9slice_idle_base@3x.png";
            public const string RowSelected = Root + "row/dojo_row_roster_9slice_selected_base@3x.png";
            public const string RowEdge = Root + "row/dojo_row_roster_9slice_selected_glow@3x.png";

            public const string TileIdle = Root + "tile/dojo_tile_item_idle_base@3x.png";
            public const string TileSelected = Root + "tile/dojo_tile_item_selected_base@3x.png";
            public const string TileEdge = Root + "tile/dojo_tile_item_selected_edge@3x.png";
            public const string TileLocked = Root + "tile/dojo_tile_item_locked_base@3x.png";

            public const string DiamondSignal = Root + "deco/dojo_deco_diamond_signal_idle_base@3x.png";
            public const string DiamondCaution = Root + "deco/dojo_deco_diamond_caution_idle_base@3x.png";

            public const string Search = Root + "icon/dojo_icon_search_flat_idle_base@3x.png";

            // The four area actions, in the design's colours: build, place, erase, destroy.
            public const string ButtonPrimary = Root + "btn/dojo_btn_primary_9slice_idle_base@3x.png";
            public const string ButtonSolid = Root + "btn/dojo_btn_solid_9slice_idle_base@3x.png";
            public const string ButtonCaution = Root + "btn/dojo_btn_caution_9slice_idle_base@3x.png";
            public const string ButtonDanger = Root + "btn/dojo_btn_danger_9slice_idle_base@3x.png";

            public const string AvatarPlate = Root + "row/dojo_row_avatar_plate_idle_base@3x.png";
        }

        /// <summary>The rail, top to bottom. Caption, the category it selects, and its icon.</summary>
        /// <remarks>
        /// The people and the areas they are given first, then the pieces a room is built from. No
        /// Plants tab: nothing is in that category, and anything given it later shows under Others.
        /// </remarks>
        static readonly string[,] Rail =
        {
            { "YOU",        "you",        "you" },
            { "AGENTS",     "agents",     "agents" },
            { "AREAS",      "areas",      "areas" },
            { "ROOMS",      "rooms",      "walls" },
            { "FLOOR",      "floors",     "floor" },
            { "WALLS",      "walls",      "walls" },
            { "TABLES",     "tables",     "tables" },
            { "DRAWERS",    "drawers",    "drawers" },
            { "APPLIANCES", "appliances", "appliances" },
            { "FURNITURES", "furnitures", "furniture" },
            { "OTHERS",     "others",     "other" },
        };

        // Layout, in canvas reference units (the canvas is authored at 1920x1080).
        const float RailX = 64f;
        const float RailWidth = 96f;
        const float RailButton = 76f;
        const float RailGap = 8f;
        const float RailTop = -88f;

        /// <summary>
        /// Where the icon and the caption sit inside a rail button: the icon measured down from the
        /// button's top, the caption up from its bottom.
        /// </summary>
        /// <remarks>
        /// Taken from the button as it was arranged in the scene rather than from what this
        /// originally generated. The first pass crowded both against the edges; these are the
        /// values that were dialled in by eye, brought back here so a rebuild reproduces the
        /// arrangement instead of undoing it.
        /// </remarks>
        const float RailIconY = -32.3f;
        const float RailLabelY = 24.3f;

        const float PanelX = 176f;
        const float PanelWidth = 432f;
        const float PanelTop = -88f;
        const float PanelHeight = 848f;

        // 145 rather than 110: the pack line became a control, and a control needs a hit target.
        const float HeaderHeight = 145f;
        const float SearchHeight = 56f;
        const float Pad = 16f;

        /// <summary>
        /// Height of the 24pt panel titles. Comfortably over one line rather than level with it.
        /// </summary>
        /// <remarks>
        /// A 24pt line in this font is 31.68 units tall. A rect shorter than that does not clip the
        /// text, it truncates it — with an overflow mode of Ellipsis, TMP decides no character fits
        /// and draws none at all, so a title given 30 units vanishes completely while still
        /// reporting its text as "AGENTS". Both titles were within two units of that edge.
        /// </remarks>
        const float TitleHeight = 36f;

        static TMP_FontAsset font;

        [MenuItem("Tools/Dojo/Build Inventory Screen")]
        public static void Build()
        {
            var inventory = GameObject.Find(InventoryPath);
            if (inventory == null)
            {
                // Logged rather than shown in a dialog. A modal blocks the editor thread, which
                // means a build driven from a script or from automation hangs instead of failing,
                // and the reason is sitting behind a window nobody is watching.
                Debug.LogError("[InventoryScreenBuilder] Could not find " + InventoryPath
                    + ". Open the Game scene and run this again.");
                return;
            }

            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);

            Clear(inventory);
            Directory.CreateDirectory(PrefabFolder);

            var rowPrefab = BuildPackRowPrefab();
            var cardPrefab = BuildCardPrefab();
            var agentRowPrefab = BuildAgentRowPrefab();
            var areaRowPrefab = BuildAreaRowPrefab();
            var roomCardPrefab = BuildRoomCardPrefab();

            // The Areas tab's icon is not in the kit yet: a stand-in, under the kit's own name.
            KitStandIns.Get("icon", "areas_flat");

            var railGo = BuildRail(inventory.transform);
            var hud = BuildHud(inventory.transform, rowPrefab, cardPrefab);
            var agentsGo = BuildAgents(inventory.transform, agentRowPrefab);
            var youGo = BuildYou(inventory.transform);
            var areasGo = BuildAreas(inventory.transform, areaRowPrefab);
            var roomsGo = BuildRooms(inventory.transform, roomCardPrefab);

            var screen = inventory.AddComponent<InventoryScreen>();
            Wire(screen, "rail", railGo.GetComponent<InventoryRail>());
            Wire(screen, "hud", hud.GetComponent<InventoryHud>());
            Wire(screen, "agentsHud", agentsGo.GetComponent<InventoryAgentsHud>());
            Wire(screen, "youHud", youGo.GetComponent<ManagerDisplayUI>());
            Wire(screen, "areasHud", areasGo.GetComponent<AreasDisplayUI>());
            Wire(screen, "roomsHud", roomsGo.GetComponent<RoomsDisplayUI>());

            // The agents panel carries its own close button, and InventoryScreen holds one field
            // for the grid's. Added as a persistent listener rather than a second field, so the two
            // headers stay independent without the screen growing a field per panel.
            UnityEditor.Events.UnityEventTools.AddPersistentListener(agentsClose.onClick, screen.Close);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(youClose.onClick, screen.Close);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(areasClose.onClick, screen.Close);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(roomsClose.onClick, screen.Close);

            agentsGo.SetActive(false);
            youGo.SetActive(false);
            areasGo.SetActive(false);
            roomsGo.SetActive(false);
            Wire(screen, "closeButton", closeButton);
            Wire(screen, "root", inventory);

            // The canvas above this one carries the entry point. Found rather than serialised by
            // hand, so a rebuild never leaves the screen with no way to be opened.
            Wire(screen, "canvasUI", inventory.GetComponentInParent<InGameCanvasUI>());

            EditorUtility.SetDirty(inventory);
            UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
            AssetDatabase.SaveAssets();

            Debug.Log("[InventoryScreenBuilder] Built " + InventoryPath + ".", inventory);
        }

        // Held while building so Build() can wire them after the panels exist.
        static Button closeButton;
        static Button agentsClose;
        static Button youClose;
        static Button areasClose;
        static Button roomsClose;

        /// <summary>
        /// Adds the Rooms tab to an inventory screen already in the scene, without rebuilding it.
        /// </summary>
        /// <remarks>
        /// <see cref="Build"/> clears the whole screen first, which takes any arrangement made by
        /// hand since with it. This touches only what the Rooms tab needs: a rail entry, cloned from
        /// the Areas one and put after it, and the Rooms panel, rebuilt if an earlier run left one.
        /// Safe to run again.
        /// </remarks>
        [MenuItem("Tools/Dojo/Add Rooms Tab")]
        public static void AddRoomsTab()
        {
            var inventory = GameObject.Find(InventoryPath);
            var screen = inventory != null ? inventory.GetComponent<InventoryScreen>() : null;
            var rail = inventory != null ? inventory.GetComponentInChildren<InventoryRail>(true) : null;

            if (screen == null || rail == null)
            {
                Debug.LogError("[InventoryScreenBuilder] Could not find the inventory screen and its rail under "
                    + InventoryPath + ". Open the Runtime scene, or run Tools > Dojo > Build Inventory Screen.");
                return;
            }

            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            Directory.CreateDirectory(PrefabFolder);

            AddRailEntry(rail, "ROOMS", "rooms", "walls", "areas");

            var old = inventory.transform.Find("Rooms");
            if (old != null)
            {
                Object.DestroyImmediate(old.gameObject);
            }

            // The list of rows an earlier version of this tab used, gone now the rooms are cards.
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/RoomRow.prefab") != null)
            {
                AssetDatabase.DeleteAsset(PrefabFolder + "/RoomRow.prefab");
            }

            var roomsGo = BuildRooms(inventory.transform, BuildRoomCardPrefab());

            // Beside the Areas panel, so the hierarchy reads in rail order.
            var areas = inventory.transform.Find("Areas");
            if (areas != null)
            {
                roomsGo.transform.SetSiblingIndex(areas.GetSiblingIndex() + 1);
            }

            Wire(screen, "roomsHud", roomsGo.GetComponent<RoomsDisplayUI>());
            UnityEditor.Events.UnityEventTools.AddPersistentListener(roomsClose.onClick, screen.Close);
            roomsGo.SetActive(false);

            EditorUtility.SetDirty(inventory);
            UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
            AssetDatabase.SaveAssets();

            Debug.Log("[InventoryScreenBuilder] Added the Rooms tab to " + InventoryPath + ". Save the scene to keep it.", inventory);
        }

        /// <summary>
        /// Puts a rail entry after the one for <paramref name="after"/>, cloned from it, and moves
        /// the entries below down one place. Nothing is done when the category is already there.
        /// </summary>
        static void AddRailEntry(InventoryRail rail, string caption, string category, string icon, string after)
        {
            var so = new SerializedObject(rail);
            var array = so.FindProperty("entries");
            var at = -1;

            for (int i = 0; i < array.arraySize; i++)
            {
                var existing = array.GetArrayElementAtIndex(i).FindPropertyRelative("category").stringValue;

                if (string.Equals(existing, category, System.StringComparison.OrdinalIgnoreCase))
                {
                    Debug.Log("[InventoryScreenBuilder] The rail already has a " + caption + " entry.", rail);
                    return;
                }

                if (string.Equals(existing, after, System.StringComparison.OrdinalIgnoreCase))
                {
                    at = i;
                }
            }

            var source = at >= 0
                ? array.GetArrayElementAtIndex(at).FindPropertyRelative("button").objectReferenceValue as Button
                : null;

            if (source == null)
            {
                Debug.LogError("[InventoryScreenBuilder] The rail has no '" + after + "' entry to put "
                    + caption + " after.", rail);
                return;
            }

            // The spacing as it stands in the scene, not as Build lays it out, so a rail arranged
            // by hand keeps its rhythm.
            var step = RailButton + RailGap;
            if (array.arraySize > 1)
            {
                var first = array.GetArrayElementAtIndex(0).FindPropertyRelative("button").objectReferenceValue as Button;
                var second = array.GetArrayElementAtIndex(1).FindPropertyRelative("button").objectReferenceValue as Button;

                if (first != null && second != null)
                {
                    step = Mathf.Abs(((RectTransform)first.transform).anchoredPosition.y
                        - ((RectTransform)second.transform).anchoredPosition.y);
                }
            }

            var top = ((RectTransform)source.transform).anchoredPosition.y;

            var clone = Object.Instantiate(source.gameObject, source.transform.parent);
            clone.name = "Entry_" + caption;
            clone.transform.SetSiblingIndex(source.transform.GetSiblingIndex() + 1);

            var label = clone.transform.Find("Label");
            if (label != null && label.GetComponent<TMP_Text>() != null)
            {
                label.GetComponent<TMP_Text>().text = caption;
            }

            var iconImage = clone.transform.Find("Icon");
            if (iconImage != null && iconImage.GetComponent<Image>() != null)
            {
                iconImage.GetComponent<Image>().sprite = Load(Art.Icon(icon));
            }

            var selected = clone.transform.Find("Selected");
            if (selected != null)
            {
                selected.gameObject.SetActive(false);
            }

            array.InsertArrayElementAtIndex(at + 1);
            var element = array.GetArrayElementAtIndex(at + 1);
            element.FindPropertyRelative("button").objectReferenceValue = clone.GetComponent<Button>();
            element.FindPropertyRelative("category").stringValue = category;
            element.FindPropertyRelative("selected").objectReferenceValue = selected != null ? selected.gameObject : null;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Everything from the new entry down moves one place, and the rail grows to hold it.
            for (int i = at + 1; i < array.arraySize; i++)
            {
                var button = array.GetArrayElementAtIndex(i).FindPropertyRelative("button").objectReferenceValue as Button;
                if (button != null)
                {
                    var rect = (RectTransform)button.transform;
                    rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, top - (i - at) * step);
                }
            }

            var railRect = (RectTransform)rail.transform;
            railRect.sizeDelta = new Vector2(railRect.sizeDelta.x, railRect.sizeDelta.y + step);
        }

        static void Clear(GameObject root)
        {
            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
            }

            var existing = root.GetComponent<InventoryScreen>();
            if (existing != null)
            {
                Object.DestroyImmediate(existing);
            }
        }

        // ---------------------------------------------------------------- rail

        static GameObject BuildRail(Transform parent)
        {
            var rail = Node("Rail", parent);
            var rect = rail.GetComponent<RectTransform>();
            Anchor(rect, 0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(RailX, RailTop);
            rect.sizeDelta = new Vector2(RailWidth, Rail.GetLength(0) * (RailButton + RailGap));

            var component = rail.AddComponent<InventoryRail>();
            var entries = new List<(Button button, string category, GameObject selected)>();

            for (int i = 0; i < Rail.GetLength(0); i++)
            {
                string caption = Rail[i, 0];
                string category = Rail[i, 1];
                string icon = Rail[i, 2];

                var button = Node("Entry_" + caption, rail.transform);
                var buttonRect = button.GetComponent<RectTransform>();
                Anchor(buttonRect, 0f, 1f);
                buttonRect.pivot = new Vector2(0f, 1f);
                buttonRect.anchoredPosition = new Vector2(0f, -i * (RailButton + RailGap));
                buttonRect.sizeDelta = new Vector2(RailWidth, RailButton);

                Sliced(button, Art.PlateIdle);
                var btn = button.AddComponent<Button>();
                btn.targetGraphic = button.GetComponent<Image>();

                // The lit plate under the icon, on only for the selected entry.
                var selected = Node("Selected", button.transform);
                Stretch(selected.GetComponent<RectTransform>());
                Sliced(selected, Art.PlateSelected);
                var glow = Node("Glow", selected.transform);
                Stretch(glow.GetComponent<RectTransform>());
                Sliced(glow, Art.PlateGlow);
                selected.SetActive(false);

                var iconGo = Node("Icon", button.transform);
                var iconRect = iconGo.GetComponent<RectTransform>();
                Anchor(iconRect, 0.5f, 1f);
                iconRect.anchoredPosition = new Vector2(0f, RailIconY);
                iconRect.sizeDelta = new Vector2(28f, 28f);
                Simple(iconGo, Art.Icon(icon));

                var label = Text(button.transform, "Label", caption, 11f, TextAlignmentOptions.Center);
                var labelRect = label.rectTransform;
                Anchor(labelRect, 0.5f, 0f);
                labelRect.anchoredPosition = new Vector2(0f, RailLabelY);
                labelRect.sizeDelta = new Vector2(RailWidth, 16f);

                entries.Add((btn, category, selected));
            }

            WireRailEntries(component, entries);
            return rail;
        }

        static void WireRailEntries(
            InventoryRail rail,
            List<(Button button, string category, GameObject selected)> entries)
        {
            var so = new SerializedObject(rail);
            var array = so.FindProperty("entries");
            array.arraySize = entries.Count;

            for (int i = 0; i < entries.Count; i++)
            {
                var element = array.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("button").objectReferenceValue = entries[i].button;
                element.FindPropertyRelative("category").stringValue = entries[i].category;
                element.FindPropertyRelative("selected").objectReferenceValue = entries[i].selected;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ----------------------------------------------------------------- hud

        static GameObject BuildHud(Transform parent, InventoryPackRow rowPrefab, InventoryItemCard cardPrefab)
        {
            var hud = Node("Hud", parent);
            var rect = hud.GetComponent<RectTransform>();
            Anchor(rect, 0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(PanelX, PanelTop);
            rect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            Sliced(hud, Art.Panel);

            var component = hud.AddComponent<InventoryHud>();

            var title = BuildHeader(hud.transform, out var count, out var filter, rowPrefab);
            var search = BuildSearch(hud.transform, out var placeholder);
            var grid = BuildGrid(hud.transform, out var empty);

            Wire(component, "titleLabel", title);
            Wire(component, "countLabel", count);
            Wire(component, "packFilter", filter);
            Wire(component, "searchField", search);
            Wire(component, "searchPlaceholder", placeholder);
            Wire(component, "gridContent", grid);
            Wire(component, "cardPrefab", cardPrefab);
            Wire(component, "emptyState", empty);

            return hud;
        }

        static TextMeshProUGUI BuildHeader(
            Transform parent,
            out TextMeshProUGUI count,
            out InventoryPackFilter filter,
            InventoryPackRow rowPrefab)
        {
            var header = Node("Header", parent);
            var rect = header.GetComponent<RectTransform>();
            Anchor(rect, 0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(PanelWidth, HeaderHeight);

            var title = Text(header.transform, "Title", "WALLS", 24f, TextAlignmentOptions.Left);
            Place(title.rectTransform, 0f, 1f, new Vector2(Pad, -Pad), new Vector2(220f, TitleHeight));

            count = Text(header.transform, "Count", "0 items", 13f, TextAlignmentOptions.Right);
            Place(count.rectTransform, 1f, 1f, new Vector2(-64f, -Pad), new Vector2(120f, 24f));
            count.rectTransform.pivot = new Vector2(1f, 1f);

            var close = Node("Close", header.transform);
            Place(close.GetComponent<RectTransform>(), 1f, 1f, new Vector2(-Pad, -Pad), new Vector2(30f, 30f));
            close.GetComponent<RectTransform>().pivot = new Vector2(1f, 1f);
            Sliced(close, Art.Ghost);
            closeButton = close.AddComponent<Button>();
            closeButton.targetGraphic = close.GetComponent<Image>();
            var cross = Text(close.transform, "Glyph", "×", 20f, TextAlignmentOptions.Center);
            Stretch(cross.rectTransform);

            filter = BuildPackFilter(header.transform, rowPrefab);

            return title;
        }

        static InventoryPackFilter BuildPackFilter(Transform parent, InventoryPackRow rowPrefab)
        {
            var root = Node("PackFilter", parent);
            var rect = root.GetComponent<RectTransform>();
            Anchor(rect, 0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(Pad, -62f);
            rect.sizeDelta = new Vector2(PanelWidth - Pad * 2f, 65f);

            var component = root.AddComponent<InventoryPackFilter>();

            // Trigger. Same nine-slice geometry as the old footer chip, at 65 tall.
            var trigger = Node("Trigger", root.transform);
            Place(trigger.GetComponent<RectTransform>(), 0f, 1f, Vector2.zero, new Vector2(232f, 46f));
            var triggerImage = Sliced(trigger, Art.TriggerIdle);
            var triggerButton = trigger.AddComponent<Button>();
            triggerButton.targetGraphic = triggerImage;

            var marker = Node("Marker", trigger.transform);
            Place(marker.GetComponent<RectTransform>(), 0f, 0.5f, new Vector2(14f, 0f), new Vector2(12f, 12f));
            marker.GetComponent<RectTransform>().pivot = new Vector2(0f, 0.5f);
            Simple(marker, Art.DiamondSignal);

            var triggerLabel = Text(trigger.transform, "Label", "ALL PACKS", 13f, TextAlignmentOptions.Left);
            Place(triggerLabel.rectTransform, 0f, 0.5f, new Vector2(36f, 0f), new Vector2(150f, 20f));
            triggerLabel.rectTransform.pivot = new Vector2(0f, 0.5f);

            var chevron = Node("Chevron", trigger.transform);
            Place(chevron.GetComponent<RectTransform>(), 1f, 0.5f, new Vector2(-14f, 0f), new Vector2(14f, 14f));
            chevron.GetComponent<RectTransform>().pivot = new Vector2(1f, 0.5f);
            var chevronText = Text(chevron.transform, "Glyph", "▾", 14f, TextAlignmentOptions.Center);
            Stretch(chevronText.rectTransform);

            // The locked caption, beside the trigger. Hidden until something can count locked items.
            var locked = Node("LockedCaption", root.transform);
            Place(locked.GetComponent<RectTransform>(), 0f, 1f, new Vector2(244f, 0f), new Vector2(120f, 46f));
            var lockedMarker = Node("Marker", locked.transform);
            Place(lockedMarker.GetComponent<RectTransform>(), 0f, 0.5f, new Vector2(0f, 0f), new Vector2(12f, 12f));
            lockedMarker.GetComponent<RectTransform>().pivot = new Vector2(0f, 0.5f);
            Simple(lockedMarker, Art.DiamondCaution);
            var lockedLabel = Text(locked.transform, "Label", "1 LOCKED", 12f, TextAlignmentOptions.Left);
            Place(lockedLabel.rectTransform, 0f, 0.5f, new Vector2(20f, 0f), new Vector2(96f, 20f));
            lockedLabel.rectTransform.pivot = new Vector2(0f, 0.5f);
            locked.SetActive(false);

            // Menu. Drops below the trigger and over the search and grid, so it needs to draw last.
            var menu = Node("Menu", root.transform);
            Place(menu.GetComponent<RectTransform>(), 0f, 1f, new Vector2(0f, -50f), new Vector2(232f, 132f));
            Sliced(menu, Art.MenuPanel);
            menu.AddComponent<Canvas>().overrideSorting = true;
            menu.GetComponent<Canvas>().sortingOrder = 20;
            menu.AddComponent<GraphicRaycaster>();

            var content = Node("Content", menu.transform);
            Stretch(content.GetComponent<RectTransform>());
            var layout = content.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.spacing = 2f;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childControlWidth = true;
            menu.SetActive(false);

            Wire(component, "triggerButton", triggerButton);
            Wire(component, "triggerLabel", triggerLabel);
            Wire(component, "triggerBackground", triggerImage);
            Wire(component, "triggerIdleSprite", Load(Art.TriggerIdle));
            Wire(component, "triggerOpenSprite", Load(Art.TriggerOpen));
            Wire(component, "chevron", chevron.GetComponent<RectTransform>());
            Wire(component, "menuPanel", menu);
            Wire(component, "menuContent", content.transform);
            Wire(component, "rowPrefab", rowPrefab);
            Wire(component, "lockedCaption", locked);
            Wire(component, "lockedLabel", lockedLabel);

            return component;
        }

        static TMP_InputField BuildSearch(Transform parent, out TextMeshProUGUI placeholder)
        {
            var root = Node("Search", parent);
            var rect = root.GetComponent<RectTransform>();
            Anchor(rect, 0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(Pad, -(HeaderHeight + 4f));
            rect.sizeDelta = new Vector2(PanelWidth - Pad * 2f, SearchHeight);
            Sliced(root, Art.FieldIdle);

            var icon = Node("Icon", root.transform);
            Place(icon.GetComponent<RectTransform>(), 0f, 0.5f, new Vector2(14f, 0f), new Vector2(16f, 16f));
            icon.GetComponent<RectTransform>().pivot = new Vector2(0f, 0.5f);
            Simple(icon, Art.Search);

            var viewport = Node("TextArea", root.transform);
            var viewportRect = viewport.GetComponent<RectTransform>();
            Stretch(viewportRect);
            viewportRect.offsetMin = new Vector2(40f, 4f);
            viewportRect.offsetMax = new Vector2(-12f, -4f);
            viewport.AddComponent<RectMask2D>();

            placeholder = Text(viewport.transform, "Placeholder", "Filter walls", 13f, TextAlignmentOptions.Left);
            Stretch(placeholder.rectTransform);
            placeholder.color = new Color(1f, 1f, 1f, 0.35f);

            var text = Text(viewport.transform, "Text", string.Empty, 13f, TextAlignmentOptions.Left);
            Stretch(text.rectTransform);

            var field = root.AddComponent<TMP_InputField>();
            field.textViewport = viewportRect;
            field.textComponent = text;
            field.placeholder = placeholder;
            field.targetGraphic = root.GetComponent<Image>();

            return field;
        }

        static Transform BuildGrid(Transform parent, out GameObject empty)
        {
            float top = HeaderHeight + SearchHeight + 12f;

            var scroll = Node("Grid", parent);
            var rect = scroll.GetComponent<RectTransform>();
            Anchor(rect, 0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(Pad, -top);
            rect.sizeDelta = new Vector2(PanelWidth - Pad * 2f, PanelHeight - top - Pad);

            var viewport = Node("Viewport", scroll.transform);
            Stretch(viewport.GetComponent<RectTransform>());
            viewport.AddComponent<RectMask2D>();

            var content = Node("Content", viewport.transform);
            var contentRect = content.GetComponent<RectTransform>();
            Anchor(contentRect, 0f, 1f);
            contentRect.pivot = new Vector2(0f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(PanelWidth - Pad * 2f, 0f);

            var grid = content.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(188f, 172f);
            grid.spacing = new Vector2(12f, 12f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;

            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scrollRect = scroll.AddComponent<ScrollRect>();
            scrollRect.viewport = viewport.GetComponent<RectTransform>();
            scrollRect.content = contentRect;
            scrollRect.horizontal = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            // No room reserved: the tiles already leave a gutter wide enough, and taking more
            // would cost a whole column.
            VerticalScrollbar(scrollRect, false);

            empty = Node("Empty", scroll.transform);
            Stretch(empty.GetComponent<RectTransform>());
            var emptyLabel = Text(empty.transform, "Label", "Nothing here", 13f, TextAlignmentOptions.Center);
            Stretch(emptyLabel.rectTransform);
            emptyLabel.color = new Color(1f, 1f, 1f, 0.35f);
            empty.SetActive(false);

            return content.transform;
        }

        // -------------------------------------------------------------- agents

        /// <summary>
        /// The Agents tab. Same panel geometry as the grid, different contents: a roster list where
        /// the grid goes, and the agent card with its four area buttons filling the rest.
        /// </summary>
        static GameObject BuildAgents(Transform parent, AgentRosterRow rowPrefab)
        {
            var root = Node("Agents", parent);
            var rect = root.GetComponent<RectTransform>();
            Anchor(rect, 0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(PanelX, PanelTop);
            rect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            Sliced(root, Art.Panel);

            var hud = root.AddComponent<InventoryAgentsHud>();

            // Header. Two lines rather than one: an agent roster has a state worth saying (how many
            // are live) that a category of furniture does not.
            var title = Text(root.transform, "Title", "AGENTS", 24f, TextAlignmentOptions.Left);
            Place(title.rectTransform, 0f, 1f, new Vector2(Pad, -Pad), new Vector2(220f, TitleHeight));

            var subtitle = Text(root.transform, "Subtitle", "ROSTER · 0 LIVE", 12f, TextAlignmentOptions.Left);
            Place(subtitle.rectTransform, 0f, 1f, new Vector2(Pad, -46f), new Vector2(220f, 18f));
            subtitle.color = new Color(1f, 1f, 1f, 0.5f);

            var count = Text(root.transform, "Count", "0 of 0", 13f, TextAlignmentOptions.Right);
            Place(count.rectTransform, 1f, 1f, new Vector2(-64f, -Pad), new Vector2(120f, 24f));

            var close = Node("Close", root.transform);
            Place(close.GetComponent<RectTransform>(), 1f, 1f, new Vector2(-Pad, -Pad), new Vector2(30f, 30f));
            Sliced(close, Art.Ghost);
            agentsClose = close.AddComponent<Button>();
            agentsClose.targetGraphic = close.GetComponent<Image>();
            var cross = Text(close.transform, "Glyph", "×", 20f, TextAlignmentOptions.Center);
            Stretch(cross.rectTransform);

            var search = BuildAgentSearch(root.transform, out var placeholder);
            var roster = BuildRoster(root.transform, out var empty);
            var display = BuildAgentCard(root.transform, out var cardSubtitle, out var cells, out var draft);

            Wire(hud, "titleLabel", title);
            Wire(hud, "subtitleLabel", subtitle);
            Wire(hud, "countLabel", count);
            Wire(hud, "searchField", search);
            Wire(hud, "searchPlaceholder", placeholder);
            Wire(hud, "rosterContent", roster);
            Wire(hud, "rowPrefab", rowPrefab);
            Wire(hud, "emptyState", empty);
            Wire(hud, "display", display);
            Wire(hud, "cardSubtitleLabel", cardSubtitle);
            Wire(hud, "cardCellsLabel", cells);
            Wire(hud, "cardDraftMarker", draft);

            return root;
        }

        const float AgentHeader = 74f;
        const float RosterHeight = 268f;

        /// <summary>Height of one of the four action buttons.</summary>
        const float ActionHeight = 40f;

        /// <summary>How wide a scrollbar is, and how much room the content gives up for one.</summary>
        const float ScrollbarWidth = 8f;

        /// <summary>Clearance between the content and the bar beside it.</summary>
        const float ScrollbarGap = 6f;

        /// <summary>
        /// Everything pinned to the bottom of the agents card: the status line, the gap under it,
        /// and the row with Select area and Place Agent. What is left above it is the description's.
        /// </summary>
        const float CardFooter = 18f + 12f + ActionHeight;

        static TMP_InputField BuildAgentSearch(Transform parent, out TextMeshProUGUI placeholder)
        {
            var root = Node("Search", parent);
            var rect = root.GetComponent<RectTransform>();
            Anchor(rect, 0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(Pad, -AgentHeader);
            rect.sizeDelta = new Vector2(PanelWidth - Pad * 2f, SearchHeight);
            Sliced(root, Art.FieldIdle);

            var icon = Node("Icon", root.transform);
            Place(icon.GetComponent<RectTransform>(), 0f, 0.5f, new Vector2(14f, 0f), new Vector2(16f, 16f));
            Simple(icon, Art.Search);

            var viewport = Node("TextArea", root.transform);
            var viewportRect = viewport.GetComponent<RectTransform>();
            Stretch(viewportRect);
            viewportRect.offsetMin = new Vector2(40f, 4f);
            viewportRect.offsetMax = new Vector2(-12f, -4f);
            viewport.AddComponent<RectMask2D>();

            placeholder = Text(viewport.transform, "Placeholder", "Filter roster", 13f, TextAlignmentOptions.Left);
            Stretch(placeholder.rectTransform);
            placeholder.color = new Color(1f, 1f, 1f, 0.35f);

            var text = Text(viewport.transform, "Text", string.Empty, 13f, TextAlignmentOptions.Left);
            Stretch(text.rectTransform);

            var field = root.AddComponent<TMP_InputField>();
            field.textViewport = viewportRect;
            field.textComponent = text;
            field.placeholder = placeholder;
            field.targetGraphic = root.GetComponent<Image>();

            return field;
        }

        static Transform BuildRoster(Transform parent, out GameObject empty)
        {
            float top = AgentHeader + SearchHeight + 8f;

            var scroll = Node("Roster", parent);
            var rect = scroll.GetComponent<RectTransform>();
            Anchor(rect, 0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(Pad, -top);
            rect.sizeDelta = new Vector2(PanelWidth - Pad * 2f, RosterHeight);

            var viewport = Node("Viewport", scroll.transform);
            Stretch(viewport.GetComponent<RectTransform>());
            viewport.AddComponent<RectMask2D>();

            var content = Node("Content", viewport.transform);
            var contentRect = content.GetComponent<RectTransform>();
            Anchor(contentRect, 0f, 1f);
            contentRect.pivot = new Vector2(0f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(PanelWidth - Pad * 2f, 0f);

            var layout = content.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childControlWidth = true;

            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scrollRect = scroll.AddComponent<ScrollRect>();
            scrollRect.viewport = viewport.GetComponent<RectTransform>();
            scrollRect.content = contentRect;
            scrollRect.horizontal = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            VerticalScrollbar(scrollRect);

            empty = Node("Empty", scroll.transform);
            Stretch(empty.GetComponent<RectTransform>());
            var label = Text(empty.transform, "Label", "No agents", 13f, TextAlignmentOptions.Center);
            Stretch(label.rectTransform);
            label.color = new Color(1f, 1f, 1f, 0.35f);
            empty.SetActive(false);

            return content.transform;
        }

        /// <summary>
        /// The card below the roster. Built here but owned by <see cref="AgentDisplayUI"/>, which
        /// already holds every rule about painting, placing, erasing and clearing an area.
        /// </summary>
        static AgentDisplayUI BuildAgentCard(
            Transform parent,
            out TextMeshProUGUI subtitle,
            out TextMeshProUGUI cells,
            out GameObject draft)
        {
            float top = AgentHeader + SearchHeight + RosterHeight + 20f;

            var card = Node("Card", parent);
            var rect = card.GetComponent<RectTransform>();
            Anchor(rect, 0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(Pad, -top);
            rect.sizeDelta = new Vector2(PanelWidth - Pad * 2f, PanelHeight - top - Pad);

            var portraitGo = Node("Portrait", card.transform);
            Place(portraitGo.GetComponent<RectTransform>(), 0f, 1f, Vector2.zero, new Vector2(44f, 44f));
            var portrait = portraitGo.AddComponent<Image>();
            portrait.sprite = Load(Art.PlateIdle);
            portrait.type = Image.Type.Sliced;

            var name = Text(card.transform, "Name", "Agent", 18f, TextAlignmentOptions.Left);
            Place(name.rectTransform, 0f, 1f, new Vector2(56f, -2f), new Vector2(240f, 24f));

            subtitle = Text(card.transform, "Subtitle", string.Empty, 12f, TextAlignmentOptions.Left);
            Place(subtitle.rectTransform, 0f, 1f, new Vector2(56f, -26f), new Vector2(240f, 18f));
            subtitle.color = new Color(0.33f, 0.62f, 1f, 1f);

            // Measured from the bottom of the card up. The status line and the four buttons have
            // fixed heights; the description gets whatever is left over. Fixed offsets are what put
            // a 332-unit description on top of all of them.
            float body = -60f;
            float detail = card.GetComponent<RectTransform>().sizeDelta.y - 60f - CardFooter - 16f;

            var description = ScrollingText(card.transform, "Details", body, PanelWidth - Pad * 2f, detail);
            description.color = new Color(1f, 1f, 1f, 0.65f);

            float status = body - detail - 16f;
            float firstRow = status - 18f - 12f;

            draft = Node("Draft", card.transform);
            Place(draft.GetComponent<RectTransform>(), 0f, 1f, new Vector2(0f, status), new Vector2(90f, 18f));
            var draftMarker = Node("Marker", draft.transform);
            Place(draftMarker.GetComponent<RectTransform>(), 0f, 0.5f, Vector2.zero, new Vector2(10f, 10f));
            Simple(draftMarker, Art.DiamondSignal);
            var draftLabel = Text(draft.transform, "Label", "DRAFT", 11f, TextAlignmentOptions.Left);
            Place(draftLabel.rectTransform, 0f, 0.5f, new Vector2(16f, 0f), new Vector2(70f, 16f));
            draft.SetActive(false);

            // Where the agent walks: its area and floor, or "Whole floor".
            cells = Text(card.transform, "Cells", "Whole floor", 12f, TextAlignmentOptions.Right);
            Place(cells.rectTransform, 1f, 1f, new Vector2(0f, status), new Vector2(260f, 18f));
            cells.color = new Color(1f, 1f, 1f, 0.55f);

            // One row: which area the agent walks in, and putting them down. Painting an area is
            // the Areas tab's now.
            var select = AreaDropdown(card.transform, 0f, firstRow);
            var place = ActionButton(card.transform, "PlaceAgent", "PLACE AGENT", Art.ButtonSolid, 204f, firstRow);

            var display = card.AddComponent<AgentDisplayUI>();
            Wire(display, "card", card);
            Wire(display, "nameLabel", name);
            Wire(display, "descriptionLabel", description);
            Wire(display, "portrait", portrait);
            Wire(display, "placeAgentButton", place);
            Wire(display, "areaDropdown", select);

            return display;
        }

        /// <summary>
        /// The Select area dropdown: the kit's text field as its face, the menu panel as its list.
        /// </summary>
        /// <remarks>
        /// Unity's own dropdown rather than another hand-made one like the pack filter: what it
        /// lists - areas, with names the player typed - changes as they are painted, and the stock
        /// control already scrolls a long list, flips upward when there is no room below, and
        /// closes when clicked away from.
        /// </remarks>
        static TMP_Dropdown AreaDropdown(Transform parent, float x, float y)
        {
            var go = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources
            {
                standard = Load(Art.FieldIdle),
                background = Load(Art.ScrollTrack),
                inputField = Load(Art.FieldIdle),
                knob = Load(Art.ScrollHandle),
                checkmark = Load(Art.DiamondSignal),
                dropdown = null,
                mask = Load(Art.MenuPanel),
            });

            go.name = "SelectArea";
            SetLayer(go, LayerMask.NameToLayer("UI"));
            go.transform.SetParent(parent, false);
            Place(go.GetComponent<RectTransform>(), 0f, 1f, new Vector2(x, y), new Vector2(192f, ActionHeight));
            go.GetComponent<Image>().type = Image.Type.Sliced;

            var dropdown = go.GetComponent<TMP_Dropdown>();

            // Every word in it in the panel's font and colours.
            foreach (var text in go.GetComponentsInChildren<TMP_Text>(true))
            {
                if (font != null)
                {
                    text.font = font;
                }

                text.fontSize = 12f;
                text.color = Color.white;
            }

            var caption = dropdown.captionText.rectTransform;
            caption.offsetMin = new Vector2(12f, caption.offsetMin.y);
            caption.offsetMax = new Vector2(-28f, caption.offsetMax.y);
            dropdown.captionText.text = "SELECT AREA";

            // The arrow as the same glyph the pack filter uses, in place of an arrow sprite the kit
            // does not have.
            var arrow = go.transform.Find("Arrow");
            if (arrow != null)
            {
                Object.DestroyImmediate(arrow.GetComponent<Image>());
                var glyph = arrow.gameObject.AddComponent<TextMeshProUGUI>();
                glyph.text = "▾";
                glyph.fontSize = 14f;
                glyph.alignment = TextAlignmentOptions.Center;
                glyph.color = Color.white;
                if (font != null)
                {
                    glyph.font = font;
                }
            }

            // The list: the kit's panel, rows a little taller than the stock ones, and a lit row
            // under the pointer.
            var template = dropdown.template;
            var panel = template.GetComponent<Image>();
            panel.sprite = Load(Art.MenuPanel);
            panel.type = Image.Type.Sliced;
            template.sizeDelta = new Vector2(template.sizeDelta.x, 180f);

            var item = template.GetComponentInChildren<Toggle>(true);
            if (item != null)
            {
                var itemRect = (RectTransform)item.transform;
                itemRect.sizeDelta = new Vector2(itemRect.sizeDelta.x, 30f);

                var content = (RectTransform)itemRect.parent;
                content.sizeDelta = new Vector2(content.sizeDelta.x, 30f);

                var face = item.targetGraphic as Image;
                if (face != null)
                {
                    face.sprite = Load(Art.RowIdle);
                    face.type = Image.Type.Sliced;
                    face.color = Color.white;
                }

                var colours = item.colors;
                colours.normalColor = new Color(1f, 1f, 1f, 0.6f);
                colours.highlightedColor = new Color(0.6f, 0.82f, 1f, 1f);
                colours.selectedColor = new Color(1f, 1f, 1f, 0.6f);
                colours.pressedColor = new Color(0.45f, 0.7f, 1f, 1f);
                item.colors = colours;
            }

            return dropdown;
        }

        static void SetLayer(GameObject root, int layer)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.layer = layer;
            }
        }

        static Button ActionButton(Transform parent, string name, string caption, string sprite, float x, float y)
        {
            var go = Node(name, parent);
            Place(go.GetComponent<RectTransform>(), 0f, 1f, new Vector2(x, y), new Vector2(192f, ActionHeight));
            var image = Sliced(go, sprite);

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;

            var label = Text(go.transform, "Label", caption, 13f, TextAlignmentOptions.Center);
            Stretch(label.rectTransform);

            return button;
        }

        /// <summary>
        /// A block of text that scrolls instead of spilling out of its box.
        /// </summary>
        /// <remarks>
        /// A fixed box was enough while a description was a sentence. An agent's description is
        /// whatever its author typed, and Rhysand's is a full system prompt — 332 units of text in
        /// a 62-unit box. TextMeshPro set to <c>Overflow</c> then does exactly what it says: it
        /// paints the remainder straight down the card, across the status line and across all four
        /// buttons, which is what made them look broken rather than covered.
        /// <para>
        /// Scrolled rather than ellipsised, because the card is the only place this text is shown.
        /// Truncating would quietly drop half of an agent's brief with nothing to say it had gone.
        /// </para>
        /// <para>
        /// The text object is the scroll content itself rather than sitting inside a layout group.
        /// There is only ever one child here, so a group would add a second thing with an opinion
        /// about its height for no benefit — the fitter alone is enough.
        /// </para>
        /// </remarks>
        static TMP_Text ScrollingText(Transform parent, string name, float y, float width, float height)
        {
            var scroll = Node(name, parent);
            var rect = scroll.GetComponent<RectTransform>();
            Anchor(rect, 0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.sizeDelta = new Vector2(width, height);

            var viewport = Node("Viewport", scroll.transform);
            Stretch(viewport.GetComponent<RectTransform>());
            viewport.AddComponent<RectMask2D>();

            var text = Text(viewport.transform, "Description", string.Empty, 12f, TextAlignmentOptions.TopLeft);
            var textRect = text.rectTransform;
            textRect.anchorMin = new Vector2(0f, 1f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.pivot = new Vector2(0.5f, 1f);
            textRect.anchoredPosition = Vector2.zero;
            textRect.sizeDelta = new Vector2(0f, height);
            text.textWrappingMode = TextWrappingModes.Normal;

            var fitter = text.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scrollRect = scroll.AddComponent<ScrollRect>();
            scrollRect.viewport = viewport.GetComponent<RectTransform>();
            scrollRect.content = textRect;
            scrollRect.horizontal = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 20f;
            VerticalScrollbar(scrollRect);

            return text;
        }

        /// <summary>
        /// Hangs a scrollbar on the right of a scroller and makes room for it.
        /// </summary>
        /// <remarks>
        /// <c>AutoHide</c> rather than <c>AutoHideAndExpandViewport</c>. Expanding the viewport
        /// hands the layout back to Unity, which would undo the insets set here and re-measure the
        /// cards that were just laid out by hand; hiding alone leaves the geometry exactly where it
        /// was put and simply stops drawing the bar when everything fits.
        /// <para>
        /// The viewport gives up the bar's width whether or not the bar is showing. A viewport that
        /// changed width with the content would reflow every row underneath it the moment one more
        /// line arrived, and a list that reflows as it fills is worse than one that is eight units
        /// narrower than it could be.
        /// </para>
        /// </remarks>
        /// <param name="reserveRoom">
        /// Whether the viewport gives up width for the bar. False where the content is already
        /// sized to the viewport and cannot lose any: the item grid fits exactly two 188-wide tiles
        /// in 400, so taking fourteen units off it would leave room for one. There the bar sits in
        /// the twelve-unit gutter the tiles already leave, which is what it is for.
        /// </param>
        static Scrollbar VerticalScrollbar(ScrollRect scroll, bool reserveRoom = true)
        {
            var bar = Node("Scrollbar", scroll.transform);
            var rect = bar.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(ScrollbarWidth, 0f);
            rect.anchoredPosition = Vector2.zero;

            Sliced(bar, Art.ScrollTrack);

            var area = Node("Sliding Area", bar.transform);
            Stretch(area.GetComponent<RectTransform>());

            var handle = Node("Handle", area.transform);
            var handleRect = handle.GetComponent<RectTransform>();
            handleRect.sizeDelta = Vector2.zero;
            handleRect.anchoredPosition = Vector2.zero;
            var handleImage = Sliced(handle, Art.ScrollHandle);

            var scrollbar = bar.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleImage;

            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            if (reserveRoom && scroll.viewport != null)
            {
                var viewport = scroll.viewport;
                viewport.offsetMax = new Vector2(-(ScrollbarWidth + ScrollbarGap), viewport.offsetMax.y);
            }

            return scrollbar;
        }

        // ----------------------------------------------------------------- you

        /// <summary>
        /// The You tab: the manager's card and the same four area controls the agents card carries.
        /// </summary>
        /// <remarks>
        /// One entry rather than a roster, so there is no list above it — the whole tab is the card,
        /// handed to <see cref="ManagerDisplayUI"/>, which already owns placing the manager and
        /// painting the patch of floor he works.
        /// </remarks>
        static GameObject BuildYou(Transform parent)
        {
            var root = Node("You", parent);
            var rect = root.GetComponent<RectTransform>();
            Anchor(rect, 0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(PanelX, PanelTop);
            rect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            Sliced(root, Art.Panel);

            var title = Text(root.transform, "Title", "YOU", 24f, TextAlignmentOptions.Left);
            Place(title.rectTransform, 0f, 1f, new Vector2(Pad, -Pad), new Vector2(220f, TitleHeight));

            var subtitle = Text(root.transform, "Subtitle", "MANAGER", 12f, TextAlignmentOptions.Left);
            Place(subtitle.rectTransform, 0f, 1f, new Vector2(Pad, -50f), new Vector2(220f, 18f));
            subtitle.color = new Color(1f, 1f, 1f, 0.5f);

            var close = Node("Close", root.transform);
            Place(close.GetComponent<RectTransform>(), 1f, 1f, new Vector2(-Pad, -Pad), new Vector2(30f, 30f));
            Sliced(close, Art.Ghost);
            youClose = close.AddComponent<Button>();
            youClose.targetGraphic = close.GetComponent<Image>();
            var cross = Text(close.transform, "Glyph", "×", 20f, TextAlignmentOptions.Center);
            Stretch(cross.rectTransform);

            // The component sits on the panel, not on the card it drives, so showing the tab is a
            // matter of switching one object on — the same shape the other two tabs have.
            var display = root.AddComponent<ManagerDisplayUI>();
            BuildManagerCard(root.transform, display);

            return root;
        }

        static void BuildManagerCard(Transform parent, ManagerDisplayUI display)
        {
            const float top = 86f;

            var card = Node("Card", parent);
            var rect = card.GetComponent<RectTransform>();
            Anchor(rect, 0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(Pad, -top);
            rect.sizeDelta = new Vector2(PanelWidth - Pad * 2f, PanelHeight - top - Pad);

            var portraitGo = Node("Portrait", card.transform);
            Place(portraitGo.GetComponent<RectTransform>(), 0f, 1f, Vector2.zero, new Vector2(64f, 64f));
            var portrait = portraitGo.AddComponent<Image>();
            portrait.sprite = Load(Art.PlateIdle);
            portrait.type = Image.Type.Sliced;

            var name = Text(card.transform, "Name", "Manager", 18f, TextAlignmentOptions.Left);
            Place(name.rectTransform, 0f, 1f, new Vector2(76f, -8f), new Vector2(240f, 24f));

            var role = Text(card.transform, "Role", "MANAGEMENT", 12f, TextAlignmentOptions.Left);
            Place(role.rectTransform, 0f, 1f, new Vector2(76f, -34f), new Vector2(240f, 18f));
            role.color = new Color(0.33f, 0.62f, 1f, 1f);

            // The same bottom-up measurement as the agents card, and for the same reason: this
            // description is written by whoever set the manager up and is under nobody's control.
            const float body = -84f;
            float detail = card.GetComponent<RectTransform>().sizeDelta.y - 84f - ActionHeight - 16f;

            var description = ScrollingText(card.transform, "Details", body, PanelWidth - Pad * 2f, detail);
            description.color = new Color(1f, 1f, 1f, 0.65f);

            float firstRow = body - detail - 16f;

            // The same row as the agents card - the area, and putting him down - so the two tabs do
            // not teach different habits for the same two things.
            var select = AreaDropdown(card.transform, 0f, firstRow);
            var place = ActionButton(card.transform, "PlaceManager", "PLACE AGENT", Art.ButtonPrimary, 204f, firstRow);

            Wire(display, "card", card);
            Wire(display, "nameLabel", name);
            Wire(display, "descriptionLabel", description);
            Wire(display, "portrait", portrait);
            Wire(display, "placeManagerButton", place);
            Wire(display, "areaDropdown", select);
        }

        // --------------------------------------------------------------- areas

        /// <summary>
        /// The Areas tab: the floor's block areas, and the one place an area is painted.
        /// </summary>
        /// <remarks>
        /// The same panel geometry as the others. A wide + New area button under the header, a
        /// line saying how to paint while the brush is up, and the list of the floor's areas
        /// filling the rest, each row with Repaint, Rename and Delete.
        /// </remarks>
        static GameObject BuildAreas(Transform parent, AreaRow rowPrefab)
        {
            var root = Node("Areas", parent);
            var rect = root.GetComponent<RectTransform>();
            Anchor(rect, 0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(PanelX, PanelTop);
            rect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            Sliced(root, Art.Panel);

            var title = Text(root.transform, "Title", "AREAS", 24f, TextAlignmentOptions.Left);
            Place(title.rectTransform, 0f, 1f, new Vector2(Pad, -Pad), new Vector2(220f, TitleHeight));

            var subtitle = Text(root.transform, "Subtitle", "1F · 0 AREAS", 12f, TextAlignmentOptions.Left);
            Place(subtitle.rectTransform, 0f, 1f, new Vector2(Pad, -46f), new Vector2(260f, 18f));
            subtitle.color = new Color(1f, 1f, 1f, 0.5f);

            var close = Node("Close", root.transform);
            Place(close.GetComponent<RectTransform>(), 1f, 1f, new Vector2(-Pad, -Pad), new Vector2(30f, 30f));
            Sliced(close, Art.Ghost);
            areasClose = close.AddComponent<Button>();
            areasClose.targetGraphic = close.GetComponent<Image>();
            var cross = Text(close.transform, "Glyph", "×", 20f, TextAlignmentOptions.Center);
            Stretch(cross.rectTransform);

            const float newTop = 78f;
            const float newHeight = 44f;

            var newArea = Node("NewArea", root.transform);
            Place(newArea.GetComponent<RectTransform>(), 0f, 1f, new Vector2(Pad, -newTop),
                new Vector2(PanelWidth - Pad * 2f, newHeight));
            var newFace = Sliced(newArea, Art.ButtonPrimary);
            var newButton = newArea.AddComponent<Button>();
            newButton.targetGraphic = newFace;
            var newLabel = Text(newArea.transform, "Label", "+ NEW AREA", 13f, TextAlignmentOptions.Center);
            Stretch(newLabel.rectTransform);

            const float hintTop = newTop + newHeight + 8f;
            const float hintHeight = 34f;

            var hint = Text(root.transform, "Hint",
                "Drag to paint on the floor you're viewing. Ctrl-drag rubs out, the wheel sizes the brush. "
                + "Right-click to finish, Esc to cancel.", 11f, TextAlignmentOptions.TopLeft);
            Place(hint.rectTransform, 0f, 1f, new Vector2(Pad, -hintTop), new Vector2(PanelWidth - Pad * 2f, hintHeight));
            hint.textWrappingMode = TextWrappingModes.Normal;
            hint.color = new Color(0.33f, 0.62f, 1f, 1f);
            hint.gameObject.SetActive(false);

            const float listTop = hintTop + hintHeight + 8f;

            var scroll = Node("List", root.transform);
            var scrollRect = scroll.GetComponent<RectTransform>();
            Anchor(scrollRect, 0f, 1f);
            scrollRect.pivot = new Vector2(0f, 1f);
            scrollRect.anchoredPosition = new Vector2(Pad, -listTop);
            scrollRect.sizeDelta = new Vector2(PanelWidth - Pad * 2f, PanelHeight - listTop - Pad);

            var viewport = Node("Viewport", scroll.transform);
            Stretch(viewport.GetComponent<RectTransform>());
            viewport.AddComponent<RectMask2D>();

            var content = Node("Content", viewport.transform);
            var contentRect = content.GetComponent<RectTransform>();
            Anchor(contentRect, 0f, 1f);
            contentRect.pivot = new Vector2(0f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(PanelWidth - Pad * 2f, 0f);

            var layout = content.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childControlWidth = true;
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroller = scroll.AddComponent<ScrollRect>();
            scroller.viewport = viewport.GetComponent<RectTransform>();
            scroller.content = contentRect;
            scroller.horizontal = false;
            scroller.movementType = ScrollRect.MovementType.Clamped;
            VerticalScrollbar(scroller);

            var empty = Node("Empty", scroll.transform);
            Stretch(empty.GetComponent<RectTransform>());
            var emptyLabel = Text(empty.transform, "Label",
                "No areas on this floor yet.\nPress + NEW AREA and paint one.", 13f, TextAlignmentOptions.Center);
            Stretch(emptyLabel.rectTransform);
            emptyLabel.textWrappingMode = TextWrappingModes.Normal;
            emptyLabel.color = new Color(1f, 1f, 1f, 0.35f);
            empty.SetActive(false);

            var display = root.AddComponent<AreasDisplayUI>();
            Wire(display, "subtitleLabel", subtitle);
            Wire(display, "newAreaButton", newButton);
            Wire(display, "newAreaLabel", newLabel);
            Wire(display, "hintLabel", hint);
            Wire(display, "listContent", content.transform);
            Wire(display, "rowPrefab", rowPrefab);
            Wire(display, "emptyState", empty);

            return root;
        }

        // --------------------------------------------------------------- rooms

        /// <summary>
        /// The Rooms tab: every room the loaded packs offer, as a grid of cards like the floors and
        /// walls.
        /// </summary>
        /// <remarks>
        /// The Areas panel's frame around the item grid's cards: the same cell size and two
        /// columns, so a room reads as one more thing to put down. A line under the header says how
        /// to place one, and a status line says why a card could not be picked up.
        /// </remarks>
        static GameObject BuildRooms(Transform parent, RoomCard cardPrefab)
        {
            var root = Node("Rooms", parent);
            var rect = root.GetComponent<RectTransform>();
            Anchor(rect, 0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(PanelX, PanelTop);
            rect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            Sliced(root, Art.Panel);

            var title = Text(root.transform, "Title", "ROOMS", 24f, TextAlignmentOptions.Left);
            Place(title.rectTransform, 0f, 1f, new Vector2(Pad, -Pad), new Vector2(220f, TitleHeight));

            var subtitle = Text(root.transform, "Subtitle", "1F · 0 ROOMS", 12f, TextAlignmentOptions.Left);
            Place(subtitle.rectTransform, 0f, 1f, new Vector2(Pad, -46f), new Vector2(260f, 18f));
            subtitle.color = new Color(1f, 1f, 1f, 0.5f);

            var close = Node("Close", root.transform);
            Place(close.GetComponent<RectTransform>(), 1f, 1f, new Vector2(-Pad, -Pad), new Vector2(30f, 30f));
            Sliced(close, Art.Ghost);
            roomsClose = close.AddComponent<Button>();
            roomsClose.targetGraphic = close.GetComponent<Image>();
            var cross = Text(close.transform, "Glyph", "×", 20f, TextAlignmentOptions.Center);
            Stretch(cross.rectTransform);

            const float hintTop = 78f;
            const float hintHeight = 34f;

            var hint = Text(root.transform, "Hint",
                "Click a room, then click on the floor to put it down. Scroll or middle-click turns it; "
                + "right-click or Esc puts it back.",
                11f, TextAlignmentOptions.TopLeft);
            Place(hint.rectTransform, 0f, 1f, new Vector2(Pad, -hintTop), new Vector2(PanelWidth - Pad * 2f, hintHeight));
            hint.textWrappingMode = TextWrappingModes.Normal;
            hint.color = new Color(1f, 1f, 1f, 0.45f);

            const float statusTop = hintTop + hintHeight + 4f;
            const float statusHeight = 34f;

            var status = Text(root.transform, "Status", string.Empty, 11f, TextAlignmentOptions.TopLeft);
            Place(status.rectTransform, 0f, 1f, new Vector2(Pad, -statusTop), new Vector2(PanelWidth - Pad * 2f, statusHeight));
            status.textWrappingMode = TextWrappingModes.Normal;
            status.gameObject.SetActive(false);

            const float listTop = statusTop + statusHeight + 8f;

            var scroll = Node("List", root.transform);
            var scrollRect = scroll.GetComponent<RectTransform>();
            Anchor(scrollRect, 0f, 1f);
            scrollRect.pivot = new Vector2(0f, 1f);
            scrollRect.anchoredPosition = new Vector2(Pad, -listTop);
            scrollRect.sizeDelta = new Vector2(PanelWidth - Pad * 2f, PanelHeight - listTop - Pad);

            var viewport = Node("Viewport", scroll.transform);
            Stretch(viewport.GetComponent<RectTransform>());
            viewport.AddComponent<RectMask2D>();

            var content = Node("Content", viewport.transform);
            var contentRect = content.GetComponent<RectTransform>();
            Anchor(contentRect, 0f, 1f);
            contentRect.pivot = new Vector2(0f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(PanelWidth - Pad * 2f, 0f);

            // The item grid's cells, so a room card is the same size as a floor or wall card.
            var cells = content.AddComponent<GridLayoutGroup>();
            cells.cellSize = new Vector2(188f, 172f);
            cells.spacing = new Vector2(12f, 12f);
            cells.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            cells.constraintCount = 2;
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroller = scroll.AddComponent<ScrollRect>();
            scroller.viewport = viewport.GetComponent<RectTransform>();
            scroller.content = contentRect;
            scroller.horizontal = false;
            scroller.movementType = ScrollRect.MovementType.Clamped;
            VerticalScrollbar(scroller);

            var empty = Node("Empty", scroll.transform);
            Stretch(empty.GetComponent<RectTransform>());
            var emptyLabel = Text(empty.transform, "Label",
                "No rooms in the loaded packs.\nAdd a Rooms/rooms index to a pack.", 13f, TextAlignmentOptions.Center);
            Stretch(emptyLabel.rectTransform);
            emptyLabel.textWrappingMode = TextWrappingModes.Normal;
            emptyLabel.color = new Color(1f, 1f, 1f, 0.35f);
            empty.SetActive(false);

            var display = root.AddComponent<RoomsDisplayUI>();
            Wire(display, "subtitleLabel", subtitle);
            Wire(display, "statusLabel", status);
            Wire(display, "listContent", content.transform);
            Wire(display, "cardPrefab", cardPrefab);
            Wire(display, "emptyState", empty);

            return root;
        }

        // ------------------------------------------------------------- prefabs

        static InventoryItemCard BuildCardPrefab()
        {
            var root = Node("InventoryItemCard", null);
            var rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(188f, 172f);
            Sliced(root, Art.TileIdle);

            var icon = Node("Icon", root.transform);
            Place(icon.GetComponent<RectTransform>(), 0.5f, 1f, new Vector2(0f, -12f), new Vector2(120f, 104f));
            icon.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 1f);
            var iconImage = icon.AddComponent<Image>();
            iconImage.preserveAspect = true;

            var name = Text(root.transform, "Name", "Item", 13f, TextAlignmentOptions.Left);
            Place(name.rectTransform, 0f, 0f, new Vector2(12f, 12f), new Vector2(112f, 20f));
            name.rectTransform.pivot = new Vector2(0f, 0f);

            var badge = Node("SizeBadge", root.transform);
            Place(badge.GetComponent<RectTransform>(), 1f, 0f, new Vector2(-12f, 12f), new Vector2(42f, 20f));
            badge.GetComponent<RectTransform>().pivot = new Vector2(1f, 0f);
            var size = Text(badge.transform, "Label", "1×3", 12f, TextAlignmentOptions.Right);
            Stretch(size.rectTransform);
            size.color = new Color(1f, 1f, 1f, 0.55f);

            var selected = Node("Selected", root.transform);
            Stretch(selected.GetComponent<RectTransform>());
            Sliced(selected, Art.TileSelected);
            var edge = Node("Edge", selected.transform);
            Stretch(edge.GetComponent<RectTransform>());
            Sliced(edge, Art.TileEdge);
            selected.SetActive(false);

            var locked = Node("Locked", root.transform);
            Stretch(locked.GetComponent<RectTransform>());
            Sliced(locked, Art.TileLocked);
            locked.SetActive(false);

            var card = root.AddComponent<InventoryItemCard>();
            Wire(card, "nameLabel", name);
            Wire(card, "sizeLabel", size);
            Wire(card, "sizeBadge", badge);
            Wire(card, "selectedState", selected);
            Wire(card, "lockedState", locked);

            // The tile beside the card: what actually carries the piece into the world.
            var tile = root.AddComponent<ItemRenderer3D>();
            Wire(tile, "icon", iconImage);
            Wire(tile, "background", root.GetComponent<Image>());

            return SavePrefab(root, "InventoryItemCard").GetComponent<InventoryItemCard>();
        }

        static InventoryPackRow BuildPackRowPrefab()
        {
            var root = Node("InventoryPackRow", null);
            var rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(220f, 36f);
            var background = Sliced(root, Art.RowIdle);

            var selected = Node("Selected", root.transform);
            Stretch(selected.GetComponent<RectTransform>());
            Sliced(selected, Art.RowSelected);
            var edge = Node("Edge", selected.transform);
            var edgeRect = edge.GetComponent<RectTransform>();
            Anchor(edgeRect, 0f, 0.5f);
            edgeRect.pivot = new Vector2(0f, 0.5f);
            edgeRect.anchoredPosition = Vector2.zero;
            edgeRect.sizeDelta = new Vector2(3f, 36f);
            Sliced(edge, Art.RowEdge);
            selected.SetActive(false);

            var marker = Node("Marker", root.transform);
            Place(marker.GetComponent<RectTransform>(), 0f, 0.5f, new Vector2(12f, 0f), new Vector2(11f, 11f));
            marker.GetComponent<RectTransform>().pivot = new Vector2(0f, 0.5f);
            var markerImage = Simple(marker, Art.DiamondSignal);

            var label = Text(root.transform, "Label", "DEFAULT PACK", 12f, TextAlignmentOptions.Left);
            Place(label.rectTransform, 0f, 0.5f, new Vector2(32f, 0f), new Vector2(140f, 18f));
            label.rectTransform.pivot = new Vector2(0f, 0.5f);

            var count = Text(root.transform, "Count", "0", 12f, TextAlignmentOptions.Right);
            Place(count.rectTransform, 1f, 0.5f, new Vector2(-12f, 0f), new Vector2(40f, 18f));
            count.rectTransform.pivot = new Vector2(1f, 0.5f);
            count.color = new Color(1f, 1f, 1f, 0.55f);

            var button = root.AddComponent<Button>();
            button.targetGraphic = background;

            var row = root.AddComponent<InventoryPackRow>();
            Wire(row, "button", button);
            Wire(row, "label", label);
            Wire(row, "countLabel", count);
            Wire(row, "selectedState", selected);
            Wire(row, "marker", markerImage);

            return SavePrefab(root, "InventoryPackRow").GetComponent<InventoryPackRow>();
        }

        /// <summary>
        /// One roster row: avatar, name, what the agent does, and a draft flag. Wide rather than
        /// square, because what tells two agents apart is their names and not their pictures.
        /// </summary>
        static AgentRosterRow BuildAgentRowPrefab()
        {
            var root = Node("AgentRosterRow", null);
            var rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(400f, 64f);
            var background = Sliced(root, Art.RowIdle);

            var element = root.AddComponent<LayoutElement>();
            element.minHeight = 64f;
            element.preferredHeight = 64f;

            var selected = Node("Selected", root.transform);
            Stretch(selected.GetComponent<RectTransform>());
            Sliced(selected, Art.RowSelected);
            var edge = Node("Edge", selected.transform);
            Stretch(edge.GetComponent<RectTransform>());
            Sliced(edge, Art.RowEdge);
            selected.SetActive(false);

            var avatarGo = Node("Avatar", root.transform);
            Place(avatarGo.GetComponent<RectTransform>(), 0f, 0.5f, new Vector2(12f, 0f), new Vector2(40f, 40f));
            var avatar = avatarGo.AddComponent<Image>();
            avatar.sprite = Load(Art.AvatarPlate);
            avatar.type = Image.Type.Sliced;

            var initials = Text(avatarGo.transform, "Initials", "--", 14f, TextAlignmentOptions.Center);
            Stretch(initials.rectTransform);

            var name = Text(root.transform, "Name", "Agent", 13f, TextAlignmentOptions.Left);
            Place(name.rectTransform, 0f, 1f, new Vector2(62f, -14f), new Vector2(280f, 20f));

            var subtitle = Text(root.transform, "Subtitle", string.Empty, 10f, TextAlignmentOptions.Left);
            Place(subtitle.rectTransform, 0f, 1f, new Vector2(62f, -36f), new Vector2(280f, 14f));
            subtitle.color = new Color(1f, 1f, 1f, 0.45f);

            var draft = Node("Draft", root.transform);
            Place(draft.GetComponent<RectTransform>(), 1f, 0.5f, new Vector2(-14f, 0f), new Vector2(12f, 12f));
            Simple(draft, Art.DiamondCaution);
            draft.SetActive(false);

            var button = root.AddComponent<Button>();
            button.targetGraphic = background;

            var row = root.AddComponent<AgentRosterRow>();
            Wire(row, "button", button);
            Wire(row, "avatar", avatar);
            Wire(row, "initials", initials);
            Wire(row, "nameLabel", name);
            Wire(row, "subtitleLabel", subtitle);
            Wire(row, "selectedState", selected);
            Wire(row, "draftMarker", draft);

            return SavePrefab(root, "AgentRosterRow").GetComponent<AgentRosterRow>();
        }

        /// <summary>
        /// One block area in the Areas tab: its name and who uses it on the left, and Repaint,
        /// Rename and Delete on the right.
        /// </summary>
        static AreaRow BuildAreaRowPrefab()
        {
            var root = Node("AreaRow", null);
            var rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(400f, 64f);
            Sliced(root, Art.RowIdle);

            var element = root.AddComponent<LayoutElement>();
            element.minHeight = 64f;
            element.preferredHeight = 64f;

            var name = Text(root.transform, "Name", "Area", 13f, TextAlignmentOptions.Left);
            Place(name.rectTransform, 0f, 1f, new Vector2(14f, -12f), new Vector2(160f, 20f));
            name.overflowMode = TextOverflowModes.Ellipsis;

            var detail = Text(root.transform, "Detail", "NOT ASSIGNED", 10f, TextAlignmentOptions.Left);
            Place(detail.rectTransform, 0f, 1f, new Vector2(14f, -36f), new Vector2(160f, 14f));
            detail.overflowMode = TextOverflowModes.Ellipsis;
            detail.color = new Color(1f, 1f, 1f, 0.45f);

            var repaint = RowButton(root.transform, "Repaint", "REPAINT", Art.Ghost, -158f);
            var rename = RowButton(root.transform, "Rename", "RENAME", Art.Ghost, -84f);
            var delete = RowButton(root.transform, "Delete", "DELETE", Art.ButtonDanger, -10f);

            var row = root.AddComponent<AreaRow>();
            Wire(row, "nameLabel", name);
            Wire(row, "detailLabel", detail);
            Wire(row, "repaintButton", repaint);
            Wire(row, "renameButton", rename);
            Wire(row, "deleteButton", delete);

            return SavePrefab(root, "AreaRow").GetComponent<AreaRow>();
        }

        /// <summary>
        /// One room in the Rooms tab, laid out as the item card is: the thumbnail filling the top,
        /// the name bottom left and the size bottom right, with the item card's selected state.
        /// </summary>
        static RoomCard BuildRoomCardPrefab()
        {
            var root = Node("RoomCard", null);
            var rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(188f, 172f);
            var face = Sliced(root, Art.TileIdle);

            var icon = Node("Icon", root.transform);
            Place(icon.GetComponent<RectTransform>(), 0.5f, 1f, new Vector2(0f, -12f), new Vector2(160f, 116f));
            icon.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 1f);
            var iconImage = icon.AddComponent<Image>();
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;

            var name = Text(root.transform, "Name", "Room", 13f, TextAlignmentOptions.Left);
            Place(name.rectTransform, 0f, 0f, new Vector2(12f, 12f), new Vector2(120f, 20f));
            name.rectTransform.pivot = new Vector2(0f, 0f);
            name.overflowMode = TextOverflowModes.Ellipsis;

            var size = Text(root.transform, "Size", "13×12m", 12f, TextAlignmentOptions.Right);
            Place(size.rectTransform, 1f, 0f, new Vector2(-12f, 12f), new Vector2(52f, 20f));
            size.rectTransform.pivot = new Vector2(1f, 0f);
            size.color = new Color(1f, 1f, 1f, 0.55f);

            var selected = Node("Selected", root.transform);
            Stretch(selected.GetComponent<RectTransform>());
            Sliced(selected, Art.TileSelected).raycastTarget = false;
            var edge = Node("Edge", selected.transform);
            Stretch(edge.GetComponent<RectTransform>());
            Sliced(edge, Art.TileEdge).raycastTarget = false;
            selected.SetActive(false);

            var button = root.AddComponent<Button>();
            button.targetGraphic = face;

            var card = root.AddComponent<RoomCard>();
            Wire(card, "button", button);
            Wire(card, "icon", iconImage);
            Wire(card, "nameLabel", name);
            Wire(card, "sizeLabel", size);
            Wire(card, "selectedState", selected);

            return SavePrefab(root, "RoomCard").GetComponent<RoomCard>();
        }

        /// <summary>A small button on the right of a row, <paramref name="x"/> in from its right edge.</summary>
        static Button RowButton(Transform parent, string name, string caption, string sprite, float x)
        {
            var go = Node(name, parent);
            Place(go.GetComponent<RectTransform>(), 1f, 0.5f, new Vector2(x, 0f), new Vector2(68f, 28f));
            var image = Sliced(go, sprite);

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;

            var label = Text(go.transform, "Label", caption, 10f, TextAlignmentOptions.Center);
            Stretch(label.rectTransform);

            return button;
        }

        static GameObject SavePrefab(GameObject root, string name)
        {
            string path = PrefabFolder + "/" + name + ".prefab";
            var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return saved;
        }

        // ------------------------------------------------------------- helpers

        static GameObject Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");

            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }

            return go;
        }

        static void Anchor(RectTransform rect, float x, float y)
        {
            rect.anchorMin = new Vector2(x, y);
            rect.anchorMax = new Vector2(x, y);
        }

        static void Place(RectTransform rect, float x, float y, Vector2 position, Vector2 size)
        {
            Anchor(rect, x, y);
            rect.pivot = new Vector2(x, y);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        static Sprite Load(string path)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
            {
                Debug.LogWarning("[InventoryScreenBuilder] No sprite at " + path + ".");
            }

            return sprite;
        }

        static Image Sliced(GameObject go, string path)
        {
            var image = go.AddComponent<Image>();
            image.sprite = Load(path);
            image.type = Image.Type.Sliced;
            return image;
        }

        static Image Simple(GameObject go, string path)
        {
            var image = go.AddComponent<Image>();
            image.sprite = Load(path);
            image.preserveAspect = true;
            return image;
        }

        static TextMeshProUGUI Text(Transform parent, string name, string value, float size, TextAlignmentOptions align)
        {
            var go = Node(name, parent);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = size;
            text.alignment = align;
            text.textWrappingMode = TextWrappingModes.NoWrap;

            // Overflow rather than Ellipsis. Ellipsis shortens a label that is too wide, which is
            // wanted, but it also erases one that is a fraction too short vertically, which is not
            // — and the failure is silent, because the component still reports the full text.
            // Overflow spills instead, which is visible and therefore fixable.
            text.overflowMode = TextOverflowModes.Overflow;

            if (font != null)
            {
                text.font = font;
            }

            return text;
        }

        /// <summary>
        /// Sets a private serialized field. The components expose these through the inspector
        /// rather than through code, so the builder writes them the same way the inspector would.
        /// </summary>
        static void Wire(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var property = so.FindProperty(field);

            if (property == null)
            {
                Debug.LogWarning("[InventoryScreenBuilder] " + target.GetType().Name
                    + " has no serialized field '" + field + "'.");
                return;
            }

            property.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
