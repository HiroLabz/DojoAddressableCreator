using Dojo.Game.UI.Account;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Game.Editor
{
    /// <summary>
    /// Assembles the Sign in and Create account cards into the Lobby's <c>LoginScreen</c> and
    /// <c>RegistrationScreen</c>, to the v2 account mock-ups.
    /// </summary>
    /// <remarks>
    /// Only the right-hand cards: the Lobby's own background shows through behind them, so each
    /// screen's full-screen image is made transparent - it still catches clicks, so the menu behind
    /// cannot be pressed while a card is up.
    /// <para>
    /// Built from the UI kit where the kit has the part - fields, buttons, the panel, the strength
    /// bar, the diamond - and from the white stand-ins <see cref="KitStandIns"/> draws where it does not: the mail, lock,
    /// eye, person, tick, Google and Apple marks, the check box and the corner markers. They go in
    /// <c>Art/2D/UI/account</c> under the kit's own naming, so real art can replace them file for file.
    /// </para>
    /// <para>
    /// Re-runnable: each run removes the card it built last time and builds it again, so a layout
    /// change here is one menu click away from the scene.
    /// </para>
    /// </remarks>
    public static class AccountScreensBuilder
    {
        const string Kit = "Assets/Dojo/Art/2D/UI/";
        const string Fonts = "Assets/Dojo/Fonts/TMP/";
        const string CardName = "Card";
        const string MainMenuName = "MainMenuScreen";

        // The kit's palette (_palette.json).
        static readonly Color Void = Hex("#060910");
        static readonly Color Edge = Hex("#2a4570");
        static readonly Color Ink = Hex("#ffffff");
        static readonly Color InkSoft = Hex("#9fb2cc");
        static readonly Color InkMute = Hex("#6d809c");
        static readonly Color Signal = Hex("#2f8fff");

        const float CardWidth = 600f;
        const float Inset = 42f;
        const float Content = CardWidth - Inset * 2f;   // 516, the fields' width in the mock-ups
        const float RightMargin = 140f;

        [MenuItem("Tools/Dojo/Build Account Screens")]
        public static void BuildInOpenScenes()
        {
            GameObject login = null, registration = null;

            for (var i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                foreach (var root in EditorSceneManager.GetSceneAt(i).GetRootGameObjects())
                {
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name == "LoginScreen") login = t.gameObject;
                        if (t.name == "RegistrationScreen") registration = t.gameObject;
                    }
                }
            }

            if (login == null || registration == null)
            {
                EditorUtility.DisplayDialog("Build Account Screens",
                    "Open the Lobby scene first: it needs a LoginScreen and a RegistrationScreen.", "OK");
                return;
            }

            Debug.Log("[Account] " + Build(login, registration));

            // The cards are made pinned to the right edge; the Lobby's layout is what centres them
            // in their column, so it goes again after them rather than being left undone.
            Debug.Log("[Lobby] " + LobbyLayoutBuilder.Build(login.scene));
        }

        /// <summary>Builds both cards. Returns a line describing what was done.</summary>
        public static string Build(GameObject loginScreen, GameObject registrationScreen)
        {
            var parts = new Parts();

            var signInMessage = BuildSignIn(loginScreen, registrationScreen, parts);
            var createAccountMessage = BuildCreateAccount(registrationScreen, loginScreen, parts);
            Gate(loginScreen, registrationScreen, signInMessage, createAccountMessage);

            // Which screen is showing is left as it was: that is only how the scene is being edited,
            // and LobbyScreens decides at run time.
            EditorSceneManager.MarkSceneDirty(loginScreen.scene);

            return "built the Sign in card into '" + loginScreen.name + "' and the Create account card into '"
                + registrationScreen.name + "'.";
        }

        // ── The two cards ─────────────────────────────────────────────────────────────────

        static CardMessage BuildSignIn(GameObject screen, GameObject other, Parts p)
        {
            // Every top edge below is measured off the Sign in mock-up, in 1920x1080 units - with
            // the rows under the password moved down by the strength meter, which the mock-up does
            // not have and the Create account card does.
            const float meter = 53f;
            var card = Card(screen, 811f + meter, p);

            Header(card, "SIGN IN", 51f, p);
            Label(card, "Title", "Welcome back", Inset, 71f, Content, 56f, p.Poppins600, 42f, Ink);
            var subtitle = Label(card, "Subtitle",
                "Your floors, <color=#7fb2ff>agents</color> and <color=#7fb2ff>content packs</color> follow the account.",
                Inset, 133f, Content, 30f, p.Poppins400, 18f, InkSoft);
            var message = subtitle.gameObject.AddComponent<CardMessage>();

            Caption(card, "EMAIL", 191f, p);
            var email = Field(card, "Email", 227f, p.Mail, "you@studio.com", TMP_InputField.ContentType.EmailAddress, false, p, out _, out _);

            Caption(card, "PASSWORD", 323f, p);
            TMP_Text showLabel;
            Button show;
            var password = Field(card, "Password", 358f, p.Lock, "Password", TMP_InputField.ContentType.Password, true, p, out show, out showLabel);

            // As far under the field as the Create account card's meter sits under its own.
            Image[] segments;
            Image marker;
            TMP_Text strengthLabel;
            Strength(card, 358f + 89f, p, out segments, out marker, out strengthLabel);

            var keep = Checkbox(card, "KeepSignedIn", "Keep me signed in", 454f + meter, true, p);
            var forgot = Link(card, "ForgotPassword", "Forgot password?", TextAlignmentOptions.MidlineRight,
                Inset, 454f + meter, Content, 24f, p);

            var signIn = Primary(card, "SignIn", "SIGN IN", 512f + meter, p);
            Divider(card, "OR CONTINUE WITH", 608f + meter, p);

            Button google, apple;
            Socials(card, 661f + meter, p, out google, out apple);

            Button create;
            Footer(card, "New here?", "Create an account", 752f + meter, p, out create);

            // Not ??: in the editor a missing component comes back as Unity's stand-in null, which
            // ?? does not see as null.
            var panel = screen.GetComponent<SignInPanel>();
            if (panel == null)
            {
                panel = screen.AddComponent<SignInPanel>();
            }
            Wire(panel, ("email", email), ("password", password), ("showPassword", show),
                ("showPasswordLabel", showLabel), ("keepSignedIn", keep), ("signIn", signIn),
                ("forgotPassword", forgot), ("google", google), ("apple", apple),
                ("createAccount", create), ("thisScreen", screen), ("registrationScreen", other),
                ("strengthLabel", strengthLabel), ("strengthMarker", marker));
            WireSegments(panel, segments);

            return message;
        }

        static CardMessage BuildCreateAccount(GameObject screen, GameObject other, Parts p)
        {
            // Every top edge below is measured off the Create account mock-up, in 1920x1080 units.
            var card = Card(screen, 924f, p);

            Header(card, "CREATE ACCOUNT", 20f, p);
            Label(card, "Title", "Start your first floor", Inset, 54f, Content, 56f, p.Poppins600, 42f, Ink);
            var subtitle = Label(card, "Subtitle", "One account, every device. No card needed.",
                Inset, 116f, Content, 30f, p.Poppins400, 18f, InkSoft);
            var message = subtitle.gameObject.AddComponent<CardMessage>();

            Caption(card, "DISPLAY NAME", 169f, p);
            var name = Field(card, "DisplayName", 203f, p.User, "Your name", TMP_InputField.ContentType.Name, false, p, out _, out _);

            Caption(card, "EMAIL", 290f, p);
            var email = Field(card, "Email", 325f, p.Mail, "you@studio.com", TMP_InputField.ContentType.EmailAddress, false, p, out _, out _);

            Caption(card, "PASSWORD", 414f, p);
            TMP_Text showLabel;
            Button show;
            var password = Field(card, "Password", 449f, p.Lock, "At least 8 characters", TMP_InputField.ContentType.Password, true, p, out show, out showLabel);

            Image[] segments;
            Image marker;
            TMP_Text strengthLabel;
            Strength(card, 538f, p, out segments, out marker, out strengthLabel);

            var agree = Checkbox(card, "Agree", "I agree to the terms of service and privacy policy", 598f, false, p);

            var create = Primary(card, "CreateAccount", "CREATE ACCOUNT", 650f, p);
            Divider(card, "OR SIGN UP WITH", 741f, p);

            Button google, apple;
            Socials(card, 787f, p, out google, out apple);

            Button signIn;
            Footer(card, "Already have an account?", "Sign in", 873f, p, out signIn);

            var panel = screen.GetComponent<CreateAccountPanel>();
            if (panel == null)
            {
                panel = screen.AddComponent<CreateAccountPanel>();
            }
            Wire(panel, ("displayName", name), ("email", email), ("password", password),
                ("showPassword", show), ("showPasswordLabel", showLabel), ("strengthLabel", strengthLabel),
                ("strengthMarker", marker), ("agree", agree), ("createAccount", create),
                ("google", google), ("apple", apple), ("signIn", signIn), ("thisScreen", screen),
                ("loginScreen", other));
            WireSegments(panel, segments);

            return message;
        }

        /// <summary>
        /// Four segments under a password, and the line that says how strong it is, the bars'
        /// top at <paramref name="top"/>. Plain bars rather than the kit's fill, which ships blue:
        /// tinting blue green gives teal.
        /// </summary>
        static void Strength(RectTransform card, float top, Parts p, out Image[] segments, out Image marker, out TMP_Text label)
        {
            segments = new Image[PasswordStrength.Segments];
            const float gap = 8f;
            var width = (Content - gap * (segments.Length - 1)) / segments.Length;
            for (var i = 0; i < segments.Length; i++)
            {
                segments[i] = Sprite(card, "Strength" + (i + 1), null, Inset + i * (width + gap), top, width, 5f);
                segments[i].color = Hex("#1c2940");
            }

            marker = Sprite(card, "StrengthMarker", p.Diamond, Inset, top + 20f, 10f, 10f);
            marker.enabled = false;
            label = Label(card, "StrengthLabel", string.Empty, Inset + 18f, top + 13f, Content - 18f, 24f, p.Poppins400, 15f, InkSoft);
        }

        static void WireSegments(UnityEngine.Object panel, Image[] segments)
        {
            var so = new SerializedObject(panel);
            var list = so.FindProperty("segments");
            list.arraySize = segments.Length;
            for (var i = 0; i < segments.Length; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = segments[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ── Pieces ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The one component that decides which of the three screens shows, put on their parent so
        /// it stays awake while all three are hidden.
        /// </summary>
        static void Gate(GameObject loginScreen, GameObject registrationScreen,
            CardMessage signInMessage, CardMessage createAccountMessage)
        {
            var host = loginScreen.transform.parent;
            if (host == null || host != registrationScreen.transform.parent)
            {
                Debug.LogError("[Account] LoginScreen and RegistrationScreen need the same parent for LobbyScreens to sit on.");
                return;
            }

            var mainMenu = host.Find(MainMenuName);
            if (mainMenu == null)
            {
                Debug.LogError("[Account] No " + MainMenuName + " beside the cards: the menu will not show after sign-in.");
            }

            var screens = host.GetComponent<LobbyScreens>();
            if (screens == null)
            {
                screens = host.gameObject.AddComponent<LobbyScreens>();
            }

            Wire(screens, ("mainMenuScreen", mainMenu != null ? mainMenu.gameObject : null),
                ("loginScreen", loginScreen), ("registrationScreen", registrationScreen),
                ("signIn", loginScreen.GetComponent<SignInPanel>()),
                ("createAccount", registrationScreen.GetComponent<CreateAccountPanel>()),
                ("signInMessage", signInMessage), ("createAccountMessage", createAccountMessage));
        }

        /// <summary>The card itself: panel, corner markers, pinned right and centred.</summary>
        static RectTransform Card(GameObject screen, float height, Parts p)
        {
            // Transparent, not removed: it still stops clicks reaching the Lobby behind the card.
            var cover = screen.GetComponent<Image>();
            if (cover != null)
            {
                cover.color = new Color(0f, 0f, 0f, 0f);
                cover.raycastTarget = true;
            }

            var old = screen.transform.Find(CardName);
            if (old != null)
            {
                UnityEngine.Object.DestroyImmediate(old.gameObject);
            }

            var card = Rect(CardName, (RectTransform)screen.transform);
            card.anchorMin = card.anchorMax = new Vector2(1f, 0.5f);
            card.pivot = new Vector2(1f, 0.5f);
            card.anchoredPosition = new Vector2(-RightMargin, 0f);
            card.sizeDelta = new Vector2(CardWidth, height);

            var panel = Stretched("Panel", card).gameObject.AddComponent<Image>();
            panel.sprite = p.Panel;
            panel.type = Image.Type.Sliced;
            panel.raycastTarget = true;

            // Corner markers, centred on each corner of the card.
            var corners = new[] { new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(1f, 0f) };
            for (var i = 0; i < corners.Length; i++)
            {
                var holder = Rect("Corner" + (i + 1), card);
                holder.anchorMin = holder.anchorMax = corners[i];
                holder.pivot = new Vector2(0.5f, 0.5f);
                holder.anchoredPosition = Vector2.zero;
                holder.sizeDelta = new Vector2(24f, 24f);

                var fill = Stretched("Fill", holder, 6f).gameObject.AddComponent<Image>();
                fill.sprite = p.Box;
                fill.type = Image.Type.Sliced;
                fill.color = Void;
                fill.raycastTarget = false;

                var ring = Stretched("Ring", holder).gameObject.AddComponent<Image>();
                ring.sprite = p.Corner;
                ring.color = Signal;
                ring.raycastTarget = false;
            }

            return card;
        }

        static void Header(RectTransform card, string words, float top, Parts p)
        {
            var header = Label(card, "Header", "[ " + words + " ]", Inset, top, Content, 24f, p.Mono500, 15f, Signal);
            header.characterSpacing = 30f;
        }

        static void Caption(RectTransform card, string words, float top, Parts p)
        {
            var caption = Label(card, "Caption " + words, words, Inset, top, Content, 20f, p.Mono500, 14f, InkMute);
            caption.characterSpacing = 25f;
        }

        /// <summary>A text field in the kit's frame, with its icon, and a Show switch for passwords.</summary>
        static TMP_InputField Field(
            RectTransform card, string name, float top, Sprite icon, string placeholder,
            TMP_InputField.ContentType type, bool withShow, Parts p, out Button show, out TMP_Text showLabel)
        {
            // The halo first, so it draws behind the frame. The kit's glow is 26.7 larger all round.
            const float halo = 26.67f;
            var glow = Sprite(card, name + " Glow", p.FieldGlow, Inset - halo, top - halo, Content + halo * 2f, 64f + halo * 2f);
            glow.color = Signal;
            glow.enabled = false;

            var frame = Sprite(card, name, p.FieldIdle, Inset, top, Content, 64f);
            frame.type = Image.Type.Sliced;
            frame.raycastTarget = true;
            var root = frame.rectTransform;

            var mark = Sprite(root, "Icon", icon, 20f, 21f, 22f, 22f);
            mark.color = InkSoft;

            var area = Rect("Text Area", root);
            area.anchorMin = new Vector2(0f, 0f);
            area.anchorMax = new Vector2(1f, 1f);
            area.offsetMin = new Vector2(56f, 0f);
            area.offsetMax = new Vector2(withShow ? -104f : -18f, 0f);
            area.gameObject.AddComponent<RectMask2D>();

            var hint = Text(area, "Placeholder", placeholder, p.Poppins400, 20f, InkMute, TextAlignmentOptions.MidlineLeft);
            var value = Text(area, "Text", string.Empty, p.Poppins400, 20f, Ink, TextAlignmentOptions.MidlineLeft);

            var input = root.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = area;
            input.textComponent = value;
            input.placeholder = hint;
            input.fontAsset = p.Poppins400;
            input.pointSize = 20f;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.contentType = type;
            input.caretColor = Signal;
            input.customCaretColor = true;
            input.caretWidth = 2;
            input.selectionColor = new Color(Signal.r, Signal.g, Signal.b, 0.35f);
            input.targetGraphic = frame;
            input.transition = Selectable.Transition.None;

            var focus = root.gameObject.AddComponent<InputFieldFocus>();
            Wire(focus, ("frame", frame), ("idle", p.FieldIdle), ("focused", p.FieldFocused), ("glow", glow));

            show = null;
            showLabel = null;

            if (withShow)
            {
                var holder = Rect("Show", root);
                holder.anchorMin = holder.anchorMax = new Vector2(1f, 0.5f);
                holder.pivot = new Vector2(1f, 0.5f);
                holder.anchoredPosition = new Vector2(-16f, 0f);
                holder.sizeDelta = new Vector2(84f, 40f);

                var hit = holder.gameObject.AddComponent<Image>();
                hit.color = new Color(0f, 0f, 0f, 0f);

                var eye = Sprite(holder, "Eye", p.Eye, 0f, 11f, 20f, 18f);
                eye.color = InkMute;

                showLabel = Text(holder, "Label", "Show", p.Poppins400, 15f, InkMute, TextAlignmentOptions.MidlineLeft);
                var labelRect = showLabel.rectTransform;
                labelRect.offsetMin = new Vector2(28f, 0f);

                show = holder.gameObject.AddComponent<Button>();
                show.targetGraphic = hit;
            }

            return input;
        }

        /// <summary>A check box with its words beside it; the words are part of what can be clicked.</summary>
        static Toggle Checkbox(RectTransform card, string name, string words, float top, bool on, Parts p)
        {
            var root = Rect(name, card);
            Place(root, Inset, top, 400f, 24f);

            var box = Sprite(root, "Box", p.Box, 0f, 0f, 24f, 24f);
            box.type = Image.Type.Sliced;
            box.raycastTarget = true;

            var edge = Sprite(root, "Edge", p.BoxEdge, 0f, 0f, 24f, 24f);
            edge.type = Image.Type.Sliced;
            edge.color = Edge;

            var tick = Sprite(root, "Tick", p.Check, 3f, 3f, 18f, 18f);
            tick.color = Ink;

            Label(root, "Label", words, 36f, 0f, 364f, 24f, p.Poppins400, 17f, InkSoft).raycastTarget = true;

            var toggle = root.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = box;
            toggle.graphic = tick;
            toggle.transition = Selectable.Transition.None;
            toggle.isOn = on;

            var style = root.gameObject.AddComponent<CheckboxStyle>();
            Wire(style, ("box", box), ("edge", edge));

            // Coloured now as well as in Play, so the scene shows what the game will.
            box.color = on ? Signal : Hex("#0c121e");
            edge.enabled = !on;

            return toggle;
        }

        /// <summary>The kit's primary button, glow and all, full width.</summary>
        static Button Primary(RectTransform card, string name, string caption, float top, Parts p)
        {
            const float halo = 26.67f;
            var glow = Sprite(card, name + " Glow", p.PrimaryGlow, Inset - halo, top - halo, Content + halo * 2f, 64f + halo * 2f);
            glow.color = Signal;

            var face = Sprite(card, name, p.PrimaryIdle, Inset, top, Content, 64f);
            face.type = Image.Type.Sliced;
            face.raycastTarget = true;

            var label = Text(face.rectTransform, "Label", caption, p.Poppins600, 17f, Ink, TextAlignmentOptions.Center);
            label.characterSpacing = 12f;

            var button = face.gameObject.AddComponent<Button>();
            button.targetGraphic = face;
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState
            {
                highlightedSprite = p.PrimaryHover,
                pressedSprite = p.PrimaryPressed,
                selectedSprite = p.PrimaryIdle,
                disabledSprite = p.Disabled,
            };

            var follow = face.gameObject.AddComponent<GlowWhenInteractable>();
            Wire(follow, ("button", button), ("glow", glow));

            return button;
        }

        static void Divider(RectTransform card, string words, float top, Parts p)
        {
            const float middle = 240f;
            var line = (Content - middle) / 2f - 12f;

            var left = Sprite(card, "Divider Left", null, Inset, top + 10f, line, 1f);
            left.color = new Color(Edge.r, Edge.g, Edge.b, 0.8f);

            var right = Sprite(card, "Divider Right", null, Inset + Content - line, top + 10f, line, 1f);
            right.color = left.color;

            var label = Label(card, "Divider " + words, words, Inset, top, Content, 20f, p.Mono500, 13f, InkMute);
            label.alignment = TextAlignmentOptions.Center;
            label.characterSpacing = 25f;
        }

        /// <summary>Google and Apple, side by side in ghost buttons.</summary>
        static void Socials(RectTransform card, float top, Parts p, out Button google, out Button apple)
        {
            const float gap = 20f;
            var width = (Content - gap) / 2f;

            google = Social(card, "Google", Inset, top, width, p.Google, Hex("#3b7fe6"), p);
            apple = Social(card, "Apple", Inset + width + gap, top, width, p.Apple, Hex("#8d97a8"), p);
        }

        static Button Social(RectTransform card, string name, float left, float top, float width, Sprite mark, Color plateColour, Parts p)
        {
            var face = Sprite(card, name, p.GhostIdle, left, top, width, 64f);
            face.type = Image.Type.Sliced;
            face.raycastTarget = true;

            var row = Rect("Content", face.rectTransform);
            row.anchorMin = Vector2.zero;
            row.anchorMax = Vector2.one;
            row.offsetMin = row.offsetMax = Vector2.zero;
            SizedByContent(row.gameObject.AddComponent<HorizontalLayoutGroup>(), 14f);

            var plate = Rect("Plate", row);
            Fixed(plate, 36f, 36f);
            var plateSize = plate.gameObject.AddComponent<LayoutElement>();
            plateSize.minWidth = plateSize.preferredWidth = 36f;
            var plateImage = plate.gameObject.AddComponent<Image>();
            plateImage.sprite = p.Box;
            plateImage.type = Image.Type.Sliced;
            plateImage.color = plateColour;
            plateImage.raycastTarget = false;

            var markImage = Stretched("Mark", plate, 8f).gameObject.AddComponent<Image>();
            markImage.sprite = mark;
            markImage.color = Ink;
            markImage.raycastTarget = false;

            var label = Text(row, "Label", name, p.Poppins400, 18f, InkSoft, TextAlignmentOptions.MidlineLeft);
            Fixed(label.rectTransform, 0f, 30f);

            var button = face.gameObject.AddComponent<Button>();
            button.targetGraphic = face;
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState
            {
                highlightedSprite = p.GhostHover,
                pressedSprite = p.GhostPressed,
                selectedSprite = p.GhostIdle,
                disabledSprite = p.Disabled,
            };

            return button;
        }

        /// <summary>"New here? Create an account" - the second half is the link.</summary>
        static void Footer(RectTransform card, string lead, string link, float top, Parts p, out Button button)
        {
            var row = Rect("Footer", card);
            Place(row, Inset, top, Content, 30f);
            SizedByContent(row.gameObject.AddComponent<HorizontalLayoutGroup>(), 6f);

            var leading = Text(row, "Lead", lead, p.Poppins400, 17f, InkMute, TextAlignmentOptions.MidlineLeft);
            Fixed(leading.rectTransform, 0f, 30f);

            var linkText = Text(row, "Link", link, p.Poppins400, 17f, Signal, TextAlignmentOptions.MidlineLeft);
            Fixed(linkText.rectTransform, 0f, 30f);
            linkText.raycastTarget = true;

            button = linkText.gameObject.AddComponent<Button>();
            button.targetGraphic = linkText;
        }

        static Button Link(RectTransform card, string name, string words, TextAlignmentOptions alignment,
            float left, float top, float width, float height, Parts p)
        {
            // Only as wide as its words, pinned to the right, so the check box beside it stays clickable.
            var text = Label(card, name, words, left, top, width, height, p.Poppins400, 17f, Signal);
            text.alignment = alignment;
            text.rectTransform.pivot = new Vector2(1f, 1f);
            text.rectTransform.anchoredPosition = new Vector2(left + width, -top);
            text.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            text.raycastTarget = true;

            var button = text.gameObject.AddComponent<Button>();
            button.targetGraphic = text;

            return button;
        }

        // ── Layout helpers ────────────────────────────────────────────────────────────────

        static TMP_Text Label(RectTransform parent, string name, string words, float left, float top,
            float width, float height, TMP_FontAsset font, float size, Color colour)
        {
            var text = Text(parent, name, words, font, size, colour, TextAlignmentOptions.MidlineLeft);
            Place(text.rectTransform, left, top, width, height);
            return text;
        }

        static TextMeshProUGUI Text(RectTransform parent, string name, string words, TMP_FontAsset font,
            float size, Color colour, TextAlignmentOptions alignment)
        {
            var rect = Stretched(name, parent);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = size;
            text.color = colour;
            text.alignment = alignment;
            text.text = words;
            text.richText = true;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            return text;
        }

        static Image Sprite(RectTransform parent, string name, Sprite sprite, float left, float top, float width, float height)
        {
            var rect = Rect(name, parent);
            Place(rect, left, top, width, height);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>Top-left anchored, measured down from the parent's top edge, as the mock-ups are.</summary>
        static void Place(RectTransform rect, float left, float top, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(left, -top);
            rect.sizeDelta = new Vector2(width, height);
        }

        /// <summary>
        /// A centred row whose children are as wide as they ask to be - a label as wide as its words.
        /// </summary>
        /// <remarks>
        /// Widths are left to the layout, never measured here. Text on an object that has never
        /// been active measures a tenth of its size (TextMeshPro only learns it is UI text when the
        /// object first wakes), and the Create account screen is saved hidden - measuring at build
        /// time baked those tiny widths in and stacked each row on its centre. The layout asks again
        /// whenever the screen is shown, by which time the answer is right.
        /// </remarks>
        static void SizedByContent(HorizontalLayoutGroup layout, float spacing)
        {
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        }

        /// <summary>A fixed size about its own centre, for a child a layout group positions.</summary>
        static void Fixed(RectTransform rect, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
        }

        static RectTransform Stretched(string name, RectTransform parent, float inset = 0f)
        {
            var rect = Rect(name, parent);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
            return rect;
        }

        static RectTransform Rect(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        static void Wire(UnityEngine.Object target, params (string field, UnityEngine.Object value)[] references)
        {
            var so = new SerializedObject(target);

            foreach (var (field, value) in references)
            {
                var property = so.FindProperty(field);
                if (property == null)
                {
                    Debug.LogError("[Account] " + target.GetType().Name + " has no field '" + field + "'.");
                    continue;
                }

                property.objectReferenceValue = value;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var colour);
            return colour;
        }

        // ── Sprites and fonts ─────────────────────────────────────────────────────────────

        /// <summary>Everything the cards are made of, loaded or generated once per build.</summary>
        sealed class Parts
        {
            public readonly Sprite Panel = KitSprite("panel/dojo_panel_statcard_9slice_idle_base");
            public readonly Sprite FieldIdle = KitSprite("field/dojo_field_text_9slice_idle_base");
            public readonly Sprite FieldFocused = KitSprite("field/dojo_field_text_9slice_focused_base");
            public readonly Sprite FieldGlow = KitSprite("field/dojo_field_text_9slice_focused_glow");
            public readonly Sprite PrimaryIdle = KitSprite("btn/dojo_btn_primary_9slice_idle_base");
            public readonly Sprite PrimaryHover = KitSprite("btn/dojo_btn_primary_9slice_hover_base");
            public readonly Sprite PrimaryPressed = KitSprite("btn/dojo_btn_primary_9slice_pressed_base");
            public readonly Sprite PrimaryGlow = KitSprite("btn/dojo_btn_primary_9slice_idle_glow");
            public readonly Sprite GhostIdle = KitSprite("btn/dojo_btn_ghost_9slice_idle_base");
            public readonly Sprite GhostHover = KitSprite("btn/dojo_btn_ghost_9slice_hover_base");
            public readonly Sprite GhostPressed = KitSprite("btn/dojo_btn_ghost_9slice_pressed_base");
            public readonly Sprite Disabled = KitSprite("btn/dojo_btn_disabled_9slice_idle_base");
            public readonly Sprite Diamond = KitSprite("deco/dojo_deco_diamond_ink_idle_base");

            public readonly Sprite Mail = KitStandIns.Get("account", "mail");
            public readonly Sprite Lock = KitStandIns.Get("account", "lock");
            public readonly Sprite Eye = KitStandIns.Get("account", "eye");
            public readonly Sprite User = KitStandIns.Get("account", "user");
            public readonly Sprite Check = KitStandIns.Get("account", "check");
            public readonly Sprite Google = KitStandIns.Get("account", "google");
            public readonly Sprite Apple = KitStandIns.Get("account", "apple");
            public readonly Sprite Box = KitStandIns.Get("account", "box");
            public readonly Sprite BoxEdge = KitStandIns.Get("account", "boxedge");
            public readonly Sprite Corner = KitStandIns.Get("account", "corner");

            public readonly TMP_FontAsset Poppins400 = Font("Poppins-Regular SDF");
            public readonly TMP_FontAsset Poppins600 = Font("Poppins-SemiBold SDF");
            public readonly TMP_FontAsset Mono500 = Font("JetBrainsMono-Medium SDF");
        }

        static Sprite KitSprite(string name)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Kit + name + "@3x.png");
            if (sprite == null)
            {
                Debug.LogError("[Account] The UI kit has no " + name + ".");
            }

            return sprite;
        }

        static TMP_FontAsset Font(string name)
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Fonts + name + ".asset");
            if (font == null)
            {
                Debug.LogError("[Account] Missing font " + name + ".");
            }

            return font;
        }
    }
}
