# A placed room as one unit: work notes (creator)

As of 5 Nov 2026. Jacob.

The game's room-as-one-unit work (dojo-game-app, `jacob/room_as_one_unit`), brought into the creator so the two behave alike and a world passes between them unchanged. The creator already grouped a placed room under a `RoomContainer`, lifted it whole on a press, and deleted it whole. This adds what it lacked.

## Ticket and branches

| Item | Value |
| --- | --- |
| Ticket | [[Hiro Dojo] A placed room as one unit: pick up, move, turn or delete it as a whole](https://app.clickup.com/t/14ze67xdx4n) |
| Branch | `jacob/room_as_one_unit`, from `main_development` |
| Game | dojo-game-app, `jacob/room_as_one_unit`; notes in its `Documentation/Tasks/Room-As-One-Unit-Notes.md` |

## Decisions

As in the game (Jacob, 4 Nov):
- **A room's own pieces are never handled one by one.** A press lifts the whole room; its pieces can't be Ctrl+click selected or deleted alone. Its floors and walls are edited in the room editor.
- **What the player puts in a room stays theirs** - movable and deletable on its own - and goes with the room when it is moved or deleted.
- **Shared walls:** where two rooms meet, the room put down turns off its own overlapping walls instead of both standing through each other. They come back on when what stands in their place goes.
- **Entrance cuts:** a room's entrance turns off the wall it opens through (a neighbour's, or a loose one), so the rooms connect. Turned off, not destroyed: it comes back when the room goes.
- **The save format is the game's.** `SavedRoom.order`, `areaIds`, `entrances` (in the room file's `SavedOpening` shape, in world x and z) and `SavedPiece.hidden`, so the starter `world.json` made here loads in the game with its rooms intact.

Kept as the creator had it: the grab point when lifting a room, lining a room up with the floor tiles beside it, refusing to lift a room whose piece is in use, and the room editor (which still cuts its own openings by removing the room's walls - that is editing the room itself).

## What changed

| Commit | Change |
| --- | --- |
| `e40b1c8` | `RoomContainer` remembers its order, areas and entrances; the save gains the game's fields. Old worlds load as before |
| `2798ce0` | Three fixes: Ctrl+click no longer selects room pieces; a delete takes only what is on a piece (never floors, walls or another room's pieces along its edge); nothing saves while a room is in the air, and leaving Edit with one in hand puts it back |
| `adb6e29` | Loose pieces on a room's floor tiles go with it, not only those on its furniture |
| `7a804a9` | A room moves its own areas, by id. A room saved before this falls back once to the areas inside its pieces, and remembers them |
| `0c9458e` | Shared walls: `RoomWalls`, `HiddenWall`. Turned-off walls give up their cells, are saved as hidden and turned off again on load; lifting or deleting a room, or deleting a loose wall, brings back any wall with nothing left in its place |
| `a6d0d26` | Entrance cuts on placing and on putting a moved room down; the entrances move and turn with the room |
| `f3525d6` | The creator's first EditMode test assembly (`Dojo.Game.Tests.EditMode`), with `RoomAsOneUnitTests` |

**Not ported:** the game's database tables and Spacetime publishing. The creator has no Spacetime layer (`NullWorldPublisher`), so the save file carries everything.

## Tests

- **`RoomAsOneUnitTests`, 12 EditMode tests:** the container and its order, a piece's room, areas and entrances, the shared-wall and entrance rules (another floor, a wall across a doorway), the save with order, areas, entrances and turned-off walls, a world saved before this, and the order rooms are kept in. Pass (Jacob, 5 Nov).
- **Checked in Play (5 Nov):** shared walls, entrance cuts, walls coming back when a room moves or is deleted, things on the floor going with a room, leaving Edit mid-move, save and reload. Pass.

## Known issues and follow-ups

- Moving a single loose wall does not bring back a room wall turned off behind it; only deleting it, or moving the whole room, does. As in the game.
