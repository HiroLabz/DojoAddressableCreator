using System.Collections.Generic;
using Dojo.Framework.Events;
using Dojo.Framework.UI;
using Dojo.Game.Events;
using Dojo.Game.InGame.Agents;
using Dojo.Game.Managers;
using Dojo.Game.Placement;
using Dojo.Game.Server;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using VContainer;

namespace Dojo.Game.UI
{
    /// <summary>
    /// The inventory drawer's Agents tab: a strip of tiles across the drawer, and below it the
    /// selected agent's card — name, description, the block area they walk in, and the button that
    /// puts them down.
    /// </summary>
    /// <remarks>
    /// The tiles are built in code, one per entry in the roster, because there is nothing to author
    /// per agent: a tile is a coloured rect with a name on it, and a prefab for that would be one
    /// more thing to keep in step with the file that decides how many there are.
    /// <para>
    /// No painting here any more. Areas are painted and named in the Areas tab; an agent is given
    /// one with <b>Select area</b>, from every area but the manager's, or none - its whole floor.
    /// <b>Place Agent</b> is the same click the furniture tiles use, so an agent goes down under
    /// the rules everything else does.
    /// </para>
    /// </remarks>
    public sealed class AgentDisplayUI : MonoBehaviour
    {
        [Header("Card")]
        [Tooltip("The agentsbox panel. Shown while the Agents tab is up and hidden with it. Left " +
                 "empty this object is used, which is where the component is expected to live.")]
        [SerializeField] GameObject card;

        [Tooltip("Name of the selected agent. The label on img_agent.")]
        [SerializeField] TMP_Text nameLabel;

        [Tooltip("What the selected agent does. The label under description.")]
        [SerializeField] TMP_Text descriptionLabel;

        [Tooltip("Portrait of the selected agent. Tinted per agent when no sprite is set, so the " +
                 "card still tells the three of them apart before there is any art.")]
        [SerializeField] Image portrait;

        [Header("Controls")]
        [Tooltip("placeAgent. Puts the agent into the world under the cursor.")]
        [SerializeField] Button placeAgentButton;

        [Tooltip("Select area: which block area this agent walks in, or none for its whole floor.")]
        [SerializeField] TMP_Dropdown areaDropdown;

        [Tooltip("Select department: one of the vision's presets, or Custom… to type one.")]
        [SerializeField] TMP_Dropdown departmentDropdown;

        [Tooltip("Set role: asks for the agent's job title, such as \"Lead researcher\".")]
        [SerializeField] Button roleButton;

        [Header("Tiles")]
        [Tooltip("Size of one agent tile in the drawer strip, in pixels.")]
        [SerializeField] Vector2 tileSize = new Vector2(84f, 84f);

        [Tooltip("Colour of a tile that is not selected.")]
        [SerializeField] Color tileColour = new Color(0.16f, 0.18f, 0.22f, 0.9f);

        [Tooltip("Colour of the tile whose agent the card is showing.")]
        [SerializeField] Color tileSelectedColour = new Color(0.35f, 1f, 0.55f, 0.9f);

        [Tooltip("Point size of the name on a tile.")]
        [SerializeField] float tileFontSize = 15f;

        IAgentRegistry registry;
        BlockAreas blockAreas;
        PlacementController placement;
        FurniturePlacementService service;
        AgentAssembler assembler;

        // Where a painted area is announced, so the world can save itself. This panel knows
        // nothing about who is listening.
        IEventHub hub;

        // Asks for a custom department and for the job title.
        IDialogManager dialogs;
        PopupPrefabs popups;

        // The department behind each of the department dropdown's choices, in order. The last
        // choice, Custom…, has none and is CustomChoice.
        readonly List<string> departmentChoices = new List<string>();

        const string CustomChoice = "Custom…";

        readonly List<Tile> tiles = new List<Tile>();

        // Which agent the card is showing. An id rather than the credential itself: the roster is
        // re-read when the tab is opened, and holding the object would leave the card bound to an
        // entry no longer in the list.
        int selectedId;

