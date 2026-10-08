using System.Collections.Generic;
using Dojo.Framework.Events;
using Dojo.Framework.UI;
using Dojo.Game.Events;
using Dojo.Game.InGame.Agents;
using Dojo.Game.InGame.Controllers;
using Dojo.Game.Placement;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using VContainer;

namespace Dojo.Game.UI
{
    /// <summary>
    /// The inventory drawer's You tab: the manager's card — who they are, the block area they go
    /// back to, and putting them into the world.
    /// </summary>
    /// <remarks>
    /// The counterpart of <see cref="AgentDisplayUI"/>, and deliberately not the same class. The
    /// two look alike and share the roster shape and the placement gesture, but they differ in the
    /// things that matter: there is exactly one manager, so there is no strip of tiles and nothing
    /// to select; and a manager is driven by the player's clicks rather than by an
    /// <c>AgentRoutine</c>, so bringing one to life is a different job.
    /// <para>
    /// No painting here any more. Areas are painted and named in the Areas tab; the manager is
    /// given one with <b>Select area</b>, from those no agent is using - he never shares one.
    /// </para>
    /// </remarks>
    public sealed class ManagerDisplayUI : MonoBehaviour
    {
        [Header("Card")]
        [Tooltip("The youBox panel. Shown while the You tab is up and hidden with it. Left empty " +
                 "this object is used, which is where the component is expected to live.")]
        [SerializeField] GameObject card;

        [Tooltip("The manager's name. The label on img_agent.")]
        [SerializeField] TMP_Text nameLabel;

        [Tooltip("What the manager does. The label under description.")]
        [SerializeField] TMP_Text descriptionLabel;

        [Tooltip("Portrait. Tinted when no sprite is set, so the card is not blank before art.")]
        [SerializeField] Image portrait;

        [Header("Controls")]
        [Tooltip("placeAgent — the 'Place Manager' button. Puts the manager into the world.")]
        [SerializeField] Button placeManagerButton;

        [Tooltip("Select area: the block area he walks back to when left standing, or none.")]
        [SerializeField] TMP_Dropdown areaDropdown;

        ManagerRosterFile file;
        BlockAreas blockAreas;
        PlacementController placement;
        FurniturePlacementService service;
        ManagerAssembler assembler;

        // Where a painted area is announced, so the world can save itself. This panel knows
        // nothing about who is listening.
        IEventHub hub;

        AgentCredential manager;

        // The piece on the cursor, waiting for the gesture to end so it can be handed over to the
        // player. Null the rest of the time.
        FurniturePiece pending;

        // The area id behind each of the dropdown's choices, in order: 0, the first, is none.
        readonly List<int> choiceIds = new List<int>();

        // Up while the dropdown is being filled, so setting its value is not taken for a choice.
        bool filling;

        /// <summary>The manager's credentials, or null before the tab has been opened.</summary>
        public AgentCredential Manager => manager;

        /// <summary>
        /// The panel to raise and lower, resolved on demand.
        /// </summary>
        /// <remarks>
        /// Not read straight out of the field, because <see cref="InventoryUI"/> can reach
        /// <see cref="Dismiss"/> before this component's <c>Awake</c> has filled it in — the two
        /// live on different objects and Unity does not order their <c>Awake</c> calls.
        /// </remarks>
        GameObject Card => card != null ? card : gameObject;

        /// <summary>Receives the manager's roster, the world's areas and the placement system.</summary>
        [Inject]
        public void Construct(
            ManagerRosterFile rosterFile,
            BlockAreas areas,
            PlacementController controller,
            FurniturePlacementService placementService,
            ManagerAssembler managerAssembler,
            IEventHub eventHub)
        {
            this.hub = eventHub;
            this.file = rosterFile;
            this.blockAreas = areas;
            this.placement = controller;
            this.service = placementService;
            this.assembler = managerAssembler;

            Listen();
        }

        void Awake()
        {
            if (card == null)
            {
                card = gameObject;
            }

            if (placeManagerButton != null)
            {
                placeManagerButton.onClick.AddListener(OnPlaceManager);
            }

            if (areaDropdown != null)
            {
                areaDropdown.onValueChanged.AddListener(OnAreaChosen);
            }

            // Deliberately does not hide the card. The card is authored inactive, so this Awake
            // does not run at scene load at all — it runs the first time something activates the
            // card, which is Show. Unity runs Awake synchronously inside SetActive(true), so a
            // SetActive(false) here would fire in the middle of the call that was raising the
            // panel and take it straight back down. Visibility has one owner: Show and Dismiss.
        }

        void OnDestroy() => Unlisten();

        // Subscribed once, whichever of injection and Awake happens second. The scene scope defers
        // building until its parent root scope is ready, so injection can land either side of Awake.
        bool listening;

