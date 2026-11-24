using System.Collections.Generic;
using Dojo.Game.Placement;
using UnityEditor;
using UnityEngine;

namespace Dojo.Game.Editor
{
    /// <summary>
    /// Checks a saved world against the building rules before it becomes the default world: every
    /// piece on the grid and over the floor, no two pieces on the same cells, walls only on the floor,
    /// every piece's prefab there, and every room piece belonging to a room.
    /// </summary>
    /// <remarks>
    /// The game enforces these as pieces are placed, so a world built by hand in Play passes. The check
    /// is for what got around that: a hand-edited file, a piece whose prefab was renamed or deleted
    /// since, a room template that changed size. Cells are worked out as the game's own
    /// <c>FurnitureSpawner</c> does: a piece's size rounded up to whole cells, a wall's length rounded
    /// down to the width it is laid at.
    /// </remarks>
    public static class WorldRules
    {
        /// <summary>Above this, a piece is standing on another piece rather than on the floor.</summary>
        const float OnTheFloor = 0.2f;

        sealed class Shape
        {
            public Vector3 Size;
            public bool Floor;
            public bool Wall;
        }

        /// <summary>Every problem found, as a line a person can act on; empty when there are none.</summary>
        public static List<string> Check(WorldSave world)
        {
            var problems = new List<string>();
            if (world == null)
            {
                problems.Add("There is no world.");
                return problems;
            }

            var cell = MvpKitImporter.MvpCell;
            var shapes = new Dictionary<string, Shape>();
            var rooms = new HashSet<string>();
            foreach (var room in world.rooms)
            {
                if (room != null && !string.IsNullOrEmpty(room.id))
                {
                    rooms.Add(room.id);
                }
            }

            var roomsUsed = new HashSet<string>();

            foreach (var floor in world.floors)
            {
                if (floor == null)
                {
                    continue;
                }

                var label = "floor " + (floor.index + 1) + ": ";
                var ground = new HashSet<Vector2Int>();
                var taken = new Dictionary<Vector2Int, string>();

                // Ground first, so every other piece can be checked against it.
                foreach (var piece in floor.pieces)
                {
                    var shape = piece != null ? ShapeOf(piece.resource, shapes) : null;
                    if (shape == null || !shape.Floor || piece.hidden)
                    {
                        continue;
                    }

                    foreach (var c in Cells(piece, shape, cell))
                    {
                        ground.Add(c);
                    }
                }

                foreach (var piece in floor.pieces)
                {
                    if (piece == null || piece.hidden)
                    {
                        continue;
                    }

                    var what = Short(piece.resource) + " at (" + piece.position.x.ToString("0.##") + ", " + piece.position.z.ToString("0.##") + ")";

                    if (!string.IsNullOrEmpty(piece.room))
                    {
                        roomsUsed.Add(piece.room);
                        if (!rooms.Contains(piece.room))
                        {
                            problems.Add(label + what + " belongs to a room that isn't in the world (" + piece.room + ").");
                        }
                    }

                    var shape = ShapeOf(piece.resource, shapes);
                    if (shape == null)
                    {
                        problems.Add(label + what + ": there is no prefab for " + piece.resource + ".");
                        continue;
                    }

                    if (shape.Floor)
                    {
                        continue;
                    }

                    var standing = piece.position.y > OnTheFloor;
                    if (standing)
                    {
                        if (shape.Wall)
                        {
                            problems.Add(label + what + " is a wall standing on another piece. Walls stand on the floor only.");
                        }

                        continue;
                    }

                    if (!OnGrid(piece, shape, cell))
                    {
                        problems.Add(label + what + " is off the " + cell + " m grid.");
                    }

                    foreach (var c in Cells(piece, shape, cell))
                    {
                        if (!ground.Contains(c))
                        {
                            problems.Add(label + what + " hangs off the floor.");
                            break;
                        }
                    }

                    foreach (var c in Cells(piece, shape, cell))
                    {
                        string other;
                        if (taken.TryGetValue(c, out other))
                        {
                            problems.Add(label + what + " overlaps " + other + ".");
                            break;
                        }
                    }

                    foreach (var c in Cells(piece, shape, cell))
                    {
                        taken[c] = what;
                    }
                }
            }

            foreach (var room in world.rooms)
            {
                if (room != null && !roomsUsed.Contains(room.id))
                {
                    problems.Add("Room '" + room.name + "' has no pieces.");
                }
            }

            foreach (var connector in world.connectors)
            {
                if (connector != null && ShapeOf(connector.address, shapes) == null)
                {
                    problems.Add("There is no prefab for the " + connector.kind + " " + connector.address + ".");
                }
            }

            return problems;
        }