        /// <summary>The agent the card is showing, or null before anything is selected.</summary>
        public AgentCredential Selected => registry != null ? registry.Find(selectedId) : null;

        /// <summary>How many agents the roster holds.</summary>
        public int AgentCount => registry == null ? 0 : registry.Agents.Count;

        // The area id behind each of the dropdown's choices, in order: 0, the first, is none.
        readonly List<int> choiceIds = new List<int>();

        // Up while the dropdown is being filled, so setting its value is not taken for a choice.
        bool filling;

        /// <summary>
        /// The panel to raise and lower, resolved on demand.
        /// </summary>
        /// <remarks>
        /// Not read straight out of the field, because <see cref="InventoryUI"/> can reach
        /// <see cref="Dismiss"/> before this component's <c>Awake</c> has filled it in — the two
        /// live on different objects and Unity does not order their <c>Awake</c> calls. Falling
        /// back to this object is the same answer <c>Awake</c> arrives at, so the only difference
        /// is that an early call works instead of throwing.
        /// </remarks>
        GameObject Card => card != null ? card : gameObject;

        /// <summary>
        /// Receives the roster, the world's areas and the placement system.
        /// </summary>
        /// <remarks>
        /// All of them come from the scope rather than from a search, so a scene wired wrongly says
        /// so when it loads instead of when a button is pressed.
        /// <para>
        /// No <c>WorldRoot</c> among them, deliberately: a placed agent is parented to it, but
        /// <c>BeginDragFromInventory</c> is what does the parenting and it already has the root.
        /// Taking a second reference to it here would be a dependency this panel never reads.
        /// </para>
        /// </remarks>
        [Inject]
        public void Construct(
            IAgentRegistry agentRegistry,
            BlockAreas areas,
            PlacementController controller,
            FurniturePlacementService placementService,
            AgentAssembler agentAssembler,
            IEventHub eventHub,
            IDialogManager dialogManager)
        {
            this.hub = eventHub;
            this.dialogs = dialogManager;
            this.registry = agentRegistry;
            this.blockAreas = areas;
            this.placement = controller;
            this.service = placementService;
            this.assembler = agentAssembler;

            Listen();
        }

        /// <summary>The styled popups, where the scene has them (QA issue 8).</summary>
        [Inject]
        public void ConstructPopups(IObjectResolver resolver)
        {
            if (resolver != null)
            {
                resolver.TryResolve(out popups);
            }
        }

        /// <summary>
        /// Asks for a line of text in the styled Prompt popup, as the area's name is asked for; the
        /// dialog's Prompt where there is none. Nothing happens when it is cancelled.
        /// </summary>
        void AskText(string heading, string question, string suggestion, string hint, System.Action<string> typed, System.Action cancelled)
        {
            if (popups == null || popups.Prompt == null || PopupManager.Instance == null)
            {
                dialogs.Prompt(question, suggestion, typed, cancelled, hint);
                return;
            }

            var popup = PopupManager.Instance.Show(popups.Prompt)
                .Tint(MessagePopup.Tone.Info)
                .Present(heading, question)
                .Caption(MessagePopup.Choice.Cancel, "CANCEL")
                .Caption(MessagePopup.Choice.Primary, "SAVE")
                .Ask(suggestion);

            // The Prompt's own hint is for naming an area.
            var box = popup.GetComponentInChildren<TMP_InputField>(true);
            if (box != null && box.placeholder is TMP_Text placeholder && !string.IsNullOrEmpty(hint))
            {
                placeholder.text = hint;
            }

            popup.Chosen += choice =>
            {
                if (choice == MessagePopup.Choice.Primary && popup.Answer.Length > 0)
                {
                    typed(popup.Answer);
                }
                else
                {
                    cancelled?.Invoke();
                }
            };
        }

