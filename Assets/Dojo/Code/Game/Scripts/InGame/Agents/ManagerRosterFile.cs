using Dojo.Framework.Content;

namespace Dojo.Game.InGame.Agents
{
    /// <summary>
    /// The manager's credentials: the one entry shipped with the game and the area the player has
    /// painted over it.
    /// </summary>
    /// <remarks>
    /// The same shape as the agents' roster and the same code behind it — see
    /// <see cref="CredentialFile"/> — because the manager holds exactly the fields an agent does.
    /// It is a roster with one entry rather than a single credential on its own: keeping the shape
    /// means the merge, the save and the world-load restore all work on the manager without a
    /// second implementation of any of them, and a second manager later would need no new code.
    /// </remarks>
    public sealed class ManagerRosterFile : CredentialFile
    {
        public ManagerRosterFile(IContentService content) : base(content) { }

        /// <summary>Content address of the shipped manager credentials.</summary>
        public override string CatalogueResource => "Managers/manager";

        /// <summary>
        /// Kept apart from the agents' file on purpose. The manager is not one of the agents — he
        /// is the player — and mixing the two would put him in the agents tab's tile strip.
        /// </summary>
        protected override string FileName => "manager.json";

        protected override string Label => "Manager";

        /// <summary>
        /// The manager, or null when the credentials file is missing or empty.
        /// </summary>
        /// <remarks>
        /// There is only ever one, so the panel is spared unpacking a list to find out which entry
        /// it is meant to be showing. The first entry wins if somebody adds a second, which is a
        /// quiet answer to a file that should not have one.
        /// </remarks>
        public AgentCredential ReadOne()
        {
            var roster = Read();

            if (roster == null || roster.agents == null)
            {
                return null;
            }

            foreach (var credential in roster.agents)
            {
                if (credential != null)
                {
                    return credential;
                }
            }

            return null;
        }
    }
}
