using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;

namespace Dojo.Game.InGame.Controllers
{
    /// <summary>
    /// Mouse control for the isometric camera: the wheel zooms, and holding the middle button
    /// drags the world under the cursor.
    /// </summary>
    /// <remarks>
    /// The camera is orthographic, so zoom changes <c>orthographicSize</c> rather than moving the
    /// camera, and one screen pixel is a fixed number of world units regardless of depth. That is
    /// what makes the drag track the cursor exactly.
    /// </remarks>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraRig : MonoBehaviour
    {
        [Header("Zoom")]
        [Tooltip("Change in orthographic size per wheel notch.")]
        [SerializeField] float zoomStep = 0.8f;

        [Tooltip("Closest zoom, in orthographic size.")]
        [SerializeField] float minZoom = 2f;

        [Tooltip("Furthest zoom, in orthographic size.")]
        [SerializeField] float maxZoom = 20f;

        [Tooltip("How quickly the zoom eases to the new level. 0 snaps instantly.")]
        [SerializeField] float zoomSmoothing = 14f;

        [Header("Drag")]
        [Tooltip("Hold this button to drag the world.")]
        [SerializeField] DragButton dragButton = DragButton.Middle;

        [Tooltip("Drag the camera instead of the world, so the view follows the mouse.")]
        [SerializeField] bool invertDrag;

        [Header("Rotate")]
        [Tooltip("Hold the right button and drag to swing the view around the point being looked at.")]
        [SerializeField] bool enableRotate = true;

        [Tooltip("Degrees of swing per pixel dragged.")]
        [SerializeField] float degreesPerPixel = 0.25f;

        [Tooltip("Let a vertical drag change the pitch too. Off by default: the camera is set to a " +
                 "true isometric 35.26 degrees, and tilting it gives up that projection.")]
        [SerializeField] bool allowPitch;

        [SerializeField] float minPitch = 15f;

        [SerializeField] float maxPitch = 80f;

        [Tooltip("Height of the plane the view swings around. 0 is the floor.")]
        [SerializeField] float pivotHeight;

        [Header("Touch (phones and tablets only)")]
        [Tooltip("Pixels the two fingers' midpoint must travel before the drag counts as a pan, so " +
                 "a pinch or a twist does not also slide the view.")]
        [SerializeField] float panDeadZone = 8f;

        [Tooltip("Degrees the two fingers must turn before a twist starts rotating, so a pinch or a " +
                 "pan does not also wobble the view.")]
        [SerializeField] float twistDeadZone = 4f;

        [Header("General")]
        [Tooltip("Ignore wheel and drag while the cursor is over UI, so scrolling a panel does " +
                 "not also zoom the world.")]
        [SerializeField] bool blockedByUI = true;

        enum DragButton
        {
            Middle,
            Right,
        }

        Camera cam;
        float targetZoom;
        bool dragging;
        Vector2 lastPointer;
        bool rotating;
        Vector2 lastRotatePointer;

        CinemachinePositionComposer drivenComposer;

        /// <summary>
        /// The virtual camera this rig steers instead of the Camera itself, or null for direct
        /// control. Set by <see cref="ManagerCameraFollow"/> when it takes over the view.
        /// </summary>
        /// <remarks>
        /// Both cannot write the transform in the same frame — that was the original reason this
        /// component was simply switched off while following, which also switched off the player's
        /// mouse. Steering the virtual camera instead keeps every gesture working and still leaves
        /// exactly one writer: Cinemachine.
        /// </remarks>
        public CinemachineCamera DrivenCamera
        {
            get { return drivenCamera; }
            set
            {
                drivenCamera = value;
                drivenComposer = value == null ? null : value.GetComponent<CinemachinePositionComposer>();

                // Adopt whatever zoom the side being handed control is already at. Without this the
                // first scroll after a handover snaps to the other one's level, which reads as the
                // camera lurching for no reason the player can see.
                targetZoom = value == null ? cam.orthographicSize : value.Lens.OrthographicSize;
            }
        }

        CinemachineCamera drivenCamera;

        /// <summary>True when input should be applied to the virtual camera rather than this one.</summary>
        bool Driving => DrivenCamera != null && DrivenCamera.enabled;

        /// <summary>The transform whose axes the gestures are measured against.</summary>
        Transform Rig => Driving ? DrivenCamera.transform : transform;

        /// <summary>The orthographic size currently in effect, wherever it is being read from.</summary>
        float CurrentZoom => Driving ? DrivenCamera.Lens.OrthographicSize : cam.orthographicSize;

        /// <summary>
        /// While true the rig ignores the mouse entirely. Furniture placement raises this once a
        /// piece is dropped and awaiting a commit, because the wheel then turns the piece and the
        /// right button cancels it — both of which this rig would otherwise claim.
        /// </summary>
        public bool InputSuppressed { get; set; }

        /// <summary>
        /// While true, only panning works: the wheel does not zoom and the view does not rotate.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="InputSuppressed"/>, which takes the whole rig away. Edit phase
        /// wants the opposite of that — the player is arranging a room and needs to move around it,
        /// but a zoom or a swing under them while they are placing furniture is the camera arguing
        /// with the work.
        /// <para>
        /// A rotation in progress is dropped rather than left to finish, for the same reason
        /// <see cref="InputSuppressed"/> drops one: resuming a gesture the player has stopped
        /// thinking about makes the view lurch.
        /// </para>
        /// </remarks>
        public bool PanOnly { get; set; }

        /// <summary>True while a drag is in progress.</summary>
        public bool IsDragging => dragging;

        /// <summary>
        /// The orthographic size the rig is easing towards, clamped to the configured range.
        /// </summary>
        /// <remarks>
        /// Anything else that wants to change the zoom must go through here rather than writing the
        /// lens. The rig eases towards this value every frame, so a direct write is undone before
        /// the next draw — which looks exactly like the zoom being ignored.
        /// </remarks>
        public float TargetZoom
        {
            get { return targetZoom; }
            set { targetZoom = Mathf.Clamp(value, minZoom, maxZoom); }
        }

        /// <summary>The zoom the game opened at, for putting the view back to how it started.</summary>
        /// <remarks>
        /// Taken in <c>Start</c>, once every <c>Awake</c> has run: the manager camera hands this rig
        /// its camera in its own <c>Awake</c>, and the zoom the rig adopts then is the opening one.
        /// </remarks>
        public float StartZoom { get; private set; }

        /// <summary>Closest the rig will allow.</summary>
        public float MinZoom => minZoom;

        /// <summary>Furthest the rig will allow. A computed framing has to be clamped to this.</summary>
        public float MaxZoom => maxZoom;

        /// <summary>True while the view is being swung around.</summary>
        public bool IsRotating => rotating;

        /// <summary>
        /// Two-finger gestures instead of the mouse: on a phone or tablet only.
        /// </summary>
        /// <remarks>
        /// The editor and desktop builds keep the mouse and nothing about them changes. Unity's
        /// Device Simulator reports a mobile platform, so it takes this path too - though it can only
        /// simulate one finger, so a two-finger gesture needs a real device.
        /// </remarks>
        static bool TouchPlatform => Application.isMobilePlatform || (Application.isEditor && SimulateTouchInEditor);

        /// <summary>Whether the camera is taking gestures rather than the mouse, for anything that explains the controls.</summary>
        public static bool UsesTouch => TouchPlatform;

        /// <summary>
        /// Editor testing only: takes the touch path in the editor so simulated touches can drive it.
        /// Ignored in every build.
        /// </summary>
        public static bool SimulateTouchInEditor { get; set; }

        // The two-finger gesture in progress, if any.
        bool touchGesture;
        bool touchPanning;
        bool touchTwisting;
        Vector2 touchStartMid;
        float touchStartAngle;
        Vector2 lastTouchMid;
        float lastTouchDistance;
        float lastTouchAngle;

        void Awake()
        {
            cam = GetComponent<Camera>();
            targetZoom = cam.orthographicSize;
        }

        void Start() => StartZoom = targetZoom;

        void OnEnable()
        {
            if (TouchPlatform)
            {
                EnhancedTouchSupport.Enable();
            }
        }

        void Update()
        {
            if (TouchPlatform)
            {
                UpdateTouch();
                return;
            }

            var mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            if (InputSuppressed)
            {
                // Any in-progress gesture is dropped rather than resumed later, so the view does
                // not lurch when control comes back.
                dragging = false;
                rotating = false;
                return;
            }

            if (PanOnly)
            {
                // Drag only. A rotation already under way is dropped here rather than allowed to
                // finish, so the view does not keep swinging after the drawer opened.
                rotating = false;
                HandleDrag(mouse);
                return;
            }

            HandleZoom(mouse);
            HandleDrag(mouse);
            HandleRotate(mouse);
        }

        /// <summary>
        /// The camera on a touchscreen: two fingers dragged together pan, a pinch zooms, and a
        /// twist rotates.
        /// </summary>
        /// <remarks>
        /// Two fingers only. One finger is left alone for acting on the room - tapping, and moving
        /// pieces in Edit - so a camera gesture can never be mistaken for one of those.
        /// <para>
        /// All three read from the same pair of fingers each frame, so they can happen together:
        /// the midpoint's travel is the pan, the change in their distance the zoom, and the change
        /// in the angle between them the turn. Pan and twist wait for a small dead zone before they
        /// start, because nobody pinches without their fingers drifting or turning a little.
        /// </para>
        /// <para>
        /// A pan or a twist reports as <see cref="IsDragging"/> or <see cref="IsRotating"/>, exactly
        /// as the mouse gestures do, so <c>MainCinemachineCamera</c> treats them the same way.
        /// </para>
        /// </remarks>
        void UpdateTouch()
        {
            if (!EnhancedTouchSupport.enabled)
            {
                EnhancedTouchSupport.Enable();
            }

            if (InputSuppressed)
            {
                EndTouchGesture();
                return;
            }

            var touches = UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches;

            if (touches.Count < 2)
            {
                EndTouchGesture();
                EaseZoom();
                return;
            }

            var a = touches[0].screenPosition;
            var b = touches[1].screenPosition;
            var mid = (a + b) * 0.5f;
            var span = b - a;
            var distance = span.magnitude;
            var angle = Mathf.Atan2(span.y, span.x) * Mathf.Rad2Deg;

            if (!touchGesture)
            {
                // A gesture that starts on the UI belongs to the UI - two fingers on a scrolling
                // panel must not also move the world behind it.
                if (TouchOverUI(touches[0]) || TouchOverUI(touches[1]))
                {
                    return;
                }

                touchGesture = true;
                touchPanning = false;
                touchTwisting = false;
                touchStartMid = lastTouchMid = mid;
                touchStartAngle = lastTouchAngle = angle;
                lastTouchDistance = distance;
                return;
            }

            if (!touchPanning && (mid - touchStartMid).magnitude > panDeadZone)
            {
                touchPanning = true;
            }

            if (touchPanning)
            {
                ApplyPan(mid - lastTouchMid);
            }

            if (!PanOnly)
            {
                // The lens grows as the fingers close: an orthographic size is how much ground fits,
                // so it scales by the inverse of the spread.
                if (lastTouchDistance > 1f && distance > 1f)
                {
                    targetZoom = Mathf.Clamp(targetZoom * (lastTouchDistance / distance), minZoom, maxZoom);
                }

                if (enableRotate && !touchTwisting
                    && Mathf.Abs(Mathf.DeltaAngle(touchStartAngle, angle)) > twistDeadZone)
                {
                    touchTwisting = true;
                }

                if (touchTwisting)
                {
                    // Fingers turning anticlockwise on screen turn the room anticlockwise with them.
                    ApplyTurn(Mathf.DeltaAngle(lastTouchAngle, angle), 0f);
                }

                EaseZoom();
            }

            dragging = touchPanning;
            rotating = touchTwisting;

            lastTouchMid = mid;
            lastTouchDistance = distance;
            lastTouchAngle = angle;
        }

        void EndTouchGesture()
        {
            touchGesture = false;
            touchPanning = false;
            touchTwisting = false;
            dragging = false;
            rotating = false;
        }

        bool TouchOverUI(UnityEngine.InputSystem.EnhancedTouch.Touch touch)
            => blockedByUI
            && EventSystem.current != null
            && EventSystem.current.IsPointerOverGameObject(touch.touchId);

        void HandleZoom(Mouse mouse)
        {
            if (!OverBlockingUI())
            {
                var scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    // Only the sign is used. Windows reports 120 per notch and other platforms
                    // report their own scale, so raw values would zoom at wildly different rates.
                    targetZoom = Mathf.Clamp(targetZoom - Mathf.Sign(scroll) * zoomStep, minZoom, maxZoom);
                }
            }

            EaseZoom();
        }

