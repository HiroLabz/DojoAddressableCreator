using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Game.UI
{
    /// <summary>
    /// One agent in the roster list: an avatar carrying the agent's picture (its initials when there
    /// is none), its name, what it does, and whether it is the one the card below is showing.
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

        [Tooltip("The agent's picture, inside the plate. Replaces the initials when there is one.")]
        [SerializeField] Image picture;

        [Tooltip("The plate behind a picture. A tint would fight the picture's own colours.")]
        [SerializeField] Color pictureBackdrop = new Color(1f, 1f, 1f, 0.08f);

        [Header("Labels")]
        [Tooltip("The agent's name.")]
        [SerializeField] TextMeshProUGUI nameLabel;

        [Tooltip("What it does, e.g. SUPPORT - DOCUMENTATION.")]
        [SerializeField] TextMeshProUGUI subtitleLabel;

        [Header("States")]
        [Tooltip("Shown only on the row the card is showing.")]
        [SerializeField] GameObject selectedState;

        [Tooltip("Marks an agent that has an area painted but is not in the world yet.")]
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
        /// <param name="portrait">The agent's picture, or null to show its initials.</param>
        public void Bind(int agentId, string displayName, string subtitle, bool draft, Sprite portrait = null)
        {
            bool pictured = portrait != null && picture != null;
            AgentId = agentId;

            if (nameLabel != null)
            {
                nameLabel.text = displayName ?? string.Empty;
            }

            if (subtitleLabel != null)
            {
                subtitleLabel.text = (subtitle ?? string.Empty).ToUpperInvariant();
            }

            if (picture != null)
            {
                picture.sprite = portrait;
                picture.preserveAspect = true;
                picture.enabled = pictured;
            }

            if (initials != null)
            {
                initials.text = InitialsOf(displayName);
                initials.enabled = !pictured;
            }

            if (avatar != null)
            {
                avatar.color = pictured ? pictureBackdrop : AgentDisplayUI.TintFor(agentId);
            }

            SetDraft(draft);
            SetSelected(false);
        }

        /// <summary>Lights or clears the DRAFT flag, as the agent goes into the world.</summary>
        public void SetDraft(bool draft)
        {
            if (draftMarker != null)
            {
                draftMarker.SetActive(draft);
            }
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
        /// all — an empty avatar reads as a failed image rather than as a nameless agent. The Agent
        /// Passport uses it too, so the two show the same letters.
        /// </remarks>
        public static string InitialsOf(string displayName)
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
