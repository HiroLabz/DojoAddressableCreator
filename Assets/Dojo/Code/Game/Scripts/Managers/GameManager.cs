using Dojo.Framework.Events;
using Dojo.Framework.UI;
using Dojo.Framework.World;
using Dojo.Game.Components;
using Dojo.Game.Events;
using Dojo.Game.InGame.Agents;
using Dojo.Game.InGame.Controllers;
using Dojo.Game.Placement;
using Dojo.Game.Server;
using Dojo.Game.UI;
using UnityEngine;
using VContainer;

namespace Dojo.Game.Managers
{
    /// <summary>
    /// Owns the cast of the Game scene and turns UI intent into agent behaviour. The UI publishes
    /// <see cref="AgentCallRequested"/> and never touches an agent directly, so the buttons stay
    /// unaware of navigation entirely.
    /// </summary>
    public sealed class GameManager : MonoBehaviour
    {
        [Header("Cast")]
        [Tooltip("The manager the agents are summoned to.")]
        [SerializeField] ManagerController owner;

        [Tooltip("In call order: element 0 is Agent1, element 1 is Agent2, and so on.")]
        [SerializeField] AgentRoutine[] agents = new AgentRoutine[0];

        [Tooltip("Where a summoned agent sits: the LoungeChair in the manager's office.")]
        [SerializeField] Chair managerOfficeChair;

        [Header("UI")]
        [Tooltip("Chat window opened where the manager meets an agent. Replaces the old details " +
                 "panel, which only read out what the agent was doing.")]
        [SerializeField] AgentChatUI agentChat;

        [Tooltip("The chat window on the game canvas. A window rather than a dialog, so it is a " +
                 "scene object held here rather than something built on demand by DialogManager.")]
        [SerializeField] ChatAgentWindow chatWindow;

        IEventHub hub;
        IAgentRegistry registry;
        IDialogManager dialogs;
        WorldOpenIntent worldIntent;

        /// <summary>For the "No manager present" notice.</summary>
        PopupPrefabs popups;

        /// <summary>
        /// World saving and loading, or null in a scene that has no world to open.
        /// </summary>
        /// <remarks>
        /// <b>Handed over by the scope rather than injected</b>, and for the same reason the
        /// placement controller's owner is: <c>WorldService</c> is only registered when the scene
        /// contains a <c>WorldRoot</c>, and the <c>Game</c> scene does not. As a constructor
        /// parameter it would be resolved in every scene, throw in the ones with no world, and
        /// take the rest of that build callback down with it — leaving this manager, the file tray
        /// and the inventory all uninjected and each complaining about it in turn.
        /// <para>
        /// Null is therefore a legitimate state and not a failure: a scene with no world root has
        /// nothing to load, and <see cref="OpenStartingWorld"/> says nothing about it.
        /// </para>
        /// </remarks>
        public WorldService World { get; set; }

        // Who the chat is currently about, so closing it can let that particular agent go. The
        // window itself only knows a name, and two agents could share one.
        AgentRoutine talkingTo;

        /// <summary>
        /// The manager the agents report to, found in the scene if none was authored.
        /// </summary>
        /// <remarks>
        /// The fallback is not tidying: this scene no longer has a manager authored into it. He is
        /// placed by the You tab at runtime, so the serialized field is empty and the only way to
        /// reach him is to look. Cached once found, and only consulted when an agent has just been
        /// met — so the search happens at most once per meeting rather than per frame.
        /// <para>
        /// Inactive ones included, because a manager on the cursor mid-placement is a real state
        /// and finding him then is better than deciding there is no manager at all.
        /// </para>
        /// </remarks>
        public ManagerController Owner
        {
            get
            {
                if (owner == null)
                {
                    owner = FindAnyObjectByType<ManagerController>(FindObjectsInactive.Include);
                }

                return owner;
            }
        }

        /// <remarks>
        /// The registry comes in alongside the hub because the chat window is titled from the
        /// credentials rather than from the GameObject's name: an agent in the world is a wrapped
        /// prefab called whatever it was stamped with, while the name, department and platform id
        /// the player needs live in the data. The registry rather than the file, so this reads
        /// whatever the startup sync actually loaded.
        /// <para>
        /// The dialog manager arrives for the sake of <see cref="OpenStartingWorld"/>, and is safe
        /// to ask for because it is declared on the project root and so exists in every scene. The
        /// world service is <em>not</em> a parameter here — see <see cref="World"/>.
        /// </para>
        /// </remarks>
        [Inject]
        public void Construct(
            IEventHub hub,
            IAgentRegistry agentRegistry,
            IDialogManager dialogManager,
            WorldOpenIntent worldIntent,
            PopupPrefabs popups)
        {
            this.hub = hub;
            this.registry = agentRegistry;
            this.dialogs = dialogManager;
            this.worldIntent = worldIntent;
            this.popups = popups;
        }

