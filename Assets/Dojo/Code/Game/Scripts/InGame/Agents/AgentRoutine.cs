using System.Collections.Generic;
using Dojo.Game.Components;
using Dojo.Game.InGame.Controllers;
using UnityEngine;
using UnityEngine.AI;

namespace Dojo.Game.InGame.Agents
{
    /// <summary>
    /// Wander the home floor for a few seconds, then hop onto a chair and sit for a while, then
    /// wander again.
    /// <para>
    /// Confinement works one of two ways, depending on which kind of world this agent is in.
    /// </para>
    /// <para>
    /// <b>Authored scene.</b> The NavMesh area mask does it, not good behaviour: the agent may only
    /// path over <b>Walkable</b>, every floor outside its own area is baked as <b>Outside</b>, and
    /// so a route out simply does not exist. <see cref="SummonTo"/> adds Outside to the mask for
    /// the length of an errand and <see cref="SendHome"/> takes it away again.
    /// </para>
    /// <para>
    /// <b>A world the player builds.</b> There the mask cannot help: every floor laid is baked
    /// Walkable, so the mask holds the whole world. Confinement is the set of cells the player
    /// painted, handed over by <see cref="ConfineTo"/> — every destination is chosen from those
    /// cells, and an agent that finds itself outside them walks back. The area mask still guards
    /// errands, which are the one time an agent is meant to leave.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class AgentRoutine : MonoBehaviour
    {
        enum State
        {
            Wandering,
            ApproachingChair,
            SittingDown,
            Sitting,
            StandingUp,
            ReturningHome,
        }

        [Header("Area")]
        [Tooltip("The floor this agent may roam. Defaults to a sibling named 'setfloor'.")]
        [SerializeField] Renderer homeFloor;

        [Tooltip("Root to search for chairs. Leave empty to consider every chair in the scene, " +
                 "which is what lets a chair moved in from another area be used. What an agent " +
                 "will actually walk to is decided by its home floor below, not by this.")]
        [SerializeField] Transform chairRoot;

        [Header("Timing")]
        [Tooltip("How long a wandering stretch lasts before the agent heads for a chair.")]
        [SerializeField] Vector2 wanderSeconds = new Vector2(3f, 10f);

        [Tooltip("How long the agent stays seated. A fresh value in this range is rolled each sit. " +
                 "X is the minimum, Y the maximum.")]
        [SerializeField] Vector2 sitSeconds = new Vector2(10f, 60f);

        [Tooltip("Random pause after reaching a wander destination.")]
        [SerializeField] Vector2 pauseSeconds = new Vector2(0.5f, 2f);

        [Tooltip("Seconds the hop onto or off the seat takes.")]
        [SerializeField] float jumpSeconds = 0.5f;

        [Tooltip("Peak height of the hop, above the straight line between the floor and the seat.")]
        [SerializeField] float jumpHeight = 0.8f;

        [Tooltip("Give up walking to a chair in its own area after this long.")]
        [SerializeField] float approachTimeout = 15f;

        [Tooltip("Give up on a summons or a trip home after this long. The journey is much longer.")]
        [SerializeField] float errandTimeout = 60f;

        [Header("Hopping")]
        [Tooltip("Furthest the agent will hop onto a seat, measured across the floor. A chair it " +
                 "cannot walk this close to is skipped rather than jumped at from across the room.")]
        [SerializeField] float hopReach = 1.5f;

        [Tooltip("Furthest the agent will hop to get out of a corner it cannot walk out of.")]
        [SerializeField] float escapeReach = 3f;

        [Header("Confinement")]
        [Tooltip("Area mask for normal life. Bit 0 = Walkable, which is only the area floors, so " +
                 "the agent physically cannot path off its own floor.")]
        [SerializeField] int homeAreaMask = 1 << 0;

        [Tooltip("Area mask while running an errand. Adds bit 3 = Outside, the shared floors.")]
        [SerializeField] int travelAreaMask = (1 << 0) | (1 << 3);

        [SerializeField] int samplesPerPick = 12;

        NavMeshAgent agent;
        Chair[] chairs;
        Bounds home;

        // The cells the player painted: a set for asking "is this point mine", and a list for
        // picking somewhere to walk. Both empty in the authored scene, where an agent's floor is
        // one renderer and the rectangle around it is the honest answer. Each cell is keyed by its
        // floor too, so a cell on 3F is not the agent's just because it stands under it on 1F.
        readonly HashSet<Vector3Int> homeCells = new HashSet<Vector3Int>();
        readonly List<Vector3> homeCentres = new List<Vector3>();
        float homeCellSize;

        /// <summary>
        /// Which floor a height is on, or null in a scene without floors, where every cell is on
        /// the one there is. Set by whoever brings the agent to life.
        /// </summary>
        public System.Func<float, int> FloorOf { get; set; }

        // The elevator ride, when the agent has one: its routine stands aside for the length of it.
        ElevatorRider rider;

        State state;
        float wanderEndsAt;
        float resumeAt;
        float sitEndsAt;
        float giveUpAt;
        Chair target;
        Chair pendingSummon;
        bool summoned;
        bool halted;
        Vector3 standPoint;
        float halfHeight;

        // Wander picks in a row that found nowhere reachable. Enough of them, and the agent asks
        // whether it is shut in rather than standing still for the rest of the session.
        const int ShutInAfterFailedPicks = 30;
        int failedPicks;

        // How many of the nearest painted cells are tried for a way back in.
        const int WayBackCandidates = 32;

        // When a trip home with no walk home available asks again.
        float retryHomeAt;

        bool warnedShutIn;
        NavMeshPath scratch;
        readonly List<Vector3> anchors = new List<Vector3>();
        readonly List<Vector3> byDistance = new List<Vector3>();

        NavMeshPath Scratch => scratch ?? (scratch = new NavMeshPath());

        /// <summary>This agent's own walkable floor, and the areas it may use on it.</summary>
        /// <remarks>
        /// Every query says which floor it means. There is one floor per model size, and a query
        /// that did not say could be answered from somebody else's - a thinner character's floor
        /// reaches closer to walls than this one can.
        /// </remarks>
        NavMeshQueryFilter Filter => new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };

