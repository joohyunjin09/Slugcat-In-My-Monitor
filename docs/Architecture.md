# Desktop pet architecture

## Runtime boundary

`SlugcatInMyMonitor.exe` is a .NET Framework 4.8 Windows application. It does not reference or load `Assembly-CSharp.dll`, UnityEngine, BepInEx, RainWorld.exe, or Steam APIs. The local DLL was used only as a static analysis reference while implementing the independent simulation.

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
 DirectComposition visual surfaces
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

The top-level HWND covers `SystemInformation.VirtualScreen`, including negative monitor coordinates, but uses `WS_EX_NOREDIRECTIONBITMAP` and does not own a full-desktop backing bitmap. Each active Slugcat is rendered into a small premultiplied-alpha bitmap and uploaded to its own `IDCompositionSurface`. A native Direct3D 11 bridge positions those surfaces as separate visuals and submits every pixel and position update in one DirectComposition `Commit`, preventing mixed old/new frames when several Slugcats move independently. `WM_DISPLAYCHANGE`/`WM_DPICHANGED` update the visual coordinate origin.

`WS_EX_TOOLWINDOW`, `WS_EX_NOACTIVATE`, no taskbar entry, and `WM_NCHITTEST` minimize desktop interference. Empty pixels return `HTTRANSPARENT`; body/head hit regions accept grab-and-throw input.

The renderer caches decoded atlases, tint matrices, and one reusable composition bitmap per Slugcat. Physics and procedural graphics remain at 40 Hz while the overlay timer requests interpolated frames at the highest refresh rate among monitors currently occupied by Slugcats.

## Original character selection

Survivor (`White`), Monk (`Yellow`), Hunter (`Red`), and Gourmand use the shared original `BodyA`/`HipsA`/`HeadA*`/`FaceA*` atlas path. Their default colors and stats come from the analyzed DLL. Gourmand adds the original body/hips width branches. DMS, `mods`, and `mergedmods` are not automatic asset sources; an embedded-atlas failure falls back only to a non-mod base loose-atlas location and then to procedural drawing.

## Installed-game audio

`RainWorldAudioEngine` is a single process-wide service shared by all Slugcats. It streams the installed base `soundeffects/sounds.txt` while retaining only the permitted movement and Downpour-ability definitions. Their referenced names then filter `AssetBundles/loadedsoundeffects`, including numbered clip families such as `jump2_1..7`; metadata for every unrelated bank clip is discarded while scanning. Playback reads only the FSB5 range needed for the selected clip. PCM16 payloads are retained directly; admitted FSB5 Vorbis payloads such as Spearmaster's extraction sounds are reconstructed and decoded entirely in memory on the audio worker. No game sound is copied into the repository or output package.

The 40 Hz simulation never opens files, decompresses UnityFS blocks, decodes Vorbis, or calls the Windows audio device. It only appends to a 512-command bounded queue, and the worker drains up to 256 commands per visit. Common locomotion clips and the exact sound IDs used by the implemented Downpour abilities are prepared before normal playback; other permitted movement clips enter a 24 MiB LRU cache on demand. A strict SoundID admission set allows Slugcat/Slugpup movement, installed Push To Meow voices, and only those Downpour ability sounds; unrelated weapon, world, creature-interaction, UI, and all ground/wall/belly sliding sounds are rejected before they reach the queue. UnityFS's temporary block cache is capped and explicitly trimmed after a clip is retained. One process-wide 48 kHz stereo WinMM device receives a software mix of at most 128 playback cursors. Ordinary sounds use at most ninety-six, reserving thirty-two cursors for terrain impacts, Downpour ability feedback, and meows. Each cursor references the shared cached PCM array without copying or pinning it. Four reusable 10 ms output buffers keep 40 ms queued on an above-normal-priority mixer thread, while a bounded peak limiter prevents overlapping voices from clipping into static. Saturation never resets an in-progress sound. All priority events bypass ordinary command admission while remaining in the same FIFO as loop lifecycle commands. A rejected ordinary command rolls back its cooldown record, and `PLAYALL` reserves every required layer before starting any of them, preventing a partially played explosion or movement composite.