        /// <summary>Moves the lens a step towards <see cref="TargetZoom"/>.</summary>
        void EaseZoom()
        {
            if (!cam.orthographic)
            {
                return;
            }

            var eased = zoomSmoothing <= 0f
                ? targetZoom
                : Mathf.Lerp(CurrentZoom, targetZoom,
                    1f - Mathf.Exp(-zoomSmoothing * Time.unscaledDeltaTime));

            if (Driving)
            {
                // Through the lens, not the Camera: the brain copies the live virtual camera's lens
                // onto the Camera every frame, so writing the Camera directly would be overwritten
                // before anything drew.
                var lens = DrivenCamera.Lens;
                lens.OrthographicSize = eased;
                DrivenCamera.Lens = lens;
                return;
            }

            cam.orthographicSize = eased;
        }

        bool OverBlockingUI()
            => blockedByUI
            && EventSystem.current != null
            && EventSystem.current.IsPointerOverGameObject();

        void HandleDrag(Mouse mouse)
        {
            var button = dragButton == DragButton.Right ? mouse.rightButton : mouse.middleButton;

            if (button.wasPressedThisFrame && !OverBlockingUI())
            {
                dragging = true;
                lastPointer = mouse.position.ReadValue();
            }

            if (button.wasReleasedThisFrame)
            {
                dragging = false;
            }

            if (!dragging)
            {
                return;
            }

            var pointer = mouse.position.ReadValue();
            var delta = pointer - lastPointer;
            lastPointer = pointer;

            ApplyPan(delta);
        }

