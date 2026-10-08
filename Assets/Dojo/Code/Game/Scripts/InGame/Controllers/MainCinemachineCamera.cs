using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Dojo.Game.InGame.Controllers
{
    /// <summary>
    /// The game camera: follows the owner manager, yields when the player pans, and blends back
    /// rather than cutting.
    /// </summary>
    /// <remarks>
    /// Two virtual cameras, and the brain blends between them — which is the only way to get a
    /// timed camera transition out of Cinemachine. One camera is free and steered by
    /// <see cref="CameraRig"/>; the other frames the manager through its composer. Raising one's
    /// priority hands it the view and the brain eases the picture across over
    /// <see cref="blendSeconds"/>.
    /// <para>
    /// A single camera cannot do this. Toggling one on and off leaves the brain with nothing to
    /// blend from, so it cuts — there has to be a second camera for the shot to travel between.
    /// </para>
    /// <para>
    /// The manager is spawned when a world loads or when Place Manager is pressed, so the follow
    /// target cannot be assigned in the inspector and is found at runtime.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(CinemachineCamera))]
    [RequireComponent(typeof(CinemachinePositionComposer))]
    public sealed class MainCinemachineCamera : MonoBehaviour
    {
        /// <summary>Priority of whichever camera currently owns the view.</summary>
        const int Live = 20;

        /// <summary>Priority of the camera waiting its turn.</summary>
        const int Standby = 10;

        [Tooltip("The free camera the player steers. The rig moves this one; this component blends " +
                 "between it and the manager camera.")]
        [SerializeField] CinemachineCamera freeCamera;

        [Tooltip("The rig that turns mouse input into camera movement. It steers whichever camera " +
                 "currently owns the view.")]
        [SerializeField] CameraRig rig;

        [Tooltip("Start following as soon as a manager exists.")]
        [SerializeField] bool followOnStart = true;

        [Tooltip("Seconds between looks for a manager while none has been found. Stops once found.")]
        [SerializeField] float searchInterval = 0.25f;

        [Header("Transition")]
        [Tooltip("How the picture travels between the free camera and the manager camera.")]
        [SerializeField] CinemachineBlendDefinition.Styles blendStyle = CinemachineBlendDefinition.Styles.EaseInOut;

        [Tooltip("Seconds the transition takes.")]
        [SerializeField] float blendSeconds = 1.5f;

        [Tooltip("Damping once the manager is framed. Smaller keeps them tighter in the dead zone.")]
        [SerializeField] float followDamping = 1f;

        [Header("Returning after a pan")]
        [Tooltip("How long to leave the view where the player put it before blending back to the " +
                 "manager. The clock only starts once they stop dragging.")]
        [SerializeField] float returnDelay = 3f;

        [Header("Facing the view")]
        [Tooltip("Seconds after a pan or a rotation ends before the manager turns to face the " +
                 "camera. Negative turns this off.")]
        [SerializeField] float faceCameraDelay = 1f;

        [Header("Recentre")]
        [Tooltip("Let a key put the camera back on the manager immediately.")]
        [SerializeField] bool allowKeyboardResume = true;

        [Tooltip("The key that recentres on the manager.")]
        [SerializeField] Key resumeKey = Key.F;

        CinemachineCamera followCamera;
        CinemachinePositionComposer composer;
        CinemachineBrain brain;
        ManagerController target;

        bool following;
        float nextSearch;
        float returnAt;

        /// <summary>A pan or rotation has ended and the manager has not yet turned to the new view.</summary>
        bool faceAfterGesture;

        /// <summary>When he turns, reset by every frame the gesture is still going.</summary>
        float faceAt;

        /// <summary>True while the manager camera owns the view.</summary>
        public bool IsFollowing => following;

        /// <summary>
        /// True while the player is looking at a floor with ▲ or ▼, from the moment they press one
        /// until the view goes back to the manager.
        /// </summary>
        /// <remarks>
        /// Looking is the view let go of, like a pan, with one difference: a rotation does not
        /// bring the view back onto the manager to turn around him. It turns around what is in the
        /// middle of the floor being looked at, and counts as the player moving the view, so the
        /// return waits for them as it does after a pan.
        /// <para>
        /// The return pause starts once the move to the floor has finished, and again each time a
        /// pan or a turn ends: the view goes back to him after that long with neither.
        /// </para>
        /// </remarks>
        public bool IsLooking => looking;

        bool looking;

        /// <summary>
        /// Lets go of the manager so the player can look at another floor. The view comes back to
        /// him after the usual pause with no pan or turn, or as soon as he is sent somewhere.
        /// </summary>
        public void LookAway()
        {
            looking = true;
            SetFollowing(false);
        }

        /// <summary>
        /// While true the camera stops driving itself: no return to the manager, no blends.
        /// </summary>
        /// <remarks>
        /// For Edit phase. The view stays exactly where the player puts it, because everything
        /// this component normally does — waiting <see cref="returnDelay"/> and easing back onto
        /// the manager, or coming back the moment he is told to walk — is the camera taking the
        /// shot away from somebody who is in the middle of arranging a room.
        /// <para>
        /// The brain's blend is switched to <c>Cut</c> while suspended and put back afterwards.
        /// Suppressing what triggers a blend is not the same as suppressing the blend: a handover
        /// already under way when this is raised would otherwise carry on gliding.
        /// </para>
        /// </remarks>
        public bool Suspended
        {
            get { return suspended; }
            set
            {
                if (suspended == value)
                {
                    return;
                }

                suspended = value;

                if (suspended)
                {
                    // Handed over from exactly where the picture is, so this reads as the camera
                    // stopping rather than as a cut to somewhere else.
                    Release();

                    // Every easing the camera can do, switched off for the whole of Edit rather
                    // than only the ones Release happens to touch. Release returns early when the
                    // free camera already had the view, so relying on it to have cut would leave
                    // the eased blend armed for whatever happened next.
                    ApplyCutBlend();
                    SetDamping(0f);
                }
                else
                {
                    // Normal behaviour returns, including the return timer — so the camera eases
                    // back to the manager a few seconds later, exactly as it would after any pan.
                    SetDamping(followDamping);
                    returnAt = Time.unscaledTime + Mathf.Max(0f, returnDelay);
                }
            }
        }

        bool suspended;

        /// <summary>The manager currently being followed, or null when none has been found.</summary>
        public ManagerController Target => target;

        /// <summary>Someone the camera is watching instead of the manager for now, or null.</summary>
        /// <remarks>
        /// Everything else carries on as normal while there is one - a pan, the return after it,
        /// a rotation - it simply comes back to this subject rather than to the manager.
        /// </remarks>
        public Transform Focus => focus;

        Transform focus;

        /// <summary>Blends the view onto <paramref name="subject"/> and keeps it there until <see cref="ClearFocus"/>.</summary>
        public void FocusOn(Transform subject)
        {
            if (subject == null || subject == focus)
            {
                return;
            }

            focus = subject;
            Refollow();
        }

        /// <summary>Blends the view back onto the manager.</summary>
        public void ClearFocus()
        {
            if (focus == null)
            {
                return;
            }

            focus = null;
            Refollow();
        }

        /// <summary>
        /// Hands the view over to whoever is followed now: out to the free camera where the picture
        /// is, then the eased blend back in onto the new subject - a journey, the same as any return.
        /// </summary>
        void Refollow()
        {
            if (suspended)
            {
                return;
            }

            if (following)
            {
                Release();
            }

            SetFollowing(true);
        }

        void Awake()
        {
            followCamera = GetComponent<CinemachineCamera>();
            composer = GetComponent<CinemachinePositionComposer>();

            SetDamping(followDamping);
            ApplyBlend();

            // The free camera starts with the view, because there is nobody to follow yet.
            GiveViewTo(freeCamera, followCamera);
            following = false;

            if (rig != null && freeCamera != null)
            {
                rig.DrivenCamera = freeCamera;
            }
        }

        void OnDisable()
        {
            if (target != null)
            {
                target.TravelRequested -= OnTravelRequested;
            }
        }

        void LateUpdate()
        {
            if (!AcquireTarget())
            {
                return;
            }

            if (suspended)
            {
                // Nothing below this line: no return countdown, no keyboard resume, no drag
                // bookkeeping. The player owns the view until the phase says otherwise.
                faceAfterGesture = false;
                return;
            }

            TrackFacing();

            // While the button is down the player is still choosing a view, so the clock keeps
            // resetting rather than counting down under them mid-drag.
            if (rig != null && rig.IsDragging)
            {
                Release();
                returnAt = Time.unscaledTime + Mathf.Max(0f, returnDelay);
                return;
            }

            // Looking at a floor, the clock also waits for the move there to finish: the pause the
            // player gets to look is counted from when the view arrives, not from the button press.
            if (looking && rig != null && rig.IsShifting)
            {
                returnAt = Time.unscaledTime + Mathf.Max(0f, returnDelay);
                return;
            }

            // A rotation turns around the manager. Panned away, only the free camera has the view,
            // and it has nothing framing him to orbit - so the view goes back onto him first, and
            // from then on the follow camera's composer keeps him at the centre as it turns.
            //
            // Not while looking at another floor: there the turn is around what is on screen, and it
            // is the player moving the view, so the return waits for them as it does after a pan.
            if (rig != null && rig.IsRotating && !following)
            {
                if (looking)
                {
                    returnAt = Time.unscaledTime + Mathf.Max(0f, returnDelay);
                    return;
                }

                SetFollowing(true);
                return;
            }

            if (allowKeyboardResume && ResumePressed())
            {
                SetFollowing(true);
                return;
            }

            if (!following && Time.unscaledTime >= returnAt)
            {
                SetFollowing(true);
            }
        }

        /// <summary>
        /// One <see cref="faceCameraDelay"/> after a pan or a rotation ends, turns the manager to
        /// face the camera.
        /// </summary>
        /// <remarks>
        /// Every frame the gesture is still going pushes the moment back, so the turn waits for the
        /// player to stop rather than twitching along with each drag. Faced towards where the view
        /// is heading, not where it is: the camera may still be easing back onto him, and facing a
        /// picture that is halfway through a blend would leave him slightly off once it settled.
        /// </remarks>
        void TrackFacing()
        {
            if (faceCameraDelay < 0f || rig == null)
            {
                return;
            }

            if (rig.IsDragging || rig.IsRotating)
            {
                faceAfterGesture = true;
                faceAt = Time.unscaledTime + faceCameraDelay;
                return;
            }

            if (!faceAfterGesture || Time.unscaledTime < faceAt)
            {
                return;
            }

            faceAfterGesture = false;

            var heading = following ? followCamera : freeCamera;

            if (heading != null)
            {
                // Facing the camera is facing back along its line of sight; any point on that line
                // gives the same yaw, whether the lens is orthographic or not.
                target.FaceCamera(target.transform.position - heading.transform.forward * 10f);
            }
        }

        /// <summary>
        /// The manager was told to walk somewhere, so bring the camera back to watch.
        /// </summary>
        void OnTravelRequested(TravelEstimate travel) => SetFollowing(true);

        bool AcquireTarget()
        {
            if (target != null)
            {
                return true;
            }

            if (following)
            {
                Release();
            }

            if (Time.unscaledTime < nextSearch)
            {
                return false;
            }

            nextSearch = Time.unscaledTime + Mathf.Max(0.05f, searchInterval);
            target = FindAnyObjectByType<ManagerController>();

            if (target == null)
            {
                return false;
            }

            target.TravelRequested += OnTravelRequested;

            if (followOnStart)
            {
                SetFollowing(true);
            }

            // Found him - the game has started, or he has just been placed - so he turns to face
            // the camera once the view has settled, the same turn a pan or a rotation ends with.
            if (faceCameraDelay >= 0f)
            {
                faceAfterGesture = true;
                faceAt = Time.unscaledTime + faceCameraDelay;
            }

            return true;
        }

        bool ResumePressed()
        {
            var keyboard = Keyboard.current;
            return keyboard != null && keyboard[resumeKey].wasPressedThisFrame;
        }

        /// <summary>Blends the view onto the manager, or hands it back to the player.</summary>
        public void SetFollowing(bool follow)
        {
            if (suspended && follow)
            {
                // Refused rather than queued. This is the door every automatic return comes
                // through — the return timer, the keyboard key, and the manager being told to walk
                // — so closing it here closes all of them at once.
                return;
            }

            if (!follow)
            {
                Release();
                returnAt = Time.unscaledTime + Mathf.Max(0f, returnDelay);
                return;
            }

            if ((target == null && focus == null) || following || freeCamera == null)
            {
                return;
            }

            // Going back to the manager is the one handover that is a journey, so it is the one
            // that asks for the eased blend. Set here rather than once at startup because
            // <see cref="Release"/> leaves the brain on Cut, and a blend definition is read when a
            // transition begins — too late to change it afterwards.
            ApplyBlend();

            // Take the free camera's orientation and zoom across, so the blend is a move and not
            // also a swing and a zoom. Whatever angle the player left the view at is the angle the
            // manager is then framed from.
            followCamera.transform.rotation = freeCamera.transform.rotation;
            followCamera.Lens = freeCamera.Lens;

            // Any pan the player did is forgotten, or arriving at the manager would put them
            // wherever the view had last been dragged to.
            composer.TargetOffset = Vector3.zero;

            // The focus when there is one, the manager otherwise. A new subject snaps the follow
            // camera onto them before it takes the view, so the brain's eased blend is the whole of
            // the journey rather than a blend towards a camera that is itself still damping across.
            var subject = focus != null ? focus : target.transform;

            if (followCamera.Follow != subject)
            {
                followCamera.Follow = subject;
                followCamera.PreviousStateIsValid = false;
            }

            GiveViewTo(followCamera, freeCamera);
            following = true;
            looking = false;

            if (rig != null)
            {
                rig.DrivenCamera = followCamera;
            }
        }

        /// <summary>
        /// Hands the view to the player, starting from exactly where the picture already is.
        /// </summary>
        /// <remarks>
        /// The free camera is planted on the live camera's current pose before it is given the
        /// view. Without that it would still be wherever it was last left, and handing over would
        /// blend the picture across the room to meet it.
        /// <para>
        /// <b>The handover cuts.</b> It has to: the player is already looking at this picture, so
        /// there is nothing to travel to, and the blend was what made panning feel like a tween.
        /// A pan begins by handing the view over, and for as long as the brain was easing between
        /// the two cameras the picture was a lerp of a stationary follow camera and a free camera
        /// moving with the mouse — so the view lagged and eased after the cursor for the whole
        /// blend instead of tracking it. In Edit that showed up on the first pan after the
        /// inventory opened and nowhere else, because by the second pan the blend had finished.
        /// </para>
        /// </remarks>
        void Release()
        {
            if (!following || freeCamera == null)
            {
                return;
            }

            PlantFreeCameraOnScreen();

            // Before the priority change, not after: the brain reads the blend definition when a
            // transition begins, so a cut applied afterwards arrives too late to affect the one
            // just started. That was the original bug — the suspend path set Cut after calling
            // this, and the eased blend it meant to cancel had already begun.
            ApplyCutBlend();

            GiveViewTo(freeCamera, followCamera);
            following = false;

            if (rig != null)
            {
                rig.DrivenCamera = freeCamera;
            }
        }

        /// <summary>
        /// Puts the free camera exactly where the picture is, so handing it the view shows nothing.
        /// </summary>
        /// <remarks>
        /// Mid-blend the follow camera's own pose is not what is on screen — the brain is showing
        /// a mix of the two — so cutting to a camera planted there would jump. The brain's own
        /// state is the picture by definition, so it is used whenever one is being blended.
        /// <para>
        /// Only while blending. Settled, the two are the same answer, and the follow camera's
        /// transform is the one that cannot be a frame stale: the brain's state is written in its
        /// own <c>LateUpdate</c>, which may not have run yet this frame.
        /// </para>
        /// </remarks>
        void PlantFreeCameraOnScreen()
        {
            EnsureBrain();

            if (brain != null && brain.IsBlending)
            {
                var onScreen = brain.State;

                freeCamera.transform.SetPositionAndRotation(
                    onScreen.GetFinalPosition(),
                    onScreen.GetFinalOrientation());
                freeCamera.Lens = onScreen.Lens;

                return;
            }

            freeCamera.transform.SetPositionAndRotation(
                followCamera.transform.position,
                followCamera.transform.rotation);
            freeCamera.Lens = followCamera.Lens;
        }

        static void GiveViewTo(CinemachineCamera live, CinemachineCamera standby)
        {
            if (live != null)
            {
                var p = live.Priority;
                p.Enabled = true;
                p.Value = Live;
                live.Priority = p;
                live.enabled = true;
            }

            if (standby != null)
            {
                var p = standby.Priority;
                p.Enabled = true;
                p.Value = Standby;
                standby.Priority = p;
                standby.enabled = true;
            }
        }

        /// <summary>The brain, found once and kept.</summary>
        void EnsureBrain()
        {
            if (brain == null)
            {
                brain = Object.FindAnyObjectByType<CinemachineBrain>();
            }
        }

        /// <summary>Tells the brain how long a handover should take, and in what shape.</summary>
        void ApplyBlend()
        {
            EnsureBrain();

            if (brain != null)
            {
                brain.DefaultBlend = new CinemachineBlendDefinition(blendStyle, Mathf.Max(0f, blendSeconds));
            }
        }

        /// <summary>Makes the next handover instant.</summary>
        void ApplyCutBlend()
        {
            EnsureBrain();

            if (brain != null)
            {
                brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
            }
        }

        void SetDamping(float seconds)
        {
            var d = Mathf.Max(0f, seconds);
            composer.Damping = new Vector3(d, d, d);
        }
    }
}