        void Awake()
        {
            if (card == null)
            {
                card = gameObject;
            }

            if (placeAgentButton != null)
            {
                placeAgentButton.onClick.AddListener(OnPlaceAgent);
            }

            if (areaDropdown != null)
            {
                areaDropdown.onValueChanged.AddListener(OnAreaChosen);
            }

            if (departmentDropdown != null)
            {
                departmentDropdown.onValueChanged.AddListener(OnDepartmentChosen);
            }

            if (roleButton != null)
            {
                roleButton.onClick.AddListener(OnSetRole);
            }

            // Deliberately does NOT hide the card. Hiding it here looks harmless and is not: the
            // card is authored inactive, so this Awake does not run at scene load at all — it runs
            // the first time something activates the card, which is Populate. Unity runs Awake
            // synchronously inside SetActive(true), so a SetActive(false) here fires in the middle
            // of the call that was raising the panel and takes it straight back down. Pressing the
            // Agents tab then did nothing at all, twice over: no card, and no second chance,
            // because Awake never runs again to be got right.
            //
            // Visibility has one owner instead: Dismiss and Populate, driven by InventoryUI's tab.
            // The initial hide is InventoryUI's own Start, which rebuilds into its opening tab and
            // dismisses this on the way past.
        }

        void OnDestroy() => Unlisten();

        // Subscribed once whichever of injection and Awake happens second, and never twice. The
        // scene scope defers building until its parent root scope is ready, and that root is
        // DontDestroyOnLoad and older than the scene, so injection can land either side of Awake.
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
        /// Fills <paramref name="container"/> with one tile per agent and shows the card. Called by
        /// <see cref="InventoryUI"/> when the Agents tab is selected.
        /// </summary>
        /// <remarks>
        /// The roster is read here rather than cached from the first open, so an area painted this
        /// session — or a credentials file edited between two opens of the drawer — shows on the
        /// card without a reload. It is three entries and a small file.
        /// </remarks>
        public void Populate(Transform container)
        {
            // Raised first, before anything that could decide there is nothing to show. Pressing
            // the Agents tab means "show me the agents box", and that has to happen whether the
            // roster reads, comes back empty, or is not there at all — a tab that silently does
            // nothing is indistinguishable from a broken button, where an empty card at least says
            // the tab works and the file does not.
            Card.SetActive(true);

            Build(container);

            Restore();
        }

        /// <summary>
        /// Raises the card without building a strip, for a caller that draws the roster itself.
        /// </summary>
        /// <remarks>
        /// <see cref="InventoryAgentsHud"/> lists agents as wide rows carrying an avatar, a name and
        /// a subtitle, which the square strip tiles above cannot express. It builds those itself and
        /// drives this through <see cref="Select"/>, so what is shared is the card, the four area
        /// buttons and every rule about painting — and what differs is only the list.
        /// <para>
        /// The alternative was to hand <see cref="Populate(Transform)"/> a hidden container and
        /// throw its tiles away, which works and leaves a strip of invisible objects for the next
        /// reader to explain.
        /// </para>
        /// </remarks>
        public void Populate()
        {
            Card.SetActive(true);

            Restore();
        }

        /// <summary>
        /// Whoever was selected stays selected across a tab switch. A roster that no longer holds
        /// them, or a first open, falls back to the first agent.
        /// </summary>
        void Restore()
        {
            var wanted = registry != null ? registry.Find(selectedId) : null;
            Select(wanted != null ? wanted.id : FirstId());
        }

        /// <summary>Hides the card. Called when another tab is selected.</summary>
        public void Dismiss()
        {
            Card.SetActive(false);
        }

        int FirstId()
        {
            if (registry == null)
            {
                return 0;
            }

            foreach (var agent in registry.Agents)
            {
                if (agent != null)
                {
                    return agent.id;
                }
            }

            return 0;
        }

        void Build(Transform container)
        {
            if (container == null)
            {
                Debug.LogWarning("[Agents] No container to build tiles under; the card will show "
                    + "but the drawer strip will be empty.", this);
                return;
            }

            Clear(container);

            foreach (var agent in registry.Agents)
            {
                if (agent != null)
                {
                    tiles.Add(BuildTile(agent, container));
                }
            }
        }

