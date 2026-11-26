using System;
using Dojo.Game.Placement;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Game.UI
{
    /// <summary>
    /// One floor layout in the FLOORS tab, drawn like a room card: its thumbnail, its name, and its
    /// size in tiles. <see cref="RoomCard"/> for floors.
    /// </summary>
    public sealed class FloorLayoutCard : MonoBehaviour
    {
        [SerializeField] Button button;
        [SerializeField] Image icon;
        [SerializeField] TextMeshProUGUI nameLabel;

        [Tooltip("Columns by rows, in tiles.")]
        [SerializeField] TextMeshProUGUI sizeLabel;

        [Tooltip("Shown while this card's layout is on the cursor.")]
        [SerializeField] GameObject selectedState;

        [Tooltip("Deletes the layout. Only on layouts made here: the pack's own cannot be deleted.")]
        [SerializeField] Button deleteButton;

        [Tooltip("Makes the layout one of the pack's. Only on layouts made here, and only in the " +
                 "editor, where the pack's files can be changed.")]
        [SerializeField] Button promoteButton;

        [Tooltip("Opens the layout in the floor editor, to be saved over itself. On layouts made " +
                 "here, and in the editor on the pack's layouts too.")]
        [SerializeField] Button editButton;

        public FloorLayoutEntry Layout { get; private set; }

        /// <summary>Raised with the card's layout when it is clicked.</summary>
        public event Action<FloorLayoutEntry> Picked;

        /// <summary>Raised with the card's layout when its DELETE is pressed.</summary>
        public event Action<FloorLayoutEntry> DeletePressed;

        /// <summary>Raised with the card's layout when its PROMOTE is pressed.</summary>
        public event Action<FloorLayoutEntry> PromotePressed;

        /// <summary>Raised with the card's layout when its EDIT is pressed.</summary>
        public event Action<FloorLayoutEntry> EditPressed;

        void Awake()
        {
            if (button != null)
            {
                button.onClick.AddListener(() => Picked?.Invoke(Layout));
            }

            if (deleteButton != null)
            {
                deleteButton.onClick.AddListener(() => DeletePressed?.Invoke(Layout));
            }

            if (promoteButton != null)
            {
                promoteButton.onClick.AddListener(() => PromotePressed?.Invoke(Layout));
            }

            if (editButton != null)
            {
                editButton.onClick.AddListener(() => EditPressed?.Invoke(Layout));
            }

            if (icon != null)
            {
                icon.preserveAspect = true;
            }

            SetSelected(false);
        }

        public void Bind(FloorLayoutEntry layout, Sprite thumbnail, Vector2Int size)
        {
            Layout = layout;

            // Only a layout made here can be deleted or promoted; the pack's own show neither.
            var made = layout != null && CustomFloorLayouts.IsCustom(layout.address);

            if (deleteButton != null)
            {
                deleteButton.gameObject.SetActive(made);
            }

            if (promoteButton != null)
            {
                promoteButton.gameObject.SetActive(made && Application.isEditor);
            }

            if (editButton != null)
            {
                editButton.gameObject.SetActive(layout != null && (made || Application.isEditor));
            }

            if (nameLabel != null)
            {
                nameLabel.text = layout != null ? layout.name : string.Empty;
            }

            if (sizeLabel != null)
            {
                sizeLabel.text = size == Vector2Int.zero ? string.Empty : size.x + "×" + size.y;
            }

            if (icon != null)
            {
                icon.sprite = thumbnail;
                icon.enabled = thumbnail != null;
            }
        }

        public void SetSelected(bool on)
        {
            if (selectedState != null && selectedState.activeSelf != on)
            {
                selectedState.SetActive(on);
            }
        }
    }
}
