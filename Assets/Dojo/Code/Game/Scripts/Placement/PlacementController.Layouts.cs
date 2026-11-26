using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// Carrying a whole floor layout out of the FLOORS tab.
    /// </summary>
    /// <remarks>
    /// The room gesture over again (<see cref="PlacementController"/>'s Rooms part): a click on the
    /// card puts the layout on the cursor as a ghost, it follows snapped to the grid and lined up
    /// with the floor's tiles, the wheel or the middle button turns it a quarter at a time, a left
    /// click lays it, and a right click or Esc puts it back. It is refused in one case only: where
    /// any of its floor tiles would lie on floor already there, the footprint turns red and the
    /// click does nothing (Jacob, 26 Nov). On open ground the ghost hides each piece whose spot is
    /// taken, and laying it leaves those pieces out and everything already on the floor as it was
    /// (ClickUp 14ze67xgf0v, AC 7). Once laid, the pieces are the floor's own - loose, or in their
    /// rooms - and a layout is not picked up again as one.
    /// </remarks>
    public sealed partial class PlacementController
    {
        FloorLayoutPlacer layoutPlacer;
        FloorLayoutPlacer.Plan layoutPlan;
        Transform layoutGhost;
        List<RoomPlacer.Taken> layoutTaken;
        int layoutFloor;
        int layoutSteps;
        Vector2 layoutOffset;
        int layoutClashes;
        bool layoutFits;
        string layoutBlocker;

        // The layout's floor tiles, relative to its origin, and the floor's: what lines it up with
        // the floor it is laid beside.
        List<RoomPlacer.Tile> layoutTiles;
        List<RoomPlacer.Tile> layoutFloorTiles;

        /// <summary>True while a floor layout is on the cursor.</summary>
        public bool IsPlacingLayout => state == State.Layout;

        /// <summary>The layout on the cursor, or null.</summary>
        public FloorLayoutEntry LayoutInHand => state == State.Layout && layoutPlan != null ? layoutPlan.Entry : null;

        /// <summary>How many of the layout's pieces would be skipped where it is now: their spots are taken.</summary>
        public int LayoutClashes => state == State.Layout ? layoutClashes : 0;

        /// <summary>Why the layout cannot be laid where it is - it lies on floor already there - or null when it can.</summary>
        public string LayoutBlocker => state == State.Layout ? layoutBlocker : null;

        /// <summary>
        /// Puts a layout on the cursor. False when a gesture is already running or the view is
        /// from the side.
        /// </summary>
        public bool BeginLayoutFromInventory(FloorLayoutPlacer placer, FloorLayoutPlacer.Plan plan)
        {
            if (ViewBlocked)
            {
                Debug.LogWarning("[Placement] Floor layouts can't be laid from a side view. Switch the view "
                    + "cube to Top or Perspective first.", this);
                return false;
            }

            if (state != State.Idle)
            {
                Log("layout refused: a gesture is already running");
                return false;
            }

            var mouse = Mouse.current;

            if (mouse == null || view == null || placer == null || plan == null)
            {
                return false;
            }

            layoutPlacer = placer;
            layoutPlan = plan;
            layoutFloor = storeys != null ? storeys.Viewed : 0;
            layoutSteps = 0;
            layoutTaken = placer.TakenOn(layoutFloor);
            layoutGhost = placer.BuildGhost(plan, ghostLayer);
            layoutTiles = RoomPlacer.TilesOf(layoutGhost);
            layoutFloorTiles = placer.FloorTilesOn(layoutFloor);

            state = State.Layout;
            HoldView(true);
            SetRigSuppressed(true);
            SetManagerSuppressed(true);

            MoveLayoutToCursor(mouse);

            Log("LIFTED layout '" + plan.Entry.name + "' out of the inventory, footprint " + plan.Footprint.size
                + " (" + (layoutFits ? layoutClashes + " piece(s) on taken spots" : "on floor already there") + ")");

            return true;
        }

        void TickLayout(Mouse mouse)
        {
            // Edit ended with a layout in hand: it was never part of the world, so it is dropped.
            if (!EditingAllowed)
            {
                Log("layout dropped: the game left Edit");
                EndLayout();
                return;
            }

            if (dialogs != null && dialogs.IsAnyOpen)
            {
                return;
            }

            MoveLayoutToCursor(mouse);

            if (mouse.middleButton.wasPressedThisFrame)
            {
                TurnLayout(1);
            }

            var scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                TurnLayout(scroll > 0f ? 1 : -1);
            }

            var keyboard = Keyboard.current;

            if (mouse.rightButton.wasPressedThisFrame
                || (keyboard != null && (keyboard.escapeKey.wasPressedThisFrame || keyboard.deleteKey.wasPressedThisFrame)))
            {
                Log("layout put back");
                EndLayout();
                return;
            }

            if (!mouse.leftButton.wasPressedThisFrame || OverUI())
            {
                return;
            }

            if (!layoutFits)
            {
                // Left in hand, like a refused room, so it can be moved on instead of fetched again.
                Log("layout refused: it would lie on " + layoutBlocker + " on " + Storeys.LabelFor(layoutFloor));
                return;
            }

            var plan = layoutPlan;
            var placer = layoutPlacer;
            var floor = layoutFloor;
            var offset = layoutOffset;
            var steps = layoutSteps;
            var taken = layoutTaken;

            // The ghost goes first, so the real layout is not built through it.
            EndLayout();
            var result = placer.Build(plan, floor, offset, steps, taken);

            Log("LAID layout '" + plan.Entry.name + "' at " + offset + ", turned " + steps * 90 + " degrees: "
                + result.Built + " piece(s), " + result.Skipped + " skipped on taken spots, " + result.Rooms + " room(s), "
                + result.Areas + " area(s)");

            if (BakesNavigation)
            {
                RebuildNavigation();
            }
        }

        void TurnLayout(int steps)
        {
            layoutSteps = PlacementGrid.Normalise(layoutSteps + steps);
            Log("layout turned to " + layoutSteps * 90 + " degrees");
        }

        /// <summary>
        /// Centres the layout's footprint on the cursor, snapped and lined up with the floor's
        /// tiles, hides the ghost's pieces that stand on taken spots, and lights the footprint:
        /// red where its tiles would lie on floor already there, green anywhere else.
        /// </summary>
        void MoveLayoutToCursor(Mouse mouse)
        {
            var floorY = storeys != null ? storeys.BaseOf(layoutFloor) : 0f;

            Vector3 point;
            if (!TryCursorOnPlane(mouse, floorY, out point))
            {
                return;
            }

            var footprint = FloorLayoutPlacer.FootprintAt(layoutPlan.Footprint, layoutSteps);

            layoutOffset = RoomPlacer.Snapped(new Vector2(point.x, point.z) - footprint.center);
            layoutOffset = RoomPlacer.AlignToTiles(layoutTiles, layoutFloorTiles, layoutOffset, layoutSteps);

            var placed = new Rect(footprint.position + layoutOffset, footprint.size);

            layoutFits = !FloorLayoutPlacer.OverlapsFloor(layoutTiles, layoutFloorTiles, layoutOffset, layoutSteps);
            layoutBlocker = layoutFits ? null : "the floor already there";
            FloorLayoutPlacer.Tint(layoutFits);

            if (layoutGhost != null)
            {
                layoutGhost.SetPositionAndRotation(
                    new Vector3(layoutOffset.x, floorY, layoutOffset.y),
                    Quaternion.Euler(0f, layoutSteps * 90f, 0f));

                layoutClashes = FloorLayoutPlacer.Clashing(layoutGhost, layoutTaken);
            }

            highlight.Track(
                new Vector3(placed.center.x, floorY, placed.center.y),
                floorY,
                placed.size,
                Quaternion.identity,
                layoutFits);
        }

        /// <summary>Puts the ghost away and hands the input back.</summary>
        void EndLayout()
        {
            if (layoutGhost != null)
            {
                // Switched off before it goes: Destroy waits for the end of the frame, and the floor
                // list is rebuilt from every active piece - a layout laid this frame would otherwise
                // count the ghost's tiles as floor.
                layoutGhost.gameObject.SetActive(false);
                Destroy(layoutGhost.gameObject);
            }

            layoutGhost = null;
            HoldView(false);

            layoutTiles = null;
            layoutFloorTiles = null;
            layoutPlan = null;
            layoutPlacer = null;
            layoutTaken = null;
            layoutClashes = 0;
            layoutFits = false;
            layoutBlocker = null;

            highlight.Hide();
            state = State.Idle;

            SetRigSuppressed(false);
            SetManagerSuppressed(false);
        }
    }
}