        // Start, not Awake: injection runs from the LifetimeScope's Awake, and the order of two
        // Awakes is not guaranteed. By Start the container has certainly been built.
        void Start()
        {
            // Before the hub guard, and deliberately. Opening the world needs no event hub, and a
            // scene whose wiring is incomplete should still come up in the world the player left
            // off in rather than empty — an empty floor looks far more like lost work than a
            // console error does.
            OpenStartingWorld();
            WarnIfNoManager();

            if (hub == null)
            {
                Debug.LogError(
                    $"{nameof(GameManager)} on '{name}' was never injected. Register it with the " +
                    "scene's LifetimeScope so it receives an IEventHub.",
                    this);
                return;
            }

            hub.Subscribe<AgentCallRequested>(OnAgentCallRequested).DisposeWith(this);
            hub.Subscribe<AgentReached>(OnAgentReached).DisposeWith(this);

            if (agentChat != null)
            {
                agentChat.Closed += OnChatClosed;
            }
        }

        /// <summary>
        /// Opens the world the player was last in, or asks which one to open.
        /// </summary>
        /// <remarks>
        /// Here rather than on the file tray, because opening a world is not a thing the player
        /// asked for — it is what starting the game means. The tray is one way to change worlds
        /// once you are in one; this is the decision that there is a world at all, and it belongs
        /// with whatever owns the scene.
        /// <para>
        /// The selection itself lives in the world library and is recorded by
        /// <c>WorldService.Load</c>, so nothing here has to remember anything: picking from the
        /// chooser loads the world, and loading it is what makes it the current one. That is why
        /// the callback is the same one-liner the file tray uses.
        /// </para>
        /// <para>
        /// The chooser is opened even when nothing has been saved yet. It says so itself, which is
        /// a better answer to "why is the floor empty" than a console line the player will not
        /// read.
        /// </para>
        /// </remarks>
        void OpenStartingWorld()
        {
            // Consumed before anything else, including the no-world-root case, so a request cannot
            // survive this scene and act on the next one.
            // Initialised because the short-circuit below can skip Consume entirely, and an out
            // parameter the compiler cannot prove was written is not one it will let us read.
            string requested = string.Empty;
            bool asked = worldIntent != null && worldIntent.Consume(out requested);

            if (asked)
            {
                if (string.IsNullOrEmpty(requested))
                {
                    // The Lobby's New World button. Neither load a world nor ask which to load —
                    // being asked to pick one straight after choosing "new" is the bug, not the
                    // empty floor. It does start with nothing selected and no areas, so the
                    // world that was open is neither written over nor carried into it.
                    if (World != null)
                    {
                        World.StartBlank();
                    }

                    return;
                }

                if (World != null)
                {
                    World.Load(requested);
                }

                return;
            }

            if (World == null)
            {
                // No world root in this scene, so there is nothing to open and nothing wrong.
                // See the remarks on World.
                return;
            }

            var chosen = World.CurrentWorld;

            if (!string.IsNullOrEmpty(chosen))
            {
                World.Load(chosen);
                return;
            }

            // Nothing picked, or what was picked has since been deleted — WorldService.CurrentWorld
            // reports both as nothing. A player with no world of their own starts in the default
            // one from the content pack, fitted to their own agents and manager.
            if (World.LoadDefault())
            {
                return;
            }

            // No default world in the content either: ask which saved world to open.
            if (dialogs == null)
            {
                Debug.LogError(
                    $"{nameof(GameManager)} on '{name}' has no world selected and no dialog " +
                    "manager to ask with, so the game opens on an empty floor.",
                    this);
                return;
            }

            dialogs.Choose("Load a world", World.WorldNames(), name => World.Load(name));
        }

        /// <summary>
        /// Tells the player when their account has no manager to stand in the world as them.
        /// </summary>
        /// <remarks>
        /// The world still opens, agents and all; only the manager is missing. The red Notice
        /// popup, taken down after a few seconds, since it has no button and nothing here can fix
        /// it.
        /// </remarks>
        void WarnIfNoManager()
        {
            // With the manager switched off there is meant to be nobody, so nothing to warn about.
            if (World == null || !World.Manager || World.HasOwner)
            {
                return;
            }

            Debug.LogWarning("[Game] This account has no manager, so the world opens without one.", this);

            var layer = PopupManager.Instance;

            if (popups == null || popups.Notice == null || layer == null)
            {
                return;
            }

            var popup = layer.Show(popups.Notice)
                .Present("No manager present",
                    "This account has no manager, so there is nobody to stand in the world as you. "
                        + "Your agents still work.");

            _ = HideLaterAsync(popup, 6f);
        }

        async Awaitable HideLaterAsync(PopupBase popup, float seconds)
        {
            try
            {
                await Awaitable.WaitForSecondsAsync(seconds, destroyCancellationToken);
            }
            catch (System.OperationCanceledException)
            {
                return;
            }

            // Gone already if Play stopped meanwhile, and so is the layer: nothing to hide.
            if (popup != null && PopupManager.HasInstance)
            {
                PopupManager.Instance.Hide(popup);
            }
        }

        void OnDestroy()
        {
            if (agentChat != null)
            {
                agentChat.Closed -= OnChatClosed;
            }
        }

