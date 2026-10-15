using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using VContainer;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// Carrying a whole room out of the Rooms tab.
    /// </summary>
    /// <remarks>
    /// The same gesture as a tile or a wall taken out of the drawer, so the hands already know it:
    /// a click on the card puts the room on the cursor, it follows snapped to the grid with its
    /// footprint lit green where it fits and red where it does not, the wheel or the middle button
    /// turns it a quarter at a time, a left click puts it down, and a right click or Esc puts it
    /// back. What follows the cursor is the room itself, built from the same prefabs, as a ghost.
    /// <para>
    /// Kept apart from the single-piece gesture rather than folded into it: a room is dozens of
    /// pieces and an area, has no one <c>FurniturePiece</c> to hold, and is checked against the
    /// whole floor rather than cell by cell.
    /// </para>
    /// <para>
    /// A room already in the world goes through the same gesture: a press on any piece of it lifts
    /// the whole room, which then follows the cursor from where it was grabbed. Put down, it stays
    /// one room; put back, it returns to where it stood.
    /// </para>
    /// </remarks>
    public sealed partial class PlacementController
    {
        RoomPlacer roomPlacer;
        RoomPlacer.Plan roomPlan;
        Transform roomGhost;
        List<RoomPlacer.Taken> roomTaken;
        string roomBlocker;
        int roomFloor;
        int roomSteps;
        Vector2 roomOffset;
        bool roomFits;

        // The room lifted out of the world, or null when the room in hand came from the Rooms tab;
        // and where on it the cursor took hold, relative to its origin at no turn.
        RoomPlacer.Move roomMove;
        Vector2 roomGrab;

        // The room's floor tiles, relative to its origin, and the floor's around it: what lines the
        // room up with the floor it is put beside.
        List<RoomPlacer.Tile> roomTiles;
        List<RoomPlacer.Tile> floorTiles;

        /// <summary>True while a room is on the cursor.</summary>
        public bool IsPlacingRoom => state == State.Room;

        /// <summary>The room on the cursor, or null.</summary>
        public RoomEntry RoomInHand => state == State.Room && roomPlan != null ? roomPlan.Entry : null;

        /// <summary>What the room on the cursor would overlap where it is, or null when it fits.</summary>
        public string RoomBlocker => state == State.Room ? roomBlocker : null;

        /// <summary>
        /// Puts a room on the cursor. False when a gesture is already running or the cursor is not
        /// over the ground.
        /// </summary>
        public bool BeginRoomFromInventory(RoomPlacer placer, RoomPlacer.Plan plan)
        {
            if (ViewBlocked)
            {
                Debug.LogWarning("[Placement] Rooms can't be placed from a side view. Switch the view "
                    + "cube to Top or Perspective first.", this);
                return false;
            }

            if (state != State.Idle)
            {
                Log("room refused: a gesture is already running");
                return false;
            }

            var mouse = Mouse.current;

            if (mouse == null || view == null || placer == null || plan == null)
            {
                return false;
            }

            roomPlacer = placer;
            roomPlan = plan;
            roomFloor = storeys != null ? storeys.Viewed : 0;
            roomSteps = 0;
            roomTaken = placer.TakenOn(roomFloor);
            roomGhost = placer.BuildGhost(plan, ghostLayer);
            roomTiles = RoomPlacer.TilesOf(roomGhost);
            floorTiles = placer.FloorTilesOn(roomFloor, null);

            // Straight to the carried state, as a piece from the drawer is: the room arrives from a
            // click, so there is no press to release.
            state = State.Room;
            SetRigSuppressed(true);
            SetManagerSuppressed(true);

            MoveRoomToCursor(mouse);

            Log("LIFTED room '" + plan.Entry.name + "' out of the inventory, footprint "
                + plan.Footprint.size + " (" + (roomFits ? "fits" : "blocked") + ")");

            return true;
        }

        /// <summary>
        /// Lifts a room already in the world, whole, onto the cursor. False, with nothing lifted,
        /// when it cannot be moved.
        /// </summary>
        bool BeginRoomMove(RoomContainer room, Mouse mouse)
        {
            RoomPlacer placer;

            if (resolver == null || !resolver.TryResolve(out placer))
            {
                Log("room press ignored: no RoomPlacer in this scene");
                return false;
            }

            string problem;
            var move = placer.Lift(room, out problem);

            if (move == null)
            {
                Debug.LogWarning("[Placement] " + problem, room);
                return false;
            }

            roomPlacer = placer;
            roomMove = move;
            roomPlan = move.Plan;
            roomFloor = move.Floor;
            roomSteps = 0;
            roomTaken = placer.TakenOn(roomFloor, move);
            roomTiles = RoomPlacer.TilesOf(room.transform);
            floorTiles = placer.FloorTilesOn(roomFloor, move);

            // Held where it was grabbed, so it does not jump to centre itself on the cursor.
            Vector3 point;
            var origin = new Vector2(move.From.x, move.From.z);
            roomGrab = TryCursorOnPlane(mouse, move.From.y, out point)
                ? new Vector2(point.x, point.z) - origin
                : move.Plan.Footprint.center;

            state = State.Room;
            SetRigSuppressed(true);
            SetManagerSuppressed(true);

            MoveRoomToCursor(mouse);

            Log("LIFTED room '" + room.RoomName + "' with " + move.Pieces.Count + " piece(s), "
                + move.Riders.Count + " carried and " + move.Areas.Count + " area(s)");

            return true;
        }

        void TickRoom(Mouse mouse)
        {
            if (dialogs != null && dialogs.IsAnyOpen)
            {
                return;
            }

            MoveRoomToCursor(mouse);

            if (mouse.middleButton.wasPressedThisFrame)
            {
                TurnRoom(1);
            }

            var scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                TurnRoom(scroll > 0f ? 1 : -1);
            }

            var keyboard = Keyboard.current;
            var cancelled = mouse.rightButton.wasPressedThisFrame
                || (keyboard != null && keyboard.escapeKey.wasPressedThisFrame);

            if (cancelled)
            {
                Log("room cancelled");

                if (roomMove != null)
                {
                    // Back where it stood, as a piece goes back when a move is called off.
                    roomPlacer.PutDown(roomMove, new Vector2(roomMove.From.x, roomMove.From.z), 0, false);
                }

                EndRoom();
                return;
            }

            if (!mouse.leftButton.wasPressedThisFrame || OverUI())
            {
                return;
            }

            if (!roomFits)
            {
                // Left in hand, like a refused piece, so it can be moved on instead of fetched again.
                Log("room refused: it would overlap " + roomBlocker + " on " + Storeys.LabelFor(roomFloor));
                return;
            }

            var plan = roomPlan;
            var placer = roomPlacer;
            var floor = roomFloor;
            var offset = roomOffset;
            var steps = roomSteps;
            var move = roomMove;

            if (move != null)
            {
                EndRoom();
                placer.PutDown(move, offset, steps, true);

                Log("MOVED room '" + plan.Entry.name + "' to " + offset + ", turned " + steps * 90 + " degrees");

                if (BakesNavigation)
                {
                    RebuildNavigation();
                }

                return;
            }

            // The ghost goes first, so the real room is not built through it.
            EndRoom();
            placer.Build(plan, floor, offset, steps);

            Log("COMMITTED room '" + plan.Entry.name + "' at " + offset + ", turned " + steps * 90 + " degrees");

            if (BakesNavigation)
            {
                RebuildNavigation();
            }
        }

        void TurnRoom(int steps)
        {
            roomSteps = PlacementGrid.Normalise(roomSteps + steps);
            Log("room turned to " + roomSteps * 90 + " degrees");
        }

        /// <summary>Centres the room's footprint on the cursor, snapped, and lights it to say whether it fits.</summary>
        void MoveRoomToCursor(Mouse mouse)
        {
            var floorY = storeys != null ? storeys.BaseOf(roomFloor) : 0f;

            Vector3 point;
            if (!TryCursorOnPlane(mouse, floorY, out point))
            {
                return;
            }

            var footprint = RoomPlacer.FootprintAt(roomPlan, roomSteps);

            if (roomMove != null)
            {
                // The grab point turns with the room, so the room turns about the cursor.
                var grab = Quaternion.Euler(0f, roomSteps * 90f, 0f) * new Vector3(roomGrab.x, 0f, roomGrab.y);
                roomOffset = RoomPlacer.Snapped(new Vector2(point.x - grab.x, point.z - grab.z));
            }
            else
            {
                roomOffset = RoomPlacer.Snapped(new Vector2(point.x, point.z) - footprint.center);
            }

            // Near the floor, onto its tiles' lattice, so the room's floor meets it edge to edge.
            roomOffset = RoomPlacer.AlignToTiles(roomTiles, floorTiles, roomOffset, roomSteps);

            var placed = new Rect(footprint.position + roomOffset, footprint.size);
            roomFits = RoomPlacer.IsFree(placed, roomTaken, out roomBlocker);

            if (roomMove != null)
            {
                RoomPlacer.Carry(roomMove, roomOffset, roomSteps);
            }

            if (roomGhost != null)
            {
                roomGhost.SetPositionAndRotation(
                    new Vector3(roomOffset.x, floorY, roomOffset.y),
                    Quaternion.Euler(0f, roomSteps * 90f, 0f));
            }

            highlight.Track(
                new Vector3(placed.center.x, floorY, placed.center.y),
                floorY,
                placed.size,
                Quaternion.identity,
                roomFits);
        }

        /// <summary>Puts the ghost away and hands the input back.</summary>
        void EndRoom()
        {
            if (roomGhost != null)
            {
                // Switched off before it goes, because Destroy waits for the end of the frame and
                // the floor list is rebuilt from every active piece - a room committed this frame
                // would otherwise count the ghost's tiles as floor.
                roomGhost.gameObject.SetActive(false);
                Destroy(roomGhost.gameObject);
            }

            roomGhost = null;
            roomMove = null;
            roomTiles = null;
            floorTiles = null;
            roomPlan = null;
            roomPlacer = null;
            roomTaken = null;
            roomBlocker = null;
            roomFits = false;

            highlight.Hide();
            state = State.Idle;

            SetRigSuppressed(false);
            SetManagerSuppressed(false);
        }

        void OnDestroy()
        {
            if (roomGhost != null)
            {
                Destroy(roomGhost.gameObject);
            }

            // A room lifted out of the world is part of it: torn down mid-move, it goes back.
            if (roomMove != null && roomPlacer != null && roomMove.Room != null)
            {
                roomPlacer.PutDown(roomMove, new Vector2(roomMove.From.x, roomMove.From.z), 0, false);
            }
        }
    }
}
