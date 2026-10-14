using System;
using Dojo.Game.Placement;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Game.UI
{
    /// <summary>
    /// One room in the Rooms tab, drawn like a floor or wall tile: its thumbnail, its name, and
    /// its size. A click puts the room on the cursor, and the card stays lit while it is there.
    /// </summary>
    public sealed class RoomCard : MonoBehaviour
    {
        [SerializeField] Button button;
        [SerializeField] Image icon;
        [SerializeField] TextMeshProUGUI nameLabel;

        [Tooltip("Width by depth, in metres.")]
        [SerializeField] TextMeshProUGUI sizeLabel;

        [Tooltip("Shown while this card's room is on the cursor.")]
        [SerializeField] GameObject selectedState;

        [Tooltip("Deletes the room. Only on rooms the player made: the pack's own cannot be deleted.")]
        [SerializeField] Button deleteButton;

        public RoomEntry Room { get; private set; }

        /// <summary>Raised with the card's room when it is clicked.</summary>
        public event Action<RoomEntry> Picked;

        /// <summary>Raised with the card's room when its DELETE is pressed.</summary>
        public event Action<RoomEntry> DeletePressed;

        void Awake()
        {
            if (button != null)
            {
                button.onClick.AddListener(() => Picked?.Invoke(Room));
            }

            if (deleteButton != null)
            {
                deleteButton.onClick.AddListener(() => DeletePressed?.Invoke(Room));
            }

            if (icon != null)
            {
                icon.preserveAspect = true;
            }

            SetSelected(false);
        }

        public void Bind(RoomEntry room, Sprite thumbnail, Vector2 size)
        {
            Room = room;

            // Only a room the player made can be deleted; the pack's own never show the button.
            if (deleteButton != null)
            {
                deleteButton.gameObject.SetActive(room != null && CustomRooms.IsCustom(room.address));
            }

            if (nameLabel != null)
            {
                nameLabel.text = room != null ? room.name : string.Empty;
            }

            if (sizeLabel != null)
            {
                sizeLabel.text = size == Vector2.zero
                    ? string.Empty
                    : Mathf.RoundToInt(size.x) + "×" + Mathf.RoundToInt(size.y) + "m";
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