Playback preserves `SoundLoader.SoundTrigger` semantics from the inspected 1.11.8 DLL: random or `PLAYALL` clip selection, independent clip/trigger volume and pitch ranges, `silentChance`, `rangeFac`, and `dopplerFac` with the runtime's fixed 0.5 Doppler block. The original `Volume exponent` shapes the device level; the file's listener `Volume` is constrained to Unity's documented 0..1 range. Desktop position supplies distance attenuation and stereo pan. The allowed definitions have no authored `ignoreEffects` filter; Rain World's low-pass is owned by `VirtualMicrophone` water state, which this desktop world does not have. Ordinary posture and movement one-shots are allowed to finish naturally after their trigger, matching `SoundEmitter`; the down-input edge and the desktop-only Sit/Sleep posture edge both trigger `Slugcat_Down_On_Fours`. `DynamicSoundLoop` stops its emitter on the next update when the owning action ends. The software mixer applies only a sample-accurate 12 ms sub-frame output ramp before releasing that loop cursor to avoid a discontinuity click; it is not an authored gameplay fade. Only separately configured `StaticSoundLoop.fadeOutOnDestroyFrames` instances have a longer game-authored fade, and none are in the admitted character-sound set.

Permitted movement variants are PCM-RMS normalized against their geometric mean during background preparation, with bounded correction factors and no second PCM copy. Floor-landing sounds receive a final 0.35x gain, while non-floor terrain impacts retain their 1.65x gain. The authored `vol=0.25` Spearmaster pull/grab clips receive a dedicated 2.6x post-shaping gain so both extraction phases remain audible. High-speed desktop impacts above the DLL's 12-unit feedback threshold bypass retained-contact false negatives and the bounded admission limit, then use the reserved voice band without evicting another voice. The saved Settings master-volume control is applied last over a 0–200% range. Changing it updates the shared mixer gain in place, including active loops, without reopening the audio device or restarting playback.

The same worker detects an installed `pushtomeow` Workshop/local mod without loading its DLL, parses `custom_meows.json`, and derives the `[ADD]` SoundIDs reachable by the eight supported Slugcats and their pup forms before retaining mod sound definitions or WAV paths. Only those loose PCM16 WAVs are indexed, validated, and prepared inside the same 24 MiB cache; Watcher, Inv, alternate Rivulet B, and unrelated loose files are not retained. Each `GameLoop` owns only a tiny seeded cadence controller; clip bytes and device voices remain process-wide. Mute is a volatile admission gate plus a worker command that immediately resets all active one-shots and loops. A missing or invalid saved mute preference defaults to muted; every menu or Settings change is persisted immediately and the last state is written again during shutdown.

The cadence preserves the inspected mod's fullness-shaped hungry-SlugNPC timing while narrowing it to 24–85 seconds for an always-present desktop pet. Short/long selection is 50/50. The DLL's 33 ms face timer maps to the nearest fixed-step activation at 25 ms. Every character then uses its `PlayerGraphics` closed-eye family for 9/11 ticks; non-Spearmasters also raise the face through a 160/260 ms upward look. During standing movement and crawling, `PlayerGraphics` keeps image 4 while Blink changes it to `FaceB4` or `PFaceB4`; the upward face offset remains active. Adult faces resolve through each selected profile, pups use `PFaceB`, and Spearmaster keeps its look target while applying both tail-velocity phases with the original 80–140 ms separation.

## Failure behavior

Unsupported/corrupt Unity resources cause a procedural-rendering fallback rather than starting the game or extracting assets. DirectComposition, Direct3D, GDI drawing, or simulation failures pause animation after one log entry. The tray's **Retry rendering** action recreates the DirectComposition device and surfaces, then resumes while the process remains available for retry or exit. Logs live under `%LOCALAPPDATA%\SlugcatInMyMonitor\errors.log`. Missing installations are resolved through an explicit folder picker.
