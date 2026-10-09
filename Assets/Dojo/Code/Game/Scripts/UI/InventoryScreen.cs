using System;
using Dojo.Game.Systems;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace Dojo.Game.UI
{
    /// <summary>
    /// The inventory as one screen: the rail on the left, the hud on the right, and whether either
    /// is on show.
    /// </summary>
    /// <remarks>
    /// The rail raises a category and the hud fills with it; neither holds a reference to the
    /// other. Joining them here is what keeps the pair swappable — a different rail, or a hud shown
    /// without one, is a change to this file rather than to either component.
    /// <para>
    /// Opening is driven from outside, by <see cref="InGameCanvasUI"/> answering the entry point.
    /// This screen has no toggle button of its own, which is deliberate: there is one place that
    /// decides what the in-game menu opens, and this is not it.
    /// </para>
    /// </remarks>
    public sealed class InventoryScreen : MonoBehaviour
    {
        [Header("Parts")]
        [Tooltip("The category buttons.")]
        [SerializeField] InventoryRail rail;

        [Tooltip("The panel the selected category fills.")]
        [SerializeField] InventoryHud hud;

        [Tooltip("Shown instead of the grid for the Agents tab. Left empty, that tab selects and " +
                 "says once that there is nothing behind it.")]
        [SerializeField] InventoryAgentsHud agentsHud;

        [Tooltip("The category that opens the agents panel instead of the item grid.")]
        [SerializeField] string agentsCategory = "agents";

        [Tooltip("The manager's card. One entry rather than a roster, so it has no list above it — " +
                 "the whole tab is the card.")]
        [SerializeField] ManagerDisplayUI youHud;

        [Tooltip("The category that opens the manager's card instead of the item grid.")]
        [SerializeField] string youCategory = "you";

        [Tooltip("The block areas of the floor being viewed: where every area is painted and named.")]
        [SerializeField] AreasDisplayUI areasHud;

        [Tooltip("The category that opens the areas panel instead of the item grid.")]
        [SerializeField] string areasCategory = "areas";

        [Tooltip("The rooms the loaded packs offer, each built into the world with one button.")]
        [SerializeField] RoomsDisplayUI roomsHud;

        [Tooltip("The category that opens the rooms panel instead of the item grid.")]
        [SerializeField] string roomsCategory = "rooms";

        [Header("Chrome")]
        [Tooltip("The X in the header. Closes the whole screen.")]
        [SerializeField] Button closeButton;

        [Tooltip("The root shown and hidden. Defaults to this object.")]
        [SerializeField] GameObject root;

        [Header("Opening")]
        [Tooltip("The canvas whose entry point opens this. Leave empty to drive it from code.")]
        [SerializeField] InGameCanvasUI canvasUI;

        [Tooltip("Category selected when the screen opens with nothing chosen. Empty opens on the " +
                 "rail alone, with no grid.")]
        [SerializeField] string defaultCategory = "floors";

        /// <summary>
        /// The phase decider, as the concrete service rather than <c>IGamePhase</c>.
        /// </summary>
        /// <remarks>
        /// The same arrangement <see cref="InventoryUI"/> uses, and for the same reason:
        /// <c>Enter</c> is not on the read-only interface, so the one screen allowed to change the
        /// phase is the one screen that has to name the service. Opening this screen is what "the
        /// player came to rearrange the room" means, and closing it is what ends it.
        /// <para>
        /// Null is a legitimate state. This screen is usable in a scene with no phase registered,
        /// and a missing service must leave a working inventory rather than a throw.
        /// </para>
        /// </remarks>
        GamePhaseService phase;

        [Inject]
        public void Construct(GamePhaseService phase)
        {
            this.phase = phase;
        }

        /// <summary>Takes the Agents tab, the You tab, or both off the rail, for characters that are switched off.</summary>
        public void HideCharacterTabs(bool agents, bool you)
        {
            if (rail == null)
            {
                return;
            }

            if (agents)
            {
                rail.Hide(agentsCategory);
            }

            if (you)
            {
                rail.Hide(youCategory);
            }
        }

        /// <summary>Whether the screen is on show.</summary>
        public bool IsOpen
        {
            get { return root != null && root.activeSelf; }
        }

        /// <summary>
        /// The object shown and hidden, defaulting to this one.
        /// </summary>
        /// <remarks>
        /// Resolved here as well as in <see cref="Awake"/> because <c>Awake</c> does not run in the
        /// editor outside play mode, and a test that opens this screen would otherwise be opening
        /// a null root.
        /// </remarks>
        GameObject Root
        {
            get { return root != null ? root : (root = gameObject); }
        }

        void Awake()
        {
            if (root == null)
            {
                root = gameObject;
            }

            if (rail != null)
            {
                rail.CategorySelected += OnCategorySelected;
            }

            // The screen listens for the entry rather than the canvas reaching in to open it. That
            // keeps the canvas ignorant of what any entry leads to, which is the whole reason it
            // reports entries as events instead of opening things itself.
            if (canvasUI != null)
            {
                canvasUI.InventoryRequested += Open;
                canvasUI.StateChanged += OnMenuStateChanged;
            }

            if (closeButton != null)
            {
                closeButton.onClick.AddListener(Close);
            }

            // Authored visible so the screen can be arranged, and shut on the first frame so the
            // game does not open onto it.
            root.SetActive(false);
        }

        void OnDestroy()
        {
            if (rail != null)
            {
                rail.CategorySelected -= OnCategorySelected;
            }

            if (canvasUI != null)
            {
                canvasUI.InventoryRequested -= Open;
                canvasUI.StateChanged -= OnMenuStateChanged;
            }

            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(Close);
            }
        }

        /// <summary>
        /// Opens the screen, on the last category or the default one — and with it the Edit phase.
        /// </summary>
        /// <remarks>
        /// The screen being open <em>is</em> the edit session, exactly as the older drawer's was:
        /// furniture becomes movable and deletable, everyone living in the room stands still, and
        /// the camera stops taking the shot back to the manager. There is no separate mode switch
        /// for the player to forget.
        /// </remarks>
        public void Open()
        {
            Root.SetActive(true);

            if (phase != null)
            {
                phase.Enter(GamePhase.Edit, "inventory opened");
            }

            if (rail != null && string.IsNullOrEmpty(rail.Selected) && !string.IsNullOrEmpty(defaultCategory))
            {
                rail.Select(defaultCategory);
            }

            // Said rather than assumed: until this, the menu has no reason to believe the entry it
            // raised led anywhere, and the next press of the button would open the entries again
            // on top of this screen.
            if (canvasUI != null)
            {
                canvasUI.NotifyPanelOpened();
            }
        }

        /// <summary>
        /// Shuts the screen and hands the room back to its occupants. The category is kept, so
        /// reopening returns to it.
        /// </summary>
        public void Close()
        {
            Root.SetActive(false);

            if (phase != null)
            {
                phase.Enter(GamePhase.Play, "inventory closed");
            }

            if (canvasUI != null)
            {
                canvasUI.NotifyPanelClosed();
            }
        }

        /// <summary>
        /// Shuts the screen whenever the menu is showing anything other than a panel.
        /// </summary>
        /// <remarks>
        /// The state is the single answer to "should this be up", so this does not need to work out
        /// which press caused the change. <see cref="Close"/> reports back, and the state guard
        /// there is what stops the two bouncing off each other.
        /// </remarks>
        void OnMenuStateChanged(InGameMenuState state)
        {
            if (state != InGameMenuState.Panel)
            {
                Close();
            }
        }

        /// <summary>Opens the screen if it is shut, shuts it if it is open.</summary>
        public void Toggle()
        {
            if (IsOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        /// <summary>
        /// Hands the category to whichever panel answers for it, and puts the other one away.
        /// </summary>
        /// <remarks>
        /// Both panels are shut before either is opened, so a tab switch cannot leave two showing.
        /// It also means the agents panel is dismissed rather than merely hidden, which is what
        /// abandons a paint session that would otherwise outlive the card explaining it.
        /// </remarks>
        void OnCategorySelected(string category)
        {
            bool agents = !string.IsNullOrEmpty(agentsCategory)
                && string.Equals(category, agentsCategory, StringComparison.OrdinalIgnoreCase);

            bool you = !string.IsNullOrEmpty(youCategory)
                && string.Equals(category, youCategory, StringComparison.OrdinalIgnoreCase);

            bool areas = !string.IsNullOrEmpty(areasCategory)
                && string.Equals(category, areasCategory, StringComparison.OrdinalIgnoreCase);

            bool rooms = !string.IsNullOrEmpty(roomsCategory)
                && string.Equals(category, roomsCategory, StringComparison.OrdinalIgnoreCase);

            if (roomsHud != null)
            {
                if (rooms)
                {
                    roomsHud.Show();
                }
                else
                {
                    roomsHud.Dismiss();
                }
            }
            else if (rooms)
            {
                Debug.LogWarning("[InventoryScreen] The Rooms tab has no panel behind it. "
                    + "Run Tools > Dojo > Add Rooms Tab.", this);
            }

            if (areasHud != null)
            {
                if (areas)
                {
                    areasHud.gameObject.SetActive(true);
                    areasHud.Show();
                }
                else
                {
                    // Dismissed rather than hidden: a painting left running would outlive the panel
                    // that says which area it is.
                    areasHud.Dismiss();
                    areasHud.gameObject.SetActive(false);
                }
            }
            else if (areas)
            {
                Debug.LogWarning("[InventoryScreen] The Areas tab has no panel behind it.", this);
            }

            if (youHud != null)
            {
                if (you)
                {
                    youHud.gameObject.SetActive(true);
                    youHud.Show();
                }
                else
                {
                    // Dismissed rather than hidden, for the reason the agents panel is: a paint
                    // session left running would outlive the card that says whose area it is.
                    youHud.Dismiss();
                    youHud.gameObject.SetActive(false);
                }
            }
            else if (you)
            {
                Debug.LogWarning("[InventoryScreen] The You tab has no panel behind it.", this);
            }

            if (agentsHud != null)
            {
                if (agents)
                {
                    agentsHud.gameObject.SetActive(true);
                    agentsHud.Show();
                }
                else
                {
                    agentsHud.Hide();
                    agentsHud.gameObject.SetActive(false);
                }
            }
            else if (agents)
            {
                Debug.LogWarning("[InventoryScreen] The Agents tab has no panel behind it.", this);
            }

            // The grid answers for every category that is not one of the special tabs.
            bool grid = !agents && !you && !areas && !rooms;

            if (hud != null)
            {
                hud.gameObject.SetActive(grid);

                if (grid)
                {
                    hud.Show(category);
                }
                else
                {
                    hud.Clear();
                }
            }
        }
    }
}