        void Listen()
        {
            if (listening || blockAreas == null)
            {
                return;
            }

            blockAreas.Changed += Refresh;
            listening = true;
        }

        void Unlisten()
        {
            if (!listening || blockAreas == null)
            {
                return;
            }

            blockAreas.Changed -= Refresh;
            listening = false;
        }

        /// <summary>
        /// Raises the card and fills it in. Called by <see cref="InventoryUI"/> when the You tab is
        /// selected.
        /// </summary>
        /// <remarks>
        /// The card goes up first, before anything that could decide there is nothing to show.
        /// Pressing the tab means "show me the manager", and that has to happen whether the
        /// credentials read, come back empty, or are not there at all — a tab that silently does
        /// nothing is indistinguishable from a broken button.
        /// <para>
        /// The roster is re-read on every open rather than cached, so an area painted this session
        /// shows on the card without a reload. It is one entry and a small file.
        /// </para>
        /// </remarks>
        public void Show()
        {
            Card.SetActive(true);

            if (file == null)
            {
                // The credentials now come from downloaded content rather than a Resources folder,
                // and reaching them needs the container this panel never received. The self-built
                // roster is kept only so painted areas on disk still load; the shipped catalogue
                // cannot be read at all from here, so the card comes up empty rather than stale.
                Debug.LogWarning("[Manager] This panel was not injected, so it cannot reach the "
                    + "content service. The card will be empty, and Define Area and Place Manager "
                    + "will not work — add ManagerDisplayUI to GameLifetimeScope's injected "
                    + "components.", this);

                file = new ManagerRosterFile(null);
            }

            manager = file.ReadOne();

            Refresh();
        }

        /// <summary>Hides the card. Called when another tab is selected.</summary>
        public void Dismiss()
        {
            Card.SetActive(false);
        }

        void Refresh()
        {
            if (manager == null)
            {
                if (nameLabel != null) { nameLabel.text = string.Empty; }
                if (descriptionLabel != null) { descriptionLabel.text = string.Empty; }

                SetInteractable(false);
                return;
            }

            if (nameLabel != null)
            {
                nameLabel.text = manager.name;
            }

            if (descriptionLabel != null)
            {
                var area = blockAreas != null
                    ? blockAreas.AreaFor(Dojo.Framework.World.SnapshotRole.Manager, manager.id)
                    : null;

                descriptionLabel.text = manager.description + "\n<size=80%>" + Upper(manager.workId)
                    + " · " + manager.Status + " · "
                    + (area != null ? area.Name + " · " + Storeys.LabelFor(area.Floor) : "no area")
                    + "</size>";
            }

            if (portrait != null && portrait.sprite == null)
            {
                portrait.color = new Color(0.95f, 0.82f, 0.35f);
            }

            SetInteractable(true);
            FillAreaChoices();
        }

        /// <summary>
        /// Lists what the manager may be given - none, then every area no agent is using - and
        /// shows the one he has.
        /// </summary>
        void FillAreaChoices()
        {
            if (areaDropdown == null || blockAreas == null || manager == null)
            {
                return;
            }

            filling = true;

            var options = new List<TMP_Dropdown.OptionData> { new TMP_Dropdown.OptionData("No area") };
            choiceIds.Clear();
            choiceIds.Add(0);

            var current = blockAreas.AreaOf(Dojo.Framework.World.SnapshotRole.Manager, manager.id);
            var shown = 0;

            foreach (var area in blockAreas.ChoicesForManager())
            {
                if (area.Id == current)
                {
                    shown = choiceIds.Count;
                }

                options.Add(new TMP_Dropdown.OptionData(area.Name + " · " + Storeys.LabelFor(area.Floor)));
                choiceIds.Add(area.Id);
            }

            areaDropdown.ClearOptions();
            areaDropdown.AddOptions(options);
            areaDropdown.SetValueWithoutNotify(shown);
            areaDropdown.RefreshShownValue();

            filling = false;
        }

        /// <summary>Gives the manager the area picked. The areas announce it, so the world saves it.</summary>
        void OnAreaChosen(int index)
        {
            if (filling || manager == null || blockAreas == null || index < 0 || index >= choiceIds.Count)
            {
                return;
            }

            if (!blockAreas.Assign(Dojo.Framework.World.SnapshotRole.Manager, manager.id, choiceIds[index]))
            {
                Debug.LogWarning("[Manager] That area could not be given to the manager: an agent is using it.", this);
                Refresh();
            }
        }

        void SetInteractable(bool on)
        {
            if (areaDropdown != null)
            {
                areaDropdown.interactable = on;
            }

            if (placeManagerButton != null)
            {
                // There is only ever one owner. Once he is in the room the button goes dead, so a
                // second press cannot put a twin of the player on the floor.
                placeManagerButton.interactable = on && !OwnerAlreadyPlaced();
            }
        }

