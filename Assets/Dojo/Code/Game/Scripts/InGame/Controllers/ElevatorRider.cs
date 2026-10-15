using System.Collections;
using Dojo.Game.Placement;
using UnityEngine;
using UnityEngine.AI;

namespace Dojo.Game.InGame.Controllers
{
    /// <summary>
    /// Turns the manager reaching an elevator link into a ride: the doors open, he steps in, the
    /// doors close; the floor he is leaving fades away; the view travels to the floor he was sent
    /// to; then the doors there open and he steps out.
    /// </summary>
    /// <remarks>
    /// On the same object as his <see cref="NavMeshAgent"/>, which no longer crosses links by itself
    /// - left to that, he would glide through the shaft wall in a straight line. A link that is not
    /// an elevator's is crossed at once, as it would have been.
    /// <para>
    /// Arriving on another floor moves him onto it: his placement box goes under that floor's
    /// container and to its height, so he is saved there, hidden with it, and clicked with it.
    /// </para>
    /// <para>
    /// Added by <c>ManagerAssembler</c>, which also hands over the floors.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ElevatorRider : MonoBehaviour
    {
        const float DoorSeconds = 0.35f;
        const float StepSeconds = 0.5f;
        /// <summary>
        /// The least time he is out of sight between the doors closing and the other doors opening
        /// - the same however many floors apart, as a portal would be. Usually the view's journey
        /// to his new floor takes longer, and that is what is waited for (<see cref="HoldArrival"/>).
        /// </summary>
        const float PortalSeconds = 0.5f;

        /// <summary>The world's floors, for moving him onto the one he arrives at.</summary>
        public Storeys Floors { get; set; }

        /// <summary>
        /// While this says true, a ride's arrival waits: the doors at the far end stay shut. Null
        /// waits for nothing beyond the shortest ride.
        /// </summary>
        /// <remarks>
        /// The floor switcher sets it to "the view is still travelling to his new floor", so the
        /// doors open once the camera has got there to watch him step out.
        /// </remarks>
        public System.Func<bool> HoldArrival { get; set; }

        /// <summary>
        /// Told once the doors have shut behind him, before he is moved: the floor he is leaving,
        /// then the floor he is going to.
        /// </summary>
        /// <remarks>
        /// The floor switcher starts the floor he is leaving fading away here.
        /// </remarks>
        public System.Action<int, int> Shut { get; set; }

        /// <summary>
        /// While this says true he stays in the shut car on the floor he is leaving: he is moved to
        /// the other floor only once it is false. Null holds nothing.
        /// </summary>
        /// <remarks>
        /// The floor switcher sets it to "the floor he is leaving is still fading away", so the
        /// camera sets off for the new floor only once the old one has gone.
        /// </remarks>
        public System.Func<bool> HoldDeparture { get; set; }

        /// <summary>True while a ride is under way.</summary>
        public bool IsRiding { get; private set; }

        NavMeshAgent agent;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
        }

        void OnEnable()
        {
            if (agent != null)
            {
                agent.autoTraverseOffMeshLink = false;
            }
        }

        void Update()
        {
            if (IsRiding || agent == null || !agent.enabled || !agent.isOnNavMesh)
            {
                return;
            }

            KeepOnHisFloor();

            if (!agent.isOnOffMeshLink)
            {
                return;
            }

            var data = agent.currentOffMeshLinkData;
            var owner = agent.navMeshOwner as Component;
            var link = owner != null ? owner.GetComponent<ElevatorLink>() : null;

            if (link == null)
            {
                agent.CompleteOffMeshLink();
                return;
            }

            StartCoroutine(Ride(link, data.startPos, data.endPos));
        }

