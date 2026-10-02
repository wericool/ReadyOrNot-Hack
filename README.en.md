# Ready Or Not Solo ESP

Unofficial Windows x64 ESP for solo Ready Or Not, with Russian keyboard menu and labels.

Features: enemy/civilian boxes, 16-joint skeletons, independent numeric HP and HP bars, distances, character states, evidence/dropped weapons, reportable objects and initially incapacitated victims. Persistent settings and network-session telemetry guard.

Tested: Steam build 24942528 / menu version 133804 / UE 5.3, solo mission at 3440×1440. Separate transparent Windows Forms/GDI overlay; no DirectX hooks. Separate DX11 run untested. Borderless windowed mode recommended. Future builds, exclusive fullscreen and other resolutions are unverified.

Download the complete ZIP from [Releases](https://github.com/wericool/ReadyOrNot-Solo-ESP/releases/latest), extract to a permanent writable folder, close the game, open PowerShell in that folder:

    powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-ESP.ps1 -GameRoot "D:\SteamLibrary\steamapps\common\Ready Or Not"

Use your actual game path. Launch a solo mission and Start-ESP.cmd. Insert opens the menu; arrows/Enter change options; F6 toggles ESP; End exits. Hotkeys require the game to be foreground. Keyboard-only menu.

Source build: run build.ps1, Download-UE4SS.ps1, then installation. Requires .NET Framework 4.x, no Visual Studio. Pinned UE4SS v3.0.1-1152-ge3ba1016 with SHA-256 check. Complete release ZIP includes runtime.

Close the game before running Disable-ESP.ps1 or Enable-ESP.ps1 with the same -GameRoot argument. Disable renames the verified loader DLL and disables all UE4SS mods using it. Original game EXE/PAK files and saves are preserved. Conflicting loader versions stop installation.

UE4SS Lua reads reflected Unreal actors and writes local telemetry. External C# overlay displays it. Nothing is uploaded. Skeletons may lag due to game animation culling; some object types have no HP. Collected/reported markers depend on game flags. Full details: [Russian README](README.md).

Project and bundled UE4SS use MIT licenses; [third-party notices](THIRD-PARTY-NOTICES.md).
