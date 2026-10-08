using Dojo.Game.InGame.Agents;

namespace Dojo.Game.Events
{
    /// <summary>
    /// Raised when the manager walks up to an agent he was sent to. Whoever handles it decides
    /// what happens next — stopping the agent and opening its details panel, currently.
    /// </summary>
    public readonly struct AgentReached
    {
        /// <summary>The agent the manager reached.</summary>
        public readonly AgentRoutine Agent;

        public AgentReached(AgentRoutine agent)
        {
            Agent = agent;
        }

        public override string ToString() => $"AgentReached({(Agent != null ? Agent.name : "null")})";
    }
}
