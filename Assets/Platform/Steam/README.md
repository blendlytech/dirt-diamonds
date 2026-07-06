# Steam Platform Layer (Phase 11)

`SteamIntegration.cs` is the **only** class in the codebase that calls the Steam SDK
(Facepunch.Steamworks exclusively, per `CLAUDE.md`). Everything else goes through its
`IsAvailable`-guarded wrappers — a missing Steam client is the normal development state,
never an error (`docs/design/steam_publishing_ship_it.md` §2).

## `lib/` — vendored managed assemblies (committed)

Facepunch.Steamworks **2.5.2** (net6.0, Release), MIT-licensed, vendored from the
official GitHub release because the distribution has no complete package form:
NuGet.org stalled at 2.3.3 (Feb 2020) and has never carried the Posix build.

- Source: <https://github.com/Facepunch/Facepunch.Steamworks/releases/tag/2.5.2>
  (`Facepunch.Steamworks.2.5.2.zip` → `Release/net6.0/`), release commit `5a22fa2`.
- `Facepunch.Steamworks.Win64.dll` — P/Invokes `steam_api64`; referenced for every
  build except `linux-x64`.
- `Facepunch.Steamworks.Posix.dll` — P/Invokes `libsteam_api`; referenced only when
  publishing with `-r linux-x64` (the Godot Linux export). Same API surface, different
  native name + struct packing, which is why the platform split exists.
- The `.xml` doc files ride along for IDE IntelliSense.

Upgrading: replace all four files from a newer release zip's `Release/net6.0/` folder
and update the version + commit here and in `DirtAndDiamonds.csproj`.

## `native/` — Steam native redistributables (git-ignored drop)

`steam_api64.dll` (Windows x64) and `libsteam_api.so` (Linux x64 / Steam Deck) are
**not committed** (`.gitignore` — "copied at build time, not committed"). Drop them
here from the same release zip (`Release/Unity/redistributable_bin/win64/` and
`linux64/`); the `.csproj` copies whichever exist to the build output root
(`validate_steamworks_native` checks exactly this wiring).

Without the drop the project still builds and boots — `SteamClient.Init` fails on the
missing native, `SteamIntegration` catches it, and the session runs with Steam
features disabled. That degraded boot is the everyday dev/CI/headless path.

Both subfolders carry a `.gdignore` so the Godot importer never scans the binaries;
MSBuild reaches them by filesystem path, not `res://`.