        IEnumerator Ride(ElevatorLink link, Vector3 from, Vector3 to)
        {
            IsRiding = true;

            int fromFloor, toFloor;
            var boarding = link.StopAt(from, out fromFloor);
            var leaving = link.StopAt(to, out toFloor);

            agent.updatePosition = false;
            agent.updateRotation = false;

            var doorsIn = ElevatorDoors.On(boarding);
            var doorsOut = ElevatorDoors.On(leaving);

            // In: doors open, step into the car, doors shut behind.
            yield return Doors(doorsIn, 0f, 1f);
            yield return Step(Car(boarding));
            yield return Doors(doorsIn, 1f, 0f);

            // Shut in. The floor he is leaving fades away first, with him in it, while the view
            // stays where it is.
            if (Shut != null)
            {
                Shut(fromFloor, toFloor);
            }

            while (HoldDeparture != null && HoldDeparture())
            {
                yield return null;
            }

            // Out of sight, so nobody sees the move: now standing in the other car, on the other
            // floor. The view sets off after him from here.
            Arrive(leaving, toFloor);

            var shut = Time.time;

            // The view hears of the move at the end of the frame it was made in; then the doors
            // there stay shut until it has arrived, so he steps out in front of the camera.
            yield return null;

            while (HoldArrival != null && HoldArrival())
            {
                yield return null;
            }

            var left = PortalSeconds - (Time.time - shut);

            if (left > 0f)
            {
                yield return new WaitForSeconds(left);
            }

            // Out: doors open, step out, and the walk carries on from the far end of the link.
            yield return Doors(doorsOut, 0f, 1f);
            yield return Step(to);

            agent.CompleteOffMeshLink();
            agent.nextPosition = transform.position;
            agent.updatePosition = true;
            agent.updateRotation = true;

            yield return Doors(doorsOut, 1f, 0f);

            IsRiding = false;
        }

        /// <summary>
        /// Moves him onto the floor he has walked to, when he has come up or down a staircase: his
        /// box goes under that floor and to its height, and he does not move at all.
        /// </summary>
        /// <remarks>
        /// The elevator does this as part of the ride; stairs are walked, so nothing else would.
        /// Left under the floor he started on, he would be saved on it, at a height below its own.
        /// </remarks>
        void KeepOnHisFloor()
        {
            if (Floors == null)
            {
                return;
            }

            var floor = Mathf.Clamp(Floors.StoreyAt(transform.position.y), 0, Mathf.Max(0, Floors.Count - 1));
            var box = GetComponentInParent<FurniturePiece>();
            var holder = box != null ? box.transform : transform;

            if (Storeys.StoreyOf(holder) == floor)
            {
                return;
            }

            var standing = transform.position;

            holder.SetParent(Floors.Floor(floor), true);

            if (holder != transform)
            {
                // The box goes to the new floor's own level - not his height, which on a staircase
                // is somewhere between the two - and he is put back exactly where he stood, so the
                // walk carries on as if nothing had happened.
                holder.position = new Vector3(holder.position.x, Floors.BaseOf(floor), holder.position.z);
                transform.position = standing;
            }

            Floors.Refresh();
        }

        /// <summary>The middle of a stop's car, on its floor.</summary>
        static Vector3 Car(FurniturePiece stop)
            => stop != null ? stop.transform.position : Vector3.zero;

        /// <summary>
        /// Puts him in the arriving car, on its floor: his box under that floor at its height, and
        /// him at the car's middle.
        /// </summary>
        void Arrive(FurniturePiece stop, int floor)
        {
            var car = Car(stop);
            var box = GetComponentInParent<FurniturePiece>();
            var holder = box != null ? box.transform : transform;

            if (Floors != null)
            {
                // The box moves first, and him with it; then he is put in the car exactly. When the
                // model carries its own placement the box is him, and the one move does both.
                holder.SetParent(Floors.Floor(floor), true);

                var at = holder.position;
                holder.position = new Vector3(at.x, car.y, at.z);
            }

            transform.position = car;

            if (Floors != null)
            {
                Floors.Refresh();
            }
        }

        IEnumerator Doors(ElevatorDoors doors, float from, float to)
        {
            if (doors == null)
            {
                yield break;
            }

            for (var t = 0f; t < DoorSeconds; t += Time.deltaTime)
            {
                doors.SetOpen(Mathf.Lerp(from, to, t / DoorSeconds));
                yield return null;
            }

            doors.SetOpen(to);
        }

        IEnumerator Step(Vector3 target)
        {
            var start = transform.position;
            var flat = new Vector3(target.x - start.x, 0f, target.z - start.z);

            if (flat.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
            }

            for (var t = 0f; t < StepSeconds; t += Time.deltaTime)
            {
                transform.position = Vector3.Lerp(start, target, t / StepSeconds);
                yield return null;
            }

            transform.position = target;
        }
    }
}
