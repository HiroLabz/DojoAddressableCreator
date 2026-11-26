using System;
using System.Collections.Generic;
using Dojo.Game.InGame.Controllers;
using UnityEngine;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// Making a floor layout: a blank floor to lay a whole floor out on with the inventory - tiles,
    /// walls, rooms from the Rooms tab, pieces - saving it as a layout, and going back to the
    /// world exactly as it was left.
    /// </summary>
    /// <remarks>
    /// <see cref="RoomEditor"/> for floors (ClickUp 14ze67xgf0v), without the parts a layout has no
    /// use for: no openings, no area of its own, no department, nothing refused on lift. What it has
    /// instead is a size: a frame of <see cref="Columns"/> × <see cref="Rows"/> floor tiles drawn on
    /// the ground as a guide, which the SIZE control changes. A piece laid outside the frame is
    /// counted (<see cref="CountOutside"/>) and, if the designer agrees on save, dropped.
    /// <para>
    /// A layout keeps its rooms as rooms: a room placed from the Rooms tab is saved with its
    /// <see cref="WorldSave.SavedRoom"/>, its pieces still named to it and its areas, so placing
    /// the layout later brings the rooms back as rooms. Everything is centred on the layout's own
    /// origin in whole grid cells, as a room is.
    /// </para>
    /// </remarks>
    public sealed class FloorLayoutEditor
    {
        readonly WorldService world;
        readonly Storeys storeys;
        readonly FloorLayoutCatalog catalog;
        readonly WorldSettings settings;

        WorldSave kept;
        string keptName;
        int keptFloor;
        readonly List<KeyValuePair<CameraRig, CameraRig.ViewState>> keptViews = new List<KeyValuePair<CameraRig, CameraRig.ViewState>>();

        /// <summary>The fewest and most tiles a layout's frame can be across or deep.</summary>
        public const int MinSize = 4;
        public const int MaxSize = 24;

        /// <summary>The frame a new layout starts with.</summary>
        public const int DefaultColumns = 8;
        public const int DefaultRows = 6;

        /// <summary>One floor tile's width: the MVP kit's tile is four cells of the grid.</summary>
        public static float Tile => PlacementGrid.Step * 4f;

        /// <summary>Layouts move in whole grid cells, so one centred on its origin keeps its pieces on their cells.</summary>
        static float Snap => PlacementGrid.Step;

        public FloorLayoutEditor(WorldService world, Storeys storeys, FloorLayoutCatalog catalog, WorldSettings settings)
        {
            this.world = world;
            this.storeys = storeys;
            this.catalog = catalog;
            this.settings = settings;
        }

        /// <summary>Where the ground floor's ground is, which a layout is built on.</summary>
        public float GroundHeight => storeys.BaseOf(0);

        /// <summary>True from <see cref="Begin"/> until the layout is saved or cancelled.</summary>
        public bool IsEditing { get; private set; }

        /// <summary>The name of the layout loaded to be edited, when one was: what saving suggests.</summary>
        public string EditingName => editing != null ? editing.name : null;

        /// <summary>The layout loaded to be edited, when one was. Saved under its own name, it is replaced.</summary>
        FloorLayoutEntry editing;

        /// <summary>The frame's width in tiles.</summary>
        public int Columns { get; private set; } = DefaultColumns;

        /// <summary>The frame's depth in tiles.</summary>
        public int Rows { get; private set; } = DefaultRows;

        /// <summary>Raised when making a layout starts or ends, and when the floor is cleared.</summary>
        public event Action Changed;

        /// <summary>Raised when the frame's size changes.</summary>
        public event Action SizeChanged;

        /// <summary>Raised with the layout just saved, once the world is back.</summary>
        public event Action<FloorLayoutEntry> Saved;

        /// <summary>
        /// Keeps the world and puts an empty floor in its place, with the frame drawn on it. False,
        /// having changed nothing, when the scene has no floor bar - without its SAVE LAYOUT and
        /// CANCEL there would be no way back.
        /// </summary>
        public bool Begin()
        {
            if (IsEditing)
            {
                return true;
            }

            if (UnityEngine.Object.FindObjectsByType<Dojo.Game.UI.FloorLayoutEditorBar>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 0)
            {
                Debug.LogError("[Floors] There is no floor bar in the scene, so a layout cannot be made: there would "
                    + "be no SAVE LAYOUT or CANCEL to get back to the world. Run Tools > Dojo > Build Floor Editor and "
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
            Columns = DefaultColumns;
            Rows = DefaultRows;

            world.Show(Blank(), null);

            foreach (var rig in Rigs())
            {
                rig.ResetView();
            }

            ShowFrame();

            Debug.Log("[Floors] Making a floor layout. '" + (keptName ?? "the open world") + "' is kept, "
                + kept.PieceCount() + " piece(s), and comes back on save or cancel.");

            Raise(Changed);
            return true;
        }

        /// <summary>True while nothing has been laid: nothing to save as a layout.</summary>
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
        /// Leaves the world to edit <paramref name="entry"/>: the floor editor, with that layout in
        /// it, the frame fitted to it. Saved under its own name, it replaces the layout - one made
        /// here anywhere, one of the pack's only in the editor. False, having left nothing open,
        /// when it could not.
        /// </summary>
        public bool Edit(FloorLayoutEntry entry, out string problem)
        {
            problem = null;

            if (IsEditing)
            {
                problem = "A floor layout is already being made. Save it or cancel first.";
                return false;
            }

            if (!Begin())
            {
                problem = "The floor editor is not set up in this scene. Run Tools > Dojo > Build Floor Editor and save the scene.";
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

        /// <summary>Puts a layout in front of the designer to be edited, in place of whatever is being laid.</summary>
        public bool Load(FloorLayoutEntry entry, out string problem)
        {
            problem = null;

            if (!IsEditing)
            {
                problem = "Start making a floor layout first.";
                return false;
            }

            var layout = entry != null ? catalog.Read(entry) : null;

            if (layout == null)
            {
                problem = "That layout could not be read.";
                return false;
            }

            // Its rooms come back as rooms, so they move and delete as one here too.
            world.Show(layout, null);
            editing = entry;

            int columns, rows;
            FitSize(layout, out columns, out rows);
            Columns = columns;
            Rows = rows;

            foreach (var rig in Rigs())
            {
                rig.ResetView();
            }

            ShowFrame();
            Raise(SizeChanged);

            Debug.Log("[Floors] Editing '" + entry.name + "' - " + layout.PieceCount() + " piece(s), " + columns + " × " + rows + " tiles.");
            return true;
        }

        /// <summary>Sets the frame to <paramref name="columns"/> × <paramref name="rows"/> tiles, within the limits.</summary>
        public void Resize(int columns, int rows)
        {
            Columns = Mathf.Clamp(columns, MinSize, MaxSize);
            Rows = Mathf.Clamp(rows, MinSize, MaxSize);

            if (IsEditing)
            {
                ShowFrame();
            }

            Raise(SizeChanged);
        }

        /// <summary>The frame as it stands, on the ground floor's own plane: x across, y along z.</summary>
        public Rect Frame => FrameFor(Columns, Rows);

        /// <summary>A frame of so many tiles, centred on the origin.</summary>
        public static Rect FrameFor(int columns, int rows)
        {
            var width = columns * Tile;
            var depth = rows * Tile;
            return new Rect(-width * 0.5f, -depth * 0.5f, width, depth);
        }

        /// <summary>
        /// True when a piece standing at <paramref name="position"/> (relative to its floor) is
        /// within the frame, with half a cell's grace at the edge so a tile flush with it counts.
        /// </summary>
        public static bool Inside(Rect frame, Vector3 position)
        {
            var grace = Snap * 0.5f;
            return position.x >= frame.xMin - grace && position.x <= frame.xMax + grace
                && position.z >= frame.yMin - grace && position.z <= frame.yMax + grace;
        }

        /// <summary>How many pieces stand outside the frame now: what a save would drop.</summary>
        public int CountOutside()
        {
            if (!IsEditing)
            {
                return 0;
            }

            var outside = 0;
            var pieces = new List<Transform>();
            Storeys.AddPiecesUnder(storeys.Floor(0), pieces);
            var frame = Frame;

            foreach (var child in pieces)
            {
                if (child.GetComponent<FurniturePiece>() != null && !Inside(frame, storeys.ToFloor(0, child.position)))
                {
                    outside++;
                }
            }

            return outside;
        }

        /// <summary>Takes everything off the floor. The frame keeps its size.</summary>
        public void Clear()
        {
            if (!IsEditing)
            {
                return;
            }

            world.Show(Blank(), null);
            ShowFrame();
            Debug.Log("[Floors] The floor was cleared.");
            Raise(Changed);
        }

        /// <summary>
        /// Saves what was laid as a layout called <paramref name="name"/> and goes back to the
        /// world. Pieces outside the frame are dropped when <paramref name="dropOutside"/>, and
        /// refused otherwise. False, with <paramref name="problem"/> saying why, when nothing was saved.
        /// </summary>
        public bool Save(string name, bool dropOutside, out string problem)
        {
            problem = null;

            if (!IsEditing)
            {
                problem = "There is no floor layout being made.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                problem = "Give the layout a name.";
                return false;
            }

            // One of the pack's layouts, loaded and saved under its own name: written over itself.
            var overPack = editing != null && !CustomFloorLayouts.IsCustom(editing.address)
                && string.Equals(name.Trim(), (editing.name ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

            if (overPack && !Application.isEditor)
            {
                problem = "The pack's own layouts can only be changed in the editor. Save it under a new name to keep a copy.";
                return false;
            }

            if (!overPack && !catalog.Custom.IsNameFree(name, out problem))
            {
                return false;
            }

            if (IsEmpty)
            {
                problem = "Nothing has been laid yet, so there is no layout to save.";
                return false;
            }

            var built = world.Capture();

            Vector3 shift;
            int outside;
            var layout = ToLayout(built, Frame, dropOutside, out shift, out outside);

            if (outside > 0 && !dropOutside)
            {
                problem = outside + " piece(s) stand outside the " + Columns + " × " + Rows + " frame. Move them in, grow the frame, or save dropping them.";
                return false;
            }

            if (layout.PieceCount() == 0)
            {
                problem = "Only what is on the ground floor, inside the frame, is saved, and nothing is laid there.";
                return false;
            }

            // Rendered now, while the layout is the thing standing; the frame is not on the placed layer.
            var thumbnail = RoomThumbnailCamera.Render(storeys.Floor(0), LayerMask.NameToLayer(settings.PlacedLayerName));
            FloorLayoutEntry entry;

            if (overPack)
            {
                if (!catalog.Custom.SavePackLayout(editing, layout, thumbnail, out problem))
                {
                    return false;
                }

                entry = new FloorLayoutEntry { name = editing.name, address = editing.address, icon = editing.icon, tag = editing.tag };
            }
            else
            {
                var address = catalog.Custom.Save(name.Trim(), layout, thumbnail);
                entry = new FloorLayoutEntry { name = name.Trim(), address = address, icon = address };
            }

            Debug.Log("[Floors] Saved floor layout '" + entry.name + "' - " + layout.PieceCount() + " piece(s), "
                + layout.rooms.Count + " room(s), " + Columns + " × " + Rows + " tiles"
                + (outside > 0 ? ", " + outside + " piece(s) outside the frame dropped" : "") + ", to "
                + (overPack ? "the pack, over itself" : catalog.Custom.Folder) + (thumbnail == null ? " (no thumbnail could be rendered)" : ""));

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
                Debug.Log("[Floors] Floor layout making cancelled; nothing was saved.");
                End();
            }
        }

        void End()
        {
            HideFrame();
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

            Raise(Changed);
        }

        // ── The layout file ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// What was laid, as a layout: the ground floor's pieces, areas and rooms, centred on the
        /// layout's own origin in whole grid cells. Pieces outside <paramref name="frame"/> are
        /// counted in <paramref name="outside"/> and left out when <paramref name="dropOutside"/>;
        /// a room with none of its pieces left goes with them. Agents, managers, elevators and
        /// other floors are not part of a layout.
        /// </summary>
        public static WorldSave ToLayout(WorldSave built, Rect frame, bool dropOutside, out Vector3 shift, out int outside)
        {
            shift = Vector3.zero;
            outside = 0;

            var layout = new WorldSave();
            var ground = layout.FloorAt(0);
            var source = built.floors.Find(f => f != null && f.index == 0);

            if (source == null)
            {
                return layout;
            }

            foreach (var piece in source.pieces)
            {
                if (piece == null)
                {
                    continue;
                }

                if (!Inside(frame, piece.position))
                {
                    outside++;

                    if (dropOutside)
                    {
                        continue;
                    }
                }

                ground.pieces.Add(piece);
            }

            ground.blockAreas.AddRange(source.blockAreas);

            // The rooms on the ground floor, kept as rooms: only those a piece still names.
            var named = new HashSet<string>(StringComparer.Ordinal);
            foreach (var piece in ground.pieces)
            {
                if (!string.IsNullOrEmpty(piece.room))
                {
                    named.Add(piece.room);
                }
            }

            if (built.rooms != null)
            {
                foreach (var room in built.rooms)
                {
                    if (room != null && room.floor == 0 && !string.IsNullOrEmpty(room.id) && named.Contains(room.id))
                    {
                        layout.rooms.Add(room);
                    }
                }
            }

            // A piece naming a room that is not on this floor stands on its own.
            var kept = new HashSet<string>(StringComparer.Ordinal);
            foreach (var room in layout.rooms)
            {
                kept.Add(room.id);
            }

            foreach (var piece in ground.pieces)
            {
                if (!string.IsNullOrEmpty(piece.room) && !kept.Contains(piece.room))
                {
                    piece.room = string.Empty;
                }
            }

            if (ground.pieces.Count == 0)
            {
                return layout;
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

            foreach (var room in layout.rooms)
            {
                room.position -= shift;

                foreach (var entrance in room.entrances)
                {
                    if (entrance != null)
                    {
                        entrance.centre = new WorldSave.GroundPoint(entrance.centre.x - shift.x, entrance.centre.z - shift.z);
                    }
                }
            }

            return layout;
        }

        /// <summary>
        /// The frame that fits a layout: its ground floor's pieces, measured by their positions
        /// plus half a tile, rounded up to whole tiles and kept within the limits. The default for
        /// a layout with nothing on its ground floor.
        /// </summary>
        public static void FitSize(WorldSave layout, out int columns, out int rows)
        {
            columns = DefaultColumns;
            rows = DefaultRows;

            var source = layout != null && layout.floors != null ? layout.floors.Find(f => f != null && f.index == 0) : null;

            if (source == null || source.pieces == null || source.pieces.Count == 0)
            {
                return;
            }

            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);

            foreach (var piece in source.pieces)
            {
                if (piece == null)
                {
                    continue;
                }

                min = Vector2.Min(min, new Vector2(piece.position.x, piece.position.z));
                max = Vector2.Max(max, new Vector2(piece.position.x, piece.position.z));
            }

            // The frame is centred on the origin, so it reaches as far as the furthest piece on
            // either side, plus the half tile a tile at the edge still covers.
            var reachX = Mathf.Max(Mathf.Abs(min.x), Mathf.Abs(max.x)) + Tile * 0.5f;
            var reachZ = Mathf.Max(Mathf.Abs(min.y), Mathf.Abs(max.y)) + Tile * 0.5f;

            columns = Mathf.Clamp(Mathf.CeilToInt(reachX * 2f / Tile - 0.001f), MinSize, MaxSize);
            rows = Mathf.Clamp(Mathf.CeilToInt(reachZ * 2f / Tile - 0.001f), MinSize, MaxSize);
        }

        // ── The frame on the ground ─────────────────────────────────────────────────────────

        GameObject frameObject;
        LineRenderer frameLine;
        Material frameMaterial;

        /// <summary>Amber, as the opening tool draws its marks.</summary>
        static readonly Color FrameColour = new Color(1f, 0.78f, 0.2f, 0.9f);

        /// <summary>
        /// Draws the frame on the ground floor: a loop of four lines a little above the ground, on
        /// the default layer so the thumbnail camera, which only sees placed pieces, leaves it out.
        /// </summary>
        void ShowFrame()
        {
            if (frameObject == null)
            {
                frameObject = new GameObject("FloorLayoutFrame");
                frameObject.layer = 0;

                if (frameMaterial == null)
                {
                    frameMaterial = new Material(Shader.Find("Sprites/Default")) { name = "FloorLayoutFrame" };
                }

                frameLine = frameObject.AddComponent<LineRenderer>();
                frameLine.sharedMaterial = frameMaterial;
                frameLine.useWorldSpace = true;
                frameLine.loop = true;
                frameLine.positionCount = 4;
                frameLine.startWidth = 0.06f;
                frameLine.endWidth = 0.06f;
                frameLine.numCornerVertices = 2;
                frameLine.alignment = LineAlignment.TransformZ;
                frameLine.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                frameLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                frameLine.receiveShadows = false;
                frameLine.startColor = FrameColour;
                frameLine.endColor = FrameColour;
            }

            var frame = Frame;
            var y = GroundHeight + 0.03f;

            frameLine.SetPosition(0, new Vector3(frame.xMin, y, frame.yMin));
            frameLine.SetPosition(1, new Vector3(frame.xMax, y, frame.yMin));
            frameLine.SetPosition(2, new Vector3(frame.xMax, y, frame.yMax));
            frameLine.SetPosition(3, new Vector3(frame.xMin, y, frame.yMax));
        }

        void HideFrame()
        {
            if (frameObject != null)
            {
                UnityEngine.Object.Destroy(frameObject);
                frameObject = null;
                frameLine = null;
            }
        }

        static WorldSave Blank()
        {
            var blank = new WorldSave { name = "a new floor layout" };
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
