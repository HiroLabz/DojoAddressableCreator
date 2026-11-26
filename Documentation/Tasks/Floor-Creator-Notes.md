# Floor creator in the Addressable Creator

Ticket: [Hiro Dojo] Addressable Creator: floor creator for the design team (floor layouts, built on the Rooms system) (ClickUp 14ze67xgf0v, Bob, 25 Nov 2026). The game twin is 14ze67xgf0x and waits on this; Rold's ten layouts (14ze67xgf0y) wait on this too. Design for approval: https://claude.ai/artifact/GhQLLbQN28cD7HHxFym2yp (shared with Bob 25 Nov).

This is the build plan, written before Bob's approval of the design so the build can start the day he says yes. Where the design changes, only the "Inventory" and "Editor bar" rows move.

**Status, 26 Nov:** the first PR (data and pipeline) is built on `jacob/floor_creator_data`: `CustomFloorLayouts`, `FloorLayoutCatalog`, `FloorsPack`, `FloorThumbnails`, the generator's folder table, the Builder's Floors tab, `floors.json` with one example layout. The store is a trimmed copy of `CustomRooms`, not a shared base class: the refactor would have touched most of the rooms' store for two callers, so the rooms' behaviour stays untouched and the duplication is said here. The editor (PR 2) and the LAYOUTS tab (PR 3) wait on the design approval.

## What Bob decided (25 Nov)

- A floor creator the design team can use, in the creator, shipping whole floor layouts in the content pack.
- The same underlying system as Rooms: the same editor approach, the same file shape (`WorldLibrary` JSON), the same pack pipeline, in their own folder `Assets/Process/<pack>/Floors`.
- A new inventory item for floor layouts, in the creator and in the game.
- The UI is designed first, with Claude Design, and Bob approves it before building.
- Placing a layout never removes or moves what is already on the floor: taken spots are left as they are.
- The game never lays a floor by itself. The player places a layout, as they place a room.

## What a floor layout is

- A one-floor `WorldSave` inside a `WorldLibrary`, exactly as a room file is (`WorldSave.cs:17`, `:230`), with only `floors[0].pieces`: floor tiles, walls, and any kit pieces the designer laid. No `openings`, no `blockAreas`, no `rooms` of its own in v1.
- Its size is the bounding rect of its pieces in cells (`PlacementGrid.Step`), recentred on the origin as `RoomEditor.ToRoom` does (`RoomEditor.cs:533`). The card shows that size (`12 × 9`) and the count of rooms inside it, measured at runtime as `RoomPlacer.Prepare` measures a room's footprint.
- Files: `Assets/Process/<pack>/Floors/floor_<slug>.json` (custom) and `default_floor_<slug>.json` (pack), thumbnail `Floors/Thumbnails/floor_<slug>.png`, index `Floors/floors.json` with `{ "floors": [FloorLayoutEntry] }`. `FloorLayoutEntry` has the same four fields as `RoomEntry` (`RoomCatalog.cs:10`): `name`, `address`, `icon`, `tag`.
- Addresses: `default/Floors/default_floor_<slug>`, icon `default/Floors/Thumbnails/floor_<slug>`, custom `custom/floor_<slug>`.

## How it is built

Each row names the Rooms piece it follows, so a reviewer can diff against something that exists.

### Data and files
| Piece | Follows | Plan |
|---|---|---|
| `CustomFloorLayouts` | `CustomRooms` (`CustomRooms.cs`) | Pull the generic parts of `CustomRooms` into a `CustomContentStore(folder, filePrefix, packPrefix, addressPrefix)`: `Slug` (:1104), `Save` (:339), `Read` (:119), `Icon` (:125), `IsNameFree` (:188), `Delete` (:226), `All` (:80), `Import`/`ImportSprite` (:376/:401), `PathFor` (:1097), `Promote` (:563). `CustomRooms` becomes the Rooms instance of it; `CustomFloorLayouts` the Floors one. Prefab-making on promote (`MakePrefab`) stays Rooms-only. |
| `FloorLayoutCatalog` | `RoomCatalog` (`RoomCatalog.cs`) | Same shape with the index name and folder as parameters: `All()` (:79) reads `<pack>/Floors/floors` through `IContentService`, prefers `custom.Listed...()` for the default pack, appends the custom layouts; `IconOf` (:159) and `Read` (:246) with the same editor fallback. A generic `ContentCatalog<TEntry>` base if it stays small; otherwise a copy. |

