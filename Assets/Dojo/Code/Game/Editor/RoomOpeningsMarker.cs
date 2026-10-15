using System.Collections.Generic;
using System.IO;
using Dojo.Game.Placement;
using UnityEditor;
using UnityEngine;

namespace Dojo.Game.Editor
{
    /// <summary>
    /// Marks the doors of the pack's rooms in their files, from the gaps already in their walls,
    /// and checks every room file's openings against the rules of the room file format.
    /// </summary>
    /// <remarks>
    /// The default pack's rooms were built before rooms had openings: each has its door as a gap in
    /// its walls and nothing in its file saying so. <c>Mark Room Openings</c> gives every pack room
    /// its one entrance and one exit: a room with no entrance gets one on the gap on its south side,
    /// or else the first gap found, and a room with no exit gets one on its entrance's spot - one
    /// door, walked in and out. A room that has both is left as it is, and one whose openings break
    /// a rule is reported rather than changed.
    /// <para>
    /// The floor and walls are measured from their prefabs' colliders, as they stand in the game,
    /// by <see cref="RoomOpenings"/>, which the room editor uses too.
    /// </para>
    /// </remarks>
    public static class RoomOpeningsMarker
    {
        const string RoomsFolder = "Assets/Process/default/Rooms";
        const string PackFilePrefix = "default_room_";

        [MenuItem("Tools/Dojo/Rooms/Mark Room Openings")]
        public static void Mark() => Run(true);

        [MenuItem("Tools/Dojo/Rooms/Check Room Openings")]
        public static void Check() => Run(false);

        static void Run(bool mark)
        {
            var sizes = new Dictionary<string, Vector2>();
            var marked = 0;
            var good = 0;
            var bad = 0;

            foreach (var path in Directory.GetFiles(RoomsFolder, "*.json"))
            {
                var file = Path.GetFileNameWithoutExtension(path);

                if (!file.StartsWith(PackFilePrefix) && !file.StartsWith("room_"))
                {
                    continue;
                }

                var library = JsonUtility.FromJson<WorldLibrary>(File.ReadAllText(path));
                var room = library != null && library.worlds != null && library.worlds.Count > 0
                    ? library.Find(library.CurrentName()) ?? library.worlds[0]
                    : null;
                var ground = room != null && room.floors != null ? room.floors.Find(f => f != null && f.index == 0) : null;

                if (ground == null)
                {
                    Debug.LogWarning("[Openings] " + file + ": no ground floor, so nothing to check.");
                    bad++;
                    continue;
                }

                if (ground.openings == null)
                {
                    ground.openings = new List<WorldSave.SavedOpening>();
                }

                var tiles = new List<Rect>();
                var walls = new List<RoomOpenings.Wall>();
                RoomOpenings.Measure(ground, resource => SizeOf(resource, sizes), tiles, walls);

                string problem;

                var hasEntrance = ground.openings.Exists(o => o.kind == RoomOpenings.Entrance);
                var hasExit = ground.openings.Exists(o => o.kind == RoomOpenings.Exit);

                if ((hasEntrance && hasExit) || !mark || !file.StartsWith(PackFilePrefix))
                {
                    if (RoomOpenings.Validate(tiles, walls, ground.openings, out problem))
                    {
                        Debug.Log("[Openings] " + file + ": " + Summary(ground.openings) + " - keeps the rules.");
                        good++;
                    }
                    else
                    {
                        Debug.LogWarning("[Openings] " + file + ": " + problem);
                        bad++;
                    }

                    continue;
                }

                if (!hasEntrance)
                {
                    var gaps = RoomOpenings.Gaps(tiles, walls, ground.openings);

                    if (gaps.Count == 0)
                    {
                        Debug.LogWarning("[Openings] " + file + ": there is no gap in its walls to mark. Open it in the room "
                            + "editor and use ENTRANCE on a wall of the floor's edge.");
                        bad++;
                        continue;
                    }

                    // The south gap - the default rooms' doors are all there - else the first.
                    var south = gaps.Find(gap => gap.facing.z < -0.5f) ?? gaps[0];
                    south.kind = RoomOpenings.Entrance;
                    ground.openings.Add(south);
                }

                if (!hasExit)
                {
                    // On the entrance's spot: one door, walked in and out.
                    var exit = ground.openings.Find(o => o.kind == RoomOpenings.Entrance).Copy();
                    exit.kind = RoomOpenings.Exit;
                    ground.openings.Add(exit);
                }

                if (!RoomOpenings.Validate(tiles, walls, ground.openings, out problem))
                {
                    Debug.LogWarning("[Openings] " + file + ": the gaps found break a rule, so nothing was written. " + problem);
                    bad++;
                    continue;
                }

                File.WriteAllText(path, JsonUtility.ToJson(library, true));
                AssetDatabase.ImportAsset(path.Replace('\\', '/'), ImportAssetOptions.ForceUpdate);

                Debug.Log("[Openings] " + file + ": marked " + Summary(ground.openings) + ".");
                marked++;
            }

            Debug.Log("[Openings] " + (mark ? "Marked " + marked + " room(s); " : "") + good + " already keep the rules, "
                + bad + " need attention." + (marked > 0 ? " Run the Addressable Generator on the default pack to rebuild and upload it." : ""));
        }

        static string Summary(List<WorldSave.SavedOpening> openings)
        {
            var parts = new List<string>();

            foreach (var opening in openings)
            {
                parts.Add(opening.kind + " " + RoomOpenings.SideOf(opening.facing.ToVector2()) + " at ("
                    + opening.centre.x.ToString("0.###") + ", " + opening.centre.z.ToString("0.###") + "), "
                    + opening.width.ToString("0.##") + " m");
            }

            return parts.Count == 0 ? "no openings" : string.Join("; ", parts);
        }

        /// <summary>
        /// How much ground a piece's prefab covers unturned, from its solid colliders, by its content
        /// address: <c>&lt;pack&gt;/&lt;name&gt;</c> is <c>Assets/Process/&lt;pack&gt;/Prefabs/&lt;name&gt;.prefab</c>.
        /// </summary>
        static Vector2 SizeOf(string resource, Dictionary<string, Vector2> sizes)
        {
            if (string.IsNullOrEmpty(resource))
            {
                return Vector2.zero;
            }

            Vector2 size;

            if (sizes.TryGetValue(resource, out size))
            {
                return size;
            }

            size = Vector2.zero;
            var slash = resource.IndexOf('/');
            var prefab = slash > 0
                ? AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Process/" + resource.Substring(0, slash) + "/Prefabs/"
                    + resource.Substring(slash + 1) + ".prefab")
                : null;

            if (prefab != null)
            {
                var instance = Object.Instantiate(prefab);
                instance.hideFlags = HideFlags.HideAndDontSave;

                try
                {
                    instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    Physics.SyncTransforms();

                    var found = false;
                    var bounds = new Bounds();

                    foreach (var collider in instance.GetComponentsInChildren<Collider>())
                    {
                        if (!collider.enabled || collider.isTrigger)
                        {
                            continue;
                        }

                        if (found)
                        {
                            bounds.Encapsulate(collider.bounds);
                        }
                        else
                        {
                            bounds = collider.bounds;
                            found = true;
                        }
                    }

                    if (found)
                    {
                        size = new Vector2(bounds.size.x, bounds.size.z);
                    }
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }
            }

            if (size == Vector2.zero)
            {
                Debug.LogWarning("[Openings] No solid collider found for '" + resource + "', so it is left out of the measure.");
            }

            sizes[resource] = size;
            return size;
        }
    }
}
