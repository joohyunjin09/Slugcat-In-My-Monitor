# Rain World original parity

## 기준과 범위

이 문서는 로컬 Steam 설치본의 `RainWorld_Data/Managed/Assembly-CSharp.dll`
(`v1.11.8`, SHA-256 `B6BE1D4E18CE219D21091B51564CB6A11C1E4106B41DE903EB8E58849CB16FDB`)
을 ILSpy로 정적 분석한 결과와 현재 Windows 이식 코드를 비교한다. Rain World 프로세스는
실행하지 않았다. 좌표는 원작의 Y-up 수식을 Windows Y-down으로 경계에서 변환했다.

지원 우선순위는 원작 Survivor(White), Monk(Yellow), Hunter(Red), Gourmand이며 DMS,
Jolly/custom palette, malnourished 및 다른 모드 자산은 이 경로에 들어오지 않는다.

상태 표기:

- **동일**: 같은 상수와 계산 순서를 사용한다.
- **데스크톱 대응**: 원작 Room/terrain 책임을 Windows surface로 대체한다.
- **부분**: 원작에 존재하는 분기 가운데 데스크톱에서 의미 있는 일부만 이식했다.

## Update → graphics → draw 계약

Feature: simulation frequency and render interpolation

Rain World:
`MainLoopProcess.RawUpdate`(token `0x06000DC2`)는 40 Hz 논리 update를 수행하고 한
render frame의 세 번째 catch-up 뒤 backlog를 버린다. `Room.Update`에서 Player의 물리
update 후 graphics module update가 실행되며 `RoomCamera.DrawUpdate`가
`DrawSprites(timeStacker)`를 호출한다.

Current Desktop Implementation:
`GameLoop.Advance`는 고정 1/40초마다
`DesktopPetAI.Step → Slugcat.Step → SlugcatGraphics.Step`을 호출한다. AI는
`VirtualInput`만 반환한다. 최대 세 tick 뒤 accumulator를 버리고, `BuildPose`가 한 번만
`FixedTimeStep.Alpha`를 읽어 모든 draw state에 전달한다.

Difference:
Rain World의 `Room`, shortcut, grasp, water 및 creature pair update는 없다.

Fix:
`SlugcatPose`에 chunk/draw/head/legs/tail의 last/current/render를 함께 기록했다. render는
physics 또는 graphics particle을 수정하지 않는다.

## 물리 및 이동 수치

| Feature | Rain World | Desktop implementation | 상태 |
|---|---:|---:|---|
| BodyChunk count | 2 | 2 | 동일 |
| upper/lower radius | 9 / 8 | 9 / 8 | 동일 |
| normal mass | `.35 / .35` | `.35 / .35` | 동일 |
| connection distance | 17 | 17 | 동일 |
| connection elasticity/symmetry | `1 / .5` | `1 / .5` | 동일 |
| gravity per tick | `.9` downward after Y conversion | `.9` screen-down | 동일 |
| air friction | `.999` | `.999` | 동일 |
| surface friction | `.5` | `.5` | 동일 |
| bounce | `.1` | `.1` | 동일 |
| stand target speed upper/lower | `4.2 / 4.0 * runspeedFac` | 동일 | 동일 |
| crawl target speed | 2.5 | 2.5 | 동일 |
| ground acceleration | `2.4 * .5 = 1.2` | 1.2 | 동일 |
| air control upper/lower | `.18 / .153` | `.18 / .153` | 동일 |
| standing jump upper/lower | `4 / 3` Y-up | `-4 / -3` screen Y | 동일 |
| held jump boost | start 8, decrement 1.5, add `(boost+1)*.3` | 동일, Y sign converted | 동일 |
| landing reaction | `TerrainImpact` state branches | impact-derived 6-tick compression | 부분 |

Feature: movement and animation transitions

Rain World:
`Player.Update`(token `0x06000CE0`) performs base physical update before
`MovementUpdate`(token `0x06000D2D`). `AnimationIndex` and `BodyModeIndex` are separate.
Jump begins on the input press edge. `UpdateAnimation` and `UpdateBodyMode` contain dedicated
stand, crawl, wall, beam, corridor, swim, zero-G, roll and special-character branches.

