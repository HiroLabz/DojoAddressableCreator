using UnityEngine;

namespace Dojo.Game.InGame.Agents
{
    /// <summary>
    /// Records which credentials a placed manager is, so a saved world can put them back.
    /// </summary>
    /// <remarks>
    /// The manager's counterpart of <see cref="PlacedAgent"/>, and a separate component rather than
    /// a reuse of it. Both answer "who is this?", but they are the two questions a save asks
    /// separately: agents are a roster the player places several of, the manager is the player.
    /// Stamping a manager as a <c>PlacedAgent</c> would put them in the world save's agent list, and
    /// a load would then hand them to the agent assembler — which would switch on an
    /// <c>AgentRoutine</c> they do not have and confine them to a wander they never do.
    /// <para>
    /// Without a stamp of some kind the manager was saved as nothing at all: a placed manager
    /// deliberately carries no catalogue entry, so <c>PlacedPiece</c> cannot resolve one and the
    /// save counted them among the "child object(s) with nothing to identify them" it skipped.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PlacedManager : MonoBehaviour
    {
        [Tooltip("Id of the credentials this manager was placed from. What a saved world is matched by.")]
        [SerializeField] int credentialId;

        [Tooltip("Name from the credentials, for the inspector and the hierarchy.")]
        [SerializeField] string managerName;

        [Tooltip("Department code from the credentials.")]
        [SerializeField] string workId;


        /// <summary>Id of the credentials this manager was placed from.</summary>
        public int CredentialId => credentialId;

        /// <summary>Name from the credentials.</summary>
        public string ManagerName => managerName;

        /// <summary>Department code from the credentials.</summary>
        public string WorkId => workId;


        /// <summary>Records the credentials. Called once, as the manager leaves the drawer.</summary>
        public void Set(int id, string name, string department)
        {
            credentialId = id;
            managerName = name;
            workId = department;
        }
    }
}
