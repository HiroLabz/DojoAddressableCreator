# Proposal: placed rooms keep their department and conference flag (dojo-game-app)

Status: proposed, not applied. Written 27 Oct 2026.

## Problem

The room files the Addressable Creator ships now carry, on each room's area:

- `department`: the 14 department rooms carry their own name (HR, Sales, …), from the preset list in `Departments.Presets`.
- `conferenceRoom`: `true` on the Conference Room.

When the game places a room, `RoomPlacer.AddRoomAreas()` creates each area from its name and cells only. It drops both fields, then always opens the Department popup, even for a room whose file already says HR.

## Change

One file in dojo-game-app: `Assets/Dojo/Code/Game/Scripts/Placement/RoomPlacer.cs`.

### 1. `AddRoomAreas()`: keep the file's values on each area it adds

```diff
                 var name = UniqueAreaName(string.IsNullOrWhiteSpace(saved.name) ? room.name : saved.name);
-                added.Add(blockAreas.Add(name, storeys.Viewed, cells, saved.areaCellSize > 0f ? saved.areaCellSize : Cell).Id);
+                var area = blockAreas.Add(name, storeys.Viewed, cells, saved.areaCellSize > 0f ? saved.areaCellSize : Cell);
+
+                // As the room file has it: the department the room belongs to, and whether it is a
+                // conference room. The Department popup below only asks about rooms that do not say.
+                if (!string.IsNullOrWhiteSpace(saved.department))
+                {
+                    blockAreas.SetDepartment(area.Id, saved.department);
+                }
+
+                if (saved.conferenceRoom)
+                {
+                    blockAreas.SetConferenceRoom(area.Id, true);
+                }
+
+                added.Add(area.Id);
                 Debug.Log("[Rooms] added area " + name + " (" + cells.Count + " cells) with " + room.name + ".");
```

### 2. After building the room: ask only when the file didn't answer

```diff
             // Which department the room belongs to: asked once, for every area it brought.
-            if (added.Count > 0 && resolver != null && resolver.TryResolve(out Dojo.Game.UI.AreaDepartmentPrompt departments))
+            // A room whose file already says - a department's room, or a conference room - is not asked.
+            if (added.Count > 0 && !AllAnswered(added)
+                && resolver != null && resolver.TryResolve(out Dojo.Game.UI.AreaDepartmentPrompt departments))
             {
                 departments.Ask("'" + room.name + "'", added);
             }
```

```diff
+        /// <summary>True when every area the room brought already has a department or is a conference room.</summary>
+        bool AllAnswered(List<int> areaIds)
+        {
+            foreach (var id in areaIds)
+            {
+                var area = blockAreas.Find(id);
+
+                if (area == null || (string.IsNullOrEmpty(area.Department) && !area.ConferenceRoom))
+                {
+                    return false;
+                }
+            }
+
+            return true;
+        }
```

## Result with the default pack

| Room | In the file | Department popup |
|---|---|---|
| 14 department rooms | Own department | Not shown; the room is that department straight away |
| Conference Room | Conference room flag | Not shown |
| Manager's Room | None | Shown, as now |
| Older or player-made rooms without the fields | None | Shown, as now |

## Notes

- **Only existing pieces are used.** `BlockAreas.SetDepartment` (which tidies the name to the preset spelling and caps its length), `BlockAreas.SetConferenceRoom`, and the existing `SavedBlockArea.department` and `conferenceRoom` fields. The room file format doesn't change.
- **Alternative: always ask.** `AreaDepartmentPrompt.Ask` already starts from the first area's current department and flag. To keep asking every time, with the file's answer pre-selected, apply part 1 only.
- **Where to apply it:** on its own branch from the game's `main_development`.
- **Test to add:** an EditMode test that places a room whose area has a department and the conference flag, and checks both come through on the placed area.
