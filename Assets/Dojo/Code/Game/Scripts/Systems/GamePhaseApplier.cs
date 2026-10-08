using System;
using Dojo.Framework.Events;
using Dojo.Game.Events;
using Dojo.Game.InGame.Agents;
using Dojo.Game.InGame.Controllers;
using Dojo.Game.Placement;
using UnityEngine;
using UnityEngine.AI;
using VContainer.Unity;

namespace Dojo.Game.Systems
{
    /// <summary>
    /// Turns a phase change into the things that actually happen.
    /// </summary>
    /// <remarks>
    /// One subscriber rather than a dozen. Every agent, the manager and the placement controller
    /// could each listen for themselves, but then the order they react in is whatever order they
    /// happened to subscribe in — and the phase would be half-applied for as long as that takes.
    /// Here the whole transition is one method, and <see cref="IGamePhase.Changed"/> fires after it.
    /// <para>
    /// The scene is searched on each change rather than cached. Agents are spawned and destroyed as
    /// worlds load and the player places them, so a list captured at startup is wrong by the first
    /// time anyone opens the drawer. Phase changes happen when a human clicks a panel, so the cost
    /// of a search is irrelevant.
    /// </para>
    /// </remarks>
    public sealed class GamePhaseApplier : IStartable, IDisposable
    {
        const string LogPrefix = "[Phase]";

        /// <summary>This applier's key in the manager's suppression set.</summary>
        const string ManagerSuppressReason = "phase";

        /// <summary>
        /// How far off the navmesh counts as "still on a floor".
        /// </summary>
        /// <remarks>
        /// Generous enough to forgive the gap between a character's pivot and the surface, tight
        /// enough that somebody standing where a floor tile used to be does not pass.
        /// </remarks>
        const float OnFloorTolerance = 0.4f;

        /// <summary>How far to look for somewhere to put a character who is off the floor.</summary>
        const float RelocateSearchRadius = 15f;

        readonly IGamePhase phase;

        // Told when a placement box is moved to its person, so the save records where they stand
        // now. Moving a box is a change to the world like any drop, and the auto save only writes
        // what it has been told about.
        readonly IEventHub hub;

        public GamePhaseApplier(IGamePhase phase, IEventHub hub)
        {
            this.phase = phase;
            this.hub = hub;
        }

        public void Start()
        {
            phase.Changed += Apply;

            // Applied once at startup too, so a scene that loads into Play is actually in Play
            // rather than merely believing it is.
            Apply(phase.Current);
        }

        public void Dispose()
        {
            if (phase != null)
            {
                phase.Changed -= Apply;
            }
        }

        void Apply(GamePhase current)
        {
            var editing = current == GamePhase.Edit;

            var agents = UnityEngine.Object.FindObjectsByType<AgentRoutine>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            var gathered = 0;

            foreach (var agent in agents)
            {
                if (agent == null)
                {
                    continue;
                }

                ApplyTo(agent, current);

                if (editing && Gather(agent))
                {
                    gathered++;
                }
            }

            var managers = UnityEngine.Object.FindObjectsByType<ManagerController>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            foreach (var manager in managers)
            {
                if (manager == null)
                {
                    continue;
                }

                ApplyTo(manager, current);

                if (editing && Gather(manager))
                {
                    gathered++;
                }
            }

            if (gathered > 0)
            {
                // The placement probes are physics queries, and the next drag may come before the
                // simulation next syncs the moved colliders.
                Physics.SyncTransforms();

                // Only when somebody had actually moved, so opening the inventory on a room where
                // nobody has walked anywhere writes nothing.
                if (hub != null)
                {
                    hub.Publish(new WorldChanged("people gathered"));
                }
            }

            // ── The camera ────────────────────────────────────────────────────────────────
            // Arranging a room means moving around it, so panning stays. Everything else the
            // camera does by itself — zooming, swinging, and easing back onto the manager a few
            // seconds after you let go — is the camera taking the shot away from somebody who is
            // using it to work.
            var rigs = UnityEngine.Object.FindObjectsByType<CameraRig>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            foreach (var rig in rigs)
            {
                if (rig != null)
                {
                    rig.PanOnly = editing;
                }
            }

            var cameras = UnityEngine.Object.FindObjectsByType<MainCinemachineCamera>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            foreach (var camera in cameras)
            {
                if (camera != null)
                {
                    camera.Suspended = editing;
                }
            }

            Debug.Log($"{LogPrefix} applied {current}: {agents.Length} agent(s), {managers.Length} manager(s)"
                + (editing ? $" halted and the owner is deaf to clicks, {gathered} placement box(es) gathered" : " released")
                + $"; camera {(editing ? "pan-only, follow suspended" : "restored")}"
                + $" ({rigs.Length} rig(s), {cameras.Length} camera(s)).");

            if (!editing)
            {
                // Only on the way back into Play. Rearranging is exactly when the floor under
                // somebody gets picked up, so the check belongs after it, not during.
                RelocateStrandedCharacters(agents, managers);
            }
        }