        /// <summary>Moves the view so the ground under a pointer that moved by <paramref name="delta"/> stays under it.</summary>
        void ApplyPan(Vector2 delta)
        {
            if (delta.sqrMagnitude < 0.0001f)
            {
                return;
            }

            if (Driving)
            {
                // With a follow target the composer owns the position, so moving the virtual camera
                // would be undone the moment it next ran — the framing point is nudged instead.
                // With no target the composer stands down, and the virtual camera is moved directly
                // exactly as the Camera used to be.
                if (drivenComposer != null && DrivenCamera.Follow != null)
                {
                    drivenComposer.TargetOffset += DragOffset(delta);
                }
                else
                {
                    DrivenCamera.transform.position += DragOffset(delta);
                }

                return;
            }

            transform.position += DragOffset(delta);
        }

        /// <summary>
        /// Lifts or lowers the whole view by <paramref name="height"/> over <paramref name="seconds"/>,
        /// for the player switching to another floor. Nothing else about the view changes.
        /// </summary>
        public void ShiftFloor(float height, float seconds) => ShiftView(Vector3.up * height, seconds);

        /// <summary>
        /// Moves the whole view by <paramref name="offset"/> over <paramref name="seconds"/>: up or
        /// down to another floor, and across it when what is on that floor is elsewhere. Turn and
        /// zoom are left as they are.
        /// </summary>
        /// <remarks>
        /// Eased in and out, so the move reads as the camera travelling between floors rather than
        /// cutting. A second move before the first has finished folds into it: what is still to go
        /// is added to, and the whole rest of the move eases out again from where the view is now.
        /// </remarks>
        public void ShiftView(Vector3 offset, float seconds)
        {
            shiftRemaining += offset;

            if (shiftTween >= 0)
            {
                LeanTween.cancel(shiftTween);
                shiftTween = -1;
            }

            if (shiftRemaining.sqrMagnitude < 0.000001f)
            {
                shiftRemaining = Vector3.zero;
                return;
            }

            if (seconds <= 0f)
            {
                Move(shiftRemaining);
                shiftRemaining = Vector3.zero;
                return;
            }

            var applied = 0f;
            var total = shiftRemaining;

            shiftTween = LeanTween.value(gameObject, 0f, 1f, seconds)
                .setEase(LeanTweenType.easeInOutSine)
                .setOnUpdate((float done) =>
                {
                    var step = total * (done - applied);
                    applied = done;
                    shiftRemaining -= step;
                    Move(step);
                })
                .setOnComplete(() =>
                {
                    shiftTween = -1;
                    shiftRemaining = Vector3.zero;
                })
                .id;
        }

