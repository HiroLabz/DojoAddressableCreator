using Dojo.Game.UI;
using Dojo.Game.UI.Account;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Dojo.Game.Editor
{
    /// <summary>
    /// Lays the Lobby out to the "improved" mock-ups: sign-in and registration as two columns - a
    /// full-height brand panel, and the card centred in its own column - and home on one baseline,
    /// the Continue card first and one sidebar for session, packs and recent worlds.
    /// </summary>
    /// <remarks>
    /// Re-runnable. What was already in the scene is moved, not rebuilt, so every reference the
    /// menu and the cards hold survives; what this adds - the brand panel, its reasons and footer,
    /// the account chip, the recent worlds, the home footer, the layout switcher - is removed and
    /// made again each run. New pieces are cloned from existing ones where one has the right look,
    /// so the fonts and colours stay the scene's own.
    /// <para>
    /// Every number is the mock-up's, measured on its 720-wide frame and scaled to the canvas's
    /// 1920 × 1080. The left column is measured from the top-left corner, the right column from the
    /// top-right, so a wider screen widens the gap between them rather than cutting the sidebar off.
    /// </para>
    /// </remarks>
    public static class LobbyLayoutBuilder
    {
        const string Kit = "Assets/Dojo/Art/2D/UI/";

        const float Margin = 107f;        // both sides, the mock's 80 at 1440
        const float BrandWidth = 827f;    // the sign-in brand panel
        const float RightColumnCentre = 1373f;

        static readonly Color BrandTint = new Color(0.055f, 0.086f, 0.157f, 0.9f);
        static readonly Color Rule = new Color(0.11f, 0.165f, 0.267f, 1f);
        static readonly Color Signal = new Color(0.184f, 0.561f, 1f, 1f);

        [MenuItem("Tools/Dojo/Build Lobby Layout")]
        public static void BuildInOpenScene()
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);

                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root.GetComponentInChildren<LobbyMainMenu>(true) != null)
                    {
                        Debug.Log("[Lobby] " + Build(scene));
                        return;
                    }
                }
            }

            EditorUtility.DisplayDialog("Build Lobby Layout", "Open the Lobby scene first.", "OK");
        }

        /// <summary>Lays out the Lobby in <paramref name="scene"/>. Returns a line saying what was done.</summary>
        public static string Build(Scene scene)
        {
            Transform canvas = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == "Canvas")
                {
                    canvas = root.transform;
                }
            }

            if (canvas == null)
            {
                return "no Canvas in '" + scene.name + "'.";
            }

            var menu = canvas.Find("MainMenuScreen");
            var login = canvas.Find("LoginScreen");
            var registration = canvas.Find("RegistrationScreen");

            // Last run's additions go first, so nothing below finds a stale copy.
            Remove(canvas, "AccountChrome");
            Remove(canvas, "LobbyLayout");
            Remove(menu, "RecentWorlds");
            Remove(menu, "AccountChip");
            Remove(menu, "HomeFooter");
            Remove(menu.Find("ContinueCard/ThumbnailWell"), "LastSave");
            Remove(menu.Find("ContinueCard/ThumbnailWell"), "Picture");
            Remove(canvas.Find("hiro"), "Frame");

            // Styles to clone.
            var brandLabel = (RectTransform)canvas.Find("TopBar/BrandLabel");
            var tagline = (RectTransform)canvas.Find("Title/Tagline");
            var session = (RectTransform)menu.Find("SessionPanel");
            var continueCard = (RectTransform)menu.Find("ContinueCard");
            var panelFrame = session.Find("Frame_STANDIN").GetComponent<Image>();
            var newWorld = (RectTransform)menu.Find("MenuGrid/NewWorldButton");

            var chrome = BuildAccountChrome(canvas, brandLabel, tagline);
            PlaceCards(login, registration);

            // Wide enough for the whole build string on one line.
            var buildBadge = (RectTransform)canvas.Find("TopBar/BuildBadge");
            Corner(buildBadge, TopRight, new Vector2(-Margin, -36f), new Vector2(260f, 38f));

            LayOutHome(menu, continueCard, newWorld, session);
            var footerBuild = BuildHomeFooter(menu, brandLabel);
            BuildRecentWorlds(menu, session, panelFrame, newWorld, (RectTransform)menu.Find("HomeFooter/Links"));
            BuildAccountChip(menu, brandLabel, newWorld);

            // The pack chips' job moved into the sidebar's PACKS row.
            var mainMenu = menu.GetComponent<LobbyMainMenu>();
            var packChips = menu.Find("PackChips");
            if (packChips != null)
            {
                packChips.gameObject.SetActive(false);
                Set(mainMenu, "packChips", null);
            }

            Set(session.GetComponent<SessionPanel>(), "footerBuild", footerBuild);

            BuildLayout(canvas, login, registration, chrome, buildBadge);

            EditorSceneManager.MarkSceneDirty(scene);
            return "laid out the Lobby in '" + scene.name + "'.";
        }

        // ── Sign in and registration ─────────────────────────────────────────────────────

        static GameObject BuildAccountChrome(Transform canvas, RectTransform brandLabel, RectTransform tagline)
        {
            var chrome = Stretched("AccountChrome", canvas);

            // Just in front of the backdrop: behind the mascot, the title and the cards.
            var background = canvas.Find("Background");
            chrome.SetSiblingIndex(background != null ? background.GetSiblingIndex() + 1 : 0);

            var panel = New("BrandPanel", chrome);
            panel.anchorMin = Vector2.zero;
            panel.anchorMax = new Vector2(0f, 1f);
            panel.pivot = new Vector2(0f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            panel.sizeDelta = new Vector2(BrandWidth, 0f);
            Paint(panel, null, BrandTint);

            var divider = New("BrandDivider", chrome);
            divider.anchorMin = new Vector2(0f, 0f);
            divider.anchorMax = new Vector2(0f, 1f);
            divider.pivot = new Vector2(0f, 0.5f);
            divider.anchoredPosition = new Vector2(BrandWidth, 0f);
            divider.sizeDelta = new Vector2(2f, 0f);
            Paint(divider, null, Rule);

            // Three reasons an account matters, bottom of the brand panel.
            var reasons = new[]
            {
                ("Every floor you build, on every machine", "icon/dojo_icon_floor_flat_idle_base@3x.png", 264f),
                ("Agents keep their names and their desks", "icon/dojo_icon_agents_flat_idle_base@3x.png", 184f),
                ("Content packs unlock once, everywhere", "icon/dojo_icon_inventory_flat_idle_base@3x.png", 104f),
            };

            for (var i = 0; i < reasons.Length; i++)
            {
                var (words, iconPath, centre) = reasons[i];

                var plate = New("Reason" + (i + 1), chrome);
                Corner(plate, BottomLeft, new Vector2(Margin, centre - 26f), new Vector2(52f, 52f));
                Paint(plate, Sprite("btn/dojo_btn_iconplate_9slice_idle_base@3x.png"), Color.white, sliced: true);

                var icon = New("Icon", plate);
                icon.anchorMin = icon.anchorMax = icon.pivot = new Vector2(0.5f, 0.5f);
                icon.anchoredPosition = Vector2.zero;
                icon.sizeDelta = new Vector2(24f, 24f);
                var iconImage = Paint(icon, Sprite(iconPath), Signal);
                iconImage.preserveAspect = true;

                var text = Clone(tagline, "Text", plate);
                text.anchorMin = text.anchorMax = new Vector2(0f, 0.5f);
                text.pivot = new Vector2(0f, 0.5f);
                text.anchoredPosition = new Vector2(76f, 0f);
                text.sizeDelta = new Vector2(600f, 32f);
                var label = text.GetComponent<TMP_Text>();
                label.text = words;
                label.fontSize = 19f;
                label.alignment = TextAlignmentOptions.MidlineLeft;
            }

            // Footer of the card's column.
            var year = Clone(brandLabel, "Copyright", chrome);
            Corner(year, BottomLeft, new Vector2(BrandWidth + 106f, 30f), new Vector2(400f, 24f));
            year.GetComponent<TMP_Text>().text = "© " + System.DateTime.Now.Year + " HIROLABZ";
            year.GetComponent<TMP_Text>().fontSize = 12f;

            var links = Clone(brandLabel, "Links", chrome);
            Corner(links, BottomRight, new Vector2(-Margin, 30f), new Vector2(400f, 24f));
            var linksText = links.GetComponent<TMP_Text>();
            linksText.text = "TERMS   ·   PRIVACY";
            linksText.fontSize = 12f;
            linksText.alignment = TextAlignmentOptions.MidlineRight;

            return chrome.gameObject;
        }

        /// <summary>
        /// Both cards centred in the right-hand column, whatever their pivot - and a card taller
        /// than the column leaves room for scaled down to fit, so the create-account card's extra
        /// fields do not push it up into the top bar or down onto the footer.
        /// </summary>
        static void PlaceCards(params Transform[] screens)
        {
            const float room = 1080f - 2f * 100f;

            foreach (var screen in screens)
            {
                var card = screen != null ? screen.Find("Card") as RectTransform : null;
                if (card == null)
                {
                    continue;
                }

                var scale = Mathf.Min(1f, room / card.sizeDelta.y);
                card.localScale = new Vector3(scale, scale, 1f);

                // Anchored to the right edge, so the column's middle is measured from there.
                card.anchorMin = card.anchorMax = new Vector2(1f, 0.5f);
                var x = RightColumnCentre - 1920f + (card.pivot.x - 0.5f) * card.sizeDelta.x * scale;
                card.anchoredPosition = new Vector2(x, 0f);
            }
        }

        // ── Home ─────────────────────────────────────────────────────────────────────────

        static void LayOutHome(Transform menu, RectTransform card, RectTransform newWorld, RectTransform session)
        {
            // The Continue card: the primary thing on the screen.
            Corner(card, TopLeft, new Vector2(Margin, -419f), new Vector2(1013f, 256f));
            Place(card, "ThumbnailWell", new Vector2(26f, -28f), new Vector2(288f, 200f));
            Hide(card, "Divider");
            Place(card, "ContinueLabel", new Vector2(352f, -58f), new Vector2(360f, 20f));
            Place(card, "WorldName", new Vector2(352f, -84f), new Vector2(420f, 50f), 40f);
            Place(card, "WorldMeta", new Vector2(352f, -148f), new Vector2(420f, 60f), 14f);
            Place(card, "ResumeButton", new Vector2(786f, -96f), null);

            var thumbnail = card.Find("ThumbnailWell");
            var lastSave = Clone((RectTransform)card.Find("ContinueLabel"), "LastSave", thumbnail);
            Corner(lastSave, BottomLeft, new Vector2(14f, 10f), new Vector2(200f, 18f));
            lastSave.GetComponent<TMP_Text>().text = "LAST SAVE";
            lastSave.GetComponent<TMP_Text>().fontSize = 11f;

            // The world's picture, inside the dashed edge and under the label. Off until the menu
            // has one to show.
            var picture = New("Picture", thumbnail);
            picture.anchorMin = Vector2.zero;
            picture.anchorMax = Vector2.one;
            picture.offsetMin = new Vector2(4f, 4f);
            picture.offsetMax = new Vector2(-4f, -4f);
            picture.SetAsFirstSibling();
            var pictureImage = picture.gameObject.AddComponent<RawImage>();
            pictureImage.raycastTarget = false;
            pictureImage.enabled = false;
            Set(menu.GetComponent<LobbyMainMenu>(), "continuePicture", pictureImage);

            // New and Load a level below, side by side under the card.
            var loadWorld = (RectTransform)menu.Find("MenuGrid/LoadWorldButton");
            MenuButton(newWorld, new Vector2(Margin, -701f), new Vector2(496f, 80f));
            MenuButton(loadWorld, new Vector2(Margin + 512f, -701f), new Vector2(501f, 80f));

            // Load World is hidden for good: Recent Worlds is how a world is picked. Laid out still,
            // so bringing it back is only showing it. New World keeps its size.
            if (loadWorld != null)
            {
                loadWorld.gameObject.SetActive(false);
            }

            // The sidebar's session panel, measured from the top-right.
            Corner(session, TopRight, new Vector2(-Margin, -171f), new Vector2(640f, 325f));
            Place(session, "HeaderLabel", new Vector2(32f, -30f), null);
            Place(session, "HeaderDiamond", new Vector2(592f, -33f), null);
            Place(session, "HeaderRule", new Vector2(32f, -62f), new Vector2(576f, 1f));

            var rows = new[] { ("Row_REALTIME", -86f), ("Row_IDENTITY", -148f), ("Row_CONTENT", -210f), ("Row_BUILD", -272f) };
            foreach (var (name, y) in rows)
            {
                var row = session.Find(name) as RectTransform;
                if (row == null)
                {
                    continue;
                }

                row.anchoredPosition = new Vector2(32f, y);
                row.sizeDelta = new Vector2(576f, 26f);
                Place(row, "Value", new Vector2(186f, 0f), new Vector2(390f, 26f));
            }

            var contentLabel = session.Find("Row_CONTENT/Label");
            if (contentLabel != null)
            {
                contentLabel.GetComponent<TMP_Text>().text = "PACKS";
            }
        }

        /// <summary>A menu button at its new size, its icon and label kept together in the middle.</summary>
        static void MenuButton(RectTransform button, Vector2 position, Vector2 size)
        {
            if (button == null)
            {
                return;
            }

            Corner(button, TopLeft, position, size);
            var top = -(size.y - 36f) * 0.5f;
            Place(button, "Icon", new Vector2(size.x * 0.5f - 92f, top), null);
            Place(button, "Label", new Vector2(size.x * 0.5f - 42f, top), null);
        }

        static TMP_Text BuildHomeFooter(Transform menu, RectTransform brandLabel)
        {
            var footer = Stretched("HomeFooter", menu);

            var build = Clone(brandLabel, "Build", footer);
            Corner(build, BottomLeft, new Vector2(Margin, 30f), new Vector2(600f, 24f));
            var buildText = build.GetComponent<TMP_Text>();
            buildText.text = "—";
            buildText.fontSize = 12f;

            var links = Clone(brandLabel, "Links", footer);
            Corner(links, BottomRight, new Vector2(-Margin, 30f), new Vector2(400f, 24f));
            var linksText = links.GetComponent<TMP_Text>();
            linksText.text = "SETTINGS   ·   SIGN OUT";
            linksText.fontSize = 12f;
            linksText.alignment = TextAlignmentOptions.MidlineRight;

            return buildText;
        }

        /// <summary>
        /// Recent Worlds: a scroll list of every saved world with its picture, which grows a row at
        /// a time down to the SETTINGS · SIGN OUT line and then scrolls.
        /// </summary>
        /// <remarks>
        /// One row is built, switched off, as the template the panel copies for each world. The
        /// sizes are <see cref="RecentWorldsLayout"/>'s, which the panel uses too, so the two agree.
        /// </remarks>
        static void BuildRecentWorlds(Transform menu, RectTransform session, Image panelFrame, RectTransform newWorld,
            RectTransform limit)
        {
            var panel = New("RecentWorlds", menu);
            panel.SetSiblingIndex(session.GetSiblingIndex() + 1);
            Corner(panel, TopRight, new Vector2(-Margin, -523f),
                new Vector2(640f, RecentWorldsLayout.PanelHeight(2, float.MaxValue)));

            var frame = Clone((RectTransform)panelFrame.transform, "Frame", panel);
            frame.anchorMin = Vector2.zero;
            frame.anchorMax = Vector2.one;
            frame.offsetMin = frame.offsetMax = Vector2.zero;

            var header = Clone((RectTransform)session.Find("HeaderLabel"), "HeaderLabel", panel);
            header.anchoredPosition = new Vector2(32f, -30f);
            header.GetComponent<TMP_Text>().text = "RECENT WORLDS";

            var rule = Clone((RectTransform)session.Find("HeaderRule"), "HeaderRule", panel);
            rule.anchoredPosition = new Vector2(32f, -62f);
            rule.sizeDelta = new Vector2(576f, 1f);

            // The scroll view, from under the rule to the panel's bottom padding. The see-through
            // face is what the wheel and a drag land on, since the rows take no clicks.
            var list = New("List", panel);
            list.anchorMin = Vector2.zero;
            list.anchorMax = Vector2.one;
            list.offsetMin = new Vector2(32f, RecentWorldsLayout.Bottom);
            list.offsetMax = new Vector2(-32f, -RecentWorldsLayout.Header);
            Paint(list, null, new Color(0f, 0f, 0f, 0f)).raycastTarget = true;
            list.gameObject.AddComponent<RectMask2D>();

            var content = New("Content", list);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, RecentWorldsLayout.RowHeight);

            var scroll = list.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = list;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            // The row each world gets a copy of: its picture on the left, where the icon was.
            var rowFace = newWorld.GetComponent<Image>();
            var nameStyle = (RectTransform)menu.Find("ContinueCard/WorldName");
            var detailStyle = (RectTransform)menu.Find("ContinueCard/ContinueLabel");

            var row = New("RowTemplate", content);
            row.anchorMin = row.anchorMax = row.pivot = TopLeft;
            row.anchoredPosition = Vector2.zero;
            row.sizeDelta = new Vector2(576f, RecentWorldsLayout.RowHeight);
            var rowImage = Paint(row, rowFace.sprite, rowFace.color, sliced: rowFace.type == Image.Type.Sliced);

            // A button that opens its world, lit on hover the way NEW WORLD is. A drag still
            // scrolls the list: a button takes the click, not the drag.
            rowImage.raycastTarget = true;
            var rowButton = row.gameObject.AddComponent<Button>();
            rowButton.targetGraphic = rowImage;
            var menuButton = newWorld.GetComponent<Button>();
            if (menuButton != null)
            {
                rowButton.transition = menuButton.transition;
                rowButton.colors = menuButton.colors;
                rowButton.spriteState = menuButton.spriteState;
            }
            rowButton.navigation = new Navigation { mode = Navigation.Mode.None };

            // The card's picture shape, 1.44 to 1, small enough to leave the name room.
            var thumb = New("Thumb", row);
            Corner(thumb, TopLeft, new Vector2(14f, -13f), new Vector2(124f, 86f));
            Paint(thumb, Sprite("btn/dojo_btn_iconplate_9slice_idle_base@3x.png"), Color.white, sliced: true);

            var icon = New("Icon", thumb);
            icon.anchorMin = icon.anchorMax = icon.pivot = new Vector2(0.5f, 0.5f);
            icon.anchoredPosition = Vector2.zero;
            icon.sizeDelta = new Vector2(28f, 28f);
            Paint(icon, Sprite("icon/dojo_icon_floor_flat_idle_base@3x.png"), Signal).preserveAspect = true;

            var picture = New("Picture", thumb);
            picture.anchorMin = Vector2.zero;
            picture.anchorMax = Vector2.one;
            picture.offsetMin = new Vector2(3f, 3f);
            picture.offsetMax = new Vector2(-3f, -3f);
            var pictureImage = picture.gameObject.AddComponent<RawImage>();
            pictureImage.raycastTarget = false;
            pictureImage.enabled = false;

            var name = Clone(nameStyle, "Name", row);
            Corner(name, TopLeft, new Vector2(156f, -38f), new Vector2(250f, 36f));
            var nameText = name.GetComponent<TMP_Text>();
            nameText.fontSize = 18f;
            nameText.text = "World";
            nameText.alignment = TextAlignmentOptions.MidlineLeft;
            nameText.raycastTarget = false;

            var detail = Clone(detailStyle, "Detail", row);
            Corner(detail, TopRight, new Vector2(-18f, -46f), new Vector2(140f, 20f));
            var detailText = detail.GetComponent<TMP_Text>();
            detailText.text = string.Empty;
            detailText.alignment = TextAlignmentOptions.MidlineRight;
            detailText.color = Signal;
            detailText.raycastTarget = false;

            row.gameObject.SetActive(false);

            // In the first row's place, which is as tall as the panel ever is when empty.
            var empty = Clone((RectTransform)menu.parent.Find("Title/Tagline"), "Empty", panel);
            empty.anchorMin = empty.anchorMax = empty.pivot = new Vector2(0.5f, 1f);
            empty.anchoredPosition = new Vector2(0f, -(RecentWorldsLayout.Header + RecentWorldsLayout.RowHeight * 0.5f - 20f));
            empty.sizeDelta = new Vector2(576f, 40f);
            var emptyText = empty.GetComponent<TMP_Text>();
            emptyText.text = "No saved worlds yet";
            emptyText.fontSize = 16f;
            emptyText.alignment = TextAlignmentOptions.Center;
            empty.gameObject.SetActive(false);

            var recent = panel.gameObject.AddComponent<RecentWorldsPanel>();
            var so = new SerializedObject(recent);
            so.FindProperty("panel").objectReferenceValue = panel;
            so.FindProperty("scroll").objectReferenceValue = scroll;
            so.FindProperty("limit").objectReferenceValue = limit;
            so.FindProperty("empty").objectReferenceValue = emptyText;

            var template = so.FindProperty("template");
            template.FindPropertyRelative("root").objectReferenceValue = row.gameObject;
            template.FindPropertyRelative("name").objectReferenceValue = nameText;
            template.FindPropertyRelative("detail").objectReferenceValue = detailText;
            template.FindPropertyRelative("picture").objectReferenceValue = pictureImage;
            template.FindPropertyRelative("icon").objectReferenceValue = icon.gameObject;

            so.ApplyModifiedPropertiesWithoutUndo();

            Set(menu.GetComponent<LobbyMainMenu>(), "recentWorlds", recent);
        }

        static void BuildAccountChip(Transform menu, RectTransform brandLabel, RectTransform newWorld)
        {
            var chip = New("AccountChip", menu);
            Corner(chip, TopRight, new Vector2(-Margin, -34f), new Vector2(270f, 52f));
            var face = newWorld.GetComponent<Image>();
            Paint(chip, face.sprite, face.color, sliced: face.type == Image.Type.Sliced).raycastTarget = false;

            var email = Clone(brandLabel, "Email", chip);
            email.anchorMin = new Vector2(0f, 0.5f);
            email.anchorMax = new Vector2(1f, 0.5f);
            email.pivot = new Vector2(0.5f, 0.5f);
            email.offsetMin = new Vector2(16f, -13f);
            email.offsetMax = new Vector2(-58f, 13f);
            var emailText = email.GetComponent<TMP_Text>();
            emailText.text = "Signed in";
            emailText.fontSize = 12f;
            emailText.alignment = TextAlignmentOptions.MidlineRight;
            emailText.overflowMode = TextOverflowModes.Ellipsis;

            var avatar = New("Avatar", chip);
            avatar.anchorMin = avatar.anchorMax = avatar.pivot = new Vector2(1f, 0.5f);
            avatar.anchoredPosition = new Vector2(-10f, 0f);
            avatar.sizeDelta = new Vector2(34f, 34f);
            Paint(avatar, Sprite("account/dojo_account_box_idle_base@3x.png"), Signal, sliced: true).raycastTarget = false;

            var initial = Clone(brandLabel, "Initial", avatar);
            initial.anchorMin = Vector2.zero;
            initial.anchorMax = Vector2.one;
            initial.offsetMin = initial.offsetMax = Vector2.zero;
            var initialText = initial.GetComponent<TMP_Text>();
            initialText.text = "?";
            initialText.fontSize = 15f;
            initialText.characterSpacing = 0f;
            initialText.alignment = TextAlignmentOptions.Center;
            initialText.color = Color.white;

            var component = chip.gameObject.AddComponent<AccountChip>();
            Set(component, "email", emailText);
            Set(component, "initial", initialText);
        }

        // ── The switcher ─────────────────────────────────────────────────────────────────

        static void BuildLayout(Transform canvas, Transform login, Transform registration, GameObject chrome, RectTransform buildBadge)
        {
            var hiro = (RectTransform)canvas.Find("hiro");
            hiro.anchorMin = hiro.anchorMax = hiro.pivot = TopLeft;

            // The mascot sits in a tile, as in the mock-up; the pictures fill it inside a margin.
            var tile = New("Frame", hiro);
            tile.SetAsFirstSibling();
            tile.anchorMin = Vector2.zero;
            tile.anchorMax = Vector2.one;
            tile.offsetMin = tile.offsetMax = Vector2.zero;
            var sessionFrame = canvas.Find("MainMenuScreen/SessionPanel/Frame_STANDIN").GetComponent<Image>();
            Paint(tile, sessionFrame.sprite, sessionFrame.color, sliced: sessionFrame.type == Image.Type.Sliced).raycastTarget = false;

            var picture = hiro.Find("hiroImage") as RectTransform;
            if (picture != null)
            {
                picture.anchorMin = Vector2.zero;
                picture.anchorMax = Vector2.one;
                picture.pivot = new Vector2(0.5f, 0.5f);
                picture.offsetMin = new Vector2(10f, 10f);
                picture.offsetMax = new Vector2(-10f, -10f);
            }

            var brandLabel = (RectTransform)canvas.Find("TopBar/BrandLabel");
            var eyebrow = (RectTransform)canvas.Find("Title/Eyebrow");
            var wordmark = (RectTransform)canvas.Find("Title/Wordmark_PLACEHOLDER");
            var tagline = (RectTransform)canvas.Find("Title/Tagline");

            foreach (var piece in new[] { brandLabel, eyebrow, wordmark, tagline })
            {
                piece.anchorMin = piece.anchorMax = piece.pivot = TopLeft;
            }

            var go = new GameObject("LobbyLayout", typeof(RectTransform));
            go.transform.SetParent(canvas, false);
            var layout = go.AddComponent<LobbyLayout>();

            var so = new SerializedObject(layout);
            so.FindProperty("loginScreen").objectReferenceValue = login.gameObject;
            so.FindProperty("registrationScreen").objectReferenceValue = registration.gameObject;

            var placements = new (RectTransform target, Vector2 home, Vector2 homeSize, float homeFont, Vector2 account, Vector2 accountSize, float accountFont)[]
            {
                (brandLabel, new Vector2(Margin, -44f), new Vector2(340f, 24f), 0f, new Vector2(BrandWidth + 106f, -44f), new Vector2(340f, 24f), 0f),
                (hiro, new Vector2(Margin, -165f), new Vector2(160f, 198f), 0f, new Vector2(Margin, -131f), new Vector2(200f, 248f), 0f),
                (eyebrow, new Vector2(293f, -172f), new Vector2(420f, 22f), 0f, new Vector2(339f, -244f), new Vector2(420f, 22f), 0f),
                (wordmark, new Vector2(286f, -188f), new Vector2(560f, 116f), 104f, new Vector2(331f, -262f), new Vector2(560f, 140f), 128f),
                (tagline, new Vector2(293f, -306f), new Vector2(520f, 64f), 18f, new Vector2(Margin, -420f), new Vector2(560f, 64f), 20f),
            };

            var list = so.FindProperty("placements");
            list.arraySize = placements.Length;
            for (var i = 0; i < placements.Length; i++)
            {
                var p = placements[i];
                var element = list.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("target").objectReferenceValue = p.target;
                element.FindPropertyRelative("home").vector2Value = p.home;
                element.FindPropertyRelative("homeSize").vector2Value = p.homeSize;
                element.FindPropertyRelative("homeFont").floatValue = p.homeFont;
                element.FindPropertyRelative("account").vector2Value = p.account;
                element.FindPropertyRelative("accountSize").vector2Value = p.accountSize;
                element.FindPropertyRelative("accountFont").floatValue = p.accountFont;
            }

            var only = so.FindProperty("accountOnly");
            only.arraySize = 2;
            only.GetArrayElementAtIndex(0).objectReferenceValue = chrome;
            only.GetArrayElementAtIndex(1).objectReferenceValue = buildBadge.gameObject;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Saved in the home layout, which is what the scene opens on.
            layout.Apply(false);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────────

        static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        static readonly Vector2 TopRight = new Vector2(1f, 1f);
        static readonly Vector2 BottomLeft = new Vector2(0f, 0f);
        static readonly Vector2 BottomRight = new Vector2(1f, 0f);

        /// <summary>Anchors and pivots <paramref name="rect"/> to one corner, then places it from there.</summary>
        static void Corner(RectTransform rect, Vector2 corner, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = corner;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        /// <summary>Moves a child, and resizes it and its font when asked; leaves its anchors as they are.</summary>
        static void Place(Transform parent, string child, Vector2 position, Vector2? size, float font = 0f)
        {
            var rect = parent.Find(child) as RectTransform;
            if (rect == null)
            {
                return;
            }

            rect.anchoredPosition = position;

            if (size.HasValue)
            {
                rect.sizeDelta = size.Value;
            }

            var text = font > 0f ? rect.GetComponent<TMP_Text>() : null;
            if (text != null)
            {
                text.fontSize = font;
            }
        }

        static void Hide(Transform parent, string child)
        {
            var found = parent.Find(child);
            if (found != null)
            {
                found.gameObject.SetActive(false);
            }
        }

        static void Remove(Transform parent, string child)
        {
            var found = parent != null ? parent.Find(child) : null;
            while (found != null)
            {
                Object.DestroyImmediate(found.gameObject);
                found = parent.Find(child);
            }
        }

        static RectTransform New(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        static RectTransform Stretched(string name, Transform parent)
        {
            var rect = New(name, parent);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        /// <summary>A copy of a styled piece - its font, colour and spacing - with its children left behind.</summary>
        static RectTransform Clone(RectTransform source, string name, Transform parent)
        {
            var copy = Object.Instantiate(source.gameObject, parent, false);
            copy.name = name;

            for (var i = copy.transform.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(copy.transform.GetChild(i).gameObject);
            }

            foreach (var component in copy.GetComponents<MonoBehaviour>())
            {
                if (!(component is Graphic))
                {
                    Object.DestroyImmediate(component);
                }
            }

            copy.SetActive(true);
            return (RectTransform)copy.transform;
        }

        static Image Paint(RectTransform rect, Sprite sprite, Color colour, bool sliced = false)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = colour;
            image.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            image.raycastTarget = false;
            return image;
        }

        static Sprite Sprite(string path) => AssetDatabase.LoadAssetAtPath<Sprite>(Kit + path);

        static void Set(Object target, string field, Object value)
        {
            if (target == null)
            {
                return;
            }

            var so = new SerializedObject(target);
            var property = so.FindProperty(field);
            if (property != null)
            {
                property.objectReferenceValue = value;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }
}
