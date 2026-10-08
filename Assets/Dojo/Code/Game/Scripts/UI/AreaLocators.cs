using System;
using System.Collections.Generic;
using Dojo.Framework.Content;
using Dojo.Framework.Events;
using Dojo.Framework.World;
using Dojo.Game.Events;
using Dojo.Game.Placement;
using Dojo.Game.Systems;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using VContainer;

namespace Dojo.Game.UI
{
    /// <summary>
    /// A locator for every block area: a pin at the spot in the area the owner stops at, and a
    /// label beside it with the area's name, its floor, how far the owner is from it and how many
    /// agents are assigned to it.
    /// </summary>
    /// <remarks>
    /// The pin stands where he would stop if sent there - the middle of the area, or the free spot
    /// nearest it - so it is never inside a desk. It follows the areas: one appears when an area is
    /// made, moves when one is repainted or something is put down in it, and goes with it. Only
    /// the floor being looked at shows its locators, and only in Play: arranging the room has
    /// its own markings. An area the owner is in shows none - he is there already - and gets it
    /// back as he leaves.
    /// <para>
    /// The pins are in the world, under a root of their own outside the floors, so a world save
    /// never takes them for pieces and the walkable floor is never baked over them; the labels are
    /// on this canvas, drawn under the rest of the in-game screens. Placeholder art until the
    /// locator designs are done.
    /// </para>
    /// </remarks>
    public sealed class AreaLocators : MonoBehaviour
    {
        /// <summary>
        /// How often the agents in each area are counted, and whether the owner is in it. The
        /// owner's distance is measured every frame.
        /// </summary>
        const float WordsSeconds = 0.25f;

        /// <summary>How often every pin is put back on its spot, in case something was moved without a word.</summary>
        const float PlaceSeconds = 2f;

        /// <summary>A label further off the screen than this is not drawn.</summary>
        const float OffScreen = 40f;

        /// <summary>
        /// The pin model, from the default content pack: designed in DojoAddressableCreator, so a
        /// new design only needs a content build. Until it is loaded the locator's stand-in shows.
        /// </summary>
        public const string PinAddress = "default/default_dojo_animated_pin";

        [SerializeField] AreaLocator locatorPrefab;

        [Tooltip("One label, copied for each area. Kept switched off.")]
        [SerializeField] AreaLocatorLabel labelTemplate;

        [Tooltip("Where the labels go: this canvas, the size of the screen.")]
        [SerializeField] RectTransform labels;

        BlockAreas areas;
        Storeys storeys;
        ManagerAssembler managers;
        IGamePhase phase;
        IEventHub hub;
        IContentService content;
        IDisposable worldChanged;

        sealed class Entry
        {
            public BlockArea area;
            public AreaLocator locator;
            public AreaLocatorLabel label;
            public bool seen;

            /// <summary>The owner is in the area: there is nothing to point him to, so it is not shown.</summary>
            public bool ownerIn;

            /// <summary>The agents in the area at the last count.</summary>
            public int agents;

            /// <summary>What the label says now - metres and agents - so it is only rewritten when either changes.</summary>
            public int metresShown = int.MinValue;
            public int agentsShown = int.MinValue;
        }

        readonly Dictionary<int, Entry> entries = new Dictionary<int, Entry>();
        readonly List<int> gone = new List<int>();

        Transform root;
        Canvas canvas;
        bool dirty = true;
        float nextPlace;
        float nextWords;
        bool started;

        [Inject]
        public void Construct(BlockAreas areas, Storeys storeys, ManagerAssembler managers, IGamePhase phase, IEventHub hub,
            IContentService content)
        {
            this.areas = areas;
            this.storeys = storeys;
            this.managers = managers;
            this.phase = phase;
            this.hub = hub;
            this.content = content;
        }

        void Awake()
        {
            if (labelTemplate != null)
            {
                labelTemplate.gameObject.SetActive(false);
            }
        }

        void Start()
        {
            if (areas == null || storeys == null || locatorPrefab == null)
            {
                // A scene with no world, or not set up: nothing to mark.
                enabled = false;
                return;
            }

            canvas = GetComponentInParent<Canvas>();

            // The pins' own root, in this scene but outside the world.
            var holder = new GameObject("AreaLocators");
            SceneManager.MoveGameObjectToScene(holder, gameObject.scene);
            holder.layer = LayerMask.NameToLayer("Ignore Raycast");
            root = holder.transform;

            areas.Changed += MarkDirty;
            storeys.Changed += MarkDirty;

            if (hub != null)
            {
                worldChanged = hub.Subscribe<WorldChanged>(_ => MarkDirty());
            }

            started = true;
        }

        void OnDestroy()
        {
            if (!started)
            {
                return;
            }

            areas.Changed -= MarkDirty;
            storeys.Changed -= MarkDirty;

            if (worldChanged != null)
            {
                worldChanged.Dispose();
            }

            if (root != null)
            {
                Destroy(root.gameObject);
            }
        }

        void MarkDirty() => dirty = true;

