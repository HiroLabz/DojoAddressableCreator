using System;
using Dojo.Game.Placement;
using Dojo.Game.Systems;
using UnityEngine;
using VContainer;

namespace Dojo.Game.InGame.Controllers
{
    /// <summary>The views the view cube offers.</summary>
    public enum CameraView
    {
        /// <summary>The game's own isometric view; every floor below the viewed one stays drawn.</summary>
        Perspective,

        /// <summary>Straight down on the viewed floor. Placing and painting still work.</summary>
        Top,

        /// <summary>From the front, level with the floor. Look only.</summary>
        Front,

        /// <summary>From the back. Look only.</summary>
        Back,

        /// <summary>From the left. Look only.</summary>
        Left,

        /// <summary>From the right. Look only.</summary>
        Right,
    }

    /// <summary>
    /// The fixed camera views of the inventory's view cube: top, front, back, left and right, with
    /// the game's own view as Perspective.
    /// </summary>
    /// <remarks>
    /// Only while the inventory is open (Edit phase). Choosing a view eases the camera there,
    /// keeping the spot of floor in the middle of the screen in the middle, and shows the viewed
    /// floor alone (<see cref="Storeys.SoloViewed"/>). In the fixed views the rig pans across the
    /// screen and zooms (<see cref="CameraRig.FixedView"/>); it never turns, which Edit already
    /// stops.
    /// <para>
    /// The side views are for looking: the floor is edge-on, so no cursor can land on it. Placing
    /// and painting are stopped there (<see cref="IsLookOnly"/>), and a paint session under way is
    /// cancelled on the way in. Top keeps every tool, because the cursor is dropped onto the floor's
    /// height and a camera looking straight down meets it as well as one looking at an angle.
    /// </para>
    /// <para>
    /// Closing the inventory goes back to Perspective at once, before the follow camera takes the
    /// view back: it copies the free camera's angle when it does, and must not copy a top view.
    /// </para>
    /// </remarks>
    public sealed class CameraViews : MonoBehaviour
    {
        [Tooltip("Seconds a change of view takes.")]
        [SerializeField] float moveSeconds = 0.35f;

        [Tooltip("How far the camera stands back from the floor in the fixed views, in metres. " +
                 "Orthographic, so this changes nothing on screen; it only has to clear the building.")]
        [SerializeField] float standOff = 60f;

        [Tooltip("Height above the floor the side views look at, in metres: about half a storey.")]
        [SerializeField] float sideEyeHeight = 1.5f;

        CameraRig rig;
        IGamePhase phase;
        Storeys storeys;
        AreaPainter painter;
        PlacementController placement;

        // The game's own view, as it was when the player left it: its angle, and how far the camera
        // stood from the floor point it was looking at.
        Quaternion perspectiveRotation;
        float perspectiveDistance;
        bool havePerspective;

        // The move under way, if any.
        Vector3 fromPosition, toPosition;
        Quaternion fromRotation, toRotation;
        float moveStarted = -1f;

        /// <summary>The view now.</summary>
        public CameraView Current { get; private set; } = CameraView.Perspective;

        /// <summary>True in the side views, where placing and painting are stopped.</summary>
        public bool IsLookOnly => IsSide(Current);

        /// <summary>Raised whenever the view changes, however it was changed.</summary>
        public event Action<CameraView> Changed;

        /// <summary>Whether <paramref name="view"/> is one of the four side views.</summary>
        public static bool IsSide(CameraView view) =>
            view == CameraView.Front || view == CameraView.Back || view == CameraView.Left || view == CameraView.Right;

        /// <summary>The camera's angle in a fixed view. Perspective has none of its own.</summary>
        public static Quaternion RotationOf(CameraView view)
        {
            switch (view)
            {
                case CameraView.Top: return Quaternion.Euler(90f, 0f, 0f);
                case CameraView.Front: return Quaternion.Euler(0f, 0f, 0f);
                case CameraView.Back: return Quaternion.Euler(0f, 180f, 0f);
                case CameraView.Left: return Quaternion.Euler(0f, 90f, 0f);
                case CameraView.Right: return Quaternion.Euler(0f, -90f, 0f);
                default: return Quaternion.identity;
            }
        }

        [Inject]
        public void Construct(IGamePhase gamePhase, Storeys floors, IObjectResolver resolver)
        {
            phase = gamePhase;
            storeys = floors;

            // Only in a scene with a placement system; the views still work without them.
            resolver.TryResolve(out painter);
            resolver.TryResolve(out placement);

            if (phase != null)
            {
                phase.Changed += OnPhaseChanged;
            }

            if (resolver.TryResolve(out worlds))
            {
                worlds.Built += FrameViewedFloor;
            }
        }

