using System;
using UnityEngine;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// The two authored choices world saving actually has. Everything else about it is fixed.
    /// </summary>
    /// <remarks>
    /// A plain serialisable class rather than fields on a component, which is what let
    /// <see cref="WorldService"/> stop being a MonoBehaviour: four <c>[SerializeField]</c>s were the
    /// only reason it ever needed to be one. Exposed on the scene's LifetimeScope and registered as
    /// an instance, so the values are still authored in the inspector but the thing that reads them
    /// is an ordinary object.
    /// <para>
    /// The save path is deliberately absent. <see cref="Application.persistentDataPath"/> is the
    /// only folder a built game may write to, so any other choice would be a setting that works in
    /// the editor and fails in a build. While it was serialised it was worse than useless: a stray
    /// value could be saved into the scene and quietly point the game at another file.
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class WorldSettings
    {
        [Tooltip("Write the JSON indented. Larger, but a person can read and diff it.")]
        [SerializeField] bool prettyPrint = true;

        [Tooltip("Layer loaded pieces join. Must match the pickable mask on PlacementController, " +
                 "or a loaded world cannot be edited.")]
        [SerializeField] string placedLayerName = "Furniture";

        [Tooltip("The model every agent wears. Authored here rather than on the agents panel " +
                 "because loading a saved world has to build agents too, and the loader is not a " +
                 "component — it has no inspector to have been wired in. Left empty, agents are " +
                 "placed as capsules.")]
        [SerializeField] GameObject agentPrefab;

        [Tooltip("The manager the You tab puts down. Beside the agent prefab because it is the " +
                 "same kind of authored choice and the same loader will want it.")]
        [SerializeField] GameObject managerPrefab;

        [Header("Characters")]
        [Tooltip("Bring agents and the manager into the world. Off while the project concentrates " +
                 "on building: a load places no one and bakes no navigation, the Agents and You " +
                 "tabs are hidden, and a save keeps whatever agents and manager the world already " +
                 "had rather than dropping them.")]
        [SerializeField] bool characters = false;

        [Header("Auto save")]
        [Tooltip("Write the current world back to disk whenever anything in it is added, moved, " +
                 "rotated, deleted or painted. Only ever writes over the world already selected; " +
                 "a world that has never been saved is left alone, because there is no name to " +
                 "save it under.")]
        [SerializeField] bool autoSave = true;

        [Tooltip("Seconds of no further change before an automatic save is written. Every change " +
                 "restarts it, so a drag saves once when the player stops rather than on every " +
                 "frame of the drag.")]
        [SerializeField] float autoSaveQuietSeconds = 1f;

        [Header("Floors")]
        [Tooltip("The most floors a world may have. 0 means no limit. The save, the database and " +
                 "the code assume no maximum, so raising this is the whole change.")]
        [SerializeField] int maxFloors = 5;

        /// <summary>The most floors a world may have; 0 means there is no limit.</summary>
        public int MaxFloors => Mathf.Max(0, maxFloors);

        /// <summary>Whether a change to the world writes itself to disk shortly afterwards.</summary>
        public bool Characters => characters;

        public bool AutoSave => autoSave;

        /// <summary>
        /// Seconds of quiet before an automatic save happens.
        /// </summary>
        /// <remarks>
        /// The one number that matters, because a world save is not cheap: it bakes every
        /// navigation surface before writing, and nothing else in the game bakes at all — the
        /// player pressing Save was the only thing that ever did it. Saving on each change without
        /// a quiet period would bake once per frame of a drag.
        /// <para>
        /// Floored rather than trusted: a zero or negative value authored by accident would put
        /// the bake back on every change, which is precisely what this exists to prevent.
        /// </para>
        /// </remarks>
        public float AutoSaveQuietSeconds => Mathf.Max(0.1f, autoSaveQuietSeconds);

        /// <summary>Whether the file is written indented.</summary>
        public bool PrettyPrint => prettyPrint;

        /// <summary>Name of the layer loaded pieces join.</summary>
        public string PlacedLayerName => placedLayerName;

        /// <summary>The model every agent wears, or null to fall back to a capsule.</summary>
        public GameObject AgentPrefab => agentPrefab;

        /// <summary>The manager's model, or null when none is authored.</summary>
        /// <remarks>
        /// No capsule fallback, unlike the agents. An agent placed as a placeholder still wanders
        /// and still demonstrates the tab; a manager is the player, and quietly giving them a
        /// stand-in body would be a worse answer than saying the prefab is missing.
        /// </remarks>
        public GameObject ManagerPrefab => managerPrefab;
    }
}
