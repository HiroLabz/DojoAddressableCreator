using Dojo.Game.Placement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VContainer;

namespace Dojo.Game.UI
{
    /// <summary>
    /// One entry in the furniture inventory: the icon the player sees, and the prefab that gets
    /// placed when they drag it into the world.
    /// </summary>
    /// <remarks>
    /// The fields mirror <c>thumbnails.json</c>, written by
    /// <c>Tools ▸ Dojo ▸ Addressable Generator</c>, so a tile can be filled straight from a
    /// manifest entry: <see cref="Id"/> is its <c>prefabId</c>, <see cref="DisplayName"/> its
    /// <c>prefabName</c>, and <see cref="ThumbnailId"/> its <c>thumbnailId</c>.
    /// <para>
    /// <see cref="Id"/> holds the prefab's asset GUID rather than its name or path. Names in the
    /// office pack are display strings like "(Prb)Sofa3" and paths move when folders are tidied;
    /// the GUID survives both, so saved layouts and manifests keep resolving.
    /// </para>
    /// <para>
    /// A tile is also where a placement gesture starts. Click one and the piece leaves the drawer
    /// as a real model already on the cursor, handed to <see cref="PlacementController"/> so the
    /// drop obeys exactly the same rules; the tile turns green for as long as it is in hand. The
    /// thumbnail itself never moves — what follows the cursor is the furniture.
    /// </para>
    /// <para>
    /// A click, not a press: the grid lives in a scrolling viewport, and Unity only reports a click
    /// when the pointer did not travel far enough to be a drag. Dragging therefore still scrolls
    /// the list, and only a tap picks something up.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(RectTransform))]
    public sealed class ItemRenderer3D : MonoBehaviour,
        IPointerClickHandler,
        IBeginDragHandler,
        IDragHandler,
        IEndDragHandler
    {
        [Header("Identity")]
        [Tooltip("The furniture prefab's asset GUID. Matches 'prefabId' in thumbnails.json. Stable " +
                 "across renames and folder moves, which a name or a path is not.")]
        [SerializeField] string id;

        [Tooltip("Name shown to the player. Matches 'prefabName' in thumbnails.json.")]
        [SerializeField] string displayName;

        [Tooltip("The piece this tile places. The link the inventory instantiates from, so a tile " +
                 "needs no lookup table to do its job.")]
        [SerializeField] GameObject prefab;

        [Header("Icon")]
        [Tooltip("Image that shows the generated thumbnail.")]
        [SerializeField] Image icon;

        [Tooltip("GUID of the thumbnail PNG. Matches 'thumbnailId' in thumbnails.json. Kept so a " +
                 "tile can be traced back to the image it was built from.")]
        [SerializeField] string thumbnailId;

        [Tooltip("Resources path the prefab loads from. Matches 'prefabResource' in " +
                 "thumbnails.json. The GUID identifies the piece; this is the only address a " +
                 "running game can actually load it by, so a saved world needs both.")]
        [SerializeField] string resource;

        [Header("Highlight")]
        [Tooltip("The tile's own background. Turned green while this tile's piece is on the cursor. " +
                 "Found on the tile itself when left empty.")]
        [SerializeField] Image background;

        [Tooltip("Colour the tile takes while its piece is in hand.")]
        [SerializeField] Color highlightColour = new Color(0.35f, 1f, 0.55f, 1f);

        PlacementController placement;
        ScrollRect scroller;

        /// <summary>Receives the controller a placement gesture is handed to.</summary>
        /// <remarks>
        /// A tile is made at runtime, one per manifest entry, so it is never in the scene when the
        /// LifetimeScope builds and cannot be injected by enumeration. <see cref="InventoryUI"/>
        /// instantiates it through the container instead, which is what makes this method run.
        /// </remarks>
        [Inject]
        public void Construct(PlacementController controller)
        {
            this.placement = controller;
        }

        // True while this tile's piece is the one on the cursor, which is what the green says.
        bool highlighted;

        // What the tile looked like before it was highlighted, so it can be put back.
        Color restColour;

        /// <summary>Asset GUID of the furniture prefab. The tile's identity everywhere else.</summary>
        public string Id => id;

        /// <summary>Name for the player.</summary>
        public string DisplayName => displayName;

        /// <summary>The prefab this tile places.</summary>
        public GameObject Prefab => prefab;

        /// <summary>Asset GUID of the thumbnail this tile is showing.</summary>
        public string ThumbnailId => thumbnailId;

        /// <summary>Resources path the prefab loads from.</summary>
        public string Resource => resource;

        /// <summary>True once the tile has both an identity and something to place.</summary>
        public bool IsUsable => !string.IsNullOrEmpty(id) && prefab != null;

        void Awake()
        {
            EnforceProportionalIcon();

            // The grid lives inside a scrolling viewport and a drag on a tile belongs to it. Found
            // here rather than serialized: tiles are instantiated into the grid at runtime, and
            // Instantiate parents them before Awake runs.
            scroller = GetComponentInParent<ScrollRect>();

            if (background == null)
            {
                background = GetComponent<Image>();
            }

            if (background != null)
            {
                restColour = background.color;
            }
        }

        /// <summary>
        /// Fills the tile in from a manifest entry. Everything a tile needs arrives together, so a
        /// half-populated tile cannot reach the player.
        /// </summary>
        public void Bind(
            string prefabId,
            string name,
            GameObject piece,
            Sprite iconSprite,
            string thumbnail = null,
            string resourcePath = null)
        {
            id = prefabId;
            displayName = name;
            prefab = piece;
            thumbnailId = thumbnail;
            resource = resourcePath;

            if (icon != null)
            {
                icon.sprite = iconSprite;
                icon.enabled = iconSprite != null;
            }

            EnforceProportionalIcon();
        }

        /// <summary>
        /// Watches for the piece being put down, so the green does not outlive it.
        /// </summary>
        /// <remarks>
        /// Polled rather than told. The gesture can end in half a dozen ways — committed, cancelled
        /// with Esc, right-clicked away, deleted, dropped back over the drawer — and a tile that
        /// learned about only five of them would be left lit with nothing in hand.
        /// </remarks>
        void Update()
        {
            if (!highlighted)
            {
                return;
            }

            var controller = placement;

            if (controller == null || !controller.IsBusy)
            {
                Highlight(false);
            }
        }

        /// <summary>Turns the tile green, or puts it back to the colour it was authored with.</summary>
        void Highlight(bool on)
        {
            highlighted = on;

            if (background != null)
            {
                background.color = on ? highlightColour : restColour;
            }
        }

        /// <summary>
        /// Picks the piece up. Unity raises this only when the pointer stayed put, so a drag across
        /// the tile scrolls the list instead of emptying the drawer onto the floor.
        /// </summary>
        void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !IsUsable)
            {
                return;
            }