        WorldService worlds;

        void Awake()
        {
            rig = GetComponent<CameraRig>();
        }

        void OnDestroy()
        {
            if (phase != null)
            {
                phase.Changed -= OnPhaseChanged;
            }

            if (worlds != null)
            {
                worlds.Built -= FrameViewedFloor;
            }
        }

        /// <summary>
        /// Slides the camera so the middle of the screen is the middle of what stands on the viewed
        /// floor, keeping its angle and distance.
        /// </summary>
        /// <remarks>
        /// Run when a world is built. The camera otherwise keeps whatever spot the scene was saved
        /// looking at, and a world built away from it opened with its pieces and agents off-screen,
        /// which read as the agents having been lost.
        /// </remarks>
        public void FrameViewedFloor()
        {
            if (rig == null || rig.Steered == null || storeys == null)
            {
                return;
            }

            var floor = storeys.Floor(storeys.Viewed);
            if (floor == null)
            {
                return;
            }

            var have = false;
            var bounds = default(Bounds);

            foreach (var drawn in floor.GetComponentsInChildren<Renderer>())
            {
                if (!have)
                {
                    bounds = drawn.bounds;
                    have = true;
                }
                else
                {
                    bounds.Encapsulate(drawn.bounds);
                }
            }

            if (!have)
            {
                return;   // an empty floor: nothing to look at, so leave the camera where it is
            }

            var camera = rig.Steered;
            var focus = Focus(camera);

            moveStarted = -1f;
            camera.position += new Vector3(bounds.center.x - focus.x, 0f, bounds.center.z - focus.z);

            // Then far enough out that all of it fits, with a margin. Orthographic, so the zoom is
            // half the height on screen: the widest corner across the screen and the highest up it
            // decide it. Never closer than the opening zoom, so a small world is not blown up.
            var lens = Camera.main;
            var aspect = lens != null && lens.aspect > 0f ? lens.aspect : 16f / 9f;
            var needed = 0f;

            for (var i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? bounds.min.x : bounds.max.x,
                    (i & 2) == 0 ? bounds.min.y : bounds.max.y,
                    (i & 4) == 0 ? bounds.min.z : bounds.max.z);

                var onScreen = camera.InverseTransformPoint(corner);
                needed = Mathf.Max(needed, Mathf.Abs(onScreen.y), Mathf.Abs(onScreen.x) / aspect);
            }

            rig.TargetZoom = Mathf.Max(rig.StartZoom, needed * fitMargin);
        }

        /// <summary>
        /// Eases the camera across, keeping its angle and height, so <paramref name="point"/> is in
        /// the middle of the screen, and out far enough that <paramref name="halfSize"/> metres
        /// either side of it are in view.
        /// </summary>
        /// <remarks>
        /// For showing where something is about to happen - a room being placed for the player.
        /// Never zooms in: whatever was in view stays in view.
        /// </remarks>
        public void PanTo(Vector3 point, float halfSize)
        {
            if (rig == null || rig.Steered == null)
            {
                return;
            }

            var camera = rig.Steered;
            var focus = Focus(camera);
            var across = new Vector3(point.x - focus.x, 0f, point.z - focus.z);

            MoveTo(camera.position + across, camera.rotation, false);

            var lens = Camera.main;
            var aspect = lens != null && lens.aspect > 0f ? lens.aspect : 16f / 9f;
            rig.TargetZoom = Mathf.Max(rig.TargetZoom, halfSize * fitMargin, halfSize * fitMargin / aspect);
        }

        /// <summary>How long a move by <see cref="Choose"/> or <see cref="PanTo"/> takes.</summary>
        public float MoveSeconds => moveSeconds;

        [Tooltip("How much room to leave around a world framed on load: 1.15 is 15% either side.")]
        [SerializeField] float fitMargin = 1.15f;