        /// <summary>
        /// Gives one manager what <paramref name="current"/> asks of him.
        /// </summary>
        /// <remarks>
        /// Public because the phase is not only applied when it changes. A manager put down in the
        /// middle of Edit arrives after the freeze was handed out, and the assembler that brings
        /// him to life gives him the same treatment through here rather than a copy of it - the
        /// copy would need the suppression key, and two holders of one key is how a release by one
        /// lifts the other's hold.
        /// <para>
        /// Both parts, and they are not the same thing: suppression stops him answering a new
        /// click, Halt stops a walk already under way. In Play both are undone, which is a no-op
        /// for a manager who was never frozen.
        /// </para>
        /// </remarks>
        public static void ApplyTo(ManagerController manager, GamePhase current)
        {
            if (manager == null)
            {
                return;
            }

            var editing = current == GamePhase.Edit;

            manager.SuppressClickFor(ManagerSuppressReason, editing);

            if (editing)
            {
                manager.Halt();
            }
            else
            {
                manager.Resume();
            }
        }

        /// <summary>Gives one agent what <paramref name="current"/> asks of it.</summary>
        /// <remarks>Public for the same reason as the manager's overload.</remarks>
        public static void ApplyTo(AgentRoutine agent, GamePhase current)
        {
            if (agent == null)
            {
                return;
            }

            if (current == GamePhase.Edit)
            {
                agent.Halt();
            }
            else
            {
                agent.Resume();
            }
        }

        /// <summary>
        /// Brings the placement box a character was put down in back to where they now stand.
        /// </summary>
        /// <remarks>
        /// On the way into Edit, once they have stopped, because Edit is when the box is asked
        /// about: it is what the player picks the character up by, what a dragged chair is tested
        /// against, and what a save records. See <see cref="FurniturePiece.GatherToWalker"/>.
        /// </remarks>
        static bool Gather(Component character)
        {
            var piece = character.GetComponentInParent<FurniturePiece>();
            return piece != null && piece.GatherToWalker();
        }

        /// <summary>
        /// Puts anyone left standing off the floor back onto it.
        /// </summary>
        /// <remarks>
        /// The navmesh is the definition of "a floor" here, not the floor pieces themselves. It is
        /// the surface that decides where anybody can actually walk, so a point sampled from it is
        /// guaranteed reachable — where a nearest-floor-piece search can return the middle of a
        /// tile that has since been built over.
        /// </remarks>
        static void RelocateStrandedCharacters(AgentRoutine[] agents, ManagerController[] managers)
        {
            foreach (var agent in agents)
            {
                if (agent != null)
                {
                    Rescue(agent.gameObject, "agent");
                }
            }

            foreach (var manager in managers)
            {
                if (manager != null)
                {
                    Rescue(manager.gameObject, "owner");
                }
            }
        }

        static void Rescue(GameObject character, string what)
        {
            var at = character.transform.position;
            var navAgent = character.GetComponent<NavMeshAgent>();

            // Their own floor when they have one. There is one per model size, and "on a floor"
            // has to mean the one they walk - a thinner character's reaches closer to the walls.
            var filter = new NavMeshQueryFilter
            {
                agentTypeID = navAgent != null ? navAgent.agentTypeID : 0,
                areaMask = NavMesh.AllAreas,
            };

            if (navAgent != null
                ? NavMesh.SamplePosition(at, out _, OnFloorTolerance, filter)
                : NavMesh.SamplePosition(at, out _, OnFloorTolerance, NavMesh.AllAreas))
            {
                return;   // still standing on something walkable
            }

            NavMeshHit nearest;

            var found = navAgent != null
                ? NavMesh.SamplePosition(at, out nearest, RelocateSearchRadius, filter)
                : NavMesh.SamplePosition(at, out nearest, RelocateSearchRadius, NavMesh.AllAreas);

            if (!found)
            {
                // Nothing within reach. Said out loud and left alone: moving them somewhere
                // arbitrary would be worse than leaving them where the player can see the problem.
                Debug.LogWarning($"{LogPrefix} {what} '{character.name}' is off the floor and no "
                    + $"walkable spot was found within {RelocateSearchRadius}m; left where it stands.");
                return;
            }

            if (navAgent != null && navAgent.enabled)
            {
                // Warp rather than SetDestination: there is no path from off-mesh to on-mesh, so
                // asking them to walk there would simply fail.
                navAgent.Warp(nearest.position);
            }
            else
            {
                character.transform.position = nearest.position;
            }

            Debug.Log($"{LogPrefix} {what} '{character.name}' was off the floor; moved from "
                + at.ToString("F2") + " to " + nearest.position.ToString("F2") + ".");
        }
    }
}
