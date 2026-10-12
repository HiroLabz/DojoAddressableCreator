using System;
using System.Collections.Generic;
using Dojo.Game.InGame.Controllers;
using UnityEngine;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// Making a room: a blank place to build one with the inventory, saving it as a room of the
    /// player's own, and going back to the world exactly as it was left.
    /// </summary>
    /// <remarks>
    /// <see cref="Begin"/> keeps the world in hand - every piece, room, area, agent and elevator,
    /// which floor was being looked at and where the camera was - and puts an empty floor in its
    /// place. While the room is being made, nothing is saved to the world file: not an auto save,
    /// not SAVE WORLD, since what is standing is the room. <see cref="Save"/> writes what was built
    /// on the ground floor as a room, centred on its own origin, with a thumbnail rendered there
    /// and then; <see cref="Cancel"/> writes nothing. Both end by building the kept world again.
    /// </remarks>
    public sealed class RoomEditor
    {
        readonly WorldService world;
        readonly Storeys storeys;
        readonly RoomCatalog catalog;
        readonly WorldSettings settings;

        WorldSave kept;
        string keptName;
        int keptFloor;
        readonly List<KeyValuePair<CameraRig, CameraRig.ViewState>> keptViews = new List<KeyValuePair<CameraRig, CameraRig.ViewState>>();

        /// <summary>Rooms move in whole grid cells, so a room centred on its origin keeps its pieces on their cells.</summary>
        const float Snap = 0.25f;

        public RoomEditor(WorldService world, Storeys storeys, RoomCatalog catalog, WorldSettings settings)
        {
            this.world = world;
            this.storeys = storeys;
            this.catalog = catalog;
            this.settings = settings;
        }

        /// <summary>True from <see cref="Begin"/> until the room is saved or cancelled.</summary>
        public bool IsEditing { get; private set; }

        /// <summary>The name of the room loaded to be edited, when one was: what saving suggests.</summary>
        public string EditingName { get; private set; }

        /// <summary>Raised when making a room starts or ends.</summary>
        public event Action Changed;

        /// <summary>Raised with the room just saved, once the world is back.</summary>
        public event Action<RoomEntry> Saved;

        /// <summary>
        /// Keeps the world and puts an empty floor in its place. False, having changed nothing, when
        /// the scene has no room bar - without its SAVE ROOM and CANCEL there would be no way back.
        /// </summary>
        public bool Begin()
        {
            if (IsEditing)
            {
                return true;
            }

            if (UnityEngine.Object.FindObjectsByType<Dojo.Game.UI.RoomEditorBar>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 0)
            {
                Debug.LogError("[Rooms] There is no room bar in the scene, so a room cannot be made: there would "
                    + "be no SAVE ROOM or CANCEL to get back to the world. Run Tools > Dojo > Add Rooms Tab and "
                    + "save the scene.");
                return false;
            }

            kept = world.Capture();
            keptName = world.OpenWorldName;
            keptFloor = storeys.Viewed;

            keptViews.Clear();
            foreach (var rig in Rigs())
            {
                keptViews.Add(new KeyValuePair<CameraRig, CameraRig.ViewState>(rig, rig.SaveView()));
            }

            // Before anything is replaced: an auto save already waiting would otherwise write the
            // empty floor over the world.
            world.SavingSuspended = true;
            IsEditing = true;
            EditingName = null;

            world.Show(Blank(), null);

            foreach (var rig in Rigs())
            {
                rig.ResetView();
            }

            Debug.Log("[Rooms] Making a room. '" + (keptName ?? "the open world") + "' is kept, "
                + kept.PieceCount() + " piece(s), and comes back on save or cancel.");

            Raise(Changed);
            return true;
        }

        /// <summary>True while nothing has been built: nothing to save as a room.</summary>
        public bool IsEmpty
        {
            get
            {
                foreach (var child in storeys.Placed())
                {
                    var placed = child.GetComponent<PlacedPiece>();

                    if (placed != null && placed.IsResolvable && child.GetComponent<FurniturePiece>() != null
                        && !ElevatorShafts.IsStop(child))
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>Puts a room in front of the player to be edited, in place of whatever is being built.</summary>
        public bool Load(RoomEntry entry, out string problem)
        {
            problem = null;

            if (!IsEditing)
            {
                problem = "Start making a room first.";
                return false;
            }

            var room = entry != null ? catalog.Read(entry) : null;

            if (room == null)
            {
                problem = "That room could not be read.";
                return false;
            }

            // Its pieces are what is being made now, not members of a placed room.
            foreach (var floor in room.floors)
            {
                foreach (var piece in floor.pieces)
                {
                    piece.room = string.Empty;
                }
            }

            room.rooms.Clear();

            world.Show(room, null);
            EditingName = entry.name;

            foreach (var rig in Rigs())
            {
                rig.ResetView();
            }

            Debug.Log("[Rooms] Editing '" + entry.name + "' - " + room.PieceCount() + " piece(s).");
            return true;
        }

        /// <summary>
        /// Saves what was built as a room called <paramref name="name"/> and goes back to the world.
        /// False, with <paramref name="problem"/> saying why, when there is no name or nothing built.
        /// </summary>
        public bool Save(string name, out string problem)
        {
            problem = null;

            if (!IsEditing)
            {
                problem = "There is no room being made.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                problem = "Give the room a name.";
                return false;
            }

            if (IsEmpty)
            {
                problem = "Nothing has been built yet, so there is no room to save.";
                return false;
            }

            var room = ToRoom(world.Capture());

            if (room.PieceCount() == 0)
            {
                problem = "Only what is on the ground floor is saved, and nothing is built there.";
                return false;
            }

            // Rendered now, while the room is the thing standing.
            var thumbnail = RoomThumbnailCamera.Render(storeys.Floor(0), LayerMask.NameToLayer(settings.PlacedLayerName));
            var address = catalog.Custom.Save(name.Trim(), room, thumbnail);

            Debug.Log("[Rooms] Saved room '" + name.Trim() + "' - " + room.PieceCount() + " piece(s), to "
                + CustomRooms.Folder + (thumbnail == null ? " (no thumbnail could be rendered)" : ""));

            End();

            var saved = Saved;
            if (saved != null)
            {
                saved(new RoomEntry { name = name.Trim(), address = address, icon = address });
            }

            return true;
        }

        /// <summary>Leaves without saving: the world comes back as it was.</summary>
        public void Cancel()
        {
            if (IsEditing)
            {
                Debug.Log("[Rooms] Room making cancelled; nothing was saved.");
                End();
            }
        }

        void End()
        {
            world.Show(kept, keptName);

            if (keptFloor > 0 && keptFloor < storeys.Count)
            {
                storeys.View(keptFloor);
            }

            world.SavingSuspended = false;

            // Last, so no floor change above can move the camera after it is put back.
            foreach (var pair in keptViews)
            {
                if (pair.Key != null)
                {
                    pair.Key.LoadView(pair.Value);
                }
            }

            keptViews.Clear();
            kept = null;
            keptName = null;
            IsEditing = false;
            EditingName = null;

            Raise(Changed);
        }

        /// <summary>
        /// What was built, as a room: the ground floor's pieces and areas, centred on the room's
        /// own origin in whole grid cells. Agents, managers, elevators and other floors are not
        /// part of a room.
        /// </summary>
        static WorldSave ToRoom(WorldSave built)
        {
            var room = new WorldSave();
            var ground = room.FloorAt(0);
            var source = built.floors.Find(f => f.index == 0);

            if (source == null)
            {
                return room;
            }

            foreach (var piece in source.pieces)
            {
                piece.room = string.Empty;
                ground.pieces.Add(piece);
            }

            ground.blockAreas.AddRange(source.blockAreas);

            if (ground.pieces.Count == 0)
            {
                return room;
            }

            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);

            foreach (var piece in ground.pieces)
            {
                min = Vector2.Min(min, new Vector2(piece.position.x, piece.position.z));
                max = Vector2.Max(max, new Vector2(piece.position.x, piece.position.z));
            }

            var centre = (min + max) * 0.5f;
            var shift = new Vector3(Mathf.Round(centre.x / Snap) * Snap, 0f, Mathf.Round(centre.y / Snap) * Snap);

            foreach (var piece in ground.pieces)
            {
                piece.position -= shift;
            }

            foreach (var area in ground.blockAreas)
            {
                for (var i = 0; i < area.area.Count; i++)
                {
                    area.area[i] -= shift;
                }
            }

            return room;
        }

        static WorldSave Blank()
        {
            var blank = new WorldSave { name = "a new room" };
            blank.FloorAt(0);
            return blank;
        }

        static CameraRig[] Rigs() => UnityEngine.Object.FindObjectsByType<CameraRig>(FindObjectsSortMode.None);

        static void Raise(Action handler)
        {
            if (handler != null)
            {
                handler();
            }
        }
    }
}
