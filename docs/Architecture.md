# Desktop pet architecture

## Runtime boundary

`RainWorldDesktopPet.exe` is a .NET Framework 4.8 Windows application. It does not reference or load `Assembly-CSharp.dll`, UnityEngine, BepInEx, RainWorld.exe, or Steam APIs. The local DLL was used only as a static analysis reference while implementing the independent simulation.

At runtime the app performs two read-only operations against a Rain World installation:

1. locate and validate the installation layout;
2. read the original player atlas from `RainWorld_Data/resources.assets` and its `.resS` stream.

Unity serialized objects are found by class ID and object name. Analysis-time path IDs and offsets are not runtime constants. No decoded bitmap is written to disk or included in build output.

## Data flow

```text
Desktop windows / monitors / mouse
               │
               ▼
       DesktopCollisionWorld
               │
     utility context + attention
               │
               ▼
         DesktopPetAI
               │
          VirtualInput
               │
               ▼
        SlugcatMovement
               │
   BodyChunk + BodyChunkConnection
               │
               ▼
 SlugcatGraphics (head/limbs/tail)
               │
      interpolated SlugcatPose
               │
               ▼
 original atlas + SpriteRenderer
               │
               ▼
      transparent Win32 overlay
```

AI never writes chunk positions. Sit and sleep are also expressed as `VirtualInput` posture intents and interpreted by movement. Dragging is the explicit user interaction exception: the selected chunk follows the mouse while the other chunk and connection continue to simulate.

## Timing

`GameLoop` accumulates real seconds and consumes 0.025-second logic ticks. It performs at most three catch-up ticks per render callback and then discards backlog, matching the analyzed `MainLoopProcess.RawUpdate` policy. Rendering uses the remaining accumulator fraction to interpolate each `lastPosition → position` pair. A display's refresh rate therefore does not change simulation speed.

## Windows Room replacement

`DesktopCollisionWorld` replaces Rain World's tile `Room` with:

- each monitor's work-area floor;
- top surfaces and side walls of visible, non-cloaked top-level windows;
- virtual-screen boundaries and off-screen recovery;
- sampled movement deltas for windows that move while supporting the pet.

Window geometry refreshes at 0.25-second intervals rather than every render frame. The pet's own overlay, desktop shell workers, taskbar windows, minimized windows, and windows owned by this process are excluded. EnumWindows z-order clips surfaces hidden behind higher windows; monitor work-area clearance also removes top/side segments that would put the pet outside the visible desktop.

## Rendering and input

The overlay is one top-down 32-bit DIB whose bounds equal `SystemInformation.VirtualScreen`, including negative monitor coordinates. The HWND remains fixed at the virtual-desktop origin; only sprites move inside it, so procedural limbs and tail cannot cross a small pet-following window boundary. `WM_DISPLAYCHANGE`/`WM_DPICHANGED` rebuild the back buffer. `UpdateLayeredWindow` presents per-pixel alpha, while `WS_EX_TOOLWINDOW`, `WS_EX_NOACTIVATE`, no taskbar entry, and `WM_NCHITTEST` minimize desktop interference. Empty pixels return `HTTRANSPARENT`; body/head hit regions accept grab-and-throw input.

The renderer caches decoded atlases, tint matrices, the back buffer, and pose arrays. Physics and procedural graphics remain at 40 Hz while the overlay timer requests interpolated frames at roughly display cadence.

## Original character selection

Survivor (`White`), Monk (`Yellow`), Hunter (`Red`), and Gourmand use the shared original `BodyA`/`HipsA`/`HeadA*`/`FaceA*` atlas path. Their default colors and stats come from the analyzed DLL. Gourmand adds the original body/hips width branches. DMS, `mods`, and `mergedmods` are not automatic asset sources; an embedded-atlas failure falls back only to a non-mod base loose-atlas location and then to procedural drawing.

## Failure behavior

Unsupported/corrupt Unity resources cause a procedural-rendering fallback rather than starting the game or extracting assets. `UpdateLayeredWindow` presentation failures get at most six exponential-backoff retries, with one back-buffer replacement after the third failure; logging is capped to the first and terminal failures. Other render/simulation exceptions pause animation after one log entry. In either paused case the tray's **Retry rendering** action creates a fresh back buffer and resumes, while the process remains available for retry or exit. Logs live under `%LOCALAPPDATA%\SlugcatInMyMonitor\errors.log`. Missing installations are resolved through an explicit folder picker.
