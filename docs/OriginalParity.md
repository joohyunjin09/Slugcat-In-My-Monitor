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

## Physics/timing parity table

| 항목 | 원작 | 데스크톱 구현 | 일치 | 근거/확인 방법 |
|---|---:|---:|---|---|
| simulation frequency | 40 Hz (`0.025 s`) | 40 Hz fixed accumulator | 동일 | `MainLoopProcess.framesPerSecond=40`, `RawUpdate`; ILSpy |
| render frequency | display frame마다 `GrafUpdate` | DWM composition 주기, 60/120/144/165/240 Hz | 대응 | `RawUpdate → GrafUpdate(myTimeStacker)`; `Application.Idle + DwmFlush` |
| catch-up limit | frame당 최대 3 update 뒤 backlog 폐기 | 최대 3 tick 뒤 accumulator reset | 동일 | `MainLoopProcess.RawUpdate`; ILSpy |
| graphics/animation update | simulation tick마다 1회 | simulation tick마다 1회 | 동일 | `PlayerGraphics.Update`, `animationFrame`; ILSpy/60·144·240 회귀 |
| draw interpolation | `lastPos→pos`, `timeStacker` | 모든 chunk/head/hand/foot/tail last→current | 동일 | `PlayerGraphics.DrawSprites`; ILSpy/shared interpolation 회귀 |
| BodyChunk count | 2 | 2 | 동일 | `Player..ctor`; ILSpy |
| chunk radius | 9 / 8 | 9 / 8 | 동일 | `Player..ctor`; ILSpy |
| normal mass | `.35 / .35` | `.35 / .35` | 동일 | 총 `.7`, 절반씩; ILSpy |
| gravity | `.9` per tick, Y-up 감소; WallClimb도 base gravity 유지 | `.9` per tick, screen Y-down 증가 | 동일 | `BodyChunk.Update`/`Player.UpdateBodyMode`; ILSpy/자유낙하 수치 회귀 |
| air friction | `.999` per tick | `.999` per tick | 동일 | `BodyChunk.Update`, `Player..ctor`; ILSpy |
| surface friction | `.5` | `.5` | 동일 | `Player..ctor`; ILSpy |
| bounce | `.1` | `.1` | 동일 | `Player..ctor`; ILSpy |
| water friction / buoyancy | `.96 / .95` | Windows Room에 물 없음 | 미지원 | `Player..ctor`; ILSpy |
| 일반 최대 낙하 속도 | 전역 clamp 없음 | 전역 clamp 없음 | 동일 | `BodyChunk.Update` 전체 decompile/80-tick 회귀 |
| connection | Normal, rest 17, elasticity 1, symmetry .5 | 같은 식을 tick당 1회 | 동일 | `BodyChunkConnection.Update`; ILSpy/equation 회귀 |
| internal integration | `pos += vel` | `pos += vel` | 동일 | `BodyChunk.Update`; ILSpy/free-fall 회귀 |
| desktop world scale | Room/camera transform | Windows 입력 `/2.20`, 화면 출력 `*2.20` | 데스크톱 대응 | X/Y travel·sprite·terrain 경계 회귀 |
| Crawl input 0 | Facing 자체가 물리 힘을 만들지 않음 | 현재 chunk X offset 보존, Facing 비참조 | 동일 | `Player.MovementUpdate`/`UpdateBodyMode`; ILSpy/양 Facing 30초 회귀 |
| floor/wall collision | 20 px tile swept collision | HWND top/side와 monitor floor/boundary swept crossing | 데스크톱 대응 | `CheckVerticalCollision`/`CheckHorizontalCollision`; ILSpy/좁은 창·창 끝 낙하 회귀 |
| terrain impact speed | 해결 전 충돌축 velocity 성분 | 동일 pre-impact X/Y 성분 | 동일 | `BodyChunk` → `Player.TerrainImpact`; ILSpy/pre-impact 회귀 |
| impact severity | normal `35/60`, Gourmand `40/80`, `LerpMap(...,40,140,2.5)` | 동일 판정 후 lethal을 최대 120-tick stun으로 변환 | 데스크톱 안전 대응 | first-contact·극단/반복 충돌 회귀 |
| stun update | tick당 `stun--`, `stun>=10`에서 unconscious, 물리 계속 | 동일 | 동일 | 입력 차단·물리·자연 회복 회귀 |
| surface lifetime | Room terrain identity 지속 | HWND identity cache, 열거 실패 보존, 2회 누락 유예 | 데스크톱 대응 | `EnumWindows` 성공 여부/HWND 유효성 회귀 |
| moving terrain | Room object/terrain update | 이전/현재 HWND rect delta로 연결된 두 chunk 운반 | 데스크톱 대응 | 5방향·고속 창 이동 회귀 |
| stale limb grip | terrain이 사라지면 grip 불성립 | HWND/kind/point 검증 후 `Dangle` 해제 | 데스크톱 대응 | stale-grip 회귀 |

