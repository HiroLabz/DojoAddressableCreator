using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Game.UI
{
    /// <summary>
    /// One block area in the Areas tab: its name, who uses it, and the three things that can be
    /// done to it - paint it again, rename it, delete it.
    /// </summary>
    /// <remarks>
    /// Says what was pressed and for which area, and decides nothing: <see cref="AreasDisplayUI"/>
    /// owns every rule about painting and asking.
    /// </remarks>
    public sealed class AreaRow : MonoBehaviour
    {
        [SerializeField] TextMeshProUGUI nameLabel;

        [Tooltip("Who uses the area - the manager, some agents, or nobody.")]
        [SerializeField] TextMeshProUGUI detailLabel;

        [SerializeField] Button repaintButton;
        [SerializeField] Button renameButton;
        [SerializeField] Button deleteButton;

        /// <summary>The area this row stands for.</summary>
        public int AreaId { get; private set; }

        public event Action<int> RepaintPressed;
        public event Action<int> RenamePressed;
        public event Action<int> DeletePressed;

        void Awake()
        {
            if (repaintButton != null) repaintButton.onClick.AddListener(() => RepaintPressed?.Invoke(AreaId));
            if (renameButton != null) renameButton.onClick.AddListener(() => RenamePressed?.Invoke(AreaId));
            if (deleteButton != null) deleteButton.onClick.AddListener(() => DeletePressed?.Invoke(AreaId));
        }

        public void Bind(int areaId, string areaName, string detail)
        {
            AreaId = areaId;

            if (nameLabel != null)
            {
                nameLabel.text = areaName;
            }

            if (detailLabel != null)
            {
                detailLabel.text = detail;
            }
        }

        /// <summary>Whether the buttons answer: off while an area is being painted.</summary>
        public void SetInteractable(bool on)
        {
            if (repaintButton != null) repaintButton.interactable = on;
            if (renameButton != null) renameButton.interactable = on;
            if (deleteButton != null) deleteButton.interactable = on;
        }
    }
}
