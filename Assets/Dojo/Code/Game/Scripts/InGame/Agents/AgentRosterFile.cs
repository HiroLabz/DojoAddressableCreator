using Dojo.Framework.Content;

namespace Dojo.Game.InGame.Agents
{
    /// <summary>
    /// The agents' credentials: the roster shipped with the game and the areas the player has
    /// painted over it.
    /// </summary>
    /// <remarks>
    /// All the reading, merging and writing is <see cref="CredentialFile"/>; this only says which
    /// two files it is about. A distinct type rather than a configured instance because that is
    /// what lets the container hand the agents panel the agents' roster and the manager panel the
    /// manager's without either having to ask for a particular one by name.
    /// </remarks>
    public sealed class AgentRosterFile : CredentialFile
    {
        public AgentRosterFile(IContentService content) : base(content) { }

        /// <summary>Content address of the shipped agent credentials.</summary>
        public override string CatalogueResource => "Agents/agents";

        protected override string FileName => "agents.json";

        protected override string Label => "Agents";
    }
}