        /// <summary>
        /// Moves the camera to <paramref name="view"/>. False when nothing changed: the inventory is
        /// closed, there is no camera, or a piece is in hand (moving the view under a dragged piece
        /// would throw it somewhere the player did not aim).
        /// </summary>
        public bool Choose(CameraView view)
        {
            if (phase == null || !phase.IsEdit || rig == null || rig.Steered == null)
            {
                return false;
            }

            if (placement != null && placement.IsBusy)
            {
                Debug.Log("[Views] Put the piece down before changing the view.", this);
                return false;
            }

            if (view == Current)
            {
                return false;
            }

            var camera = rig.Steered;
            var focus = Focus(camera);

            if (Current == CameraView.Perspective)
            {
                // Remembered as an angle and a distance, not a place: by the time the player comes
                // back they may have panned, or moved to another floor, and the way back is to the
                // spot they are looking at then.
                perspectiveRotation = camera.rotation;
                perspectiveDistance = Vector3.Distance(camera.position, focus);
                havePerspective = true;
            }

            if (IsSide(view) && painter != null && painter.IsActive)
            {
                painter.Cancel();
            }

            Quaternion rotation;
            Vector3 position;
            Pose(view, focus, out position, out rotation);

            Current = view;
            Apply();
            MoveTo(position, rotation, false);

            var handler = Changed;
            if (handler != null)
            {
                handler(view);
            }

            return true;
        }

        /// <summary>Where <paramref name="view"/> puts the camera to look at <paramref name="focus"/>.</summary>
        void Pose(CameraView view, Vector3 focus, out Vector3 position, out Quaternion rotation)
        {
            if (view == CameraView.Perspective)
            {
                rotation = havePerspective ? perspectiveRotation : rig.Steered.rotation;
                var distance = havePerspective ? perspectiveDistance : standOff;
                position = focus - rotation * Vector3.forward * distance;
                return;
            }

            rotation = RotationOf(view);

            var target = view == CameraView.Top ? focus : focus + Vector3.up * sideEyeHeight;
            position = target - rotation * Vector3.forward * standOff;
        }

        /// <summary>
        /// The spot of the viewed floor in the middle of the screen: where the camera's forward
        /// meets the floor, or - looking level, from the side - the point it looks at, dropped to
        /// the floor.
        /// </summary>
        Vector3 Focus(Transform camera)
        {
            var floor = storeys != null ? storeys.ViewedBase : 0f;
            var forward = camera.forward;

            if (forward.y < -0.05f)
            {
                var along = (camera.position.y - floor) / -forward.y;
                return camera.position + forward * along;
            }

            var ahead = camera.position + forward * standOff;
            return new Vector3(ahead.x, floor, ahead.z);
        }

        /// <summary>Sets the floors, the rig and the tools for the view now in force.</summary>
        void Apply()
        {
            var fixedView = Current != CameraView.Perspective;
            var lookOnly = IsLookOnly;

            if (storeys != null)
            {
                storeys.SoloViewed = fixedView;
            }

            rig.FixedView = fixedView;

            if (painter != null)
            {
                painter.Blocked = lookOnly;
            }

            if (placement != null)
            {
                placement.ViewBlocked = lookOnly;
            }
        }

        void MoveTo(Vector3 position, Quaternion rotation, bool immediately)
        {
            var camera = rig.Steered;

            if (immediately || moveSeconds <= 0f)
            {
                moveStarted = -1f;
                camera.SetPositionAndRotation(position, rotation);
                return;
            }

            fromPosition = camera.position;
            fromRotation = camera.rotation;
            toPosition = position;
            toRotation = rotation;
            moveStarted = Time.unscaledTime;
        }

        void LateUpdate()
        {
            if (moveStarted < 0f || rig == null || rig.Steered == null)
            {
                return;
            }

            var t = Mathf.Clamp01((Time.unscaledTime - moveStarted) / moveSeconds);
            var eased = t * t * (3f - 2f * t);

            rig.Steered.SetPositionAndRotation(
                Vector3.LerpUnclamped(fromPosition, toPosition, eased),
                Quaternion.SlerpUnclamped(fromRotation, toRotation, eased));

            if (t >= 1f)
            {
                moveStarted = -1f;
            }
        }

        /// <summary>
        /// Closing the inventory goes back to Perspective at once, before the follow camera copies
        /// the free camera's angle.
        /// </summary>
        void OnPhaseChanged(GamePhase now)
        {
            if (now == GamePhase.Edit || Current == CameraView.Perspective)
            {
                return;
            }

            if (rig != null && rig.Steered != null)
            {
                Vector3 position;
                Quaternion rotation;
                Pose(CameraView.Perspective, Focus(rig.Steered), out position, out rotation);
                MoveTo(position, rotation, true);
            }

            Current = CameraView.Perspective;

            if (rig != null)
            {
                Apply();
            }

            var handler = Changed;
            if (handler != null)
            {
                handler(Current);
            }
        }
    }
}
