# Ready or Not hack by ericool — 1.0

Windows x64 launcher, ESP overlay, free camera and gameplay options. Russian menu. See [README.md](README.md) for the complete Russian guide.

## Install

Download `ReadyOrNot-Hack-1.0-win-x64.zip` from [Releases](https://github.com/wericool/ReadyOrNot-Hack/releases/latest), extract it into a writable folder and launch `ReadyOrNot-Hack.exe`. The launcher locates the running game or its Steam library, installs the bundled UE4SS runtime and Lua mod, and reports if a game restart is required. Load a mission. Use windowed or borderless display mode.

Only the EXE is required to launch; settings are created automatically. The archive also includes documentation and licenses. The hack closes automatically when the attached game process exits or crashes. Minimizing the game or switching windows does not close it.

## Controls

| Key | Action |
| --- | --- |
| Insert | Menu |
| F6 | ESP toggle |
| F7 | Free camera toggle |
| Delete | Graceful exit |
| Tab | Next category |
| Arrow keys / Enter | Select and change options |

Mouse clicks also change settings. Drag the ammo position sliders to move the HUD.

## Features by category

- **ESP:** global toggle, 25–500 m range, three suspect colors; civilians have a separate color. Objects can be marked through walls when present in the local game world.
- **Characters:** suspects, civilians, arrested and inactive filters; independent labels, states and distances.
- **Display:** boxes, skeletons, independent suspect head dot, numeric HP and a separately controlled HP bar.
- **Objects:** evidence and dropped weapons; preplaced incapacitated victims; dead/unconscious bodies awaiting a report or available cuff action; active door traps marked with an orange triangle. Disarmed/triggered traps are hidden. Door position is used if the attached trap actor is unavailable.
- **Player:** current magazine ammo, including zero; independent horizontal/vertical HUD position sliders; free camera; SWAT retaliation prevention; firing and grenade throwing in the station.
- **Speed:** separate intervals for coordinates (8/16/33/50/100 ms), skeletons (33/50/100/200/500 ms), health/states (100/250/500/1000 ms) and rendering (8/16/33/50/100 ms).
- **Info:** version, connection state, actor/object counts, measured data frequency, collection and draw-command timings, WPF GPU tier, projection error and diagnostics. GPU tier is not GPU utilization; CPU draw-command time is not GPU frame time.

The head dot has its own toggle; there is no preset that changes other display options. Ammo shows the current magazine rather than inventory reserves, hides for items without a readable magazine or stale frames, and works independently of the main ESP toggle. Position and display settings persist.

Free camera, retaliation prevention and station firing start disabled on every launch. Enable retaliation prevention before violating rules; it does not clear an existing attack. For station firing, close the menu, keep the game focused and disable free camera. Release LMB first if it was already held when enabling the feature. Gameplay behavior can depend on server authority and available local data.

## Implementation and compatibility

UE4SS reads game objects and sends data to an external WPF overlay through a local named pipe. Drawing supports hardware acceleration; game data collection runs on the CPU. The overlay does not hook DirectX 11/12. Installation uses a UE4SS Lua mod rather than a PAK and does not bypass server mod checks.

Native gameplay changes require matching function signatures and this exact Shipping EXE SHA-256:

`3431A1CB1F2103756BBB9F65D54DB666FE325AA95A60386145B8EA9E6398A86F`

Unsupported native changes are reported in Info. Changes are restored when disabled, when game data becomes stale, or during graceful exit. Future game versions are not automatically supported.

## Files, removal and validation

Settings and diagnostics are local files beside the EXE. The generated `game-location.txt` contains the local game path; it is excluded from published packages. No automatic upload is implemented.

To disable the Lua mod, close the hack, set `RoNESP : 0` in `ReadyOrNot/Binaries/Win64/ue4ss/Mods/mods.txt`, and restart the game. Keep a shared UE4SS installation if other mods use it.

Build, protocol, projection, filters, HP, skeletons, head markers, ammo, body states, trap logic and rendering checks pass. Info and automatic graceful shutdown after the monitored process exits were tested. Station firing and retaliation prevention were previously confirmed by the user in game. A real active trap and remote-server gameplay behavior are not fully verified.

Build from source with `powershell -ExecutionPolicy Bypass -File .\build.ps1` on Windows x64 with .NET Framework 4.x. The pinned UE4SS archive is downloaded if missing and its checksum is verified.

Project code and UE4SS are MIT licensed. See `LICENSE`, `LICENSES.txt`, `THIRD-PARTY-NOTICES.md` and `licenses/UE4SS-MIT.txt`. No game assets are included; this project is not affiliated with VOID Interactive.
