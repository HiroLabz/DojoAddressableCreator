using Dojo.Game.InGame.Controllers;
using UnityEngine;
using UnityEngine.AI;

namespace Dojo.Game.InGame.Agents
{
    /// <summary>
    /// Drives the manager's walking, jumping and sitting animations from what
    /// <see cref="ManagerController"/> is actually doing.
    /// </summary>
    /// <remarks>
    /// The manager moves in two entirely different ways, and only one of them is visible to the
    /// <see cref="NavMeshAgent"/>. Walking is navigation, so velocity describes it. The hop on and
    /// off a seat is not: it moves the transform directly and leaves the agent switched off, so
    /// velocity reads zero for exactly the moment the jump should play. Asking the controller is
    /// the only account that covers both — see <see cref="ManagerController.IsHopping"/>.
    /// <para>
    /// Sitting is a state rather than an event, so it is a bool the controller can sit in for as
    /// long as the manager stays on the chair. Jumping is an event with a fixed length, so it is a
    /// trigger fired on the rising edge and left to play itself out.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(ManagerController))]
    public sealed class ManagerAnimationDriver : MonoBehaviour
    {
        [Tooltip("Float parameter, 0 while standing still and 1 at full speed.")]
        [SerializeField] string speedParameter = "Speed";

        [Tooltip("Float parameter driving the Walking state's speed multiplier, so the step " +
                 "cadence tracks how fast the manager is really moving.")]
        [SerializeField] string walkRateParameter = "WalkRate";

        [Tooltip("Bool parameter held true for as long as the manager is on a chair.")]
        [SerializeField] string sittingParameter = "Sitting";

        [Tooltip("Trigger fired once when a hop on or off a seat begins.")]
        [SerializeField] string jumpParameter = "Jump";

        [Tooltip("The movement speed the walk clip looks right at, played at normal rate.")]
        [SerializeField] float referenceSpeed = 1f;

        [Tooltip("Limits on the walk cycle rate, so it neither blurs nor freezes mid-step.")]
        [SerializeField] Vector2 rateLimits = new Vector2(0.4f, 2.5f);

        [Tooltip("Seconds the speed parameter takes to catch up, so one blocked frame does not " +
                 "flicker the animation.")]
        [SerializeField] float damping = 0.1f;

        ManagerController manager;
        NavMeshAgent agent;
        Animator animator;

        int speedHash;
        int walkRateHash;
        int sittingHash;
        int jumpHash;

        bool wasHopping;

        void Awake()
        {
            manager = GetComponent<ManagerController>();
            agent = GetComponent<NavMeshAgent>();
            animator = GetComponentInChildren<Animator>(true);

            speedHash = Animator.StringToHash(speedParameter);
            walkRateHash = Animator.StringToHash(walkRateParameter);
            sittingHash = Animator.StringToHash(sittingParameter);
            jumpHash = Animator.StringToHash(jumpParameter);

            if (animator == null)
            {
                Debug.LogWarning("[Manager] No Animator under '" + name + "', so nothing will be "
                    + "animated. Add the model child back, or remove this component.", this);
            }
        }

        void Update()
        {
            if (animator == null)
            {
                return;
            }

            // A hop owns the transform outright and the agent is off for its whole length, so the
            // walk has to be reported as stopped or the manager would stride through the air.
            var hopping = manager.IsHopping;
            var navigating = !hopping && agent != null && agent.enabled && agent.isOnNavMesh;

            var worldSpeed = navigating ? agent.velocity.magnitude : 0f;
            var speed = navigating && agent.speed > 0f
                ? Mathf.Clamp01(worldSpeed / agent.speed)
                : 0f;

            animator.SetFloat(speedHash, speed, damping, Time.deltaTime);

            var rate = referenceSpeed > 0f
                ? Mathf.Clamp(worldSpeed / referenceSpeed, rateLimits.x, rateLimits.y)
                : 1f;

            animator.SetFloat(walkRateHash, rate);
            animator.SetBool(sittingHash, manager.IsSeated);

            // Rising edge only. Setting it every frame of the hop would re-enter the state and
            // restart the clip, so the jump would never finish playing.
            if (hopping && !wasHopping)
            {
                animator.SetTrigger(jumpHash);
            }

            wasHopping = hopping;
        }
    }
}
