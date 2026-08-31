# Rain World Workshop compatibility

This document records the local Workshop/DLL investigation used by the implementation. No
third-party audio, image, DLL, or configuration asset is copied into this repository. The
application reads assets only from the user's own Rain World and Steam Workshop installation.

## 1. Push To Meow layout inspected

Workshop item `3257541402` (`pushtomeow`, mod version 1.2.4) was installed at test time:

The active `PushToMeowMod.dll` SHA-256 was
`B37B2571C9D7AA8DA220BD7D1814CE7781CB90CC266949664FFCCB763AE155A3`. Its Windows file-version
resource reports 1.4.2.0, while both the BepInEx plugin identity and `modinfo.json` report 1.2.4;
runtime behavior in this document follows the plugin code and installed data rather than the
resource-version label.

```text
3257541402/
  modinfo.json
  plugins/
    PushToMeowMod.dll
    PushToMeowMod.pdb
    Newtonsoft.Json.dll
    Rewired_Core.dll
  modify/soundeffects/sounds.txt
  pushtomeow/custom_meows.json
  soundeffects/*.wav
```

`sounds.txt` is the authoritative SoundID-to-WAV registration. Each registration can contain
multiple comma-separated variations and parameters such as `vol`, `minPitch`, and `maxPitch`.
`custom_meows.json` maps Rain World slugcat names to short/long SoundIDs and optional volume,
with a priority value. The application parses these registrations instead of hard-coding asset
filenames.

## 2. Push To Meow DLL flow inspected

The installed DLL was decompiled without loading it into the desktop-pet process. The relevant
flow is:

- `PushToMeowMain.OnEnable` hooks `Player.Update` and registers the Improved Input Config keybind.
- `PushToMeowMain.HandleMeowInput` treats a press of at most 0.14 seconds as a short meow and a
  longer hold as a long meow; the input cooldown is 0.24 seconds.
- `PushToMeowMain.DoMeow` checks whether the player can meow, selects a SoundID through
  `MeowUtils.FindMeowSoundID`, calls `Room.PlaySound`, alerts nearby creatures, affects lungs and
  bubbles, and starts the visual response.
- `MeowUtils.LoadCustomMeows` scans `pushtomeow/custom_meows.json` in merged/active mod roots,
  orders definitions by priority, and lets later definitions replace earlier character mappings.
- `MeowUtils.DoMeowAnim` schedules its visual changes on 33 ms timers. Every Slugcat calls
  `Player.Blink(9/11)`. Non-Spearmasters also call `PlayerGraphics.LookAtPoint` toward a very high
  point for approximately 160/260 ms. Spearmaster skips only that look override and separately
  applies alternating impulses to its tail, with the second impulse 80-140 ms later.
- `SlugNPCMeowAI` contains food-ratio, danger-grasp, and stun-driven NPC calls. It is not a simple
  fixed periodic timer for adult players.

The inspected implementation does not add a general jump, arm gesture, or body-bob for a meow.
The desktop translation therefore activates the face on the nearest 40 Hz point after the 33 ms
timer (25 ms), preserves the independent 160/260 ms look and 9/11-tick blink lifetimes, and does
not stretch the pose to the decoded clip duration. `PlayerGraphics.DefaultFaceSprite` supplies
the character-specific result: ordinary adults close to `FaceB`, Artificer changes from its
directional `FaceC/FaceD` to `FaceB`, Saint is already in its `FaceB` condition, and rendered pups
use `PFaceB`. Spearmaster keeps its current look target while blinking and wiggling its tail.

## 3. Slugcat voice mapping

The mappings below came from the installed registration/configuration data plus the DLL's
Rivulet/fallback branches:

| Rain World name | Adult family | Pup selection | Short/long variations |
| --- | --- | --- | ---: |
| White, Yellow, Red | Normal | dedicated Pup family at 1.0 pitch | 8 / 8 |
| Gourmand | Fat, 1.15 volume multiplier | Fat at 1.3 pitch | 8 / 8 |
| Artificer | Coarse, 1.2 volume multiplier | Coarse at 1.3 pitch | 8 / 8 |
| Rivulet | Rivulet A, 0.8 volume multiplier | Rivulet A at 1.3 pitch | 8 / 8 |
| Spear | Spear, 0.55 volume multiplier | Spear at 1.3 pitch | 4 / 4 |
| Saint | Whispery | Whispery at 1.3 pitch | 8 / 8 |

Unknown characters use the mod's Normal fallback. Watcher, Inv, alternate Rivulet B, and the
Katzen easter egg exist in the installed files but are not reachable by the eight currently
supported desktop characters, so their WAVs are not retained.