### The editor
| Piece | Follows | Plan |
|---|---|---|
| `FloorLayoutEditor` | `RoomEditor` (`RoomEditor.cs`) | The same session: `Begin()` keeps the world (`world.Capture()`, name, viewed floor, camera views), sets `SavingSuspended`, shows `Blank()` (:898); `End()` (:391) puts it all back. Pieces arrive through the normal inventory and `PlacementController`, on floor 0. Dropped: openings (`CutOpening` … `CheckOpenings`, :645–876), `AddFloorArea` (:431), the department prompt, `RefuseLift` (:63). Kept: `IsEmpty`, `ToRoom` as `ToLayout` (recentre, snap), `Save` (:278) minus the area and openings steps, `RoomThumbnailCamera.Render` (:348). Best as a common base class `WorldSpaceEditor` with `RoomEditor` and `FloorLayoutEditor` on top; if that refactor grows, copy and trim instead and say so in the PR. |
| `FloorLayoutEditorBar` | `RoomEditorBar` (`RoomEditorBar.cs`) | Copy and trim: no `RoomOpeningTool`. Buttons per the design: SIZE · CLEAR · SAVE LAYOUT · CANCEL. `OnSave` (:161) asks for the name only (no department); `OnCancel` (:303) asks "leave without saving?" when something changed. SIZE opens a columns × rows stepper (4–24): growing adds empty cells, shrinking asks when pieces would fall outside and drops them on OK. CLEAR asks, then empties floor 0. |
| Thumbnail | `RoomThumbnailCamera` (`RoomThumbnailCamera.cs:24`) | Reused as is: 256×256, yaw 135, pitch 40, orthographic size `maxExtent * 1.1`, placed-piece layer only. A floor is wider than a room, so check the 256 px picture still reads; if not, raise the size for layouts only. The editor-time menu `Tools/Dojo/Content/Floors/Generate Floor Thumbnails` mirrors `RoomThumbnails.cs` (:35). |

