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
    /// The bar shown while a room is being made: what is going on, ENTRANCE and EXIT for marking
    /// its doors, and SAVE ROOM, LOAD ROOM and CANCEL.
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

        [Tooltip("Takes up the opening tool to make entrances: see RoomOpeningTool.")]
        [SerializeField] Button entranceButton;

        [Tooltip("Takes up the opening tool to make exits.")]
        [SerializeField] Button exitButton;

        [Tooltip("Left showing while a room is being made, by name: the line of controls at the top " +
                 "of the screen, and the view cube, for looking at the room from every side. " +
                 "Everything else beside this bar is put away.")]
        [SerializeField] string[] keepShowingNames = { "ControlsHintBar", "ViewCube" };

        RoomEditor editor;
        RoomCatalog catalog;
        IDialogManager dialogs;
        RoomOpeningTool openings;

        // What this bar put away when a room started being made, to bring back when it ends.
        readonly List<GameObject> putAway = new List<GameObject>();
        bool focused;

        [Inject]
        public void Construct(RoomEditor roomEditor, RoomCatalog roomCatalog, IDialogManager dialogManager,
            PlacementController placementController)
        {
            editor = roomEditor;
            catalog = roomCatalog;
            dialogs = dialogManager;

            // Here rather than in Awake: the bar starts switched off, so Awake waits for the first
            // room, and the tool has to be listening before then.
            openings = GetComponent<RoomOpeningTool>();
            if (openings == null)
            {
                openings = gameObject.AddComponent<RoomOpeningTool>();
            }

            openings.Init(editor, placementController);
            openings.Said += Say;
            openings.ModeChanged += Refresh;

            editor.Changed += Refresh;
            editor.Refused += Say;
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
            if (entranceButton != null) entranceButton.onClick.AddListener(() => TakeUp(RoomOpenings.Entrance));
            if (exitButton != null) exitButton.onClick.AddListener(() => TakeUp(RoomOpenings.Exit));

            Refresh();
        }

        void OnDestroy()
        {
            if (editor != null)
            {
                editor.Changed -= Refresh;
                editor.Refused -= Say;
            }

            if (openings != null)
            {
                openings.Said -= Say;
                openings.ModeChanged -= Refresh;
            }
        }

        void TakeUp(string kind)
        {
            if (openings != null)
            {
                openings.Toggle(kind);
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

            var mode = editing && openings != null ? openings.Mode : null;
            Highlight(entranceButton, mode == RoomOpenings.Entrance);
            Highlight(exitButton, mode == RoomOpenings.Exit);

            if (editing && openings != null && openings.Mode != null)
            {
                Say("Click a wall or amber gap to make " + (openings.Mode == RoomOpenings.Entrance ? Legend.Entrance : Legend.Exit)
                    + ". Click an opening to take it off or switch it, drag it to move it. Esc to stop.");
            }
            else if (editing)
            {
                Say((string.IsNullOrEmpty(editor.EditingName)
                    ? "Build a room, mark its one entrance and one exit, then save it. "
                    : "Editing '" + editor.EditingName + "'. Save it under its name to replace it. ")
                    + Legend.Entrance + ": arrow out. " + Legend.Exit + ": arrow in.");
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

            // Before a name is asked for, so a room that cannot be saved says why straight away.
            string openingProblem;
            if (!editor.CheckOpenings(out openingProblem))
            {
                Say(openingProblem);
                return;
            }

            if (openings != null)
            {
                openings.Stop();
            }

            var made = new List<string>();
            foreach (var room in catalog.Custom.All())
            {
                made.Add(room.name);
            }

            // The room being edited too, so its own name reads OVERWRITE - one of the pack's included.
            if (!string.IsNullOrEmpty(editor.EditingName) && !made.Contains(editor.EditingName))
            {
                made.Add(editor.EditingName);
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

            if (openings != null)
            {
                openings.Stop();
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

        /// <summary>The kinds' names in the colours the opening tool draws them in, for the status line.</summary>
        static class Legend
        {
            public static readonly string Entrance = "<color=#" + ColorUtility.ToHtmlStringRGB(RoomOpeningTool.EntranceColour) + "><b>ENTRANCE</b></color>";
            public static readonly string Exit = "<color=#" + ColorUtility.ToHtmlStringRGB(RoomOpeningTool.ExitColour) + "><b>EXIT</b></color>";
        }

        /// <summary>Gold: what an opening button is lit with while its tool is in hand.</summary>
        static readonly Color Lit = new Color(1f, 0.78f, 0.2f);

        /// <summary>
        /// Lights an opening button gold while its tool is in hand, and puts it back when it is not.
        /// The label stays white, bold while lit.
        /// </summary>
        static void Highlight(Button button, bool on)
        {
            if (button == null)
            {
                return;
            }

            var face = button.targetGraphic;
            if (face != null)
            {
                face.color = on ? Lit : Color.white;
            }

            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.color = Color.white;
                label.fontStyle = on ? FontStyles.Bold : FontStyles.Normal;
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
