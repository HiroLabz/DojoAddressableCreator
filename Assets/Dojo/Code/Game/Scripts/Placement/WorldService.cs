using System;
using System.Collections.Generic;
using Dojo.Framework.Content;
using Dojo.Framework.World;
using Dojo.Game.InGame.Agents;
using Dojo.Game.Server;
using UnityEngine;
using VContainer;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// Writes the world the player has built to the library, and builds it again from it.
    /// </summary>
    /// <remarks>
    /// An ordinary object, not a component. Nothing here reads a transform, runs per frame or
    /// answers a collision — it orchestrates a file and a spawner — so a GameObject would buy it
    /// nothing but a place to hang <c>[SerializeField]</c>s and a <c>Start</c> in which to go
    /// hunting for its collaborators. Those are now <see cref="WorldSettings"/> and constructor
    /// arguments, which is the whole reason the hunting could go.
    /// <para>
    /// Only what is inside <see cref="WorldRoot"/> is saved. Everything else in the scene is
    /// scenery that exists whether or not anybody has built anything, and a save file that carried
    /// it would be a save file that could overwrite the game.
    /// </para>
    /// <para>
    /// The three responsibilities are deliberately in three objects: this one decides what a world
    /// is, <see cref="WorldLibraryFile"/> knows the JSON and the path, and
    /// <see cref="NavigationBaker"/> knows the surfaces. Only the first needs a loaded scene.
    /// </para>
    /// </remarks>
    public sealed class WorldService
    {
        readonly WorldLibraryFile library;
        readonly IWorldPublisher worldPublisher;
        readonly IWorldSource worldSource;

        // The one door that moves the backend's current world. Needed here only to clear it when a
        // new world starts; saving and loading already record the choice through the publisher.
        readonly IWorldSelection selection;

        readonly NavigationBaker navigation;
        readonly FurniturePlacementService placement;
        readonly PlacementGrid grid;
        readonly WorldRoot world;

        // World > Floor > objects. Everything is saved from, and built into, one of its floors.
        readonly Storeys storeys;

        // The elevators, which belong to the world rather than to a floor: saved once as a shaft,
        // and rebuilt as a stop on every floor.
        readonly ElevatorShafts shafts;
        readonly WorldSettings settings;
        readonly AgentRosterFile roster;

        // Who the agents are. The platform's list, not the shipped catalogue, which is empty on
        // purpose — the roster file above is only where painted areas are written.
        readonly IAgentRegistry registry;

        readonly AgentAssembler agents;
        readonly ManagerRosterFile managerRoster;
        readonly ManagerAssembler managers;

        /// <summary>The world's named areas and who uses which: written into every save, taken from every load.</summary>
        readonly BlockAreas blockAreas;

        /// <summary>
        /// Hands each agent their area's cells. Held so it exists from the start - it does its work
        /// on every change to the areas, including the load below.
        /// </summary>
        readonly AgentAreaBinding areaBinding;

        // Where furniture comes from now. Read synchronously through TryGet, which is safe because
        // the startup gate will not let a game scene load until the catalogue is downloaded.
        readonly IContentService content;

        // Loaded pieces are built through the container for the same reason placed ones are: a
        // component on the art that expects injection has to get it however it came into the world.
        readonly IObjectResolver resolver;

        /// <summary>
        /// Everything this needs, handed over by the container.
        /// </summary>
        /// <remarks>
        /// No null checks and no fallbacks. A scene missing any of these fails to resolve when its
        /// LifetimeScope builds, which is at scene load and long before the player has built
        /// anything to lose — a better answer than the <c>FindAnyObjectByType</c> that used to sit
        /// here and left the service half-wired and quietly skipping work.
        /// </remarks>
        public WorldService(
            WorldLibraryFile library,
            NavigationBaker navigation,
            FurniturePlacementService placement,
            PlacementGrid grid,
            WorldRoot world,
            Storeys storeys,
            ElevatorShafts shafts,
            WorldSettings settings,
            AgentRosterFile roster,
            IAgentRegistry registry,
            AgentAssembler agents,
            ManagerRosterFile managerRoster,
            ManagerAssembler managers,
            IContentService content,
            IWorldPublisher worldPublisher,
            IWorldSource worldSource,
            IWorldSelection selection,
            IObjectResolver resolver,
            BlockAreas blockAreas,
            AgentAreaBinding areaBinding)
        {
            this.blockAreas = blockAreas;
            this.areaBinding = areaBinding;
            this.selection = selection;
            this.library = library;
            this.navigation = navigation;
            this.placement = placement;
            this.grid = grid;
            this.world = world;
            this.storeys = storeys;
            this.shafts = shafts;
            this.settings = settings;
            this.roster = roster;
            this.registry = registry;
            this.agents = agents;
            this.managerRoster = managerRoster;
            this.managers = managers;
            this.content = content;
            this.worldPublisher = worldPublisher;
            this.worldSource = worldSource;
            this.resolver = resolver;
        }

        /// <summary>Where the library is read from and written to.</summary>
        public string FullPath => library.FullPath;

        /// <summary>Whether agents and the manager are brought into the world. See <see cref="WorldSettings.Characters"/>.</summary>
        public bool Characters => settings.Characters;

        /// <summary>
        /// The name of the world standing in the scene, or null when it has none yet - a new world,
        /// or the default one, until it is first saved.
        /// </summary>
        /// <remarks>
        /// Set the moment a world is loaded or saved, which <see cref="CurrentWorld"/> is not: that
        /// asks the backend, and the backend hears about a first save a moment after it happens.
        /// What needs to know which world is on screen - its picture - asks this.
        /// </remarks>
        public string OpenWorldName { get; private set; }

        /// <summary>
        /// Every world the player has, from the file and the database together, plus those saved
        /// this session. New names are only ever handed out from here - see <see cref="SaveOpenWorld"/>.
        /// </summary>
        readonly WorldRegistry known = new WorldRegistry();

        /// <summary>Takes in every world the file and the database know about.</summary>
        /// <remarks>
        /// Both, not whichever is ready. A world only one of them has is still a world, and a name
        /// picked from the other alone could be its name.
        /// </remarks>
        void RefreshKnown()
        {
            known.AddAll(library.Read().Names());

            if (worldSource.IsReady)
            {
                known.AddAll(worldSource.Names());
            }
        }

        /// <summary>
        /// Saves the world that is open: under its own name, or - a new world, or the starter one,
        /// before its first save - under the next World# no world has. Returns the name used.
        /// </summary>
        /// <remarks>
        /// What saving without asking goes through, so the name never comes from anywhere else.
        /// A world is only ever written over by itself; the save dialog, where the player types a
        /// name and is told OVERWRITE, is the one way to write over another.
        /// </remarks>
        public string SaveOpenWorld()
        {
            RefreshKnown();

            var unnamed = string.IsNullOrEmpty(OpenWorldName);
            var name = known.NameFor(OpenWorldName);

            if (unnamed)
            {
                Debug.Log("[World] This world had no name, so it is saved as a new world, '" + name + "'.", world);
            }

            SaveAs(name);
            return name;
        }

        /// <summary>True when there is something to load.</summary>
        public bool HasSave => library.Exists;

        /// <summary>
        /// Every saved world, from the database when it can be read and the file otherwise.
        /// </summary>
        /// <remarks>
        /// The file is the fallback rather than the source. IsReady is false both when there is no
        /// backend and before the first snapshot lands, and in either case the file is the only
        /// honest answer - an empty list would tell the player their worlds are gone.
        /// </remarks>
        public List<string> WorldNames()
            => worldSource.IsReady ? worldSource.Names() : library.Read().Names();

        /// <summary>
        /// The world the game should open, or null when nothing has been picked yet.
        /// </summary>
        /// <remarks>
        /// Null covers both "never chosen" and "chosen, but that world has since gone", because a
        /// caller can do nothing different about the two: either way the player has to be asked.
        /// <see cref="WorldLibrary.CurrentName"/> is what collapses them.
        /// <para>
        /// Read from disk on each call rather than cached, for the same reason
        /// <see cref="WorldNames"/> is: it is wanted once at startup, and a cached answer would go
        /// stale the moment anything else wrote to the same file.
        /// </para>
        /// </remarks>
        public string CurrentWorld
            => worldSource.IsReady ? worldSource.Current() : library.Read().CurrentName();

        /// <summary>
        /// Removes a saved world, from the library and then from the backend.
        /// </summary>
        /// <remarks>
        /// The file first and the backend after, the same order <see cref="SaveAs"/> uses: the
        /// local library is what the player sees, so it is what must be right. The backend call is
        /// advisory and is not waited on — a delete that appeared to fail because a database was
        /// unreachable would be a worse answer than one that quietly reconciles later.
        /// <para>
        /// Deleting the world currently open leaves the scene standing. What is on screen was never
        /// the file; it is only no longer anywhere to return to, which the next save settles.
        /// </para>
        /// </remarks>
        public void Delete(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            var held = library.Read();

            if (!held.Remove(name))
            {
                Debug.LogWarning("[World] '" + name + "' was not in the library, so nothing was deleted.", world);
                return;
            }

            library.Write(held);
            known.Remove(name);

            // Its picture goes with it, or a later world saved under the same name would open on
            // the old one's.
            WorldPictureFile.InPersistentData().Delete(name);

            if (worldPublisher != null)
            {
                _ = worldPublisher.DeleteAsync(name);
            }
        }

        /// <summary>
        /// Bakes navigation and writes what is standing into the library under this name, replacing
        /// a world already called that.
        /// </summary>
        /// <remarks>
        /// No separate "are you sure" for a replacement. Whoever chose the name knows whether it was
        /// an existing one — the save dialog's own button says OVERWRITE when that is what pressing
        /// it would do — and a second modal saying the same thing again is friction, not safety.
        /// </remarks>
        public void SaveAs(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                Debug.LogError("[World] A world needs a name to be saved under.", world);
                return;
            }

            SaveAs(library.Read(), name);
        }

        /// <summary>Bakes navigation and writes the world into the library under this name.</summary>
        void SaveAs(WorldLibrary held, string name)
        {
            // Baked before the settings are read, so what is written describes the mesh that now
            // exists rather than the one that did a moment ago. Only for characters: nothing else
            // walks, and this runs on every auto save.
            if (settings.Characters)
            {
                navigation.Bake();
            }

            var save = new WorldSave
            {
                name = name,
                savedUtc = DateTime.UtcNow.ToString("o"),
            };

            var skipped = 0;

            // Every floor, the empty ones too: a floor the player has added but not yet furnished
            // is still part of the world, and has to be there on the next load.
            foreach (var storey in storeys.All)
            {
                var floor = save.FloorAt(storey.Index);

                foreach (Transform child in storey.transform)
                {
                    var piece = child.GetComponent<FurniturePiece>();

                    // Relative to the floor, which is the whole point: removing a floor below only
                    // renumbers this one, and nothing on it has to move.
                    var position = storeys.ToFloor(storey.Index, child.position);

                    // Agents first, because they are the one thing on a floor that is not
                    // furniture and would otherwise fall into the skipped count below: they carry
                    // no catalogue entry on purpose, so PlacedPiece can never resolve one.
                    // The manager before the agents. He is neither furniture nor one of them, and
                    // without his own stamp he fell through to the skipped count below: a placed
                    // manager carries no catalogue entry on purpose, so PlacedPiece never resolves one.
                    var boss = child.GetComponent<PlacedManager>();

                    if (boss != null)
                    {
                        floor.managers.Add(new WorldSave.SavedAgent
                        {
                            credentialId = boss.CredentialId,
                            name = boss.ManagerName,
                            workId = boss.WorkId,
                            position = position,
                            rotation = child.rotation,
                            rotationSteps = piece != null ? piece.RotationSteps : 0,
                        });

                        continue;
                    }

                    var agent = child.GetComponent<PlacedAgent>();

                    if (agent != null)
                    {
                        floor.agents.Add(new WorldSave.SavedAgent
                        {
                            credentialId = agent.CredentialId,
                            name = agent.AgentName,
                            workId = agent.WorkId,
                            position = position,
                            rotation = child.rotation,
                            rotationSteps = piece != null ? piece.RotationSteps : 0,
                        });

                        continue;
                    }

                    // An elevator stop is not a piece of this floor: the shaft it belongs to is saved
                    // once, on the world, below.
                    if (ElevatorShafts.IsStop(child))
                    {
                        continue;
                    }

                    var placed = child.GetComponent<PlacedPiece>();

                    if (piece == null || placed == null || !placed.IsResolvable)
                    {
                        skipped++;
                        continue;
                    }

                    floor.pieces.Add(new WorldSave.SavedPiece
                    {
                        prefabId = placed.PrefabId,
                        resource = placed.Resource,
                        name = child.name,
                        position = position,
                        rotation = child.rotation,
                        scale = child.localScale,
                        rotationSteps = piece.RotationSteps,
                        isFloor = piece.IsFloor,
                    });
                }
            }

            if (!settings.Characters)
            {
                KeepCharacters(held.Find(name), save);
            }

            SaveBlockAreas(save);

            save.connectors = shafts.Describe();
            save.navigation = navigation.Describe();

            held.Put(save);

            // Saving is also choosing. Without this, "Save as World9" then a restart would open
            // whatever was selected before, which reads as the save having been lost.
            held.Select(name);
            OpenWorldName = save.name;
            known.Add(save.name);

            library.Write(held);

            // The routine save is not logged: the backend publisher below lists every piece, agent
            // and area it sends, so a second summary of the same save was only noise. What is kept
            // is the case that summary was actually useful for — pieces that could not be written.
            if (skipped > 0)
            {
                Debug.LogWarning("[World] '" + name + "' skipped " + skipped
                    + " child object(s) with nothing to identify them; they are not in the save.", world);
            }

            // After the file, never instead of it, and not awaited. The world is already safe on
            // disk by this line; the backend copy is an extra, and a database that is down must
            // not be able to make saving fail.
            _ = PublishAsync(save);
        }

        /// <summary>
        /// Sends the world that was just written to the backend.
        /// </summary>
        /// <remarks>
        /// Translated into <see cref="WorldSnapshot"/> rather than handing over <c>WorldSave</c>,
        /// because the publisher lives in an assembly that cannot see this one — the dependency
        /// runs the other way, and inverting it for one method would be a poor trade.
        /// </remarks>
        async Awaitable PublishAsync(WorldSave save)
        {
            try
            {
                await worldPublisher.PublishAsync(Describe(save));
            }
            catch (System.Exception exception)
            {
                // Swallowed on purpose: the save itself already succeeded.
                Debug.LogWarning("[World] '" + save.name + "' was saved locally but not to the backend: "
                    + exception.Message, world);
            }
        }

        /// <summary>One saved world in terms the backend can take.</summary>
        static WorldSnapshot Describe(WorldSave save)
        {
            save.Normalised();

            var snapshot = new WorldSnapshot { name = save.name };

            foreach (var floor in save.floors)
            {
                var into = snapshot.FloorAt(floor.index);

                foreach (var piece in floor.pieces)
                {
                    into.pieces.Add(new SnapshotPiece
                    {
                        // The address, not the GUID: it is what the content system resolves and
                        // what a world written weeks ago still needs to name.
                        address = piece.resource,
                        position = piece.position,
                        rotation = piece.rotation,
                        scale = piece.scale,
                        rotationSteps = piece.rotationSteps,
                        isFloor = piece.isFloor,
                    });
                }

                AddAgents(into, floor.agents, SnapshotRole.Agent);
                AddAgents(into, floor.managers, SnapshotRole.Manager);

                foreach (var area in floor.blockAreas)
                {
                    if (area == null)
                    {
                        continue;
                    }

                    var written = new SnapshotBlockArea
                    {
                        id = area.id,
                        name = area.name,
                        cellSize = area.areaCellSize,
                    };

                    written.points.AddRange(area.area ?? new List<Vector3>());
                    into.blockAreas.Add(written);
                }
            }

            foreach (var given in save.assignments)
            {
                if (given != null)
                {
                    snapshot.assignments.Add(new SnapshotAssignment
                    {
                        role = given.role,
                        credentialId = given.credentialId,
                        areaId = given.areaId,
                    });
                }
            }

            foreach (var connector in save.connectors)
            {
                snapshot.connectors.Add(new SnapshotConnector
                {
                    kind = connector.kind,
                    address = connector.address,
                    x = connector.x,
                    z = connector.z,
                    rotationSteps = connector.rotationSteps,
                });
            }

            foreach (var surface in save.navigation)
            {
                snapshot.navigation.Add(new SnapshotSurface
                {
                    agentTypeId = surface.agentTypeId,
                    agentRadius = surface.agentRadius,
                    agentHeight = surface.agentHeight,
                    agentSlope = surface.agentSlope,
                    agentClimb = surface.agentClimb,
                    voxelSize = surface.voxelSize,
                    tileSize = surface.tileSize,
                    minRegionArea = surface.minRegionArea,
                    boundsCentre = surface.boundsCentre,
                    boundsSize = surface.boundsSize,
                });
            }

            return snapshot;
        }

        static void AddAgents(SnapshotFloor floor, List<WorldSave.SavedAgent> from, SnapshotRole role)
        {
            foreach (var agent in from)
            {
                floor.agents.Add(new SnapshotAgent
                {
                    role = role,
                    credentialId = agent.credentialId,
                    position = agent.position,
                    rotation = agent.rotation,
                    rotationSteps = agent.rotationSteps,
                });
            }
        }

        /// <summary>
        /// Copies the agents and managers of the world as it was last saved into the one being
        /// written.
        /// </summary>
        /// <remarks>
        /// With characters off none are standing in the world, so a save written from what is
        /// standing would drop every one of them. Keeping the previous save's means switching
        /// characters back on finds them where they were.
        /// </remarks>
        static void KeepCharacters(WorldSave previous, WorldSave save)
        {
            if (previous == null)
            {
                return;
            }

            previous.Normalised();

            foreach (var floor in previous.floors)
            {
                var into = save.FloorAt(floor.index);

                into.agents.AddRange(floor.agents ?? new List<WorldSave.SavedAgent>());
                into.managers.AddRange(floor.managers ?? new List<WorldSave.SavedAgent>());
            }
        }

        /// <summary>
        /// Copies every block area, and who uses which, into the save.
        /// </summary>
        /// <remarks>
        /// From the areas themselves, not from the agents standing in the world: an area exists
        /// whether or not anybody uses it, and whether or not they have been put down yet. Each
        /// area's cells go under its own floor, relative to it.
        /// </remarks>
        void SaveBlockAreas(WorldSave save)
        {
            foreach (var area in blockAreas.All)
            {
                var local = new List<Vector3>();

                foreach (var cell in area.Cells)
                {
                    local.Add(storeys.ToFloor(area.Floor, cell));
                }

                save.FloorAt(area.Floor).blockAreas.Add(new WorldSave.SavedBlockArea
                {
                    id = area.Id,
                    name = area.Name,
                    area = local,
                    areaCellSize = area.CellSize,
                });
            }

            save.assignments = blockAreas.Assignments();
        }

        /// <summary>
        /// Takes on the save's block areas and who uses which, replacing the last world's, and
        /// hands each agent their area's cells.
        /// </summary>
        /// <remarks>
        /// Before the agents are put down, so each is confined to its area the moment it starts.
        /// An area converted from a world saved before block areas may have no name - the database
        /// never stored one - so it is named after whoever it was painted for.
        /// </remarks>
        int LoadBlockAreas(WorldSave save)
        {
            var loaded = new List<BlockArea>();

            foreach (var floor in save.floors)
            {
                foreach (var saved in floor.blockAreas)
                {
                    if (saved == null || saved.id == 0)
                    {
                        continue;
                    }

                    var cells = new List<Vector3>();

                    foreach (var point in saved.area ?? new List<Vector3>())
                    {
                        cells.Add(storeys.FromFloor(floor.index, point));
                    }

                    var name = string.IsNullOrWhiteSpace(saved.name) ? OwnerName(save, saved.id) : saved.name;
                    loaded.Add(new BlockArea(saved.id, name, floor.index,
                        saved.areaCellSize > 0f ? saved.areaCellSize : grid.CellSize, cells));
                }
            }

            blockAreas.Load(loaded, save.assignments);
            areaBinding.Apply();

            return loaded.Count;
        }

        /// <summary>The name of whoever uses an area, for one that was never given its own.</summary>
        string OwnerName(WorldSave save, int areaId)
        {
            foreach (var given in save.assignments)
            {
                if (given == null || given.areaId != areaId)
                {
                    continue;
                }

                AgentCredential owner = null;

                if (given.role == SnapshotRole.Agent && registry != null)
                {
                    owner = registry.Find(given.credentialId);
                }
                else if (given.role == SnapshotRole.Manager && managerRoster != null)
                {
                    var all = managerRoster.Read();
                    owner = all != null ? all.Find(given.credentialId) : null;
                }

                if (owner != null && !string.IsNullOrWhiteSpace(owner.name))
                {
                    return owner.name;
                }
            }

            return "Area";
        }

        /// <summary>
        /// Whether the signed-in player has a manager to stand in the world as them.
        /// </summary>
        public bool HasOwner => managerRoster != null && managerRoster.ReadOne() != null;

        /// <summary>
        /// Clears whatever is standing and builds the default world from the content pack, with the
        /// signed-in player's own agents and manager in its places. False when the content holds no
        /// default world, so the caller can fall back.
        /// </summary>
        /// <remarks>
        /// For a player with no world at all it is their first world, and is saved at once as
        /// World1. For one who has worlds but none open, it is a template again: not saved until
        /// they change it, and then as a new World#.
        /// </remarks>
        public bool LoadDefault()
        {
            var template = DefaultWorld.Read(content);

            if (template == null)
            {
                Debug.LogWarning("[World] There is no default world at '" + DefaultWorld.Address
                    + "' in the content, so there is nothing to start from.", world);
                return false;
            }

            // Without characters nobody is fitted into the template's places, so none are read.
            var owner = settings.Characters && managerRoster != null ? managerRoster.ReadOne() : null;
            var people = settings.Characters && registry != null ? registry.Agents : null;
            int unplaced;
            var fitted = DefaultWorld.FitTo(template, people, owner, out unplaced);

            Debug.Log("[World] starting from the default world '" + template.name + "': "
                + fitted.AgentCount() + " of the player's agent(s) placed"
                + (unplaced > 0 ? ", " + unplaced + " with no place left (place them from the Agents panel)" : "")
                + (owner == null ? ", and no manager" : "")
                + (settings.Characters ? "" : " (characters are off)") + ".", world);

            Build(fitted);
            OpenWorldName = null;

            // A player's first world is the starter world. Saved as World1 straight away, so it is
            // theirs from the start: a NEW WORLD after it is World2, not their only world.
            RefreshKnown();

            if (known.Count == 0)
            {
                SaveOpenWorld();
            }

            return true;
        }

        /// <summary>
        /// A new, empty world: nothing is current, and no agent or manager has an area.
        /// </summary>
        /// <remarks>
        /// The Lobby's New World. The floor is already empty - nothing was built - so what is left
        /// is making the data agree with it. No world selected, in the file and on the backend, so
        /// the first save names a new World# instead of writing over the world that was open. And
        /// no areas, so the agent and manager cards offer Define Area rather than Edit Area over
        /// an area painted somewhere else.
        /// </remarks>
        public void StartBlank()
        {
            LoadBlockAreas(new WorldSave { name = "the new world" });
            OpenWorldName = null;

            var held = library.Read();

            if (held.Select(null))
            {
                library.Write(held);
            }

            if (selection != null)
            {
                selection.ClearCurrent();
            }

            Debug.Log("[World] New world: nothing selected, and no agent or manager has an area. "
                + "It is saved as a new World# when the inventory closes.", world);
        }

        /// <summary>Clears whatever is standing and builds the named world in its place.</summary>
        public void Load(string name)
        {
            if (worldSource.IsReady)
            {
                var fromDatabase = worldSource.Load(name);

                if (fromDatabase != null)
                {
                    Debug.Log("[World] loading '" + fromDatabase.name + "' from the database.", world);
                    Build(Rebuild(fromDatabase));
                    OpenWorldName = fromDatabase.name;
                    RememberChoice(fromDatabase.name);
                    return;
                }

                // Readable, but this world is not in it. Fall through to the file rather than
                // refuse: a world saved before the backend existed is still the player's.
                Debug.LogWarning("[World] '" + name + "' is not in the database; trying the file.", world);
            }

            var held = library.Read();
            var save = held.Find(name);

            if (save == null)
            {
                Debug.LogError("[World] No saved world called '" + name + "' in " + FullPath, world);
                return;
            }

            Debug.Log("[World] loading '" + save.name + "' from " + FullPath, world);
            Build(save);
            OpenWorldName = save.name;

            // Recorded here rather than by the caller, so every route into a world — the file
            // tray, the startup chooser, anything added later — remembers the choice without
            // having to know that it should. Only written when it actually changed: re-opening the
            // world that is already selected is the common case and costs no write.
            if (held.Select(save.name))
            {
                library.Write(held);
            }
        }

        /// <summary>Records which world is open, in the file that still tracks it.</summary>
        void RememberChoice(string name)
        {
            var held = library.Read();

            if (held.Select(name))
            {
                library.Write(held);
            }
        }

        /// <summary>
        /// A world from the database, in the shape the builder already understands.
        /// </summary>
        /// <remarks>
        /// The inverse of Describe. Kept as a translation rather than teaching Build a second
        /// shape, so there is exactly one thing that turns saved data into a standing world and it
        /// cannot drift between two sources.
        /// </remarks>
        static WorldSave Rebuild(WorldSnapshot snapshot)
        {
            var save = new WorldSave { name = snapshot.name };

            foreach (var source in snapshot.floors)
            {
                var floor = save.FloorAt(source.index);

                foreach (var piece in source.pieces)
                {
                    floor.pieces.Add(new WorldSave.SavedPiece
                    {
                        // No GUID: the database stores the address, which is what resolves the asset.
                        prefabId = string.Empty,
                        resource = piece.address,
                        name = piece.address,
                        position = piece.position,
                        rotation = piece.rotation,
                        scale = piece.scale,
                        rotationSteps = piece.rotationSteps,
                        isFloor = piece.isFloor,
                    });
                }

                foreach (var agent in source.agents)
                {
                    var written = new WorldSave.SavedAgent
                    {
                        credentialId = agent.credentialId,
                        position = agent.position,
                        rotation = agent.rotation,
                        rotationSteps = agent.rotationSteps,
                    };

                    if (agent.role == SnapshotRole.Manager)
                    {
                        floor.managers.Add(written);
                    }
                    else
                    {
                        floor.agents.Add(written);
                    }
                }

                foreach (var area in source.areas)
                {
                    var written = new WorldSave.SavedAgentArea
                    {
                        credentialId = area.credentialId,
                        areaCellSize = area.cellSize,
                    };

                    written.area.AddRange(area.points);

                    if (area.role == SnapshotRole.Manager)
                    {
                        floor.managerAreas.Add(written);
                    }
                    else
                    {
                        floor.agentAreas.Add(written);
                    }
                }

                foreach (var area in source.blockAreas)
                {
                    var written = new WorldSave.SavedBlockArea
                    {
                        id = area.id,
                        name = area.name,
                        areaCellSize = area.cellSize,
                    };

                    written.area.AddRange(area.points);
                    floor.blockAreas.Add(written);
                }
            }

            foreach (var given in snapshot.assignments)
            {
                save.assignments.Add(new WorldSave.SavedAssignment
                {
                    role = given.role,
                    credentialId = given.credentialId,
                    areaId = given.areaId,
                });
            }

            foreach (var connector in snapshot.connectors)
            {
                save.connectors.Add(new WorldSave.SavedConnector
                {
                    kind = connector.kind,
                    address = connector.address,
                    x = connector.x,
                    z = connector.z,
                    rotationSteps = connector.rotationSteps,
                });
            }

            foreach (var surface in snapshot.navigation)
            {
                save.navigation.Add(new WorldSave.SavedSurface
                {
                    agentTypeId = surface.agentTypeId,
                    agentRadius = surface.agentRadius,
                    agentHeight = surface.agentHeight,
                    agentSlope = surface.agentSlope,
                    agentClimb = surface.agentClimb,
                    voxelSize = surface.voxelSize,
                    tileSize = surface.tileSize,
                    minRegionArea = surface.minRegionArea,
                    boundsCentre = surface.boundsCentre,
                    boundsSize = surface.boundsSize,
                });
            }

            return save;
        }

        /// <summary>Clears whatever is standing and builds the given world in its place.</summary>
        void Build(WorldSave save)
        {
            if (save == null)
            {
                Debug.LogError("[World] That world could not be read.", world);
                return;
            }

            // A world saved before floors existed, or the default one, comes in with everything at
            // the top; this puts it on floor 0, where its positions already are.
            save.Normalised();

            // The elevators stand back until the world is up: every half-built state in between
            // would otherwise read as a stop deleted or a shaft moved.
            shafts.BeginBuild();

            Clear();

            // Every floor the save has, empty ones included, before anything is put on one.
            var count = 1;
            foreach (var floor in save.floors)
            {
                count = Mathf.Max(count, floor.index + 1);
            }

            storeys.Reset(count);

            var layer = ResolveLayer();
            var built = 0;
            var missing = 0;

            foreach (var floor in save.floors)
            {
                var into = storeys.Floor(floor.index);

                foreach (var saved in floor.pieces)
                {
                    if (BuildPiece(saved, floor.index, into, layer) != null)
                    {
                        built++;
                    }
                    else
                    {
                        missing++;
                    }
                }
            }

            // The floor list first, so each elevator stop stands on its floor's tiles; then a stop on
            // every floor for each shaft the world has.
            Physics.SyncTransforms();
            placement.RefreshFloors();
            var elevators = shafts.Restore(save.connectors);

            // Everything is standing before anything is asked about it: the floor list, what rests
            // on what, and the cells held all depend on the whole world being there.
            Physics.SyncTransforms();

            placement.RefreshFloors();

            foreach (var child in storeys.Placed())
            {
                var piece = child.GetComponent<FurniturePiece>();
                if (piece != null)
                {
                    piece.SizeTo(grid.CellSize);
                    placement.Register(piece);
                }
            }

            placement.ResolveSupports();

            // Navigation is for the characters alone, so without them there is nothing to bake -
            // and baking every surface was most of the time a load took.
            var baked = settings.Characters ? navigation.Bake(save.navigation) : 0;

            // Areas before agents, and both after the bake. An agent is confined to its area the
            // moment it is brought to life, so its area has to be on its credentials by then — and
            // being brought to life means being put on a NavMesh, which does not exist until the
            // floors that were just built have been baked into one.
            var restored = LoadBlockAreas(save);
            var assembled = settings.Characters ? AssembleAgents(save) : 0;
            var bosses = settings.Characters ? AssembleManagers(save) : 0;

            // The elevators back to reacting, with their links laid over the mesh just baked.
            shafts.EndBuild();

            // Everyone is standing now, so the floors above the viewed one can be hidden with them.
            storeys.Refresh();

            // No source named here. Build is given a world and does not know where it was read
            // from — the caller has already said, and a path printed from this depth was simply
            // whatever the file happens to be, which read as the truth and was not.
            Debug.Log("[World] built '" + save.name + "' - " + storeys.Count + " floor(s), "
                + (elevators > 0 ? elevators + " elevator(s), " : "") + built + " piece(s), "
                + assembled + " agent(s), " + bosses + " manager(s), " + restored + " area(s)"
                + (missing > 0 ? ", " + missing + " could not be found" : "")
                + ", baked " + baked + " navigation surface(s).", world);
        }

        /// <summary>
        /// Builds the manager the save was holding, and hands him to the player.
        /// </summary>
        /// <remarks>
        /// Through <see cref="ManagerAssembler"/>, which is the same code the Place Manager button
        /// runs — so a loaded manager is assembled exactly as a placed one was rather than by a
        /// second half-complete copy of those steps.
        /// <para>
        /// Registered and settled before being activated. The assembler releases the cells again as
        /// it activates — he walks away from them — but going through the claim first is what puts
        /// the model at the right height on the floor beneath it.
        /// </para>
        /// </remarks>
        int AssembleManagers(WorldSave save)
        {
            if (managers == null || save.ManagerCount() == 0)
            {
                return 0;
            }

            var all = managerRoster != null ? managerRoster.Read() : null;
            var layer = ResolveLayer();
            var built = 0;

            foreach (var floor in save.floors)
            foreach (var saved in floor.managers)
            {
                if (saved == null)
                {
                    continue;
                }

                var credential = all != null ? all.Find(saved.credentialId) : null;

                if (credential == null)
                {
                    Debug.LogWarning("[World] '" + save.name + "' has manager " + saved.credentialId
                        + " ('" + saved.name + "') standing in it, but there are no credentials with "
                        + "that id any more. Skipped.", world);
                    continue;
                }

                // Back on his own floor: the assembler puts him under the floor at this height.
                var piece = managers.Assemble(credential, storeys.FromFloor(floor.index, saved.position), layer);

                if (piece == null)
                {
                    // No manager prefab on the scene's world settings, or the spawner refused it.
                    Debug.LogWarning("[World] Could not build manager '" + saved.name + "'. Check "
                        + "that a manager prefab is assigned under World on the scene's "
                        + "LifetimeScope.", world);
                    continue;
                }

                piece.SetRotationSteps(saved.rotationSteps);
                piece.SizeTo(grid.CellSize);
                piece.Settle();

                placement.Register(piece);
                managers.Activate(piece);

                built++;
            }

            return built;
        }

        /// <summary>
        /// Builds every agent the save was holding, and starts them walking.
        /// </summary>
        /// <remarks>
        /// Through <see cref="AgentAssembler"/>, which is the same code the Place Agent button runs,
        /// so a loaded agent is assembled exactly as a placed one was rather than by a second
        /// half-complete copy of those steps.
        /// <para>
        /// Registered and settled before being activated, in that order. The assembler releases the
        /// cells again as it activates — an agent walks away from them — but going through the
        /// claim first is what puts the model at the right height on the floor beneath it.
        /// </para>
        /// </remarks>
        int AssembleAgents(WorldSave save)
        {
            if (agents == null || save.AgentCount() == 0)
            {
                return 0;
            }

            var layer = ResolveLayer();
            var assembled = 0;

            foreach (var floor in save.floors)
            foreach (var saved in floor.agents)
            {
                if (saved == null)
                {
                    continue;
                }

                var credential = registry != null ? registry.Find(saved.credentialId) : null;

                if (credential == null)
                {
                    Debug.LogWarning("[World] '" + save.name + "' has agent " + saved.credentialId
                        + " ('" + saved.name + "') standing in it, but there are no credentials with "
                        + "that id any more. Skipped.", world);
                    continue;
                }

                // Back on their own floor: the assembler puts them under the floor at this height.
                var piece = agents.Assemble(credential, storeys.FromFloor(floor.index, saved.position), layer);

                if (piece == null)
                {
                    continue;   // the spawner has already said why
                }

                piece.SetRotationSteps(saved.rotationSteps);
                piece.SizeTo(grid.CellSize);
                piece.Settle();

                placement.Register(piece);
                agents.Activate(piece, credential);

                assembled++;
            }

            return assembled;
        }

        /// <summary>Takes down whatever is currently built.</summary>
        /// <remarks>
        /// Destroyed immediately rather than at the end of the frame. A load puts the new world up
        /// in the same call, and deferred destruction would have both worlds standing at once —
        /// claiming each other's cells, and answering each other's collision probes.
        /// </remarks>
        void Clear()
        {
            var root = world.Root;

            // Every piece on every floor gives up its cells first: the floors are what the root
            // holds now, and the pieces are one level down.
            foreach (var piece in root.GetComponentsInChildren<FurniturePiece>(true))
            {
                placement.Release(piece);
            }

            for (var i = root.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.DestroyImmediate(root.GetChild(i).gameObject);
            }
        }

        /// <summary>
        /// Builds one saved piece onto a floor. Null, with the reason logged, when its prefab cannot
        /// be found or measured.
        /// </summary>
        FurniturePiece BuildPiece(WorldSave.SavedPiece saved, int floorIndex, Transform into, int layer)
        {
            var prefab = LoadPrefab(saved);

            if (prefab == null)
            {
                Debug.LogWarning("[World] Nothing to build '" + saved.name + "' from — neither '"
                    + saved.resource + "' nor the manifest resolved it. Skipped.", world);
                return null;
            }

            // Through the spawner, so the wrapper, the collider, the obstacle and the navigation
            // flags are made exactly as they were the first time. The saved position is the
            // wrapper's own, relative to its floor, so it is put back on it.
            var piece = FurnitureSpawner.Spawn(
                prefab, grid, storeys.FromFloor(floorIndex, saved.position), layer,
                placement.MaxFootprintCells, into, resolver);

            if (piece == null)
            {
                return null;
            }

            var placed = piece.GetComponent<PlacedPiece>();
            if (placed != null)
            {
                placed.Set(saved.prefabId, saved.resource);
            }

            piece.SetRotationSteps(saved.rotationSteps);
            piece.transform.localScale = saved.scale;
            piece.Settle();

            return piece;
        }

        /// <summary>
        /// Builds a room into the open world: its pieces and its named areas, onto one floor, turned
        /// <paramref name="steps"/> quarter turns about its own origin and then moved by
        /// <paramref name="offset"/>. Returns how many pieces were built.
        /// </summary>
        /// <remarks>
        /// A room is a world file of one floor, the same shape the game saves, so its pieces go
        /// through <see cref="BuildPiece"/> exactly as a loaded world's do and are ordinary pieces
        /// afterwards - moved, rotated and deleted one by one, and saved by their own addresses.
        /// Where it goes is the caller's choice; nothing here checks for room.
        /// </remarks>
        public int AddRoom(WorldSave room, int floorIndex, Vector3 offset, int steps, string fallbackName)
        {
            if (room == null)
            {
                return 0;
            }

            room.Normalised();

            var into = storeys.Floor(floorIndex);
            var layer = ResolveLayer();
            var turn = Quaternion.Euler(0f, PlacementGrid.Normalise(steps) * 90f, 0f);
            var added = new List<FurniturePiece>();
            var missing = 0;
            var areas = 0;

            foreach (var floor in room.floors)
            {
                // One floor: a room's own floor 0 goes on whichever floor it is put on.
                if (floor.index != 0)
                {
                    continue;
                }

                foreach (var saved in floor.pieces)
                {
                    // Moved as the room is, and turned with it: about the room's origin, and on
                    // its own pivot by the same quarter turns.
                    var moved = new WorldSave.SavedPiece
                    {
                        prefabId = saved.prefabId,
                        resource = saved.resource,
                        name = saved.name,
                        position = turn * saved.position + offset,
                        rotation = turn * saved.rotation,
                        scale = saved.scale,
                        rotationSteps = PlacementGrid.Normalise(saved.rotationSteps + steps),
                        isFloor = saved.isFloor,
                    };

                    var piece = BuildPiece(moved, floorIndex, into, layer);

                    if (piece != null)
                    {
                        added.Add(piece);
                    }
                    else
                    {
                        missing++;
                    }
                }
            }

            // As a load does: everything standing before the floor list, the cells held and what
            // rests on what are worked out.
            Physics.SyncTransforms();
            placement.RefreshFloors();

            foreach (var piece in added)
            {
                piece.SizeTo(grid.CellSize);
                placement.Register(piece);
            }

            placement.ResolveSupports();

            foreach (var floor in room.floors)
            {
                if (floor.index != 0)
                {
                    continue;
                }

                foreach (var saved in floor.blockAreas)
                {
                    if (saved == null || saved.area == null || saved.area.Count == 0)
                    {
                        continue;
                    }

                    var cells = new List<Vector3>(saved.area.Count);

                    foreach (var point in saved.area)
                    {
                        cells.Add(storeys.FromFloor(floorIndex, turn * point + offset));
                    }

                    var name = string.IsNullOrWhiteSpace(saved.name) ? fallbackName : saved.name;
                    blockAreas.Add(name, floorIndex, cells, saved.areaCellSize > 0f ? saved.areaCellSize : grid.CellSize);
                    areas++;
                }
            }

            storeys.Refresh();

            Debug.Log("[World] added room '" + fallbackName + "' on " + Storeys.LabelFor(floorIndex) + " at ("
                + offset.x + ", " + offset.z + "), turned " + PlacementGrid.Normalise(steps) * 90 + " degrees - " + added.Count + " piece(s), " + areas + " area(s)"
                + (missing > 0 ? ", " + missing + " could not be found" : "") + ".", world);

            return added.Count;
        }

        /// <summary>The prefab a saved entry refers to: by content address, or by GUID via the manifest.</summary>
        /// <remarks>
        /// <c>saved.resource</c> is used unchanged as the content address. That is not a
        /// coincidence — addresses were deliberately set to the Resources-relative strings these
        /// saves already contain, so worlds written before content moved to Addressables still
        /// resolve without migrating a single file.
        /// </remarks>
        GameObject LoadPrefab(WorldSave.SavedPiece saved)
        {
            if (!string.IsNullOrEmpty(saved.resource))
            {
                GameObject direct;
                if (content != null && content.TryGet(saved.resource, out direct) && direct != null)
                {
                    return direct;
                }
            }

            return null;
        }

        int ResolveLayer()
        {
            var layer = LayerMask.NameToLayer(settings.PlacedLayerName);

            if (layer >= 0)
            {
                return layer;
            }

            Debug.LogError("[World] There is no layer called '" + settings.PlacedLayerName
                + "'. Loaded pieces will be on the default layer and cannot be picked up.", world);

            return 0;
        }

    }
}