        void LateUpdate()
        {
            var now = Time.unscaledTime;

            if (dirty || now >= nextPlace)
            {
                dirty = false;
                nextPlace = now + PlaceSeconds;
                Rebuild();
            }

            var showing = phase == null || !phase.IsEdit;
            var viewed = storeys.Viewed;

            // A floor an elevator ride is fading away or in has no pins until it has settled, so
            // none hangs over a floor that is only half there.
            var arrived = !storeys.IsAway(viewed) && !storeys.IsFading(viewed);
            var words = now >= nextWords;

            if (words)
            {
                nextWords = now + WordsSeconds;
            }

            var manager = managers != null ? managers.Active : null;
            var view = Camera.main;

            foreach (var entry in entries.Values)
            {
                // Counted now and then, not every frame: an area can have well over a thousand cells.
                if (words)
                {
                    entry.ownerIn = manager != null && entry.area.Contains(manager.transform.position);
                    entry.agents = AssignedAgents(entry.area);
                }

                var on = showing && arrived && entry.area.Floor == viewed && !entry.ownerIn;

                if (entry.locator.gameObject.activeSelf != on)
                {
                    entry.locator.gameObject.SetActive(on);
                }

                var labelled = on && view != null && Place(entry, view);

                if (entry.label.gameObject.activeSelf != labelled)
                {
                    entry.label.gameObject.SetActive(labelled);
                }

                if (labelled)
                {
                    // Counted as it comes into view, so it never shows a stale number.
                    if (!entry.seen)
                    {
                        entry.agents = AssignedAgents(entry.area);
                    }

                    // The distance every frame, so it counts down as he walks; the words are only
                    // rewritten when the whole metres - or the agents - change.
                    var metres = manager != null
                        ? Vector3.Distance(manager.transform.position, entry.locator.transform.position)
                        : -1f;
                    var shown = metres >= 0f ? Mathf.RoundToInt(metres) : -1;

                    if (!entry.seen || shown != entry.metresShown || entry.agents != entry.agentsShown)
                    {
                        entry.metresShown = shown;
                        entry.agentsShown = entry.agents;
                        entry.label.Set(entry.area.Name, Line(entry.area.Floor, metres, entry.agents));
                    }
                }

                entry.seen = labelled;
            }
        }

        /// <summary>
        /// Puts the label at its pin's head on the screen. False when the head is behind the camera
        /// or well off the screen, where there is nothing to point at.
        /// </summary>
        bool Place(Entry entry, Camera view)
        {
            var screen = view.WorldToScreenPoint(entry.locator.Head);

            if (screen.z <= 0f
                || screen.x < -OffScreen || screen.x > Screen.width + OffScreen
                || screen.y < -OffScreen || screen.y > Screen.height + OffScreen)
            {
                return false;
            }

            var eye = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.rootCanvas.worldCamera
                : null;

            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(labels, screen, eye, out local))
            {
                return false;
            }

            entry.label.Place(local);
            return true;
        }

        /// <summary>One locator per area, each on the spot the owner would stop at; none for an area that has gone.</summary>
        void Rebuild()
        {
            var filter = Filter();
            var alive = new HashSet<int>();

            // The designed pin, once the content has it; asked again each time until then.
            GameObject model;
            if (content == null || !content.TryGet(PinAddress, out model))
            {
                model = null;
            }

            foreach (var area in areas.All)
            {
                alive.Add(area.Id);

                Entry entry;
                if (!entries.TryGetValue(area.Id, out entry))
                {
                    entry = new Entry
                    {
                        area = area,
                        locator = Instantiate(locatorPrefab, root, false),
                        label = Instantiate(labelTemplate, labels, false),
                    };

                    entries[area.Id] = entry;
                }

                entry.area = area;

                if (model != null && !entry.locator.HasModel)
                {
                    entry.locator.UseModel(model);
                }

                entry.locator.name = "Locator " + area.Name;
                entry.label.name = "Label " + area.Name;
                entry.locator.transform.position = Spot(area, filter);
                entry.seen = false;
            }

            gone.Clear();

            foreach (var pair in entries)
            {
                if (!alive.Contains(pair.Key))
                {
                    gone.Add(pair.Key);
                }
            }

            foreach (var id in gone)
            {
                var entry = entries[id];
                Destroy(entry.locator.gameObject);
                Destroy(entry.label.gameObject);
                entries.Remove(id);
            }
        }

        /// <summary>The owner's walkable floor, so a pin stands where he can; any floor when he is not there.</summary>
        NavMeshQueryFilter Filter()
        {
            var manager = managers != null ? managers.Active : null;
            var walker = manager != null ? manager.GetComponent<NavMeshAgent>() : null;

            return walker != null
                ? new NavMeshQueryFilter { agentTypeID = walker.agentTypeID, areaMask = walker.areaMask }
                : new NavMeshQueryFilter { agentTypeID = 0, areaMask = NavMesh.AllAreas };
        }

        /// <summary>
        /// Where the owner would stop in the area: its middle, or the free spot nearest it. Its
        /// middle when there is no walkable floor to be had - before the floor is baked, say.
        /// </summary>
        static Vector3 Spot(BlockArea area, NavMeshQueryFilter filter)
        {
            var reach = area.CellSize * 0.5f;

            foreach (var point in area.CentreFirst())
            {
                NavMeshHit hit;
                if (NavMesh.SamplePosition(point, out hit, reach, filter))
                {
                    return hit.position;
                }
            }

            return area.Centre;
        }

        /// <summary>
        /// How many agents have the area - given it in the Areas tab - wherever they are standing
        /// now. The owner is not one of them: he is not an agent.
        /// </summary>
        int AssignedAgents(BlockArea area)
        {
            var count = 0;

            foreach (var holder in areas.Holders(area.Id))
            {
                if (holder.Role == SnapshotRole.Agent)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// The label's second line: the floor, the owner's distance in whole metres, and the agents
        /// in the area - "3F · 12m · 2 AGENTS". The distance is left out when there is no owner.
        /// </summary>
        public static string Line(int floor, float metres, int agents)
        {
            var parts = new List<string>(3) { Storeys.LabelFor(floor) };

            if (metres >= 0f)
            {
                parts.Add(Mathf.RoundToInt(metres) + "m");
            }

            parts.Add(agents + (agents == 1 ? " AGENT" : " AGENTS"));
            return string.Join(" · ", parts);
        }
    }
}
