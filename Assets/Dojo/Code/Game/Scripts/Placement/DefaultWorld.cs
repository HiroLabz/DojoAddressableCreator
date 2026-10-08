using System;
using System.Collections.Generic;
using Dojo.Framework.Content;
using Dojo.Framework.World;
using Dojo.Game.InGame.Agents;
using UnityEngine;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// The world a player starts in before they have saved one of their own, shipped in the
    /// default content pack.
    /// </summary>
    /// <remarks>
    /// Authored in DojoAddressableCreator as <c>Process/default/World/world.json</c> - a world
    /// library file, the same shape the game saves - which the pack tool registers at
    /// <see cref="Address"/>.
    /// <para>
    /// A template, not a save: its agents and manager are places, not people. Whoever it was built
    /// with is somebody else's roster, so <see cref="FitTo"/> puts the signed-in player's own agents
    /// and manager into those places instead of matching ids that belong to another account.
    /// </para>
    /// </remarks>
    public static class DefaultWorld
    {
        /// <summary>Content address of the default world.</summary>
        public const string Address = "default/World/world";

        /// <summary>The default world, or null when the content has none or it cannot be read.</summary>
        public static WorldSave Read(IContentService content)
        {
            TextAsset asset;

            if (content == null || !content.TryGet(Address, out asset) || asset == null)
            {
                return null;
            }

            WorldLibrary library;

            try
            {
                library = JsonUtility.FromJson<WorldLibrary>(asset.text);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[World] The default world at '" + Address + "' could not be read: "
                    + exception.Message);
                return null;
            }

            if (library == null || library.worlds == null || library.worlds.Count == 0)
            {
                return null;
            }

            return library.Find(library.CurrentName()) ?? library.worlds[0];
        }

        /// <summary>
        /// The template with the player's own people in it: their agents in its agent places, in
        /// roster order, and their manager in its manager's place.
        /// </summary>
        /// <remarks>
        /// Every count works. No agents leaves the agent places empty; fewer agents than places
        /// fills the first ones; more agents than places puts the rest nowhere -
        /// <paramref name="unplaced"/> says how many - and they stay in the Agents panel to be put
        /// down by hand. No manager leaves his place empty. A place's painted area goes with
        /// whoever takes the place.
        /// </remarks>
        public static WorldSave FitTo(WorldSave template, IReadOnlyList<AgentCredential> agents,
            AgentCredential manager, out int unplaced)
        {
            // The template is authored before floors existed, at the top level; this puts it on
            // floor 0, which is where its positions already are.
            template.Normalised();

            var fitted = new WorldSave
            {
                name = template.name,
                savedUtc = template.savedUtc,
                connectors = template.connectors ?? new List<WorldSave.SavedConnector>(),
                navigation = template.navigation ?? new List<WorldSave.SavedSurface>(),
            };

            var people = new List<AgentCredential>();

            if (agents != null)
            {
                foreach (var agent in agents)
                {
                    if (agent != null)
                    {
                        people.Add(agent);
                    }
                }
            }

            // Floor by floor, ground first, so the places fill in the order a person would read
            // them: the agents in roster order, the manager in the first manager's place found.
            var next = 0;
            var bossSeated = false;

            // The template's areas come too, as they are. Those given to a place are kept only if
            // somebody takes the place - below - so an empty place leaves no area behind.
            var placesTaken = new HashSet<int>();

            foreach (var floor in template.floors)
            {
                var into = fitted.FloorAt(floor.index);
                into.pieces = floor.pieces ?? new List<WorldSave.SavedPiece>();
                into.blockAreas = floor.blockAreas ?? new List<WorldSave.SavedBlockArea>();

                foreach (var place in Present(floor.agents))
                {
                    if (next >= people.Count)
                    {
                        break;
                    }

                    Seat(place, people[next], SnapshotRole.Agent, template, fitted, into.agents, placesTaken);
                    next++;
                }

                var bossPlaces = Present(floor.managers);

                if (!bossSeated && manager != null && bossPlaces.Count > 0)
                {
                    Seat(bossPlaces[0], manager, SnapshotRole.Manager, template, fitted, into.managers, placesTaken);
                    bossSeated = true;
                }
            }

            DropAreasOfEmptyPlaces(template, fitted, placesTaken);

            unplaced = people.Count - next;

            return fitted;
        }

        /// <summary>Puts one person in one place, and gives them that place's area, named after them.</summary>
        static void Seat(WorldSave.SavedAgent place, AgentCredential person, SnapshotRole role,
            WorldSave template, WorldSave fitted, List<WorldSave.SavedAgent> seated, HashSet<int> placesTaken)
        {
            seated.Add(new WorldSave.SavedAgent
            {
                credentialId = person.id,
                name = person.name,
                workId = person.workId,
                position = place.position,
                rotation = place.rotation,
                rotationSteps = place.rotationSteps,
            });

            var given = template.AssignmentOf(role, place.credentialId);

            if (given == null)
            {
                return;
            }

            placesTaken.Add(given.areaId);

            fitted.assignments.Add(new WorldSave.SavedAssignment
            {
                role = role,
                credentialId = person.id,
                areaId = given.areaId,
            });

            var area = fitted.FindArea(given.areaId);

            if (area != null)
            {
                area.name = person.name;
            }
        }

        /// <summary>Takes away the areas the template gave to places nobody took.</summary>
        static void DropAreasOfEmptyPlaces(WorldSave template, WorldSave fitted, HashSet<int> placesTaken)
        {
            var givenToPlaces = new HashSet<int>();

            foreach (var given in template.assignments)
            {
                if (given != null)
                {
                    givenToPlaces.Add(given.areaId);
                }
            }

            foreach (var floor in fitted.floors)
            {
                floor.blockAreas.RemoveAll(area =>
                    area != null && givenToPlaces.Contains(area.id) && !placesTaken.Contains(area.id));
            }
        }

        static List<WorldSave.SavedAgent> Present(List<WorldSave.SavedAgent> all)
        {
            var present = new List<WorldSave.SavedAgent>();

            if (all != null)
            {
                foreach (var entry in all)
                {
                    if (entry != null)
                    {
                        present.Add(entry);
                    }
                }
            }

            return present;
        }
    }
}
