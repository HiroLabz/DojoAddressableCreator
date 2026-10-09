using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

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
            roomOffset = RoomPlacer.Snapped(new Vector2(point.x, point.z) - footprint.center);

            var placed = new Rect(footprint.position + roomOffset, footprint.size);
            roomFits = RoomPlacer.IsFree(placed, roomTaken, out roomBlocker);

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
        }
    }
}
