using System;
using System.Collections.Generic;
using Dojo.Framework.Events;
using Dojo.Framework.World;
using Dojo.Game.Events;
using Dojo.Game.Server;
using UnityEngine;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// Keeps every placed agent confined to the block area it has been given - or, with none, to
    /// its whole floor - as the areas and the floors change.
    /// </summary>
    /// <remarks>
    /// The area itself lives in the world (<see cref="BlockAreas"/>). The credentials are given a
    /// copy of its cells for the session, for whatever still reads them there, and nothing here
    /// writes that copy anywhere.
    /// <para>
    /// Run on every change to the areas - one painted again, given away, deleted - so placed agents
    /// move to their new area straight away, and on every change to the world, because "the whole
    /// floor" grows with every tile laid on it.
    /// </para>
    /// </remarks>
    public sealed class AgentAreaBinding : IDisposable
    {
        readonly BlockAreas areas;
        readonly IAgentRegistry registry;
        readonly AgentAssembler agents;
        readonly IDisposable worldChanges;

        public AgentAreaBinding(BlockAreas areas, IAgentRegistry registry, AgentAssembler agents, IEventHub hub)
        {
            this.areas = areas;
            this.registry = registry;
            this.agents = agents;

            areas.Changed += Apply;

            if (hub != null)
            {
                worldChanges = hub.Subscribe<WorldChanged>(_ => Apply());
            }
        }

        /// <summary>Every agent's credentials given their area's cells, and placed agents confined.</summary>
        public void Apply()
        {
            if (registry == null)
            {
                return;
            }

            foreach (var credential in registry.Agents)
            {
                if (credential == null)
                {
                    continue;
                }

                var area = areas.AreaFor(SnapshotRole.Agent, credential.id);

                credential.area = area != null ? new List<Vector3>(area.Cells) : new List<Vector3>();
                credential.areaCellSize = area != null ? area.CellSize : credential.areaCellSize;

                if (agents != null)
                {
                    agents.Reconfine(credential);
                }
            }
        }

        public void Dispose()
        {
            areas.Changed -= Apply;

            if (worldChanges != null)
            {
                worldChanges.Dispose();
            }
        }
    }
}
