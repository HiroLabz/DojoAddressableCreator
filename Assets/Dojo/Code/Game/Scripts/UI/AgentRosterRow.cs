using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Game.UI
{
    /// <summary>
    /// One agent in the roster list: an avatar carrying the agent's initials, its name, what it
    /// does, and whether it is the one the card below is showing.
    /// </summary>
    /// <remarks>
    /// A wide row rather than the square tile <see cref="AgentDisplayUI"/> builds for its strip.
    /// An agent is not distinguishable from a thumbnail the way a sofa is — what tells two apart is
    /// the name and the job, and neither fits in a tile.
    /// <para>
    /// The avatar's colour comes from <see cref="AgentDisplayUI.TintFor"/> rather than from
    /// anything here, so a row and the agent's pieces out on the floor are always the same colour.
    /// </para>
    /// </remarks>
    public sealed class AgentRosterRow : MonoBehaviour
    {
        [Header("Row")]
        [Tooltip("The whole row. Clicking it selects this agent.")]
        [SerializeField] Button button;

        [Header("Avatar")]
        [Tooltip("The coloured plate behind the initials.")]
        [SerializeField] Image avatar;

        [Tooltip("Up to two letters taken from the agent's name.")]
        [SerializeField] TextMeshProUGUI initials;

        [Header("Labels")]
        [Tooltip("The agent's name.")]
        [SerializeField] TextMeshProUGUI nameLabel;

        [Tooltip("What it does, e.g. SUPPORT - DOCUMENTATION.")]
        [SerializeField] TextMeshProUGUI subtitleLabel;

        [Header("States")]
        [Tooltip("Shown only on the row the card is showing.")]
        [SerializeField] GameObject selectedState;

        [Tooltip("Marks an agent that has an area painted but not yet committed.")]
        [SerializeField] GameObject draftMarker;

        /// <summary>Raised with this row's agent id when the player picks it.</summary>
        public event Action<int> Clicked;

        /// <summary>The agent this row stands for.</summary>
        public int AgentId { get; private set; }

        void Awake()
        {
            if (button != null)
            {
                button.onClick.AddListener(Raise);
            }
        }

        void OnDestroy()
        {
            if (button != null)
            {
                button.onClick.RemoveListener(Raise);
            }
        }

        /// <summary>Fills the row in. Everything arrives together, as the other tiles do.</summary>
        public void Bind(int agentId, string displayName, string subtitle, bool draft)
        {
            AgentId = agentId;

            if (nameLabel != null)
            {
                nameLabel.text = displayName ?? string.Empty;
            }

            if (subtitleLabel != null)
            {
                subtitleLabel.text = (subtitle ?? string.Empty).ToUpperInvariant();
            }

            if (initials != null)
            {
                initials.text = InitialsOf(displayName);
            }

            if (avatar != null)
            {
                avatar.color = AgentDisplayUI.TintFor(agentId);
            }

            if (draftMarker != null)
            {
                draftMarker.SetActive(draft);
            }

            SetSelected(false);
        }

        /// <summary>Lights the row whose agent the card is showing.</summary>
        public void SetSelected(bool selected)
        {
            if (selectedState != null)
            {
                selectedState.SetActive(selected);
            }
        }

        void Raise()
        {
            var handler = Clicked;
            if (handler != null)
            {
                handler(AgentId);
            }
        }

        /// <summary>
        /// The first letter of each of the first two words, so "Lead qualification" reads LQ.
        /// </summary>
        /// <remarks>
        /// Falls back to the first two characters for a one-word name, and to a dash for no name at
        /// all — an empty avatar reads as a failed image rather than as a nameless agent.
        /// </remarks>
        static string InitialsOf(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return "-";
            }

            var words = displayName.Split(new[] { ' ', '\t', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);

            if (words.Length >= 2)
            {
                return (char.ToUpperInvariant(words[0][0]).ToString()
                        + char.ToUpperInvariant(words[1][0]));
            }

            string word = words[0];
            return word.Length >= 2
                ? word.Substring(0, 2).ToUpperInvariant()
                : word.ToUpperInvariant();
        }
    }
}
