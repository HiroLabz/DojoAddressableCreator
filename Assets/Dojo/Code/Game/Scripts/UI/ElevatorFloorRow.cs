using System;
using Dojo.Framework.UI;
using Dojo.Game.Placement;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Dojo.Game.UI
{
    /// <summary>
    /// One key in the elevator panel: a floor - its number, its name, what is on it - or one of a
    /// floor's block areas, listed under it. A word on the right says how it stands: the floor the
    /// manager is on, or where he is heading.
    /// </summary>
    /// <remarks>
    /// The row is its outline, as in the mock-up: an unbroken edge round the cut corners, lit green
    /// on the floor he is on and cyan on where he has been sent. Painted here rather than with a
    /// button's colour tint, because each look changes half a dozen parts at once.
    /// <para>
    /// A floor's row sends him to the floor, to the elevator's doors there; an area's row sends him
    /// on into that area. An area's row has no second line, so its name sits in the middle. The
    /// panel copies this row, from the Game scene, once per floor and once per area.
    /// </para>
    /// </remarks>
    public sealed class ElevatorFloorRow : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        /// <summary>How a floor or an area stands, which decides how its row looks.</summary>
        public enum Look
        {
            /// <summary>Somewhere he can be sent.</summary>
            Normal,

            /// <summary>The floor the manager is on.</summary>
            Here,

            /// <summary>Where he has just been sent.</summary>
            Heading,
        }

        [SerializeField] Button button;

        [Tooltip("The light round the row, on the floor he is on and where he is heading.")]
        [SerializeField] ChamferShape glow;

        [SerializeField] ChamferShape fill;

        [Tooltip("A wash of colour from the left, with the glow.")]
        [SerializeField] ChamferShape tint;

        [SerializeField] ChamferShape innerGlow;
        [SerializeField] ChamferShape edge;
        [SerializeField] ChamferShape keyFill;
        [SerializeField] ChamferShape keyEdge;

        [Tooltip("The face of the key: 3F.")]
        [SerializeField] TMP_Text key;

        [Tooltip("Floor 3, or the area's name.")]
        [SerializeField] TMP_Text title;

        [Tooltip("What is on the floor: 34 PIECES · 2 AGENTS. Empty, and hidden, for an area.")]
        [SerializeField] TMP_Text details;

        [Tooltip("YOU ARE HERE or HEADING THERE, on the right.")]
        [SerializeField] TMP_Text tag;

        static readonly Color Cyan = Rgb(103, 232, 249);
        static readonly Color Green = Rgb(93, 227, 164);
        static readonly Color Ink = Rgb(219, 233, 255);
        static readonly Color Mute = Rgb(111, 140, 184);
        static readonly Color KeyBlack = Rgb(3, 9, 15);

        Look look;
        bool hovered;

        // Where the name sits over the second line, kept so a row used for an area and then a
        // floor puts it back.
        bool measured;
        Vector2 titleMin, titleMax, titleOffsetMin, titleOffsetMax;
        TextAlignmentOptions titleAlignment;

        /// <summary>This row was picked.</summary>
        public event Action<ElevatorFloorRow> Picked;

        /// <summary>The floor this row stands for, or its area is on, from 0.</summary>
        public int Floor { get; private set; }

        /// <summary>The area this row stands for, or null for a floor's own row.</summary>
        public BlockArea Area { get; private set; }

        /// <summary>Whether it can be picked. Off on the floor he is on, and while the panel is on its way out.</summary>
        public bool Interactable
        {
            set
            {
                if (button != null)
                {
                    button.interactable = value;
                }

                if (!value)
                {
                    hovered = false;
                    Paint();
                }
            }
        }

        void Awake()
        {
            if (button != null)
            {
                button.onClick.AddListener(Press);
            }
        }

        void OnDestroy()
        {
            if (button != null)
            {
                button.onClick.RemoveListener(Press);
            }
        }

        void Press()
        {
            var handler = Picked;
            if (handler != null)
            {
                handler(this);
            }
        }

        /// <summary>Makes this a floor's row, and says how the floor stands.</summary>
        public void SetFloor(int floor, string face, string name, string contents, Look how)
        {
            Show(floor, null, face, name, contents, how);
        }

        /// <summary>Makes this the row of one of a floor's areas: its floor's number and its name, nothing more.</summary>
        public void SetArea(int floor, BlockArea area, string face, Look how)
        {
            Show(floor, area, face, area != null ? area.Name : string.Empty, string.Empty, how);
        }

        void Show(int floor, BlockArea area, string face, string name, string contents, Look how)
        {
            Floor = floor;
            Area = area;

            if (key != null) key.text = face;
            if (title != null) title.text = name;

            var second = !string.IsNullOrEmpty(contents);

            if (details != null)
            {
                details.text = contents;
                details.gameObject.SetActive(second);
            }

            Centre(!second);

            look = how;
            hovered = false;
            Paint();
        }

        /// <summary>The name in the middle of the row when there is no second line, or back over it.</summary>
        void Centre(bool centred)
        {
            if (title == null)
            {
                return;
            }

            var rect = title.rectTransform;

            if (!measured)
            {
                titleMin = rect.anchorMin;
                titleMax = rect.anchorMax;
                titleOffsetMin = rect.offsetMin;
                titleOffsetMax = rect.offsetMax;
                titleAlignment = title.alignment;
                measured = true;
            }

            if (centred)
            {
                rect.anchorMin = new Vector2(titleMin.x, 0f);
                rect.anchorMax = new Vector2(titleMax.x, 1f);
                rect.offsetMin = new Vector2(titleOffsetMin.x, 0f);
                rect.offsetMax = new Vector2(titleOffsetMax.x, 0f);
                title.alignment = TextAlignmentOptions.MidlineLeft;
            }
            else
            {
                rect.anchorMin = titleMin;
                rect.anchorMax = titleMax;
                rect.offsetMin = titleOffsetMin;
                rect.offsetMax = titleOffsetMax;
                title.alignment = titleAlignment;
            }
        }

        /// <summary>Changes only how it stands - the row picked becomes where he is heading.</summary>
        public void SetLook(Look how)
        {
            look = how;
            Paint();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            hovered = button == null || button.interactable;
            Paint();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            hovered = false;
            Paint();
        }

        /// <summary>Every part, for the look and the hover. The colours are the mock-up's.</summary>
        void Paint()
        {
            var lit = look == Look.Here || look == Look.Heading;
            var light = look == Look.Here ? Green : Cyan;

            // Only a plain row lights up under the pointer: the lit ones are lit already.
            var hot = hovered && !lit;

            Show(glow, lit);
            Show(tint, lit);
            Show(innerGlow, lit);

            if (lit)
            {
                Solid(glow, light, 0.5f);
                Gradient(tint, light, 0.26f, 0.08f, horizontal: true);
                Solid(innerGlow, light, look == Look.Here ? 0.3f : 0.28f);
            }

            if (look == Look.Here)
            {
                Solid(edge, Green, 0.9f);
                Gradient(fill, Rgb(16, 44, 42), Rgb(10, 28, 35));
            }
            else if (look == Look.Heading)
            {
                Solid(edge, Cyan, 0.75f);
                Gradient(fill, Rgb(18, 49, 76), Rgb(10, 30, 51));
            }
            else
            {
                Solid(edge, Cyan, hot ? 0.75f : 0.42f);
                Gradient(fill, hot ? Rgb(21, 48, 74) : Rgb(11, 28, 49), hot ? Rgb(11, 30, 51) : Rgb(7, 19, 35));
            }

            // The key: black with a cyan rim, or glassy in the row's light when lit.
            if (lit)
            {
                Solid(keyFill, light, 0.16f);
                Solid(keyEdge, light, look == Look.Here ? 0.8f : 0.75f);
            }
            else
            {
                Solid(keyFill, KeyBlack, 1f);
                Solid(keyEdge, Cyan, 0.45f);
            }

            if (key != null)
            {
                key.color = look == Look.Here ? Rgb(239, 255, 247)
                    : look == Look.Heading ? Rgb(234, 255, 255)
                    : Cyan;
            }

            if (title != null)
            {
                title.color = Ink;
            }

            if (details != null)
            {
                details.color = look == Look.Here ? new Color(170f / 255f, 240f / 255f, 210f / 255f, 0.75f) : Mute;
            }

            if (tag != null)
            {
                switch (look)
                {
                    case Look.Here:
                        tag.text = "YOU ARE HERE";
                        tag.color = Green;
                        break;

                    case Look.Heading:
                        tag.text = "HEADING THERE";
                        tag.color = Cyan;
                        break;

                    default:
                        tag.text = string.Empty;
                        break;
                }

                tag.gameObject.SetActive(tag.text.Length > 0);
            }
        }

        static void Show(Graphic part, bool shown)
        {
            if (part != null)
            {
                part.enabled = shown;
            }
        }

        static void Solid(ChamferShape part, Color colour, float alpha)
        {
            if (part != null)
            {
                part.SetSolid(new Color(colour.r, colour.g, colour.b, alpha));
            }
        }

        static void Gradient(ChamferShape part, Color colour, float startAlpha, float endAlpha, bool horizontal)
        {
            if (part != null)
            {
                part.SetGradient(
                    new Color(colour.r, colour.g, colour.b, startAlpha),
                    new Color(colour.r, colour.g, colour.b, endAlpha),
                    horizontal);
            }
        }

        static void Gradient(ChamferShape part, Color start, Color end)
        {
            if (part != null)
            {
                part.SetGradient(start, end);
            }
        }

        static Color Rgb(int r, int g, int b) => new Color32((byte)r, (byte)g, (byte)b, 255);
    }
}
