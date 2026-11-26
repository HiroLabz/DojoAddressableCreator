using System.Collections.Generic;
using Dojo.Framework.UI;
using Dojo.Game.Placement;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace Dojo.Game.UI
{
    /// <summary>
    /// The bar shown while a floor plan is being made: what is going on, SIZE for the frame,
    /// CLEAR, SAVE FLOOR PLAN and CANCEL. <see cref="RoomEditorBar"/> for floors (ClickUp 14ze67xgf0v).
    /// "Floor plan" is what the designer reads; the code keeps "layout" (Bob and Jacob, 26 Nov).
    /// </summary>
    /// <remarks>
    /// Laying things out is the inventory's, exactly as in the world, the Rooms tab included; this
    /// bar only starts and ends things. SIZE opens a strip with the frame's columns and rows, each
    /// stepped by one between 4 and 24. Saving asks for a name through the same save dialog rooms
    /// use, and when pieces stand outside the frame, asks once more before dropping them.
    /// </remarks>
    public sealed class FloorLayoutEditorBar : MonoBehaviour
    {
        [SerializeField] GameObject root;
        [SerializeField] TMP_Text statusLabel;
        [SerializeField] Button saveButton;
        [SerializeField] Button cancelButton;

        [Tooltip("Shows and hides the size strip.")]
        [SerializeField] Button sizeButton;

        [Tooltip("Takes everything off the floor, after asking.")]
        [SerializeField] Button clearButton;

        [Header("Size strip")]
        [Tooltip("The strip with the frame's columns and rows, shown by SIZE.")]
        [SerializeField] GameObject sizeStrip;

        [Tooltip("Reads the frame's size: '8 × 6 tiles'.")]
        [SerializeField] TMP_Text sizeLabel;

        [SerializeField] Button columnsDown;
        [SerializeField] Button columnsUp;
        [SerializeField] Button rowsDown;
        [SerializeField] Button rowsUp;

        [Tooltip("Left showing while a layout is being made, by name: the line of controls at the top " +
                 "of the screen, and the view cube, for looking at the floor from every side. " +
                 "Everything else beside this bar is put away.")]
        [SerializeField] string[] keepShowingNames = { "ControlsHintBar", "ViewCube" };

        FloorLayoutEditor editor;
        FloorLayoutCatalog catalog;
        IDialogManager dialogs;

        /// <summary>The styled popups the dialogs ask with, as the room bar's.</summary>
        Dojo.Game.Managers.PopupPrefabs popups;

        // What this bar put away when a layout started being made, to bring back when it ends.
        readonly List<GameObject> putAway = new List<GameObject>();
        bool focused;

        [Inject]
        public void Construct(FloorLayoutEditor floorEditor, FloorLayoutCatalog floorCatalog, IDialogManager dialogManager,
            Dojo.Game.Managers.PopupPrefabs popupPrefabs)
        {
            editor = floorEditor;
            catalog = floorCatalog;
            dialogs = dialogManager;
            popups = popupPrefabs;

            editor.Changed += Refresh;
            editor.SizeChanged += RefreshSize;
            Refresh();
        }

        void Awake()
        {
            if (root == null)
            {
                root = gameObject;
            }

            if (saveButton != null) saveButton.onClick.AddListener(OnSave);
            if (cancelButton != null) cancelButton.onClick.AddListener(OnCancel);
            if (sizeButton != null) sizeButton.onClick.AddListener(OnSize);
            if (clearButton != null) clearButton.onClick.AddListener(OnClear);
            if (columnsDown != null) columnsDown.onClick.AddListener(() => Step(-1, 0));
            if (columnsUp != null) columnsUp.onClick.AddListener(() => Step(1, 0));
            if (rowsDown != null) rowsDown.onClick.AddListener(() => Step(0, -1));
            if (rowsUp != null) rowsUp.onClick.AddListener(() => Step(0, 1));

            if (sizeStrip != null)
            {
                sizeStrip.SetActive(false);
            }

            Refresh();
        }

        void OnDestroy()
        {
            if (editor != null)
            {
                editor.Changed -= Refresh;
                editor.SizeChanged -= RefreshSize;
            }
        }

        /// <summary>The frame follows what is laid: measured again each frame while a plan is being made.</summary>
        void Update()
        {
            if (editor != null && editor.IsEditing)
            {
                editor.RefreshFrame();
            }
        }

        /// <summary>Shown only while a layout is being made, with everything else put away.</summary>
        void Refresh()
        {
            var editing = editor != null && editor.IsEditing;

            if (root != null && root.activeSelf != editing)
            {
                root.SetActive(editing);
            }

            Focus(editing);

            if (!editing && sizeStrip != null)
            {
                sizeStrip.SetActive(false);
            }

            if (editing)
            {
                Say(string.IsNullOrEmpty(editor.EditingName)
                    ? "Lay out a whole floor with the inventory - tiles, walls, rooms and pieces - inside the frame, then save it as a floor plan."
                    : "Editing the floor plan '" + editor.EditingName + "'. Save it under its name to replace it.");
                RefreshSize();
            }
        }

        void RefreshSize()
        {
            if (editor == null)
            {
                return;
            }

            if (sizeLabel != null)
            {
                sizeLabel.text = editor.Columns + " × " + editor.Rows + " tiles";
            }

            if (columnsDown != null) columnsDown.interactable = editor.Columns > FloorLayoutEditor.MinSize;
            if (columnsUp != null) columnsUp.interactable = editor.Columns < FloorLayoutEditor.MaxSize;
            if (rowsDown != null) rowsDown.interactable = editor.Rows > FloorLayoutEditor.MinSize;
            if (rowsUp != null) rowsUp.interactable = editor.Rows < FloorLayoutEditor.MaxSize;
        }

        void OnSize()
        {
            if (sizeStrip != null)
            {
                sizeStrip.SetActive(!sizeStrip.activeSelf);
            }

            RefreshSize();
        }

        void Step(int columns, int rows)
        {
            if (editor != null)
            {
                editor.Resize(editor.Columns + columns, editor.Rows + rows);
            }
        }

        void OnClear()
        {
            if (editor == null)
            {
                return;
            }

            if (editor.IsEmpty)
            {
                Say("The floor is already empty.");
                return;
            }

            if (dialogs == null)
            {
                editor.Clear();
                return;
            }

            PopupAsk.Confirm(popups, dialogs, "Clear the floor?",
                "Everything laid here is taken off. The frame keeps its size.", "CLEAR",
                MessagePopup.Tone.Alert, editor.Clear);
        }

        void OnSave()
        {
            if (editor == null || dialogs == null)
            {
                return;
            }

            if (editor.IsEmpty)
            {
                Say("Nothing has been laid yet, so there is nothing to save.");
                return;
            }

            var made = new List<string>();
            foreach (var layout in catalog.Custom.All())
            {
                made.Add(layout.name);
            }

            // The layout being edited too, so its own name reads OVERWRITE - one of the pack's included.
            if (!string.IsNullOrEmpty(editor.EditingName) && !made.Contains(editor.EditingName))
            {
                made.Add(editor.EditingName);
            }

            PopupAsk.SaveAs(popups, dialogs, "Save floor plan", "What is this floor plan called?", made,
                editor.EditingName ?? string.Empty, null, name =>
            {
                var outside = editor.CountOutside();

                if (outside == 0)
                {
                    SaveAs(name, false);
                    return;
                }

                PopupAsk.Confirm(popups, dialogs, outside + (outside == 1 ? " piece is" : " pieces are") + " outside the frame",
                    "They stand outside the " + editor.Columns + " × " + editor.Rows + " frame and will be dropped from the floor plan. "
                    + "The rest is saved as '" + name.Trim() + "'.", "SAVE", MessagePopup.Tone.Alert, () => SaveAs(name, true));
            });
        }

        void SaveAs(string name, bool dropOutside)
        {
            string problem;

            if (editor.Save(name, dropOutside, out problem))
            {
                return;
            }

            // Said in a popup as well as on the bar, and the name asked for again, so a taken name
            // is retyped rather than hunted for (Jacob, 26 Nov; placeholder wording for now).
            Say(problem);

            if (dialogs != null)
            {
                PopupAsk.Confirm(popups, dialogs, "Not saved", problem, "OK", MessagePopup.Tone.Alert, OnSave);
            }
        }

        void OnCancel()
        {
            if (editor == null)
            {
                return;
            }

            if (editor.IsEmpty || dialogs == null)
            {
                editor.Cancel();
                return;
            }

            PopupAsk.Confirm(popups, dialogs, "Leave without saving?",
                "What has been laid here is thrown away, and the world comes back as it was.", "LEAVE",
                MessagePopup.Tone.Alert, editor.Cancel);
        }

        /// <summary>
        /// While a layout is being made: only this bar, the inventory - down to the tabs a floor is
        /// laid from, the Rooms tab included - and the controls line. The menu, the floor switcher,
        /// the elevator panel and the area markers are put away, and come back as they were.
        /// </summary>
        void Focus(bool editing)
        {
            if (editing == focused)
            {
                return;
            }

            focused = editing;

            var parent = root != null ? root.transform.parent : transform.parent;
            var inventory = parent != null ? parent.GetComponentInChildren<InventoryScreen>(true) : null;

            if (editing)
            {
                putAway.Clear();

                if (parent != null)
                {
                    foreach (Transform sibling in parent)
                    {
                        var go = sibling.gameObject;

                        if (go == root || go == gameObject || !go.activeSelf
                            || System.Array.IndexOf(keepShowingNames, go.name) >= 0
                            || (inventory != null && go == inventory.gameObject))
                        {
                            continue;
                        }

                        go.SetActive(false);
                        putAway.Add(go);
                    }
                }

                if (inventory != null)
                {
                    inventory.SetFloorEditing(true);
                }

                return;
            }

            foreach (var go in putAway)
            {
                if (go != null)
                {
                    go.SetActive(true);
                }
            }

            putAway.Clear();

            if (inventory != null)
            {
                inventory.SetFloorEditing(false);
            }
        }

        void Say(string text)
        {
            if (statusLabel != null)
            {
                statusLabel.text = text;
            }
        }
    }
}