        // Eased hop between the floor and a seat.
        Vector3 glideFromPos;
        Quaternion glideFromRot;
        Vector3 glideToPos;
        Quaternion glideToRot;
        float glideStartedAt;

        /// <summary>True while the agent is away on a summons rather than on its own floor.</summary>
        public bool IsSummoned => summoned;

        /// <summary>True while the agent has been stopped in place and is ignoring its routine.</summary>
        public bool IsHalted => halted;

        /// <summary>
        /// True while the agent is on a chair, i.e. actually at a desk working rather than
        /// wandering the floor.
        /// </summary>
        public bool IsSeated => state == State.Sitting || state == State.SittingDown;

        /// <summary>
        /// While true the agent keeps walking its floor and never heads for a chair: seated, it
        /// stands up; on its way to one, it lets it go. Switched off, the routine carries on as
        /// normal from a fresh stretch of wandering.
        /// </summary>
        /// <remarks>
        /// For the first-time tour's "Meet your agent", where the camera follows an agent the
        /// player is asked to click - one sat at a desk is small, still, and easy to miss.
        /// </remarks>
        public bool StaysOnFeet
        {
            get { return staysOnFeet; }
            set
            {
                if (staysOnFeet == value)
                {
                    return;
                }

                staysOnFeet = value;

                if (!value)
                {
                    if (state == State.Wandering)
                    {
                        wanderEndsAt = Time.time + Random.Range(wanderSeconds.x, wanderSeconds.y);
                    }

                    return;
                }

                switch (state)
                {
                    case State.Sitting:
                        // Up at once; the hop off the seat still plays.
                        sitEndsAt = 0f;
                        break;
                    case State.ApproachingChair:
                        GiveUpOnChair();
                        break;
                }
            }
        }

        bool staysOnFeet;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();

            if (homeFloor == null && transform.parent != null)
            {
                var floor = transform.parent.Find("setfloor");
                if (floor != null)
                {
                    homeFloor = floor.GetComponent<Renderer>();
                }
            }

            home = homeFloor != null
                ? homeFloor.bounds
                : new Bounds(transform.position, new Vector3(4f, 4f, 4f));

            RescanChairs();

            // Sitting puts the capsule's feet on the seat, so the pivot has to clear it by half
            // the visual height. Read it rather than assume, since the capsule is scaled.
            var visual = GetComponent<Renderer>();
            halfHeight = visual != null ? visual.bounds.extents.y : 0.4f;