        void Clear(Transform container)
        {
            tiles.Clear();

            // Every child goes, not just the tiles this built: the furniture tabs build into the
            // same container, and a leftover sofa under the Agents tab would be worse than an
            // empty strip.
            for (var i = container.childCount - 1; i >= 0; i--)
            {
                var child = container.GetChild(i).gameObject;

                if (Application.isPlaying)
                {
                    // Unparented before being destroyed: Destroy is deferred to the end of the
                    // frame, so without this the layout group spends a frame arranging the old
                    // children alongside the new ones and the switch visibly jumps.
                    child.transform.SetParent(null, false);
                    Destroy(child);
                }
                else
                {
                    DestroyImmediate(child);
                }
            }
        }

        Tile BuildTile(AgentCredential agent, Transform container)
        {
            var go = new GameObject("AgentTile_" + agent.name, typeof(RectTransform));
            go.transform.SetParent(container, false);

            var rect = (RectTransform)go.transform;
            rect.sizeDelta = tileSize;

            // The strip is a HorizontalLayoutGroup set to force-expand, which ignores sizeDelta.
            // A LayoutElement is the only thing it does read, so the tile keeps its shape.
            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = tileSize.x;
            element.preferredHeight = tileSize.y;
            element.minWidth = tileSize.x;
            element.minHeight = tileSize.y;

            var background = go.AddComponent<Image>();
            background.color = tileColour;

            var button = go.AddComponent<Button>();
            button.targetGraphic = background;

            var id = agent.id;
            button.onClick.AddListener(() => Select(id));

            var label = new GameObject("Name", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            label.transform.SetParent(go.transform, false);

            var labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(4f, 4f);
            labelRect.offsetMax = new Vector2(-4f, -4f);

            label.text = agent.name;
            label.fontSize = tileFontSize;
            label.alignment = TextAlignmentOptions.Center;

            // Off, so the whole tile is the button. A label that took the pointer would leave a
            // ring around the text as the only part of the tile a click landed on.
            label.raycastTarget = false;

            return new Tile { Id = agent.id, Background = background, Label = label };
        }

        /// <summary>Shows one agent on the card.</summary>
        public void Select(int agentId)
        {
            selectedId = agentId;

            Refresh();
        }

        /// <summary>
        /// Raised after the card is redrawn — on a selection, an area painted or assigned, a
        /// department or role set — so the parts of the card other components fill stay in step.
        /// </summary>
        public event System.Action Refreshed;

        /// <summary>Puts the card and the tiles in step with the roster and the areas.</summary>
        void Refresh()
        {
            RedrawCard();
            Refreshed?.Invoke();
        }

        void RedrawCard()
        {
            var agent = Selected;

            foreach (var tile in tiles)
            {
                if (tile.Background != null)
                {
                    tile.Background.color = tile.Id == selectedId ? tileSelectedColour : tileColour;
                }
            }

            if (agent == null)
            {
                if (nameLabel != null) { nameLabel.text = string.Empty; }
                if (descriptionLabel != null) { descriptionLabel.text = string.Empty; }

                SetInteractable(false);
                return;
            }

            if (nameLabel != null)
            {
                nameLabel.text = agent.name;
            }

            if (descriptionLabel != null)
            {
                // The department, the job and the type are on the card because they are the things
                // the credentials carry that the name does not.
                descriptionLabel.text = agent.description + "\n<size=80%>" + DetailsText(agent)
                    + " · " + agent.Status + " · " + AreaText(agent) + "</size>";
            }

            if (portrait != null && portrait.sprite == null)
            {
                portrait.color = TintFor(agent.id);
            }

            SetInteractable(true);
            FillAreaChoices(agent);
            FillDepartmentChoices(agent);
        }

        /// <summary>The department and the job, in words: "SALES · Lead researcher", either alone.</summary>
        /// <remarks>
        /// Only a department the player chose. The sync's guess comes from the platform slug, which
        /// can be a word like "default" that reads as a department called Default.
        /// </remarks>
        public static string DetailsText(AgentCredential agent)
        {
            if (agent == null)
            {
                return string.Empty;
            }

            var department = agent.departmentChosen ? Upper(agent.workId) : string.Empty;

            if (string.IsNullOrWhiteSpace(agent.jobTitle))
            {
                return department;
            }

            return string.IsNullOrEmpty(department) ? agent.jobTitle : department + " · " + agent.jobTitle;
        }

        /// <summary>
        /// Lists the departments - the agent's own first when it is not a preset, then every
        /// preset, then Custom… - and shows the one it has.
        /// </summary>
        void FillDepartmentChoices(AgentCredential agent)
        {
            if (departmentDropdown == null)
            {
                return;
            }

            filling = true;

            var shown = DepartmentRows(agent, departmentChoices);
            var options = new List<TMP_Dropdown.OptionData>();

            foreach (var department in departmentChoices)
            {
                options.Add(new TMP_Dropdown.OptionData(department ?? DepartmentPrompt));
            }

            options.Add(new TMP_Dropdown.OptionData(CustomChoice));

            departmentDropdown.ClearOptions();
            departmentDropdown.AddOptions(options);
            departmentDropdown.SetValueWithoutNotify(shown);
            departmentDropdown.RefreshShownValue();

            filling = false;
        }

        /// <summary>What the department dropdown reads when the agent has none.</summary>
        const string DepartmentPrompt = "Select department";

        /// <summary>
        /// Fills <paramref name="rows"/> with the department behind each of the dropdown's rows, in
        /// order, and returns the row to show. Custom… is not included; it always comes last.
        /// </summary>
        /// <remarks>
        /// An agent with no department gets a prompt row first, held as <c>null</c>, and it is the
        /// one shown. The dropdown's value then really means "none". Showing the first preset's
        /// row with its caption painted over looked the same until anything refreshed the caption,
        /// and then a new world's agent read as being in Reception — and Reception could not be
        /// picked, because it was already the value.
        /// <para>
        /// A custom department is offered as itself, so the dropdown can show what the agent has
        /// without pretending it is a preset. A guessed one is not shown at all - see DetailsText.
        /// </para>
        /// </remarks>
        public static int DepartmentRows(AgentCredential agent, List<string> rows)
        {
            rows.Clear();

            var current = agent != null && agent.departmentChosen ? agent.workId ?? string.Empty : string.Empty;
            var shown = Departments.IndexOf(current);

            if (string.IsNullOrWhiteSpace(current))
            {
                rows.Add(null);
                shown = 0;
            }
            else if (shown < 0)
            {
                rows.Add(current);
                shown = 0;
            }

            rows.AddRange(Departments.Presets);
            return shown;
        }

        /// <summary>
        /// Puts the agent in the department picked, or asks for a custom one. Saved at once, like
        /// an area.
        /// </summary>
        void OnDepartmentChosen(int index)
        {
            var agent = Selected;

            if (filling || agent == null || index < 0)
            {
                return;
            }

            if (index < departmentChoices.Count)
            {
                if (departmentChoices[index] == null)
                {
                    Refresh();   // the prompt: choosing it chooses nothing
                    return;
                }

                ApplyDepartment(agent, departmentChoices[index]);
                return;
            }

            // Custom…
            if (dialogs == null)
            {
                Refresh();
                return;
            }

            AskText("Name the department", "Which department is " + agent.name + " in?",
                agent.departmentChosen ? agent.workId ?? string.Empty : string.Empty,
                "e.g. Customer Success",
                typed => ApplyDepartment(agent, typed),
                Refresh);
        }

        void ApplyDepartment(AgentCredential agent, string department)
        {
            var normalised = Departments.Normalise(department);

            // A blank custom name changes nothing: an agent always keeps a department once it has one.
            if (string.IsNullOrEmpty(normalised))
            {
                Refresh();
                return;
            }

            agent.workId = normalised;
            agent.departmentChosen = true;

            SaveDetails(agent);
        }

        /// <summary>Asks for the agent's job title. Blank clears it.</summary>
        void OnSetRole()
        {
            var agent = Selected;

            if (agent == null || dialogs == null)
            {
                return;
            }

            AskText("Set role", "What is " + agent.name + "'s role in this world?", agent.jobTitle ?? string.Empty,
                "e.g. Lead researcher",
                typed =>
                {
                    agent.jobTitle = string.IsNullOrWhiteSpace(typed) ? string.Empty : typed.Trim();
                    SaveDetails(agent);
                },
                null);
        }

        /// <summary>
        /// Saves the agent's department and job with the open world, brings every placed copy's
        /// label in step, and redraws the card.
        /// </summary>
        /// <remarks>
        /// Into the world, not the roster: each world is its own company layout, so a department
        /// set here belongs to this world only and a new world starts without it. Announcing the
        /// change is what saves it — autosave captures the details along with the rest of the
        /// world (<see cref="WorldAgentDetails"/>).
        /// </remarks>
        void SaveDetails(AgentCredential agent)
        {
            if (hub != null)
            {
                hub.Publish(new WorldChanged(agent.name + "'s department or role"));
            }

            // A placed copy carries the department as a label only - a load rebuilds it from the
            // roster - but a world saved now would write the old one into the file.
            foreach (var placed in FindObjectsByType<PlacedAgent>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (placed.CredentialId == agent.id)
                {
                    placed.Set(agent.id, agent.name, agent.workId);
                }
            }

            Refresh();
        }

        /// <summary>Where this agent walks, in words: its area and floor, or its whole floor.</summary>
        public string AreaText(AgentCredential agent)
        {
            var area = agent != null && blockAreas != null
                ? blockAreas.AreaFor(Dojo.Framework.World.SnapshotRole.Agent, agent.id)
                : null;

            return area != null ? area.Name + " · " + Storeys.LabelFor(area.Floor) : "Whole floor";
        }

        /// <summary>
        /// Lists what this agent may be given - none, then every area but the manager's - and shows
        /// the one it has.
        /// </summary>
        void FillAreaChoices(AgentCredential agent)
        {
            if (areaDropdown == null || blockAreas == null)
            {
                return;
            }

            filling = true;

            var options = new List<TMP_Dropdown.OptionData> { new TMP_Dropdown.OptionData("Whole floor") };
            choiceIds.Clear();
            choiceIds.Add(0);

            var current = blockAreas.AreaOf(Dojo.Framework.World.SnapshotRole.Agent, agent.id);
            var shown = 0;

            foreach (var area in blockAreas.ChoicesForAgent())
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

        /// <summary>
        /// Gives the agent the area picked. The areas announce it, so the world saves it, and every
        /// placed copy of the agent heads for its new area at once.
        /// </summary>
        void OnAreaChosen(int index)
        {
            var agent = Selected;

            if (filling || agent == null || blockAreas == null || index < 0 || index >= choiceIds.Count)
            {
                return;
            }

            if (!blockAreas.Assign(Dojo.Framework.World.SnapshotRole.Agent, agent.id, choiceIds[index]))
            {
                Debug.LogWarning("[Agents] '" + agent.name + "' could not be given that area: it is the manager's.", this);
                Refresh();
            }
        }

        void SetInteractable(bool on)
        {
            if (areaDropdown != null)
            {
                areaDropdown.interactable = on;
            }

            if (departmentDropdown != null)
            {
                departmentDropdown.interactable = on;
            }

            if (roleButton != null)
            {
                roleButton.interactable = on;
            }

            if (placeAgentButton != null)
            {
                placeAgentButton.interactable = on;
            }
        }

        void OnPlaceAgent()
        {
            var agent = Selected;

            if (agent == null)
            {
                return;
            }

            if (placement == null)
            {
                Debug.LogWarning("[Agents] No PlacementController in the scene, so '" + agent.name
                    + "' cannot be put down.", this);
                return;
            }

            // No prefab id and no Resources path, deliberately. That leaves the piece's
            // PlacedPiece unresolvable, which is what keeps an agent out of the world save's
            // furniture list — it is saved as an agent instead, by identity, so a load rebuilds it
            // with its routine and its area rather than as a piece of scenery.
            if (!placement.BeginDragFromInventory(assembler.Prefab))
            {
                return;   // refused, and already logged
            }

            // Stamped on the piece now on the cursor rather than on the prefab, so a second agent
            // placed from the same model does not overwrite the first one's identity. Quietened
            // with it, so a live NavMeshAgent does not steer against the cursor for the drag.
            assembler.Stamp(placement.Held, agent);
            AgentAssembler.Quieten(placement.Held);

            pending = placement.Held;
            pendingId = agent.id;
        }

        // The agent currently on the cursor, waiting for the gesture to end so it can be brought
        // to life. Null the rest of the time.
        FurniturePiece pending;
        int pendingId;

        /// <summary>
        /// Watches for a placed agent's gesture to end, and wakes them where they came to rest.
        /// </summary>
        /// <remarks>
        /// Polled rather than told, for the same reason the furniture tiles poll: the gesture can
        /// end half a dozen ways — committed, cancelled with Esc, right-clicked away, deleted,
        /// dropped back over the drawer — and there is no one callback for all of them. A cancelled
        /// piece is destroyed, and a destroyed <c>Object</c> compares equal to null, so an
        /// abandoned placement falls out of this on its own without a case of its own.
        /// </remarks>
        void Update()
        {
            if (pending == null || (placement != null && placement.IsBusy))
            {
                return;
            }

            Activate(pending, pendingId);
            pending = null;
        }

        /// <summary>
        /// Turns a committed piece from furniture into a walking agent.
        /// </summary>
        /// <remarks>
        /// The work itself is <see cref="AgentAssembler.Activate"/>, shared with the world loader
        /// so a placed agent and a loaded one come alive by the same steps rather than by two
        /// half-complete copies of them. This only decides <em>when</em>: once the gesture has
        /// ended and the piece has come to rest.
        /// </remarks>
        void Activate(FurniturePiece piece, int agentId)
        {
            var agent = registry != null ? registry.Find(agentId) : null;

            assembler.Activate(piece, agent);

            Debug.Log("[Agents] placed " + (agent != null ? agent.name : "an agent") + " at "
                + piece.transform.position.ToString("F2") + ", walking in: " + AreaText(agent), this);

            // The Agents tab's DRAFT flag goes out once the agent is in the world.
            Refresh();
        }

        static string Upper(string text)
            => string.IsNullOrEmpty(text) ? string.Empty : text.ToUpperInvariant();

        /// <summary>
        /// A colour per agent id, so the three cards look different before there is any portrait
        /// art. Spread around the hue circle by the golden ratio, which keeps any two ids apart
        /// however many there turn out to be.
        /// </summary>
        public static Color TintFor(int id)
            => Color.HSVToRGB(Mathf.Repeat(id * 0.618033988f, 1f), 0.45f, 0.95f);

        /// <summary>One tile in the drawer strip, and the agent it stands for.</summary>
        struct Tile
        {
            public int Id;
            public Image Background;
            public TMP_Text Label;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Finds the card's parts by the names they have in the scene, so a panel dropped onto
        /// <c>agentsbox</c> is wired without six drag-and-drops.
        /// </summary>
        /// <remarks>
        /// Only ever fills in what is empty. An explicitly assigned reference is the author's
        /// answer and is never second-guessed — a search that overwrote one would make the
        /// inspector unusable, because every save would put it back.
        /// </remarks>
        void OnValidate()
        {
            if (placeAgentButton == null)
            {
                placeAgentButton = FindChild<Button>("placeAgent");
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
