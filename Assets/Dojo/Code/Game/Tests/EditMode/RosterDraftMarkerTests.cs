using System.Collections.Generic;
using Dojo.Game.InGame.Agents;
using Dojo.Game.UI;
using NUnit.Framework;
using UnityEngine;

namespace Dojo.Game.Tests.EditMode
{
    /// <summary>
    /// The Agents tab's DRAFT flag (QA issue 26): lit for an agent that has an area but is not in
    /// the world yet, and out once it is placed, so it never sits beside a Passport that says ACTIVE.
    /// </summary>
    public sealed class RosterDraftMarkerTests
    {
        static AgentCredential Agent(int id, int cells)
        {
            var agent = new AgentCredential { id = id, name = "Agent " + id };

            for (int i = 0; i < cells; i++)
            {
                agent.area.Add(new Vector3(i, 0f, 0f));
            }

            return agent;
        }

        [Test]
        public void AnAgentWithAnArea_NotInTheWorld_IsDraft()
        {
            Assert.That(InventoryAgentsHud.IsDraft(Agent(3, 12), new HashSet<int>()), Is.True);
        }

        [Test]
        public void AnAgentWithAnArea_InTheWorld_IsNotDraft()
        {
            Assert.That(InventoryAgentsHud.IsDraft(Agent(3, 12), new HashSet<int> { 3 }), Is.False);
        }

        [Test]
        public void AnotherAgentInTheWorld_LeavesTheFlagOn()
        {
            Assert.That(InventoryAgentsHud.IsDraft(Agent(3, 12), new HashSet<int> { 4 }), Is.True);
        }

        [Test]
        public void AnAgentWithoutAnArea_IsNotDraft()
        {
            Assert.That(InventoryAgentsHud.IsDraft(Agent(3, 0), new HashSet<int>()), Is.False);
        }

        [Test]
        public void NoAgent_IsNotDraft()
        {
            Assert.That(InventoryAgentsHud.IsDraft(null, new HashSet<int>()), Is.False);
        }
    }
}
