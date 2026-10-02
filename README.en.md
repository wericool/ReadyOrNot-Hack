# Ready Or Not Solo ESP

Unofficial Windows x64 ESP for solo Ready Or Not, with Russian menu and labels.

Features: enemy/civilian boxes, 16-joint skeletons, independent numeric HP and bars, distances and states, independent arrested filter, evidence/dropped weapons, reportable objects and incapacitated victims. Four menu groups with keyboard/mouse handlers.

## One EXE launch

Download ReadyOrNot-ESP.exe from [Releases](https://github.com/wericool/ReadyOrNot-Solo-ESP/releases/latest), put it in a permanent writable folder and double-click it. The launcher contains the overlay, Lua mod, pinned UE4SS and licenses. No scripts or dependency downloads are needed by the end user.

It detects the running game, installs/updates the mod and starts the overlay. With the game closed it searches Steam libraries; if discovery fails, select the game shipping EXE once.

First loader installation/enabling requires restarting the game once. The launcher explains this and waits; it never closes the game automatically. Existing loaded UE4SS with Lua auto-reload can update the mod in the running mission. Otherwise a mod update requires restart too.

Settings, telemetry, cached game location and backups are local files created beside the EXE; nothing is uploaded. Moving the EXE only requires starting it again. Foreign loader hashes are rejected; original EXE/PAK files and saves are preserved.

Insert: menu; Tab: group; arrows/Enter: options; F6: ESP; End: exit. Mouse controls may require pausing for a free cursor. Actual in-game mouse operation remains unconfirmed due to capture failure.

## Update interval

General menu offers 100 / 50 / 33 / 16 / 8 ms, applied without restart to collection and overlay polling. Actual rate depends on game-thread load. One live solo mission measured approximately 10 / 19 / 29 / 37 / 41 Hz respectively. Default: 16 ms.

Tested base: Steam build 24942528 / menu 133804 / UE 5.3 at 3440x1440. Separate DX11 launch and exclusive fullscreen untested. Borderless recommended. One-EXE update of an already loaded mod verified in a live mission; fresh loader extraction verified in a fixture, not a complete fresh-game launch.

For source builds run build.ps1 with .NET Framework 4.x. The pinned official UE4SS archive is downloaded if absent and SHA-256 verified. Build creates one x64 EXE with embedded resources. Manual maintenance scripts remain in source, not the minimal release ZIP.

[Full Russian documentation](README.md). Project and UE4SS: MIT; [third-party notices](THIRD-PARTY-NOTICES.md).
