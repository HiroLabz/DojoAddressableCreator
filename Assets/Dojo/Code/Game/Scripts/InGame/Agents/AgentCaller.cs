using Dojo.Game.Components;
using UnityEngine;

namespace Dojo.Game.InGame.Agents
{
    /// <summary>
    /// The button-facing API for summoning agents to the manager's office.
    /// <para>
    /// Wire a Button's OnClick to <see cref="CallAgent1"/>, <see cref="CallAgent2"/> or
    /// <see cref="CallAgent3"/>. They take no arguments, so they show up directly in the OnClick
    /// dropdown under "AgentCaller" with no extra plumbing.
    /// </para>
    /// </summary>
    public sealed class AgentCaller : MonoBehaviour
    {
        [Tooltip("Agents in call order: element 0 is Agent1, element 1 is Agent2, and so on.")]
        [SerializeField] AgentRoutine[] agents = new AgentRoutine[0];

        [Tooltip("Where a summoned agent sits. The LoungeChair in the manager's office.")]
        [SerializeField] Chair loungeChair;

        public void CallAgent1() => Call(0);

        public void CallAgent2() => Call(1);

        public void CallAgent3() => Call(2);

        public void SendAgent1Home() => SendHome(0);

        public void SendAgent2Home() => SendHome(1);

        public void SendAgent3Home() => SendHome(2);

        /// <summary>Summon one agent, numbered from zero, to the lounge chair.</summary>
        public void Call(int index)
        {
            var agent = Get(index);
            if (agent == null)
            {
                return;
            }

            if (loungeChair == null)
            {
                Debug.LogError($"{nameof(AgentCaller)} on '{name}' has no lounge chair assigned.", this);
                return;
            }

            agent.SummonTo(loungeChair);
        }

        /// <summary>Send one agent, numbered from zero, back to its own floor.</summary>
        public void SendHome(int index)
        {
            var agent = Get(index);
            if (agent != null)
            {
                agent.SendHome();
            }
        }

        /// <summary>Send everyone back, handy for a single "dismiss all" button.</summary>
        public void SendAllHome()
        {
            for (var i = 0; i < agents.Length; i++)
            {
                SendHome(i);
            }
        }

        AgentRoutine Get(int index)
        {
            if (index < 0 || index >= agents.Length)
            {
                Debug.LogError($"{nameof(AgentCaller)} on '{name}' has no agent at index {index}.", this);
                return null;
            }

            if (agents[index] == null)
            {
                Debug.LogError($"{nameof(AgentCaller)} on '{name}' has an empty slot at index {index}.", this);
            }

            return agents[index];
        }
    }
}
