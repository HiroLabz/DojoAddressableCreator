using System;
using Dojo.Framework.Events;
using Dojo.Framework.UI;
using Dojo.Game.Events;
using Dojo.Game.InGame.Controllers;
using Dojo.Game.Managers;
using Dojo.Game.Placement;
using Dojo.Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace Dojo.Game.UI
{
    /// <summary>
    /// The floor stepper beside the TAB button - ▲ 2F ▼ - and, while the room is being arranged,
    /// the button that adds a floor on top.
    /// </summary>
    /// <remarks>
    /// ▲ and ▼ are for looking: they switch the floor being looked at, which hides the floors above
    /// it (see <see cref="Storeys"/>), let the camera go of the manager, and lift or lower it by one
    /// floor - moving it across too when nothing on that floor would be on screen. Turns then swing
    /// around what is on screen, and the camera comes back to him after the usual pause.
    /// <para>
    /// The manager decides the floor too: sending him somewhere brings the view back to him, and
    /// whenever he reaches another floor that floor is shown and the camera goes with him. The
    /// arrows are for looking elsewhere - to send him there - in the meantime.
    /// </para>
    /// <para>
    /// Add floor sits here rather than in the inventory's header, which has no room left beside its
    /// title, count and close button. It shows only in Edit, which is when the inventory is open.
    /// </para>
    /// <para>
    /// Built by <c>Tools ▸ Dojo ▸ Build Floor Switcher</c>, which fills in every reference below.
    /// </para>
    /// </remarks>
    public sealed class FloorSwitcher : MonoBehaviour
    {
        /// <summary>How long a switch between floors takes: the camera's move and the shade's.</summary>
        const float SwitchSeconds = 2f;

        /// <summary>How dark the shade over the floors below is, 0 clear to 1 black.</summary>
        const float ShadeAlpha = 0.55f;

        /// <summary>How far under the viewed floor's own level the shade lies, so its tiles stay above it.</summary>
        const float ShadeBelow = 0.03f;

        /// <summary>
        /// A change in his height bigger than this from one frame to the next is a jump to another
        /// floor - an elevator's car - not a step. A stair step is a quarter of a metre.
        /// </summary>
        const float JumpHeight = 1f;

        /// <summary>
        /// How much of the screen's edge, on each side, does not count as the middle: a floor seen
        /// only in a sliver at the edge is as good as not seen.
        /// </summary>
        const float MiddleMargin = 0.2f;

        [Tooltip("The ▲ 2F ▼ panel. Hidden while the world has one floor: there is nothing to switch to.")]
        [SerializeField] GameObject stepper;

        [Tooltip("A dark see-through sheet laid just under the floor being viewed, so the floors below " +
                 "read as behind it. No collider: clicks and drags never meet it.")]
        [SerializeField] Renderer shade;

        [SerializeField] Button up;
        [SerializeField] Button down;
        [SerializeField] TMP_Text label;

        [Tooltip("Adds a floor on top. Shown only while the room is being arranged.")]
        [SerializeField] Button add;

        [SerializeField] TMP_Text addLabel;

        [Tooltip("Sends the manager to the floor being looked at. Shown in Play, when he is on another floor.")]
        [SerializeField] Button goTo;

        [SerializeField] TMP_Text goToLabel;

        Storeys storeys;
        IEventHub hub;
        IGamePhase phase;
        ManagerAssembler managers;
        ElevatorShafts shafts;
        PopupPrefabs popups;
        CameraRig rig;

        /// <summary>The camera that follows the manager, let go of while the player looks at another floor.</summary>
        MainCinemachineCamera follow;

        /// <summary>This switcher's answer to where a turn swings around, kept so it can be taken back.</summary>
        CameraRig.PivotFinder pivotFinder;
        IDisposable unreachable;
        IDisposable refused;

        /// <summary>
        /// The floor height the camera was last set for. Only read while the player is holding the
        /// view; while it follows the manager, <see cref="Framed"/> works it out from him.
        /// </summary>
        float cameraBase;

        /// <summary>The manager's floor at the last look, so a new one is noticed the frame he reaches it.</summary>
        int managerFloor = -1;

        /// <summary>His height at the last look, so a jump between floors can be told from a walk.</summary>
        float managerHeight;

        bool managerSeen;

        /// <summary>The manager whose walk orders are listened to, so a new one is noticed.</summary>
        ManagerController watched;

        /// <summary>He has just been sent somewhere: the view goes back to him at the end of the frame.</summary>
        bool backToHim;

        /// <summary>Where Add floor and Go to sit beside the stepper; with no stepper they move to its place.</summary>
        float besideStepper;

        MaterialPropertyBlock shadeBlock;
        float shadeAlpha;
        int shadeMove = -1;
        int shadeFade = -1;

        bool started;

        [Inject]
        public void Construct(Storeys storeys, IEventHub hub, IGamePhase phase,
            ManagerAssembler managers, ElevatorShafts shafts, PopupPrefabs popups)
        {
            this.storeys = storeys;
            this.hub = hub;
            this.phase = phase;
            this.managers = managers;
            this.shafts = shafts;
            this.popups = popups;
        }

        void Awake()
        {
            if (up != null) up.onClick.AddListener(() => Step(+1));
            if (down != null) down.onClick.AddListener(() => Step(-1));
            if (add != null) add.onClick.AddListener(AddFloor);
            if (goTo != null) goTo.onClick.AddListener(GoToViewedFloor);

            if (add != null)
            {
                besideStepper = ((RectTransform)add.transform).anchoredPosition.x;
            }
        }

        void Start()
        {
            if (storeys == null)
            {
                // A scene with no world: nothing to switch between.
                gameObject.SetActive(false);
                return;
            }

            var view = Camera.main;
            rig = view != null ? view.GetComponent<CameraRig>() : null;
            follow = FindAnyObjectByType<MainCinemachineCamera>();

            if (rig != null)
            {
                pivotFinder = PivotOnScreen;
                rig.FreePivot = pivotFinder;
            }

            cameraBase = storeys.ViewedBase;
            storeys.Changed += Refresh;

            // Straight to where it belongs for the floor on screen, with no fade: nothing switched.
            PlaceShade(immediately: true);

            if (phase != null)
            {
                phase.Changed += OnPhaseChanged;
            }

            if (hub != null)
            {
                unreachable = hub.Subscribe<FloorUnreachable>(OnFloorUnreachable);
                refused = hub.Subscribe<PlacementRefused>(OnPlacementRefused);
            }

            started = true;
            Refresh();
        }

        void OnDestroy()
        {
            if (!started)
            {
                return;
            }

            storeys.Changed -= Refresh;

            if (rig != null && rig.FreePivot == pivotFinder)
            {
                rig.FreePivot = null;
            }

            if (phase != null)
            {
                phase.Changed -= OnPhaseChanged;
            }

            if (unreachable != null)
            {
                unreachable.Dispose();
            }

            if (refused != null)
            {
                refused.Dispose();
            }

            Watch(null);
        }

        /// <summary>Listens to this manager's walk orders, and stops listening to the last one's.</summary>
        void Watch(ManagerController manager)
        {
            if (manager == watched)
            {
                return;
            }

            if (watched != null)
            {
                watched.TravelRequested -= OnTravelRequested;
            }

            if (hooked != null)
            {
                if (hooked.HoldArrival == viewTravelling)
                {
                    hooked.HoldArrival = null;
                }

                if (hooked.HoldDeparture == floorFading)
                {
                    hooked.HoldDeparture = null;
                }

                if (hooked.Shut == doorsShut)
                {
                    hooked.Shut = null;
                }
            }

            // A ride being shown for the manager going away would leave its floor faded for good.
            if (rideFrom >= 0 && storeys != null)
            {
                rideFrom = -1;
                storeys.BringBack();
            }

            hooked = null;
            watched = manager;

            if (watched != null)
            {
                watched.TravelRequested += OnTravelRequested;
            }
        }

        /// <summary>
        /// Has his elevator rides wait for the view. Once the doors shut, the floor he is leaving
        /// fades away, and he is moved only when it has gone. The view then travels to his new
        /// floor, and the doors there open once it has arrived. Done whenever his rider turns up,
        /// which may be after he does.
        /// </summary>
        void HookRider()
        {
            if (watched == null || hooked != null)
            {
                return;
            }

            hooked = watched.GetComponent<ElevatorRider>();

            if (hooked == null)
            {
                return;
            }

            if (viewTravelling == null)
            {
                viewTravelling = () => rideFrom >= 0 || (rig != null && rig.IsShifting);
                floorFading = () => rideFrom >= 0 && storeys.IsFading(rideFrom);
                doorsShut = OnDoorsShut;
            }

            hooked.HoldArrival = viewTravelling;
            hooked.HoldDeparture = floorFading;
            hooked.Shut = doorsShut;
        }

        /// <summary>The rider whose rides wait for the view.</summary>
        ElevatorRider hooked;

        /// <summary>
        /// Whether the view is still on its way to his new floor - the ride not yet over, or the
        /// camera still moving. Kept so it can be taken back.
        /// </summary>
        Func<bool> viewTravelling;

        /// <summary>Whether the floor he is leaving is still fading away. Kept so it can be taken back.</summary>
        Func<bool> floorFading;

        /// <summary>The start of a ride's fade, kept so it can be taken back.</summary>
        Action<int, int> doorsShut;

        /// <summary>
        /// The floor his ride is leaving, faded away until the view has reached his new floor; -1
        /// when no ride is being shown.
        /// </summary>
        int rideFrom = -1;

        /// <summary>
        /// The doors have shut on him: the floor he is leaving fades away, whole, while the camera
        /// stays where it is.
        /// </summary>
        /// <remarks>
        /// Only when that floor is the one on screen. A ride the player is not watching - they are
        /// looking at another floor - happens with nothing to show.
        /// </remarks>
        void OnDoorsShut(int from, int to)
        {
            if (storeys == null || from == to || storeys.Viewed != from)
            {
                return;
            }

            rideFrom = from;

            // He is in the shut car: the sweep starts from the elevator, where he is.
            storeys.FadeAway(from, watched != null ? watched.transform.position : Vector3.zero);
        }

        /// <summary>
        /// Gives back the floor the ride left once the view has reached his new floor: going up,
        /// it shows again underneath, as floors below do; going down, it stays hidden above.
        /// </summary>
        void FinishRide(ManagerController manager)
        {
            if (rideFrom < 0)
            {
                return;
            }

            var arrived = manager == null || manager.Floor != rideFrom;
            var still = rig == null || !rig.IsShifting;
            var settled = !storeys.IsFading(rideFrom) && (manager == null || !storeys.IsFading(manager.Floor));

            if (arrived && still && settled)
            {
                rideFrom = -1;
                storeys.BringBack();
            }
        }

        /// <summary>
        /// He has a new target - a spot, an agent, a seat, or Go to - so the view goes back to him:
        /// his floor, and the camera on him with no floor lift left over.
        /// </summary>
        /// <remarks>
        /// Done at the end of the frame rather than here, because the camera is told about the same
        /// order and may only now be taking the view back from the player. By then it is following
        /// him again, and the lift to undo is the one it is actually framing with.
        /// </remarks>
        void OnTravelRequested(TravelEstimate _)
        {
            backToHim = true;
        }

        /// <summary>
        /// Says, in red, why an elevator would not go down: no other floor to stop at, or something
        /// in the way on one.
        /// </summary>
        /// <remarks>
        /// Red because it is a refusal. The footprint only shows the floor on screen, so without
        /// this the player sees red under a spot that looks perfectly clear.
        /// </remarks>
        void OnPlacementRefused(PlacementRefused e)
        {
            string body;

            if (e.Reason == PlacementRejection.NoOtherFloor)
            {
                body = "There's no other floor to stop at from this spot. Lay floor tiles here on "
                    + "another floor first.";
            }
            else if (e.Reason == PlacementRejection.BlockedOnAnotherFloor)
            {
                var floor = shafts != null ? shafts.LastBlockedFloor : -1;
                body = (floor >= 0
                        ? "Something is in the way on floor " + (floor + 1) + "."
                        : "Something is in the way on another floor.")
                    + " Clear that spot first.";
            }
            else
            {
                return;
            }

            if (popups == null || popups.Affirmation == null)
            {
                Debug.Log("[Floors] The elevator can't go here: " + body);
                return;
            }

            PopupManager.Instance.Show(popups.Affirmation)
                .Tint(MessagePopup.Tone.Alert)
                .Present("Elevator can't go here", body)
                .Caption(MessagePopup.Choice.Primary, "OK");
        }

        /// <summary>
        /// Keeps the view on the manager's floor: the moment he reaches another one, by the elevator
        /// or the stairs, that floor is the one shown, and the camera travels there.
        /// </summary>
        /// <remarks>
        /// ▲ and ▼ still look at another floor - to send him there. The order sends the view back to
        /// him, on his own floor, and from there it follows him to wherever he was sent.
        /// <para>
        /// An elevator moves him between floors in one step, and the camera, following him, would
        /// jump with him. It is held where it is for that step instead, so the switch to his floor
        /// is the same two-second move as one made with the arrows.
        /// </para>
        /// <para>
        /// The camera coming back to him after the player has dragged it away drops any floor switch
        /// - it frames him, on his floor - so the floor shown goes back to his too.
        /// </para>
        /// </remarks>
        void LateUpdate()
        {
            if (!started)
            {
                return;
            }

            var manager = Manager;
            var floor = manager != null ? manager.Floor : -1;
            var followed = Follows(manager);

            Watch(manager);
            HookRider();

            if (manager != null)
            {
                var height = manager.transform.position.y;

                if (followed && managerSeen && Mathf.Abs(height - managerHeight) > JumpHeight)
                {
                    rig.HoldThroughJump(height - managerHeight);
                }

                managerHeight = height;
                managerSeen = true;
            }

            if (floor != managerFloor)
            {
                var first = managerFloor < 0;
                managerFloor = floor;

                // Arriving on a floor while the player is looking at another one - he walked back
                // to his area by himself - leaves the view where they put it. The look ends on its
                // own, and the view goes back to him then.
                if (follow != null && follow.IsLooking && !first)
                {
                    RefreshGoTo();
                }
                else if (floor >= 0)
                {
                    // On the first look the camera has only just been pointed at him, perhaps not
                    // yet by following: it starts on his floor, so nothing is moved to get there.
                    if (first && !followed)
                    {
                        cameraBase = storeys.BaseOf(floor);
                    }

                    // Refreshed even when the floor shown does not change, as when he arrives on
                    // the floor the player is already looking at: the camera still has to settle.
                    storeys.View(floor);

                    // A ride being shown that arrives above the floor it left comes onto a floor
                    // that was out of sight: it fades in, as the one left faded away. Started in
                    // the same frame as the view, so it is never drawn whole first.
                    if (rideFrom >= 0 && floor > rideFrom)
                    {
                        storeys.FadeIn(floor, manager.transform.position);
                    }

                    Refresh();
                }
                else
                {
                    RefreshGoTo();
                }
            }
            else if (backToHim && floor >= 0)
            {
                storeys.View(floor);
                Refresh();
            }
            else if (followed)
            {
                var framedFloor = Mathf.Clamp(storeys.StoreyAt(Framed()), 0, Mathf.Max(0, storeys.Count - 1));

                if (framedFloor != storeys.Viewed)
                {
                    storeys.View(framedFloor);
                }
            }

            backToHim = false;

            if (followed)
            {
                cameraBase = Framed();
            }

            FinishRide(manager);
        }

        /// <summary>Whether the camera is following the manager himself, rather than the player holding it.</summary>
        bool Follows(ManagerController manager)
        {
            return manager != null && rig != null && rig.FollowTarget == manager.transform;
        }

        /// <summary>
        /// The floor height the camera is set for: his floor's plus the lift a floor switch has
        /// added while it follows him, or the height last set while the player holds the view.
        /// </summary>
        float Framed()
        {
            var manager = Manager;

            return Follows(manager) ? storeys.BaseOf(manager.Floor) + rig.FollowLift : cameraBase;
        }

        ManagerController Manager => managers != null && managers.Active != null ? managers.Active : null;

        /// <summary>Sends the manager to the floor being looked at, by the first way that gets there.</summary>
        void GoToViewedFloor()
        {
            var manager = Manager;

            if (manager == null || storeys == null || shafts == null)
            {
                return;
            }

            manager.TryGoToFloor(storeys.Viewed, shafts.ArrivalPoints(storeys.Viewed));
        }

        /// <summary>
        /// Says, in blue, why the manager stayed put: there is no way to that floor yet.
        /// </summary>
        /// <remarks>
        /// Blue because it is information, not an error: nothing failed, the building is simply not
        /// joined up yet. One OK button, which is all it needs.
        /// </remarks>
        void OnFloorUnreachable(FloorUnreachable e)
        {
            if (popups == null || popups.Affirmation == null)
            {
                Debug.Log("[Floors] The manager can't reach " + Storeys.LabelFor(e.Storey) + ".");
                return;
            }

            PopupManager.Instance.Show(popups.Affirmation)
                .Tint(MessagePopup.Tone.Info)
                .Present(
                    "Can't get to floor " + (e.Storey + 1),
                    "The manager has no way there yet. Place an elevator or stairs, and lay floor tiles "
                        + "where they arrive.")
                .Caption(MessagePopup.Choice.Primary, "OK");
        }

        void OnPhaseChanged(GamePhase _) => Refresh();

        /// <summary>
        /// ▲ or ▼: looks at the floor above or below. For looking only - the camera lets go of the
        /// manager, and comes back to him on its own after the usual pause with no pan.
        /// </summary>
        /// <remarks>
        /// When nothing on that floor would be in the middle of the screen - its layout is elsewhere
        /// - the view also moves across to the nearest thing on it, in the same move.
        /// </remarks>
        void Step(int direction)
        {
            if (storeys == null)
            {
                return;
            }

            var to = Mathf.Clamp(storeys.Viewed + direction, 0, Mathf.Max(0, storeys.Count - 1));

            if (to == storeys.Viewed)
            {
                return;
            }

            if (follow != null)
            {
                follow.LookAway();
            }

            // Worked out before the view moves: it measures from where the screen is looking now.
            var across = AcrossTo(to);

            storeys.View(to);

            if (rig != null && across != Vector3.zero)
            {
                rig.ShiftView(across, SwitchSeconds);
            }
        }

        /// <summary>
        /// How far the view has to move across for the floor it is going to to show something:
        /// nothing when part of that floor would already be in the middle of the screen, or when
        /// the floor is empty; otherwise as far as the nearest thing on it.
        /// </summary>
        /// <remarks>
        /// Nearest rather than the middle of the floor, so the view moves no further than it has to
        /// and the player can still tell where they are. An empty floor keeps the view where it is,
        /// which is where its first tiles will most likely be laid.
        /// </remarks>
        Vector3 AcrossTo(int floor)
        {
            var view = Camera.main;

            if (view == null)
            {
                return Vector3.zero;
            }

            // Where the middle of the screen meets the floor in view. Lifting the view and the floor
            // by the same height keeps that spot where it is, so it is where the middle of the
            // screen will meet the floor it is going to.
            var framed = Framed();
            var ray = view.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            float along;

            if (!new Plane(Vector3.up, new Vector3(0f, framed, 0f)).Raycast(ray, out along))
            {
                return Vector3.zero;
            }

            var centre = ray.GetPoint(along);
            var lift = storeys.BaseOf(floor) - framed;

            var found = false;
            var nearest = Vector3.zero;
            var best = float.MaxValue;

            foreach (var piece in storeys.Floor(floor).GetComponentsInChildren<FurniturePiece>(true))
            {
                Bounds bounds;

                if (!TryBounds(piece, out bounds))
                {
                    continue;
                }

                if (InMiddle(view, bounds, lift))
                {
                    return Vector3.zero;
                }

                var flat = new Vector2(bounds.center.x - centre.x, bounds.center.z - centre.z).sqrMagnitude;

                if (flat < best)
                {
                    best = flat;
                    nearest = bounds.center;
                    found = true;
                }
            }

            return found ? new Vector3(nearest.x - centre.x, 0f, nearest.z - centre.z) : Vector3.zero;
        }

        /// <summary>
        /// Whether a thing on another floor would be in the middle of the screen once the view has
        /// risen by <paramref name="lift"/> - seen now, that is where it would be were it that much
        /// lower.
        /// </summary>
        static bool InMiddle(Camera view, Bounds bounds, float lift)
        {
            var top = bounds.max.y - lift;
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);

            for (var i = 0; i < 4; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? bounds.min.x : bounds.max.x,
                    top,
                    (i & 2) == 0 ? bounds.min.z : bounds.max.z);

                var seen = view.WorldToViewportPoint(corner);

                if (seen.z <= 0f)
                {
                    return false;
                }

                min = Vector2.Min(min, seen);
                max = Vector2.Max(max, seen);
            }

            return max.x > MiddleMargin && min.x < 1f - MiddleMargin
                && max.y > MiddleMargin && min.y < 1f - MiddleMargin;
        }

        /// <summary>
        /// What a turn swings around while the camera is not following him: the thing in the middle
        /// of the screen on a floor that can be seen - the one being looked at or one below.
        /// </summary>
        /// <remarks>
        /// The floors above are hidden but still in the ray's way, so they are passed over, the
        /// same rule a click uses (<see cref="Storeys.FloorSeen"/>). With nothing there, the spot
        /// straight ahead at the height of the floor being looked at.
        /// </remarks>
        bool PivotOnScreen(Transform camera, out Vector3 pivot)
        {
            pivot = Vector3.zero;

            if (storeys == null)
            {
                return false;
            }

            var ray = new Ray(camera.position, camera.forward);
            var hits = Physics.RaycastAll(ray, 1000f, ~0, QueryTriggerInteraction.Ignore);
            var best = float.MaxValue;
            var found = false;

            foreach (var hit in hits)
            {
                var floor = Storeys.StoreyOf(hit.transform);

                if (floor > storeys.Viewed || hit.distance >= best)
                {
                    continue;
                }

                best = hit.distance;
                pivot = hit.point;
                found = true;
            }

            if (found)
            {
                return true;
            }

            float along;

            if (new Plane(Vector3.up, new Vector3(0f, storeys.ViewedBase, 0f)).Raycast(ray, out along))
            {
                pivot = ray.GetPoint(along);
                return true;
            }

            return false;
        }

        static bool TryBounds(FurniturePiece piece, out Bounds bounds)
        {
            bounds = default(Bounds);
            var any = false;

            foreach (var drawn in piece.GetComponentsInChildren<Renderer>(true))
            {
                if (!any)
                {
                    bounds = drawn.bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(drawn.bounds);
                }
            }

            return any;
        }

        /// <summary>
        /// A new, empty floor on top, and the view taken up to it so the player can lay its tiles.
        /// </summary>
        /// <remarks>
        /// Announced as a change to the world, so autosave keeps it: an added floor is part of the
        /// world even before anything is on it. Switching floors is not, and is not announced.
        /// </remarks>
        void AddFloor()
        {
            if (storeys == null)
            {
                return;
            }

            var index = storeys.Add();

            if (index < 0)
            {
                return;   // at the limit; the button says so
            }

            if (hub != null)
            {
                hub.Publish(new WorldChanged("floor " + Storeys.LabelFor(index) + " added"));
            }
        }

        /// <summary>The label, which arrows can be pressed, the add button, and the camera.</summary>
        void Refresh()
        {
            if (storeys == null)
            {
                return;
            }

            var viewed = storeys.Viewed;
            var count = storeys.Count;

            // One floor: nothing to step between, so no stepper. Add floor and Go to take its place
            // beside the TAB button rather than leaving a gap where it was.
            var several = count > 1;

            if (stepper != null)
            {
                stepper.SetActive(several);
            }

            SetBeside(add, several);
            SetBeside(goTo, several);

            if (label != null)
            {
                label.text = Storeys.LabelFor(viewed);
            }

            if (up != null)
            {
                up.interactable = viewed < count - 1;
            }

            if (down != null)
            {
                down.interactable = viewed > 0;
            }

            if (add != null)
            {
                add.gameObject.SetActive(phase == null || phase.IsEdit);
                add.interactable = storeys.CanAdd;
            }

            if (addLabel != null)
            {
                addLabel.text = storeys.CanAdd
                    ? "+ ADD FLOOR"
                    : "UP TO " + storeys.Limit + " FLOORS";
            }

            // By the difference, so the camera keeps everything else the player has done to it, and
            // over a couple of seconds so it travels to the floor rather than cutting to it.
            var floorBase = storeys.ViewedBase;
            var framed = Framed();

            if (rig != null && !Mathf.Approximately(floorBase, framed))
            {
                rig.ShiftFloor(floorBase - framed, SwitchSeconds);
            }

            cameraBase = floorBase;

            PlaceShade(immediately: false);
            RefreshGoTo();
        }

        /// <summary>Puts a button beside the stepper, or in its place when there is no stepper.</summary>
        void SetBeside(Button button, bool stepperShown)
        {
            if (button == null)
            {
                return;
            }

            var rect = (RectTransform)button.transform;
            rect.anchoredPosition = new Vector2(stepperShown ? besideStepper : 0f, rect.anchoredPosition.y);
        }

        /// <summary>
        /// Lays the shade just under the floor being viewed, darkening everything below it, and
        /// clears it on the ground floor where there is nothing below to darken.
        /// </summary>
        /// <remarks>
        /// Moved and faded over the same two seconds as the camera, so the floor left behind sinks
        /// into the dark as the view rises from it. Drawn only; it has no collider and is on the
        /// Ignore Raycast layer, so nothing the mouse does can meet it.
        /// </remarks>
        void PlaceShade(bool immediately)
        {
            if (shade == null || storeys == null)
            {
                return;
            }

            var height = storeys.ViewedBase - ShadeBelow;
            var alpha = storeys.Viewed > 0 ? ShadeAlpha : 0f;

            if (shadeMove >= 0) { LeanTween.cancel(shadeMove); shadeMove = -1; }
            if (shadeFade >= 0) { LeanTween.cancel(shadeFade); shadeFade = -1; }

            if (immediately)
            {
                var at = shade.transform.position;
                shade.transform.position = new Vector3(at.x, height, at.z);
                SetShadeAlpha(alpha);
                return;
            }

            shadeMove = LeanTween.moveY(shade.gameObject, height, SwitchSeconds)
                .setEase(LeanTweenType.easeInOutSine)
                .setOnComplete(() => shadeMove = -1)
                .id;

            shadeFade = LeanTween.value(shade.gameObject, shadeAlpha, alpha, SwitchSeconds)
                .setEase(LeanTweenType.easeInOutSine)
                .setOnUpdate((float a) => SetShadeAlpha(a))
                .setOnComplete(() => shadeFade = -1)
                .id;
        }

        void SetShadeAlpha(float alpha)
        {
            shadeAlpha = alpha;

            if (shadeBlock == null)
            {
                shadeBlock = new MaterialPropertyBlock();
            }

            shade.GetPropertyBlock(shadeBlock);
            shadeBlock.SetColor("_BaseColor", new Color(0f, 0f, 0f, alpha));
            shade.SetPropertyBlock(shadeBlock);

            // Not drawn at all when clear, so the ground floor costs nothing.
            shade.enabled = alpha > 0.001f;
        }

        /// <summary>"GO TO 3F": in Play, when the manager is on another floor than the one on screen.</summary>
        void RefreshGoTo()
        {
            if (goTo == null || storeys == null)
            {
                return;
            }

            var manager = Manager;
            var show = manager != null
                && (phase == null || !phase.IsEdit)
                && manager.Floor != storeys.Viewed;

            goTo.gameObject.SetActive(show);

            if (show && goToLabel != null)
            {
                goToLabel.text = "GO TO " + Storeys.LabelFor(storeys.Viewed);
            }
        }
    }
}