### The pack pipeline
| Piece | Follows | Plan |
|---|---|---|
| `FloorsPack` | `RoomsPack` (`RoomsPack.cs:29-36`) | Constants (`Folder`, `ThumbnailFolder`, `IndexPath`, `GroupName = "Content_Floors"`), `EnsureGroup` (:57, copies `Content_Shared`'s schemas), `CheckIndex` (:142), `Promote`/`Register` (:259). No `PrefabFolder`: layouts need no prefab. |
| Generator | `AddressableGenerator.RegisterExtraFolders` (`:1085-1208`, Rooms branch at :1121) | Make the Rooms branch a table of folder name → pack class (`Rooms` → `RoomsPack`, `Floors` → `FloorsPack`) so both get `EnsureGroup`, `ReleaseEditorOnlyEntries` (:786), `CheckIndex`, sprite import for `Thumbnails/*.png`, and `Assign(..., forceGroup)` (:1163). Addresses come from `AddressForExtra` (:467) unchanged. The group asset `Assets/AddressableAssetsData/AssetGroups/Content_Floors.asset` and its two schemas are created by `EnsureGroup` on first run and committed. |
| Builder | `AddressableBuilder.Build` (`:589`), `VersionedPackedMode` | No change: it is group-agnostic. The next release carries `dojo_v<N>_content_floors_*` bundles by itself. |
| Builder window | `DojoBuilderWindow.cs:437-497` (Rooms tab) | A Floors tab with the same three actions: list `floor_*` / `default_floor_*`, Promote, Generate thumbnails. No "check openings". |

### The inventory
| Piece | Follows | Plan |
|---|---|---|
| Category | `InventoryScreen.roomsCategory` (`InventoryScreen.cs:59`, `:326`, `:364`) | A `layoutsCategory = "layouts"` and a tab between ROOMS and FLOOR. The tab icon is new: the kit has no ROOMS or LAYOUTS flat sprite; both need one in `Assets/Dojo/Art/2D/UI/icon` (flat, white, 96 px like the others). |
| Panel and cards | `RoomsDisplayUI` (`RoomsDisplayUI.cs:372`) + `RoomCard` (`RoomCard.cs:80`, prefab `UI/Inventory/RoomCard.prefab`) | `FloorLayoutsDisplayUI`, same `Rebuild`: `catalog.All()`, one card per layout, `Bind(entry, catalog.IconOf(entry), footprint)`; the card's second line reads `12 × 9 · 2 rooms`. Reuse the `RoomCard` prefab with the item tile from the kit if the design is approved as drawn. Buttons: + CREATE LAYOUT, and on each card EDIT and DELETE (DELETE only for custom layouts, as rooms do). In the creator there is no YOURS section: every layout is the pack's. |

### Placement
| Piece | Follows | Plan |
|---|---|---|
| `FloorLayoutPlacer` | `RoomPlacer` (`RoomPlacer.cs`: `Plan` :29, `Prepare` :173, `FootprintAt` :196, `TakenOn` :225, `AlignToTiles` :354, `BuildGhost` :989, `Build` :1030) and `PlacementController.Rooms.cs` (`BeginRoomFromInventory` :63, `TickRoom` :171) | Copy `Prepare`, `FootprintAt`, `AlignToTiles`, `BuildGhost`, the carry (`HeldElsewhere`, turn with wheel, Esc puts back, as rooms). The one real difference: **no refusal**. `IsFree` (:918) and the "doesn't fit here" path (:238-243) go; instead `Build` walks the layout's pieces and skips each one whose cells are taken on the target floor (`TakenOn` per piece, or `HolderAt`). The ghost greys the clashing cells rather than turning red. No `RoomContainer`, no `RoomWalls`, no areas, no entrances: the pasted pieces are loose pieces of the floor, saved as any other piece by `WorldService.Capture` (:393). Rooms inside a layout are still rooms (their `SavedRoom` and pieces tagged `room`), rebuilt by the normal load. |
| The question | the game's `RoomPlacer.Ask` (game `RoomPlacer.cs:1273`) | The creator today refuses silently and has no dialog. v1 keeps the creator's click-to-place; the OK / PLACE IT FOR ME question in the design is for the game port, where the popups exist. Noted on the design. |

### Tests
| Test | Models |
|---|---|
| `FloorLayoutCatalogTests` | the game's `RoomCatalogTests` (11): index read, custom appended, address and icon resolution, name free |
| `FloorLayoutEditorTests` | the game's `RoomEditorTests` (25) and `RoomEditorGuardsTests` (5): begin keeps and restores the world, save recentres and snaps, empty refused, size stepper trims, cancel writes nothing |
| `FloorLayoutPlacerTests` | the creator's `RoomAsOneUnitTests` (13): footprint, align to tiles, and the new rule: a taken cell is skipped and the piece there is untouched, the rest is laid |
| `FloorsPackTests` | `RoomsPack.CheckIndex` behaviour: a bad index entry is named, Editor/ entries are released |

## Order

1. **First PR, data and pipeline:** `CustomContentStore` refactor with `CustomRooms` unchanged in behaviour (tests green before and after), `CustomFloorLayouts`, `FloorLayoutCatalog`, `FloorsPack`, the generator table, `Content_Floors` group, the Builder window tab, an empty `floors.json`. Proof: a hand-written layout JSON in `Process/default/Floors` lands in a content build as `dojo_v<N>_content_floors_*`.
2. **Second PR, the editor:** `FloorLayoutEditor`, `FloorLayoutEditorBar` with SIZE and CLEAR, the save with thumbnail, EDIT and DELETE on cards, `Tools > Dojo > Build Floor Editor`. Proof: make a layout, see it in the Floors tab, reopen and resave it.
3. **Third PR, the inventory and placement:** the LAYOUTS tab, `FloorLayoutsDisplayUI`, `FloorLayoutPlacer` with the skip-taken rule, the ghost. Proof: place a layout on an empty floor and on a floor with pieces; the pieces stay.
4. Then the game port (14ze67xgf0x): its own `IPlayerFloorLayoutStore` like `IPlayerRoomStore`, the popups from the design, the YOURS section. Nothing is shared between the repos today (every Rooms file has diverged), so it is a port, not a copy.

## Needs Bob

- Approval of the design (AC 1) before PR 2 and 3 start; PR 1 has no UI and can start first if he agrees.
- The answer to the size question: is a layout's size free (the stepper), or always the viewed floor's size? The stepper is the one control the design invented.
- A flat ROOMS and LAYOUTS icon for the kit, or the go-ahead to draw them in the kit's style.
- The server side for the game port later (`player_floor_layout` table, as `player_room`).

## Open questions

- Should a layout carry its rooms as rooms (their `SavedRoom` with areas and entrances) or flatten them to tiles and walls? Rooms-as-rooms keeps departments and doors working after placement; flattening is simpler. Plan says rooms-as-rooms, since the designer lays them from the Rooms tab anyway.
- Pack layouts have no "edit a placed layout": once placed, the pieces are the floor's. EDIT on the card edits the layout file, not the floor. Confirm that reading with Bob.
- Thumbnail size for a wide floor (256 px may be too small to read); decide when the first real layout exists.
