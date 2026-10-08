using System.Collections.Generic;
using Dojo.Framework.Events;
using Dojo.Game.Components;
using Dojo.Game.Events;
using Dojo.Game.InGame.Agents;
using Dojo.Game.Placement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using VContainer;

namespace Dojo.Game.InGame.Controllers
{
    /// <summary>
    /// Click a floor and the manager walks there; click an agent and he walks to that agent
    /// instead, announcing <see cref="AgentReached"/> once he gets there; click an elevator and he
    /// walks up to its doors, where the floors it goes to are offered (<see cref="ElevatorReached"/>)
    /// - as they are wherever a walk he was sent on ends against an elevator. Uses a
    /// <see cref="NavMeshAgent"/> rather than a CharacterController: the two both drive the
    /// transform, and running them together is a race the navigation docs warn against.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class ManagerController : MonoBehaviour
    {
        [Tooltip("Camera the click is cast from. Defaults to Camera.main.")]
        [SerializeField] Camera view;

        [Tooltip("Which colliders count as clickable.")]
        [SerializeField] LayerMask clickable = ~0;

        [Tooltip("How far the click ray reaches. The isometric camera sits well back, so this " +
                 "needs to comfortably exceed its distance to the floor.")]
        [SerializeField] float rayDistance = 500f;

        [Tooltip("How far from the clicked point to search for NavMesh. Clicking a wall or a desk " +
                 "then walks to the nearest reachable ground instead of doing nothing.")]
        [SerializeField] float snapDistance = 2f;

        [Header("Reaching an agent")]
        [Tooltip("Centre-to-centre distance that counts as reaching the target agent, and so " +
                 "opens the chat. Talking distance rather than touching distance: the two are " +
                 "having a conversation, not a collision.")]
        [SerializeField] float contactDistance = 2f;

        [Tooltip("How close to stop when walking to an agent. Kept under contactDistance so the " +
                 "manager actually comes to rest inside it rather than short of it.")]
        [SerializeField] float agentStoppingDistance = 1.5f;

        [Tooltip("How far the target must move before the route is recalculated. Re-pathing every " +
                 "frame makes the manager nudge on the spot and he never settles.")]
        [SerializeField] float repathThreshold = 0.25f;

        [Header("Sitting")]
        [Tooltip("Seconds the hop onto or off a seat takes.")]
        [SerializeField] float jumpSeconds = 0.5f;

        [Tooltip("Peak height of the hop above the straight line between floor and seat.")]
        [SerializeField] float jumpHeight = 0.8f;

        [Tooltip("Furthest he will hop onto a seat, measured across the floor. A chair he cannot " +
                 "walk this close to is not sat on: he walks as near to it as he can get and stops.")]
        [SerializeField] float hopReach = 1.5f;

        [Header("Arrival")]
        [Tooltip("Turn to face the camera once a walk finishes, so the manager comes to rest " +
                 "looking at the player rather than whichever way he happened to be heading.")]
        [SerializeField] bool faceCameraOnArrival = true;

        [Tooltip("Logs what became of each click and walk order: where he was sent, or why not.")]
        [SerializeField] bool logClicks = true;

        [Tooltip("Seconds that turn takes. Zero snaps.")]
        [SerializeField] float faceCameraSeconds = 0.25f;

        [Header("Elevators")]
        [Tooltip("How close to an elevator's block, across the floor, a walk has to end for its " +
                 "floors to be offered. Its doors are where he stands to ride, 0.6 m out.")]
        [SerializeField] float elevatorReach = 1f;

        NavMeshAgent agent;
        IEventHub hub;

        /// <summary>His own walkable floor, and the areas he may use on it.</summary>
        /// <remarks>
        /// There is one floor per model size, and a query that does not say which could be answered
        /// from a thinner character's floor, which reaches closer to walls than he can.
        /// </remarks>
        NavMeshQueryFilter Filter => new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };

        NavMeshPath scratch;
        NavMeshPath Scratch => scratch ?? (scratch = new NavMeshPath());

        /// <summary>
        /// The point he can actually walk to that is nearest <paramref name="target"/>: the target
        /// itself when the way is open, or where the way runs out when it is blocked.
        /// </summary>
        /// <remarks>
        /// So an order across a wall is carried out as far as it can be, rather than refused or
        /// forced. It is the end of the partial path - the closest the floor lets him come.
        /// </remarks>
        Vector3 NearestReachableTo(Vector3 target)
        {
            if (NavMesh.CalculatePath(transform.position, target, Filter, Scratch)
                && Scratch.status == NavMeshPathStatus.PathPartial
                && Scratch.corners.Length > 0)
            {
                return Scratch.corners[Scratch.corners.Length - 1];
            }

            return target;
        }

        AgentRoutine targetAgent;
        bool reachedTarget;
        float defaultStoppingDistance;
        Vector3 lastIssuedDestination;
        bool hasIssuedDestination;

        readonly SeatHop hop = new SeatHop();
        Chair seat;               // claimed chair: being walked to, or currently sat on
        Vector3 seatStandPoint;   // floor spot to hop back down to
        bool seated;
        bool walkingToSeat;
        bool hoppingToSeat;
        System.Action afterStandingUp;
        float halfHeight;

        // Turning to face the camera on arrival. facedCamera latches for the current order so the
        // turn plays once per walk rather than restarting every frame that HasArrived stays true.
        bool facedCamera;
        bool turningToCamera;
        Quaternion turnFrom;
        Quaternion turnTo;
        float turnStartedAt;
        bool rotationWasAgentDriven;

        /// <summary>The agent the manager is currently walking to, if any.</summary>
        /// <summary>
        /// While true, clicks are ignored entirely.
        /// </summary>
        /// <remarks>
        /// Read-only now, and computed from <see cref="SuppressClickFor"/>. It used to be settable,
        /// and that was a bug waiting to happen: several things suppress the manager for reasons
        /// that overlap in time — furniture placement raises it for the length of a drag, area
        /// painting raises it for the length of a session, and Edit phase raises it for as long as
        /// the player is arranging the room. With one shared bool the last writer won, so finishing
        /// a furniture drag lowered it again and the manager started answering clicks in the middle
        /// of Edit.
        /// </remarks>
        public bool SuppressClick => suppressReasons.Count > 0;

