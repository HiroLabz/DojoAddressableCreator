using System;
using System.Collections.Generic;
using Dojo.Game.InGame.Controllers;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using VContainer;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// The area gesture: a green brush follows the cursor over the floor, holding the left button
    /// paints the cells it crosses, and the right button commits the lot as an area.
    /// </summary>
    /// <remarks>
    /// The counterpart to <see cref="PlacementController"/> and deliberately not part of it. The
    /// two share the grid, the floor lookup and the highlight, but nothing else: this one holds no
    /// piece, has no footprint to rotate, has no legality to check beyond "is that floor", and ends
    /// on the right button rather than the left. Folding it into a controller already running a
    /// three-state machine over a held object would have meant a fourth state that shares none of
    /// the other three's data.
    /// <para>
    /// Its whole job is to work out an area and hand it back. It does not know what an area is
    /// for, who it belongs to, or that agents exist — <see cref="Committed"/> carries a list of
    /// floor points and nothing else, and whoever started the session is the one who remembers
    /// what they started it for. That is what lets the same brush serve anything needing a patch
    /// of floor picked out.
    /// </para>
    /// <para>
    /// Only ever one session at a time, and never while a piece is in hand — see
    /// <see cref="Begin"/>. While a session runs the camera rig and the placement controller both
    /// stand down: the rig owns the right button that commits, and a press-and-hold would
    /// otherwise lift whatever the brush is passing over.
    /// </para>
    /// </remarks>
    [DefaultExecutionOrder(-100)]
    public sealed class AreaPainter : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("Camera the cursor is cast from. Defaults to Camera.main.")]
        [SerializeField] Camera view;

        [Header("Brush")]
        [Tooltip("Width of the brush in grid cells. Cells are 0.25m, so a single-cell brush would " +
                 "take a very long time to cover a room; the wheel changes this mid-stroke.")]
        [SerializeField, Range(1, 16)] int brushCells = 3;

        [Tooltip("Smallest the wheel may shrink the brush to, in cells.")]
        [SerializeField, Range(1, 16)] int minBrushCells = 1;

        [Tooltip("Largest the wheel may grow the brush to, in cells.")]
        [SerializeField, Range(1, 32)] int maxBrushCells = 12;

        [Tooltip("How far the cursor ray reaches. The isometric camera sits well back.")]
        [SerializeField] float rayDistance = 500f;

        [Tooltip("Log each step of the gesture to the console.")]
        [SerializeField] bool logGesture;

        FurniturePlacementService service;
        PlacementHighlight highlight;
        PlacementController placement;
        CameraRig rig;

        // An area is painted on the floor being looked at. Null in a scene without floors, which
        // paints on the ground as it always did.
        Storeys storeys;

        [Inject]
        public void Construct(Storeys storeys)
        {
            this.storeys = storeys;
        }

        // Cells painted so far this session, keyed so crossing the same cell twice in one stroke
        // costs nothing. A list would grow without bound over a slow drag and then need de-duping
        // before it could be drawn.
        readonly HashSet<Vector3Int> cells = new HashSet<Vector3Int>();

        // The painted cells as panels, rebuilt only when the set actually changes rather than every
        // frame: a stationary brush over an already-painted room would otherwise re-lay several
        // hundred transforms sixty times a second.
        readonly List<PlacementHighlight.MarkedArea> panels = new List<PlacementHighlight.MarkedArea>();

        // Reused by the brush so a stroke does not allocate a cell list every frame.
        readonly List<Vector3Int> brush = new List<Vector3Int>();

        bool painting;
        bool erasing;
        bool dirty;

        /// <summary>True while an area is being painted.</summary>
        public bool IsActive { get; private set; }

        /// <summary>
        /// While true, the left button erases instead of painting — Ctrl held down permanently.
        /// </summary>
        /// <remarks>
        /// Driven by the Erase Area toggle on the agent and manager panels. Right-drag would have
        /// been the obvious gesture for this, but the right button already commits the session, and
        /// overloading it would make every commit a gamble on whether the mouse moved a pixel.
        /// <para>
        /// Cleared by <see cref="Begin"/>, so a session never starts in erase mode because the last
        /// one ended in it.
        /// </para>
        /// </remarks>
        public bool EraseMode
        {
            get { return eraseMode; }
            set { SetEraseMode(value); }
        }

        bool eraseMode;

        /// <summary>
        /// Raised whenever erase mode goes on or off, however it was changed.
        /// </summary>
        /// <remarks>
        /// The panels put a label on this toggle, and the brush can now turn it off by itself when
        /// the player right-clicks. Without this the button would go on claiming to be erasing
        /// after the brush had stopped — the classic two-copies-of-one-truth bug.
        /// </remarks>
        public event Action<bool> EraseModeChanged;

        void SetEraseMode(bool value)
        {
            if (eraseMode == value)
            {
                return;
            }

            eraseMode = value;

            // Any stroke in progress stops meaning what it meant when it started.
            erasing = false;
            painting = false;

            var handler = EraseModeChanged;

            if (handler != null)
            {
                handler(value);
            }
        }

        /// <summary>
        /// Throws away every painted cell, leaving the session open.
        /// </summary>
        /// <remarks>
        /// What the Clear Agent Area button calls. The session stays up so the player can paint a
        /// replacement straight away, and nothing is written anywhere until they commit — clearing
        /// and then cancelling leaves the old area exactly as it was.
        /// </remarks>
        public void ClearCells()
        {
            if (!IsActive)
            {
                return;
            }

            var had = cells.Count;

            cells.Clear();
            Redraw();

            Log("CLEARED " + had + " cell(s); commit to save the empty area, or cancel to keep the old one");
        }

        /// <summary>How many cells are currently painted.</summary>
        public int CellCount => cells.Count;

        /// <summary>Width of the brush in cells, as the wheel has left it.</summary>
        public int BrushCells => brushCells;

        /// <summary>
        /// Raised when the right button commits a session. Carries the middle of every painted
        /// cell in world space, and nothing about who asked for them.
        /// </summary>
        /// <remarks>
        /// An event rather than a call back into a caller, because the painter has no business
        /// knowing what asked it to paint. The list handed over is a fresh one the listener may
        /// keep — the session's own set is cleared the moment this returns.
        /// </remarks>
        public event Action<List<Vector3>> Committed;

        /// <summary>Raised when a session ends without committing, so a caller can put itself back.</summary>
        public event Action Cancelled;

        void Awake()
        {
            service = GetComponent<FurniturePlacementService>();
            placement = GetComponent<PlacementController>();

            highlight = GetComponent<PlacementHighlight>();
            if (highlight == null)
            {
                highlight = gameObject.AddComponent<PlacementHighlight>();
            }
        }

        /// <summary>
        /// The camera the cursor is cast from, resolved on demand.
        /// </summary>
        /// <remarks>
        /// Deliberately not settled in <c>Awake</c>, which is where this used to be and where it
        /// could not work. Unlike every other component here this one is created at runtime, by
        /// <c>GameLifetimeScope</c> while its own <c>Awake</c> is running — the earliest moment in
        /// the frame — so <see cref="Camera.main"/> is routinely still null when that happens, and
        /// the serialized field is never filled in because there is no component in the scene for
        /// anybody to fill it in on. The result was a painter that refused every session for the
        /// life of the scene.
        /// <para>
        /// Cached once an answer is had, so the tag lookup behind <c>Camera.main</c> is not paid
        /// per frame.
        /// </para>
        /// </remarks>
        Camera View
        {
            get
            {
                if (view == null)
                {
                    view = Camera.main;
                }

                return view;
            }
        }

        /// <summary>
        /// The camera rig, resolved on demand alongside the camera it lives on.
        /// </summary>
        /// <remarks>
        /// Was a <c>Start</c> lookup, which read the camera field before anything had filled it in
        /// and so found nothing — and a null rig means the right button still belongs to the camera
        /// while the brush is up, so the click meant to commit an area orbits the view instead.
        /// </remarks>
        CameraRig Rig
        {
            get
            {
                if (rig == null && View != null)
                {
                    rig = View.GetComponent<CameraRig>();
                }

                return rig;
            }
        }

        /// <summary>
        /// Starts painting an area, seeded with <paramref name="existing"/> so editing an area
        /// resumes it rather than starting over.
        /// </summary>
        /// <returns>
        /// False when nothing was started — a piece is already in hand, or there is no cursor to
        /// paint with. The caller is expected to leave its own state alone in that case.
        /// </returns>
        public bool Begin(List<Vector3> existing)
        {
            if (IsActive)
            {
                // Starting again mid-session would silently throw away whatever was painted, so
                // the running session is abandoned out loud instead.
                Log("a session was still running; cancelling it");
                Cancel();
            }

            // Every refusal below is a warning rather than a Log, and that is not a style choice.
            // A refused Begin looks exactly like a dead button: the press comes to nothing, on
            // screen and in the console, and there is nothing for anybody to go on. Log() is gated
            // behind the logGesture tickbox, which is off by default, so this is the one path here
            // that must never be quiet.
            if (placement != null && placement.IsBusy)
            {
                Debug.LogWarning("[Area] Cannot define an area while a piece is on the cursor. "
                    + "Put it down or press Esc first.", this);
                return false;
            }

            if (Mouse.current == null)
            {
                Debug.LogWarning("[Area] No mouse, so there is nothing to paint with.", this);
                return false;
            }

            if (View == null)
            {
                Debug.LogWarning("[Area] No camera to cast the cursor from, and none tagged "
                    + "MainCamera to fall back on. Assign one on this component.", this);
                return false;
            }

            if (service == null)
            {
                Debug.LogWarning("[Area] No FurniturePlacementService beside this painter, so "
                    + "there is no way to tell floor from furniture.", this);
                return false;
            }

            // The floor rectangles are a snapshot, and the player has very likely just been laying
            // ground. A stale list would refuse to paint over floor that is plainly there.
            service.RefreshFloors();

            // An area is cells of floor, so with no floor anywhere there is nothing this gesture
            // could produce. Said plainly here rather than left to be discovered: starting the
            // session anyway gives a brush that is red everywhere it goes and paints nothing, which
            // reads as a broken brush rather than as an empty world. The dynamic scene starts with
            // its authored environment switched off, so this is the ordinary state of a fresh world
            // and not an error.
            if (service.FloorCount == 0)
            {
                Debug.LogWarning("[Area] There is no floor in the world yet, so there is "
                    + "nowhere to paint an area. Lay some ground from the FLOOR tab first, then "
                    + "define the area over it.", this);
                return false;
            }

            IsActive = true;
            painting = false;
            erasing = false;

            cells.Clear();
            Seed(existing);

            Redraw();

            SetSuppressed(true);

            // A fresh session never inherits the last one's erase toggle. Opening the brush and
            // finding the first stroke deletes instead of paints is the kind of surprise that
            // costs a player their area.
            EraseMode = false;

            Log("BEGAN, seeded with " + cells.Count + " cell(s); hold the "
                + "left button to paint, Ctrl (or the Erase Area toggle) to erase, wheel to resize "
                + "the brush, right-click to commit, Esc to cancel");

            return true;
        }

        /// <summary>
        /// Takes the cells an area was saved with back to grid coordinates.
        /// </summary>
        /// <remarks>
        /// The saved area is world points, so a grid that has moved or changed cell size since the
        /// area was painted lands the seeded cells somewhere slightly different from where they
        /// were drawn. That is the right trade: the points are still the floor the player painted,
        /// and rounding them onto today's grid is a shift of at most half a cell, where trusting a
        /// stored cell coordinate against a moved grid would be a shift of anything at all.
        /// </remarks>
        void Seed(List<Vector3> existing)
        {
            if (existing == null || service.Grid == null)
            {
                return;
            }

            foreach (var point in existing)
            {
                // On the floor the point's height names, so an area painted upstairs is drawn
                // upstairs again.
                var cell = service.Grid.WorldToCell(point);
                var key = PlacementGrid.KeyFor(service.Grid.StoreyAt(point.y), PlacementGrid.FloorLevel);
                cells.Add(new Vector3Int(cell.x, cell.y, key));
            }
        }

        /// <summary>Ends the session, keeping nothing. The area on disk is left as it was.</summary>
        public void Cancel()
        {
            if (!IsActive)
            {
                return;
            }

            End();
            Log("CANCELLED");

            var handler = Cancelled;
            if (handler != null)
            {
                handler();
            }
        }

        /// <summary>
        /// Ends the session and hands the painted cells over. An empty area commits as empty, which
        /// is how a player takes an agent's area away again.
        /// </summary>
        public void Commit()
        {
            if (!IsActive)
            {
                return;
            }

            // Built before End clears the set, and handed over rather than shared: the listener
            // stores this on the credential it saves, and a list the painter went on to reuse
            // would quietly rewrite an area that had already been saved.
            var points = new List<Vector3>(cells.Count);
            foreach (var cell in cells)
            {
                points.Add(CentreOf(cell));
            }

            End();
            Log("COMMITTED " + points.Count + " cell(s)");

            var handler = Committed;
            if (handler != null)
            {
                handler(points);
            }
        }

        void End()
        {
            IsActive = false;
            painting = false;
            erasing = false;

            // Erase mode belongs to the session, not to the painter, so it goes out with it —
            // whichever way the session ended. Every exit runs through here, which is why it is
            // set here rather than in Commit and Cancel separately: a third exit added later gets
            // the same treatment without anybody remembering to add it.
            SetEraseMode(false);

            cells.Clear();
            panels.Clear();

            if (highlight != null)
            {
                highlight.ClearPaint();
                highlight.Hide();
            }

            SetSuppressed(false);
        }

        void Update()
        {
            if (!IsActive)
            {
                return;
            }

            var mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            if (Abandoned())
            {
                Cancel();
                return;
            }

            var keyboard = Keyboard.current;

            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                Cancel();
                return;
            }

            if (keyboard != null && keyboard.deleteKey.wasPressedThisFrame && cells.Count > 0)
            {
                Log("cleared " + cells.Count + " cell(s)");
                cells.Clear();
                dirty = true;
            }

            Resize(mouse);

            // The right button commits, and it is read before the brush so a click that both
            // finishes a stroke and commits does not paint one last cell after the commit.
            if (mouse.rightButton.wasPressedThisFrame && !OverUI())
            {
                // One press ends the whole thing — erase mode and the session together. It briefly
                // took two presses, erase off then commit, and that was worse: right-click means
                // "I am done here" and having it sometimes mean "half done" is a hassle rather
                // than a safeguard. Commit drops erase mode on its way out.
                Commit();
                return;
            }

            TrackBrush(mouse, keyboard);

            if (dirty)
            {
                Redraw();
            }
        }

        /// <summary>
        /// True when something else has taken the gesture over and the session cannot continue.
        /// </summary>
        /// <remarks>
        /// Only <see cref="PlacementController"/> can do this, by being handed a piece while the
        /// brush was up — the inventory drawer is open throughout a paint session and its tiles
        /// still work. Ending here rather than fighting over the cursor: two gestures both moving
        /// something under the same mouse is worse than losing the newer one.
        /// </remarks>
        bool Abandoned() => placement != null && placement.IsBusy;

        /// <summary>The wheel grows and shrinks the brush. Free to use: the rig has stood down.</summary>
        void Resize(Mouse mouse)
        {
            var scroll = mouse.scroll.ReadValue().y;

            if (Mathf.Approximately(scroll, 0f))
            {
                return;
            }

            var wanted = brushCells + (scroll > 0f ? 1 : -1);
            var clamped = Mathf.Clamp(wanted, Mathf.Max(1, minBrushCells), Mathf.Max(1, maxBrushCells));

            if (clamped == brushCells)
            {
                return;
            }

            brushCells = clamped;
            Log("brush is now " + brushCells + " cell(s) across");
        }

        /// <summary>
        /// Moves the brush light to the floor under the cursor and, while the left button is down,
        /// paints or erases the cells it covers.
        /// </summary>
        void TrackBrush(Mouse mouse, Keyboard keyboard)
        {
            // A press that lands on the drawer belongs to the drawer. Without this the very click
            // that opened the session — the pointer still over the Define Area button — would
            // start a stroke, and every tile click afterwards would paint underneath the panel.
            if (mouse.leftButton.wasPressedThisFrame && !OverUI())
            {
                painting = true;

                // Two ways to erase, and they mean the same thing to everything downstream: hold
                // Ctrl for one stroke, or latch EraseMode from the panel and keep both hands free.
                // The latched form exists because the obvious gesture — right-drag — is already
                // taken by commit, and because a visible button is findable where a modifier is not.
                erasing = EraseMode
                    || (keyboard != null
                        && (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed));
            }

            if (mouse.leftButton.wasReleasedThisFrame)
            {
                painting = false;
                erasing = false;
            }

            Vector3 point;
            float top;

            if (!TryFloorUnderCursor(mouse, out point, out top))
            {
                // Off the floor entirely: the brush turns red where it is rather than vanishing, so
                // the player can see that the cursor is the problem and not the button.
                ShowBrush(point, top, false);
                return;
            }

            ShowBrush(point, top, true);

            if (!painting || OverUI())
            {
                return;
            }

            Paint(point);
        }

        /// <summary>Adds or takes away every cell the brush covers, floor cells only.</summary>
        void Paint(Vector3 centre)
        {
            var grid = service.Grid;
            if (grid == null)
            {
                return;
            }

            var footprint = new Vector2Int(brushCells, brushCells);
            var origin = grid.OriginFor(centre, footprint, PlacementGrid.FloorLevel);

            grid.CellsFor(origin, footprint, brush);

            foreach (var cell in brush)
            {
                if (erasing)
                {
                    dirty |= cells.Remove(cell);
                    continue;
                }

                // Tested per cell rather than once for the whole brush. A brush wider than the
                // floor it is over would otherwise paint past the edge of the ground, and an area
                // is meant to be somewhere an agent can actually stand.
                float ignored;
                if (!service.TryFloorTopAt(CentreOf(cell), out ignored))
                {
                    continue;
                }

                dirty |= cells.Add(cell);
            }
        }

        /// <summary>Lays a panel on every painted cell.</summary>
        /// <remarks>
        /// One panel per cell rather than a merged outline of the region. The cells are what is
        /// saved, and a player who has painted a ragged edge or left a hole in the middle should
        /// see exactly that rather than a tidied rectangle that is not what they will get.
        /// </remarks>
        void Redraw()
        {
            dirty = false;
            panels.Clear();

            var size = CellSize();

            foreach (var cell in cells)
            {
                var centre = CentreOf(cell);

                float top;
                if (!service.TryFloorTopAt(centre, out top))
                {
                    // The floor under it has gone since it was painted. Kept in the set — an area
                    // is the player's and the ground may come back — but there is nothing to draw
                    // it on.
                    continue;
                }

                panels.Add(new PlacementHighlight.MarkedArea
                {
                    Centre = centre,
                    Top = top,
                    Size = new Vector2(size, size),
                    Rotation = Quaternion.identity,
                });
            }

            highlight.PaintAll(panels);
        }

        void ShowBrush(Vector3 centre, float top, bool overFloor)
        {
            var width = CellSize() * brushCells;

            // Red while erasing. The highlight's "invalid" colour is doing double duty here — it
            // already means "this will not add anything", which is exactly true of a brush that
            // takes cells away. Sharing it keeps one colour vocabulary rather than inventing a
            // third: green adds, red does not.
            var showAsValid = overFloor && !EraseMode;

            highlight.Track(centre, top, new Vector2(width, width), Quaternion.identity, showAsValid);
        }

        float CellSize()
        {
            var grid = service.Grid;
            return grid != null ? grid.CellSize : 0.25f;
        }

        /// <summary>Middle of a cell, lifted to the floor it sits on.</summary>
        Vector3 CentreOf(Vector3Int cell)
        {
            var grid = service.Grid;

            if (grid == null)
            {
                return Vector3.zero;
            }

            var centre = grid.CellCentre(cell);

            float top;
            centre.y = service.TryFloorTopAt(centre, out top) ? top : centre.y;

            return centre;
        }

        /// <summary>
        /// Where the cursor meets the floor, and how high that floor is.
        /// </summary>
        /// <remarks>
        /// Not a physics raycast, for the same reason <see cref="PlacementController"/> does not
        /// use one to aim a dragged piece: the scene is full of desktop-prop colliders on the
        /// default layer, and the nearest thing a ray from an isometric camera meets is very often
        /// a monitor or a wall rather than the ground. The floor's height is the exact answer, so a
        /// plane at that height cannot be got in the way of.
        /// <para>
        /// Solved in two passes because plane and height each depend on the other: the first drops
        /// the cursor onto the viewed floor's own level to find out <em>which</em> tile is being
        /// pointed at, and the second re-drops it onto that tile's height. One correction is enough
        /// — the tiles of one floor are within a few centimetres of each other, and the residual
        /// error after the second pass is far inside one 0.25m cell.
        /// </para>
        /// </remarks>
        bool TryFloorUnderCursor(Mouse mouse, out Vector3 point, out float floorTop)
        {
            floorTop = 0f;

            Vector3 flat;
            if (!TryCursorOnPlane(mouse, storeys != null ? storeys.ViewedBase : 0f, out flat))
            {
                // Camera looking parallel to the ground: there is no intersection to paint on.
                point = Vector3.zero;
                return false;
            }

            float top;
            if (!service.TryFloorTopAt(flat, out top))
            {
                point = flat;
                return false;
            }

            Vector3 corrected;
            if (!TryCursorOnPlane(mouse, top, out corrected))
            {
                point = flat;
                return false;
            }

            // Re-asked at the corrected point: the correction can carry the cursor off the slab it
            // was over, and painting the edge of a floor the cursor has already left is exactly the
            // half-cell error this pass exists to remove.
            float settled;
            if (!service.TryFloorTopAt(corrected, out settled))
            {
                point = corrected;
                return false;
            }

            corrected.y = settled;

            point = corrected;
            floorTop = settled;

            return true;
        }

        bool TryCursorOnPlane(Mouse mouse, float planeY, out Vector3 point)
        {
            var ray = View.ScreenPointToRay(CursorPixel(mouse));
            var plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));

            float distance;
            if (plane.Raycast(ray, out distance) && distance <= rayDistance)
            {
                point = ray.GetPoint(distance);
                return true;
            }

            point = Vector3.zero;
            return false;
        }

        /// <summary>
        /// The cursor in screen pixels, held inside the screen.
        /// </summary>
        /// <remarks>
        /// The mouse device keeps reporting a position while the pointer is outside the game view,
        /// and on a monitor left of the view it reads thousands of pixels negative — x = -4208
        /// against a 1920-wide screen, in the case this was written for. A ray through that pixel
        /// meets the ground far outside any floor, so the brush would report "not over floor"
        /// wherever the player actually was. Clamping stops it at the edge of the view instead,
        /// which is recoverable; inside the view it changes nothing.
        /// </remarks>
        static Vector2 CursorPixel(Mouse mouse)
        {
            var pixel = mouse.position.ReadValue();

            return new Vector2(
                Mathf.Clamp(pixel.x, 0f, Screen.width - 1f),
                Mathf.Clamp(pixel.y, 0f, Screen.height - 1f));
        }

        bool OverUI() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        /// <summary>
        /// Stands the camera rig and the placement gesture down for the session, and hands them
        /// back after.
        /// </summary>
        /// <remarks>
        /// Two collaborators, both reached by component lookup on objects this painter can see: the
        /// rig owns the right button that commits here and the wheel that resizes the brush, and
        /// the placement controller lifts a piece on a quarter-second press-and-hold, which is
        /// exactly what painting looks like to it — brushing across a desk would otherwise pick the
        /// desk up mid-stroke.
        /// <para>
        /// The owner's <c>ManagerController</c> is deliberately absent. A left press does dispatch
        /// him, and he does have to stand down for a stroke, but he is the placement controller's
        /// business and not this class's: <c>SuppressGesture</c> passes it on. The brush has no
        /// reason to know the owner exists, and coupling it to him meant a scene with no owner in
        /// it could not resolve a painter at all.
        /// </para>
        /// </remarks>
        void SetSuppressed(bool suppressed)
        {
            if (Rig != null)
            {
                Rig.InputSuppressed = suppressed;
            }

            if (placement != null)
            {
                placement.SuppressGesture = suppressed;
            }
        }

        void Log(string message)
        {
            if (logGesture)
            {
                Debug.Log("[Area] " + message, this);
            }
        }

        void OnDisable()
        {
            // A session cannot survive the painter being switched off: the brush would be left
            // lit on the floor with nothing reading the mouse, and the rig would stay suppressed.
            if (IsActive)
            {
                Cancel();
            }
        }
    }
}
