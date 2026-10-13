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
    /// The bar shown while a room is being made: what is going on, and SAVE ROOM, LOAD ROOM and
    /// CANCEL.
    /// </summary>
    /// <remarks>
    /// Building itself is the inventory's, exactly as in the world; this bar only starts and ends
    /// things. Saving asks for a name through the same save dialog worlds use - which will not take
    /// an empty one and says OVERWRITE over a room already made - and refuses outright while nothing
    /// is built. Loading lists every room, the pack's too, as somewhere to start from.
    /// </remarks>
    public sealed class RoomEditorBar : MonoBehaviour
    {
        [SerializeField] GameObject root;
        [SerializeField] TMP_Text statusLabel;
        [SerializeField] Button saveButton;
        [SerializeField] Button loadButton;
        [SerializeField] Button cancelButton;

        [Tooltip("Left showing while a room is being made: the line of controls at the top of the " +
                 "screen. Everything else beside this bar is put away.")]
        [SerializeField] string keepShowing = "ControlsHintBar";

        RoomEditor editor;
        RoomCatalog catalog;
        IDialogManager dialogs;

        // What this bar put away when a room started being made, to bring back when it ends.
        readonly List<GameObject> putAway = new List<GameObject>();
        bool focused;

        [Inject]
        public void Construct(RoomEditor roomEditor, RoomCatalog roomCatalog, IDialogManager dialogManager)
        {
            editor = roomEditor;
            catalog = roomCatalog;
            dialogs = dialogManager;

            editor.Changed += Refresh;
            Refresh();
        }

        void Awake()
        {
            if (root == null)
            {
                root = gameObject;
            }

            if (saveButton != null) saveButton.onClick.AddListener(OnSave);
            if (loadButton != null) loadButton.onClick.AddListener(OnLoad);
            if (cancelButton != null) cancelButton.onClick.AddListener(OnCancel);

            Refresh();
        }

        void OnDestroy()
        {
            if (editor != null)
            {
                editor.Changed -= Refresh;
            }
        }

        /// <summary>Shown only while a room is being made, with everything else put away.</summary>
        void Refresh()
        {
            var editing = editor != null && editor.IsEditing;

            if (root != null && root.activeSelf != editing)
            {
                root.SetActive(editing);
            }

            Focus(editing);

            if (editing)
            {
                Say(string.IsNullOrEmpty(editor.EditingName)
                    ? "Build a room with the inventory, then save it."
                    : "Editing '" + editor.EditingName + "'. Save it under its name to replace it.");
            }
        }

        void OnSave()
        {
            if (editor == null || dialogs == null)
            {
                return;
            }

            if (editor.IsEmpty)
            {
                Say("Nothing has been built yet, so there is nothing to save.");
                return;
            }

            var made = new List<string>();
            foreach (var room in catalog.Custom.All())
            {
                made.Add(room.name);
            }

            dialogs.SaveAs("Save room", made, name =>
            {
                string problem;

                if (!editor.Save(name, out problem))
                {
                    Say(problem);
                }
            });
        }

        void OnLoad()
        {
            if (editor == null || dialogs == null)
            {
                return;
            }

            var rooms = catalog.All();
            var names = new List<string>();

            foreach (var room in rooms)
            {
                names.Add(room.name);
            }

            if (names.Count == 0)
            {
                Say("There are no rooms to load.");
                return;
            }

            dialogs.Choose("Load room", names, chosen =>
            {
                var entry = rooms.Find(r => r.name == chosen);
                string problem = null;

                if (entry == null || !editor.Load(entry, out problem))
                {
                    Say(entry == null ? "That room is gone." : problem);
                    return;
                }

                Refresh();
            });
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

            dialogs.Confirm("Leave without saving? What has been built here is thrown away, and the world comes back as it was.",
                editor.Cancel);
        }

        /// <summary>
        /// While a room is being made: only this bar, the inventory - down to the tabs a room is
        /// built from - and the controls line. The menu, the floor switcher, the elevator panel and
        /// the area markers are put away, and come back as they were.
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

                        if (go == root || go == gameObject || !go.activeSelf || go.name == keepShowing
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
                    inventory.SetRoomEditing(true);
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
                inventory.SetRoomEditing(false);
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