### 원작 자유낙하 수치

초기 `posY=0`, `velY=0`에서 Windows Y-down 부호로
`velY=(velY+0.9)*0.999`, `posY+=velY`를 적용한 결과다. 어떤 값에도 `deltaTime`을
다시 곱하지 않으며 전역 속도 clamp도 없다.

| tick | time (s) | posY | velY | gravity/tick | air friction/tick |
|---:|---:|---:|---:|---:|---:|
| 0 | 0.000 | 0 | 0 | .9 | .999 |
| 1 | 0.025 | 0.8991 | 0.8991 | .9 | .999 |
| 2 | 0.050 | 2.6964009 | 1.7973009 | .9 | .999 |
| 5 | 0.125 | 13.4685314811 | 4.4865179865 | .9 | .999 |
| 10 | 0.250 | 49.3024447880 | 8.9506482034 | .9 | .999 |
| 20 | 0.500 | 187.6205598664 | 17.8121916318 | .9 | .999 |
| 40 | 1.000 | 727.7679760958 | 35.2715035274 | .9 | .999 |
| 80 | 2.000 | 2837.8459089535 | 69.1593134045 | .9 | .999 |

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
`FixedTimeStep.Alpha`를 읽어 모든 draw state에 전달한다. `LayeredOverlayWindow`는
16 ms timer가 아니라 `Application.Idle`에서 draw하고 `DwmFlush`로 monitor/DWM refresh에
맞춘다. 실패 재시도 timer는 정상 render cadence에 사용하지 않는다.

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
| maximum fall speed | 전역 clamp 없음 | 전역 clamp 없음 | 동일 |
| final screen X/Y travel | camera/world transform | `velocity * 2.20` | 데스크톱 대응 |
| stand target speed upper/lower | `4.2 / 4.0 * runspeedFac` | 동일 | 동일 |
| crawl target speed | 2.5 base, 1 with vertical input | 동일 | 동일 |
| ground acceleration | `2.4 * .5 = 1.2` | 1.2 | 동일 |
| airborne target/acceleration | `3.6 / (2.4*.5=1.2)` | 동일 | 동일 |
| standing jump upper/lower | `4 / 3` Y-up | `-4 / -3` screen Y | 동일 |
| held jump boost | start 8, decrement 1.5, add `(boost+1)*.3` | 동일, Y sign converted | 동일 |
| landing reaction | `TerrainImpact` blink/stun/death branches | 동일 blink/severity, death 대신 recoverable stun과 desktop landing compression | 동일/안전 대응 |

Feature: movement and animation transitions

Rain World:
`Player.Update`(token `0x06000CE0`) performs base physical update before
`MovementUpdate`(token `0x06000D2D`). `AnimationIndex` and `BodyModeIndex` are separate.
Jump begins on the input press edge. `UpdateAnimation` and `UpdateBodyMode` contain dedicated
stand, crawl, wall, beam, corridor, swim, zero-G, roll and special-character branches.

Current Desktop Implementation:
`Slugcat.Step` snapshots both chunks, integrates/collides them, solves the connection, then
`SlugcatMovement.ApplyInput` adds the next-tick movement forces. Stand, crawl, default airborne,
wall climb, crawl turn, sit, sleep, stunned and dead use their corresponding state branches.

Difference:
Beam/corridor/swim/zero-G/shortcut/grasp and full roll/belly-slide/super-jump branches are not
reachable in the Windows Room adapter. Creature-to-creature collision is omitted.

