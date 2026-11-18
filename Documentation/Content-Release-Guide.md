# Content release guide

How content gets from DojoAddressableCreator into the game. This builds on Bob's content versioning (ClickUp 14ze67xfaj9: creator #19 and #20, DojoAddressables #1 and #2, dojo-game-app #148 and #149).

## How it works
- **One tool:** Tools ▸ Dojo ▸ Addressables ▸ **5. Addressable Builder**.
  - **Process Addressable Folder** runs the Addressable Generator over every pack in `Assets/Process`, `default` first.
  - **Build** builds the content as the next version straight into the folder in **Save to**.
- **Every release is numbered:** 0 first, then +1 each time. Build reads the number from `version.json` in that folder.
- **One flat folder, one release:** `version.json`, `catalog_<n>.bin`, its `.hash` and every bundle sit together in `development/`. There are no `v<n>/` folders.
  - Build copies the new files in and checks them, then writes `version.json`.
  - Only then does it remove the files of earlier builds, so the folder never holds more than one release.
  - If anything fails, `version.json` and the files already there are left as they were, and a popup says why.
- **The game reads `version.json` first:**
  - Same version as last time: it keeps its cached bundles.
  - Higher version: it clears the cache and downloads the new catalogue and bundles from the same folder.
  - Lower version, or a missing or unreadable `version.json`: an error and the Retry popup.
- **The publish** (a push to DojoAddressables `main`) refuses a release with anything missing, a skipped or lower number, or a changed republish of the same number. It reads the published version from the CDN, so the deploy role needs no read access to S3.

## Test locally
1. In the Addressable Builder, set **Save to** to the folder your local web server serves, for example `E:\xamp\htdocs\addressableTest\DojoAddressables\development`.
2. **Process Addressable Folder**, then **Build**.
3. In the game, set `RootLifetimeScope` ▸ Content Settings ▸ **Source** to **Localhost** (Local Root points at that folder). Never commit it set to Localhost.
4. Play. The game keeps the last version it downloaded separately for each content root, so local and published versions never mix.

## Publish a release
1. **Merge first.** The content must be on the creator branch content is built from. Ask Bob which one: `main` or `main_development`.
2. **Pull DojoAddressables** so its `development/version.json` is the published one.
3. In the Addressable Builder, set **Save to** to DojoAddressables' `development/` folder, then **Process Addressable Folder** and **Build**.
4. In DojoAddressables, commit everything in `development/` (new files and removed old ones) and push to `main`. The workflow checks the release, uploads the bundles, then the catalogue and hash, then `version.json` last.
5. Check it in the game: Source **Remote**, press **CLEAR ALL**, then Play. The Console should show `content version <n>`, and the new content should load.

## New assets from Harold
Harold puts new 3D assets on the creator branch `development/bob/3dDump`. It's an asset store only: never merge it.
1. Pull the files you need into your working branch: `git checkout origin/development/bob/3dDump -- <files>`.
2. Run **Tools ▸ Dojo ▸ Content ▸ 1. Build MVP Kit Prefabs**. If a name needs changing, give it its own name in `MvpKitImporter.Renamed`. The MVP elevator keeps `default_elevator_mvp`: saved worlds use that address.
3. Run the Addressable Generator. Then add the new address to both MVP kit lists: **Tools ▸ Dojo ▸ Content ▸ 2. Update MVP Scene Inventory** does `RuntimeMVP`'s, and `Game.unity`'s `worldSettings.inventoryOnly` in dojo-game-app is edited by hand.
4. Test it locally (see *Test locally*), then publish it as the next release.

## Content tools
Everything for making content is under **Tools ▸ Dojo ▸ Content**:
- **1. Build MVP Kit Prefabs** and **2. Update MVP Scene Inventory**: Harold's models into the default pack, and the MVP scene's inventory list.
- **3. Export World To Pack**: the world last saved becomes `default/World/world_mvp`, the world new players start in.
- **Rooms ▸**: Mark Room Openings, Check Room Openings, Promote Room To Pack, Generate Room Thumbnails.

The old (pre-MVP) assets were deleted on 18 Nov: the first kit's pieces and rooms, its default world, the `_old` packs and the OfficeEssentialsPack. The one-off UI builders went with them. Everything is still in git at the tag `fallback/pre-mvp-cleanup`, in this repo, dojo-game-app and DojoAddressables. The strawberry pack and the area locator pin (`default_dojo_animated_pin`) were kept.
