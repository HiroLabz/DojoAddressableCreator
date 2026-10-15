using UnityEngine;
using UnityEngine.AI;

namespace Dojo.Game.InGame.Agents
{
    /// <summary>
    /// Tells the agent's <see cref="Animator"/> how fast it is actually moving, so the walk
    /// animation plays while it walks and the idle plays while it stands.
    /// </summary>
    /// <remarks>
    /// The value is read from <see cref="NavMeshAgent.velocity"/> rather than pushed by whatever
    /// asked the agent to move. <see cref="AgentRoutine"/> is not the only thing that moves one —
    /// a path can be blocked, a destination can be reached early, and an agent shoved aside by
    /// avoidance is moving without having been told to. Reading the velocity is the only account
    /// that is true in all of those cases, and it costs one field read a frame.
    /// <para>
    /// The parameter is normalised against <see cref="NavMeshAgent.speed"/> rather than written in
    /// metres per second. A normalised value means the controller's threshold keeps meaning the
    /// same thing after somebody retunes the agent's speed, and it is the form a blend tree would
    /// want if the walk is ever joined by a run.
    /// </para>
    /// <para>
    /// The component sits on the root, beside the <see cref="NavMeshAgent"/>, while the
    /// <see cref="Animator"/> lives on the model child — so the animator is found by search rather
    /// than required here. That keeps the art swappable: a different model child changes nothing
    /// about this component.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class AgentAnimationDriver : MonoBehaviour
    {
        [Tooltip("Float parameter on the controller, 0 while standing and 1 at full speed. Must " +
                 "match the parameter name in the Animator Controller.")]
        [SerializeField] string speedParameter = "Speed";

        [Tooltip("Seconds the parameter takes to catch up with a change in speed. Smooths the " +
                 "step in and out of the walk so a single blocked frame does not flicker the " +
                 "animation. Zero follows the velocity exactly.")]
        [SerializeField] float damping = 0.1f;

        [Tooltip("Float parameter driving the walk state's speed multiplier, so the cadence " +
                 "tracks how fast the agent is really moving. Must match the parameter the Walk " +
                 "state's Speed field is set to.")]
        [SerializeField] string walkRateParameter = "WalkRate";

        [Tooltip("The movement speed the walk clip looks right at, played at normal rate. Moving " +
                 "faster than this speeds the cycle up, slower slows it down. Tune by eye.")]
        [SerializeField] float referenceSpeed = 0.5f;

        [Tooltip("Limits on the cycle rate, so a sprint does not become a blur and a crawl does " +
                 "not freeze mid-step.")]
        [SerializeField] Vector2 rateLimits = new Vector2(0.4f, 2.5f);

        NavMeshAgent agent;
        Animator animator;
        int speedHash;
        int walkRateHash;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            animator = GetComponentInChildren<Animator>(true);
            speedHash = Animator.StringToHash(speedParameter);
            walkRateHash = Animator.StringToHash(walkRateParameter);

            if (animator == null)
            {
                Debug.LogWarning("[Agent] No Animator under '" + name + "', so nothing will be "
                    + "animated. Add the model child back, or remove this component.", this);
            }
        }

        void Update()
        {
            if (animator == null)
            {
                return;
            }

            // A disabled or off-mesh agent has no meaningful velocity — while sitting or mid-hop
            // the NavMeshAgent is switched off, and reading it then would leave whatever value it
            // held when it stopped, freezing the agent in a walk it is not doing.
            var moving = agent.enabled && agent.isOnNavMesh;
            var worldSpeed = moving ? agent.velocity.magnitude : 0f;
            var speed = moving && agent.speed > 0f
                ? Mathf.Clamp01(worldSpeed / agent.speed)
                : 0f;

            animator.SetFloat(speedHash, speed, damping, Time.deltaTime);

            // The walk clip has no forward stride to plant a foot with — it is a waddle in place —
            // so the feet cannot be made to stick. Matching the cadence to the real speed is the
            // closest thing available: the robot steps faster when it travels faster, which reads
            // as intent rather than as sliding.
            var rate = referenceSpeed > 0f
                ? Mathf.Clamp(worldSpeed / referenceSpeed, rateLimits.x, rateLimits.y)
                : 1f;

            animator.SetFloat(walkRateHash, rate);
        }
    }
}