        /// <summary>
        /// Keeps the view exactly where it is while the subject it follows jumps by
        /// <paramref name="height"/> - an elevator moving the manager between floors in one step.
        /// </summary>
        /// <remarks>
        /// Only the framing point is moved back by the jump, so what is framed stays put; the camera
        /// and the plane it swings around do not move, since the view has not. A floor switch
        /// afterwards (<see cref="ShiftFloor"/>) then carries the view to his new floor.
        /// </remarks>
        public void HoldThroughJump(float height)
        {
            if (FollowTarget == null || Mathf.Approximately(height, 0f))
            {
                return;
            }

            drivenComposer.TargetOffset -= Vector3.up * height;
        }

        /// <summary>What the view is following, or null while the player is holding it.</summary>
        public Transform FollowTarget => Driving && drivenComposer != null ? DrivenCamera.Follow : null;

        /// <summary>
        /// How far above the followed subject the view is framed, counting a floor switch still on
        /// its way. Only floor switches raise it: a pan while following moves the framing flat.
        /// </summary>
        public float FollowLift => (FollowTarget != null ? drivenComposer.TargetOffset.y : 0f) + shiftRemaining.y;

        /// <summary>True while a move made by <see cref="ShiftView"/> is still on its way.</summary>
        public bool IsShifting => shiftTween >= 0;