Fix:
Desktop AI never assigns position, animation, body part or tail state. All autonomous actions pass
through `VirtualInput` and the Player-equivalent movement layer.

### Air control parity

Ordinary `BodyMode.Default + Animation.None` air uses `dynamicRunSpeed=3.6` for both chunks and
changes X by at most `2.4*surfaceFriction=1.2` toward the input direction. It does not multiply the
air limit by `runspeedFac`, does not converge to a custom target velocity, and applies no grounded
friction branch. Thus a `+3.6` velocity with left input becomes approximately `+2.3964`, `+1.1940`,
then `-0.0072` after integration and control over three ticks. Tests A–D cover held direction,
neutral momentum, opposite input, and left/right control from a vertical fall.

### TerrainImpact and stun parity

`DesktopCollisionWorld` records the collision-axis velocity before bounce/stop resolution and
passes the original-equivalent direction, desktop normal, chunk, speed, previous directional
contact, and terrain identity through `TerrainImpactData`. Like `BodyChunk.lastContactPoint`, first
contact depends on the previous contact direction—not a window/monitor ID change. Impact callbacks
run only above the original `PhysicalObject.impactTreshhold=1`.

On first contact, Survivor/Monk/Hunter use stun/lethal thresholds `35/60`; Gourmand uses `40/80`.
Only a downward floor impact is originally lethal, while a wall impact can still reach severity 140.
Original duration/severity is `(int)LerpMap(speed, stunThreshold, lethalThreshold, 40, 140, 2.5)`.
The desktop-pet safety layer maps every originally lethal result to stun, caps only the applied value
at `MaxImpactStunDurationSeconds=3.0` (`120` ticks at 40 Hz), and fixes an absolute deadline when the
first impact starts an episode. Later terrain impacts may raise stun only within the remaining ticks;
they cannot reset that deadline. `Creature.Stunned` remains
`stun>=10`, the counter decreases once per 40 Hz tick, and `MovementUpdate` is skipped while
`stun>0`; BodyChunk integration, collision, bounce, connection, graphics particles, and limbs keep
updating. Unconscious graphics clear look direction, use `FaceStunned`, and unused hands follow the
original `SlugcatHand` retraction path. TerrainImpact never sets `dead` in the desktop pet.

After the original BodyChunk collision and BodyChunkConnection order, a connection can move one
chunk a few units back through the junction of an exposed monitor boundary and its floor. A final
contact-only projection uses the same frozen terrain snapshot for those shallow monitor penetrations.
It emits no duplicate TerrainImpact and is not an off-screen recovery teleport.

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
Crawl selects `HeadA7`/`FaceA4` (or blink `FaceB4`); face X offset ignores look X, while Y look remains independent.
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
are used. Both stretch and position are last/current interpolated. `SpriteRenderer` calculates the
exact triangle vertices/index list, then submits their shared outer boundary as one GDI+ fill so
anti-aliased shared edges cannot appear as separate TailSegments. This same mesh path is unconditional
for both loaded-atlas and procedural-body rendering; no segmented sprite/round-line tail fallback remains:

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

All body parts store Rain World simulation coordinates. There is no moving pet root and therefore
no double translation. Windows terrain/cursor data is divided by the world scale once at ingress;
`SpritePlacement` records physics source, interpolated position, anchor, local rect, overlay
position and final screen position for debug comparison.

Feature: visual size

Physics stays at original Rain World scale. A single `DesktopWorldScale=2.20` matrix maps absolute
simulation coordinates and atlas-local drawing to desktop pixels. Window surfaces, cursor/grab
input and moving-window deltas use the inverse transform at ingress. Positions, atlas pixels, tail
mesh, pen widths and X/Y travel therefore receive the same scale without changing BodyChunk radius,
gravity, connection length or procedural-part host velocity. `GraphicsBounds` transforms all
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