Current Desktop Implementation:
`Slugcat.Step` snapshots both chunks, integrates/collides them, solves the connection, then
`SlugcatMovement.ApplyInput` adds the next-tick movement forces. Stand, crawl, default airborne,
wall climb, pre-jump/jump/fall/land, sit and sleep remain separate state values.

Difference:
Beam/corridor/swim/zero-G/shortcut/grasp and full roll/belly-slide/super-jump branches are not
reachable in the Windows Room adapter. Landing death/stun and creature collision are omitted.

Fix:
Desktop AI never assigns position, animation, body part or tail state. All autonomous actions pass
through `VirtualInput` and the Player-equivalent movement layer.

## PlayerGraphics procedural state

Feature: head particle and neck

Rain World:
`PlayerGraphics..ctor`(token `0x06001C1E`) creates
`GenericBodyPart(radius=4, surface=.8, air=.99)`. `PlayerGraphics.Update`
(token `0x06001C20`) calls `Update`, then `ConnectToPoint` at
`Lerp(upperDraw,lowerDraw,.2)+Dir(lowerDraw,upperDraw)*3`, radius 3, elastic `.2`, host upper
velocity, adapt `.7`, exaggerate `.1`.

Current Desktop Implementation:
`BodyPart.Update` and `ConnectToPoint` use the same statement order and constants. Head current and
last are interpolated only in `BuildPose`.

Difference:
The Windows surface adapter does not yet reproduce `PushOutOfTerrain`'s complete 3×3 tile/slope
search for graphics particles.

Fix:
The previous radius-8 spring/max-length head was removed.

Feature: legs

Rain World:
One `GenericBodyPart(radius=1, surface=.8, air=.99)` represents the complete legs sprite. On the
floor it connects to lower chunk plus `(legsDirection.x*8,1)` with radius 5, elastic `.25`,
host velocity `(lower.vel.x,-10)`, adapt `.5`, exaggerate `.1`.

Current Desktop Implementation:
One `BodyPart Legs` uses the same constraint, with Y signs converted. `LegsA*` is placed at its
single interpolated position, rotation comes from `Aim(legsDirection,zero)`, anchorY is `.25`, and
Stand alone applies facing scaleX.

Difference:
Beam/corridor/zero-G legs targets are partial because those Room states are unavailable.

Fix:
The two independently simulated sine-wave desktop feet were removed.

Feature: hands and arms

Rain World:
Two `SlugcatHand` particles have radius 3, connection radius 20 and default Limb hunt speed 7,
quickness `.5`. `DrawSprites` derives the shoulder from body rotation and
`4.5/(retractCounter+1)`, selects `PlayerArm{round(distance/2)}`, uses hand→shoulder rotation +90,
anchorX `.9`, and signed distance to the body line for scaleY.

Current Desktop Implementation:
`LimbMode` reproduces `HuntRelativePosition`, `HuntAbsolutePosition`, `Retracted`, and `Dangle`.
Each hand stores `pos/lastPos/vel`, relative/absolute hunt positions, hunt speed, quickness,
reached-snap state, connection snapshots, and retract counter. Tick order is the original order:
consume the previous target in `Limb.Update`, apply the 20 px `ConnectToPoint`, then choose the next
target in `EngageInMovement`. Unused Stand/Walk/short Jump/Fall hands retract after five ticks;
DownOnFours/CrawlTurn uses the original `-6+12*index + normalizedVelocity.x*20` target, WallClimb
uses the original alternating `-7/+3` Y-up offsets, and Sleep applies the common curl target.
The renderer uses the original shoulder, retract-counter spread, visibility, arm frame, rotation,
anchor and body-mode-specific scaleY formulas.

Difference:
The desktop `FindGrip` adapter searches exposed window/floor/wall surface segments rather than a
3×3 Rain World tile/slope/beam grid. Beam-only `OnTopOfTerrainHand`, grasped-object and swimming/
zero-G branches remain outside the supported desktop state set. No free-hand sine animation or
random target is present.