        /// <summary>
        /// The chat was dismissed: stop following that agent so the next click starts fresh, and
        /// let it get back to its routine.
        /// </summary>
        /// <remarks>
        /// The agent is remembered here rather than taken from the window's argument. The window
        /// knows a display name and nothing else — two agents may well share one — and what has to
        /// be resumed is the particular <c>AgentRoutine</c> that was halted.
        /// </remarks>
        void OnChatClosed(string agentName)
        {
            var boss = Owner;

            if (boss != null)
            {
                boss.ClearTarget();
            }

            if (talkingTo != null)
            {
                talkingTo.Resume();
                talkingTo = null;
            }
        }

        /// <summary>
        /// The manager met an agent: stop it and open the chat where the two of them are standing.
        /// </summary>
        /// <remarks>
        /// "Met" is already contact: <c>ManagerController</c> publishes this once the flat distance
        /// between the two is inside its contact distance, so there is no second collision test to
        /// add here.
        /// <para>
        /// The window is opened at the midpoint between them rather than at either one, so it is
        /// anchored to the meeting itself; <c>AgentChatUI</c> then keeps it on screen.
        /// </para>
        /// </remarks>
        void OnAgentReached(AgentReached message)
        {
            if (message.Agent == null)
            {
                return;
            }

            message.Agent.Halt();
            talkingTo = message.Agent;

            var credential = CredentialFor(message.Agent);

            // No credentials to be had means an agent authored into the scene rather than placed
            // from the roster. Named after the object so the window still says who it is.
            string who = credential != null ? credential.name : message.Agent.name;
            string work = credential != null ? credential.workId : string.Empty;
            string platformId = credential != null ? credential.hiroAgentId : null;

            if (OpenChatDialog(who, work, platformId))
            {
                return;
            }

            if (agentChat == null)
            {
                return;
            }

            // The old window, still here for a scene that has not been moved over. It is placed at
            // the meeting point, which is the whole difference from the dialog above.
            var boss = Owner;

            var at = boss != null
                ? (boss.transform.position + message.Agent.transform.position) * 0.5f
                : message.Agent.transform.position;

            if (credential != null)
            {
                agentChat.OpenAt(at, credential.name, credential.workId,
                    AgentDisplayUI.TintFor(credential.id), credential.hiroAgentId);
            }
            else
            {
                agentChat.OpenAt(at, message.Agent.name);
            }
        }

        /// <summary>
        /// Raises the full-screen chat window, and says whether it went up.
        /// </summary>
        /// <remarks>
        /// False rather than a complaint when there is no dialog to open: a scene whose
        /// <c>DialogPrefabs</c> has no chat window assigned falls back to the panel it used before,
        /// which is a working game rather than an agent that cannot be talked to.
        /// </remarks>
        bool OpenChatDialog(string agentName, string workId, string platformAgentId)
        {
            if (chatWindow == null)
            {
                return false;
            }

            chatWindow.Open(new ChatContext(agentName, workId, platformAgentId, null), OnChatWindowClosed);
            return true;
        }

        /// <remarks>
        /// The dialog reports no name, so the manager is released by the same path the old window's
        /// close took — what mattered there was that the conversation had ended, not who it was with.
        /// </remarks>
        void OnChatWindowClosed()
        {
            OnChatClosed(null);
        }

        /// <summary>
        /// The credentials behind an agent standing in the world, or null.
        /// </summary>
        /// <remarks>
        /// <c>PlacedAgent</c> is searched for upwards, because the two are not on the same object:
        /// the spawner parents the art it was handed under a container carrying the placement
        /// components, so the routine rides on the art while the stamp is on the container above it.
        /// </remarks>
        AgentCredential CredentialFor(AgentRoutine agent)
        {
            if (registry == null || agent == null)
            {
                return null;
            }

            var placed = agent.GetComponentInParent<PlacedAgent>();

            return placed == null ? null : registry.Find(placed.CredentialId);
        }

        void OnAgentCallRequested(AgentCallRequested message)
        {
            var agent = GetAgent(message.AgentIndex);
            if (agent == null)
            {
                return;
            }

            if (managerOfficeChair == null)
            {
                Debug.LogError($"{nameof(GameManager)} on '{name}' has no manager office chair assigned.", this);
                return;
            }

            agent.SummonTo(managerOfficeChair);
        }

        /// <summary>Send one agent, numbered from zero, back to its own floor.</summary>
        public void SendAgentHome(int index)
        {
            var agent = GetAgent(index);
            if (agent != null)
            {
                agent.SendHome();
            }
        }

        public void SendAllAgentsHome()
        {
            for (var i = 0; i < agents.Length; i++)
            {
                SendAgentHome(i);
            }
        }

        AgentRoutine GetAgent(int index)
        {
            if (index < 0 || index >= agents.Length)
            {
                Debug.LogError($"{nameof(GameManager)} on '{name}' has no agent at index {index}.", this);
                return null;
            }

            if (agents[index] == null)
            {
                Debug.LogError($"{nameof(GameManager)} on '{name}' has an empty agent slot at index {index}.", this);
            }

            return agents[index];
        }
    }
}
