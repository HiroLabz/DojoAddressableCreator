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

        public RoomEntry Room { get; private set; }

        /// <summary>Raised with the card's room when it is clicked.</summary>
        public event Action<RoomEntry> Picked;

        void Awake()
        {
            if (button != null)
            {
                button.onClick.AddListener(() => Picked?.Invoke(Room));
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