            var controller = placement;

            if (controller == null)
            {
                Debug.LogWarning("[Inventory] No PlacementController in the scene, so '" + displayName
                    + "' cannot be taken out.", this);
                return;
            }

            if (!controller.BeginDragFromInventory(prefab, id, resource))
            {
                return;   // refused, and already logged; the click simply comes to nothing
            }

            Highlight(true);
        }

        /// <remarks>
        /// The scroll view is driven by hand rather than left in the event chain, because a tile
        /// that handles clicks also intercepts the drags that would otherwise reach the list behind
        /// it. Everything it would have received is passed straight on.
        /// </remarks>
        void IBeginDragHandler.OnBeginDrag(PointerEventData eventData)
        {
            if (scroller != null)
            {
                scroller.OnBeginDrag(eventData);
            }
        }

        void IDragHandler.OnDrag(PointerEventData eventData)
        {
            if (scroller != null)
            {
                scroller.OnDrag(eventData);
            }
        }

        void IEndDragHandler.OnEndDrag(PointerEventData eventData)
        {
            if (scroller != null)
            {
                scroller.OnEndDrag(eventData);
            }
        }

        /// <summary>
        /// Keeps the icon's proportions honest: <see cref="Image.preserveAspect"/> on, so a
        /// thumbnail is never stretched to fill a tile that is not the same shape as it.
        /// </summary>
        /// <remarks>
        /// Set here as well as on the prefab because it is one checkbox and silently costs nothing
        /// when wrong — a squashed sofa still draws, so nobody notices until the art looks off.
        /// The sibling <see cref="AspectRatioFitter"/> sizes the rect; this stops the sprite being
        /// distorted inside whatever rect it ends up with. The two solve different halves of the
        /// same problem and are both wanted.
        /// </remarks>
        void EnforceProportionalIcon()
        {
            if (icon == null)
            {
                return;
            }

            icon.preserveAspect = true;
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (icon == null)
            {
                // The icon is the only child that draws the thumbnail, so finding it by type is
                // more reliable than expecting whoever built the prefab to have wired it.
                foreach (var candidate in GetComponentsInChildren<Image>(true))
                {
                    if (candidate.gameObject != gameObject)
                    {
                        icon = candidate;
                        break;
                    }
                }
            }

            if (icon != null && !icon.preserveAspect)
            {
                icon.preserveAspect = true;
                UnityEditor.EditorUtility.SetDirty(icon);
            }
        }
#endif
    }
}
