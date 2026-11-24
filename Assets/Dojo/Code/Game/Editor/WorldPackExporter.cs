using System.Collections.Generic;
using System.IO;
using Dojo.Game.Placement;
using UnityEditor;
using UnityEngine;

namespace Dojo.Game.Editor
{
    /// <summary>
    /// Tools ▸ Dojo ▸ Content ▸ Export World To Pack: the world last saved with SAVE WORLD becomes the default
    /// pack's starting world (<c>Assets/Process/default/World/world_mvp.json</c>, address
    /// <c>default/World/world_mvp</c>), the one a new player's game opens on. The first kit's world.json went with the old assets (18 Nov).
    /// </summary>
    /// <remarks>
    /// Replaces copying the file out of the persistent data folder by hand. Only the current world
    /// goes, as a library of one, which is what <c>DefaultWorld.Read</c> in the game takes. Its
    /// people's names are blanked: the game fits the player's own agents and manager into their
    /// places (<c>DefaultWorld.FitTo</c>), and the creator's roster names mean nothing there.
    /// Departments, conference rooms, areas and who is assigned to which go as they are.
    /// <para>
    /// Run the Addressable Generator afterwards, and upload the pack, for the game to see it.
    /// </para>
    /// </remarks>
    public static class WorldPackExporter
    {
        public const string PackPath = "Assets/Process/default/World/world_mvp.json";

        /// <summary>The MVP scene's own save file, read whatever scene was last open.</summary>
        public static WorldLibraryFile Saves() => new WorldLibraryFile(MvpKitImporter.MvpSaveFile);

        [MenuItem("Tools/Dojo/Content/3. Export World To Pack", false, 3)]
        public static void Export()
        {
            var source = Saves();

            if (!source.Exists)
            {
                EditorUtility.DisplayDialog("Export World To Pack",
                    "There is no saved world yet. Play RuntimeMVP, build the world, and SAVE WORLD first.\n\n" + source.FullPath, "OK");
                return;
            }

            var library = source.Read();
            var world = library.Find(library.CurrentName()) ?? (library.worlds.Count > 0 ? library.worlds[0] : null);

            if (world == null)
            {
                EditorUtility.DisplayDialog("Export World To Pack", "The saved library has no world in it.", "OK");
                return;
            }

            ExportWorld(world, true);
        }

        /// <summary>
        /// Makes <paramref name="world"/> the default pack's starting world, after saying what it holds
        /// and what the rule check found. False when it was not exported.
        /// </summary>
        public static bool ExportWorld(WorldSave world, bool ask)
        {
            var summary = Describe(world);
            var problems = WorldRules.Check(world);
            var findings = problems.Count == 0
                ? "Rule check: no problems."
                : "Rule check: " + problems.Count + " problem(s), first ones:\n- "
                  + string.Join("\n- ", problems.GetRange(0, System.Math.Min(6, problems.Count)));

            if (ask && !EditorUtility.DisplayDialog("Make Default World",
                    "Make '" + world.name + "' the world new players start in?\n\n" + summary
                    + "\n\n" + findings + "\n\nThis replaces " + PackPath + ".", "Make default", "Cancel"))
            {
                return false;
            }

            // A copy, so the saved world keeps its people's names.
            var copy = JsonUtility.FromJson<WorldSave>(JsonUtility.ToJson(world));
            BlankNames(copy);

            var pack = new WorldLibrary();
            pack.worlds.Add(copy);
            pack.current = copy.name;

            Directory.CreateDirectory(Path.GetDirectoryName(PackPath));
            File.WriteAllText(PackPath, JsonUtility.ToJson(pack, true));
            AssetDatabase.ImportAsset(PackPath);

            Debug.Log("[World] '" + world.name + "' is now the default world (" + PackPath + "): " + summary.Replace("\n", "; ")
                + ". " + findings.Replace("\n", " ")
                + " For the game to see it, build and publish the content (Addressable Builder, see Documentation/Content-Release-Guide.md).");
            return true;
        }

        public static string Describe(WorldSave world)
        {
            int areas = 0, withDepartment = 0, conference = 0, agents = 0, managers = 0;
            var departments = new List<string>();

            foreach (var floor in world.floors)
            {
                foreach (var area in floor.blockAreas)
                {
                    if (area == null)
                    {
                        continue;
                    }

                    areas++;

                    if (!string.IsNullOrEmpty(area.department))
                    {
                        withDepartment++;

                        if (!departments.Contains(area.department))
                        {
                            departments.Add(area.department);
                        }
                    }

                    if (area.conferenceRoom)
                    {
                        conference++;
                    }
                }

                agents += floor.agents != null ? floor.agents.Count : 0;
                managers += floor.managers != null ? floor.managers.Count : 0;
            }

            return areas + " area(s), " + withDepartment + " with a department ("
                + (departments.Count > 0 ? string.Join(", ", departments) : "none") + "), "
                + conference + " conference room(s)\n"
                + agents + " agent place(s), " + managers + " manager place(s)"
                + (managers == 0 ? "  <- no manager place: the owner will not be placed" : string.Empty)
                + (agents == 0 ? "  <- no agent places: no agent will be placed" : string.Empty);
        }

        static void BlankNames(WorldSave world)
        {
            foreach (var floor in world.floors)
            {
                foreach (var place in floor.agents)
                {
                    if (place != null) place.name = string.Empty;
                }

                foreach (var place in floor.managers)
                {
                    if (place != null) place.name = string.Empty;
                }
            }
        }
    }
}