Feature: Crawl face direction

Rain World:
In conscious Crawl, `DrawSprites` forces head view index 7 and `FaceA4`, zeros the horizontal look
offset, and derives face scaleX from the interpolated upper/lower body direction rather than the
attention direction.

Current Desktop Implementation:
Crawl selects `HeadA7`/`FaceA4`; face X offset ignores look X, while Y look remains independent.
Strong body-axis direction selects scaleX and a 0.5 px dead band falls back to persistent Facing, so
small procedural chunk motion cannot flip the face for one frame.

Difference:
None for the supported conscious adult Crawl/DownOnFours path.

Feature: tail

Rain World:
Four `TailSegment`s are `(radius,length)=(6,4),(4,7),(2.5,7),(1,7)`, previous influence
`1,.5,.5,.5`. Update uses damping `.75..95`, gravity `.1..5`, outward force 28 halved per segment,
and hips distance limit `9*(i+1)`. Draw sprite 2 is a 15-vertex, 13-triangle custom mesh.

Current Desktop Implementation:
The same segment layout, connection solver, `StretchedRad`, damping/gravity/outward force and limit
are used. Both stretch and position are last/current interpolated. `SpriteRenderer` uses the exact
triangle index list:

```text
(0,1,2) (1,2,3) (4,5,6) (5,6,7) (8,9,10) (9,10,11)
(12,13,14) (2,3,4) (3,4,5) (6,7,8) (7,8,9)
(10,11,12) (11,12,13)
```

Difference:
Water submersion and tile/slope tail collision are replaced by desktop floor-surface queries.

## DrawSprites placement

Feature: body, hips, head and face

Rain World:
`PlayerGraphics.DrawSprites`(token `0x06001C28`) computes breath as
`.5+.5*sin(2π*Lerp(lastBreath,breath,timeStacker))`. Body uses upper draw position, rotation from
hips→upper, anchorY `.7894737`, and width
`1+Lerp(-.05,.05,breath)*verticality` (Gourmand base 1.4). Hips is
`(2*lower+upper)/3`, rotates toward interpolated tail root and has width `1+.05*breath`
(Gourmand base 1.6). Head frame folds the 34-direction angle to `HeadA0..17`. Face is
`head + Lerp(lastLook,look,t)*3 + (0,-2)` in Y-up.

Current Desktop Implementation:
The same sources, anchors, scale formulas and element selection are used. Face offset is converted
once to screen `(0,+2)`. Breath and look direction share the pose timeStacker.

Difference:
Aerobic/malnourished/sleepCurlUp, blink, eyes-closed variants and class-special overrides are
partial.

Feature: sprite indices and draw order

| index | purpose | element | source | anchor/scale |
|---:|---|---|---|---|
| 0 | torso | `BodyA` | interpolated upper draw position | anchorY `.7894737` |
| 1 | hips | `HipsA` | `(2*hips+upper)/3` | centered |
| 2 | tail | `Futile_White` mesh | interpolated tail chain | 15 vertices/13 triangles |
| 3 | head | `HeadA0..17` | interpolated head | centered/folded scaleX |
| 4 | legs | `LegsA*` | interpolated legs particle | anchorY `.25` |
| 5,6 | arms | `PlayerArm0..12` | interpolated hands | anchorX `.9` |
| 7,8 | hand foreground | `OnTopOfTerrainHand` | hand/grip state | 미구현 overlay |
| 9 | face | `FaceA0..8` | head + interpolated look | centered |

Sprites 0..6 and face 9 are emitted in the same Futile order. Slots 7/8 are documented but not
drawn until full grip/terrain hand state is ported.

## Atlas trim, pivot and pixel precision

Feature: FSprite local rectangle

Rain World:
`FSprite` restores the untrimmed source size before applying anchor. In Y-up Futile coordinates:
`textureRect=(-anchor*sourceSize)` and local Y adds
`sourceHeight-sourceRect.y-sourceRect.height`.

Current Desktop Implementation:
`AtlasElement.GetLocalRectangle` performs the equivalent Y-down conversion:

```text
x = spriteSource.x - anchorX * sourceWidth
y = spriteSource.y - (1-anchorY) * sourceHeight
```

Difference:
None for non-rotated Rain World player atlas elements. Rotated atlas records are rejected by the
loader instead of being silently centered.

Fix:
Float positions are retained through simulation, interpolation and GDI transform. Integer
conversion occurs only in Win32 window/back-buffer dimensions; there is no per-sprite pixel snap.

## Coordinate spaces and overlay

Feature: coordinate responsibility

Current Desktop Implementation:

```text
Simulation/Desktop World Space
    RenderSpace.WorldToOverlay (exactly once)
Overlay/Render Space
    AtlasElement.GetLocalRectangle
Sprite Local Space
```

All body parts store desktop world coordinates. There is no moving pet root and therefore no double
translation. `SpritePlacement` records physics source, interpolated position, anchor, local rect,
overlay position and final screen position for debug comparison.

Feature: visual size

Physics stays at original Rain World scale. A single `CharacterRenderScale=2.20` matrix is applied
around the interpolated body midpoint after world interpolation and before atlas-local drawing.
Positions, atlas pixels, tail mesh and pen widths therefore receive the same scale without changing
BodyChunk radius, gravity, connection length or movement. `GraphicsBounds` transforms all
extremities through the same matrix and scales its sprite reach margin.

Feature: overlay clipping

Rain World:
The room camera owns a render surface larger than one creature.

Current Desktop Implementation:
`LayeredOverlayWindow` owns one top-down 32-bit DIB covering
`SystemInformation.VirtualScreen`. The HWND remains at the virtual desktop origin instead of
following the pet. `WM_DISPLAYCHANGE` and `WM_DPICHANGED` rebuild bounds/back buffer. Negative
monitor origins are preserved. `GraphicsBounds` includes head, body, hips, hands, legs and every
tail segment; it is diagnostic rather than a physics clamp.

Difference:
Pixels physically outside all monitor bounds cannot be displayed, but a sprite inside a monitor is
no longer clipped by a 560×420 pet-following HWND.

## Debug and regression evidence

F1 debug output additionally contains both shoulder points, hand last/current/render/target,
connection last/current/render, max length, mode/retract state, hip/legs target, HeadDirection,
selected face element/scaleX and CharacterRenderScale. Lines show shoulder→hand and hand→target.
Per-sprite anchor/local/overlay/final placement is retained in the pose. `ParityDiagnostics` writes
only detected body, tail, hand-length, hand-tick, or unexplained Crawl face-flip discontinuities to
`%LOCALAPPDATA%\SlugcatInMyMonitor\parity.log`; it never corrects state. A Crawl flip record includes
animation, body mode, persistent facing, look/head directions, selected face/scaleX, both hand
snapshots, targets, connection points, and limb modes.

The executable test suite covers:

- exact `ConnectToPoint` equation and Futile trim/anchor coordinates;
- shared interpolation for draw positions, head, legs and tail;
- 40 simulation updates under 240 render samples;
- 520 ticks (13 seconds) of idle, walking, repeated turns, jump/fall/landing;
- negative virtual-screen origin round trip and graphics bounds;
- Stand/Walk retraction, Crawl target equations, 20 px arm constraints, Crawl face stability,
  rotated shoulder basis, uniform 2.20 scale and non-mutating expanded debug rendering;
- moving window tops/walls, occlusion, screen-edge and embedded original atlas loading.

## Remaining non-parity surface

This is a Windows desktop Player/PlayerGraphics port, not the complete Rain World runtime. The
remaining intentional boundary is full tile/slope `PushOutOfTerrain`, tile/beam-specific
`SlugcatHand.FindGrip`/foreground hand sprites, water/beam/corridor/zero-G/shortcut/grasp states,
blink/object-looker RNG, aerobic/malnourished/hypothermia/special-class graphics and collision with
other Rain World creatures. These are not hidden by smoothing, clamping, teleporting or sprite
offsets; unsupported states remain explicitly documented.