            agent.areaMask = homeAreaMask;
        }

        void OnEnable() => BeginWandering();

        void OnDisable() => ReleaseChair();

        void Update()
        {
            if (halted)
            {
                return;
            }

            // In a closed car between floors the agent is off the NavMesh's surface: nothing is
            // asked of it until the ride has put it down on the far side.
            if (rider == null)
            {
                rider = GetComponent<ElevatorRider>();
            }

            if (rider != null && rider.IsRiding)
            {
                return;
            }

            switch (state)
            {
                case State.Wandering:
                    TickWandering();
                    break;
                case State.ApproachingChair:
                    TickApproaching();
                    break;
                case State.SittingDown:
                    TickGliding(State.Sitting);
                    break;
                case State.Sitting:
                    TickSitting();
                    break;
                case State.StandingUp:
                    TickGliding(State.Wandering);
                    break;
                case State.ReturningHome:
                    TickReturningHome();
                    break;
            }
        }

        // ---- Public API ---------------------------------------------------------------

        /// <summary>
        /// Send the agent to <paramref name="chair"/>, wherever it is, and have it sit there. If it
        /// is currently seated it stands up first, so the handover is never a teleport.
        /// </summary>
        public void SummonTo(Chair chair)
        {
            if (chair == null || !isActiveAndEnabled)
            {
                return;
            }

            switch (state)
            {
                case State.Sitting:
                    // Cut the sit short; the hop off the seat still plays.
                    pendingSummon = chair;
                    sitEndsAt = 0f;
                    return;
                case State.SittingDown:
                case State.StandingUp:
                    pendingSummon = chair;
                    return;
                default:
                    BeginSummon(chair);
                    return;
            }
        }

        /// <summary>
        /// Stops the agent where it stands and suspends its routine. Any chair it had claimed is
        /// released so nobody else is locked out of it while it waits.
        /// </summary>
        public void Halt()
        {
            halted = true;
            ReleaseChair();

            if (agent.enabled && agent.isOnNavMesh)
            {
                agent.ResetPath();
                agent.velocity = Vector3.zero;
                agent.isStopped = true;
            }
        }

        /// <summary>Releases a <see cref="Halt"/> and starts the routine over from wandering.</summary>
        public void Resume()
        {
            if (!halted)
            {
                return;
            }

            halted = false;

            // A halt can land mid-hop or mid-sit, which leaves the NavMeshAgent switched off and the
            // capsule off the mesh. Put it back on the floor before handing control back.
            if (!agent.enabled)
            {
                // Where it hopped up from, while that is still floor, and only then the floor
                // nearest to where it is. The nearest floor to a seat is very often the shut-in
                // corner between the chair, the desk and the wall - which is where an agent landed
                // every time the room went from Edit back to Play while it was sitting.
                NavMeshHit hit;
                Vector3 landing;

                if (NavMesh.SamplePosition(standPoint, out hit, 0.5f, Filter))
                {
                    landing = hit.position;
                }
                else if (NavMesh.SamplePosition(transform.position, out hit, 3f, Filter))
                {
                    landing = hit.position;
                }
                else
                {
                    landing = standPoint;
                }
                transform.position = landing;
                agent.enabled = true;
                agent.Warp(landing);
            }

            if (agent.isOnNavMesh)
            {
                agent.isStopped = false;
            }

            BeginWandering();
        }

        /// <summary>Walk back to the home floor and resume the normal routine.</summary>
        public void SendHome()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            pendingSummon = null;

            switch (state)
            {
                case State.Sitting:
                    summoned = true;    // so the stand-up hands over to ReturningHome
                    sitEndsAt = 0f;
                    return;
                case State.SittingDown:
                case State.StandingUp:
                    summoned = true;
                    return;
                default:
                    BeginReturningHome();
                    return;
            }
        }

        // ---- Wandering ----------------------------------------------------------------

        void BeginWandering()
        {
            state = State.Wandering;
            wanderEndsAt = Time.time + Random.Range(wanderSeconds.x, wanderSeconds.y);
            resumeAt = Time.time;
        }

        void TickWandering()
        {
            if (!agent.enabled || !agent.isOnNavMesh || agent.pathPending)
            {
                return;
            }

            // Checked before the "still walking" test so the agent breaks off its current stroll
            // and heads for a chair as soon as the timer is up, rather than finishing the leg.
            if (Time.time >= wanderEndsAt && chairs.Length > 0 && !staysOnFeet)
            {
                BeginApproachingChair();
                return;
            }

            // Before anything else about where to go next: are we even in the area? Choosing every
            // destination from the painted cells is not on its own enough to stay inside them. A
            // path between two cells is worked out by the NavMesh, which knows nothing about the
            // painting and will happily route the long way round a desk and out across a
            // neighbour's floor. This is the leash for that, and for a drift out of the area by any
            // other route — a summons that ended somewhere odd, or a floor taken away underfoot.
            if (Stray())
            {
                return;
            }

            // hasPath is false before the first destination and after arriving, which is exactly
            // when a new one is wanted. remainingDistance alone will not do: it reads Infinity
            // while no path exists, so the agent would never set off.
            if (agent.hasPath && agent.remainingDistance > agent.stoppingDistance)
            {
                return;
            }

            if (Time.time < resumeAt)
            {
                return;
            }

            Vector3 destination;
            if (TryPickFloorPoint(out destination))
            {
                failedPicks = 0;
                agent.SetDestination(destination);
                resumeAt = Time.time + Random.Range(pauseSeconds.x, pauseSeconds.y);
            }
            else if (++failedPicks >= ShutInAfterFailedPicks)
            {
                // Nowhere in its own area it can walk to, time after time. Only now is it worth
                // asking whether it is shut in and has to hop out; if not, it waits and tries again.
                failedPicks = 0;

                if (!TryEscape())
                {
                    resumeAt = Time.time + 5f;
                }
            }
        }

        /// <summary>
        /// Somewhere inside the area that the agent can walk to from where it is.
        /// </summary>
        /// <remarks>
        /// Candidates come from the painted cells when there are any, rather than from anywhere
        /// in the rectangle around them. Sampling that rectangle is how an agent ended up
        /// strolling across floor outside its own area: on an L-shaped or scattered painting,
        /// most of the rectangle is somebody else's floor.
        /// <para>
        /// <b>Walkable, not just inside the area.</b> Walls split a painted area into rooms, and a
        /// cell in the next room is still "in the area". Sent there, the agent walked as far as the
        /// wall between, stopped against it, and stood there - which is what "going into walls" was.
        /// A candidate only counts when there is a complete path to it.
        /// </para>
        /// </remarks>
        bool TryPickFloorPoint(out Vector3 destination)
        {
            destination = transform.position;

            Vector3 from;
            var canCheck = TryStandingPoint(out from);

            for (var i = 0; i < samplesPerPick; i++)
            {
                var candidate = HasPaintedArea ? RandomPointInArea() : new Vector3(
                    Random.Range(home.min.x, home.max.x),
                    home.center.y,
                    Random.Range(home.min.z, home.max.z));

                // A tight search radius matters: a candidate that lands on furniture should fail
                // rather than snap to whatever mesh happens to be nearest.
                NavMeshHit hit;
                if (!NavMesh.SamplePosition(candidate, out hit, 1f, Filter) || !IsHome(hit.position))
                {
                    continue;
                }

                if (canCheck && !Reachable(from, hit.position))
                {
                    continue;
                }

                destination = hit.position;
                return true;
            }

            return false;
        }

        /// <summary>Whether there is a complete walk from one floor point to another.</summary>
        bool Reachable(Vector3 from, Vector3 to)
            => NavMesh.CalculatePath(from, to, Filter, Scratch)
               && Scratch.status == NavMeshPathStatus.PathComplete;

        /// <summary>
        /// The nearest point inside the area that the agent can actually walk to.
        /// </summary>
        /// <remarks>
        /// Nearest by the straight line was the old rule, and it sent an agent standing just
        /// outside its area to the cell under the wall beside it. The floor nearest that cell was
        /// the very spot the agent stood on, so it was told every frame to go where it already was,
        /// and never moved again. Each candidate here has to be inside the area, somewhere other
        /// than here, and reachable.
        /// </remarks>
        bool TryWayBackIn(out Vector3 back)
        {
            back = transform.position;

            Vector3 here;
            if (!TryStandingPoint(out here))
            {
                return false;
            }

            byDistance.Clear();
            byDistance.AddRange(homeCentres);
            byDistance.Sort((p, q) => NavReach.Flat(p, here).CompareTo(NavReach.Flat(q, here)));

            var tries = Mathf.Min(byDistance.Count, WayBackCandidates);

            for (var i = 0; i < tries; i++)
            {
                NavMeshHit hit;
                if (!NavMesh.SamplePosition(byDistance[i], out hit, homeCellSize, Filter))
                {
                    continue;   // under furniture: no floor there at all
                }

                // Snapped out of the cell - off the side of a wall, typically - or onto the spot
                // it is already standing on. Neither brings it home.
                if (!IsHome(hit.position) || NavReach.Flat(hit.position, here) < 0.1f)
                {
                    continue;
                }

                if (!Reachable(here, hit.position))
                {
                    continue;
                }

                back = hit.position;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Sends the agent back into its area if it has wandered out, and says whether it did.
        /// </summary>
        /// <remarks>
        /// Only ever applies to a painted area, and only while the agent is living its own life:
        /// a summons is the one time it is <em>meant</em> to be off its floor, and hauling it back
        /// mid-errand would fight the thing that sent it.
        /// <para>
        /// Re-issued rather than teleported. The agent walks back the way it came, which reads as
        /// an agent thinking better of where it is rather than as a glitch — and it cannot fail the
        /// way a teleport onto an unknown patch of NavMesh can.
        /// </para>
        /// <para>
        /// The way back is the nearest part of the area the agent can walk to - see
        /// <see cref="TryWayBackIn"/> - and once it is on its way it is left to walk rather than
        /// re-planned every frame. With no way back at all it is left to the ordinary wander, which
        /// finds nothing either and in the end asks whether it is shut in.
        /// </para>
        /// </remarks>
        bool Stray()
        {
            if (!HasPaintedArea || summoned || IsHome(transform.position))
            {
                return false;
            }

            // Already walking back in.
            if (agent.hasPath && agent.remainingDistance > agent.stoppingDistance && IsHome(agent.destination))
            {
                return true;
            }

            Vector3 back;
            if (!TryWayBackIn(out back))
            {
                return false;
            }

            agent.SetDestination(back);

            // Held to the walk back rather than re-picked next frame, which would have it choosing
            // a new cell every frame for as long as it takes to get there.
            resumeAt = Time.time + 0.5f;

            return true;
        }

        /// <summary>
        /// A point inside one randomly chosen painted cell.
        /// </summary>
        /// <remarks>
        /// Jittered within the cell rather than sitting on its centre, so a small area does not
        /// have the agent pacing between the same few exact spots. Kept well inside the edge, so
        /// the point cannot fall into a neighbouring cell and fail the home test that is about to
        /// be applied to it.
        /// </remarks>
        Vector3 RandomPointInArea()
        {
            var centre = homeCentres[Random.Range(0, homeCentres.Count)];
            var reach = homeCellSize * 0.4f;

            return new Vector3(
                centre.x + Random.Range(-reach, reach),
                centre.y,
                centre.z + Random.Range(-reach, reach));
        }

        // ---- Walking to the chosen chair ----------------------------------------------

        void BeginApproachingChair()
        {
            var chair = PickFreeChair();
            if (chair == null)
            {
                // Every chair taken; keep wandering and try again shortly.
                wanderEndsAt = Time.time + Random.Range(wanderSeconds.x, wanderSeconds.y);
                return;
            }

            Vector3 approach;
            if (!TryFindApproachPoint(chair, true, out approach))
            {
                chair.Release();
                wanderEndsAt = Time.time + Random.Range(wanderSeconds.x, wanderSeconds.y);
                return;
            }

            target = chair;
            standPoint = approach;
            agent.SetDestination(standPoint);
            giveUpAt = Time.time + approachTimeout;
            state = State.ApproachingChair;
        }

        void BeginSummon(Chair chair)
        {
            ReleaseChair();

            if (!chair.TryOccupy())
            {
                return;     // somebody is already in it
            }

            summoned = true;
            agent.areaMask = travelAreaMask;

            Vector3 approach;
            if (!TryFindApproachPoint(chair, false, out approach))
            {
                chair.Release();
                summoned = false;
                agent.areaMask = homeAreaMask;
                BeginWandering();
                return;
            }

            target = chair;
            standPoint = approach;
            agent.SetDestination(standPoint);
            giveUpAt = Time.time + errandTimeout;
            state = State.ApproachingChair;
        }

        /// <summary>
        /// Floor within a hop of the seat that the agent can actually walk to from where it is.
        /// The agent hops the remaining gap, so it has no reason to walk around to the back first.
        /// </summary>
        /// <remarks>
        /// It used to be simply the closest floor to the seat. Next to a desk and a wall that is
        /// often a corner with no way in, and an agent sent there stopped short, counted itself
        /// arrived, and hopped to the seat from wherever it had stopped - which is how one jumped
        /// from the red chair to the blue one. A chair with no reachable floor near it is now
        /// simply not a chair this agent can use from here.
        /// </remarks>
        bool TryFindApproachPoint(Chair chair, bool mustBeHome, out Vector3 approach)
        {
            approach = transform.position;

            Vector3 from;
            if (!TryStandingPoint(out from))
            {
                return false;
            }

            return NavReach.TryReachableNear(
                from, chair.SeatPosition, hopReach, Filter,
                mustBeHome ? (System.Func<Vector3, bool>)IsHome : null,
                Scratch, out approach);
        }

        /// <summary>Where the agent is standing on the NavMesh, if it is on it.</summary>
        bool TryStandingPoint(out Vector3 point)
        {
            NavMeshHit hit;
            if (NavMesh.SamplePosition(transform.position, out hit, 1f, Filter))
            {
                point = hit.position;
                return true;
            }

            point = transform.position;
            return false;
        }

        /// <summary>
        /// Hops the agent out of a corner it cannot walk out of, and says whether it did.
        /// </summary>
        /// <remarks>
        /// Only when it really is shut in: it can walk to <em>none</em> of its own area from where
        /// it stands. A painted area split by walls into rooms is not that - the agent simply lives
        /// in the part it can reach, and never hops over a wall into another. The first version of
        /// this asked for "most of the area", treated a split area as a trap, and was the wrong
        /// rule. When it is shut in, the nearest floor within <see cref="escapeReach"/> that
        /// reaches any of its area is where it hops to, inside its area if there is any.
        /// <para>
        /// A hop rather than a teleport, with the same arc as getting off a chair, so it reads as
        /// the agent stepping over something rather than as a glitch.
        /// </para>
        /// </remarks>
        bool TryEscape()
        {
            Vector3 here;
            if (!TryStandingPoint(out here))
            {
                return false;
            }

            var reference = AreaAnchors();

            if (NavReach.IsOpen(here, reference, Filter, Scratch))
            {
                return false;
            }

            Vector3 exit;
            var found = (HasPaintedArea
                    && NavReach.TryWayOut(here, escapeReach, reference, Filter, IsHome, Scratch, out exit))
                || NavReach.TryWayOut(here, escapeReach, reference, Filter, null, Scratch, out exit);

            if (!found)
            {
                if (!warnedShutIn)
                {
                    warnedShutIn = true;
                    Debug.LogWarning("[Agent] '" + name + "' is shut in at " + here.ToString("F2")
                        + " and no open floor is within " + escapeReach + "m to hop out to.", this);
                }

                return false;
            }

            Debug.Log("[Agent] '" + name + "' was shut in at " + here.ToString("F2")
                + "; hopping out to " + exit.ToString("F2") + ".", this);

            // The same hop as getting down off a seat: the NavMeshAgent lets go of the transform,
            // and the landing hands it back and resumes whatever the agent was doing.
            ReleaseChair();
            agent.enabled = false;

            // Where a halt mid-hop lands it, as for a hop off a seat.
            standPoint = exit;

            var facing = exit - transform.position;
            facing.y = 0f;

            BeginGlide(
                exit,
                facing.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(facing) : transform.rotation,
                State.StandingUp);

            return true;
        }

        /// <summary>
        /// A handful of floor points spread over the agent's area, to judge "can I walk around my
        /// own area from here" against.
        /// </summary>
        List<Vector3> AreaAnchors()
        {
            anchors.Clear();

            NavMeshHit hit;

            if (HasPaintedArea)
            {
                var step = Mathf.Max(1, homeCentres.Count / 8);

                for (var i = 0; i < homeCentres.Count; i += step)
                {
                    if (NavMesh.SamplePosition(homeCentres[i], out hit, homeCellSize, Filter))
                    {
                        anchors.Add(hit.position);
                    }
                }

                return anchors;
            }

            for (var x = 0; x < 3; x++)
            {
                for (var z = 0; z < 3; z++)
                {
                    var probe = new Vector3(
                        Mathf.Lerp(home.min.x, home.max.x, (x + 0.5f) / 3f),
                        home.center.y,
                        Mathf.Lerp(home.min.z, home.max.z, (z + 0.5f) / 3f));

                    if (NavMesh.SamplePosition(probe, out hit, 1f, Filter))
                    {
                        anchors.Add(hit.position);
                    }
                }
            }

            return anchors;
        }

        /// <summary>
        /// Rebuilds the list of chairs this agent knows about.
        /// </summary>
        /// <remarks>
        /// Public because furniture moves and is spawned at runtime, and a list captured once in
        /// Awake would never see any of it. With no <c>chairRoot</c> assigned this takes every
        /// chair in the scene rather than only the ones parented under this agent's own area: a
        /// chair carried in from another area is a chair, and the hierarchy it happens to sit in
        /// says nothing about where it now stands. Which of them the agent will actually use is
        /// still decided by <see cref="IsHome"/>.
        /// </remarks>
        public void RescanChairs()
        {
            chairs = chairRoot != null
                ? chairRoot.GetComponentsInChildren<Chair>(true)
                : FindObjectsByType<Chair>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }

        /// <summary>
        /// Replaces the floor this agent roams with the exact cells the player painted.
        /// </summary>
        /// <remarks>
        /// Public because an area is defined long after Awake has run: the agent is placed out of
        /// the drawer onto ground the player has just laid, and the area they may walk is painted
        /// afterwards. There is no <c>setfloor</c> sibling to find in that world at all, so
        /// without this a placed agent falls back to a 4m box around wherever they landed.
        /// <para>
        /// <b>The cells themselves, not the rectangle around them.</b> This took a rectangle at
        /// first, on the reasoning that the NavMesh would do the real confining - that
        /// <see cref="homeAreaMask"/> is bit 0 and every floor outside an area bakes as Outside,
        /// so a generous rectangle costs nothing. That is true of the authored scene and false of
        /// a world the player builds: every floor laid there is baked Walkable, so the mask holds
        /// the whole world and confines nothing. The rectangle was the only thing keeping an agent
        /// in, and the rectangle around an L-shaped or ragged painting covers a great deal of
        /// floor nobody painted - which is exactly where agents were drifting off to.
        /// </para>
        /// <para>
        /// An empty area is refused rather than taken, because the empty region it would install
        /// has every wander destination collapse onto one point - an agent standing perfectly
        /// still for the rest of the session, with nothing on screen to say why.
        /// </para>
        /// </remarks>
        public void ConfineTo(IList<Vector3> cells, float cellSize)
        {
            if (cells == null || cells.Count == 0 || cellSize <= 0f)
            {
                Debug.LogWarning("[Agent] '" + name + "' was given an empty area; keeping the one "
                    + "it had.", this);
                return;
            }

            homeCellSize = cellSize;
            homeCells.Clear();
            homeCentres.Clear();

            var min = cells[0];
            var max = cells[0];

            foreach (var centre in cells)
            {
                homeCells.Add(CellKey(centre));
                homeCentres.Add(centre);

                min = Vector3.Min(min, centre);
                max = Vector3.Max(max, centre);
            }

            // Kept alongside the cells rather than instead of them. Nothing confines by it any
            // more, but the wander sampler still wants one height to sample at and the trip home
            // still wants a point to call the middle of the area.
            var half = cellSize * 0.5f;
            home = new Bounds();
            home.SetMinMax(
                new Vector3(min.x - half, min.y, min.z - half),
                new Vector3(max.x + half, max.y, max.z + half));

            // The floor reference goes with it. Awake reads it once, but a re-enable would
            // otherwise put the old bounds straight back over the area just handed over.
            homeFloor = null;

            // Whatever it was walking to was chosen inside the old area and may well be outside
            // this one. Re-picked now rather than left to expire, which for a long sit would be a
            // minute of the agent walking somewhere it is no longer allowed to be.
            if (!halted && state != State.Sitting && state != State.SittingDown)
            {
                BeginWandering();
            }
        }

        /// <summary>
        /// Which painted cell a world point falls in.
        /// </summary>
        /// <remarks>
        /// Floored rather than rounded, which is what makes the two directions agree. A saved area
        /// holds cell <em>centres</em>, and a Unity grid puts the centre of cell k at
        /// (k + 0.5) * size - so flooring a centre gives k, and flooring any point inside that
        /// cell gives the same k. Rounding would map the two halves of one cell to different keys.
        /// </remarks>
        Vector3Int CellKey(Vector3 point)
            => new Vector3Int(
                Mathf.FloorToInt(point.x / homeCellSize),
                Mathf.FloorToInt(point.z / homeCellSize),
                FloorOf != null ? FloorOf(point.y) : 0);

        /// <summary>
        /// Lets the agent use more of the walkable areas - the stairs and the elevator - both in
        /// its own area and on its way there.
        /// </summary>
        /// <remarks>
        /// Any way between floors the manager can use, an agent can: its area may be on another
        /// floor, and a walk back to it has to be able to cross. Added to whichever mask is in use
        /// at the moment as well, so it takes effect without waiting for the next errand.
        /// </remarks>
        public void AllowAreas(int mask)
        {
            homeAreaMask |= mask;
            travelAreaMask |= mask;

            if (agent != null)
            {
                agent.areaMask |= mask;
            }
        }

        /// <summary>True once this agent has been given a painted area to keep to.</summary>
        bool HasPaintedArea => homeCells.Count > 0 && homeCellSize > 0f;

        Chair PickFreeChair()
        {
            if (chairs.Length == 0)
            {
                return null;
            }

            // Start from a random index so the agent does not always favour the same chair.
            var offset = Random.Range(0, chairs.Length);
            for (var i = 0; i < chairs.Length; i++)
            {
                var chair = chairs[(offset + i) % chairs.Length];
                if (chair == null || chair.IsOccupied)
                {
                    continue;
                }

                // Reachability is checked before claiming, not after. Claiming first and releasing
                // on failure meant an agent could spend attempt after attempt on chairs that had
                // been moved off its floor, and never sit down at all.
                Vector3 approach;
                if (!TryFindApproachPoint(chair, true, out approach))
                {
                    continue;
                }

                if (chair.TryOccupy())
                {
                    return chair;
                }
            }

            return null;
        }

        void TickApproaching()
        {
            if (target == null)
            {
                BeginWandering();
                return;
            }

            if (Time.time >= giveUpAt)
            {
                GiveUpOnChair();
                return;
            }

            if (!agent.enabled || !agent.isOnNavMesh || agent.pathPending)
            {
                return;
            }

            if (agent.hasPath && agent.remainingDistance > agent.stoppingDistance)
            {
                return;
            }

            // Stopping is not arriving. A path that could only get part of the way, or none at
            // all, stops the agent short too - and a hop from there crossed the room. The hop
            // onto a seat is only ever from within reach of it.
            if (NavReach.Flat(transform.position, target.SeatPosition) > hopReach + agent.stoppingDistance + 0.1f)
            {
                GiveUpOnChair();
                return;
            }

            BeginSittingDown();
        }

        /// <summary>Lets go of the chair and goes back to what the agent was doing before it.</summary>
        void GiveUpOnChair()
        {
            ReleaseChair();

            if (summoned)
            {
                BeginReturningHome();
            }
            else
            {
                BeginWandering();
            }
        }

        // ---- Sitting ------------------------------------------------------------------

        void BeginSittingDown()
        {
            // The seat is off the NavMesh, so the agent has to let go of the transform first or it
            // would drag the capsule straight back down to the floor.
            standPoint = transform.position;
            agent.enabled = false;

            BeginGlide(
                target.SeatPosition + Vector3.up * halfHeight,
                target.SeatRotation,
                State.SittingDown);
        }

        void TickSitting()
        {
            if (Time.time < sitEndsAt)
            {
                return;
            }

            // Cleared as the agent leaves, rather than when the chair is released after the hop,
            // so the monitor goes dark the moment it stands up.
            if (target != null)
            {
                target.SetInUse(false);
            }

            BeginGlide(standPoint, transform.rotation, State.StandingUp);
        }

        void BeginGlide(Vector3 toPosition, Quaternion toRotation, State glideState)
        {
            glideFromPos = transform.position;
            glideFromRot = transform.rotation;
            glideToPos = toPosition;
            glideToRot = toRotation;
            glideStartedAt = Time.time;
            state = glideState;
        }

        void TickGliding(State next)
        {
            var duration = Mathf.Max(0.01f, jumpSeconds);
            var k = Mathf.Clamp01((Time.time - glideStartedAt) / duration);

            // Constant travel along the line with a parabola laid over it, so it reads as a hop
            // rather than a slide. The arc term is zero at both ends and peaks halfway across.
            var pos = Vector3.Lerp(glideFromPos, glideToPos, k);
            pos.y += jumpHeight * 4f * k * (1f - k);
            transform.position = pos;

            // Turn to face the seat early in the hop so the agent lands already oriented.
            transform.rotation = Quaternion.Slerp(glideFromRot, glideToRot, Mathf.SmoothStep(0f, 1f, k));

            if (k < 1f)
            {
                return;
            }

            if (next == State.Sitting)
            {
                // Landed on the seat, so the desk is now genuinely in use.
                if (target != null)
                {
                    target.SetInUse(true);
                }

                // A summons that arrived mid-hop stands the agent straight back up, and so does being
                // told to stay on its feet.
                sitEndsAt = pendingSummon != null || staysOnFeet
                    ? 0f
                    : Time.time + Random.Range(sitSeconds.x, sitSeconds.y);
                state = State.Sitting;
                return;
            }

            // Back on the floor: hand the transform to the NavMeshAgent again.
            ReleaseChair();
            agent.enabled = true;
            agent.Warp(transform.position);

            if (pendingSummon != null)
            {
                var chair = pendingSummon;
                pendingSummon = null;
                BeginSummon(chair);
            }
            else if (summoned)
            {
                BeginReturningHome();
            }
            else
            {
                BeginWandering();
            }
        }

        // ---- Going home ---------------------------------------------------------------

        void BeginReturningHome()
        {
            ReleaseChair();
            summoned = true;                    // keep Outside available for the walk back
            agent.areaMask = travelAreaMask;

            Vector3 destination;
            if (!TryPickFloorPoint(out destination))
            {
                // No walk home from here. Hop out if it is shut in; otherwise head for the middle
                // of the area as before, but come back to the question in a couple of seconds
                // rather than on the very next frame.
                if (TryEscape())
                {
                    return;
                }

                destination = new Vector3(home.center.x, home.max.y, home.center.z);
                retryHomeAt = Time.time + 2f;
            }

            agent.SetDestination(destination);
            giveUpAt = Time.time + errandTimeout;
            state = State.ReturningHome;
        }

        void TickReturningHome()
        {
            if (!agent.enabled || !agent.isOnNavMesh || agent.pathPending)
            {
                return;
            }

            if (Time.time < retryHomeAt)
            {
                return;
            }

            var arrived = !agent.hasPath || agent.remainingDistance <= agent.stoppingDistance;
            if (!arrived && Time.time < giveUpAt)
            {
                return;
            }

            // Only re-confine once actually back on the home floor, otherwise dropping Outside
            // from the mask would strand the agent on ground it is not allowed to path over.
            if (IsHome(transform.position))
            {
                summoned = false;
                agent.areaMask = homeAreaMask;
                BeginWandering();
                return;
            }

            BeginReturningHome();   // still outside: pick another home point and keep going
        }

        void ReleaseChair()
        {
            if (target != null)
            {
                target.Release();
                target = null;
            }
        }

        /// <summary>
        /// Whether a point is inside this agent's area: the footprint, on the area's floor. Only
        /// the floor is read from the height - the NavMesh sits slightly above the floor's own
        /// bounds, so anything finer would be wrong.
        /// </summary>
        /// <remarks>
        /// The painted cells decide it whenever there are any, and only otherwise does the
        /// rectangle come into it. That order is the whole fix: the rectangle around a painted area
        /// takes in floor that was never painted — an L-shaped painting has a quarter of its
        /// bounding box outside it — and treating that as home is what let agents drift out of the
        /// region the player drew.
        /// </remarks>
        bool IsHome(Vector3 point)
        {
            if (HasPaintedArea)
            {
                return homeCells.Contains(CellKey(point));
            }

            return point.x >= home.min.x && point.x <= home.max.x
                && point.z >= home.min.z && point.z <= home.max.z;
        }
    }
}
