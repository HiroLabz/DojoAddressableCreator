using Dojo.Game.InGame.Agents;
using Dojo.Game.InGame.Controllers;
using Dojo.Game.Systems;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using VContainer;

namespace Dojo.Game.InGame.Effects
{
    /// <summary>
    /// Shows the player what they clicked in the room. An agent lights up; anything else gets a
    /// halo lying on it: flat on the floor, on top of a table, against a wall.
    /// </summary>
    /// <remarks>
    /// Play phase only. In Edit a click picks furniture up and a drag puts it down, and feedback
    /// on every one of those would be noise over the thing the player is actually looking at.
    /// <para>
    /// One place decides between the two, so a click never gets both. The agent test is the
    /// owner's own - an agent anywhere along the ray wins over the scenery in front of it - so the
    /// glow always marks the agent he is about to walk to.
    /// </para>
    /// <para>
    /// The halo finds its own surface rather than asking the owner where he was sent. It answers
    /// "what did I click", which is a question about the room; the owner answers "where can I
    /// walk", which snaps to the nearest floor. A click on a table top should glow on the table,
    /// not on the floor beside it.
    /// </para>
    /// </remarks>
    public sealed class ClickFeedback : MonoBehaviour
    {
        [Tooltip("The halo to play. A ParticleSystem that plays on awake and destroys itself.")]
        [SerializeField] ParticleSystem haloPrefab;

        [Tooltip("Colour a clicked agent lights up in. Light blue (0.6, 0.85, 1) also reads well.")]
        [SerializeField] Color agentGlow = Color.white;

        [Tooltip("Camera the click is cast from. Defaults to Camera.main.")]
        [SerializeField] Camera view;

        [Tooltip("Which colliders count as something to put a halo on.")]
        [SerializeField] LayerMask clickable = ~0;

        [Tooltip("How far the click ray reaches. Matches the owner's, for the same isometric camera.")]
        [SerializeField] float rayDistance = 500f;

        [Tooltip("How far off the surface the halo floats, so it never flickers into it.")]
        [SerializeField] float lift = 0.02f;

        IGamePhase phase;

        /// <summary>
        /// How long the halo plays, in seconds: until its last ring has faded.
        /// </summary>
        /// <remarks>
        /// The owner waits this long before carrying out a click, so the player sees what they
        /// clicked before anything moves. Read off the halo itself, so changing the effect changes
        /// the wait with it.
        /// </remarks>
        public float HaloSeconds { get; private set; }

        [Inject]
        public void Construct(IGamePhase phase)
        {
            this.phase = phase;
        }

        void Awake()
        {
            if (view == null)
            {
                view = Camera.main;
            }

            HaloSeconds = haloPrefab != null ? PlayLength(haloPrefab) : 0f;
        }

        /// <summary>
        /// How long a one-shot effect plays: until its last particle has died, across every system
        /// in it.
        /// </summary>
        /// <remarks>
        /// The system's own duration is only how long it emits for. A ring burst near its end still
        /// lives out its lifetime, and the effect is not over - nor destroyed - until it has.
        /// </remarks>
        public static float PlayLength(ParticleSystem effect)
        {
            var longest = 0f;

            foreach (var system in effect.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = system.main;
                var emission = system.emission;
                var lifetime = main.startLifetime.constantMax;
                var end = main.duration;

                for (var i = 0; i < emission.burstCount; i++)
                {
                    end = Mathf.Max(end, emission.GetBurst(i).time + lifetime);
                }

                if (emission.rateOverTime.constantMax > 0f)
                {
                    end = Mathf.Max(end, main.duration + lifetime);
                }

                end += main.startDelay.constantMax;
                longest = Mathf.Max(longest, end / Mathf.Max(0.01f, main.simulationSpeed));
            }

            return longest;
        }

        void Update()
        {
            if (phase != null && phase.IsEdit)
            {
                return;
            }

            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame || view == null)
            {
                return;
            }

            // A button press is not a click in the room.
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }

            var ray = view.ScreenPointToRay(mouse.position.ReadValue());

            var agent = TryFindAgent(ray, rayDistance, clickable);
            if (agent != null)
            {
                RendererGlow.Pulse(agent.gameObject, agentGlow);
                return;
            }

            if (haloPrefab == null)
            {
                return;
            }

            RaycastHit hit;
            if (!TryFindSurface(ray, rayDistance, clickable, out hit))
            {
                return;   // empty space: nothing to glow on
            }

            var pose = PoseOn(hit.point, hit.normal, lift);

            // Under this object so the hierarchy has one place they come and go from. The prefab
            // destroys itself when it has finished, so nothing has to keep count.
            Instantiate(haloPrefab, pose.position, pose.rotation, transform);
        }

        /// <summary>
        /// The nearest agent anywhere along <paramref name="ray"/>, or null.
        /// </summary>
        /// <remarks>
        /// Triggers included and nothing in front allowed to hide one, both as the owner does it:
        /// his click has to reach an agent sitting inside a chair's collider, and this has to agree
        /// with him about which agent was clicked.
        /// </remarks>
        public static AgentRoutine TryFindAgent(Ray ray, float distance, int mask)
        {
            AgentRoutine nearest = null;
            var nearestDistance = float.MaxValue;

            var hits = Physics.RaycastAll(ray, distance, mask, QueryTriggerInteraction.Collide);

            for (var i = 0; i < hits.Length; i++)
            {
                if (hits[i].distance >= nearestDistance)
                {
                    continue;
                }

                var agent = hits[i].transform.GetComponentInParent<AgentRoutine>();

                if (agent != null)
                {
                    nearest = agent;
                    nearestDistance = hits[i].distance;
                }
            }

            return nearest;
        }

        /// <summary>
        /// The nearest solid thing along <paramref name="ray"/>, looking through the owner and
        /// through triggers.
        /// </summary>
        /// <remarks>
        /// The owner is looked through because the halo marks where he is being sent, and he is
        /// never sent to himself. That means both the model and the placement box he was built in,
        /// which is why the stamp is checked as well as the controller. Triggers are invisible
        /// volumes, not surfaces.
        /// </remarks>
        public static bool TryFindSurface(Ray ray, float distance, int mask, out RaycastHit surface)
        {
            surface = default;
            var found = false;
            var nearest = float.MaxValue;

            var hits = Physics.RaycastAll(ray, distance, mask, QueryTriggerInteraction.Ignore);

            for (var i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];

                if (hit.distance >= nearest)
                {
                    continue;
                }

                if (hit.transform.GetComponentInParent<ManagerController>() != null
                    || hit.transform.GetComponentInParent<PlacedManager>() != null)
                {
                    continue;
                }

                nearest = hit.distance;
                surface = hit;
                found = true;
            }

            return found;
        }

        /// <summary>
        /// Where a halo on a surface sits and which way it faces.
        /// </summary>
        /// <remarks>
        /// The halo's ring lies across its own forward axis, so facing it along the surface normal
        /// lays it flat on a floor and flat against a wall alike.
        /// </remarks>
        public static Pose PoseOn(Vector3 point, Vector3 normal, float lift)
        {
            var up = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;
            return new Pose(point + up * lift, Quaternion.FromToRotation(Vector3.forward, up));
        }
    }
}
