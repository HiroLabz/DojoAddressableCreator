using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Game.UI
{
    /// <summary>
    /// One department in the inventory's Departments tab: its name, what of the building it holds,
    /// and the agents assigned to it.
    /// </summary>
    /// <remarks>
    /// A summary and nothing to press. The inventory is the Edit phase, where the camera is held,
    /// so flying to a department is left to the game's in-game menu.
    /// </remarks>
    public sealed class DepartmentRow : MonoBehaviour
    {
        [SerializeField] TextMeshProUGUI nameLabel;

        [Tooltip("What it holds: its areas and floors.")]
        [SerializeField] TextMeshProUGUI detailLabel;

        [Tooltip("The agents assigned to it, by name, or that there are none.")]
        [SerializeField] TextMeshProUGUI agentsLabel;

        [Tooltip("The assigned agent's picture, inside its frame. Hidden, leaving the empty frame, when nobody is assigned.")]
        [SerializeField] Image portrait;

        [Tooltip("The agents line when someone is assigned.")]
        [SerializeField] Color assignedColor = new Color32(93, 227, 164, 255);

        [Tooltip("The agents line when nobody is.")]
        [SerializeField] Color unassignedColor = new Color32(128, 148, 170, 255);

        /// <summary>How many agents are named before the rest are counted.</summary>
        const int NamedAgents = 4;

        /// <param name="picture">The first assigned agent's picture, or null.</param>
        public void Bind(DepartmentLine line, Sprite picture = null)
        {
            if (portrait != null)
            {
                portrait.sprite = picture;
                portrait.preserveAspect = true;
                portrait.enabled = picture != null && line.Agents > 0;
            }

            if (nameLabel != null)
            {
                nameLabel.text = line.Name;
            }

            if (detailLabel != null)
            {
                detailLabel.text = DepartmentSummary.Describe(line.Areas, line.Floors);
            }

            if (agentsLabel != null)
            {
                agentsLabel.text = DepartmentSummary.AgentsLine(line.AgentNames, NamedAgents);
                agentsLabel.color = line.Agents > 0 ? assignedColor : unassignedColor;
            }
        }
    }
}
