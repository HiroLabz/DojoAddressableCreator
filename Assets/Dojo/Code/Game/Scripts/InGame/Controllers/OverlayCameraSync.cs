using UnityEngine;

namespace Dojo.Game.InGame.Controllers
{
    /// <summary>
    /// Keeps a stacked overlay camera looking through the same lens as the camera it is stacked on.
    /// </summary>
    /// <remarks>
    /// A URP overlay camera renders the world with its own projection, and <see cref="CameraRig"/>
    /// zooms by changing <c>orthographicSize</c> every frame. Left to itself the overlay would keep
    /// whatever size it was authored with, so a piece being dragged out of the inventory would slide
    /// away from the floor under it the moment the player touched the wheel. Being a child of the
    /// base camera takes care of position and rotation; only the lens has to be copied.
    /// <para>
    /// Copied in LateUpdate, after the rig has moved and zoomed for the frame and before anything
    /// renders.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(Camera))]
    public sealed class OverlayCameraSync : MonoBehaviour
    {
        [Tooltip("Camera to copy the lens from. Left empty, the camera on the parent object is " +
                 "used, which is the one this overlay is stacked on.")]
        [SerializeField] Camera source;

        Camera mine;

        void Awake()
        {
            mine = GetComponent<Camera>();

            if (source == null && transform.parent != null)
            {
                source = transform.parent.GetComponent<Camera>();
            }

            if (source == null)
            {
                Debug.LogError("[Camera] " + name + " has nothing to copy its lens from. Parent it "
                    + "to the camera it is stacked on, or assign one.", this);
            }
        }

        void LateUpdate()
        {
            if (source == null || mine == null)
            {
                return;
            }

            mine.orthographic = source.orthographic;
            mine.orthographicSize = source.orthographicSize;
            mine.fieldOfView = source.fieldOfView;
            mine.nearClipPlane = source.nearClipPlane;
            mine.farClipPlane = source.farClipPlane;
            mine.rect = source.rect;
        }
    }
}
