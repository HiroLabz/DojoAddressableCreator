using UnityEngine.AI;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// The navigation areas that join floors, by the names the project's navigation settings give
    /// them.
    /// </summary>
    /// <remarks>
    /// Their own areas rather than Walkable, so only the manager can use them: agents walk
    /// <c>homeAreaMask</c> (Walkable) and <c>travelAreaMask</c> (Walkable and Outside), and neither
    /// includes these - which is what keeps every agent on its own floor without a line of agent
    /// code knowing that floors exist. The manager's agent walks every area.
    /// </remarks>
    public static class NavAreas
    {
        /// <summary>The steps of a staircase.</summary>
        public static int Stairs => Find("Stairs");

        /// <summary>The links between an elevator's stops.</summary>
        public static int Elevator => Find("Elevator");

        /// <summary>
        /// The area by name, or Walkable when the project has none by that name - which would let
        /// agents use it too, so it is worth noticing when it happens.
        /// </summary>
        static int Find(string name)
        {
            var area = NavMesh.GetAreaFromName(name);

            if (area < 0)
            {
                UnityEngine.Debug.LogWarning("[Floors] There is no navigation area called '" + name
                    + "' in the project settings, so it falls back to Walkable and agents could use it too.");
                return 0;
            }

            return area;
        }
    }
}
