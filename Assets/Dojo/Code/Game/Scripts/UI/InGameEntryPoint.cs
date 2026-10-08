using System;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Game.UI
{
    /// <summary>
    /// The in-game menu's one entry point: a single button in the bottom-left corner that raises
    /// and lowers the handful of things the player can open.
    /// </summary>
    /// <remarks>
    /// One button rather than four permanently on screen, because the floor is what the player came
    /// to look at. The entries tuck behind the toggle when closed and rise out of it when opened, so
    /// where they came from is never in question.
    /// <para>
    /// This owns the opening and closing only. What each entry <em>does</em> is someone else's
    /// business — see <see cref="EntrySelected"/> — so this stays useful as the set of entries
    /// changes.
    /// </para>
    /// </remarks>
    public sealed class InGameEntryPoint : MonoBehaviour
    {
        [Header("Toggle")]
        [Tooltip("The one button. Opens the entries, and closes them again.")]
        [SerializeField] Button toggleButton;

        [Tooltip("Lit plate shown only while the entries are out.")]
        [SerializeField] GameObject toggleLit;

        [Tooltip("Icon shown while closed.")]
        [SerializeField] GameObject iconClosed;

        [Tooltip("Icon shown while open.")]
        [SerializeField] GameObject iconOpen;

        [Header("Entries")]
        [Tooltip("In the order they sit on screen, bottom first. Each needs a CanvasGroup.")]
        [SerializeField] RectTransform[] entries = new RectTransform[0];

        [Tooltip("Buttons matching the entries, one for one. Used only to report which was pressed.")]
        [SerializeField] Button[] entryButtons = new Button[0];

        [Header("Motion")]
        [Tooltip("Seconds one entry takes to travel.")]
        [SerializeField] float duration = 0.28f;

        [Tooltip("Seconds between one entry starting and the next. Zero moves them as a block.")]
        [SerializeField] float stagger = 0.05f;

        [Header("Hover and press")]
        [Tooltip("The glow behind a plate while it is hovered or pressed: the kit's selected plate glow.")]
        [SerializeField] Sprite plateGlow;

        [Tooltip("The kit's plain plate face. A plate wearing it lights up to the one below while hovered.")]
        [SerializeField] Sprite plateIdle;

        [Tooltip("The kit's lit plate face, TAB's while open.")]
        [SerializeField] Sprite plateLit;

        [Tooltip("An entry's label while hovered.")]
        [SerializeField] Sprite labelHover;

        [Tooltip("An entry's label while pressed.")]
        [SerializeField] Sprite labelPressed;

        [SerializeField] Color glowTint = new Color(0.184f, 0.561f, 1f, 1f);    // #2F8FFF, the signal blue

        [Tooltip("The shop entry's glow, in its own pink like its plate.")]
        [SerializeField] Color shopGlowTint = new Color(0.91f, 0.384f, 0.478f, 1f);   // #E8627A

        /// <summary>Raised with the index of the entry the player pressed.</summary>
        public event Action<int> EntrySelected;

        /// <summary>
        /// Raised when the button is pressed. What the press should <em>do</em> is not decided here.
        /// </summary>
        /// <remarks>
        /// The button used to toggle the entries directly, which made the press mean one fixed
        /// thing. It does not: with a panel showing, the same press means "put that away" and must
        /// not raise the entries. Only something that knows what is currently on screen can tell
        /// those apart, so this reports the press and <see cref="InGameCanvasUI"/> decides.
        /// </remarks>
        public event Action TogglePressed;

        /// <summary>Whether the entries are currently out.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>The one button, for anything that needs to point at it - the tour does.</summary>
        public RectTransform ToggleRect => toggleButton != null ? (RectTransform)toggleButton.transform : null;

        /// <summary>The entry at <paramref name="index"/>, bottom first, or null.</summary>
        public RectTransform EntryAt(int index) => index >= 0 && index < entries.Length ? entries[index] : null;

        // Where each entry sits when open, read from how it was authored — so the layout stays a
        // thing you arrange in the scene rather than numbers buried in here.
        Vector2[] openPositions;
        CanvasGroup[] groups;

        void Awake()
        {
            Cache();

            if (toggleButton != null)
            {
                toggleButton.onClick.AddListener(RaiseTogglePressed);
            }

            for (int i = 0; i < entryButtons.Length; i++)
            {
                if (entryButtons[i] == null)
                {
                    continue;
                }

                int index = i;
                entryButtons[i].onClick.AddListener(() => OnEntryPressed(index));
            }

            AddGlows();
            ApplyClosedImmediate();
        }

        /// <summary>The rollover, hover and press on every button of the rail, TAB included.</summary>
        void AddGlows()
        {
            if (toggleButton != null)
            {
                Glow(toggleButton, glowTint);
            }

            foreach (var button in entryButtons)
            {
                if (button != null)
                {
                    // Told apart by name, as the shop's plate itself is: it is the one pink entry.
                    Glow(button, button.name == "Shop" ? shopGlowTint : glowTint);
                }
            }
        }

        void Glow(Button button, Color tint)
        {
            var glow = button.GetComponent<RailButtonGlow>();
            if (glow == null)
            {
                glow = button.gameObject.AddComponent<RailButtonGlow>();
            }

            glow.Setup(plateGlow, plateIdle, plateLit, labelHover, labelPressed, tint);
        }

        void OnDestroy()
        {
            if (toggleButton != null)
            {
                toggleButton.onClick.RemoveListener(RaiseTogglePressed);
            }

            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i] != null)
                {
                    LeanTween.cancel(entries[i].gameObject);
                }
            }
        }

        void Cache()
        {
            openPositions = new Vector2[entries.Length];
            groups = new CanvasGroup[entries.Length];

            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i] == null)
                {
                    continue;
                }

                openPositions[i] = entries[i].anchoredPosition;

                groups[i] = entries[i].GetComponent<CanvasGroup>();
                if (groups[i] == null)
                {
                    groups[i] = entries[i].gameObject.AddComponent<CanvasGroup>();
                }
            }
        }

        /// <summary>Opens the entries if they are down, closes them if they are up.</summary>
        public void Toggle()
        {
            SetOpen(!IsOpen);
        }

        /// <summary>
        /// Shuts the entries at once, with no travel.
        /// </summary>
        /// <remarks>
        /// For the moment an entry is chosen. The panel it opens appears immediately, and entries
        /// still sliding down in front of it read as the menu being slow to get out of the way
        /// rather than as an answer to the press.
        /// </remarks>
        public void CloseImmediate()
        {
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i] != null)
                {
                    // Cancelled first, or a slide already running would keep writing positions
                    // over the ones set below and the entries would drift back out.
                    LeanTween.cancel(entries[i].gameObject);
                }
            }

            ApplyClosedImmediate();
        }

        /// <summary>Raises or lowers the entries.</summary>
        public void SetOpen(bool open)
        {
            IsOpen = open;
            ApplyToggleLook();

            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i] == null)
                {
                    continue;
                }

                LeanTween.cancel(entries[i].gameObject);

                // Opening runs bottom-up and closing top-down, so the entry nearest the button is
                // always the first to leave it and the last to return.
                int order = open ? i : entries.Length - 1 - i;
                float delay = stagger * order;

                Vector2 target = open ? openPositions[i] : ClosedPosition(i);

                LeanTween.value(entries[i].gameObject, entries[i].anchoredPosition, target, duration)
                         .setDelay(delay)
                         .setEase(open ? LeanTweenType.easeOutCubic : LeanTweenType.easeInCubic)
                         .setOnUpdateVector2(SetPosition(i))
                         .setIgnoreTimeScale(true);

                if (groups[i] != null)
                {
                    LeanTween.alphaCanvas(groups[i], open ? 1f : 0f, duration)
                             .setDelay(delay)
                             .setIgnoreTimeScale(true);

                    // Only the raised entries take clicks. Without this a closed entry still
                    // answers the mouse from underneath the button that hid it.
                    groups[i].interactable = open;
                    groups[i].blocksRaycasts = open;
                }
            }
        }

        /// <summary>
        /// Where an entry rests when closed: tucked behind the toggle, so it appears to come out of
        /// the button rather than fading in somewhere above it.
        /// </summary>
        Vector2 ClosedPosition(int index)
        {
            if (toggleButton == null)
            {
                return openPositions[index];
            }

            var toggleRect = (RectTransform)toggleButton.transform;
            return new Vector2(openPositions[index].x, toggleRect.anchoredPosition.y);
        }

        // A method rather than a lambda capturing i, so every entry gets its own setter instead of
        // all three writing to whichever index the loop finished on.
        Action<Vector2> SetPosition(int index)
        {
            return value =>
            {
                if (entries[index] != null)
                {
                    entries[index].anchoredPosition = value;
                }
            };
        }

        void ApplyClosedImmediate()
        {
            IsOpen = false;
            ApplyToggleLook();

            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i] == null)
                {
                    continue;
                }

                entries[i].anchoredPosition = ClosedPosition(i);

                if (groups[i] != null)
                {
                    groups[i].alpha = 0f;
                    groups[i].interactable = false;
                    groups[i].blocksRaycasts = false;
                }
            }
        }

        void ApplyToggleLook()
        {
            if (toggleLit != null)
            {
                toggleLit.SetActive(IsOpen);
            }

            if (iconClosed != null)
            {
                iconClosed.SetActive(!IsOpen);
            }

            if (iconOpen != null)
            {
                iconOpen.SetActive(IsOpen);
            }
        }

        void OnEntryPressed(int index)
        {
            var handler = EntrySelected;
            if (handler != null)
            {
                handler(index);
            }
        }

        void RaiseTogglePressed()
        {
            var handler = TogglePressed;
            if (handler != null)
            {
                handler();
            }
        }
    }
}
