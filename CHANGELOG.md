# Changelog

## 1.2.0
- One EXE embeds overlay, Lua mod, pinned UE4SS and licenses.
- Detect running game or Steam libraries; install/update automatically.
- Explain and wait for restart when loader is not yet loaded.
- Configurable update interval: 100/50/33/16/8 ms, applied live and persisted.
- Keep object discovery at approximately once per second across speed settings.
- Verified all intervals and single-EXE update in a live solo mission.

## 1.1.0
- Independent arrested-character filter, hidden by default.
- Four menu groups with Tab navigation and mouse handlers.
- 16 ms telemetry target, early filtering of hidden categories and disabled skeletons.
- Reuse skeleton pens, skip unchanged telemetry parsing, show observed data Hz.
- Validated offscreen rendering and filter independence; live Lua range/skeleton filtering.
- In-game mouse verification remains incomplete due to capture failure.

## 1.0.0
- Enemy and civilian boxes, skeletons, distance and state labels.
- Independent numerical HP and HP bar controls.
- Evidence and dropped weapon markers.
- Reportable objects and initially incapacitated victims.
- Fixed streamed-level discovery using Level.OwningWorld.
- Keyboard menu, persistent options and solo-session guard.
- Portable installer with explicit game path, pinned UE4SS dependency and loader enable/disable scripts.
