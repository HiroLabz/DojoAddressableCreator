using System.Collections.Generic;
using Dojo.Framework.Events;
using Dojo.Framework.UI;
using Dojo.Game.Components;
using Dojo.Game.Events;
using Dojo.Game.Managers;
using Dojo.Game.InGame.Agents;
using Dojo.Game.InGame.Controllers;
using Dojo.Game.Systems;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using VContainer;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// The whole furniture gesture: long-press a piece to lift it, drag it to a cell, release to
    /// drop it, scroll to turn it, Delete to throw it away, click to commit or Esc to put it back.
    /// </summary>
    /// <remarks>
    /// Runs ahead of <see cref="ManagerController"/> via <see cref="DefaultExecutionOrder"/>,
    /// because that controller acts on pointer-<em>down</em>. Claiming the press a frame later
    /// would be too late — the manager would already be walking before the hold threshold elapsed.
    /// Ordering it first lets this claim the press in the same frame it happens.
    /// </remarks>
    [DefaultExecutionOrder(-100)]
    public sealed class PlacementController : MonoBehaviour
    {
        /// <summary>Where the gesture currently is. Mirrors the state machine in the GDD.</summary>
        enum State
        {
            /// <summary>Nothing held. Clicks belong to the manager.</summary>
            Idle,

            /// <summary>A piece is following the cursor and obstructing nothing.</summary>
            Dragging,

            /// <summary>
            /// Let go of, but not committed. The piece keeps following the cursor with the button
            /// released; the middle button turns it, and only a click puts it down.
            /// </summary>
            Pending,
        }

        [Header("Wiring")]
        [Tooltip("Camera the cursor is cast from. Defaults to Camera.main.")]
        [SerializeField] Camera view;

        [Tooltip("Layers a piece can be picked up from. Should be just Furniture.")]
        [SerializeField] LayerMask pickable = ~0;

        [Header("Feel")]
        [Tooltip("Log each step of the gesture to the console. For working out why a press did " +
                 "not take, without guessing.")]
        [SerializeField] bool logGesture;

        [Tooltip("How far the click ray reaches. The isometric camera sits well back.")]
        [SerializeField] float rayDistance = 500f;

        [Header("Inventory drops")]
        [Tooltip("Layer a piece rides on while it is being dragged out of the inventory drawer. " +
                 "Drawn by the overlay camera stacked on the view, which is what puts the piece in " +
                 "front of the panel it came from. The main camera must not render this layer, or " +
                 "the piece is drawn twice and the copy behind the panel shows through it.")]
        [SerializeField] string ghostLayerName = "PlacementGhost";

        [Tooltip("Layer a piece joins once it has been committed to the world. Must be a layer the " +
                 "pickable mask above includes, or a piece could be put down and then never picked " +
                 "up again.")]
        [SerializeField] string placedLayerName = "Furniture";

        [Tooltip("How close a floor's edge must come to another floor's before it is pulled flush " +
                 "against it, in metres. Ground cannot tile on the grid alone - the pack's slabs " +
                 "are 19.56 cells across - so edges are matched to each other instead. Zero turns " +
                 "the behaviour off.")]
        [SerializeField] float floorSnapDistance = 0.4f;

        [Header("Deleting")]
        [Tooltip("Wording of that question when one piece is selected.")]
        [SerializeField] string deleteQuestion = "Delete this item?";

        [Tooltip("Wording when several are selected. {0} is how many.")]
        [SerializeField] string deleteQuestionMany = "Delete these {0} items?";

        FurniturePlacementService service;
        PlacementHighlight highlight;
        CameraRig rig;

        // Both handed over by the scene's LifetimeScope. The manager was found with
        // FindAnyObjectByType and the root with GameObject.Find by name, which meant a renamed
        // object took the world silently and pieces started appearing at the top of the scene.
        ManagerController manager;
        WorldRoot world;

        State state = State.Idle;
        FurniturePiece held;

        // True while the piece in hand was made for this gesture rather than picked up off the
        // floor. Such a piece has nowhere to be put back to, so cancelling destroys it.
        bool heldFromInventory;

        int ghostLayer;
        int placedLayer;

        // The one owner of every modal in the game, handed over by the scene's LifetimeScope.
        IDialogManager dialogs;

        // The scene's container. Pieces coming out of the drawer are built through it rather than
        // through Object.Instantiate, so a component on the art that expects injection gets it.
        IObjectResolver resolver;

        // Where "the world changed" is announced. This controller does not know or care that an
        // auto save is listening — the same reason the call buttons publish rather than reaching
        // for an agent.
        IEventHub hub;

        // Every navigation mesh the world builds into - one per agent type. Laying a floor has to
        // update all of them, because each agent type walks its own baked copy of the world.
        Unity.AI.Navigation.NavMeshSurface[] Surfaces => world != null ? world.Surfaces : null;

        // What the piece in hand would be standing on if it were dropped now. Null means the floor.
        FurniturePiece previewSupport;

        // True when the piece was picked up off another piece rather than off the floor. Such a
        // piece has no authored floor height of its own to go back to, so on the floor it takes the
        // floor's; one lifted from the floor keeps its own, because furniture is authored sunk into
        // the slab.
        bool heldKeepsOwnHeight;

        // Clutter picked up along with the piece it was sitting on. Parented to it for the duration
        // of the drag, so it follows every move and every quarter turn for free.
        readonly List<FurniturePiece> riders = new List<FurniturePiece>();

        // The held piece's rotation when the riders were gathered, so the turn it has been through
        // can be folded into each rider's own rotation when they are put down.
        int ridersLiftedAtSteps;

        // Reused by the delete question so asking does not allocate a list every time.
        readonly List<GameObject> standingOn = new List<GameObject>();

        // Pieces added with Ctrl+click while one is pending. The piece in hand is deliberately not
        // in here — it is what the gesture is already about — so what a delete removes is it, these,
        // and whatever is standing on any of them.
        readonly List<FurniturePiece> selection = new List<FurniturePiece>();

        readonly List<PlacementHighlight.MarkedArea> selectionMarks = new List<PlacementHighlight.MarkedArea>();

        // Floors the piece in hand is currently pulled flush against. Highlighted while it is, so
        // the snap is something the player sees rather than something they hope happened.
        readonly List<FurniturePiece> snappedTo = new List<FurniturePiece>();

        // Everything one delete is about to destroy, gathered before any of it is.
        readonly List<GameObject> condemned = new List<GameObject>();

        // Where the cursor met the piece's own height on the press that lifted it.
        Vector3 pressGroundPoint;
        bool pressHasGround;

        Vector3Int previewOrigin;
        PlacementRejection previewRejection = PlacementRejection.None;

        // The last cell the piece could legally have gone to during this drag. A release over an
        // illegal cell falls back to this, so the pending state is always committable and the
        // player can never be left holding a piece that refuses to go down anywhere.
        Vector3Int lastValidOrigin;
        bool hasValidOrigin;

        // Where the piece sat relative to the cursor's floor point when it was grabbed. Without
        // this the piece's centre snaps to the cursor's floor point on lift, which on an isometric
        // camera is about a metre behind where the player actually grabbed it — the piece appears
        // to leap out from under the cursor the instant it lifts.
        Vector3 grabOffset;

        // Last pixel a drag line was written for, so the log follows mouse movement rather than
        // frame rate: a stationary cursor would otherwise fill the console at 60 lines a second.
        Vector2 lastLoggedPixel;

        /// <summary>True while a piece is being moved or is awaiting a commit.</summary>
        public bool IsBusy => state != State.Idle;

        /// <summary>The piece under the player's control, or null.</summary>
        public FurniturePiece Held => held;

        /// <summary>
        /// Stops a new gesture being started, while leaving one already running alone.
        /// </summary>
        /// <remarks>
        /// Set by <see cref="AreaPainter"/> for the length of an area session, and the exact
        /// counterpart of the <c>SuppressClick</c> this controller sets on the manager. Painting
        /// holds the left button down and drags it across the floor, which is indistinguishable
        /// from the press-and-hold that lifts a piece: without this, brushing over a desk for a
        /// quarter of a second picks the desk up in the middle of the stroke.
        /// <para>
        /// Only the idle watch is suppressed, deliberately. A drag or a pending piece must still be
        /// finishable — and cannot in fact coexist with a session, because
        /// <see cref="AreaPainter.Begin"/> refuses to start while <see cref="IsBusy"/>.
        /// </para>
        /// <para>
        /// The manager is stood down with it, and that is on purpose: he acts on left-press, so a
        /// paint stroke would otherwise send him walking to wherever it started. It is passed on
        /// here rather than done by the brush because the manager is <em>this</em> class's
        /// collaborator — the brush has no business knowing the owner exists, and a scene with no
        /// owner in it should still be able to paint an area.
        /// </para>
        /// </remarks>
        public bool SuppressGesture
        {
            get { return suppressGesture; }
            set
            {
                suppressGesture = value;
                SetManagerSuppressed(value);
            }
        }

        bool suppressGesture;

        void Awake()
        {
            service = GetComponent<FurniturePlacementService>();

            highlight = GetComponent<PlacementHighlight>();
            if (highlight == null)
            {
                highlight = gameObject.AddComponent<PlacementHighlight>();
            }

            if (view == null)
            {
                view = Camera.main;
            }

            ghostLayer = ResolveLayer(ghostLayerName);
            placedLayer = ResolveLayer(placedLayerName);
        }

        /// <summary>
        /// A layer by name, or layer 0 with a complaint. Named rather than a <c>LayerMask</c> field
        /// because both of these are one layer and a mask would let somebody tick four.
        /// </summary>
        int ResolveLayer(string name)
        {
            var layer = LayerMask.NameToLayer(name);
            if (layer >= 0)
            {
                return layer;
            }

            Debug.LogError("[Placement] There is no layer called '" + name + "'. Add it under "
                + "Edit > Project Settings > Tags and Layers, or inventory drops will be drawn on "
                + "the default layer.", this);

            return 0;
        }

        void Start()
        {
            // The rig is on the camera this controller was given, so it is a component lookup on a
            // known object rather than a search of the scene.
            rig = view != null ? view.GetComponent<CameraRig>() : null;
        }

        /// <summary>
        /// Rebuilds every navigation mesh, and sets everyone walking again once it is done.
        /// </summary>
        /// <remarks>
        /// Laying a floor is the one change carving cannot express. A NavMeshObstacle can only take
        /// walkable space away; a new floor <em>adds</em> somewhere to walk, and the only way to
        /// tell navigation about it is to build the surface again.
        /// <para>
        /// Updated rather than rebuilt from scratch, which is both cheaper and asynchronous, and
        /// the actors are re-pathed on completion rather than immediately — a path worked out
        /// against the old surface would not know about the floor that has just appeared.
        /// </para>
        /// </remarks>
        void RebuildNavigation()
        {
            var surfaces = Surfaces;

            if (surfaces == null || surfaces.Length == 0)
            {
                Debug.LogWarning("[Placement] No NavMeshSurface in the scene, so a new floor cannot "
                    + "be walked on. Add one, or nothing will path across what the player builds.", this);
                return;
            }

            var rebuilt = 0;

            foreach (var surface in surfaces)
            {
                if (surface == null || !surface.isActiveAndEnabled)
                {
                    continue;
                }

                if (surface.navMeshData == null)
                {
                    surface.BuildNavMesh();
                    RepathActors();
                }
                else
                {
                    surface.UpdateNavMesh(surface.navMeshData).completed += _ => RepathActors();
                }

                rebuilt++;
            }

            Log("navigation rebuilt across " + rebuilt + " surface(s)");
        }

        void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null || view == null)
            {
                return;
            }

            switch (state)
            {
                case State.Idle:
                    TickIdle(mouse);
                    break;

                case State.Dragging:
                    TickDragging(mouse);
                    break;

                case State.Pending:
                    TickPending(mouse);
                    break;
            }
        }

        void TickIdle(Mouse mouse)
        {
            if (suppressGesture)
            {
                // The manager is deliberately not handed back: whoever suppressed this owns the
                // button and is suppressing the manager too, and will release both when it is
                // finished.
                pressHasGround = false;
                return;
            }

            var keyboard = Keyboard.current;

            // Right-click ends the selection and lets everything go. Only meaningful while
            // something is selected, so an ordinary right-click in an empty room is still the
            // camera's.
            if (mouse.rightButton.wasPressedThisFrame && selection.Count > 0)
            {
                ClearSelection();
                return;
            }

            // Delete, with nothing in hand, acts on the selection alone. Without this the marks
            // could be made and never used, because the only other delete lives in the gesture.
            if (keyboard != null && keyboard.deleteKey.wasPressedThisFrame && selection.Count > 0)
            {
                AskToDelete();
                return;
            }

            if (mouse.leftButton.wasPressedThisFrame && !OverUI())
            {
                // Ctrl marks a piece instead of lifting it, which is what starts a multiple
                // selection. Tested before the press is watched, because watching it lifts, and a
                // lifted piece is the one thing the player did not ask for here.
                if (keyboard != null && keyboard.ctrlKey.isPressed)
                {
                    if (EditingAllowed)
                    {
                        ToggleSelected(mouse);
                    }

                    return;
                }

                WatchPress(mouse);
            }
        }

        /// <summary>
        /// Drops the whole selection and takes its marks off the floor.
        /// </summary>
        /// <remarks>
        /// The pieces themselves are untouched — they were never lifted, only marked, so letting
        /// go of them is nothing more than forgetting which ones they were.
        /// </remarks>
        void ClearSelection()
        {
            Log("selection cancelled - " + selection.Count + " piece(s) let go");

            selection.Clear();
            RefreshMarks();
        }

        /// <summary>
        /// Picks up the piece under the cursor, and takes the button away from the manager for the
        /// length of the gesture so a lift never also dispatches him.
        /// </summary>
        /// <remarks>
        /// A press lifts immediately. It used to require the button be held still for a quarter of
        /// a second first, which made a piece unmovable in practice: pressing and dragging — the
        /// one gesture everybody actually uses to move a thing — moves the cursor past the slack
        /// long before the timer fires, so the gesture was abandoned every time.
        /// <para>
        /// The hold bought nothing anyway. It existed to tell "lift this" apart from "walk the
        /// owner here", but this method returns above on <see cref="EditingAllowed"/>, so it only
        /// ever ran in Edit — where the owner is deaf to clicks by design and there is nothing to
        /// tell apart.
        /// </para>
        /// </remarks>
        void WatchPress(Mouse mouse)
        {
            if (!EditingAllowed)
            {
                // The single gate for lifting anything already in the room. Refused silently
                // rather than loudly: in Play phase a click on the sofa is not a mistake, it is
                // just a click on the sofa, and the owner is the one who should answer it.
                return;
            }

            int hits;
            var nearest = PieceUnderCursor(mouse, out hits);

            if (nearest == null)
            {
                Log("press hit no FurniturePiece (" + hits + " hits on the pick mask) - "
                    + "is this piece set up for placement?");
                return;
            }

            // An occupied chair simply does not respond, rather than refusing loudly.
            if (!nearest.CanLift)
            {
                Log("press on " + nearest.gameObject.name + " refused: in use");
                return;
            }

            // Worked out before the lift, because BeginDrag needs it to keep the piece under the
            // cursor exactly where it was grabbed instead of jumping.
            pressHasGround = TryCursorOnPlane(mouse, nearest.transform.position.y, out pressGroundPoint);

            SetManagerSuppressed(true);
            BeginDrag(nearest);
        }

        void Log(string message)
        {
            if (logGesture)
            {
                Debug.Log("[Placement] " + message, this);
            }
        }

        /// <summary>
        /// A loggable per-object id, on whichever API this Unity version still has.
        /// </summary>
        /// <remarks>
        /// <c>GetInstanceID()</c> became a hard compile error in 6000.6 in favor of
        /// <c>GetEntityId()</c> — but that replacement doesn't exist before 6000.5, and this
        /// project's pinned version (6000.3.23f1) predates it. Neither API alone compiles on both,
        /// so this picks whichever one the running Editor actually has.
        /// </remarks>
        static object EntityLogId(UnityEngine.Object obj)
        {
#if UNITY_6000_5_OR_NEWER
            return obj.GetEntityId();
#else
            return obj.GetInstanceID();
#endif
        }

        /// <summary>
        /// Says that the world now differs from what is on disk.
        /// </summary>
        /// <remarks>
        /// Published rather than acted on, so this controller stays unaware that anything saves at
        /// all — the same arrangement the call buttons have with the agents. Anything that wants
        /// to react subscribes.
        /// <para>
        /// A null hub is passed over in silence. This component is injected in every scene that
        /// has a placement system, and one of them may have no hub registered; a warning per
        /// placement would be worse than the missing auto save it is warning about.
        /// </para>
        /// </remarks>
        void Announce(string reason)
        {
            if (hub != null)
            {
                hub.Publish(new WorldChanged(reason));
            }
        }

        /// <summary>
        /// Reports which object is in hand and where it is, once per actual mouse movement. Names
        /// alone are ambiguous here — the scene holds 25 objects called <c>(Prb)OfficeChair</c> —
        /// so the instance id goes out with it to identify exactly which one is moving.
        /// </summary>
        void LogDragMove(Mouse mouse, Vector3 cursorWorld)
        {
            if (!logGesture)
            {
                return;
            }

            var pixel = CursorPixel(mouse);
            if ((pixel - lastLoggedPixel).sqrMagnitude < 1f)
            {
                return;
            }

            lastLoggedPixel = pixel;

            Log("drag '" + held.gameObject.name + "' id=" + EntityLogId(held)
                + " pos=" + held.transform.position.ToString("F3")
                + " cell=" + previewOrigin
                + " cursorWorld=" + cursorWorld.ToString("F3")
                + " screen=(" + pixel.x.ToString("F0") + "," + pixel.y.ToString("F0") + ")"
                + " " + previewRejection);
        }

        void BeginDrag(FurniturePiece piece)
        {
            Log("LIFTED " + piece.gameObject.name + " - drag it, release to drop");

            held = piece;
            state = State.Dragging;

            // Released before the drag rather than on commit, so the piece can be dropped
            // overlapping the cells it came from without failing its own validity test.
            held.Lift();
            service.Release(held);

            // Where it came from is legal by definition, so a drag that never finds anywhere else
            // legal still has somewhere to settle.
            lastValidOrigin = held.OriginCell;
            hasValidOrigin = true;

            // Keeps the piece under the cursor exactly where it was grabbed instead of jumping.
            // A flat XZ offset worked out once at lift, so it stays true however the piece's height
            // changes as it moves between the floor and a desk top.
            grabOffset = pressHasGround ? held.transform.position - pressGroundPoint : Vector3.zero;
            grabOffset.y = 0f;

            // Lifted off another piece, so there is no authored floor height to return to.
            heldKeepsOwnHeight = piece.SupportedBy == null;

            // Cleared so the first movement of this drag always reports, however small.
            lastLoggedPixel = Vector2.zero;

            GatherRiders(piece);

            Log("PICKED '" + piece.gameObject.name + "' id=" + EntityLogId(piece)
                + " at " + piece.transform.position.ToString("F3")
                + " cell=" + piece.OriginCell
                + " grabOffset=" + grabOffset.ToString("F3")
                + " riders=" + riders.Count);

        }

        /// <summary>
        /// Collects the clutter standing on a piece about to be moved, and parents it to that piece
        /// so it follows every move and every quarter turn without being tracked frame by frame.
        /// </summary>
        /// <remarks>
        /// Nothing rides on a piece that is itself standing on something, because only two levels
        /// exist. Each rider gives up its cells for the length of the drag exactly as the piece
        /// under it does, so the whole group can be put back down overlapping where it stood.
        /// </remarks>
        void GatherRiders(FurniturePiece piece)
        {
            riders.Clear();
            ridersLiftedAtSteps = piece.RotationSteps;

            // Two levels only, so something already standing on furniture carries nothing itself.
            if (piece.SupportedBy != null)
            {
                return;
            }

            foreach (var prop in FindObjectsByType<FurniturePiece>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (prop.SupportedBy != piece)
                {
                    continue;
                }

                riders.Add(prop);
                service.Release(prop);
                prop.Lift();
                prop.transform.SetParent(piece.transform, true);
            }
        }

        /// <summary>
        /// Puts the clutter back down wherever the group has ended up, whether that was a commit or
        /// a cancel. Either way the riders are already sitting in the right place, because they were
        /// carried there; all that is left is to let go of them and re-claim the cells.
        /// </summary>
        void SettleRiders()
        {
            if (riders.Count == 0)
            {
                return;
            }

            // How far the piece underneath was turned while they were carried. Applied to each
            // rider's own rotation so its footprint agrees with the way it now looks.
            var turned = held != null ? held.RotationSteps - ridersLiftedAtSteps : 0;

            foreach (var prop in riders)
            {
                if (prop == null)
                {
                    continue;
                }

                prop.transform.SetParent(null, true);
                prop.SetRotationSteps(prop.RotationSteps + turned);
                prop.Settle();

                // Registered rather than claimed: it was legal where it stood and it has been
                // carried, not re-placed, so it keeps its spot even if the group has come to rest
                // somewhere that would not have accepted it from scratch.
                service.Register(prop);
            }

            riders.Clear();
        }

        /// <summary>
        /// Lifts a piece straight out of the inventory: builds <paramref name="prefab"/> under the
        /// cursor and hands it to the same drag the floor gesture uses, so snapping, the highlight,
        /// rotation, the rejection rules and the commit are all the one code path.
        /// </summary>
        /// <param name="prefabId">
        /// Asset GUID of the catalogue entry, recorded on the piece so a saved world can build it
        /// again. The piece is not a prefab instance by the time the spawner has finished with it,
        /// so nothing else remembers where it came from.
        /// </param>
        /// <param name="resource">Resources path of the same prefab, which is what a load can use.</param>
        /// <returns>
        /// False when nothing was lifted — a gesture was already running, or the prefab could not
        /// be made into a piece. The caller is expected to leave its own gesture alone in that case.
        /// </returns>
        public bool BeginDragFromInventory(GameObject prefab, string prefabId = null, string resource = null)
        {
            if (state != State.Idle)
            {
                Log("inventory drag refused: a gesture is already running");
                return false;
            }

            var mouse = Mouse.current;
            if (mouse == null || view == null || prefab == null)
            {
                return false;
            }

            // The floor plane is not known until there is a point to ask about, so the cursor is
            // dropped onto the viewed floor's own level first and the answer only used to correct
            // the height. On a floor with no tiles yet that level is where a first tile goes.
            var baseHeight = storeys != null ? storeys.ViewedBase : 0f;

            Vector3 ground;
            if (!TryCursorOnPlane(mouse, baseHeight, out ground))
            {
                Log("inventory drag refused: the cursor is not over the ground plane");
                return false;
            }

            float floorTop;
            if (service.TryFloorTopAt(ground, out floorTop))
            {
                ground.y = floorTop;
            }

            // Onto the floor being looked at: that is the floor the player is building on.
            var parent = storeys != null ? storeys.Floor(storeys.Viewed) : world.Root;

            var piece = FurnitureSpawner.Spawn(
                prefab, service.Grid, ground, ghostLayer, service.MaxFootprintCells, parent,
                resolver);

            if (piece == null)
            {
                return false;   // the spawner has already said why
            }

            var placed = piece.GetComponent<PlacedPiece>();
            if (placed != null)
            {
                placed.Set(prefabId, resource);
            }

            held = piece;
            heldFromInventory = true;

            // Straight to pending, not dragging. The piece arrives from a click, so no button is
            // held and there is no release coming to end a drag with — dragging would sit there
            // until the next click, and spend that click going pending instead of putting the piece
            // down. Pending already follows the cursor and commits on a click, which is the whole
            // of what taking something out of the drawer should feel like.
            state = State.Pending;
            SetRigSuppressed(true);

            held.Lift();

            // Unlike a piece off the floor there is no cell it came from, so until the drag finds
            // somewhere legal there is nowhere to fall back to and a release simply discards it.
            hasValidOrigin = false;
            lastValidOrigin = Vector3Int.zero;

            // A piece pulled out of a list is held by its middle: there is no grab point on the
            // model to preserve, because the player grabbed a thumbnail.
            grabOffset = Vector3.zero;
            lastLoggedPixel = Vector2.zero;

            SetManagerSuppressed(true);

            // Built on the floor rather than authored into it, so it takes whatever height the
            // surface under it has rather than keeping the one it was spawned at. Ground is the
            // exception: floors tile side by side at one level rather than climbing onto each other.
            heldKeepsOwnHeight = piece.IsFloor;

            // Aimed at whatever is under the cursor rather than the ground point it was built on,
            // so a monitor dragged straight onto a desk starts out on the desk.
            Vector3 target;
            if (!TryCursorTarget(mouse, out target))
            {
                target = ground;
            }

            MovePreviewTo(target);

            Log("LIFTED '" + piece.name + "' out of the inventory, footprint " + piece.Footprint
                + " stacking=" + piece.StackingRule
                + " (" + previewRejection + ")");

            return true;
        }

        void TickDragging(Mouse mouse)
        {
            // Escape has to work mid-drag too. Without it, a piece dragged somewhere illegal could
            // only be abandoned by dropping it first.
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                Log("cancelled mid-drag");
                Cancel();
                return;
            }

            Vector3 point;
            var aimed = TryCursorTarget(mouse, out point);

            if (aimed)
            {
                MovePreviewTo(point + grabOffset);
                LogDragMove(mouse, point);
            }

            if (!mouse.leftButton.wasReleasedThisFrame)
            {
                return;
            }

            // Let go back over the drawer it came from: it goes back in the drawer. A piece off the
            // floor has somewhere to return to and goes pending as it always did.
            if (heldFromInventory && OverUI())
            {
                Log("dropped back over the inventory - discarded");
                Cancel();
                return;
            }

            // Dropped somewhere illegal: settle on the last legal cell of this drag instead of
            // entering a pending state that can never be committed.
            if (previewRejection != PlacementRejection.None)
            {
                if (!hasValidOrigin)
                {
                    Log("dropped with no legal cell anywhere in this drag - returning it");
                    Cancel();
                    return;
                }

                Log("dropped on " + previewRejection + " - snapped back to the last legal cell");
                SnapPreviewTo(lastValidOrigin);
            }

            state = State.Pending;

            // The wheel now turns the piece, so the camera must stop reading it. The rig also owns
            // the right button, which cancels here, so it stands down entirely.
            SetRigSuppressed(true);
            Log("PENDING (" + previewRejection + ") - still follows the cursor; middle-click or "
                + "scroll to rotate, left-click to commit, Esc or right-click to cancel");
        }

        void TickPending(Mouse mouse)
        {
            // The question owns the input while it is up: the piece must not turn, commit or cancel
            // underneath it. The rig and the manager stay suppressed too, because the gesture has
            // not ended - it is only waiting for an answer.
            if (dialogs != null && dialogs.IsAnyOpen)
            {
                return;
            }

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.deleteKey.wasPressedThisFrame)
            {
                AskToDelete();
                return;
            }

            // Still in hand, just without the button held: the piece goes on following the cursor,
            // snapping as it goes, and only a click puts it down. Letting go of the mouse ends the
            // press, not the placement.
            Vector3 point;
            if (TryCursorTarget(mouse, out point))
            {
                MovePreviewTo(point + grabOffset);
                LogDragMove(mouse, point);
            }

            // The middle button turns it. Free to use because the camera rig stands down for the
            // whole of this state, and it owns that button everywhere else.
            if (mouse.middleButton.wasPressedThisFrame)
            {
                Rotate(1);
            }

            var scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                // Only the sign is used: Windows reports 120 per notch and other platforms use
                // their own scale, so raw values would spin the piece at wildly different rates.
                Rotate(scroll > 0f ? 1 : -1);
            }

            var cancelled = mouse.rightButton.wasPressedThisFrame
                || (keyboard != null && keyboard.escapeKey.wasPressedThisFrame);

            if (cancelled)
            {
                Cancel();
                return;
            }

            if (mouse.leftButton.wasPressedThisFrame && !OverUI())
            {
                // Ctrl turns a click from "put it down here" into "and this one too". Tested first,
                // because committing would end the gesture the selection belongs to.
                if (keyboard != null && keyboard.ctrlKey.isPressed)
                {
                    ToggleSelected(mouse);
                    return;
                }

                Commit();
            }
        }

        /// <summary>
        /// Adds the piece under the cursor to the selection, or takes it out again if it is
        /// already there.
        /// </summary>
        /// <remarks>
        /// A selected piece is not lifted and does not move — it stays exactly where it stands,
        /// wearing a panel, until the delete happens. Only the piece in hand is being placed, which
        /// is why it is kept apart from this list rather than being the first entry in it.
        /// </remarks>
        void ToggleSelected(Mouse mouse)
        {
            int hits;
            var piece = PieceUnderCursor(mouse, out hits);

            if (piece == null)
            {
                Log("ctrl-click hit no piece (" + hits + " hits on the pickable mask)");
                return;
            }

            if (piece == held)
            {
                Log("ctrl-click on the piece already in hand - it is deleted either way");
                return;
            }

            if (selection.Remove(piece))
            {
                Log("deselected '" + piece.gameObject.name + "' - " + (selection.Count + 1) + " now selected");
            }
            else
            {
                selection.Add(piece);
                Log("selected '" + piece.gameObject.name + "' - " + (selection.Count + 1) + " now selected");
            }

            RefreshMarks();
        }

        /// <summary>
        /// Draws a panel under everything worth pointing out: the selection, and any floor the
        /// piece in hand has just been pulled flush against.
        /// </summary>
        /// <remarks>
        /// The two share one set of panels deliberately. They never mean quite the same thing —
        /// one is "this is coming with the delete", the other "this is what you have lined up
        /// against" — but both answer the same question, which is why a piece the player is not
        /// touching has suddenly lit up.
        /// </remarks>
        void RefreshMarks()
        {
            selectionMarks.Clear();

            foreach (var piece in selection)
            {
                if (piece != null)
                {
                    selectionMarks.Add(MarkFor(piece));
                }
            }

            foreach (var piece in snappedTo)
            {
                if (piece != null && !selection.Contains(piece))
                {
                    selectionMarks.Add(MarkFor(piece));
                }
            }

            highlight.MarkAll(selectionMarks);
        }

        PlacementHighlight.MarkedArea MarkFor(FurniturePiece piece)
        {
            var cell = service.Grid.CellSize;

            return new PlacementHighlight.MarkedArea
            {
                Centre = piece.transform.position,
                Top = SurfaceUnder(piece),
                Size = new Vector2(piece.Footprint.x * cell, piece.Footprint.y * cell),
                Rotation = Quaternion.Euler(0f, piece.RotationSteps * 90f, 0f),
            };
        }

        /// <summary>
        /// The height a panel drawn under <paramref name="piece"/> should lie at: the top of the
        /// desk it stands on, or of the floor it stands on.
        /// </summary>
        /// <remarks>
        /// Emphatically not the underside of the piece's own colliders. Furniture in this scene is
        /// authored sunk into the floor slab — a conference table's legs reach to y = -0.16 while
        /// the floor surface is at 0.107 — so a panel put at the object's own bottom is drawn
        /// underneath the floor and never seen at all. The same reasoning as
        /// <see cref="ShowHighlight"/>, which has always asked the floor rather than the piece.
        /// </remarks>
        float SurfaceUnder(FurniturePiece piece)
        {
            // Standing on furniture: the top of whatever is holding it up.
            if (piece.SupportedBy != null)
            {
                Bounds support;
                if (FurniturePlacementService.TryColliderBounds(piece.SupportedBy.gameObject, out support))
                {
                    return support.max.y;
                }
            }

            float floorTop;
            return service.TryFloorTopAt(piece.transform.position, out floorTop)
                ? floorTop
                : piece.transform.position.y;
        }

        /// <summary>
        /// Layers a press may pick a piece from: every layer while editing, the configured mask
        /// otherwise.
        /// </summary>
        /// <remarks>
        /// In Edit the player is arranging the room, and everything standing in it is theirs to
        /// move — so what may be picked is decided by whether the thing is a
        /// <see cref="FurniturePiece"/>, not by which layer it happens to sit on. The mask was
        /// authored as Furniture alone, which silently made anything that arrived on another layer
        /// unselectable: a world loaded onto a different placed layer, or a prefab authored on
        /// Default, looked identical to a piece that was simply refusing to move.
        /// <para>
        /// Outside Edit the mask still applies. Nothing lifts in Play anyway, and narrowing the
        /// ray keeps it cheap.
        /// </para>
        /// </remarks>
        int PickMask => EditingAllowed ? Physics.AllLayers : pickable.value;

        /// <summary>The nearest liftable piece under the cursor, or null.</summary>
        FurniturePiece PieceUnderCursor(Mouse mouse, out int hitCount)
        {
            var ray = view.ScreenPointToRay(CursorPixel(mouse));
            var hits = Physics.RaycastAll(ray, rayDistance, PickMask, QueryTriggerInteraction.Ignore);

            hitCount = hits.Length;

            FurniturePiece nearest = null;
            var nearestDistance = float.MaxValue;

            for (var i = 0; i < hits.Length; i++)
            {
                var piece = hits[i].transform.GetComponentInParent<FurniturePiece>();
                if (piece == null || hits[i].distance >= nearestDistance)
                {
                    continue;
                }

                // Only what is on the floor being looked at. The floors above are hidden but still
                // there, and the ray meets them first.
                if (storeys != null && Storeys.StoreyOf(piece.transform) != storeys.Viewed)
                {
                    continue;
                }

                nearest = piece;
                nearestDistance = hits[i].distance;
            }

            return nearest;
        }

        /// <summary>
        /// Asks whether the piece in hand should be thrown away. The gesture stays exactly where it
        /// is until the question is answered, so dismissing it leaves the piece still pending and
        /// still committable.
        /// </summary>
        void AskToDelete()
        {
            if (!EditingAllowed)
            {
                // Gated here rather than only at the keystroke, because this is the last point
                // every delete path passes through. A gate on the input alone would be one new
                // caller away from being wrong.
                Log("delete ignored: the game is in Play phase");
                return;
            }

            // The piece in hand counts only when there is one. A selection made from Idle has no
            // held piece, and counting one anyway asked to delete three while showing two marks.
            var count = (held != null ? 1 : 0) + selection.Count;

            if (count == 0)
            {
                return;
            }

            Log("asking to delete " + count + " piece(s)");

            var dialog = Dialogs();

            if (dialog == null)
            {
                return;   // already said why; deleting silently would be worse than not deleting
            }

            dialog.Confirm(
                count > 1 ? string.Format(deleteQuestionMany, count) : deleteQuestion,
                DeleteSelection);
        }

        /// <summary>
        /// Throws the piece in hand away for good, along with anything standing on it.
        /// </summary>
        /// <remarks>
        /// Whatever is standing on it goes too, and touching is the test. Riders were parented to
        /// the piece when it was lifted and are destroyed along with it; the rest of what is up
        /// there is found by collision, because in an authored scene most of it — the monitors,
        /// keyboards and mice — is plain scenery in a different branch of the hierarchy with no
        /// link to the desk at all. The alternative is a monitor left hanging in the air where its
        /// desk used to be.
        /// <para>
        /// No cells have to be given back: they were released when the piece was lifted and never
        /// reclaimed, because it was never committed. Nor is the NavMesh carve an issue, for the
        /// same reason — a piece in hand obstructs nothing. The agents are still asked to think
        /// again, because a chair that has ceased to exist is one nobody can be sent to sit on.
        /// </para>
        /// </remarks>
        void DeleteSelection()
        {
            var doomed = held;

            // Everything is worked out before anything is destroyed, while every piece is still
            // standing and its colliders still say what is resting on them.
            condemned.Clear();

            // Only when there is one: Condemn dereferences the piece, so a selection made with
            // empty hands would throw here rather than delete.
            if (doomed != null)
            {
                Condemn(doomed);
            }

            foreach (var piece in selection)
            {
                if (piece != null)
                {
                    Condemn(piece);
                }
            }

            Log("DELETED " + condemned.Count + " object(s) from "
                + (selection.Count + (doomed != null ? 1 : 0))
                + " selected piece(s)"
                + (riders.Count > 0 ? ", plus " + riders.Count + " riding on the one in hand" : ""));

            NotifyAgentsIfSeating(doomed);

            foreach (var piece in selection)
            {
                if (piece != null)
                {
                    NotifyAgentsIfSeating(piece);
                }
            }

            // Null when the delete came from a selection rather than from a piece in hand.
            var removedGround = doomed != null && (doomed.IsFloor || doomed.IsWalkway);
            foreach (var piece in selection)
            {
                if (piece != null && (piece.IsFloor || piece.IsWalkway))
                {
                    removedGround = true;
                }
            }

            highlight.Hide();
            Finish();

            foreach (var thing in condemned)
            {
                if (thing != null)
                {
                    Destroy(thing);
                }
            }

            condemned.Clear();
            RepathActors();

            if (removedGround)
            {
                service.RefreshFloors();
                RebuildNavigation();
            }

            // Destroy is deferred to the end of the frame, so the condemned pieces are still under
            // the root at this instant. An auto save is safe regardless because it waits out a
            // quiet period first and so runs frames later, with them actually gone — but anything
            // that acted on this immediately would write them straight back.
            Announce("deleted");
        }

        /// <summary>
        /// Marks a piece and everything standing on it for destruction, and gives its cells back.
        /// </summary>
        /// <remarks>
        /// The cells matter for a selected piece and not for the one in hand: the held piece gave
        /// its own up when it was lifted, but a selected one is still settled and still holding
        /// ground it is about to stop occupying. Releasing both is simpler than remembering which
        /// is which, and releasing twice costs nothing.
        /// <para>
        /// Riders are not gathered here. They are parented to the piece in hand for the length of
        /// the gesture, so they are destroyed along with it, and the standing-on probe skips
        /// anything already a child of the piece it is asked about.
        /// </para>
        /// </remarks>
        void Condemn(FurniturePiece piece)
        {
            service.Release(piece);
            service.CollectStandingOn(piece, standingOn);

            foreach (var thing in standingOn)
            {
                var stacked = thing.GetComponent<FurniturePiece>();
                if (stacked != null)
                {
                    service.Release(stacked);
                }

                if (!condemned.Contains(thing))
                {
                    condemned.Add(thing);
                }
            }

            if (!condemned.Contains(piece.gameObject))
            {
                condemned.Add(piece.gameObject);
            }
        }

        /// <summary>
        /// Receives the dialog manager and the world root from the container.
        /// </summary>
        /// <remarks>
        /// The dialog manager is declared on the project root and brought up by the Lobby, so it
        /// resolves through this scene's scope even though the object itself is older than the
        /// scene. The world root lives in this scene and is declared on its own scope.
        /// <para>
        /// Injection runs from the LifetimeScope's Awake, so both are in hand before this
        /// controller's Start — which is what let the searches that used to sit there go.
        /// </para>
        /// <para>
        /// The owner's <c>ManagerController</c> is deliberately <em>not</em> among them, and used
        /// to be. It is the owner's own component and there is not always an owner: a scene without
        /// one registered nothing, this injector could not resolve it, and the exception aborted
        /// the whole container build — which is why every other component in the scene then
        /// reported that it "was never injected". Those were casualties, not separate faults.
        /// The owner is handed over by <see cref="Owner"/> instead, which tolerates not existing.
        /// </para>
        /// </remarks>
        [Inject]
        public void Construct(
            IDialogManager dialogs,
            WorldRoot worldRoot,
            Storeys storeys,
            IObjectResolver container,
            IEventHub eventHub,
            IGamePhase phase)
        {
            this.dialogs = dialogs;
            this.world = worldRoot;
            this.storeys = storeys;
            this.resolver = container;
            this.hub = eventHub;
            this.phase = phase;
        }

        /// <summary>
        /// The world's floors. Pieces are lifted from, and dropped onto, the one being looked at.
        /// </summary>
        Storeys storeys;

        /// <summary>
        /// Which phase the game is in. Furniture may only be moved or thrown away in Edit.
        /// </summary>
        /// <remarks>
        /// Read-only: this controller asks, it does not decide. Null in a scene with no phase
        /// service registered, which is treated as Edit so a scene that never adopted phases keeps
        /// working exactly as it did.
        /// </remarks>
        IGamePhase phase;

        /// <summary>True when furniture may be picked up, moved or deleted.</summary>
        bool EditingAllowed => phase == null || phase.IsEdit;

        /// <summary>
        /// The owner who stands down for the length of a gesture, or null when the scene has none.
        /// </summary>
        /// <remarks>
        /// A property set by the scene's LifetimeScope rather than a constructor dependency,
        /// because the two are not the same promise: a dependency must exist or nothing resolves,
        /// and an owner is genuinely optional. The scope is the one class with any business
        /// searching the scene, so it is the one place that can hand this over without either
        /// controller having to go looking for the other.
        /// <para>
        /// Only ever read to raise and lower <c>SuppressClick</c>: he acts on pointer-down, so
        /// without it a press that lifts a piece would also dispatch him to it.
        /// </para>
        /// </remarks>
        public ManagerController Owner
        {
            get { return manager; }
            set
            {
                manager = value;

                // Brought straight in step with the gesture already running, so an owner handed
                // over mid-drag is not left free to walk for the rest of it.
                SetManagerSuppressed(suppressGesture || state != State.Idle);
            }
        }

        /// <summary>The injected dialog manager, or null with a reason logged.</summary>
        IDialogManager Dialogs()
        {
            if (dialogs == null)
            {
                Debug.LogError(
                    nameof(PlacementController) + " on '" + name + "' was never injected, so it "
                    + "cannot ask the player anything. Register it with the scene's LifetimeScope.",
                    this);
            }

            return dialogs;
        }

        void Rotate(int steps)
        {
            held.SetRotationSteps(held.RotationSteps + steps);

            // A quarter turn swaps width for depth, so a placement legal at one angle can be
            // refused at the next. Re-tested every notch rather than only on commit. Re-derived
            // from the piece's own position so the block stays centred where it already sits.
            SnapPreviewTo(service.Grid.OriginFor(held.transform.position, held.RotatedFootprint));
            Log("rotated to " + held.RotationSteps * 90 + " degrees (" + previewRejection + ")");
        }

        /// <summary>Puts the preview under the cursor, snapped to the nearest legal-sized block.</summary>
        void MovePreviewTo(Vector3 cursorPoint)
            => SnapPreviewTo(service.Grid.OriginFor(cursorPoint, held.RotatedFootprint));

        /// <summary>
        /// Moves the piece to a specific origin cell and re-tests it, remembering the cell if it
        /// turned out to be legal.
        /// </summary>
        void SnapPreviewTo(Vector3Int origin)
        {
            var footprint = held.RotatedFootprint;
            var centre = service.Grid.CentreOf(origin, footprint);

            // Pieces are authored sunk into the floor slab, so a piece keeps its own height rather
            // than being lifted onto a computed floor surface. Clutter is the exception: it has no
            // height of its own and takes both its height and its level from whatever it lands on.
            var height = held.RestorePosition.y;
            var supported = true;

            previewSupport = null;

            float surface;
            FurniturePiece support;
            bool anySurface;
            var wholeFootprint = service.TrySupportUnder(
                held, origin, footprint, out surface, out support, out anySurface);

            if (support != null)
            {
                // Standing on furniture: its top is the height, and its cells are on the level
                // above the floor so they never fight the piece underneath for them. Taken whether
                // or not the placement is legal, so a piece half overhanging a desk hovers at desk
                // height showing red instead of dropping to the floor and back each time the cursor
                // crosses the rim.
                height = surface;
                previewSupport = support;
                origin.z = PlacementGrid.KeyFor(PlacementGrid.StoreyOfKey(origin.z), PlacementGrid.SurfaceLevel);
                supported = wholeFootprint;
            }
            else if (!heldKeepsOwnHeight && anySurface)
            {
                // On the floor, with no authored height of its own to keep.
                height = surface;
            }

            // Ground is matched to ground before it is matched to the grid. The grid cannot make
            // these slabs meet - their width is not a whole number of cells - so where a neighbour
            // is within reach its edge wins over the cell boundary.
            snappedTo.Clear();

            if (held.IsFloor)
            {
                Vector3 flush;
                if (service.TrySnapFloor(
                        held, new Vector3(centre.x, height, centre.z), floorSnapDistance, out flush, snappedTo))
                {
                    centre = flush;
                }
            }

            held.transform.position = new Vector3(centre.x, height, centre.z);

            previewOrigin = origin;
            previewRejection = supported
                ? service.Validate(held, origin, footprint)
                : PlacementRejection.Unsupported;

            if (previewRejection == PlacementRejection.None)
            {
                lastValidOrigin = origin;
                hasValidOrigin = true;
            }

            ShowHighlight(centre, height);
            RefreshMarks();
        }

        /// <summary>
        /// Draws the footprint panel on the floor beneath the piece.
        /// </summary>
        /// <remarks>
        /// Sized from the <em>authored</em> footprint and turned by the piece's own rotation, which
        /// is how the obstacle box is built too. A quarter turn therefore swaps the panel's width
        /// and depth exactly as it swaps the footprint, and the panel always outlines the same
        /// ground the piece will actually block.
        /// </remarks>
        void ShowHighlight(Vector3 centre, float surfaceTop)
        {
            var cell = service.Grid.CellSize;
            var size = new Vector2(held.Footprint.x * cell, held.Footprint.y * cell);

            // A piece standing on furniture has already worked out the top it is resting on, and
            // the panel goes there. On the floor it is the floor that matters and not the piece:
            // furniture is authored sunk into the slab, so a piece's own Y is below the surface the
            // panel has to lie on.
            var top = surfaceTop;

            if (previewSupport == null)
            {
                float floorTop;
                top = service.TryFloorTopAt(centre, out floorTop) ? floorTop : held.RestorePosition.y;
            }

            highlight.Track(
                centre,
                top,
                size,
                Quaternion.Euler(0f, held.RotationSteps * 90f, 0f),
                previewRejection == PlacementRejection.None);
        }

        void Commit()
        {
            if (previewRejection != PlacementRejection.None)
            {
                // Refused, but the piece stays in hand rather than snapping back, so the player can
                // simply turn it or move it a cell instead of starting the gesture again.
                Log("commit refused: " + previewRejection);

                // A refusal about another floor cannot be read off the red footprint, which only
                // shows this one, so it is said out loud.
                if (hub != null
                    && (previewRejection == PlacementRejection.NoOtherFloor
                        || previewRejection == PlacementRejection.BlockedOnAnotherFloor))
                {
                    hub.Publish(new PlacementRefused(previewRejection, -1));
                }

                return;
            }

            if (!service.TryClaim(held, previewOrigin, held.RotatedFootprint))
            {
                Log("commit refused by the service at " + previewOrigin);
                return;
            }

            // Off the ghost layer and onto the one the pickable mask reads, so the piece is drawn
            // by the main camera like the rest of the scenery and can be picked up again.
            if (heldFromInventory)
            {
                FurnitureSpawner.SetLayer(held.gameObject, placedLayer);
            }

            // Remembered so the piece underneath can collect this one when it is next moved.
            held.SetSupportedBy(previewSupport);

            Log("COMMITTED " + held.gameObject.name + " at cell " + previewOrigin
                + " on " + (previewSupport == null ? "the floor" : previewSupport.name));

            // Stairs too: they add somewhere to walk, which only a rebuild can tell navigation about.
            var laidGround = held.IsFloor || held.IsWalkway;

            NotifyAgentsIfSeating(held);
            RepathActors();
            highlight.Hide();
            held.Settle();
            SettleRiders();
            Finish();

            // Ground last, once the piece has settled where it belongs: the rebuild reads the scene
            // as it now stands, and the floor list decides where furniture may be put down next.
            if (laidGround)
            {
                service.RefreshFloors();
                RebuildNavigation();
            }

            // Announced after everything above, so an auto save reads a world that has finished
            // settling. This one call covers furniture, agents and the manager alike: all three
            // are put down by dragging them out and committing them here.
            Announce("committed");
        }

        void Cancel()
        {
            // It was built for this gesture and never belonged anywhere, so it goes with the
            // gesture. Read before Finish, which is what lets go of the reference.
            if (heldFromInventory)
            {
                var discarded = held.gameObject;
                highlight.Hide();
                Finish();
                Destroy(discarded);
                return;
            }

            held.transform.position = held.RestorePosition;
            held.SetRotationSteps(held.RestoreRotationSteps);

            // Re-registered rather than claimed: where it came from is where it belongs, even if
            // something has since moved into an overlapping cell.
            service.Register(held);

            highlight.Hide();
            held.Settle();
            SettleRiders();
            Finish();
        }

        void Finish()
        {
            // Hidden here rather than in each caller: every way a gesture can end funnels through
            // this, so the panel cannot be left glowing on the floor by a path that forgot to.
            highlight.Hide();

            // This project runs with Physics.autoSyncTransforms off, so a collider stays where it
            // was until the next fixed step even though its transform has moved. Placement is the
            // only thing that moves furniture, and the probe that decides what a prop can stand on
            // is a raycast — so without this, a mug dragged over a desk that was just repositioned
            // would be told the desk is still at its old spot. One sync per gesture, at the one
            // point every gesture passes through, rather than one per frame of a drag.
            Physics.SyncTransforms();

            held = null;
            heldFromInventory = false;
            previewSupport = null;
            riders.Clear();
            selection.Clear();
            selectionMarks.Clear();
            snappedTo.Clear();
            state = State.Idle;
            previewRejection = PlacementRejection.None;
            hasValidOrigin = false;

            SetRigSuppressed(false);
            SetManagerSuppressed(false);
        }

        /// <summary>
        /// Where the cursor meets the horizontal plane at <paramref name="planeY"/>. This is the
        /// point the dragged piece is centred on.
        /// </summary>
        /// <remarks>
        /// Deliberately not a raycast. A ray from the cursor has to pass the piece being dragged,
        /// and the nearest collider behind it is very often a monitor standing on a desk, a wall,
        /// or an agent rather than the floor — the scene has 100 desktop-prop colliders on the
        /// default layer. Snapping to whichever of those the ray met is what sent a lifted piece
        /// jumping across the room. A piece keeps its own height regardless, so the plane at that
        /// height is the exact answer and no geometry can get in the way of it.
        /// </remarks>
        /// <summary>
        /// Where the cursor is pointing, for the piece currently in hand.
        /// </summary>
        /// <remarks>
        /// A piece over a surface it may stand on is aimed by a ray at that surface; anything else
        /// is aimed at a horizontal plane at its own height, exactly as furniture always was.
        /// <para>
        /// The ray is what makes standing on furniture work at all. A plane has to be at
        /// <em>some</em> height, and the height of a piece that can climb depends on what it is
        /// over — so plane and piece would each depend on the other. On this isometric camera a
        /// plane at desk height and one at floor height put the same pixel over 1.6m apart, so that
        /// circle does not converge, it flickers, every frame the cursor is near a table edge. What
        /// a ray meets does not depend on where the piece currently is, so there is no circle.
        /// </para>
        /// </remarks>
        bool TryCursorTarget(Mouse mouse, out Vector3 point)
        {
            var ray = view.ScreenPointToRay(CursorPixel(mouse));

            FurniturePiece support;
            var storey = storeys != null ? storeys.Viewed : 0;

            if (service.TryCursorSurface(ray, rayDistance, held, storey, out point, out support)
                && support != null)
            {
                return true;
            }

            return TryCursorOnPlane(mouse, held.RestorePosition.y, out point);
        }

        bool TryCursorOnPlane(Mouse mouse, float planeY, out Vector3 point)
        {
            var ray = view.ScreenPointToRay(CursorPixel(mouse));
            var plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));

            float distance;
            if (plane.Raycast(ray, out distance))
            {
                point = ray.GetPoint(distance);
                return true;
            }

            // Camera looking parallel to the floor: there is no intersection to place anything on.
            point = Vector3.zero;
            return false;
        }

        /// <summary>
        /// Tells every agent to look at the chairs again after one has been put down. Moving a
        /// chair does not invalidate a held reference, but which agent may use it depends on where
        /// it now stands — and a chair spawned rather than moved would not be in any list at all.
        /// </summary>
        void NotifyAgentsIfSeating(FurniturePiece piece)
        {
            // Null is an ordinary answer now: a delete started from a selection has no piece in
            // hand, and the caller passes that hand through.
            if (piece == null || piece.GetComponent<Chair>() == null)
            {
                return;
            }

            foreach (var routine in FindObjectsByType<AgentRoutine>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                routine.RescanChairs();
            }
        }

        /// <summary>
        /// Makes every actor plan its route again now that the walkable space has changed.
        /// </summary>
        /// <remarks>
        /// A path is a list of corners worked out when it was requested. Carving a hole across one
        /// does not bend it — the actor keeps following corners that now run through furniture, and
        /// walks through the piece that was just put down. Re-issuing the destination forces a
        /// fresh path around it.
        /// </remarks>
        void RepathActors()
        {
            foreach (var actor in FindObjectsByType<NavMeshAgent>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!actor.isOnNavMesh || !actor.hasPath)
                {
                    continue;
                }

                var destination = actor.destination;
                actor.ResetPath();
                actor.SetDestination(destination);
            }
        }

        /// <summary>
        /// The cursor in screen pixels, held inside the screen.
        /// </summary>
        /// <remarks>
        /// The clamp is not tidying, it is the difference between a placeable piece and one that
        /// can never be put down. The mouse device reports a position relative to the game view,
        /// and it keeps reporting when the pointer is outside it — on a second monitor left of the
        /// view it reads several thousand pixels negative. A ray cast through that pixel meets the
        /// ground tens of metres outside the world, so the piece is <c>OutOfBounds</c> wherever the
        /// player actually is, no cell in the drag is ever legal, and a click cannot commit it: the
        /// piece is stuck on the cursor until Esc. It was seen at x = -4208 against a 1920-wide
        /// screen.
        /// <para>
        /// Clamping makes the piece stop at the edge of the view instead of flying off, which is
        /// both recoverable and what the player expects when they run the pointer off the window.
        /// Inside the view — every normal frame — this changes nothing at all.
        /// </para>
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
        /// Raises or lowers the manager's click suppression on this controller's behalf.
        /// </summary>
        /// <remarks>
        /// Named rather than assigned. Placement is one of several things that suppress the
        /// manager and they overlap in time — Edit phase holds him for as long as the drawer is
        /// open, while this holds him only for the length of a drag. When both wrote one shared
        /// bool, finishing a drag released a suppression the phase still wanted, and the manager
        /// answered a click meant for a piece of furniture.
        /// </remarks>
        void SetManagerSuppressed(bool suppressed)
        {
            if (manager != null)
            {
                manager.SuppressClickFor(ManagerSuppressReason, suppressed);
            }
        }

        /// <summary>This controller's key in the manager's suppression set.</summary>
        const string ManagerSuppressReason = "placement";

        void SetRigSuppressed(bool suppressed)
        {
            if (rig != null)
            {
                rig.InputSuppressed = suppressed;
            }
        }

    }
}
