using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// Builds the world's navigation meshes, and describes the settings they were built with.
    /// </summary>
    /// <remarks>
    /// Saving bakes, and loading bakes again. That is not an optimisation: walkable space cannot be
    /// carved into existence, so a floor that has just been laid is invisible to pathfinding until a
    /// surface is rebuilt. See <see cref="WorldSave"/> for why the mesh itself is not in the file.
    /// </remarks>
    public sealed class NavigationBaker
    {
        readonly WorldRoot world;

        public NavigationBaker(WorldRoot world)
        {
            this.world = world;
        }

        /// <summary>Builds every navigation surface. Returns how many were actually built.</summary>
        public int Bake(List<WorldSave.SavedSurface> settings = null)
        {
            var surfaces = world.Surfaces;

            if (surfaces == null || surfaces.Length == 0)
            {
                Debug.LogWarning("[World] No NavMeshSurface in the scene, so nothing can be walked "
                    + "on. Add one per agent type.", world);
                return 0;
            }

            var built = 0;
            var disabled = 0;

            foreach (var surface in surfaces)
            {
                if (surface == null)
                {
                    continue;
                }

                if (!surface.isActiveAndEnabled)
                {
                    disabled++;
                    continue;
                }

                Apply(surface, settings);
                surface.BuildNavMesh();
                built++;
            }

            if (disabled > 0)
            {
                Debug.LogError("[World] " + disabled + " NavMeshSurface(s) are on disabled objects, so "
                    + "they were not baked and nothing will path across them. Enable the object they "
                    + "are on — a surface that is switched off contributes no walkable space at all.", world);
            }

            return built;
        }

        /// <summary>Puts a saved bake's settings back on a surface, when there are any.</summary>
        void Apply(NavMeshSurface surface, List<WorldSave.SavedSurface> settings)
        {
            if (settings == null)
            {
                return;
            }

            foreach (var saved in settings)
            {
                if (saved.agentTypeId != surface.agentTypeID)
                {
                    continue;
                }

                surface.overrideVoxelSize = true;
                surface.voxelSize = saved.voxelSize;
                surface.overrideTileSize = true;
                surface.tileSize = saved.tileSize;
                surface.minRegionArea = saved.minRegionArea;

                // Agent size belongs to the project's navigation settings rather than to the
                // surface, so it cannot be restored from here — only reported when it has drifted,
                // because a world baked for a different-sized agent will not walk the same.
                var current = NavMesh.GetSettingsByID(surface.agentTypeID);
                if (!Mathf.Approximately(current.agentRadius, saved.agentRadius)
                    || !Mathf.Approximately(current.agentHeight, saved.agentHeight))
                {
                    Debug.LogWarning("[World] '" + NavMesh.GetSettingsNameFromID(surface.agentTypeID)
                        + "' was saved with radius " + saved.agentRadius + " / height " + saved.agentHeight
                        + " but the project now says " + current.agentRadius + " / " + current.agentHeight
                        + ". The world will bake, but agents may fit differently than they did.", world);
                }

                return;
            }
        }

        /// <summary>The settings every live surface was last built with, for the save file.</summary>
        public List<WorldSave.SavedSurface> Describe()
        {
            var described = new List<WorldSave.SavedSurface>();
            var surfaces = world.Surfaces;

            if (surfaces == null)
            {
                return described;
            }

            foreach (var surface in surfaces)
            {
                if (surface == null || !surface.isActiveAndEnabled)
                {
                    continue;
                }

                var settings = NavMesh.GetSettingsByID(surface.agentTypeID);
                var bounds = surface.navMeshData != null ? surface.navMeshData.sourceBounds : new Bounds();

                described.Add(new WorldSave.SavedSurface
                {
                    agentTypeId = surface.agentTypeID,
                    agentRadius = settings.agentRadius,
                    agentHeight = settings.agentHeight,
                    agentSlope = settings.agentSlope,
                    agentClimb = settings.agentClimb,
                    voxelSize = surface.voxelSize,
                    tileSize = surface.tileSize,
                    minRegionArea = surface.minRegionArea,
                    boundsCentre = bounds.center,
                    boundsSize = bounds.size,
                });
            }

            return described;
        }
    }
}
