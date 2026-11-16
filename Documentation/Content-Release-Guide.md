# Content release guide

How content gets from DojoAddressableCreator into the game. This builds on Bob's content versioning (ClickUp 14ze67xfaj9: creator #19 and #20, DojoAddressables #1 and #2, dojo-game-app #148 and #149).

## How it works
- **Every publish is a numbered release:** 0 first, then +1 each time.
- **Each release has its own folder,** `v<n>/`. It holds the catalogue `catalog_<n>.bin`, its `.hash`, and every bundle. A path is never reused, so the CDN can't serve old bytes under a reused name. That's what caused the "CRC Mismatch" errors.
- **`version.json` sits at the top** of DojoAddressables' `development/` folder: `{ "version": n, "catalog": "catalog_<n>.bin" }`.
- **The game reads `version.json` first:**
  - Same version as last time: it keeps its cached bundles.
  - Higher version: it clears the cache and downloads `v<n>/`.
  - Lower version, or a missing or unreadable `version.json`: an error and the Retry popup.
- **The publish** (a push to DojoAddressables `main`) refuses:
  - anything missing from a release;
  - a skipped or lower number;
  - a changed republish of the same number.

## Publish a release
1. **Merge first.** The content must be on the creator branch content is built from. Ask Bob which one: `main` or `main_development`.
2. **Pull DojoAddressables.** The next number is read from your local `development/version.json`. If it's out of date, you'll build a number that's already taken and the publish will refuse it.
3. In the creator, run **Tools ▸ Dojo ▸ Addressables ▸ 3. Build Content (Next Version)**. It builds `v<n>/` and writes `version.json` into DojoAddressables' `development/`.
4. In DojoAddressables, commit only `development/v<n>/` and `development/version.json`. Don't commit loose files at the top of `development/`; they belong to no release.
5. Push to `main`. The workflow checks the release, uploads `v<n>/`, then uploads `version.json` last.
6. Check it in the game: point `RootLifetimeScope` at the CDN, press **CLEAR ALL**, then Play. The Console should show `content version <n>`, and the new content should load.

> From v1 on, the deploy role needs `s3:GetObject` on `version.json` and `v*/catalog*.hash` (Bob).

## Test without publishing
Don't use menu 3 to test. A versioned build in DojoAddressables that never gets published still takes its number locally, and the publish then refuses the next real one as "skips ahead".

Use **Tools ▸ Dojo ▸ Addressables ▸ 4. Build Local Test Content** instead:
1. It builds a release laid out the same way into `DojoLocalContent/` next to the projects (`E:\hirolabz\DojoLocalContent`), outside every repo, with its own numbers.
2. Serve the folder: `cd E:\hirolabz\DojoLocalContent` then `python -m http.server 8765 --bind 127.0.0.1`.
3. In the game, set your local `RootLifetimeScope` content root to `http://127.0.0.1:8765`. Never commit that.
4. Play. The game keeps the last version it downloaded separately for each content root, so local and published versions never mix.
5. Each new local build is the next local number. The game clears its cache and downloads it.

## New assets from Harold
Harold puts new 3D assets on the creator branch `development/bob/3dDump`. It's an asset store only: never merge it.
1. Pull the files you need into your working branch: `git checkout origin/development/bob/3dDump -- <files>`.
2. Run **Tools ▸ Dojo ▸ MVP Kit ▸ 1. Build Prefabs**. If a name clashes with a first-kit piece, give it its own name in `MvpKitImporter.Renamed`, as was done for `Elevator` → `default_elevator_mvp`.
3. Run the Addressable Generator, and add the new address to both MVP kit lists: `RuntimeMVP`'s inventory list and `Game.unity`'s `worldSettings.inventoryOnly`.
4. Test with a local test build (menu 4), then publish it as the next release.
