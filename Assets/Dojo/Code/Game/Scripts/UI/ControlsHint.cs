using System.Collections.Generic;
using System.Text;
using Dojo.Game.Placement;
using Dojo.Game.Systems;
using TMPro;
using UnityEngine;
using VContainer;

namespace Dojo.Game.UI
{
    /// <summary>
    /// The one-line guide at the top of the game screen: which buttons do what, for whatever the
    /// player is doing right now.
    /// </summary>
    /// <remarks>
    /// Styled like the Lobby's <c>[   OFFICE SIMULATION   ]</c> eyebrow - the same mono face,
    /// spacing and brackets - with each key in the signal blue and what it does in the soft ink.
    /// <para>
    /// Four lines, not two. Edit phase is really three: browsing the room, holding a piece, and
    /// painting an agent's area, and the same buttons mean different things in each - the right
    /// button cancels a piece but finishes a painted area. A single Edit line would be wrong for
    /// two of the three.
    /// </para>
    /// <para>
    /// Every binding here is read from the code that owns it: <c>CameraRig</c> and
    /// <c>MainCinemachineCamera</c> for Play, <c>PlacementController</c> for the furniture, and
    /// <c>AreaPainter</c> for areas. Change a binding there and change its line here.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(TMP_Text))]
    public sealed class ControlsHint : MonoBehaviour
    {
        /// <summary>What the player is doing, as far as the controls are concerned.</summary>
        public enum Context
        {
            /// <summary>Play phase: looking around, and sending the manager places.</summary>
            Play,

            /// <summary>Edit phase with nothing in hand.</summary>
            Edit,

            /// <summary>Edit phase with a piece in hand, dragged or waiting to be put down.</summary>
            Placing,

            /// <summary>Edit phase, painting an agent's area.</summary>
            Painting,
        }

        [Tooltip("Found in the scene if left empty.")]
        [SerializeField] PlacementController placement;

        [Tooltip("Found in the scene if left empty.")]
        [SerializeField] AreaPainter painter;

        [SerializeField] Color keyColour = new Color(0.184f, 0.561f, 1f, 1f);      // #2F8FFF
        [SerializeField] Color actionColour = new Color(0.624f, 0.698f, 0.8f, 1f); // #9FB2CC

        IGamePhase phase;
        TMP_Text label;
        Context shown;
        bool shownWithManager;
        bool shownTouch;
        bool drawn;

        /// <summary>Says whether there is a manager, which two of the Play steps need.</summary>
        InGame.Controllers.MainCinemachineCamera follow;

        [Inject]
        public void Construct(IGamePhase gamePhase)
        {
            phase = gamePhase;
        }

        void Awake()
        {
            label = GetComponent<TMP_Text>();
            label.raycastTarget = false;   // a guide, never something in the way of a click
        }

        void Start()
        {
            if (placement == null)
            {
                placement = FindAnyObjectByType<PlacementController>();
            }

            if (painter == null)
            {
                painter = FindAnyObjectByType<AreaPainter>();
            }

            follow = FindAnyObjectByType<InGame.Controllers.MainCinemachineCamera>();
        }

        /// <summary>Checked every frame, redrawn only when the context changes.</summary>
        void Update()
        {
            var now = Pick(
                phase != null && phase.IsEdit,
                placement != null && placement.IsBusy,
                painter != null && painter.IsActive);

            var withManager = follow != null && follow.Target != null;
            var touch = InGame.Controllers.CameraRig.UsesTouch;

            if (drawn && now == shown && withManager == shownWithManager && touch == shownTouch)
            {
                return;
            }

            shown = now;
            shownWithManager = withManager;
            shownTouch = touch;
            drawn = true;
            label.text = Format(Steps(now, withManager, touch), keyColour, actionColour);
        }

        /// <summary>Which line applies. Painting wins over a piece in hand; Play ignores both.</summary>
        public static Context Pick(bool edit, bool placing, bool painting)
        {
            if (!edit)
            {
                return Context.Play;
            }

            if (painting)
            {
                return Context.Painting;
            }

            return placing ? Context.Placing : Context.Edit;
        }

        /// <summary>The key and what it does, for each thing worth knowing in this context.</summary>
        /// <remarks>
        /// Without a manager, Play leaves out CLICK WALK and F RECENTRE: both act on him, and a key
        /// the guide offers that then does nothing reads as broken.
        /// <para>
        /// On a touchscreen only the camera gestures are listed - two-finger drag, twist and pinch
        /// (see <c>CameraRig.UpdateTouch</c>). Edit keeps the camera to panning, so it lists only
        /// that. The mouse and keyboard lines would name buttons a phone does not have.
        /// </para>
        /// </remarks>
        public static IReadOnlyList<KeyValuePair<string, string>> Steps(Context context, bool withManager = true,
            bool touch = false)
        {
            if (touch)
            {
                return context == Context.Play
                    ? Pairs("2-FINGER DRAG", "PAN", "TWIST", "ROTATE", "PINCH", "ZOOM")
                    : Pairs("2-FINGER DRAG", "PAN");
            }

            if (context == Context.Play && !withManager)
            {
                return Pairs(
                    "MIDDLE-DRAG", "PAN",
                    "RIGHT-DRAG", "ROTATE",
                    "WHEEL", "ZOOM");
            }

            switch (context)
            {
                case Context.Edit:
                    return Pairs(
                        "DRAG FROM INVENTORY", "PLACE",
                        "DRAG A PIECE", "MOVE",
                        "CTRL+CLICK", "SELECT",
                        "DEL", "DELETE");

                case Context.Placing:
                    return Pairs(
                        "CLICK", "PUT DOWN",
                        "WHEEL", "TURN",
                        "RIGHT-CLICK", "CANCEL",
                        "DEL", "DELETE");

                case Context.Painting:
                    return Pairs(
                        "DRAG", "PAINT",
                        "CTRL+DRAG", "ERASE",
                        "WHEEL", "BRUSH SIZE",
                        "RIGHT-CLICK", "DONE",
                        "ESC", "CANCEL");

                default:
                    return Pairs(
                        "MIDDLE-DRAG", "PAN",
                        "RIGHT-DRAG", "ROTATE",
                        "WHEEL", "ZOOM",
                        "CLICK", "WALK",
                        "F", "RECENTRE");
            }
        }

        /// <summary>The line as rich text: <c>[   KEY action   ·   KEY action   ]</c>.</summary>
        public static string Format(IReadOnlyList<KeyValuePair<string, string>> steps, Color key, Color action)
        {
            var keyHex = "#" + ColorUtility.ToHtmlStringRGB(key);
            var actionHex = "#" + ColorUtility.ToHtmlStringRGB(action);
            var text = new StringBuilder("<color=" + keyHex + ">[</color>   ");

            for (var i = 0; i < steps.Count; i++)
            {
                if (i > 0)
                {
                    text.Append("   <color=" + actionHex + ">·</color>   ");
                }

                text.Append("<color=" + keyHex + ">" + steps[i].Key + "</color> ");
                text.Append("<color=" + actionHex + ">" + steps[i].Value + "</color>");
            }

            return text.Append("   <color=" + keyHex + ">]</color>").ToString();
        }

        static IReadOnlyList<KeyValuePair<string, string>> Pairs(params string[] flat)
        {
            var pairs = new List<KeyValuePair<string, string>>();

            for (var i = 0; i + 1 < flat.Length; i += 2)
            {
                pairs.Add(new KeyValuePair<string, string>(flat[i], flat[i + 1]));
            }

            return pairs;
        }
    }
}
