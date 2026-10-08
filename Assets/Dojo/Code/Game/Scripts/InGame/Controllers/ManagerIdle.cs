using System.Collections.Generic;
using Dojo.Framework.World;
using Dojo.Game.Placement;
using Dojo.Game.Systems;
using UnityEngine;
using UnityEngine.AI;

namespace Dojo.Game.InGame.Controllers
{
    /// <summary>
    /// What the manager does when nobody is telling him anything: after a while standing still he
    /// walks to the middle of his block area if he has one and is not in it, and with none,
    /// somewhere on his floor.
    /// </summary>
    /// <remarks>
    /// "Standing still" is narrow on purpose: not walking, not sitting or hopping onto a seat, not
    /// with an agent (the chat is open while he is), not in the elevator or waiting at one while a
    /// floor is picked, and not while the room is being arranged. Anything else and the clock
    /// starts again.
    /// <para>
    /// These walks are his own, not orders: they go through <see cref="ManagerController.Stroll"/>,
    /// which is not announced, so there is no halo and the camera is not pulled back to him. If the
    /// camera is following him it follows the walk, as it follows any.
    /// </para>
    /// <para>
    /// Added by <c>ManagerAssembler</c>, which also hands over the areas and the floors.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ManagerIdle : MonoBehaviour
    {
        /// <summary>How long he stands still before he goes somewhere of his own accord.</summary>
        public const float IdleSeconds = 10f;

        /// <summary>How many spots are tried before a walk is given up on until the next time.</summary>
        const int Tries = 24;

        public BlockAreas Areas { get; set; }
        public Storeys Floors { get; set; }
        public IGamePhase Phase { get; set; }

        /// <summary>Whose area to go back to: the manager's credentials.</summary>
        public int CredentialId { get; set; }

        ManagerController controller;
        NavMeshAgent agent;
        ElevatorRider rider;
        float stillSince;

        void Awake()
        {
            controller = GetComponent<ManagerController>();
            agent = GetComponent<NavMeshAgent>();
            rider = GetComponent<ElevatorRider>();
            stillSince = Time.time;
        }

        void Update()
        {
            if (!StandingStill())
            {
                stillSince = Time.time;
                return;
            }

            if (Time.time - stillSince < IdleSeconds)
            {
                return;
            }

            // Counted again from now whether or not a walk is found, so a manager with nowhere to
            // go is asked every ten seconds rather than every frame.
            stillSince = Time.time;

            var area = Areas != null ? Areas.AreaFor(SnapshotRole.Manager, CredentialId) : null;

            if (area != null)
            {
                if (!IsIn(area))
                {
                    GoBackTo(area);
                }

                return;
            }

            WanderHisFloor();
        }

        bool StandingStill()
        {
            if (controller == null || agent == null || !controller.enabled)
            {
                return false;
            }

            if (Phase != null && Phase.IsEdit)
            {
                return false;
            }

            if (rider == null)
            {
                rider = GetComponent<ElevatorRider>();
            }

            if (controller.IsHalted || controller.IsSeated || controller.IsHopping
                || controller.TargetAgent != null || controller.WaitingAtElevator
                || (rider != null && rider.IsRiding))
            {
                return false;
            }

            if (!agent.enabled || !agent.isOnNavMesh || agent.pathPending)
            {
                return false;
            }

            var walking = agent.hasPath && agent.remainingDistance > agent.stoppingDistance + 0.05f;
            return !walking && agent.velocity.sqrMagnitude < 0.01f;
        }

        /// <summary>Whether he is standing in his area: on its floor, within one of its cells.</summary>
        bool IsIn(BlockArea area)
        {
            var here = transform.position;

            if (Floors != null && Floors.StoreyAt(here.y) != area.Floor)
            {
                return false;
            }

            var reach = area.CellSize * 0.75f;

            foreach (var cell in area.Cells)
            {
                if (Mathf.Abs(cell.x - here.x) <= reach && Mathf.Abs(cell.z - here.z) <= reach)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// To the middle of his area, on whichever floor it is - or, with something standing there,
        /// to the free spot nearest it that he can reach.
        /// </summary>
        void GoBackTo(BlockArea area)
        {
            controller.StrollToFirst(area.CentreFirst(), area.CellSize * 0.5f);
        }

        /// <summary>Somewhere random on the tiles of the floor he is on.</summary>
        void WanderHisFloor()
        {
            if (Floors == null)
            {
                return;
            }

            var floor = Floors.StoreyAt(transform.position.y);
            var tiles = new List<Bounds>();

            foreach (var piece in Floors.Floor(floor).GetComponentsInChildren<FurniturePiece>(true))
            {
                Bounds bounds;

                if (piece != null && piece.IsFloor && !piece.IsWalkway
                    && Storeys.StoreyOf(piece.transform) == floor && TryBounds(piece, out bounds))
                {
                    tiles.Add(bounds);
                }
            }

            if (tiles.Count == 0)
            {
                return;
            }

            for (var i = 0; i < Tries; i++)
            {
                var tile = tiles[Random.Range(0, tiles.Count)];
                var point = new Vector3(
                    Random.Range(tile.min.x + 0.3f, tile.max.x - 0.3f),
                    tile.max.y,
                    Random.Range(tile.min.z + 0.3f, tile.max.z - 0.3f));

                if (FlatSqr(point, transform.position) > 1f && controller.Stroll(point))
                {
                    return;
                }
            }
        }

        static float FlatSqr(Vector3 a, Vector3 b)
        {
            var dx = a.x - b.x;
            var dz = a.z - b.z;
            return dx * dx + dz * dz;
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
    }
}
