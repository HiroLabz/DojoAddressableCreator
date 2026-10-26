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
        readonly FurniturePlacementService placement;

        WorldSave kept;
        string keptName;
        int keptFloor;
        readonly List<KeyValuePair<CameraRig, CameraRig.ViewState>> keptViews = new List<KeyValuePair<CameraRig, CameraRig.ViewState>>();

        /// <summary>Rooms move in whole grid cells, so a room centred on its origin keeps its pieces on their cells.</summary>
        const float Snap = 0.25f;

        public RoomEditor(WorldService world, Storeys storeys, RoomCatalog catalog, WorldSettings settings,
            FurniturePlacementService placement)
        {
            this.world = world;
            this.storeys = storeys;
            this.catalog = catalog;
            this.settings = settings;
            this.placement = placement;
        }

        /// <summary>
        /// The room's doors while it is being made, where they stand on the ground floor. Written
        /// into the room file on save, moved with the room as it is centred.
        /// </summary>
        public readonly List<WorldSave.SavedOpening> Openings = new List<WorldSave.SavedOpening>();

        /// <summary>Raised when an opening is made, changed or taken out.</summary>
        public event Action OpeningsChanged;

        /// <summary>Raised with why a piece was not let go of to be moved, for the room bar to say.</summary>
        public event Action<string> Refused;

        /// <summary>
        /// True, and says why, when <paramref name="piece"/> may not be picked up while the room is
        /// being made: a floor tile with an entrance or exit on its edge. Moved, it would leave the
        /// door on no floor; take the door off, or drag it elsewhere, first.
        /// </summary>
        public bool RefuseLift(FurniturePiece piece)
        {
            Bounds bounds;

            if (!IsEditing || piece == null || !piece.IsFloor || Openings.Count == 0
                || !FurniturePlacementService.TryColliderBounds(piece.gameObject, out bounds))
            {
                return false;
            }

            var tile = Rect.MinMaxRect(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z);
            var held = Openings.FindAll(opening => RoomOpenings.OnEdgeOf(tile, opening));

            if (held.Count == 0)
            {
                return false;
            }

            var what = held.Count > 1 ? "the room's entrance and exit are" : "the room's " + held[0].kind + " is";
            Refused?.Invoke("That floor tile stays put: " + what + " on its edge. Drag the door to another spot, "
                + "or take it off with its tool, to move the tile.");

            return true;
        }

        /// <summary>Where the ground floor's ground is, which a room is built on.</summary>
        public float GroundHeight => storeys.BaseOf(0);

        /// <summary>True from <see cref="Begin"/> until the room is saved or cancelled.</summary>
        public bool IsEditing { get; private set; }

        /// <summary>The name of the room loaded to be edited, when one was: what saving suggests.</summary>
        public string EditingName => editing != null ? editing.name : null;

        /// <summary>The room loaded to be edited, when one was. Saved under its own name, it is replaced.</summary>
        RoomEntry editing;

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
            editing = null;
            Openings.Clear();

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

        /// <summary>
        /// Leaves the world to edit <paramref name="entry"/>: the room editor, with that room in
        /// it. Saved under its own name, it replaces the room - a room the player made anywhere,
        /// one of the pack's only in the editor. False, having left nothing open, when it could not.
        /// </summary>
        public bool Edit(RoomEntry entry, out string problem)
        {
            problem = null;

            if (IsEditing)
            {
                problem = "A room is already being made. Save it or cancel first.";
                return false;
            }

            if (!Begin())
            {
                problem = "The room editor is not set up in this scene. Run Tools > Dojo > Add Rooms Tab and save the scene.";
                return false;
            }

            if (!Load(entry, out problem))
            {
                Cancel();
                return false;
            }

            Raise(Changed);
            return true;
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
            editing = entry;

            // Its doors as its file has them: the room is built where its file puts it, so they
            // are where they belong already.
            Openings.Clear();
            var ground = room.floors.Find(f => f != null && f.index == 0);

            if (ground != null && ground.openings != null)
            {
                foreach (var opening in ground.openings)
                {
                    if (opening != null)
                    {
                        Openings.Add(opening.Copy());
                    }
                }
            }

            Raise(OpeningsChanged);

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

            // One of the pack's rooms, loaded and saved under its own name: written over itself.
            var overPack = editing != null && !CustomRooms.IsCustom(editing.address)
                && string.Equals(name.Trim(), (editing.name ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

            if (overPack && !Application.isEditor)
            {
                problem = "The pack's own rooms can only be changed in the editor. Save it under a new name to keep a copy.";
                return false;
            }

            // Its thumbnail goes beside the pack rooms' own, so a name of theirs would replace one.
            if (!overPack && !catalog.Custom.IsNameFree(name, out problem))
            {
                return false;
            }

            if (IsEmpty)
            {
                problem = "Nothing has been built yet, so there is no room to save.";
                return false;
            }

            if (!CheckOpenings(out problem))
            {
                return false;
            }

            var built = world.Capture();

            // The room's own area over every floor tile it was built with, named as the room is -
            // as each of the pack's rooms carries one - so placing it brings its area with it.
            var tiles = AddFloorArea(built, name.Trim(), EditingName);

            Vector3 shift;
            var room = ToRoom(built, out shift);

            // The doors, moved with everything else as the room is centred on its origin.
            var groundFloor = room.FloorAt(0);
            groundFloor.openings.Clear();

            foreach (var opening in Openings)
            {
                var moved = opening.Copy();
                moved.centre = new WorldSave.GroundPoint(opening.centre.x - shift.x, opening.centre.z - shift.z);
                groundFloor.openings.Add(moved);
            }

            if (room.PieceCount() == 0)
            {
                problem = "Only what is on the ground floor is saved, and nothing is built there.";
                return false;
            }

            // Rendered now, while the room is the thing standing.
            var thumbnail = RoomThumbnailCamera.Render(storeys.Floor(0), LayerMask.NameToLayer(settings.PlacedLayerName));
            RoomEntry entry;

            if (overPack)
            {
                if (!catalog.Custom.SavePackRoom(editing, room, thumbnail, out problem))
                {
                    return false;
                }

                entry = new RoomEntry { name = editing.name, address = editing.address, icon = editing.icon, tag = editing.tag };
            }
            else
            {
                var address = catalog.Custom.Save(name.Trim(), room, thumbnail);
                entry = new RoomEntry { name = name.Trim(), address = address, icon = address };
            }

            Debug.Log("[Rooms] Saved room '" + entry.name + "' - " + room.PieceCount() + " piece(s), "
                + (tiles > 0 ? "an area of " + tiles + " tile(s) over its floors" : "no floor tiles, so no area") + ", to "
                + (overPack ? "the pack, over itself" : CustomRooms.Folder) + (thumbnail == null ? " (no thumbnail could be rendered)" : ""));

            End();

            var saved = Saved;
            if (saved != null)
            {
                saved(entry);
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
            editing = null;
            Openings.Clear();

            Raise(Changed);
        }

        /// <summary>
        /// Gives the built ground floor an area named <paramref name="name"/> covering every floor
        /// tile on it, cell by cell on the area grid. One already there under that name, or under
        /// <paramref name="previousName"/> - the room's own area from when it was loaded to be
        /// edited - is replaced rather than kept beside it. Returns how many tiles it covers.
        /// </summary>
        /// <remarks>
        /// From the floor tiles' colliders as they stand, since a saved piece knows only where it
        /// is and not how far it reaches. Every other area painted while building is kept as it is.
        /// </remarks>
        int AddFloorArea(WorldSave built, string name, string previousName)
        {
            Physics.SyncTransforms();

            var cells = new HashSet<Vector2Int>();
            var top = float.MinValue;
            var pieces = new List<Transform>();
            Storeys.AddPiecesUnder(storeys.Floor(0), pieces);

            foreach (var child in pieces)
            {
                var piece = child.GetComponent<FurniturePiece>();
                Bounds bounds;

                if (piece == null || !piece.IsFloor || !FurniturePlacementService.TryColliderBounds(child.gameObject, out bounds))
                {
                    continue;
                }

                top = Mathf.Max(top, bounds.max.y);

                // Every cell whose middle is on the tile: cells sit at 0.125 + 0.25k, as painted ones do.
                var fromX = Mathf.CeilToInt((bounds.min.x - AreaCell * 0.5f) / AreaCell - 0.001f);
                var toX = Mathf.FloorToInt((bounds.max.x - AreaCell * 0.5f) / AreaCell + 0.001f);
                var fromZ = Mathf.CeilToInt((bounds.min.z - AreaCell * 0.5f) / AreaCell - 0.001f);
                var toZ = Mathf.FloorToInt((bounds.max.z - AreaCell * 0.5f) / AreaCell + 0.001f);

                for (var x = fromX; x <= toX; x++)
                {
                    for (var z = fromZ; z <= toZ; z++)
                    {
                        cells.Add(new Vector2Int(x, z));
                    }
                }
            }

            var ground = built.FloorAt(0);
            System.Predicate<WorldSave.SavedBlockArea> isRoomsOwn = area => area != null
                && (string.Equals(area.name, name, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrEmpty(previousName) && string.Equals(area.name, previousName, StringComparison.OrdinalIgnoreCase)));

            // What the area being replaced was, so painting it again does not undo it: a conference
            // room stays one, and a department's room stays that department's.
            var replaced = ground.blockAreas.Find(isRoomsOwn);
            ground.blockAreas.RemoveAll(isRoomsOwn);

            if (cells.Count == 0)
            {
                return 0;
            }

            var height = storeys.ToFloor(0, new Vector3(0f, top, 0f)).y;
            var points = new List<Vector3>(cells.Count);

            foreach (var cell in cells)
            {
                points.Add(new Vector3(cell.x * AreaCell + AreaCell * 0.5f, height, cell.y * AreaCell + AreaCell * 0.5f));
            }

            ground.blockAreas.Add(new WorldSave.SavedBlockArea
            {
                id = AreaIdFor(name),
                name = name,
                area = points,
                areaCellSize = AreaCell,
                department = replaced != null ? replaced.department ?? string.Empty : string.Empty,
                conferenceRoom = replaced != null && replaced.conferenceRoom,
            });

            return cells.Count;
        }

        /// <summary>The size of an area's cells: the placement grid's, which painted areas use too.</summary>
        const float AreaCell = 0.25f;

        /// <summary>
        /// A stable id for a room's area, from its name. Only the room file keeps it: a placed
        /// room's area is given a fresh id in the world it is placed in.
        /// </summary>
        static int AreaIdFor(string name)
        {
            unchecked
            {
                var hash = (int)2166136261;

                foreach (var c in name.ToLowerInvariant())
                {
                    hash = (hash ^ c) * 16777619;
                }

                return (hash & 0x7FFFFFFF) | 1;
            }
        }

        /// <summary>
        /// What was built, as a room: the ground floor's pieces and areas, centred on the room's
        /// own origin in whole grid cells. Agents, managers, elevators and other floors are not
        /// part of a room.
        /// </summary>
        static WorldSave ToRoom(WorldSave built, out Vector3 shift)
        {
            shift = Vector3.zero;
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
            shift = new Vector3(Mathf.Round(centre.x / Snap) * Snap, 0f, Mathf.Round(centre.y / Snap) * Snap);

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

        // ── Openings ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The ground floor as it stands: its floor tiles, and its walls with the piece each one is,
        /// by <see cref="RoomOpenings.Wall.Id"/>.
        /// </summary>
        public void Measure(List<Rect> tiles, List<RoomOpenings.Wall> walls, List<Transform> wallPieces)
        {
            Physics.SyncTransforms();

            var pieces = new List<Transform>();
            Storeys.AddPiecesUnder(storeys.Floor(0), pieces);

            foreach (var child in pieces)
            {
                Bounds bounds;

                if (!child.gameObject.activeInHierarchy || !FurniturePlacementService.TryColliderBounds(child.gameObject, out bounds))
                {
                    continue;
                }

                var piece = child.GetComponent<FurniturePiece>();

                if (piece != null && piece.IsFloor)
                {
                    tiles.Add(Rect.MinMaxRect(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z));
                }
                else if (IsWall(child))
                {
                    walls.Add(RoomOpenings.WallFrom(new Vector2(bounds.center.x, bounds.center.z),
                        new Vector2(bounds.size.x, bounds.size.z), wallPieces.Count));
                    wallPieces.Add(child);
                }
            }
        }

        /// <summary>A wall, as the room file format tells: its resource names one.</summary>
        public static bool IsWall(Transform piece)
        {
            var placed = piece != null ? piece.GetComponent<PlacedPiece>() : null;
            return RoomOpenings.IsWall(placed != null && !string.IsNullOrEmpty(placed.Resource) ? placed.Resource : piece != null ? piece.name : null);
        }

        /// <summary>The gaps in the walls along the floor's edge that are not openings yet.</summary>
        public List<WorldSave.SavedOpening> Gaps()
        {
            var tiles = new List<Rect>();
            var walls = new List<RoomOpenings.Wall>();
            Measure(tiles, walls, new List<Transform>());

            return RoomOpenings.Gaps(tiles, walls, Openings);
        }

        /// <summary>
        /// Takes a wall of the floor's edge out and makes the gap an opening of
        /// <paramref name="kind"/>. False, with nothing changed, when the wall is not on the edge or
        /// an opening is there already.
        /// </summary>
        public bool CutOpening(Transform wallPiece, string kind, out string problem)
        {
            problem = RoomOpenings.OneOfEach(Openings, kind);

            if (problem != null)
            {
                return false;
            }

            var tiles = new List<Rect>();
            var walls = new List<RoomOpenings.Wall>();
            var wallPieces = new List<Transform>();
            Measure(tiles, walls, wallPieces);

            var at = wallPieces.IndexOf(wallPiece);
            WorldSave.SavedOpening opening;

            if (at < 0 || !RoomOpenings.TryAtWall(tiles, walls[at], kind, out opening))
            {
                problem = "That wall is not on the edge of the floor, so it cannot be a door. Pick a wall with floor on one side only.";
                return false;
            }

            foreach (var other in Openings)
            {
                if (RoomOpenings.Clash(opening, other))
                {
                    problem = "There is " + (kind == RoomOpenings.Entrance ? "an entrance" : "an exit") + " there already.";
                    return false;
                }
            }

            // Every wall across the span, the one picked included.
            foreach (var wall in walls)
            {
                if (RoomOpenings.Crosses(wall, opening))
                {
                    RemovePiece(wallPieces[wall.Id]);
                }
            }

            Openings.Add(opening);
            Raise(OpeningsChanged);
            return true;
        }

        /// <summary>How far from the cursor a place on the floor's edge may be for a dragged opening to go to it.</summary>
        const float MoveReach = 1.5f;

        /// <summary>
        /// Where the opening at <paramref name="index"/> would go if let go of near
        /// <paramref name="point"/>: onto the wall or the gap on the floor's edge nearest it, as
        /// wide as it is. False when there is nowhere near, or the place nearest is taken by another
        /// opening - then <paramref name="target"/> is that place anyway, to show where it was refused.
        /// </summary>
        public bool TryMoveTarget(int index, Vector2 point, out WorldSave.SavedOpening target)
        {
            target = null;

            if (index < 0 || index >= Openings.Count)
            {
                return false;
            }

            var moving = Openings[index];
            var tiles = new List<Rect>();
            var walls = new List<RoomOpenings.Wall>();
            Measure(tiles, walls, new List<Transform>());

            // Its own place counts as a gap, so it can be put back where it was.
            var others = new List<WorldSave.SavedOpening>(Openings);
            others.RemoveAt(index);

            var best = MoveReach;

            foreach (var wall in walls)
            {
                WorldSave.SavedOpening atWall;

                if (!RoomOpenings.TryAtWall(tiles, wall, moving.kind, out atWall))
                {
                    continue;
                }

                atWall.width = moving.width;
                var distance = RoomOpenings.DistanceTo(atWall, point);

                if (distance < best)
                {
                    best = distance;
                    target = atWall;
                }
            }

            // Onto a door of the other kind, to share its spot.
            foreach (var other in others)
            {
                if (other.kind == moving.kind)
                {
                    continue;
                }

                var shared = other.Copy();
                shared.width = moving.width;
                var distance = RoomOpenings.DistanceTo(shared, point);

                if (distance < best)
                {
                    best = distance;
                    target = shared;
                }
            }

            foreach (var gap in RoomOpenings.Gaps(tiles, walls, others))
            {
                var inGap = RoomOpenings.Within(gap, point, moving.width);
                var distance = RoomOpenings.DistanceTo(inGap, point);

                if (distance < best)
                {
                    best = distance;
                    target = inGap;
                }
            }

            if (target == null)
            {
                return false;
            }

            target.kind = moving.kind;

            var place = target;
            return !others.Exists(other => RoomOpenings.Clash(place, other));
        }

        /// <summary>
        /// Moves the opening at <paramref name="index"/> to <paramref name="target"/>, taking out the
        /// walls across it there. The place it leaves stays open, as a gap.
        /// </summary>
        public void MoveOpening(int index, WorldSave.SavedOpening target)
        {
            if (index < 0 || index >= Openings.Count || target == null)
            {
                return;
            }

            var tiles = new List<Rect>();
            var walls = new List<RoomOpenings.Wall>();
            var wallPieces = new List<Transform>();
            Measure(tiles, walls, wallPieces);

            foreach (var wall in walls)
            {
                if (RoomOpenings.Crosses(wall, target))
                {
                    RemovePiece(wallPieces[wall.Id]);
                }
            }

            Openings[index].centre = target.centre;
            Openings[index].facing = target.facing;
            Raise(OpeningsChanged);
        }

        /// <summary>
        /// Adds an opening of <paramref name="kind"/> on the very spot of <paramref name="place"/> -
        /// an exit on an entrance's, or the other way round - so one door is both. False, with
        /// <paramref name="problem"/> saying why, when one of that kind is there already.
        /// </summary>
        public bool AddAt(WorldSave.SavedOpening place, string kind, out string problem)
        {
            problem = RoomOpenings.OneOfEach(Openings, kind);

            if (problem != null)
            {
                return false;
            }

            var opening = place.Copy();
            opening.kind = kind;

            if (Openings.Exists(other => RoomOpenings.Clash(opening, other)))
            {
                problem = "There is " + (kind == RoomOpenings.Entrance ? "an entrance" : "an exit") + " there already.";
                return false;
            }

            Openings.Add(opening);
            Raise(OpeningsChanged);
            return true;
        }

        /// <summary>Makes a gap already in the walls an opening of <paramref name="kind"/>.</summary>
        public bool MarkGap(WorldSave.SavedOpening gap, string kind, out string problem)
        {
            problem = RoomOpenings.OneOfEach(Openings, kind);

            if (problem != null)
            {
                return false;
            }

            var opening = gap.Copy();
            opening.kind = kind;
            Openings.Add(opening);
            Raise(OpeningsChanged);
            return true;
        }

        /// <summary>Forgets an opening. The gap in the walls stays, offered as a gap again.</summary>
        public void RemoveOpening(int index)
        {
            if (index >= 0 && index < Openings.Count)
            {
                Openings.RemoveAt(index);
                Raise(OpeningsChanged);
            }
        }

        /// <summary>
        /// True when the room's openings keep the rules of the room file format; otherwise
        /// <paramref name="problem"/> says which rule failed.
        /// </summary>
        public bool CheckOpenings(out string problem)
        {
            var tiles = new List<Rect>();
            var walls = new List<RoomOpenings.Wall>();
            Measure(tiles, walls, new List<Transform>());

            return RoomOpenings.Validate(tiles, walls, Openings, out problem);
        }

        void RemovePiece(Transform piece)
        {
            if (piece == null)
            {
                return;
            }

            var furniture = piece.GetComponent<FurniturePiece>();

            if (furniture != null)
            {
                placement.Release(furniture);
            }

            // Switched off first: Destroy waits for the end of the frame, and the next measure must
            // not find it.
            piece.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(piece.gameObject);
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
