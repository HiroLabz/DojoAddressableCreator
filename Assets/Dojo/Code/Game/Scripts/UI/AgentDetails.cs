using System;
using Dojo.Framework.UI;
using Dojo.Game.InGame.Agents;
using TMPro;
using UnityEngine;

namespace Dojo.Game.UI
{
    /// <summary>
    /// Slide-in panel showing what one agent is currently up to. The sliding, the toggle button
    /// and the remembered closed position all come from <see cref="InGamePopup"/>; this only adds
    /// the labels and which agent they describe.
    /// </summary>
    public sealed class AgentDetails : InGamePopup
    {
        [Header("Details")]
        [Tooltip("Shows the agent's name.")]
        [SerializeField] TMP_Text titleLabel;

        [Tooltip("Shows what the agent is doing. Handed over to the ticker while it is working.")]
        [SerializeField] TMP_Text statusLabel;

        [Tooltip("Rolling readout shown while the agent is seated and working.")]
        [SerializeField] RunningTextEffect taskTicker;

        [Tooltip("One is picked at random when a loafing agent is inspected.")]
        [SerializeField] string[] scoldMessages =
        {
            "Not at your desk again? Back to work.",
            "I don't pay you to wander the floor.",
            "Is this what I walked over here to see?",
            "Chair. Desk. Work. In that order.",
        };

        [Tooltip("Which agent to describe on start. Optional - ShowFor() overrides it.")]
        [SerializeField] AgentRoutine agent;

        [Tooltip("How often the labels refresh while the panel is open.")]
        [SerializeField] float refreshInterval = 0.25f;

        float nextRefreshAt;
        string scold;

        /// <summary>The agent currently being described.</summary>
        public AgentRoutine Agent => agent;

        /// <summary>Raised as the panel closes, carrying whichever agent it was showing.</summary>
        public event Action<AgentRoutine> Closed;

        protected override void Awake()
        {
            base.Awake();
            Refresh();
        }

        /// <summary>Points the panel at <paramref name="target"/> and slides it in.</summary>
        public void ShowFor(AgentRoutine target)
        {
            SetAgent(target);
            Show();
        }

        /// <summary>Changes which agent is described without opening or closing the panel.</summary>
        public void SetAgent(AgentRoutine target)
        {
            agent = target;

            // Chosen once per agent, not per refresh, or the telling-off would reword itself
            // several times a second.
            scold = PickScold();
            Refresh();
        }

        public override void Hide()
        {
            var wasShowing = agent;

            if (taskTicker != null)
            {
                taskTicker.Stop();
            }

            base.Hide();
            Closed?.Invoke(wasShowing);
        }

        void Update()
        {
            // Only while visible: a closed panel has nothing to show and no reason to poll.
            if (!IsOpen || agent == null)
            {
                return;
            }

            // Unscaled so the panel keeps updating if the game is paused, matching the tween.
            if (Time.unscaledTime < nextRefreshAt)
            {
                return;
            }

            nextRefreshAt = Time.unscaledTime + Mathf.Max(0.05f, refreshInterval);
            Refresh();
        }

        void Refresh()
        {
            if (titleLabel != null)
            {
                titleLabel.text = agent != null ? agent.name : "No agent";
            }

            // Seated means at a desk, so it is working; anything else means it was caught loafing.
            var working = agent != null && agent.IsSeated;

            if (working && taskTicker != null)
            {
                if (!taskTicker.IsPlaying)
                {
                    taskTicker.Play();
                }

                return;   // the ticker owns the label from here, so do not fight it
            }

            if (taskTicker != null)
            {
                taskTicker.Stop();
            }

            if (statusLabel != null)
            {
                statusLabel.text = Describe(working);
            }
        }

        string Describe(bool working)
        {
            if (agent == null)
            {
                return "Nothing selected.";
            }

            // Only reached without a ticker assigned; the ticker normally draws the working state.
            return working ? "TASK: in progress" : scold;
        }

        string PickScold()
        {
            if (scoldMessages == null || scoldMessages.Length == 0)
            {
                return "Back to work.";
            }

            return scoldMessages[UnityEngine.Random.Range(0, scoldMessages.Length)];
        }

        /// <summary>
        /// Enters from the right instead of the left, so the panel does not land on top of the
        /// call tray. This is the one thing it changes about the inherited slide.
        /// </summary>
        protected override Vector2 ShownPosition()
        {
            var parent = Rect.parent as RectTransform;
            if (parent == null)
            {
                return HiddenPosition;
            }

            var anchorX = parent.rect.xMin + parent.rect.width * ((Rect.anchorMin.x + Rect.anchorMax.x) * 0.5f);
            var shownX = parent.rect.xMax - anchorX - Rect.rect.width * (1f - Rect.pivot.x);
            return new Vector2(shownX, HiddenPosition.y);
        }
    }
}
