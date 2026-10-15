namespace Dojo.Game.Events
{
    /// <summary>
    /// Raised when one of the "Call Agent" buttons is pressed. Carries an index rather than an
    /// agent reference so the UI needs to know nothing about the agents themselves.
    /// </summary>
    public readonly struct AgentCallRequested
    {
        /// <summary>Zero-based: 0 is Agent1, 1 is Agent2, 2 is Agent3.</summary>
        public readonly int AgentIndex;

        public AgentCallRequested(int agentIndex)
        {
            AgentIndex = agentIndex;
        }

        public override string ToString() => $"AgentCallRequested(Agent{AgentIndex + 1})";
    }
}