        /// <summary>What is still to be travelled by a move in progress.</summary>
        Vector3 shiftRemaining;

        int shiftTween = -1;

        void Move(Vector3 step)
        {
            Lift(step.y);
            Slide(new Vector3(step.x, 0f, step.z));
        }

        /// <summary>Moves the view flat, by the same three cases a drag does.</summary>
        void Slide(Vector3 across)
        {
            if (across.sqrMagnitude < 0.000001f)
            {
                return;
            }

            if (Driving)
            {
                if (drivenComposer != null && DrivenCamera.Follow != null)
                {
                    drivenComposer.TargetOffset += across;
                }
                else
                {
                    DrivenCamera.transform.position += across;
                }

                return;
            }

            transform.position += across;
        }

        /// <summary>
        /// Lifts or lowers the view at once.
        /// </summary>
        /// <remarks>
        /// The same three cases a drag is: while following the manager the composer owns the
        /// position, so its framing point is raised instead; with no target the virtual camera is
        /// moved; otherwise this camera is. The plane the view swings around goes up with it, or the
        /// first rotation on the new floor would swing around the floor below.
        /// <para>
        /// While following, the camera is carried with the framing point. The composer only goes
        /// after a point that leaves its dead zone, and a floor's height does not: raised alone, the
        /// point moved and the camera merely slid along its line of sight, so a ride down left the
        /// view still aimed where the floor above had been.
        /// </para>
        /// </remarks>
        void Lift(float height)
        {
            if (Mathf.Approximately(height, 0f))
            {
                return;
            }

            var lift = Vector3.up * height;
            pivotHeight += height;

            if (Driving)
            {
                if (drivenComposer != null && DrivenCamera.Follow != null)
                {
                    drivenComposer.TargetOffset += lift;
                    DrivenCamera.OnTargetObjectWarped(DrivenCamera.Follow, lift);
                }
                else
                {
                    DrivenCamera.transform.position += lift;
                }

                return;
            }

            transform.position += lift;
        }

        void HandleRotate(Mouse mouse)
        {
            // Right is also an option for panning; if it is set to that, panning keeps the button.
            if (!enableRotate || dragButton == DragButton.Right)
            {
                return;
            }

            var button = mouse.rightButton;

            if (button.wasPressedThisFrame && !OverBlockingUI())
            {
                rotating = true;
                lastRotatePointer = mouse.position.ReadValue();
            }

            if (button.wasReleasedThisFrame)
            {
                rotating = false;
            }

            if (!rotating)
            {
                return;
            }

            var pointer = mouse.position.ReadValue();
            var delta = pointer - lastRotatePointer;
            lastRotatePointer = pointer;

            if (delta.sqrMagnitude < 0.0001f)
            {
                return;
            }

            ApplyTurn(delta.x * degreesPerPixel, allowPitch ? delta.y * degreesPerPixel : 0f);
        }

