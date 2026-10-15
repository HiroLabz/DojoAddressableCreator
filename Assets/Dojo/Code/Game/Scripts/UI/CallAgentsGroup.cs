using Dojo.Framework.Events;
using Dojo.Framework.UI;
using Dojo.Game.Events;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace Dojo.Game.UI
{
    /// <summary>
    /// The call-agents tray. Sliding on and off screen is inherited from
    /// <see cref="InGamePopup"/>; all this adds is the Call buttons, which publish an
    /// <see cref="AgentCallRequested"/> rather than touching an agent, so the UI stays ignorant of
    /// navigation.
    /// </summary>
    public sealed class CallAgentsGroup : InGamePopup
    {
        [Header("Calls")]
        [Tooltip("In call order: element 0 is Agent1, element 1 is Agent2, and so on.")]
        [SerializeField] Button[] callButtons = new Button[0];

        IEventHub hub;

        [Inject]
        public void Construct(IEventHub hub)
        {
            this.hub = hub;
        }

        protected override void Awake()
        {
            base.Awake();

            for (var i = 0; i < callButtons.Length; i++)
            {
                if (callButtons[i] == null)
                {
                    continue;
                }

                var index = i;   // captured per iteration, otherwise every button reports the last index
                callButtons[i].onClick.AddListener(() => CallAgent(index));
            }
        }

        public void CallAgent1() => CallAgent(0);

        public void CallAgent2() => CallAgent(1);

        public void CallAgent3() => CallAgent(2);

        /// <summary>Announces that an agent was called. Someone else decides what that means.</summary>
        public void CallAgent(int index)
        {
            if (hub == null)
            {
                Debug.LogError(
                    $"{nameof(CallAgentsGroup)} on '{name}' was never injected. Register it with " +
                    "the scene's LifetimeScope so it receives an IEventHub.",
                    this);
                return;
            }

            hub.Publish(new AgentCallRequested(index));
        }
    }
}