        static string Short(string address)
        {
            var slash = address != null ? address.LastIndexOf('/') : -1;
            return slash >= 0 ? address.Substring(slash + 1) : address;
        }

        /// <summary>The prefab's size and kind, from <c>Assets/Process/&lt;pack&gt;/Prefabs</c>; null when it is missing.</summary>
        static Shape ShapeOf(string address, Dictionary<string, Shape> cache)
        {
            if (string.IsNullOrEmpty(address))
            {
                return null;
            }

            Shape shape;
            if (cache.TryGetValue(address, out shape))
            {
                return shape;
            }

            var slash = address.IndexOf('/');
            var path = slash > 0
                ? "Assets/Process/" + address.Substring(0, slash) + "/Prefabs/" + address.Substring(slash + 1) + ".prefab"
                : null;
            var prefab = path != null ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : null;

            if (prefab != null)
            {
                var bounds = new Bounds();
                var measured = false;
                foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null)
                    {
                        continue;
                    }

                    var local = filter.sharedMesh.bounds;
                    var matrix = prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                    for (var i = 0; i < 8; i++)
                    {
                        var corner = matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents,
                            new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                        if (!measured)
                        {
                            bounds = new Bounds(corner, Vector3.zero);
                            measured = true;
                        }
                        else
                        {
                            bounds.Encapsulate(corner);
                        }
                    }
                }

                shape = new Shape
                {
                    Size = measured ? bounds.size : Vector3.one * MvpKitImporter.MvpCell,
                    Floor = prefab.GetComponentInChildren<FloorPiece>(true) != null,
                    Wall = RoomOpenings.IsWall(address),
                };
            }

            cache[address] = shape;
            return shape;
        }

        static Vector2Int Footprint(WorldSave.SavedPiece piece, Shape shape, float cell)
        {
            var across = Mathf.Max(1, Mathf.CeilToInt(shape.Size.x / cell - 0.01f));
            var along = Mathf.Max(1, Mathf.CeilToInt(shape.Size.z / cell - 0.01f));

            if (shape.Wall)
            {
                if (shape.Size.x >= shape.Size.z)
                {
                    across = Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(cell, Mathf.Floor((shape.Size.x + 0.01f) / cell) * cell) / cell));
                }
                else
                {
                    along = Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(cell, Mathf.Floor((shape.Size.z + 0.01f) / cell) * cell) / cell));
                }
            }

            return piece.rotationSteps % 2 == 1 ? new Vector2Int(along, across) : new Vector2Int(across, along);
        }

        static IEnumerable<Vector2Int> Cells(WorldSave.SavedPiece piece, Shape shape, float cell)
        {
            var size = shape.Floor
                ? new Vector2Int(Mathf.Max(1, Mathf.RoundToInt(shape.Size.x / cell)), Mathf.Max(1, Mathf.RoundToInt(shape.Size.z / cell)))
                : Footprint(piece, shape, cell);
            var minX = Mathf.RoundToInt((piece.position.x - size.x * cell * 0.5f) / cell);
            var minZ = Mathf.RoundToInt((piece.position.z - size.y * cell * 0.5f) / cell);

            for (var x = 0; x < size.x; x++)
            {
                for (var z = 0; z < size.y; z++)
                {
                    yield return new Vector2Int(minX + x, minZ + z);
                }
            }
        }

        static bool OnGrid(WorldSave.SavedPiece piece, Shape shape, float cell)
        {
            var size = Footprint(piece, shape, cell);
            var x = (piece.position.x - size.x * cell * 0.5f) / cell;
            var z = (piece.position.z - size.y * cell * 0.5f) / cell;
            return Mathf.Abs(x - Mathf.Round(x)) < 0.02f && Mathf.Abs(z - Mathf.Round(z)) < 0.02f;
        }
    }
}