        /// <summary>
        /// True once an owner is standing in the world.
        /// </summary>
        /// <remarks>
        /// Asked of the scene rather than tracked in a flag, because the owner can arrive two ways
        /// — placed from this card, or rebuilt by a world load — and a flag set in one of them
        /// would be wrong after the other. A load is also why this is re-asked rather than answered
        /// once: the card outlives the world it was opened over.
        /// <para>
        /// The piece still on the cursor does not count. It carries the controller already, so
        /// without this the button would switch off the instant it was pressed and stay off if the
        /// placement were then cancelled.
        /// </para>
        /// </remarks>
        bool OwnerAlreadyPlaced()
        {
            var existing = FindAnyObjectByType<ManagerController>(FindObjectsInactive.Include);

            if (existing == null)
            {
                return false;
            }

            return pending == null || existing.gameObject != pending.gameObject;
        }

        void OnPlaceManager()
        {
            if (manager == null || placement == null)
            {
                Debug.LogWarning("[Manager] Nothing to place, or no PlacementController in the "
                    + "scene.", this);
                return;
            }

            var prefab = assembler != null ? assembler.Prefab : null;

            if (prefab == null)
            {
                Debug.LogWarning("[Manager] No manager prefab is set on the scene's world settings, "
                    + "so there is nothing to put down. Assign one under World on the scene's "
                    + "LifetimeScope.", this);
                return;
            }

            // No prefab id and no Resources path, the same as an agent: that leaves PlacedPiece
            // unresolvable, which is what keeps the manager out of the world save's furniture list.
            if (!placement.BeginDragFromInventory(prefab))
            {
                return;   // refused, and already logged
            }

            pending = placement.Held;

            // Stamped and quietened through the assembler, which is also what a world load uses.
            // The stamp is what a save recognises: without it the manager carries no catalogue
            // entry and no identity either, and SaveAs counted him among the children it skipped.
            assembler.Stamp(pending, manager);
            ManagerAssembler.Quieten(pending);
        }

        /// <summary>
        /// Watches for the placement gesture to end, and hands the manager over to the player.
        /// </summary>
        /// <remarks>
        /// Polled rather than told, for the same reason the furniture tiles poll: the gesture can
        /// end half a dozen ways and there is no one callback for all of them. A cancelled piece is
        /// destroyed, and a destroyed <c>Object</c> compares equal to null, so an abandoned
        /// placement falls out of this without a case of its own.
        /// </remarks>
        void Update()
        {
            if (pending == null || (placement != null && placement.IsBusy))
            {
                return;
            }

            Activate(pending);
            pending = null;

            // The owner is in the room now, so the card is out of date: Place Manager has to go
            // dead the moment the gesture ends rather than on the next time the tab is opened.
            Refresh();
        }

        /// <summary>
        /// Hands the committed piece over to the player.
        /// </summary>
        /// <remarks>
        /// The work itself is <see cref="ManagerAssembler.Activate"/>, shared with the world
        /// loader so a placed manager and a loaded one come alive by the same steps rather than by
        /// two half-complete copies of them. This only decides <em>when</em>.
        /// </remarks>
        void Activate(FurniturePiece piece)
        {
            assembler.Activate(piece);

            Debug.Log("[Manager] placed " + piece.name + " at "
                + piece.transform.position.ToString("F2"), this);
        }

        static string Upper(string text)
            => string.IsNullOrEmpty(text) ? string.Empty : text.ToUpperInvariant();

#if UNITY_EDITOR
        /// <summary>
        /// Finds the card's parts by the names they have in the scene, so a panel dropped onto
        /// <c>youBox</c> is wired without five drag-and-drops.
        /// </summary>
        /// <remarks>
        /// Only ever fills in what is empty. An explicitly assigned reference is the author's
        /// answer and is never second-guessed — a search that overwrote one would make the
        /// inspector unusable, because every save would put it back.
        /// </remarks>
        void OnValidate()
        {
            if (placeManagerButton == null)
            {
                placeManagerButton = FindChild<Button>("placeAgent");
            }

            if (portrait == null)
            {
                portrait = FindChild<Image>("img_agent");
            }

            if (nameLabel == null && portrait != null)
            {
                nameLabel = portrait.GetComponentInChildren<TMP_Text>(true);
            }

            if (descriptionLabel == null)
            {
                var box = FindChild<Image>("description");
                descriptionLabel = box != null ? box.GetComponentInChildren<TMP_Text>(true) : null;
            }
        }

        T FindChild<T>(string named) where T : Component
        {
            foreach (var candidate in GetComponentsInChildren<T>(true))
            {
                if (candidate.gameObject.name == named)
                {
                    return candidate;
                }
            }

            return null;
        }
#endif
    }
}