F1 debug output contains simulation Hz/tick/step/time/accumulator/timeStacker, frame당 simulation
step 수, measured render FPS/monitor refresh, both chunks의 pos/last/render/velocity/contact 및
surface id/kind, 현재 monitor id/bounds/work-area/taskbar/floor, gravity/air/connection/X
multiplier, surface LTRB/previous/current rect/velocity/miss, 이전/현재 air input과 두 chunk의
pre/post air X, TerrainImpact pre/post velocity·direction·normal·speed·first-contact·surface,
stun counter/initial value/conscious/dead/face, animation/body/frame/input/facing,
head/hand/foot/tail last/current/render, tail root/tip/tangent/perpendicular/radius/mesh L-R
vertices와 wireframe, grip id, look/face state.
Lines show shoulder→hand and hand→target.
Per-sprite anchor/local/overlay/final placement is retained in the pose. `ParityDiagnostics` writes
only detected body, tail, hand-length, hand-tick, or unexplained Crawl face-flip discontinuities to
`%LOCALAPPDATA%\SlugcatInMyMonitor\parity.log`; it never corrects state. A Crawl flip record includes
animation, body mode, persistent facing, look/head directions, selected face/scaleX, both hand
snapshots, targets, connection points, and limb modes.
접촉이 사라지면 `%LOCALAPPDATA%\SlugcatInMyMonitor\surface-loss.log`에 tick, chunk, HWND/kind,
reason, pos/last/velocity, 이전/현재 contact, input, surface 존재 여부, 이전/현재 rect와 surface
velocity를 기록한다. 이 로그 역시 복구·pin·teleport를 하지 않는다.
공중 입력 변화는 `air-control.log`, `impactTreshhold`를 넘은 충돌은 `terrain-impact.log`에
기록한다. monitor floor 아래 terrain escape가 실제로 관측되면 `terrain-escape.log`에 현재
monitor bounds/work area/floor, chunk 상태, 마지막 terrain, snapshot version과 후보 surface를
기록할 뿐 위치나 속도는 수정하지 않는다.

The executable test suite covers:

- exact `ConnectToPoint` equation and Futile trim/anchor coordinates;
- shared interpolation for draw positions, head, legs and tail;
- 60/144/240 render samples에서 동일한 40 Hz physics/graphics/animation 상태;
- tick 0..80 원작 자유낙하 수치와 swept landing;
- 양 Facing Crawl idle 30초 무드리프트;
- 520 ticks (13 seconds) of idle, walking, repeated turns, jump/fall/landing;
- negative virtual-screen origin round trip and graphics bounds;
- Stand/Walk retraction, Crawl target equations, 20 px arm constraints, Crawl face stability,
  rotated shoulder basis, uniform 2.20 scale and non-mutating expanded debug rendering;
- 고속 이동의 좁은 창 top swept collision과 tick당 동일 immutable snapshot version;
- 창 끝→아래 창→monitor floor 순서, taskbar/floor/boundary identity, 음수·엇갈린 monitor seam;
- 공중 입력 A–D, 반대 입력 momentum, Hunter 공중 제한 3.6;
- pre-impact 방향 성분, 방향 기반 first-contact, normal/Gourmand severity threshold와 비치명 safety 변환;
- 극단 충돌 140→120 tick cap, 반복 충돌 deadline 비연장, 3초 후 conscious recovery;
- atlas 유무와 무관한 단일 15-vertex tail topology, root/width/tip/tangent/perpendicular;
- stun 중 physics/input/limb/FaceStunned/mouse 차단과 현재 물리 상태에서의 자연 회복;
- Crawl/반전 30초 arm rotation 연속성, head start/stop 3-unit 연결, 입력 가속/마찰 parity;
- HWND 열거 실패/2회 miss grace/실제 expiry, stale limb grip release;
- 모든 방향의 빠른 moving-window carry와 5분 상당 varied-window sprite integrity;
- moving window tops/walls, occlusion, screen-edge and embedded original atlas loading.

## Remaining non-parity surface

This is a Windows desktop Player/PlayerGraphics port, not the complete Rain World runtime. The
remaining intentional boundary is full tile/slope `PushOutOfTerrain`, tile/beam-specific
`SlugcatHand.FindGrip`/foreground hand sprites, water/beam/corridor/zero-G/shortcut/grasp states,
blink/object-looker RNG, aerobic/malnourished/hypothermia/special-class graphics and collision with
other Rain World creatures. These are not hidden by smoothing, clamping, teleporting or sprite
offsets; unsupported states remain explicitly documented.