`PushToMeowLibrary` resolves these relationships from data, validates each referenced PCM16 WAV,
and preserves registered pitch/volume plus per-character volume. `PushToMeowController` chooses
short/long variants 50/50 and uses a fullness-shaped 24-85 second interval, while suppressing
sleeping, stunned, dragged, or muted pets. Meows use the sixteen-voice priority reserve within the
128-cursor single-device mix and the same FIFO as other lifecycle commands, so a busy movement mix neither
steals an active voice nor drops the visible meow event.

## 4. Dress My Slugcat layout inspected

Workshop item `2948971756` (`dressmyslugcat`, modinfo version 2.1.7) contained:

```text
2948971756/
  modinfo.json
  plugins/DressMySlugcat.dll
  newest/plugins/DressMySlugcat.dll
  newest/plugins/DressMySlugcat.pdb
  newest/plugins/DressMySlugcatConfig.xml
  dressmyslugcat/
    template/metadata.json + paired *.png/*.txt atlases
    asymmetry template/metadata.json + paired atlases
    Shirt v2/metadata.json + paired atlases
```

The newest DLL's plugin attribute reports 2.1.5 despite the Workshop modinfo version. Both the
newest and legacy assemblies were inspected; the newest behavior is the implementation target.

## 5. How DMS discovers and registers user skins

The DLL's `Utils.ListDirectory` uses Rain World's asset view for active mods. When its optional
`LoadInactiveMods` setting is enabled it also enumerates merged mods, `ModManager.InstalledMods`,
and the game root. `AtlasHooks.LoadAtlasesInternal("dressmyslugcat")` recursively finds
`metadata.json`, requires `id`, `name`, and `author`, and loads every paired PNG/TXT atlas in that
directory. It prefixes atlas elements with the spritesheet ID and rejects duplicate IDs.

`SpriteSheet.ParseAtlases` separates `Left` and `Right` element families for asymmetric skins and
only marks a body-part group available when that group's required frames are complete.
`PlayerGraphicsHooks` remembers the original element name and substitutes the matching DMS
element at draw time. It chooses an asymmetric side from movement/body orientation and falls back
to the original element when a replacement is unavailable. Saint `HeadB*` maps to generic
`HeadA*`; Artificer `FaceC*`/`FaceD*` maps to generic `FaceA*`/`FaceB*`.

The core groups confirmed in `SpriteDefinitions` are HEAD, FACE, BODY, ARMS, HIPS, LEGS, TAIL,
FACESCAR, GILLS, TAILSPECKLES, ASCENSION, and PIXEL. Tail replacement uses `TailTexture` on the
existing triangle mesh. DMS selection itself is stored per slugcat/player in a BinaryFormatter
file; this application deliberately does not deserialize that unsafe game save format and keeps
its own current menu selection.

## 6. Workshop discovery and caching added

`WorkshopCatalog` discovers Steam roots through the registry and every `libraryfolders.vdf`, then
checks app `312520` Workshop content, Rain World's `mods`, and `mergedmods`. It reads Rain World's
Remix `EnabledMods`/`ModLoadOrder` data to distinguish active from installed-but-inactive mods.
It parses loose real-world `modinfo.json` files safely (including trailing commas), fingerprints
only relevant metadata/asset directories, and never loads a Workshop DLL.

The scan occurs at startup and on explicit refresh. `FileSystemWatcher` only marks the catalog
dirty; **Reload Sprites** or the Skin Editor's refresh action applies the change, so opening a
menu never performs Workshop enumeration, JSON parsing, PNG decode, or file probing. No such work
occurs per frame. `WorkshopAssetCache` caches decoded WAV durations for the process and
invalidates entries after file size/time changes or deletion. A part whose selection disappears
or becomes incomplete during refresh falls back to the base appearance independently.

## 7. DMS parser and renderer added

`DmsSkinCatalog` scans every installed mod for the real `dressmyslugcat/metadata.json` convention,
loads paired atlases once, validates complete frame groups, parses default colors/tail parameters,
and isolates failures per atlas and per skin. `DmsSkinDefinition.TryGetSprite` applies the actual
element-name, generic-character, and asymmetric-side rules.

`SpriteRenderer` replaces each current animation element by name, so idle, walking, crawling,
jumping/falling, climbing, sitting/sleeping, turning, and stun paths continue to use their proper
frames. It maps `TailTexture` onto the existing continuous tail mesh. Selection is isolated across
the 12 official groups: HEAD, FACE, BODY, ARMS, HIPS, LEGS, TAIL, FACESCAR, GILLS,
TAILSPECKLES, ASCENSION, and PIXEL. A skin can be selected for a group only when every required
frame for that group exists; otherwise that part remains vanilla.