        /// <summary>
        /// The world's floors, so a click only lands on the floor being looked at. Null in a scene
        /// without floors, where every click counts as it always did.
        /// </summary>
        /// <remarks>
        /// Handed over by <c>ManagerAssembler</c> rather than injected: the manager is also used in
        /// scenes that have no floors at all, and a dependency there would be a resolve failure.
        /// </remarks>
        public Storeys Floors { get; set; }

        /// <summary>
        /// The world's elevators, for walking up to one and being offered its floors there. Null in
        /// a scene without floors. Handed over by <c>ManagerAssembler</c>, like <see cref="Floors"/>.
        /// </summary>
        public ElevatorShafts Elevators { get; set; }

        /// <summary>
        /// True while the player is picking a floor for him at an elevator: he waits at its doors,
        /// and does not wander off after his usual pause. Set by the elevator panel.
        /// </summary>
        public bool WaitingAtElevator { get; set; }

        /// <summary>His elevator rides, found on first use; null until he has one.</summary>
        ElevatorRider rider;

        readonly HashSet<string> suppressReasons = new HashSet<string>();

        /// <summary>
        /// Suppresses or releases clicks on behalf of one named holder.
        /// </summary>
        /// <remarks>
        /// Clicks stay suppressed while any holder still holds it, so holders never clobber each
        /// other and none of them has to know the others exist. Releasing a reason nobody holds is
        /// harmless, which matters because the release often runs on a path that may not have run
        /// the raise — a drag cancelled by a scene change, say.
        /// </remarks>
        /// <param name="reason">Stable identifier for the holder, e.g. "phase" or "drag".</param>
        public void SuppressClickFor(string reason, bool suppressed)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return;
            }

            if (suppressed)
            {
                suppressReasons.Add(reason);
            }
            else
            {
                suppressReasons.Remove(reason);
            }
        }

        /// <summary>True while the manager is frozen for Edit phase.</summary>
        public bool IsHalted { get; private set; }

        /// <summary>
        /// Stops the manager where he stands and keeps him there.
        /// </summary>
        /// <remarks>
        /// Mirrors <c>AgentRoutine.Halt</c>, which already existed — the manager had no equivalent,
        /// only click suppression, and suppressing clicks does nothing about a walk already under
        /// way. A manager mid-stride when the player opens the drawer would keep walking across a
        /// room being rearranged under him.
        /// </remarks>
        public void Halt()
        {
            if (IsHalted)
            {
                return;
            }

            IsHalted = true;
            legs.Clear();
            offerAtEnd = false;

            if (agent != null && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
            }
        }

        /// <summary>Lets the manager move again. Safe to call when he was never halted.</summary>
        public void Resume()
        {
            if (!IsHalted)
            {
                return;
            }

            IsHalted = false;

            if (agent != null && agent.isOnNavMesh)
            {
                agent.isStopped = false;
            }
        }

        public AgentRoutine TargetAgent => targetAgent;

        /// <summary>True once the manager is actually sitting on a chair.</summary>
        public bool IsSeated => seated;

        /// <summary>
        /// True while the arcing hop on or off a seat is running.
        /// </summary>
        /// <remarks>
        /// Exposed for the animation driver, which has no other way to tell a hop from ordinary
        /// walking: a hop moves the transform directly and leaves the <c>NavMeshAgent</c> switched
        /// off, so velocity reads zero throughout exactly the moment the jump should play.
        /// </remarks>
        public bool IsHopping => hop.IsHopping;

        /// <summary>The chair the manager is on, or heading for.</summary>
        public Chair Seat => seat;

        [Inject]
        public void Construct(IEventHub hub)
        {
            this.hub = hub;
        }

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            defaultStoppingDistance = agent.stoppingDistance;

            // Sitting puts the capsule's feet on the seat, so the pivot has to clear it by half
            // the visual height. Read it rather than assume.
            var visual = GetComponentInChildren<Renderer>();
            halfHeight = visual != null ? visual.bounds.extents.y : 0.6f;

            if (view == null)
            {
                view = Camera.main;
            }
        }

        void Update()
        {
            HandleClick();

            // A hop owns the transform outright, so nothing else may run during one.
            if (hop.IsHopping)
            {
                if (hop.Tick(transform))
                {
                    FinishHop();
                }

                return;
            }

            if (seated)
            {
                return;
            }

            TrackSeat();
            TrackTargetAgent();
            TrackRide();
            TrackElevatorArrival();
            TrackArrivalFacing();
        }

        void HandleClick()
        {
            // Furniture placement takes the button for the length of its gesture. Set from
            // PlacementController, which runs first via DefaultExecutionOrder so the flag is
            // already up in the same frame the press happens — this method acts on pointer-down,
            // so a flag raised a frame later would arrive after the manager had been dispatched.
            var mouse = Mouse.current;
            var pressed = mouse != null && mouse.leftButton.wasPressedThisFrame;

            if (SuppressClick)
            {
                if (pressed)
                {
                    LogClick("ignored: clicks are held by " + string.Join(", ", suppressReasons));
                }

                return;
            }

            // Mid-ride he is in a closed car between floors: a new order waits until he steps out,
            // rather than being given to a walker that is not on the NavMesh's surface.
            if (rider == null)
            {
                rider = GetComponent<ElevatorRider>();
            }

            if (rider != null && rider.IsRiding)
            {
                if (pressed)
                {
                    LogClick("ignored: riding the elevator");
                }

                return;
            }

            if (!pressed || view == null)
            {
                return;
            }

            // Without this, clicking a UI button would also send the manager walking to whatever
            // happens to be behind it.
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                LogClick("ignored: the pointer is over the UI");
                return;
            }

            var ray = view.ScreenPointToRay(mouse.position.ReadValue());

            // Every hit along the ray, not just the first. A seated agent sits inside the chair's
            // collider, so a plain Raycast returns the chair and the agent is never clickable.
            // Triggers are included so the agents' pick colliders register.
            var hits = Physics.RaycastAll(ray, rayDistance, clickable, QueryTriggerInteraction.Collide);

            AgentRoutine nearestAgent = null;
            var nearestAgentDistance = float.MaxValue;
            Chair nearestChair = null;
            var nearestChairDistance = float.MaxValue;
            FurniturePiece nearestElevator = null;
            var nearestElevatorDistance = float.MaxValue;
            var groundPoint = Vector3.zero;
            var groundDistance = float.MaxValue;
            var hasGround = false;
            var elsewhere = 0;

            // The floor the click lands on: the nearest one on screen at that pixel, which is the
            // floor being viewed or one below showing through where it has no tiles.
            var clicked = Floors != null ? FloorSeen(hits) : 0;

            for (var i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];

                // Never pick the manager himself.
                if (hit.transform.IsChildOf(transform))
                {
                    continue;
                }

                // Only what is on that floor. The floors above the viewed one are hidden but still
                // there, and the ray meets them first; and an agent on a floor below, under the tile
                // that was clicked, is out of sight and must not win over it.
                if (Floors != null && Storeys.StoreyOf(hit.transform) != clicked)
                {
                    elsewhere++;
                    continue;
                }

                var hitAgent = hit.transform.GetComponentInParent<AgentRoutine>();
                if (hitAgent != null)
                {
                    if (hit.distance < nearestAgentDistance)
                    {
                        nearestAgent = hitAgent;
                        nearestAgentDistance = hit.distance;
                    }

                    continue;
                }

                var hitPiece = hit.transform.GetComponentInParent<FurniturePiece>();
                if (hitPiece != null && ElevatorShafts.IsStop(hitPiece))
                {
                    if (hit.distance < nearestElevatorDistance)
                    {
                        nearestElevator = hitPiece;
                        nearestElevatorDistance = hit.distance;
                    }

                    continue;
                }

                var hitChair = hit.transform.GetComponentInParent<Chair>();
                if (hitChair != null)
                {
                    if (hit.distance < nearestChairDistance)
                    {
                        nearestChair = hitChair;
                        nearestChairDistance = hit.distance;
                    }

                    continue;
                }

                if (hit.distance < groundDistance)
                {
                    groundDistance = hit.distance;
                    groundPoint = hit.point;
                    hasGround = true;
                }
            }

            // An agent anywhere along the ray wins over the scenery in front of it, and a chair or
            // an elevator - whichever is nearer - wins over the floor behind it. Each is carried out
            // once the click's halo has played.
            if (nearestAgent != null)
            {
                var agentClicked = nearestAgent;
                LogClick("on " + agentClicked.name + ": following");
                AfterHalo(() => Follow(agentClicked));
                return;
            }

            if (nearestElevator != null && nearestElevatorDistance < nearestChairDistance)
            {
                var stopClicked = nearestElevator;
                LogClick("on " + stopClicked.name + ": walking up to it");
                AfterHalo(() => WalkToElevator(stopClicked));
                return;
            }

            if (nearestChair != null)
            {
                var chairClicked = nearestChair;
                LogClick("on " + chairClicked.name + ": sitting");
                AfterHalo(() => SitOn(chairClicked));
                return;
            }

            if (hasGround)
            {
                var pointClicked = groundPoint;
                AfterHalo(() => GoTo(pointClicked));
                return;
            }

            LogClick("nothing to walk to"
                + (clicked >= 0 ? " on " + (Floors != null ? Storeys.LabelFor(clicked) : "the floor") : " on screen")
                + ": " + hits.Length + " hit(s), " + elsewhere + " of them on other floors");
        }

        /// <summary>The click effect, whose halo a click waits for. Found on the first click.</summary>
        Effects.ClickFeedback feedback;

        /// <summary>Counts clicks, so an order still waiting for its halo gives way to a newer one.</summary>
        int clicks;

        /// <summary>
        /// Carries out a click once its halo has played, so the player sees what they clicked
        /// before he - and the camera with him - moves.
        /// </summary>
        /// <remarks>
        /// As long as the halo itself lasts, read off it (<see cref="Effects.ClickFeedback.HaloSeconds"/>).
        /// A click made while one is still waiting replaces it, and an order whose clicks have been
        /// taken away in the meantime - Edit opened, a drag begun, a ride started - is dropped,
        /// exactly as the click would have been.
        /// </remarks>
        async void AfterHalo(System.Action order)
        {
            var issued = ++clicks;

            if (feedback == null)
            {
                feedback = FindAnyObjectByType<Effects.ClickFeedback>();
            }

            var wait = feedback != null ? feedback.HaloSeconds : 0f;

            if (wait > 0f)
            {
                try
                {
                    await Awaitable.WaitForSecondsAsync(wait, destroyCancellationToken);
                }
                catch (System.OperationCanceledException)
                {
                    return;   // he is gone
                }
            }

            if (issued != clicks)
            {
                LogClick("replaced by a newer click before its halo had played");
                return;
            }

            if (SuppressClick || IsHalted || (rider != null && rider.IsRiding))
            {
                LogClick("dropped: clicks were taken away while its halo played");
                return;
            }

            order();
        }

        /// <summary>The floor of the nearest thing on screen along the click's ray, or -1.</summary>
        int FloorSeen(RaycastHit[] hits)
        {
            var distances = new List<float>(hits.Length);
            var floors = new List<int>(hits.Length);

            foreach (var hit in hits)
            {
                if (hit.transform.IsChildOf(transform))
                {
                    continue;
                }

                distances.Add(hit.distance);
                floors.Add(Storeys.StoreyOf(hit.transform));
            }

            return Storeys.FloorSeen(distances, floors, Floors.Viewed);
        }

        /// <summary>
        /// One line per click saying what became of it, so a click that seems to do nothing says
        /// why in the log rather than leaving it to be guessed.
        /// </summary>
        void LogClick(string message) => LogOrder("click " + message);

        void LogOrder(string message)
        {
            if (logClicks)
            {
                Debug.Log("[Manager] " + message, this);
            }
        }

        /// <summary>Walks to <paramref name="target"/> and reports when it is reached.</summary>
        public void Follow(AgentRoutine target)
        {
            if (StandUpFirst(() => Follow(target)))
            {
                return;
            }

            ReleaseSeat();
            legs.Clear();
            offerAtEnd = false;
            targetAgent = target;
            reachedTarget = false;
            hasIssuedDestination = false;
            agent.stoppingDistance = agentStoppingDistance;
            CancelTurnToCamera();
        }

        /// <summary>
        /// Forgets the agent being followed without moving the manager. Called when the details
        /// panel closes, so a later click starts a fresh follow rather than resuming the old one.
        /// </summary>
        public void ClearTarget()
        {
            targetAgent = null;
            reachedTarget = false;
            hasIssuedDestination = false;
            agent.stoppingDistance = defaultStoppingDistance;
            CancelTurnToCamera();
        }

        /// <summary>Walks to a point on the ground, dropping any agent being followed.</summary>
        public void GoTo(Vector3 worldPoint)
        {
            if (StandUpFirst(() => GoTo(worldPoint)))
            {
                return;
            }

            ReleaseSeat();
            legs.Clear();
            offerAtEnd = false;
            targetAgent = null;
            reachedTarget = false;
            hasIssuedDestination = false;
            agent.stoppingDistance = defaultStoppingDistance;
            CancelTurnToCamera();

            NavMeshHit navHit;
            if (!NavMesh.SamplePosition(worldPoint, out navHit, snapDistance, Filter))
            {
                LogOrder("go to " + worldPoint.ToString("F2") + " refused: no walkable ground for him within "
                    + snapDistance + "m (agent type " + agent.agentTypeID + ")");
            }
            else
            {
                // Another floor with no way there: he stays where he is and the player is told why.
                // Walking as far as this floor allows would end nowhere they asked for.
                if (Floors != null)
                {
                    var targetFloor = Floors.StoreyAt(navHit.position.y);

                    if (targetFloor != Floors.StoreyAt(transform.position.y) && !CanReach(navHit.position))
                    {
                        LogOrder("go to " + navHit.position.ToString("F2") + " on " + Storeys.LabelFor(targetFloor)
                            + " refused: no complete path from " + Storeys.LabelFor(Floor) + " (" + Scratch.status + ")");

                        if (hub != null)
                        {
                            hub.Publish(new FloorUnreachable(targetFloor));
                        }

                        return;
                    }
                }

                // Blocked: as close as he can get, which is the nearest he can come to what was
                // clicked. Said out loud so the destination he is given is the one he will reach.
                var destination = NearestReachableTo(navHit.position);

                LogOrder("go to " + destination.ToString("F2") + " from " + transform.position.ToString("F2")
                    + (Floors != null ? " (" + Storeys.LabelFor(Floor) + " to " + Storeys.LabelFor(Floors.StoreyAt(destination.y)) + ")" : ""));

                Head(destination, true);

                // A walk to a spot: if it ends against an elevator, without riding one on the way,
                // that elevator's floors are offered.
                offerAtEnd = true;
                rodeOnTheWay = false;
                elevatorWanted = false;
            }
        }

        /// <summary>
        /// Walks him somewhere of his own accord - back to his area, or about his floor, after
        /// standing still a while. Not an order: nothing is announced, so the camera is not pulled
        /// back to him. False when there is no way there, and he stays put.
        /// </summary>
        public bool Stroll(Vector3 point)
        {
            if (seated || hop.IsHopping || IsHalted || agent == null || !agent.isOnNavMesh)
            {
                return false;
            }

            NavMeshHit hit;
            if (!NavMesh.SamplePosition(point, out hit, snapDistance, Filter) || !CanReach(hit.position))
            {
                return false;
            }

            legs.Clear();
            offerAtEnd = false;
            targetAgent = null;
            reachedTarget = false;
            agent.stoppingDistance = defaultStoppingDistance;
            CancelTurnToCamera();

            LogOrder("strolls to " + hit.position.ToString("F2")
                + (Floors != null ? " (" + Storeys.LabelFor(Floor) + " to " + Storeys.LabelFor(Floors.StoreyAt(hit.position.y)) + ")" : ""));

            Head(hit.position, false);
            return true;
        }

        /// <summary>
        /// Sets where he is walking to. Every walk goes through here - to a spot, to an agent, to
        /// a seat - so a new target is announced the same way whatever was clicked to get it.
        /// </summary>
        /// <param name="destination">The point he walks to.</param>
        /// <param name="fresh">
        /// True for a new target. False when the same one is only re-aimed, as each time the agent
        /// he is following moves on: that is still the walk already announced, and announcing it
        /// again would pull the camera back to him every few steps.
        /// </param>
        void Head(Vector3 destination, bool fresh)
        {
            agent.SetDestination(destination);

            // Recorded so HasArrived has something to answer against. Without this a plain walk
            // never reports arrival at all.
            lastIssuedDestination = destination;
            hasIssuedDestination = true;

            if (!fresh)
            {
                return;
            }

            var handler = TravelRequested;
            if (handler != null)
            {
                handler(EstimateTravel(destination));
            }
        }

        /// <summary>
        /// Sends him to another floor by the first way up or down that actually gets there - an
        /// elevator's doors, or a staircase's end. Says why when none does.
        /// </summary>
        /// <param name="floor">The floor to go to, from 0.</param>
        /// <param name="arrivals">Where that floor can be stepped onto from another.</param>
        /// <returns>True when he set off.</returns>
        public bool TryGoToFloor(int floor, System.Collections.Generic.IEnumerable<Vector3> arrivals)
        {
            if (arrivals != null)
            {
                foreach (var point in arrivals)
                {
                    NavMeshHit navHit;
                    if (NavMesh.SamplePosition(point, out navHit, snapDistance, Filter) && CanReach(navHit.position))
                    {
                        GoTo(navHit.position);

                        // Sent to a floor: arriving at an elevator's doors there offers nothing.
                        offerAtEnd = false;
                        return true;
                    }
                }
            }

            if (hub != null)
            {
                hub.Publish(new FloorUnreachable(floor));
            }

            return false;
        }

        /// <summary>
        /// Walks him up to an elevator's doors. Reaching them offers its floors
        /// (<see cref="TrackElevatorArrival"/>).
        /// </summary>
        void WalkToElevator(FurniturePiece stop)
        {
            // Taken away while the halo played - Edit opened and the elevator was deleted.
            if (stop == null)
            {
                LogOrder("walk to the elevator dropped: the elevator has gone");
                return;
            }

            Vector3 doors;
            if (Elevators != null && Elevators.TryDoorOn(stop, Storeys.StoreyOf(stop.transform), out doors))
            {
                GoTo(doors);
            }
            else
            {
                // Doors that open onto no tiles: as near to it as he can get, which is still against it.
                GoTo(stop.transform.position);
            }

            // The elevator itself was asked for, so its floors are offered when he gets there even
            // if the way there rode one - from another floor, the shortest way may be this very car.
            elevatorWanted = offerAtEnd;
        }

        /// <summary>
        /// Where he goes next, in turn, once he has got to where he is walking: the far end of a
        /// ride, and the spot asked for beyond it.
        /// </summary>
        readonly Queue<Vector3> legs = new Queue<Vector3>();

        /// <summary>
        /// The walk under way is one he was sent on to a spot, and when it ends against an elevator
        /// that elevator's floors are offered.
        /// </summary>
        bool offerAtEnd;

        /// <summary>The walk under way has ridden an elevator: it ends stepping out of one, not walking up to it.</summary>
        bool rodeOnTheWay;

        /// <summary>The walk under way is to an elevator that was clicked: it offers its floors even after a ride.</summary>
        bool elevatorWanted;

        /// <summary>
        /// His walk has ended against an elevator, on his floor - at its doors or beside it - so its
        /// floors are offered (<see cref="ElevatorReached"/>).
        /// </summary>
        /// <remarks>
        /// Only a walk he was sent on to a spot, and only once it is over: walking past an elevator
        /// on the way somewhere else offers nothing. Not the first leg of a ride, which ends at the
        /// doors on purpose; not a walk that rode an elevator, which ends stepping out of one -
        /// unless the elevator itself was clicked; and not his own strolls, a walk to another floor,
        /// or one to an agent or a seat.
        /// </remarks>
        void TrackElevatorArrival()
        {
            if (rider == null)
            {
                rider = GetComponent<ElevatorRider>();
            }

            if (rider != null && rider.IsRiding)
            {
                rodeOnTheWay = true;
                return;
            }

            if (!offerAtEnd || legs.Count > 0 || targetAgent != null || walkingToSeat
                || !agent.enabled || !agent.isOnNavMesh || !HasArrived())
            {
                return;
            }

            offerAtEnd = false;

            if ((rodeOnTheWay && !elevatorWanted) || Elevators == null || Floors == null)
            {
                return;
            }

            var floor = Floor;
            var stop = Elevators.StopTouching(transform.position, floor, elevatorReach);

            if (stop == null)
            {
                return;
            }

            if (hub == null)
            {
                Debug.LogError(
                    $"{nameof(ManagerController)} on '{name}' was never injected, so nothing will " +
                    "hear that he reached an elevator.",
                    this);
                return;
            }

            LogOrder("reached " + stop.name + " on " + Storeys.LabelFor(floor) + ": offering its floors");
            hub.Publish(new ElevatorReached(stop));
        }

        /// <summary>
        /// Sends him to another floor by one particular elevator: first to its doors on his own
        /// floor, then - from there, where riding it is the shortest way - on to the floor asked
        /// for: to its doors there, or straight on to <paramref name="finish"/> when a spot there
        /// was asked for, such as one of its areas.
        /// </summary>
        /// <remarks>
        /// In legs so it is that elevator he takes. Sent straight from where he stands, he would go
        /// whichever way is shortest, which may be the stairs. From the doors, the walk to the spot
        /// is one walk - out of the car and on, without stopping - when its way runs through this
        /// elevator; otherwise he is sent to the far doors first, and on from there.
        /// <para>
        /// When the elevator does not stop on his floor, or he cannot walk to it, he goes by any way
        /// there is: to the spot or its doors on that floor, else wherever else that floor is
        /// reached (<paramref name="otherWays"/>). With none, he stays put and the player is told why.
        /// </para>
        /// </remarks>
        /// <param name="floor">The floor to go to, from 0.</param>
        /// <param name="board">In front of the elevator's doors on his floor; null when it does not stop there.</param>
        /// <param name="alight">In front of its doors on that floor; null when it does not stop there.</param>
        /// <param name="otherWays">Where else that floor can be stepped onto from another.</param>
        /// <param name="finish">
        /// Where on that floor to end up, best first - the first that is free and he can walk to is
        /// taken. Null or empty for the doors. On his own floor it is the only thing to go to: he
        /// walks there.
        /// </param>
        /// <param name="finishReach">
        /// How near a spot in <paramref name="finish"/> he must be able to stand for it to count as
        /// free: half an area's cell, so a spot under a desk is passed over for the next. Zero for
        /// the usual two metres.
        /// </param>
        public void RideTo(int floor, Vector3? board, Vector3? alight, IEnumerable<Vector3> otherWays,
            IEnumerable<Vector3> finish = null, float finishReach = 0f)
        {
            if (StandUpFirst(() => RideTo(floor, board, alight, otherWays, finish, finishReach)))
            {
                return;
            }

            // A spot on his own floor - one of its areas: no ride, only the walk there.
            if (Floors != null && floor == Floor)
            {
                WalkToFirst(finish, finishReach);
                return;
            }

            var to = default(NavMeshHit);
            var from = default(NavMeshHit);
            var canAlight = alight.HasValue && NavMesh.SamplePosition(alight.Value, out to, snapDistance, Filter);
            var rides = canAlight && board.HasValue
                && NavMesh.SamplePosition(board.Value, out from, snapDistance, Filter)
                && CanReach(from.position)
                && Joined(from.position, to.position);

            // The spot asked for: the first that is free and can be walked to from the far doors, or
            // from here when there is no stop there.
            Vector3? end = null;
            Vector3 first;

            if (TryFirstSpot(finish, finishReach, canAlight ? to.position : transform.position, out first))
            {
                end = first;
            }

            if (rides)
            {
                LogOrder("rides the elevator from " + Storeys.LabelFor(Floor) + " to " + Storeys.LabelFor(floor)
                    + (end.HasValue ? ", then on to " + end.Value.ToString("F2") : ""));

                GoTo(from.position);

                if (end.HasValue && Through(from.position, end.Value, to.position))
                {
                    legs.Enqueue(end.Value);
                }
                else
                {
                    legs.Enqueue(to.position);

                    if (end.HasValue)
                    {
                        legs.Enqueue(end.Value);
                    }
                }

                return;
            }

            var direct = end ?? (canAlight ? to.position : (Vector3?)null);

            if (direct.HasValue && CanReach(direct.Value))
            {
                LogOrder("goes to " + Storeys.LabelFor(floor) + " by another way: the elevator does not "
                    + "stop on " + Storeys.LabelFor(Floor) + ", or he cannot walk to it there");
                GoTo(direct.Value);

                // Sent to a floor, not to the elevator: arriving at its doors there offers nothing.
                offerAtEnd = false;
                return;
            }

            TryGoToFloor(floor, otherWays);
        }

        /// <summary>
        /// Walks him to the first of <paramref name="spots"/> that is free and he can reach.
        /// Arriving offers no elevator, even beside one: he was sent there, not to it.
        /// </summary>
        void WalkToFirst(IEnumerable<Vector3> spots, float reach)
        {
            Vector3 spot;

            if (TryFirstSpot(spots, reach, transform.position, out spot))
            {
                GoTo(spot);
                offerAtEnd = false;
                return;
            }

            LogOrder("walk refused: none of the spots asked for is free and can be reached from here");
        }

        /// <summary>
        /// Walks him, of his own accord, to the first of <paramref name="spots"/> that is free and he
        /// can reach - the middle of his area, or as near it as is free. Not an order: see
        /// <see cref="Stroll"/>. False when none is.
        /// </summary>
        public bool StrollToFirst(IEnumerable<Vector3> spots, float reach)
        {
            Vector3 spot;
            return TryFirstSpot(spots, reach, transform.position, out spot) && Stroll(spot);
        }

        /// <summary>How many free spots are tried for a way there before the rest are given up on.</summary>
        const int SpotTries = 24;

        /// <summary>
        /// The first of <paramref name="spots"/> that is free - he can stand within
        /// <paramref name="reach"/> of it - and that he can walk to from <paramref name="start"/>.
        /// </summary>
        /// <remarks>
        /// The reach is what keeps a spot under a desk from counting: with the usual two metres the
        /// nearest floor beside the desk would do, which may be outside the area altogether. Zero
        /// means those two metres. Spots that are free but out of reach are tried only so many
        /// times: an area cut off from him is cut off all over, and every try is a path search.
        /// </remarks>
        bool TryFirstSpot(IEnumerable<Vector3> spots, float reach, Vector3 start, out Vector3 found)
        {
            found = Vector3.zero;

            if (spots == null)
            {
                return false;
            }

            var radius = reach > 0f ? reach : snapDistance;
            var tries = 0;

            foreach (var point in spots)
            {
                NavMeshHit spot;
                if (!NavMesh.SamplePosition(point, out spot, radius, Filter))
                {
                    continue;   // something stands there
                }

                if (Joined(start, spot.position))
                {
                    found = spot.position;
                    return true;
                }

                if (++tries >= SpotTries)
                {
                    break;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether the way from <paramref name="from"/> to <paramref name="to"/> passes
        /// <paramref name="via"/>: a corner of it there. A ride's far doors are such a corner, as
        /// the end of the elevator's link.
        /// </summary>
        bool Through(Vector3 from, Vector3 to, Vector3 via)
        {
            if (!Joined(from, to))
            {
                return false;
            }

            foreach (var corner in Scratch.corners)
            {
                var dx = corner.x - via.x;
                var dz = corner.z - via.z;

                if (dx * dx + dz * dz < 0.36f && Mathf.Abs(corner.y - via.y) < 0.5f)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// He has reached where he was walking to: on to the next leg - the far end of the ride, or
        /// the spot beyond it.
        /// </summary>
        void TrackRide()
        {
            if (legs.Count == 0 || !agent.enabled || !agent.isOnNavMesh || !HasArrived())
            {
                return;
            }

            var next = legs.Dequeue();

            // A new walk forgets the legs after it, so they are kept aside and given back.
            var after = legs.ToArray();
            GoTo(next);

            foreach (var leg in after)
            {
                legs.Enqueue(leg);
            }

            // Part of the ride: it ends stepping out of the elevator, or where he was sent beyond
            // it, and neither offers anything.
            offerAtEnd = false;
        }

        /// <summary>The floor he is standing on, from 0, or 0 in a scene without floors.</summary>
        public int Floor => Floors != null ? Floors.StoreyAt(transform.position.y) : 0;

        /// <summary>Whether a complete path runs from where he is to <paramref name="point"/>.</summary>
        bool CanReach(Vector3 point) => Joined(transform.position, point);

        /// <summary>Whether a complete path runs between two points, for him.</summary>
        bool Joined(Vector3 from, Vector3 to)
        {
            return NavMesh.CalculatePath(from, to, Filter, Scratch)
                && Scratch.status == NavMeshPathStatus.PathComplete;
        }

        /// <summary>
        /// How far the manager has to walk to a point, and roughly how long it will take.
        /// </summary>
        /// <remarks>
        /// The path is calculated here rather than read off the agent, because the agent's own path
        /// is still pending on the frame the order is given — <c>remainingDistance</c> is
        /// <c>Infinity</c> until it resolves, which is no use to anything wanting to plan around the
        /// journey.
        /// <para>
        /// The estimate divides the walked distance by the agent's top speed, so it is a floor
        /// rather than a promise: acceleration at each end and slowing into turns both cost time it
        /// does not model. With speed 2 and acceleration 10 that is a fifth of a second at each
        /// change of pace — immaterial across a room, and worth knowing before timing anything
        /// tightly to it.
        /// </para>
        /// </remarks>
        public TravelEstimate EstimateTravel(Vector3 worldPoint)
        {
            if (agent == null)
            {
                return new TravelEstimate(worldPoint, 0f, 0f, false);
            }

            NavMeshHit navHit;
            var destination = NavMesh.SamplePosition(worldPoint, out navHit, snapDistance, Filter)
                ? navHit.position
                : worldPoint;

            var path = new NavMeshPath();
            var found = NavMesh.CalculatePath(transform.position, destination, Filter, path);

            // A partial path means the destination cannot actually be reached; the length is still
            // the honest answer for how far it will get.
            var complete = found && path.status == NavMeshPathStatus.PathComplete;
            var distance = LengthOf(path);
            var walked = Mathf.Max(0f, distance - agent.stoppingDistance);
            var seconds = walked / Mathf.Max(0.01f, agent.speed);

            return new TravelEstimate(destination, distance, seconds, complete);
        }

        /// <summary>Seconds until the manager reaches its current destination, live.</summary>
        /// <remarks>
        /// Zero both when standing still and while the path is still being computed, because in
        /// neither case is there an answer yet. Ask <see cref="IsTravelling"/> to tell those apart.
        /// </remarks>
        public float EstimatedSecondsRemaining
        {
            get
            {
                if (agent == null || !agent.enabled || !agent.isOnNavMesh || agent.pathPending)
                {
                    return 0f;
                }

                var remaining = agent.remainingDistance;
                if (float.IsInfinity(remaining) || float.IsNaN(remaining))
                {
                    return 0f;
                }

                var walked = Mathf.Max(0f, remaining - agent.stoppingDistance);
                return walked / Mathf.Max(0.01f, agent.speed);
            }
        }

        /// <summary>Total ground covered by a path, corner to corner.</summary>
        static float LengthOf(NavMeshPath path)
        {
            var corners = path.corners;
            if (corners == null || corners.Length < 2)
            {
                return 0f;
            }

            var total = 0f;
            for (var i = 1; i < corners.Length; i++)
            {
                total += Vector3.Distance(corners[i - 1], corners[i]);
            }

            return total;
        }

        /// <summary>
        /// Raised when the manager is given a new target to walk to, carrying how far and how long.
        /// </summary>
        /// <remarks>
        /// For the camera, which wants to show the walk and needs to know how much of one there is
        /// before deciding how to frame it. Raised after the destination is set and only when one
        /// was found, so a click on unreachable ground never announces a journey that will not
        /// happen.
        /// <para>
        /// Raised for every kind of target alike - a spot, an agent to go to, a seat - because they
        /// all set his destination through <see cref="Head"/>. Not raised again while the same
        /// target is only re-aimed, as when the agent he is following walks on.
        /// </para>
        /// </remarks>
        public event System.Action<TravelEstimate> TravelRequested;

        /// <summary>True while there is still ground to cover towards the current destination.</summary>
        public bool IsTravelling
        {
            get
            {
                if (agent == null || !agent.enabled || !agent.isOnNavMesh)
                {
                    return false;
                }

                return agent.pathPending || agent.remainingDistance > agent.stoppingDistance;
            }
        }

        void TrackTargetAgent()
        {
            if (targetAgent == null || !agent.enabled || !agent.isOnNavMesh)
            {
                return;
            }

            // The agent may still be wandering, so the route is refreshed rather than set once,
            // otherwise the manager would walk to where it used to be. Only when the target has
            // actually moved, though: re-issuing every frame keeps him nudging on the spot and he
            // never comes to rest, so the arrival test below would never pass.
            NavMeshHit navHit;
            if (NavMesh.SamplePosition(targetAgent.transform.position, out navHit, snapDistance, Filter))
            {
                var fresh = !hasIssuedDestination;
                var moved = fresh
                    || (navHit.position - lastIssuedDestination).sqrMagnitude > repathThreshold * repathThreshold;
                if (moved)
                {
                    Head(navHit.position, fresh);
                }
            }

            if (reachedTarget)
            {
                return;
            }

            // Contact first. Flat distance, because the two capsules sit at different heights and
            // only the ground separation decides whether they are touching.
            var a = transform.position;
            var b = targetAgent.transform.position;
            var separation = new Vector2(a.x - b.x, a.z - b.z).magnitude;
            var touching = separation <= contactDistance;

            // Otherwise, settle for having run out of path. A seated agent is up on a chair that
            // is baked Not Walkable, so the nearest standable spot can be further away than the
            // contact distance and the manager would otherwise wait there forever.
            var landed = !touching && HasArrived();

            if (!touching && !landed)
            {
                return;
            }

            reachedTarget = true;
            agent.ResetPath();

            if (hub != null)
            {
                hub.Publish(new AgentReached(targetAgent));
            }
            else
            {
                Debug.LogError(
                    $"{nameof(ManagerController)} on '{name}' was never injected, so nothing will " +
                    "hear that it reached an agent.",
                    this);
            }
        }

        // ---- Sitting --------------------------------------------------------------------

        /// <summary>Walks to <paramref name="chair"/> and hops onto it.</summary>
        public void SitOn(Chair chair)
        {
            if (chair == null)
            {
                return;
            }

            // Clicking the chair he is already on is a no-op rather than a stand-up-and-sit-again.
            if (seated && seat == chair)
            {
                return;
            }

            if (StandUpFirst(() => SitOn(chair)))
            {
                return;
            }

            ReleaseSeat();

            if (!chair.TryOccupy())
            {
                return;     // somebody is already in it
            }

            legs.Clear();
            offerAtEnd = false;
            targetAgent = null;
            reachedTarget = false;
            hasIssuedDestination = false;
            CancelTurnToCamera();

            // Floor within a hop of the seat that he can actually walk to. It used to be simply the
            // floor nearest the seat, reachable or not; with the way blocked he walked as far as
            // the obstacle, counted himself arrived, and hopped to the seat from there - half the
            // room in one jump.
            Vector3 approach;
            if (NavReach.TryReachableNear(transform.position, chair.SeatPosition, hopReach, Filter, null, Scratch, out approach))
            {
                seat = chair;
                walkingToSeat = true;
                agent.stoppingDistance = agentStoppingDistance;
                Head(approach, true);
                return;
            }

            // No way to the chair. He does not sit: he goes as close to it as he can and stops,
            // the nearest he can come to what was clicked.
            chair.Release();
            Debug.Log("[Manager] '" + chair.name + "' cannot be reached from here; walking as close "
                + "to it as he can instead.", this);

            GoTo(chair.SeatPosition);
        }

        /// <summary>Hops off the current seat, then runs <paramref name="next"/>.</summary>
        public void StandUp(System.Action next = null)
        {
            if (!seated)
            {
                next?.Invoke();
                return;
            }

            seated = false;

            if (seat != null)
            {
                seat.SetInUse(false);
            }

            afterStandingUp = next;
            hoppingToSeat = false;
            hop.Begin(transform, seatStandPoint, transform.rotation, jumpSeconds, jumpHeight);
        }

        /// <summary>
        /// Queues <paramref name="retry"/> behind a stand-up when seated. Returns true when the
        /// caller should back off and let the hop finish.
        /// </summary>
        bool StandUpFirst(System.Action retry)
        {
            // Interrupted mid-hop: abandon it and drop straight back to the floor. Calling
            // StandUp here instead would re-enter this method and recurse.
            if (hop.IsHopping)
            {
                hop.Cancel();
                hoppingToSeat = false;
                transform.position = seatStandPoint;

                if (!agent.enabled)
                {
                    agent.enabled = true;
                    agent.Warp(seatStandPoint);
                }

                ReleaseSeat();
                afterStandingUp = null;
                return false;   // the caller can carry on straight away
            }

            if (!seated)
            {
                return false;
            }

            StandUp(retry);
            return true;
        }

        void TrackSeat()
        {
            if (!walkingToSeat || seat == null || !agent.enabled || !agent.isOnNavMesh)
            {
                return;
            }

            var flat = new Vector2(transform.position.x - seat.SeatPosition.x,
                                   transform.position.z - seat.SeatPosition.z).magnitude;

            if (flat > hopReach && !HasArrived())
            {
                return;
            }

            // Stopping is not arriving. Something laid across the way after the walk began stops
            // him short too, and the hop is only ever from within reach of the seat.
            if (flat > hopReach + agent.stoppingDistance + 0.1f)
            {
                Debug.Log("[Manager] Stopped " + flat.ToString("F1") + "m short of '" + seat.name
                    + "' - the way is blocked, so he stays where he got to.", this);

                ReleaseSeat();
                agent.ResetPath();
                return;
            }

            // The seat is off the NavMesh, so hand the transform over before hopping onto it.
            walkingToSeat = false;
            seatStandPoint = transform.position;
            agent.ResetPath();
            agent.enabled = false;

            hoppingToSeat = true;
            hop.Begin(transform, seat.SeatPosition + Vector3.up * halfHeight, seat.SeatRotation,
                      jumpSeconds, jumpHeight);
        }

        void FinishHop()
        {
            if (hoppingToSeat)
            {
                seated = true;

                if (seat != null)
                {
                    seat.SetInUse(true);
                }

                return;
            }

            // Landed back on the floor.
            if (!agent.enabled)
            {
                agent.enabled = true;
                agent.Warp(transform.position);
            }

            ReleaseSeat();

            var next = afterStandingUp;
            afterStandingUp = null;
            next?.Invoke();
        }

        void ReleaseSeat()
        {
            if (seat != null)
            {
                seat.Release();
                seat = null;
            }

            seated = false;
            walkingToSeat = false;
        }

        /// <summary>
        /// True once the manager has finished walking. remainingDistance reads Infinity while no
        /// path exists, so this is false before one has been computed rather than firing early.
        /// </summary>
        bool HasArrived()
        {
            if (!hasIssuedDestination || agent.pathPending)
            {
                return false;
            }

            return agent.remainingDistance <= Mathf.Max(agent.stoppingDistance, 0.05f);
        }

        // ---- Facing the camera on arrival ------------------------------------------------

        /// <summary>
        /// The rotation a character standing at <paramref name="from"/> needs to look at
        /// <paramref name="cameraPosition"/>, with the camera's height discarded.
        /// </summary>
        /// <remarks>
        /// Yaw only. The isometric camera sits well above the floor, and feeding that height into
        /// the rotation would pitch the manager backwards off his feet. Returns false when the
        /// camera is directly overhead: flattening leaves a zero-length direction, and
        /// <see cref="Quaternion.LookRotation(Vector3)"/> of that is undefined.
        /// </remarks>
        public static bool TryFacingTowards(Vector3 from, Vector3 cameraPosition, out Quaternion facing)
        {
            var flat = cameraPosition - from;
            flat.y = 0f;

            if (flat.sqrMagnitude < 0.0001f)
            {
                facing = Quaternion.identity;
                return false;
            }

            facing = Quaternion.LookRotation(flat.normalized, Vector3.up);
            return true;
        }

        /// <summary>
        /// Starts the turn the first frame a plain walk finishes, and drives it thereafter.
        /// </summary>
        /// <remarks>
        /// Plain walks only. Following an agent ends facing that agent and sitting ends facing the
        /// way the chair points; both are meaningful, so neither is overridden here.
        /// </remarks>
        void TrackArrivalFacing()
        {
            if (turningToCamera)
            {
                TickTurnToCamera();
                return;
            }

            if (!faceCameraOnArrival || facedCamera || targetAgent != null || walkingToSeat)
            {
                return;
            }

            if (!agent.enabled || !agent.isOnNavMesh || !HasArrived())
            {
                return;
            }

            facedCamera = true;
            BeginTurnToCamera();
        }

        void BeginTurnToCamera()
        {
            if (view == null)
            {
                view = Camera.main;
            }

            if (view == null)
            {
                return;
            }

            BeginTurnTo(view.transform.position);
        }

        /// <summary>
        /// Turns the manager to face <paramref name="viewPosition"/>, if he is standing still with
        /// nothing else to face.
        /// </summary>
        /// <remarks>
        /// For the camera, after the player has panned or swung the view: the same smooth, yaw-only
        /// turn he makes on arriving somewhere. Refused while he is walking, following an agent,
        /// sitting or hopping - each of those decides which way he faces, and turning him mid-way
        /// would fight it. A turn already running is restarted towards the new view.
        /// </remarks>
        public void FaceCamera(Vector3 viewPosition)
        {
            if (IsTravelling || targetAgent != null || seated || walkingToSeat || hop.IsHopping)
            {
                return;
            }

            // Ended first so the agent's own rotation setting is put back before it is saved again;
            // saving it mid-turn would save the "off" the turn had set, and leave it off for good.
            EndTurnToCamera();
            BeginTurnTo(viewPosition);
        }

        void BeginTurnTo(Vector3 viewPosition)
        {
            Quaternion facing;
            if (!TryFacingTowards(transform.position, viewPosition, out facing))
            {
                return;
            }

            // Already looking that way — playing a turn would read as a twitch.
            if (Quaternion.Angle(transform.rotation, facing) < 0.5f)
            {
                return;
            }

            turnFrom = transform.rotation;
            turnTo = facing;
            turnStartedAt = Time.time;
            turningToCamera = true;

            // The agent steers rotation itself while it owns one, and would fight the slerp.
            rotationWasAgentDriven = agent.updateRotation;
            agent.updateRotation = false;
        }

        void TickTurnToCamera()
        {
            var k = faceCameraSeconds <= 0f
                ? 1f
                : Mathf.Clamp01((Time.time - turnStartedAt) / faceCameraSeconds);

            transform.rotation = Quaternion.Slerp(turnFrom, turnTo, Mathf.SmoothStep(0f, 1f, k));

            if (k >= 1f)
            {
                EndTurnToCamera();
            }
        }

        /// <summary>Stops the turn and hands rotation back to the agent.</summary>
        void EndTurnToCamera()
        {
            if (!turningToCamera)
            {
                return;
            }

            turningToCamera = false;
            agent.updateRotation = rotationWasAgentDriven;
        }

        /// <summary>
        /// Drops any turn in progress and re-arms it for the next arrival. Called by every order
        /// that sends the manager somewhere new.
        /// </summary>
        void CancelTurnToCamera()
        {
            EndTurnToCamera();
            facedCamera = false;
        }
    }
}
