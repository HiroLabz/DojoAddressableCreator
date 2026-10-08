using UnityEngine;
using UnityEngine.InputSystem;

namespace Dojo.Game.InGame.Controllers
{
    /// <summary>
    /// Walks the player around the office with WASD / arrows / left stick. Uses a
    /// <see cref="CharacterController"/>, which collides with every static collider in the scene
    /// (floor, tables, chairs) without needing a Rigidbody.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        [Tooltip("Metres per second.")]
        [SerializeField] float moveSpeed = 4f;

        [Tooltip("Degrees per second the player turns to face its heading.")]
        [SerializeField] float turnSpeed = 720f;

        [SerializeField] float gravity = -20f;

        [Tooltip("Movement is relative to this camera. Defaults to Camera.main.")]
        [SerializeField] Camera view;

        CharacterController controller;
        InputAction moveAction;
        float verticalVelocity;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (view == null)
            {
                view = Camera.main;
            }

            // Built in code rather than pulled from an .inputactions asset so the component works
            // the moment it is dropped on an object, with nothing to wire up in the inspector.
            moveAction = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
            moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");
            moveAction.AddBinding("<Gamepad>/leftStick");
        }

        void OnEnable() => moveAction.Enable();

        void OnDisable() => moveAction.Disable();

        void OnDestroy() => moveAction?.Dispose();

        void Update()
        {
            var heading = ToCameraSpace(moveAction.ReadValue<Vector2>());

            // A small constant downward push while grounded keeps the capsule pinned to the
            // ground; letting it settle at exactly 0 makes isGrounded flicker on ledges.
            if (controller.isGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = -2f;
            }
            else
            {
                verticalVelocity += gravity * Time.deltaTime;
            }

            var velocity = heading * moveSpeed;
            velocity.y = verticalVelocity;
            controller.Move(velocity * Time.deltaTime);

            if (heading.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    Quaternion.LookRotation(heading, Vector3.up),
                    turnSpeed * Time.deltaTime);
            }
        }

        /// <summary>
        /// Maps input onto the camera's ground plane so "up" is always away from the camera. The
        /// view is rotated 45 degrees for the isometric look, so feeding raw world-space input to
        /// the controller would send the player off diagonally from whatever key was pressed.
        /// </summary>
        Vector3 ToCameraSpace(Vector2 input)
        {
            if (view == null)
            {
                return new Vector3(input.x, 0f, input.y);
            }

            var forward = Vector3.ProjectOnPlane(view.transform.forward, Vector3.up).normalized;
            var right = Vector3.ProjectOnPlane(view.transform.right, Vector3.up).normalized;
            return Vector3.ClampMagnitude(forward * input.y + right * input.x, 1f);
        }
    }
}