The Skin Editor is the single runtime customization path. Each part independently offers
Vanilla plus active DMS sheets complete for that part, with preview/name/author/source metadata.
Settings and tray no longer expose a whole-DMS-skin selector, preventing a partial sheet from
silently affecting unrelated parts. Inactive installed skins remain unselectable, matching DMS's
default `LoadInactiveMods=false` behavior. The `--dms-skin` command-line compatibility option
atomically fills only the selected sheet's complete groups.

## 8. Locally tested installed skins

At the final scan the machine had 94 installed Rain World mods, 23 Remix-enabled mods, and 48
valid DMS spritesheets. Tested source mods included DMS's templates, Raincoat&Umbrella, Cats
(DMS), ENA Slugcats, Peeks Inanimate Insanity skins, Venny's scugz redrawn, and Dracoslug.
Render previews covered Raincoat Survivor, Venny Rivulet, and the full DMS template on
Spearmaster. The parser also exercised:

- incomplete part-only sheets (for example Raincoat body/head/arms and Cats face-only);
- asymmetric left/right atlases;
- DMS standard special groups and all base animation element families;
- corrupt Peeks hip descriptors, which are skipped while valid head/body/limb parts remain;
- a corrupt Venny Rivulet tail and Spearmaster speckle pair, which preserve the base special part;
- all Push To Meow voice targets: Survivor, Monk, Hunter, Gourmand, Artificer, Rivulet,
  Spearmaster, Saint, Watcher, and Inv where applicable.

The five newly installed source mods were inactive in the Remix list during testing. They are
recognized and logged, but must be enabled in Rain World's Remix menu before the UI allows them
to be selected.

## 9. Related implementation

The integration is split across `Workshop/DmsSkinCatalog.cs`, `UI/SkinEditorWindow.cs`,
`Graphics/SpriteRenderer.cs`, `Core/GameLoop.cs`, the `Audio` directory, and corresponding tests.
The local game-fidelity re-audit is recorded in
`docs/analysis/RainWorldFidelityOverhaul.md`.

## 10. Safety and known limits

- Missing frameworks/assets leave the integration disabled and the existing pet operational.
- Bad JSON, PNG/TXT pairs, missing WAVs, and mid-update files are logged and isolated rather than
  terminating the application. Debug detail is separated from normal release logging, and the
  log rotates at 2 MB under `%LOCALAPPDATA%/SlugcatInMyMonitor/workshop.log`.
- The runtime audio engine reads the installed base game's `sounds.txt` and admitted UnityFS
  PCM16/FSB5 Vorbis clips entirely in memory.
  If `pushtomeow` is installed, it additionally parses that mod's `[ADD]` SoundIDs and
  `custom_meows.json`. The audio worker validates and prepares only loose PCM16 WAV families
  reachable by the supported Slugcats and Slugpups; unrelated, Watcher, Inv, and alternate
  Rivulet B files remain unloaded. OGG and unsupported WAV encodings are skipped instead of guessed.
- The desktop program cannot call Rain World's `Room.PlaySound`, oracle/creature reactions,
  lungs, bubbles, or the game's input hook because it does not host a Rain World room/player.
- DMS core and confirmed special groups are supported. Other Rain World mods can call DMS's
  public `SpriteDefinitions.AddSprite` API from their own Unity/BepInEx DLL to invent arbitrary
  sprite indices, shaders, and container ordering. Loading and executing those foreign DLLs in
  this process would violate the safety requirement, so unknown custom plugin-defined accessory
  groups are not executed. Missing groups preserve the base renderer. Installed data-only DMS
  skins and the inspected core/special extra sprites work without their DLLs.
- DMS metadata tail texture and color are applied. Tail length/wideness/roundness/lift defaults
  are parsed, but arbitrary segment-count tail rebuilding is not applied because the desktop
  physics and its parity checks deliberately use Rain World's original four simulated segments.
- The implementation matches DMS's default active-mod visibility. It detects inactive installed
  skins for logging/change tracking, but does not make them selectable until Remix enables them.

## 11. Verification

`build.ps1 -Configuration Debug` and `build.ps1 -Configuration Release` compile the .NET
Framework 4.8 solution. The test executable covers the existing fixed-step physics/AI/rendering
suite, Workshop discovery without DLL loading, actual Push To Meow registration/duration parsing,
DMS per-part isolation, valid partial-atlas survival, corrupt-atlas fallback, special-sprite
fallback, actual installed UnityFS sound decoding, asynchronous desktop refresh, and preview
rendering. Final command results and commit information are reported with the delivery.
