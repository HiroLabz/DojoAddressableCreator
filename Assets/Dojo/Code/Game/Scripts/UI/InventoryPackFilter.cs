using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Dojo.Game.UI
{
    /// <summary>
    /// The pack control in the inventory header: shows which pack the grid is showing, and opens a
    /// menu to change it to another pack or to all of them.
    /// </summary>
    /// <remarks>
    /// This replaced a static caption reading "DEFAULT PACK - 1 LOCKED". A caption is text and a
    /// control needs a hit target, which is what makes the header taller; the locked count moved
    /// out beside the trigger rather than being dropped, so nothing the caption said was lost.
    /// <para>
    /// The filter is handed its packs and their counts — see <see cref="SetPacks"/> — rather than
    /// reading the catalogue itself. It therefore has no opinion about what a pack is, cannot
    /// disagree with the grid about how many items are in one, and can be shown a set of packs in a
    /// test with no container behind it.
    /// </para>
    /// <para>
    /// Rows are pooled rather than rebuilt. The menu is refilled on every category change, and
    /// destroying a handful of rows each time is work on a frame already laying out a grid.
    /// </para>
    /// </remarks>
    public sealed class InventoryPackFilter : MonoBehaviour
    {
        /// <summary>A pack the filter can be set to, and how much is in it.</summary>
        public readonly struct PackEntry
        {
            /// <summary>Pack key, or empty for the all-packs row.</summary>
            public readonly string Key;

            /// <summary>What the row reads, e.g. DEFAULT PACK.</summary>
            public readonly string Label;

            /// <summary>How many items the player holds in it.</summary>
            public readonly int Count;

            public PackEntry(string key, string label, int count)
            {
                Key = key ?? string.Empty;
                Label = label ?? string.Empty;
                Count = count;
            }
        }

        /// <summary>The key meaning do not filter by pack. Empty, so an unset field means all.</summary>
        public const string AllPacks = "";

        /// <summary>What the all-packs row reads.</summary>
        const string AllPacksLabel = "ALL PACKS";

        [Header("Trigger")]
        [Tooltip("Opens and closes the menu.")]
        [SerializeField] Button triggerButton;

        [Tooltip("Name of the pack currently showing.")]
        [SerializeField] TextMeshProUGUI triggerLabel;

        [Tooltip("Swapped for the open sprite while the menu is down.")]
        [SerializeField] Image triggerBackground;

        [SerializeField] Sprite triggerIdleSprite;
        [SerializeField] Sprite triggerOpenSprite;

        [Tooltip("Points down while closed and up while open. Rotated rather than swapped.")]
        [SerializeField] RectTransform chevron;

        [Header("Menu")]
        [Tooltip("The panel that drops below the trigger. Hidden while closed.")]
        [SerializeField] GameObject menuPanel;

        [Tooltip("Parent the rows are created under.")]
        [SerializeField] Transform menuContent;

        [Tooltip("One row, instantiated per pack.")]
        [SerializeField] InventoryPackRow rowPrefab;

        [Header("Locked")]
        [Tooltip("The locked caption beside the trigger. Hidden while nothing is locked, which is " +
                 "always until a source of unowned packs exists.")]
        [SerializeField] GameObject lockedCaption;

        [SerializeField] TextMeshProUGUI lockedLabel;

        /// <summary>Raised with the pack key chosen, or <see cref="AllPacks"/>.</summary>
        public event Action<string> PackSelected;

        /// <summary>The pack being shown, or <see cref="AllPacks"/>.</summary>
        public string Selected { get; private set; } = AllPacks;

        /// <summary>Whether the menu is down.</summary>
        public bool IsOpen { get; private set; }

        readonly List<InventoryPackRow> rows = new List<InventoryPackRow>();

        // Reused rather than allocated per press: RaycastAll fills a list, and a dropdown that
        // littered the heap every time anyone clicked anywhere would be a silly thing to ship.
        readonly List<RaycastResult> hits = new List<RaycastResult>();

        /// <summary>
        /// Shuts the menu when the player presses anywhere that is not the menu or its trigger.
        /// </summary>
        /// <remarks>
        /// Polled on press rather than handled by a full-screen blocker behind the panel. A blocker
        /// is the usual trick and it has two costs here: it must be authored into the prefab at
        /// exactly the right depth, and while it is up it swallows the very clicks — on a category,
        /// on an item — that the player most likely meant. This closes the menu and lets the click
        /// through to whatever it was aimed at.
        /// <para>
        /// On <em>press</em>, not release, because that is when a menu should get out of the way.
        /// The trigger's own button fires on release, so a press on the trigger is ignored here and
        /// the release that follows toggles it shut exactly once — checking the release instead
        /// would close it here and reopen it there.
        /// </para>
        /// </remarks>
        void Update()
        {
            if (!IsOpen)
            {
                return;
            }

            Pointer pointer = Pointer.current;

            if (pointer == null || !pointer.press.wasPressedThisFrame)
            {
                return;
            }

            if (IsPointerOverMenu(pointer.position.ReadValue()))
            {
                return;
            }

            SetOpen(false);
        }

        /// <summary>Whether a screen point lands on the trigger or anywhere inside the menu.</summary>
        bool IsPointerOverMenu(Vector2 screenPosition)
        {
            EventSystem events = EventSystem.current;

            if (events == null)
            {
                // No event system means no clicks are reaching anything anyway. Reporting "inside"
                // leaves the menu open rather than having it close on a press it never received.
                return true;
            }

            var data = new PointerEventData(events) { position = screenPosition };

            hits.Clear();
            events.RaycastAll(data, hits);

            for (int i = 0; i < hits.Count; i++)
            {
                Transform hit = hits[i].gameObject == null ? null : hits[i].gameObject.transform;

                if (hit == null)
                {
                    continue;
                }

                if (menuPanel != null && hit.IsChildOf(menuPanel.transform))
                {
                    return true;
                }

                if (triggerButton != null && hit.IsChildOf(triggerButton.transform))
                {
                    return true;
                }
            }

            return false;
        }

        void Awake()
        {
            if (triggerButton != null)
            {
                triggerButton.onClick.AddListener(Toggle);
            }

            SetOpen(false);
            SetLockedCount(0);
        }

        void OnDestroy()
        {
            if (triggerButton != null)
            {
                triggerButton.onClick.RemoveListener(Toggle);
            }
        }

        /// <summary>
        /// Rebuilds the menu. The all-packs row is added here rather than expected in the list, so
        /// a caller cannot forget it and leave the player unable to get back to everything.
        /// </summary>
        public void SetPacks(IReadOnlyList<PackEntry> packs, int totalCount)
        {
            EnsureRows((packs != null ? packs.Count : 0) + 1);

            if (rows.Count == 0)
            {
                return;
            }

            int used = 0;
            Fill(rows[used++], new PackEntry(AllPacks, AllPacksLabel, totalCount));

            if (packs != null)
            {
                for (int i = 0; i < packs.Count && used < rows.Count; i++)
                {
                    Fill(rows[used++], packs[i]);
                }
            }

            for (int i = used; i < rows.Count; i++)
            {
                rows[i].gameObject.SetActive(false);
            }

            // A pack can disappear between rebuilds — a category the player holds nothing from, or
            // content unloaded mid-session. Falling back to all packs keeps the grid showing
            // something rather than filtering to a pack that is no longer there.
            if (!string.IsNullOrEmpty(Selected) && IndexOf(Selected, packs) < 0)
            {
                Selected = AllPacks;
            }

            ApplySelection();
        }

        /// <summary>Sets the caption beside the trigger. Zero hides it.</summary>
        public void SetLockedCount(int locked)
        {
            if (lockedCaption != null)
            {
                lockedCaption.SetActive(locked > 0);
            }

            if (lockedLabel != null && locked > 0)
            {
                lockedLabel.text = locked + " LOCKED";
            }
        }

        /// <summary>Chooses a pack, closes the menu and tells anyone listening.</summary>
        public void Select(string packKey)
        {
            Selected = packKey ?? AllPacks;
            ApplySelection();
            SetOpen(false);

            var handler = PackSelected;
            if (handler != null)
            {
                handler(Selected);
            }
        }

        /// <summary>Opens the menu if it is shut, shuts it if it is open.</summary>
        public void Toggle()
        {
            SetOpen(!IsOpen);
        }

        /// <summary>Drops the menu or puts it away.</summary>
        public void SetOpen(bool open)
        {
            IsOpen = open;

            if (menuPanel != null)
            {
                menuPanel.SetActive(open);
            }

            if (triggerBackground != null)
            {
                var sprite = open ? triggerOpenSprite : triggerIdleSprite;
                if (sprite != null)
                {
                    triggerBackground.sprite = sprite;
                }
            }

            if (chevron != null)
            {
                chevron.localRotation = Quaternion.Euler(0f, 0f, open ? 180f : 0f);
            }
        }

        void EnsureRows(int needed)
        {
            if (rowPrefab == null || menuContent == null)
            {
                return;
            }

            while (rows.Count < needed)
            {
                var row = Instantiate(rowPrefab, menuContent);
                row.Clicked += Select;
                rows.Add(row);
            }
        }

        void Fill(InventoryPackRow row, PackEntry entry)
        {
            row.gameObject.SetActive(true);
            row.Bind(entry);
        }

        void ApplySelection()
        {
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].SetSelected(
                    string.Equals(rows[i].PackKey, Selected, StringComparison.OrdinalIgnoreCase));
            }

            if (triggerLabel != null)
            {
                triggerLabel.text = LabelFor(Selected);
            }
        }

        string LabelFor(string packKey)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].gameObject.activeSelf &&
                    string.Equals(rows[i].PackKey, packKey, StringComparison.OrdinalIgnoreCase))
                {
                    return rows[i].Label;
                }
            }

            return AllPacksLabel;
        }

        static int IndexOf(string packKey, IReadOnlyList<PackEntry> packs)
        {
            if (packs == null)
            {
                return -1;
            }

            for (int i = 0; i < packs.Count; i++)
            {
                if (string.Equals(packs[i].Key, packKey, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
