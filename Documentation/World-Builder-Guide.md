# World builder guide

How to build the default world and add new pieces to the MVP kit in DojoAddressableCreator. Everything is in one window: **Tools ▸ Dojo ▸ Content ▸ Builder (World, Assets, Rooms, Floors)**.

## The rules
The game enforces these while you build, and **Check rules** looks for them before a world becomes the default.
- **Grid:** pieces snap to a 0.3 m grid. Floor tiles are 1.2 m (4 cells).
- **Floor:** every piece stands on a floor tile. Nothing hangs off the edge.
- **Walls:** walls stand on the floor only, never on a desk or another wall. A piece counts as a wall when its name says *wall*, *partition* or *railing*.
- **Overlaps:** two pieces can't share the same cells. Monitors, plants and the like can stand *on* a desk or table.
- **Rooms:** a placed room is one unit. It moves, turns and is deleted whole, with its floor, walls and furniture.
- **Thumbnails:** every inventory thumbnail is a sprite. The Builder makes them, so you don't have to.

## Build a world
1. Open the Builder, **World** tab.
2. Type a name, then press:
   - **From scratch**: one floor tile to build out from.
   - **Copy of default**: the current default world, to change it.
3. The world opens in **RuntimeMVP** and plays. Build it from the inventory:
   - **Floor:** lay the floor tiles first.
   - **Walls:** add walls along the room edges.
   - **Furniture:** desks, chairs and the rest. Monitors go on desks.
   - **Rooms:** place them from the inventory's **Rooms** tab.
   - **Areas and departments:** paint them from the inventory's **Areas** tab.
   - **Floors and elevator:** add more floors with **+ ADD FLOOR**, and an elevator to link them.
   - **People:** put the owner down from the **You** tab, and up to four agents from the **Agents** tab (Sidekick Supreme and Sample Agents 2 to 4). These are only *places*: in the game, the player's own owner and agents stand in them, in order. Without an owner's place nobody is seated as the owner; with no agent places, no agents are placed.
4. The world saves itself as you go. You can also use **SAVE WORLD** in the in-game menu.
5. Stop Play. Back in the Builder, pick the world under **Saved worlds**, then:
   - **Check rules** lists anything off the grid, off the floor or overlapping, any wall on top of something, and any piece whose prefab is missing.
   - **Make Default World** makes it the world new players start in. It shows what the world holds and what the check found before it replaces the default.
6. Publish it for the game: see `Content-Release-Guide.md`.

To keep working on a world later, pick it and press **Play it**.

## Add a new piece
1. Put the model in `Assets/Dojo/MVP` (`.fbx`), and its textures in `Assets/Dojo/MVP/textures`. Name the textures as the model's materials point at them.
2. Open the Builder, **Assets** tab. The new model is listed as **new**.
3. Set:
   - **Kind:**
     - **Furniture:** anything else.
     - **Floor:** tiles and platforms that other pieces stand on.
     - **Wall:** room walls, glass partitions, railings.
     - **Chair:** agents sit on it; the seat faces away from the backrest.
     - **Elevator:** links floors.
     - **Skip:** not built, e.g. a file with several pieces in it.
     - **Auto:** worked out from the name.
   - **Tab:** the inventory tab it goes under. Leave it on (guess) to let the name decide.
   - **Prefab name:** leave it empty for `default_` plus the model name, or type your own.
4. Tick it and press **Build & Add**. It makes:
   - the prefab, standing on its base, centred, with its materials
   - its sprite thumbnail, its address and its inventory entry
   - its place in RuntimeMVP's inventory
5. Play and place it to check how it looks.
6. For the game: Bob or Jacob adds its address to `Game.unity`'s inventory list in dojo-game-app. Then the content is published.

To change a piece, export the model again over the old `.fbx`. It shows as **changed**: tick it and **Build & Add** again.

A model whose materials aren't in the kit gets a material made from its own textures. Check its colours in Play.

## Rooms
1. Make a room in Play: inventory ▸ **Rooms** ▸ room editor, then save it.
2. Builder ▸ **Rooms** tab: the room is listed under *Made, not yet in the pack*.
3. Press **Promote** to make it part of the pack. It ships with the content and appears in RuntimeMVP's inventory.

**Generate room thumbnails** and **Check room openings** are on the same tab.

## Floor layouts
A floor layout is a whole floor shipped in the pack, built on the Rooms system: a one-floor world file in `Assets/Process/default/Floors`, the same shape as a room file. The pack's layouts are `default_floor_<name>.json`, listed in `floors.json` in the order the tab shows them, with a thumbnail at `Thumbnails/floor_<name>.png`; they build into their own `Content_Floors` group when the Addressable Generator runs on the pack.

1. Builder ▸ **Floors** tab lists the pack's layouts and warns when `floors.json` does not match the files.
2. **Generate floor thumbnails** renders every layout's picture from its file (no prefab is needed).
3. A layout made in the floor editor is `floor_<name>.json`; **Promote** puts it in the pack and on the list.

### Making a layout in Play
4. Inventory ▸ **FLOORS** ▸ **+ CREATE LAYOUT**: the world is kept and a blank floor takes its place, with an amber frame of 8 × 6 tiles drawn on it. **SIZE** opens the strip that steps the frame's columns and rows (4 to 24).
5. Lay the floor out from the other tabs, the Rooms tab included: a room placed here is saved as a room, so it comes back as one.
6. **SAVE LAYOUT** asks for a name. Pieces outside the frame are counted and, if you agree, dropped. **CANCEL** throws it away and brings the world back. **CLEAR** empties the floor.
7. The card shows the layout with its thumbnail and size in tiles. **EDIT** opens it again; **DELETE** and **PROMOTE** are the same as a room's.

Placing a layout on a floor comes next (ClickUp 14ze67xgf0v, 14ze67xgf0x); the plan is in `Documentation/Tasks/Floor-Creator-Notes.md`. The floor editor is built into RuntimeMVP by **Tools ▸ Dojo ▸ Build Floor Editor**, from the room editor's own objects.
