using UnityEngine;

namespace Dojo.Game.InGame.Agents
{
    /// <summary>
    /// Records which set of credentials a placed agent is, so an area painted later can find the
    /// capsules it applies to.
    /// </summary>
    /// <remarks>
    /// The agent counterpart of <c>PlacedPiece</c>, and for the same reason: a placed agent is not
    /// a prefab instance by the time the spawner has wrapped it, so Unity's own prefab link is gone
    /// and nothing else remembers where it came from.
    /// <para>
    /// The credentials are copied here rather than referenced. An <see cref="AgentCredential"/> is
    /// a plain object read fresh from the roster each time the drawer opens, so a reference would
    /// go stale the next time the tab was shown; <see cref="CredentialId"/> is the durable link and
    /// the rest is here so an agent standing in the world can say who they are without the roster
    /// being loaded at all.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PlacedAgent : MonoBehaviour
    {
        [Tooltip("Id of the credentials this agent was placed from. What an area is matched by.")]
        [SerializeField] int credentialId;

        [Tooltip("Name from the credentials, for the inspector and the hierarchy.")]
        [SerializeField] string agentName;

        [Tooltip("Department code from the credentials.")]
        [SerializeField] string workId;


        /// <summary>Id of the credentials this agent was placed from.</summary>
        public int CredentialId => credentialId;

        /// <summary>Name from the credentials.</summary>
        public string AgentName => agentName;

        /// <summary>Department code from the credentials.</summary>
        public string WorkId => workId;


        /// <summary>Records the credentials. Called once, as the agent leaves the drawer.</summary>
        public void Set(int id, string name, string department)
        {
            credentialId = id;
            agentName = name;
            workId = department;
        }
    }
}