        /// <summary>Swings the view by <paramref name="yaw"/> degrees, and tilts it by <paramref name="pitch"/>.</summary>
        void ApplyTurn(float yaw, float pitch)
        {
            if (Driving && drivenComposer != null && DrivenCamera.Follow != null)
            {
                // Only the rotation is changed; the composer recomputes where the camera has to sit
                // to keep its framing, which turns a yaw into an orbit around the manager for free.
                DrivenCamera.transform.Rotate(Vector3.up, yaw, Space.World);

                if (allowPitch && !Mathf.Approximately(pitch, 0f))
                {
                    var current = PitchOf(DrivenCamera.transform);
                    var target = Mathf.Clamp(current + pitch, minPitch, maxPitch);
                    DrivenCamera.transform.Rotate(DrivenCamera.transform.right, target - current, Space.World);
                }

                return;
            }

            // Nothing framing a target - the free camera after a pan, or this camera itself - so
            // nothing turns a yaw into an orbit. Turning it in place spun the view around the camera
            // and swung the scene off screen, which is what went wrong after a drag. It swings
            // around what it is looking at instead. (MainCinemachineCamera goes back onto the
            // manager as soon as a rotation starts, unless the player is looking at another floor,
            // so outside that this lasts one frame.)
            var swung = Rig;
            Vector3 pivot;

            if (FreePivot == null || !FreePivot(swung, out pivot))
            {
                pivot = GroundPivot(swung);
            }
            swung.RotateAround(pivot, Vector3.up, yaw);

            if (!allowPitch || Mathf.Approximately(pitch, 0f))
            {
                return;
            }

            var now = PitchOf(swung);
            var wanted = Mathf.Clamp(now + pitch, minPitch, maxPitch);
            swung.RotateAround(pivot, swung.right, wanted - now);
        }

        /// <summary>What a camera turning around a point - out of the manager's sight - finds that point.</summary>
        public delegate bool PivotFinder(Transform camera, out Vector3 pivot);

        /// <summary>
        /// Where the view swings around when nothing is being followed, or null for the point
        /// straight ahead on the pivot plane.
        /// </summary>
        /// <remarks>
        /// Set by the floor switcher, which knows which floors are hidden: it finds the thing in
        /// the middle of the screen on the floor being looked at, so a turn swings around what the
        /// player is actually looking at rather than a plane at some other floor's height.
        /// </remarks>
        public PivotFinder FreePivot { get; set; }

        /// <summary>Angle below the horizontal that an arbitrary transform looks, in degrees.</summary>
        static float PitchOf(Transform t)
            => Mathf.Asin(Mathf.Clamp(-t.forward.y, -1f, 1f)) * Mathf.Rad2Deg;

        /// <summary>Where a camera's forward ray meets the pivot plane.</summary>
        Vector3 GroundPivot(Transform camera)
        {
            var forward = camera.forward;
            var descent = -forward.y;

            // Looking at the horizon: there is no ground intersection to swing around, so pick a
            // point a little way ahead instead of dividing by nearly zero.
            if (Mathf.Abs(descent) < 0.01f)
            {
                return camera.position + forward * 10f;
            }

            return camera.position + forward * ((camera.position.y - pivotHeight) / descent);
        }

        /// <summary>
        /// How far to move the camera so the ground point under the cursor stays under it.
        /// </summary>
        /// <remarks>
        /// The camera moves on the ground plane rather than along its own up axis, which keeps its
        /// height constant. A vertical drag therefore has to be divided by the sine of the pitch:
        /// the ground recedes from the camera faster than it climbs the screen.
        /// </remarks>
        Vector3 DragOffset(Vector2 deltaPixels)
        {
            var height = Mathf.Max(1, cam.pixelHeight);
            var worldPerPixel = cam.orthographic
                ? CurrentZoom * 2f / height
                : 1f / height;

            // Measured against whichever transform is actually framing the shot, so a drag tracks
            // the cursor identically whether the rig or Cinemachine is holding the view.
            var rig = Rig;
            var right = Vector3.ProjectOnPlane(rig.right, Vector3.up).normalized;
            var groundForward = Vector3.ProjectOnPlane(rig.forward, Vector3.up).normalized;

            // Guard against a camera looking straight down the horizon, where the ground plane
            // cannot be tracked at all and this would divide by zero.
            var pitchSin = Mathf.Max(Mathf.Abs(rig.forward.y), 0.05f);

            var offset = right * (deltaPixels.x * worldPerPixel)
                       + groundForward * (deltaPixels.y * worldPerPixel / pitchSin);

            // Moving the camera against the drag is what makes the world follow the cursor.
            return invertDrag ? offset : -offset;
        }
    }
}
