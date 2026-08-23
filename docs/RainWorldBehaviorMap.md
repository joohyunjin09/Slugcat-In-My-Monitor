# Rain World 행동 대응표

이 문서는 로컬 Rain World `v1.11.8`의 관리 코드에서 정적으로 확인한 동작과 현재
Windows 데스크톱 펫 구현을 일대일로 연결한다. 원작에서 관찰한 사실은
[`docs/analysis/DllFindings.md`](analysis/DllFindings.md)에 기록된 IL·메타데이터 분석을
근거로 하며, 아래의 “Desktop Implementation”은 현재 `src/RainWorldDesktopPet` 소스의
실제 타입과 메서드를 가리킨다.

## 기준 빌드와 해석 원칙

| 항목 | 기준 |
|---|---|
| Rain World | `v1.11.8`, Steam build `22785462` |
| Unity player | `2020.3.45f1 (660cd1701bd5)` |
| 분석 DLL | `RainWorld_Data/Managed/Assembly-CSharp.dll` |
| DLL SHA-256 | `B6BE1D4E18CE219D21091B51564CB6A11C1E4106B41DE903EB8E58849CB16FDB` |
| DLL MVID | `90dcf104-fbdf-4d42-b147-5089d56af681` |
| 조사 방식 | 게임을 실행하지 않은 PE/CLR metadata, decompile, IL 정적 분석 |

이 해시가 달라진 설치본에서는 같은 클래스 이름이라도 상수와 분기가 바뀌었을 수
있으므로 다시 대조해야 한다. 이 문서에서 **재현**은 수식이나 데이터가 원작과 같은
경우, **적응**은 Windows 바탕화면 환경에 맞게 의도적으로 바꾼 경우, **미구현**은
현재 소스에 대응 코드가 없는 경우를 뜻한다.

Rain World의 좌표는 아래가 아니라 위가 `+Y`인 논리 좌표를 전제로 하지만, 현재
데스크톱 구현은 Windows 화면 좌표처럼 아래가 `+Y`다. 따라서 원작의
`vel.y -= gravity`는 데스크톱에서 `Velocity.Y += gravity`로 나타난다. 숫자의 부호만
보고 서로 다른 힘으로 해석하면 안 된다.

## 공통 update 및 렌더 계약

원작의 `MainLoopProcess.RawUpdate`는 기본 `framesPerSecond=40`, 즉 논리 tick
`0.025 s`를 사용한다. 누산기 `myTimeStacker`가 엄격히 `> 1`인 동안 논리
`Update()`를 수행하고, 한 렌더 frame에서 세 번째 update를 수행하면 남은 backlog를
0으로 버린다. 그 뒤 매 렌더 frame 한 번 `GrafUpdate(myTimeStacker)`를 호출한다.
`timeStacker`는 물리 delta가 아니라 직전/현재 논리 상태 사이의 렌더 보간값이다.

현재 구현의 정확한 대응은 다음과 같다.

| 원작 책임 | 현재 소스와 심볼 | 상태 |
|---|---|---|
| 40 Hz 논리 clock | `Core/SimulationConstants.cs`의 `LogicTicksPerSecond=40`, `LogicStepSeconds=1/40` | 재현 |
| 시간 누산과 보간 alpha | `Core/FixedTimeStep.cs`의 `AddElapsed`, `ConsumeStep`, `Alpha` | 적응 |
| frame당 최대 3 catch-up 및 backlog 폐기 | `Core/GameLoop.cs`의 `Advance`; `while (steps < 3 ...)`, 뒤의 `if (steps == 3) fixedTimeStep.Reset()` | 재현 |
| tick 순서 | `GameLoop.Advance`: `DesktopPetAI.Step` → `Slugcat.Step` → `SlugcatGraphics.Step` | 구조 재현 |
| 표시 상태 보간 | `GameLoop.BuildPose` → `SlugcatGraphics.BuildPose(Interpolation, ...)`; 각 `RenderPosition` | 재현 |

`FixedTimeStep.ConsumeStep`은 부동소수 오차를 피하려고 `accumulator + 1e-7 >= step`과
같은 판정을 한다. 이는 원작의 엄격한 `myTimeStacker > 1`과 경계 한 점에서 다르다.
`AddElapsed`는 입력 시간을 임의로 clamp하지 않는다. frame hitch는 `GameLoop`의 최대
3회 catch-up과 그 뒤 backlog 폐기에서만 제한한다.

## 우선 지원 variant와 Gourmand

현재 지원 대상 네 캐릭터는 Survivor, Monk, Hunter, Gourmand다. Gourmand는 base-game
이름이 아니라 MSC의 `MoreSlugcatsEnums.SlugcatStatsName.Gourmand`라는 점을 구분한다.

| 표시 이름 | 원작 내부 이름 | 기본 RGB / hex | run | body weight | chunk당 질량 | 현재 body/hips 폭 |
|---|---|---|---:|---:|---:|---:|
| Survivor | `SlugcatStats.Name.White` | `(255,255,255)` / `#FFFFFF` | `1.0` | `1.00` | `0.3500` | `1.0 / 1.0` |
| Monk | `SlugcatStats.Name.Yellow` | `(255,255,115)` / `#FFFF73` | `1.0` | `0.95` | `0.3325` | `1.0 / 1.0` |
| Hunter | `SlugcatStats.Name.Red` | `(255,115,115)` / `#FF7373` | `1.2` | `1.12` | `0.3920` | `1.0 / 1.0` |
| Gourmand | `MoreSlugcatsEnums.SlugcatStatsName.Gourmand` | `(240,193,151)` / `#F0C197` | `1.0` | `1.35` | `0.4725` | `1.4 / 1.6` |

원작 색상은 HSL 추정값이 아니라 `PlayerGraphics.DefaultSlugcatColor`의 직접 RGB다.
실제 게임의 `SlugcatColor`는 Jolly/CUSTOM 색상, player class override,
`CustomColorsEnabled`, 기본색 순으로 결정하므로 위 표는 “기본색” 경로에 해당한다.

현재 대응은 `Creature/SlugcatVariant.cs`의 `SlugcatVariant`와
`SlugcatAppearance.For`, `Creature/Slugcat.cs`의 `SetVariant`,
`Graphics/SpriteRenderer.cs`의 `DrawAtlasBody`/`DrawProceduralBody`다. `SetVariant`는
각 chunk 질량을 `0.35 * BodyWeightFactor`로 바꾸고, renderer는 Gourmand의
`BodyA.scaleX` 기준을 `1.4`, `HipsA.scaleX` 기준을 `1.6`으로 잡는다. 원작과 마찬가지로
Gourmand도 공통 `HeadA0..17`, `BodyA`, `HipsA`, 기본 네 segment 꼬리를 사용하며
별도 Gourmand head나 tail atlas element를 사용하지 않는다.
`Program.cs`의 `ReadVariant`와 `UI/LayeredOverlayWindow.cs`의 variant menu가 이 네 값을
`GameLoop.SetVariant`로 전달한다.

원작 Gourmand는 충돌 반지름도 다른 성체처럼 `9/8`이다. `aerobicLevel >= .95`에서
exhausted, `< .4`에서 회복하는 hysteresis와 피로 호흡 pose가 있지만, 현재
`SlugcatState`/`SlugcatGraphics`에는 aerobic 또는 `gourmandExhausted` 상태가 없다.
또한 food, throwing skill, pole/corridor 속도, malnourished profile은 현재 variant
데이터에 포함되지 않는다.

---

## Player

### Rain World Class

`Player : Creature : PhysicalObject`. 두 `BodyChunk`와 하나의
`PhysicalObject.BodyChunkConnection`을 가진 실제 물리 객체이며, 입력 history,
`AnimationIndex`, `BodyModeIndex`, jump/landing/corridor/beam/water 상태를 함께 관리한다.
일반 Survivor/Monk/Hunter/Gourmand Player에는 자율 AI가 붙지 않는다. DLC의
`SlugNPC`/`SlugNPCAI`는 별도 경로이므로 이를 네 캠페인 Player의 원작 AI로 간주하지
않는다.

### Relevant Methods

| Rain World 메서드 | IL 식별값 | 이 문서에서 쓰는 책임 |
|---|---|---|
| `Player..ctor` | token `0x06000CDC`, RVA `0x000ABD7C` | chunk, connection, 마찰·중력·부력 초기화 |
| `Player.Update` | `0x06000CE0`, `0x000ACA04` | 입력/카운터, base 물리 update, 이동 update |
| `Player.checkInput` | `0x06000CE3`, `0x000B0F04` | 10개 `InputPackage` history와 입력 source |
| `Player.UpdateAnimation` | `0x06000D10`, `0x000B5C78` | animation별 pose와 힘 |
| `Player.UpdateBodyMode` | `0x06000D13`, `0x000BC040` | 환경·접촉 기반 mode 처리 |
| `Player.MovementUpdate` | `0x06000D2D`, `0x000C5CA0` | 자세 판정, 지상/공중 가속, connection 변화 |
| `Player.WallJump` / `Player.Jump` | `0x06000D2E` / `0x06000D2F` | 점프 impulse와 jump boost |
| `Player.TerrainImpact` | `0x06000CE9`, `0x000B2828` | 착지·roll·stun/death 임계값 |
| `Player.ClassMechanicsGourmand` | `0x06000CA0`, `0x000A7BEC` | Gourmand exhaustion hysteresis |

### Observed Behavior

원작 tick에서 `Creature.Update`/`PhysicalObject.Update`가 chunk 적분과 terrain collision,
connection 해결을 먼저 수행하고, 그 뒤 `Player.MovementUpdate`가 다음 tick에 쓰일 힘과
속도를 더한다. graphics update는 Player 논리 update 뒤에 온다.

`AnimationIndex`와 `BodyModeIndex`는 하나의 enum이 아니라 서로 직교하는
`ExtEnum<T>` 두 축이다. 예를 들어 `bodyMode=Default`이면서 `animation=Roll`일 수 있다.
일반 점프 예약은 `input[0].jmp && !input[1].jmp`, 즉 **누르는 edge**다. 즉시 점프할 수
없으면 `wantToJump=5`, 지상에서는 `canJump=5`를 유지한다. crouched charged jump만
`!input[0].jmp && input[1].jmp`, 즉 release edge를 쓴다.

standing 지상 목표 속도는 upper/lower chunk 각각 `4.2/4.0 * runspeedFac`, crawl은
보통 `2.5`, 기본 수평 acceleration 기준은 `2.4`다. standing jump는 upper/lower
`vel.y=4/3`, `jumpBoost=8`이며 wall jump는 `y=8/7`, 벽 반대쪽 `x=6/5`다. 원작의
`+Y`가 위라는 점을 적용한 수치다.

`BodyChunk`는 collision 해결 전 충돌축 velocity와 `lastContactPoint` 기반 first-contact를
`Player.TerrainImpact`에 전달한다. 일반 Player는 `35/60`, Gourmand는 `40/80`을
stun/lethal severity 경계로 사용하며, stun 계산은
`(int)LerpMap(speed, stunThreshold, lethalThreshold, 40, 140, 2.5)`다. 원작의 lethal은
아래 방향 floor impact에만 적용된다. 데스크톱 구현은 이 판정 결과까지 유지한 뒤
`DesktopPetImpactResult.None/Stun/MaximumStun` 안전 계층에서 lethal을 `MaximumStun`으로
바꾼다. `MaxImpactStunDurationSeconds=3.0`을 40 Hz에서 한 번만 `120` tick으로 변환하며,
최초 impact episode의 절대 deadline은 후속 지형 충돌로 갱신하지 않는다.

### Desktop Implementation

| 현재 소스 | 정확한 대응 심볼 | 역할 |
|---|---|---|
| `Creature/Slugcat.cs` | `Slugcat..ctor`, `Step`, `SetVariant`, `ApplyMovingSurfaceDelta`, `Grab`, `Release` | 두 chunk/connection 소유, tick orchestration, variant mass, desktop drag |
| `Creature/SlugcatMovement.cs` | `SlugcatMovement.ApplyInput`, `IgnoredSurfaceId` | 가상 입력을 속도·mode·animation으로 변환하고 window-top 통과 상태 관리 |
| `Creature/SlugcatState.cs` | `SlugcatState`, `AnimationIndex`, `BodyModeIndex` | 현재 접촉·방향·animation 상태 |
| `Creature/VirtualInput.cs` | `VirtualInput`, `VirtualPosture` | `X`, `Y`, `Jump`, `Pickup`, `DropThrough`와 desktop `None/Sit/Sleep` posture의 축약 입력 package |
| `Creature/SlugcatVariant.cs` | `SlugcatAppearance.For` | 네 캐릭터의 색·run·weight·폭 profile |
| `AI/DesktopPetAI.cs`, `AI/UtilityEvaluator.cs` | `Step`, `BuildContext`, `SelectBehavior`, `ProduceInput`, `UtilityContext.SaferDirection/WallContact/JumpReady/DropReady` | 사람 입력 대신 utility 기반 `VirtualInput` 생성; 원작 Player AI의 port가 아님 |
| `Core/GameLoop.cs` | `GameLoop.Advance` | 40 Hz에서 AI → Player 대응 → graphics 순서 호출 |

`Slugcat.Step`은 tick 시작 시 두 chunk의 `BeginTick`을 호출하고, chunk를 먼저
적분한다. 이어 각 chunk의 `DesktopCollisionWorld.Resolve`, `BodyConnection.Solve`
한 번, `Movement.ApplyInput` 순으로 처리한다. 따라서
입력 힘이 주로 다음 tick 위치 적분에 나타나는 원작의 큰 순서를 유지한다. 마우스로
잡힌 상태는 선택 chunk를 커서 쪽으로 보간하는 데스크톱 전용 분기다.

`VirtualInput.DropThrough`가 지상에서 들어오고 `PrimarySupportingSurfaceId > 0`이면
`SlugcatMovement`는 현재 window-top id를 `12` tick 동안 보관한다. 두 chunk의 y-down
속도를 최소 `2.5`로 내리고 `Grounded=false`, `BodyMode=Default`, `Animation=Fall`로
전환한다. 다음 12회의 collision에서 `Slugcat.Step`이 이 id를
`DesktopCollisionWorld.Resolve`에 넘겨 **같은 `WindowTop`만** 무시한다. 음수 id인
monitor/taskbar floor는 통과하지 않는다.

animation frame은 지상이고 launch tick이 아닐 때 입력 X가 0이거나 Sit/Sleep posture면
0으로 고정된다. 이동 중에는 stand `0..6`, crawl `0..10` 범위에서 순환하며 공중에서는
계속 증가한다. 점프가 실제 launch되는 tick에는 `Grounded=false`, `BodyMode=Default`를
먼저 두고 `StabilizePosture`를 건너뛰므로 stand용 수직 힘이 `-4/-3` jump velocity를
덮어쓰지 않는다.

데스크톱 AI는 좌우 edge 거리를 비교해 더 여유 있는 쪽을 `SaferDirection`으로 저장하고,
Walk/Explore가 edge `24 px` 안에 오거나 BalanceNearEdge일 때 그 방향으로 입력한다.
마우스가 `55 px` 안으로 들어오면 현재 behavior의 minimum-duration lock을 무시하고
AvoidMouse 재평가를 허용한다. Avoid 입력은 마우스 반대 방향이며, 지상·55 px 안에서는
jump도 누른다. 이는 Windows 펫의 안전 행동이지 원작 캠페인 Player AI가 아니다.

어느 chunk든 새로 wall contact를 얻으면 `BuildContext`는 `evaluationCountdown=1`로 두어
그 `Step`에서 즉시 utility를 다시 평가한다. 접촉이 끊겨도 `WallContact`는 `18` tick
grace 동안 유지되고 마지막 접촉 방향은 `lastWallDirection`에 남는다. 공중 wall contact의
`urgentClimb`은 현재 behavior의 minimum-duration lock을 건너뛰며, `ClimbWindow` 입력은
실제 contact 방향을 우선하고 contact가 잠시 깜빡이면 저장 방향으로 `X`, screen-up인
`Y=-1`, 12 tick 주기의 jump pulse를 보낸다. `WallContactReachesClimbMovement` 테스트는
contact rising edge → `ClimbWindow` → `VirtualInput` → `SlugcatMovement`의 `WallClimb`과
두 chunk의 음수 Y 속도까지 검증해 AI가 물리를 직접 움직이지 않음을 함께 고정한다.

`UtilityContext.JumpReady`/`DropReady`는 각각의 cooldown이 0인지 나타낸다. 새 behavior로
Jump를 선택하면 `240` tick(40 Hz에서 6초), DropDown을 선택하면 `400` tick(10초)의
cooldown을 시작한다. Jump score는 지상·ready·`curiosity>.5`일 때
`0.72 + 0.42*curiosity`, DropDown score는 지상·window 위·ready·edge `<24 px`·
`curiosity>.72`일 때 `1.15`이며 그 외에는 0이다. 테스트
`UtilityActionsAreReachable`은 불리한 random variation과 기존 behavior hysteresis를
포함해도 준비된 두 행동이 경쟁 행동을 이기며, ready=false에서는 score가 0임을
검증한다.

### Important Constants

| 의미 | 원작 | 현재 소스 |
|---|---:|---:|
| 논리 tick | `40 Hz` / `0.025 s` | 동일 |
| chunk radius | `9`, `8` | `MainChunkRadius=9`, `HipsChunkRadius=8` |
| 총질량 | `0.7 * bodyWeightFac` | `2 * 0.35 * BodyWeightFactor` |
| connection | rest `17`, Normal, elasticity `1`, symmetry `.5` | 동일 초기값 |
| 공기/중력/bounce/surface | `.999 / .9 / .1 / .5` | 동일 숫자, 중력은 y-down 부호 변환 |
| water friction/buoyancy | `.96 / .95` | 미구현 |
| 입력 history | 10 packages | 최근 4 package + `previousJump` |
| window-top 통과 | `goThroughFloors`를 포함한 Room floor 처리 | 같은 양수 surface id를 `12` tick 무시, 하향속도 최소 `2.5` |
| solver 반복 | connection당 tick 1회 | `ConstraintIterations=1` |
| 일반 속도 제한 | 원작 Player의 일반 전역 clamp 없음 | 동일, 전역 clamp 없음 |
| 화면 world 배율 | `pos += vel` | 내부 적분은 동일, Windows 입출력 경계에서 X/Y `DesktopWorldScale=2.20` |
| impact stun 상한 | 원작 terrain stun 최대 `140`; floor lethal 가능 | `MaxImpactStunDurationSeconds=3.0` → `120` tick, terrain death 없음 |

### Differences

- 현재 `SlugcatMovement`는 원작 `MovementUpdate`/`UpdateAnimation`/`UpdateBodyMode`의
  완전한 port는 아니지만, 지상 목표 `4.2/4.0`, crawl 기본 `2.5`/수직 입력 시 `1`,
  공중 제한 `3.6`, 입력 방향 접근량 `2.4*.5=1.2`, idle `surfaceFriction^1.5`,
  standing jump의 y-down `-4/-3`, `jumpBoost=8`은 원작 핵심값에 맞춰져 있다.
  공중 분기는 generic target-velocity controller가 아니라 현재 momentum이 제한을 넘으면
  그대로 보존하는 원작 조건식이며, `runspeedFac`를 공중 `3.6`에 곱하지 않는다.
  일반 점프는 별도 pre-jump 없이 즉시 시작하고 Stand/Crawl/공중/착지 모두 connection
  `17`을 유지한다. 단순 wall-climb 값은 데스크톱 적응이다.
- 현재도 chunk 적분·terrain 대응 collision·connection 뒤 이동 힘을 적용하므로 원작의
  큰 update 위상과 one-pass 순서가 맞는다. Windows surface query 자체와 축약된
  state 분기는 원작 Room/Player 전체와 같지 않다.
- 일반 stand 분기는 원작 y-up의 upper `+1.5`,
  lower `-4.5`를 y-down chest `-1.5`, hips `+4.5`로 반전해 적용한다. crawl/sit/sleep은
  임의 목표 자세 spring 없이 `DownOnFours`의 y-down chest `+2`, upper/lower X 반대 힘과
  `StandUp` 감쇠를 적용한다. 방향 반전은 원작 `CrawlTurn`처럼 잠시 Default mode에서
  두 chunk를 반대로 회전시키고 손에는 Crawl grip 대신 CrawlTurn target을 준다.
  launch tick에는 body-mode 힘을 명시적으로 건너뛴다.
- 현재 CLR enum은 원작의 확장 가능한 `ExtEnum` 일부만 포함한다. 원작에는
  `LedgeGrab`, `CorridorTurn`, 수영, beam, zero-G, dead 등 더 많은 animation/mode가
  있다. 일반 상승/하강은 원작처럼 `BodyMode.Default + Animation.None`을 유지하고
  velocity 부호로 구분한다. `Sit`, `Sleep`, `WallClimb`은 데스크톱 표현 상태다.
- 물, slope, 20 px tile, beam, corridor, grasp, combat, malnourishment,
  aerobic/Gourmand exhaustion은 현재 Player 대응에 없다.
- 현재 자율 행동은 Windows 환경을 위한 `DesktopPetAI`이며, 원작 캠페인 Player에서
  추출한 AI라고 주장하지 않는다. `JumpReady`/`DropReady`, 240/400-tick cooldown과
  도달성을 위한 utility score 우선순위도 반복 행동을 제한하면서 자율 행동을 실제로
  선택 가능하게 만드는 데스크톱 전용 scheduling 규칙이다. wall-contact rising-edge 즉시
  재평가, `18` tick grace/저장 방향, `urgentClimb`도 같은 계층의 Windows 전용 규칙이다.

---

## PlayerGraphics

### Rain World Class

`PlayerGraphics : GraphicsModule`. 현재 DLL에는 `CreatureGraphics`라는 공통 타입이
없으며, Player의 실제 구체 graphics class가 `PlayerGraphics`다. 두 chunk의 draw
position을 바탕으로 `head`/`legs`의 `GenericBodyPart`, `hands`의 `SlugcatHand`, 네
`TailSegment`를 tick마다 시뮬레이션하고 atlas element를 pose에서 선택한다.

### Relevant Methods

| Rain World 메서드 | IL 식별값 | 책임 |
|---|---|---|
| `PlayerGraphics..ctor` | token `0x06001C1E`, RVA `0x002165AC` | body parts와 sprite 상태 생성 |
| `PlayerGraphics.Update` | `0x06001C20`, `0x0021708C` | 호흡, 머리, 꼬리, 손, 다리 procedural update |
| `PlayerGraphics.InitiateSprites` | `0x06001C27`, `0x0021B3B4` | 기본 sprite/mesh 구성 |
| `PlayerGraphics.DrawSprites` | `0x06001C28`, `0x0021BCC4` | `timeStacker` 보간, 회전, atlas frame, Gourmand scale |
| `PlayerGraphics.ApplyPalette` | `0x06001C29`, `0x0021F17C` | body color와 환경/상태 tint |
| `SlugcatColor` / `DefaultSlugcatColor` | `0x06001C2A` / `0x06001C2C` | custom/Jolly/default 색상 경로 |
| `PlayerGraphics.SpinePosition` | `0x06001C4C`, `0x00221588` | body-tail spine의 보간 sample |
| `SlugcatHand.Update` / `EngageInMovement` | `0x06001C5F` / `0x06001C60` | 손의 grip/회수/상태별 목표 |

### Observed Behavior

보이는 Player는 full-frame animation이 아니다. `BodyA`, `HipsA`, `HeadA0..17`,
`FaceA0..8`, `PlayerArm0..12`, 상태별 legs atlas와 13-triangle tail mesh를 물리 좌표,
각도, 손 거리, look 방향으로 조합한다. `DrawSprites`는 chunk의 두 draw state와 모든
body part의 `lastPos/pos`를 `timeStacker`로 보간한다.

깨어 있는 호흡 phase는 다음과 같고, 수면에서는 tick당 `.0125`다.

```text
breath += 1 / Lerp(60, 15, Pow(aerobicLevel, 1.5))
displayBreath = 0.5 + 0.5*sin(2*pi*Lerp(lastBreath, breath, timeStacker))
```

머리는 `GenericBodyPart(rad=4, surface=.8, air=.99)`이며 upper/hips 사이 neck target에
`ConnectToPoint(radius=3, elastic=.2, adapt=.7, exaggerate=.1)`로 연결한다. 다리는
`GenericBodyPart(rad=1)` 하나이고, 손은 `SlugcatHand(rad=3)` 두 개다. atlas 팔 frame은
shoulder-target 거리 `/2`를 `0..12`로 clamp/round해 고른다.

Gourmand는 공통 head와 꼬리를 유지한 채 정상 awake 기준 `BodyA.scaleX=1.4+동적항`,
`HipsA.scaleX=1.6+동적항`으로 커진다. head scale 전용 분기는 없다.

### Desktop Implementation

| 현재 소스 | 정확한 대응 심볼 | 역할 |
|---|---|---|
| `Graphics/SlugcatGraphics.cs` | `SlugcatGraphics..ctor`, `Step`, `BuildPose`, `ApplyMovingSurfaceDelta` | head/팔/다리/꼬리 상태 갱신, 이동 surface translation, 렌더 보간 pose 생성 |
| `Graphics/SlugcatPose.cs` | `SlugcatPose` | 논리 graphics 상태와 renderer 사이의 snapshot |
| `Graphics/SpriteRenderer.cs` | `DrawAtlasBody`, `DrawAtlasArm`, `DrawHead`, `SelectFaceFrame`, `DrawTail`, `DrawOriginalTailMesh` | atlas frame 선택, FaceA 방향, 모든 body 경로의 단일 tail silhouette와 GDI+ 그리기 |
| `Graphics/BodyPart.cs` | `BodyPart.Step`, `RenderPosition`, `Translate` | current head/limb endpoint의 단순 spring particle와 current/last 동시 이동 |
| `Graphics/Limb.cs` | `Limb.Step`, `ComputeJoint`, `Translate` | 두 팔·두 다리 endpoint, sleep 공통 hand target, 시각적 관절, target/planted point 이동 |
| `Graphics/ProceduralTail.cs` | `ProceduralTail.Step`, `CurlAround`, `Translate` | 네 tail segment orchestration과 current/last 동시 이동 |
| `Creature/SlugcatVariant.cs` | `SlugcatAppearance.For` | 기본 body color와 Gourmand 폭 기준 |

`GameLoop.Advance`는 매 논리 tick `Slugcat.Step` 직후 `SlugcatGraphics.Step`을 부른다.
렌더 때 `BuildPose(fixedTimeStep.Alpha, ...)`가 chunk, head, hands, feet, tail의
`last/current`를 보간하며, `SpriteRenderer`가 atlas가 있으면 원작 element를, 없으면
GDI+ procedural body를 그린다.

window snapshot 갱신 때 `GameLoop.Advance`는 일반 지지 상태에서는 primary window-top,
`WallClimb`에서는 접촉 chunk의 `WallSurfaceId/WallSurfaceKind`에 맞는 left/right wall
delta를 구한다. `Slugcat.ApplyMovingSurfaceDelta`로 물리 chunk를 옮긴 뒤
`SlugcatGraphics.ApplyMovingSurfaceDelta`가 head, 두 손 endpoint/target, 단일 legs particle,
drawPositions 및 모든 tail segment의 current/last 좌표를 함께 평행이동한다. 이 때문에
움직인 창을 따라갈 때 graphics spring이나 렌더 보간에 한 frame짜리 늘어짐이 생기지
않는다. idle/Sit/Sleep에서는 `AnimationFrame=0`이 전달되어 걷기 legs frame도 멈춘다.

Sleep이면 `SlugcatGraphics.Step`이 `bodyCenter=(chest+hips)/2`를 `Limb.Step`에 넘기고,
두 arm 모두 같은 `TargetPosition = bodyCenter + (Facing*10, 20)`을 사용한다. Windows의
screen y-down 좌표에서 `+20`은 몸 아래쪽이며, endpoint는 spring `.55`, damping `.5`,
gravity `0`으로 그 target을 추적한다. `SleepCurlHandsShareOriginalTarget` 회귀 테스트는
`Facing=-1`에서 양손 target이 모두 `slugcat.Center+(-10,20)`인지 검증한다.

atlas head의 일반 `FaceA/FaceB` frame은 `Head-Hips` 축에서 look offset 크기에 따라 수평 성분을
줄인 뒤 screen-space angle 절댓값을 `22.5°` 단위로 반올림해 `0..8`로 고른다. Sleep은
`1`, Crawl과 `Stand + input.x!=0`은 `4`로 override한다. Sleep/눈감김은 `FaceB`, 깨어
있는 기본형은 `FaceA`를 쓴다. `OriginalFaceFrameSelection` 테스트는 수직축
`0`, 수평축 `4`, sleep `1`을 고정한다. WallClimb arm도 다른 atlas arm과 마찬가지로
`PlayerArm{frame}`만 그리며, 현재 renderer는 `OnTopOfTerrainHand` overlay를 덧그리지 않는다.

### Important Constants

| 항목 | 원작 | 현재 구현 |
|---|---|---|
| head simulation radius | `4` (`GenericBodyPart`) | 동일 |
| head neck connection | radius `3`, elastic `.2`, adapt `.7`, exaggerate `.1` | 동일 `ConnectToPoint` 수식 |
| 손 | 2 × radius `3`, connection radius `20`, default speed `7`, quickness `.5` | 2 × radius `3`, length `20`; grip/retract solver는 부분 이식 |
| 다리 | 하나의 radius `1` particle | 동일 |
| 기본 tail layout | `(rad,len)=(6,4),(4,7),(2.5,7),(1,7)` | 동일 |
| awake breath | aerobic에 따라 `1/60..1/15` | `SlugcatState.AerobicLevel`로 동일 범위 |
| sleep breath | `.0125/tick` | 동일 phase increment |
| sleep hand target | body midpoint 옆·아래의 공통점 | `bodyCenter + (Facing*10, screen-down 20)` |
| Gourmand body/hips base X scale | `1.4 / 1.6` | 동일 기준 |

### Differences

- head와 단일 legs particle은 원작 `GenericBodyPart.Update` 및 `BodyPart.ConnectToPoint`
  수식과 상수를 쓴다. 다만 3x3 tile/slope `PushOutOfTerrain`은 desktop surface query의
  부분집합이다. 손의 full grip/retract solver도 아직 부분 이식이다.
- `SlugcatGraphics.BuildPose`는 `TailRadii[i] = Radius * Stretched`를 전달하고
  `DrawOriginalTailMesh`는 atlas 유무와 무관하게 원작의 15개 vertex 및 13개 triangle
  index와 같은 하나의 topology를 쓴다. 분절 sprite/round-line tail 경로는 없다.
- 지원하는 성체 기본 스킨의 face/head 선택과 배치는 `ResolveOriginalFaceState`에서
  Stand 이동, Crawl, 일반 공중, Wall/Beam/Ledge, Sleep, Stunned/Dead를 원작 분기로
  처리한다. WallClimb에서 `OnTopOfTerrainHand` overlay는 사용하지 않는다. palette는
  기본 body color를 사용하며 Jolly/custom color, malnourished, poison, hypothermia,
  mark/light blend와 Gourmand exhaustion 호흡 pose는 없다.
- 현재 Gourmand의 기준 폭과 공통 head/tail 선택은 원작과 맞지만, sleep/malnourished
  동적 scale 식 전체는 단순화되어 있다.
- moving-window graphics는 일반 지지 상태의 primary top delta 또는 WallClimb 접촉 wall의
  kind별 delta 하나를 전체 visual chain에 적용한다. 두 body chunk가 서로 다른 moving
  surface를 지지하는 특수 경우까지 각각 다른 delta로 skinning하지는 않는다.

---

## BodyChunk

### Rain World Class

`BodyChunk`는 `PhysicalObject`가 소유하는 원형 질점이다. `pos`, `lastPos`,
`lastLastPos`, `vel`, `rad`, `mass`, contact/slope/submersion 상태를 보관하고 자체 tile
collision을 수행한다. Unity `Rigidbody2D`/`Collider2D`를 감싼 타입이 아니다.

### Relevant Methods

| Rain World 메서드 | IL 식별값 | 책임 |
|---|---|---|
| `BodyChunk..ctor` | token `0x06000B69`, RVA `0x00097EE0` | 질점과 collision 상태 초기화 |
| `BodyChunk.Update` | `0x06000B6A`, `0x00097FA0` | 중력·물·마찰·위치 적분 |
| `CheckHorizontalCollision` | `0x06000B6F`, `0x000986F0` | swept 수평 tile collision |
| `CheckVerticalCollision` | `0x06000B70`, `0x00098CC8` | swept 수직 tile collision |
| `checkAgainstSlopesVertically` | `0x06000B71`, `0x00099534` | slope 접촉과 tangent 처리 |

### Observed Behavior

원작의 기본 적분은 다음과 같다.

```text
vel.y -= owner.gravity
if submerged:
    vel.y += owner.buoyancy * effectiveRoomGravity * submersion
    vel *= Lerp(owner.airFriction, computedWaterDrag, submersion)
else:
    vel *= owner.airFriction
lastLastPos = lastPos
lastPos = pos
pos = setPos ?? pos + vel
vertical collision -> slope collision -> horizontal collision
```

Room은 20 px tile이고 collision은 swept query다. normal 속도는 `bounce`로 반사하되
`1 + 9*(1-bounce)`보다 작으면 0이 된다. Player bounce `.1`의 stop threshold는
`9.1 px/tick`이다. tangent 속도에는 `Clamp(surfaceFriction*2,0,1)`이 적용되고,
slope 바닥에는 별도 tangent 식이 있다.

### Desktop Implementation

| 현재 소스 | 정확한 대응 심볼 | 역할 |
|---|---|---|
| `Physics/BodyChunk.cs` | `BodyChunk..ctor`, `BeginTick`, `Integrate`, `RenderPosition`, `SetMass`, `WallSurfaceId`, `WallSurfaceKind` | 최소 질점 상태, y-down 적분·보간, 접촉 wall identity |
| `Physics/DesktopCollisionWorld.cs` | `Resolve`, `ResolveHorizontal`, `ResolveVertical` | ignored window-top id와 persistent wall contact를 포함한 swept window/work-area collision |
| `Creature/Slugcat.cs` | `Slugcat.Step` | 두 chunk 적분과 constraint/collision 반복 |

`BeginTick`은 `LastPosition=Position`으로 옮기고 floor/left/right/support id,
`WallSurfaceId/WallSurfaceKind` 및 `FloorImpactSpeed`를 지운다.
`Integrate`는 y-down에서 `Velocity.Y += gravity`, `Velocity *= airFriction` 뒤
원작처럼 `Position += Velocity`를 수행한다. Window rect, cursor, grab 입력은 simulation
진입 시 `2.20`으로 나누고 렌더 좌표와 local atlas pixel은 출력 시 `2.20`을 곱한다.
따라서 X/Y 이동·중력·jump·procedural part의 비율이 동일하다. `RenderPosition`은
`Lerp(LastPosition, Position, interpolation)`이다.
WallClimb도 원작처럼 base gravity `.9` 적분을 그대로 받고, mode별 접촉/slide force는
BodyChunk 적분 뒤 movement 단계에서 별도로 적용한다.
`ResolveVertical`은 wall을 가로지른 순간뿐 아니라 chunk edge가 wall에서 `1.5 px` 안에
머무는 경우와 거의 정지한 `|Velocity.X| <= .01`인 경우에도 위치를 wall에 맞추고
`ContactLeft/Right`와 그 surface의 id/kind를 다시 세운다. 따라서 `BeginTick`이 contact
state를 지워도 다음 collision에서 동일 wall identity가 지속되어
`ClimbWindow`/`WallClimb` 판정과 kind별 moving-wall delta 선택이 끊기지 않는다.

### Important Constants

| 항목 | 원작 Player | 현재 구현 |
|---|---:|---:|
| radius | upper `9`, lower `8` | 동일 |
| 기본 chunk mass | 각 `.35 * bodyWeightFac` | 동일 |
| gravity | `.9` 아래 방향 | `.9` y-down (`+Y`) |
| air friction | `.999` | `.999` |
| bounce | `.1` | floor에서 `.1` |
| surface friction | `.5`; tangent `*=Clamp(.5*2,0,1)=1` | 동일 tangent 식 |
| water friction / buoyancy | `.96 / .95` | 없음 |
| speed clamp | 이 형태의 Player 전역 clamp 없음 | 동일 |

### Differences

- 현재 chunk state에는 `lastLastPos`, `setPos`, slope/terrain normal, submersion,
  `goThroughFloors`, `rotationChunk`가 없다. 대신 window 전용 drop-through는
  `SlugcatMovement.IgnoredSurfaceId`를 `Resolve`에 전달하는 제한된 대체 경로이고,
  `WallSurfaceId/WallSurfaceKind`는 moving Windows wall을 식별하기 위한 추가 상태다.
- 현재 collision은 Rain World의 20 px tile swept solver가 아니라 Windows surface
  목록의 top/side crossing이다. slope, beam, water, object-pair collision을 하지 않는다.
- horizontal floor에서는 현재도 `rebound=abs(vy)*bounce`와
  `stopThreshold=1+9*(1-bounce)`를 적용한다. window side wall의 X `*=-.15`와
  `1.5 px` resting-contact 유지 규칙은 Windows 적응이다. 화면 밖 강제 pin/recovery는
  collision 결과를 숨기므로 제거했다.
- 현재도 각 chunk의 desktop collision 뒤 connection을 tick당 한 번 풀어 원작의
  one-pass 큰 순서를 유지한다. collision query의 공간 모델 자체는 Room과 다르다.

---

## BodyChunkConnection

### Rain World Class

`PhysicalObject.BodyChunkConnection`은 두 `BodyChunk`의 rest distance를 유지하는
중첩 타입이다. 필드는 `chunk1`, `chunk2`, `distance`, `elasticity`,
`weightSymmetry`, `active`, `type`이고, type은 `Normal`, `Pull`, `Push`의
`ExtEnum`이다.

### Relevant Methods

| Rain World 메서드 | IL 식별값 | 책임 |
|---|---|---|
| `PhysicalObject.BodyChunkConnection..ctor` | `PhysicalObject` nested type | 두 chunk와 constraint parameter 저장 |
| `BodyChunkConnection.Update` | token `0x0600478A`, RVA `0x004F6170`, IL size `298` | type 조건 판정과 위치·속도 동시 보정 |
| `PhysicalObject.Update` | token `0x06000C04`, RVA `0x0009F444` | chunk 뒤 모든 connection update 호출 |

### Observed Behavior

거리 `d`, chunk1→chunk2 단위벡터 `u`, rest `r`, symmetry `w`, elasticity `e`일 때
원작은 다음 보정을 계산한다.

```text
c1 = u * (r-d) * w       * e
c2 = u * (r-d) * (1-w)   * e
chunk1.pos -= c1; chunk1.vel -= c1
chunk2.pos += c2; chunk2.vel += c2
```

`Normal`은 항상, `Pull`은 `d>r`, `Push`는 `d<r`일 때만 푼다.
`weightSymmetry == -1`이면 `chunk2.mass/(chunk1.mass+chunk2.mass)`로 자동 계산한다.
Player의 초기값은 `r=17`, `Normal`, `e=1`, `w=.5`다. roll에서는 rest를 `10`으로
줄이고 일부 corridor turn에서는 `Pull`로 바꾼다.

### Desktop Implementation

`Physics/BodyChunkConnection.cs`의 `BodyChunkConnection`과
`BodyChunkConnectionType`이 직접 대응한다. 생성자는 `First`, `Second`, `Distance`,
`Type`, `Elasticity`, `WeightSymmetry`를 저장하고 `Solve()`가 projection을 수행한다.
`Creature/Slugcat.cs`의 constructor가 rest `17`, `Normal`, `1`, `.5`로 만들며,
`Slugcat.Step`이 tick마다 한 번 호출한다.

현재 위치 보정은 다음과 같다.

```text
error = d-r
correction = u * error * e
First.Position  += correction*w
Second.Position -= correction*(1-w)
First.Velocity  += correction*w
Second.Velocity -= correction*(1-w)
```

`correction=u*(d-r)*e`라는 부호 표현을 쓰므로 위 식은 원작의 위치·속도 correction과
동등하다. 즉 현재 구현도 상대속도를 임의로 equalize하지 않고, 그 tick의 거리 오차로
계산한 동일 벡터를 position과 velocity에 적용한다.

### Important Constants

| 항목 | 원작 Player 초기값 | 현재 구현 |
|---|---:|---:|
| rest distance | `17` | `17` |
| type | `Normal` | `Normal` |
| elasticity | `1` | `1` |
| symmetry | `.5` | `.5` |
| roll rest | `10` | 미구현 |
| solve 횟수 | tick당 1 | `ConstraintIterations=1` |

### Differences

- 위치와 velocity에 같은 거리 오차 correction을 적용하는 핵심 수식은 현재 원작과
  맞는다.
- 현재 타입은 CLR enum이고 `active`와 `rotationChunk` 설정이 없다.
  `weightSymmetry=-1` 자동 질량 계산도 없으며 solve에서 symmetry를 `0..1`로 clamp한다.
- 현재 movement는 roll/corridor에 따라 rest `10` 또는 `Pull`로 전환하지 않는다.
- solve 횟수와 collision → connection 순서는 원작과 같다. 다만 앞 단계 collision은
  Room tile solver가 아니라 Windows surface solver다.

---

## Limb

### Rain World Class

`Limb : BodyPart`. 원작 Player의 손은 그 파생형인
`SlugcatHand(connection=bodyChunks[0], rad=3, surface=.8, air=1)` 두 개다. Limb은
목표를 추적하고 terrain/beam에서 grip을 찾는 endpoint particle이며, 원작의 다리
sprite용 `GenericBodyPart`와는 별개다.

### Relevant Methods

| Rain World 메서드 | IL 식별값 | 책임 |
|---|---|---|
| `Limb.Update` | token `0x06000BDB`, RVA `0x0009E760`, IL size `606` | mode별 target 추적, 적분, terrain push |
| `Limb.FindGrip` | `0x06000BDD`, `0x0009E9D4`, IL size `1498` | 3x3 tile/slope/beam grip 검색 |
| `BodyPart.ConnectToPoint` | `0x06000B78`, `0x00099BBC` | endpoint를 host point/radius에 제한 |
| `SlugcatHand.Update` | `0x06001C5F`, `0x0022216C` | 손 mode와 회수 |
| `SlugcatHand.EngageInMovement` | `0x06001C60`, `0x002230F0` | crawl/wall/beam target 선택 |

### Observed Behavior

원작 mode는 `HuntRelativePosition`, `HuntAbsolutePosition`, `Retracted`, `Dangle`다.
relative target은 connection chunk의 회전 기준으로 world target으로 바뀐다. target이
`huntSpeed`보다 가까우면 즉시 그 거리만큼 velocity를 두고 snap 상태를 켜며, 멀면
다음과 같이 접근한다.

```text
vel = Lerp(vel, Dir(pos,target)*huntSpeed, quickness)
```

그 뒤 connection velocity를 섞고 적분·air friction·terrain push를 수행한다.
`huntSpeed`와 `quickness`는 tick 끝에 기본값으로 돌아간다. `FindGrip`은 주변 3x3
tile에서 solid edge, floor, slope, horizontal/vertical beam 중 거리와 연결 반경 조건을
만족하는 최선점을 고른다. 손은 update 후
`ConnectToPoint(connection.pos,20,false,0,connection.vel,0,0)`를 적용한다.

### Desktop Implementation

`Graphics/Limb.cs`의 `Limb`은 이름과 endpoint 목적을 대응시키지만 구조는 새로 작성된
데스크톱 모델이다. `LimbKind.Arm/Leg`과 side별 instance를 만들고, `End`에
`Graphics.BodyPart`를 둔다. `Step`은 run cycle의 sin/cos로 arm swing, leg stride/lift,
stance foot pin을 만들며 `DesktopCollisionWorld.TryGetFloor`로 floor를 찾는다.
`ComputeJoint`는 endpoint 사이에서 두 bone처럼 보이는 elbow/knee 좌표만 계산한다.

`WallClimb` arm은 connection Y에서 wall 방향 `30 px` 안을 `TryGetWall`로 조회해 실제
wall X를 target에 쓴다. 조회가 실패할 때만 `connection.X + wallDirection*10`을 쓴다.
side와 wall 방향을 함께 비교해 두 손의 screen y-down offset을 `+7`(아래 손)과
`-3`(위 손)으로 교대한다. `WallClimbHandsTargetTheWall` 테스트가 오른쪽 wall에서 두 손의
X 방향과 서로 다른 두 Y target을 검증한다.

호출 위치는 `Graphics/SlugcatGraphics.cs`의 constructor와 `Step`이다. 두 arm은 chest,
두 leg는 hips에 연결되고, `Graphics/SpriteRenderer.cs`의 `DrawAtlasArm` 또는 procedural
limb draw가 결과를 사용한다. `Limb.Translate`는 moving window delta를 endpoint의
current/last뿐 아니라 target, connection, 활성 planted point에도 적용해 발 고정점이
뒤에 남지 않게 한다.

### Important Constants

| 항목 | 원작 손 | 현재 arm | 현재 leg |
|---|---:|---:|---:|
| endpoint radius | `3` | `2` | `2.5` |
| connection radius/length | `20` | `17` | `18` |
| 기본 hunt speed | `7` | 해당 mode 없음 | 해당 mode 없음 |
| 기본 quickness | `.5` | 해당 mode 없음 | 해당 mode 없음 |
| endpoint spring | ConnectToPoint 기반 | `.22` | planted `.46`, free `.3` |
| damping | air `1` + mode 처리 | `.72` | planted `.48`, free `.68` |
| endpoint gravity | owner/body-part 처리 | `.12` y-down | `.16` y-down |
| wall target query/offset | `FindGrip`의 주변 terrain 후보 | `30 px`; fallback X `10`; Y `+7/-3` | 해당 없음 |

현재 crawl arm은 floor query 시 `Length+15`, leg stance는 `Length+20`, leg free floor
query는 `Length+16` 범위를 쓴다. leg planted point가 connection에서 `Length*1.08`보다
멀어지면 pin을 해제한다.

### Differences

- 현재 `LimbKind.Leg`은 원작 `Limb` port가 아니라 두 발을 보기 좋게 만드는
  데스크톱 전용 procedural limb다. 원작 PlayerGraphics의 legs particle은 하나다.
- 원작의 네 mode, `huntSpeed`/`quickness` reset, `rotationChunk`, snap/retract counter,
  crawl/wall/beam별 `SlugcatHand` state machine이 없다.
- `TryGetFloor`는 원작 `FindGrip`의 floor 일부만 대체한다. solid side, slope,
  horizontal/vertical beam, custom terrain probe와 grip 경쟁은 없다. WallClimb arm의
  `TryGetWall`은 Windows left/right wall의 실제 X만 찾는 별도 축약 경로다.
- 현재 관절은 renderer용 기하 결과이며 물리 constraint나 terrain collision point가
  아니다.

---

## TailSegment

### Rain World Class

`TailSegment : BodyPart`. 각 segment가 이전 segment 또는 root point에 연결되고,
늘어난 거리만큼 자신과 이전 segment의 위치·속도를 함께 보정한다. `stretched`는
constraint 상태이면서 draw mesh 반지름을 줄이는 값이다.

### Relevant Methods

| Rain World 메서드 | IL 식별값 | 책임 |
|---|---|---|
| `TailSegment..ctor` | `PlayerGraphics..ctor`에서 네 번 호출 | radius, connection radius, 이전 segment 영향 설정 |
| `TailSegment.Update` | token `0x06000C40`, RVA `0x000A119C`, IL size `632` | 적분, overstretch constraint, terrain push |
| `PlayerGraphics.Update` | `0x06001C20`, `0x0021708C` | root/anchor, damping, gravity, outward chain force |
| `PlayerGraphics.DrawSprites` | `0x06001C28`, `0x0021BCC4` | `StretchedRad`로 tail mesh 폭 계산 |

### Observed Behavior

정상 성체 Survivor/Monk/Hunter/Gourmand의 layout은 다음과 같다.

| index | radius | rest connection radius | 이전 segment 영향 | surface / air |
|---:|---:|---:|---:|---:|
| 0 | `6` | `4` | root, `1.0` | `.85 / 1` |
| 1 | `4` | `7` | `.5` | `.85 / 1` |
| 2 | `2.5` | `7` | `.5` | `.85 / 1` |
| 3 | `1` | `7` | `.5` | `.85 / 1` |

거리 `d`가 rest `r`보다 클 때 self와 previous에 초과분을 나누어 위치와 속도 모두에
적용하며 다음 값을 저장한다.

```text
stretched = Clamp((r/(d*0.5)+2)/3, .2, 1)
StretchedRad = radius * stretched
```

PlayerGraphics가 root를 hips draw position에 두고 `pull=28`에서 시작해 segment마다
절반으로 줄이는 바깥 힘을 더한다. damping은 `.75..95`, 아래 방향 힘은 `.1..5` 사이를
shape와 lower chunk submersion으로 보간하고, 각 segment는 hips에서 `9*(i+1)`보다
멀어지지 않게 제한된다.

### Desktop Implementation

| 현재 소스 | 정확한 대응 심볼 | 역할 |
|---|---|---|
| `Physics/TailSegment.cs` | `BeginUpdate`, `ConstrainTo`, `ApplyEnvironment`, `RenderPosition` | segment 적분, overstretch, damping/gravity, 보간 |
| `Graphics/ProceduralTail.cs` | constructor, `Step`, `ResolveSurface`, `CurlAround`, `Translate` | 네 segment layout, chain 힘, desktop floor, sleep curl, moving-surface 이동 |
| `Graphics/SlugcatGraphics.cs` | `Step`, `BuildPose`, `ApplyMovingSurfaceDelta` | hips/chest 입력, pose 전달, graphics chain 평행이동 |
| `Graphics/SpriteRenderer.cs` | `DrawTail`, `DrawOriginalTailMesh` | 모든 렌더 경로의 단일 연속 polygon silhouette |

`TailSegment.ConstrainTo`는 원작과 같은 `stretched` 식을 사용한다. root는 초과분 전부를
자신에게 적용하고, 나머지는 `AffectPrevious=.5`로 self/previous에 나눈다. y-down
좌표에 맞춰 `ApplyEnvironment`는 `Velocity.Y += gravity`를 쓴다.
`ProceduralTail.Step`은 layout `6/4/2.5/1`, `4/7/7/7`, outward `28`과 매 segment `.5`
감소, shape recurrence `(shape*10+1)/11`, hips 거리 `9*(i+1)`을 유지한다.
`ProceduralTail.Translate`는 moving window delta를 네 segment의 `Position`과
`LastPosition` 양쪽에 더해 물리 chain과 interpolation 기준을 함께 옮긴다.
`SlugcatGraphics.BuildPose`는 각 렌더 폭을 `Radius*Stretched`로 전달한다.
`DrawOriginalTailMesh`는 원작의 15개 mesh vertex를 그대로 계산한다. GDI+가
공유 triangle edge를 따로 anti-alias해 seam을 만들지 않도록 동일 정점의 바깥 경계를
한 번의 연속 polygon fill로 rasterize한다. atlas load 여부로 꼬리 renderer를 바꾸지 않는다.

### Important Constants

| 의미 | 원작 | 현재 구현 |
|---|---:|---:|
| segment 수 | `4` | `TailSegmentCount=4` |
| radii | `6,4,2.5,1` | 동일 |
| rest lengths | `4,7,7,7` | 동일 |
| affect previous | root `1`, 나머지 `.5` | 동일 |
| stretched 범위 | `.2..1` | 동일 |
| outward force | `28`, segment마다 `*.5` | 동일 |
| hips max distance | `9*(i+1)` | 동일 |
| damping/gravity range | `.75..95`, `.1..5` | 동일 숫자, 물 보간 제외 |

### Differences

- 현재 `ProceduralTail`은 submersion과 `EffectiveRoomGravity`가 없어 공기용 보간만 쓴다.
- 원작 `BodyPart.PushOutOfTerrain`의 tile/slope 처리를 현재 `ResolveSurface`의
  `TryGetFloor` 한 방향 충돌로 바꿨다.
- 현재도 원작처럼 계산된 `StretchedRad=Radius*Stretched`가 보간되어 화면 굵기에
  반영되며 renderer는 원작 custom 15-vertex/13-triangle mesh 배치를 쓴다.
- 현재 `CurlAround`는 sleep pose를 위한 데스크톱 전용 후처리다.
- 원작은 상태별 anchor와 lower submersion을 더 세밀하게 고르며, 현재는 Stand/Default와
  fast-standing 중심의 축약 분기다.

---

## Windows `Room` 대체

원작 `Room`은 20 px tile, solid/floor/slope/beam/water query와 physical-object pair
collision을 제공한다. 현재 구현은 이를 Unity scene이나 Rain World room을 띄우지 않고
`Physics/DesktopCollisionWorld.cs`로 대체한다.

| 원작 `Room` 책임 | 현재 Windows 대응 | 정확한 소스 |
|---|---|---|
| floor/solid 공간 | monitor별 명시적 floor·bottom taskbar top·노출 좌우 boundary, 화면 상단 안전 여백을 만족하는 visible window top 및 left/right wall | `DesktopCollisionWorld.Refresh`, `AddMonitorTerrain`, `Desktop/WindowEnumerator.cs` |
| terrain collision | impact-time X/Y를 쓰는 swept crossing, shallow penetration 및 `1.5 px` resting contact | `ResolveHorizontal`, `ResolveVertical` |
| window top 내려가기 | 선택한 양수 window surface id만 12 tick collision에서 제외 | `VirtualInput.DropThrough`, `SlugcatMovement.IgnoredSurfaceId`, `Resolve(..., ignoredHorizontalSurfaceId)` |
| limb/tail floor query | x와 최대 낙하 거리 안의 가장 가까운 horizontal surface | `TryGetFloor` |
| wall-climb hand query | 진행 방향의 실제 window left/right wall X를 최대 `30 px`에서 선택 | `TryGetWall`, `Limb.Step` |
| ledge 거리 | 현재/선호 surface의 좌우 edge 거리 | `DistanceToEdge` |
| moving platform/wall | 이전/current bounds에서 top/left/right별 delta를 계산해 body와 전체 graphics chain에 적용 | `RefreshFromSnapshots`, `GetSurfaceMovement(id, kind)`, `Slugcat.ApplyMovingSurfaceDelta`, `SlugcatGraphics.ApplyMovingSurfaceDelta` |
| world bounds | monitor floor와 실제 노출 boundary를 같은 snapshot terrain으로 제공; 강제 clamp/teleport 없음 | `RefreshFromSnapshots`, `AddMonitorBoundary`, `Resolve` |

surface 목록은 `SimulationConstants.WindowRefreshSeconds=.25`마다 갱신되며, 물리는 그
사이 snapshot을 사용한다. HWND별 previous/current rect와 z-order를 유지하고, `EnumWindows`
자체 실패에는 cache를 그대로 보존한다. 성공한 열거에서 누락된 HWND는 2회 refresh까지
유예하되 실제 HWND가 없어지거나 최소화되면 즉시 제거한다. 이는 원작 Room이 논리 tick에서 tile query를 제공하는 것과
다르다. Windows 좌표계는 y-down이다. bottom taskbar가 있으면 `WorkArea.Bottom`을
`TaskbarTop`과 `MonitorFloor`로 함께 기록하고, top/side/없는 배치에서는 `Bounds.Bottom`을
연속 monitor floor로 사용한다. 좌우 boundary는 이웃 monitor와 Y가 겹치는 구간을 빼므로
공유 seam은 통과할 수 있고 엇갈린 monitor 사이의 실제 빈 구간만 terrain으로 남는다.
`EnumWindows`의 앞→뒤 z-order에서
앞 창 bounds를 뒤 창의 top/left/right span에서 interval subtraction하므로 완전히 가린
surface는 만들지 않고 부분 가림은 보이는 구간만 남긴다. monitor work-area 상단에서
`32 px` 이내인 maximized/top-snapped window top은, 그 위에 서면 몸과 머리가 화면 밖에
놓이므로 walkable surface에서 제외한다. vertical wall도 같은 상단 여백으로 자르고,
window 바깥쪽의 chunk 중심이 실제 monitor work area에 들어오는 구간만 남겨 화면 외곽의
최대화 창 벽을 타고 사라지지 않게 한다. window bottom, 비직사각형 window region,
곡면, slope, beam, 물, Rain World object-pair collision은 모델링하지 않는다.

window-top drop-through는 원작 Room의 일반 floor/tile 통과를 그대로 옮긴 것이 아니라
Windows surface에 맞춘 제한 규칙이다. 현재 지지 id가 양수인 실제 window일 때만 같은
`WindowTop`을 12 tick 무시하고 최소 `2.5 px/tick`의 아래 방향 속도를 준다. work-area
floor의 음수 id, 다른 window top, vertical wall은 계속 충돌한다. 반대로 wall contact는
crossing이 끝난 뒤에도 `1.5 px` 근접 판정으로 매 tick 복원되어 climb AI가 안정적으로
연속 입력을 낼 수 있다.

moving window는 `.25 s` refresh마다 top, left wall, right wall delta를 따로 저장한다.
단순 translation이면 세 delta가 같고, resize이면 top X delta는 `0`, left/right X는 각
edge의 실제 이동량이며 Y는 모두 window top 이동량이다. 일반 지지 상태는 top delta를
chunk별로 적용한다. `WallClimb`은 접촉 chunk의 `WallSurfaceId/WallSurfaceKind`로 kind별
wall delta를 골라 **두 chunk 전체**를 같은 값으로 평행이동하고 그 delta를 graphics에
전달한다. 따라서 head, limb endpoint/target, planted foot, tail current/last도 함께
이동한다. `MovingWindowWallCarriesClimber` 테스트는 translation의 left-wall `(20,30)`,
climber 전체 이동, left edge만 resize할 때 top/left/right X가 각각 `0/20/0`임을 검증한다.

이 대체 계층 때문에 Player/Limb/TailSegment 수식을 포팅할 때 `Room.GetTile`을 직접
호출하는 코드를 넣지 않고 `DesktopCollisionWorld.Resolve`/`TryGetFloor` 같은 작은
Windows collision interface로 연결해야 한다.

## 런타임 DLL 비의존성

현재 실행 파일은 분석 대상 DLL을 링크하거나 로드하지 않는다.

- `src/RainWorldDesktopPet/RainWorldDesktopPet.csproj`의 managed reference는
  .NET Framework의 `mscorlib`, `System`, `System.Core`, `System.Drawing`,
  `System.Windows.Forms`, `System.Web.Extensions`, `System.Xml`뿐이다.
- `Assembly-CSharp.dll`, `UnityEngine*.dll` reference가 없고 `Assembly.Load*`로
  Rain World 코드를 실행하는 경로도 없다.
- 이 문서의 물리·pose 상수는 현재 C# 소스에 옮겨진 값이다. 런타임 behavior는
  `Assembly-CSharp.dll`의 메서드를 호출해 얻지 않는다.
- `RainWorld/EmbeddedUnityAtlasProvider.cs`는 자체
  `UnitySerializedFileReader`/`DxtDecoder`로 `RainWorld_Data/resources.assets`의 atlas
  bytes를 읽는다. `RainWorld.exe`를 시작하거나 Unity/Rain World assembly를 로드하지
  않는다.
- `RainWorldInstallation.AssemblyCSharpPath`와 `ComputeAssemblyHash`는 설치 식별·검증용
  파일 경로/유틸리티다. 현재 runtime rendering/physics가 그 DLL code에 의존한다는
  뜻이 아니다.
- `RainWorld/RainWorldLocator.cs`는 기본적으로
  `%LocalAppData%/SlugcatInMyMonitor/rain-world-path.txt`를 최근 설치 경로 저장소로
  사용한다. `RainWorldLocator(string settingsPathOverride)`는 이 backing-file 위치를
  생성자에서 주입할 수 있게 해 테스트나 host가 별도 설정 저장소를 쓰도록 한다.
  locator는 explicit path → `RAIN_WORLD_PATH` → saved path → Steam libraries → 관례적
  경로 순으로 후보를 검사하고, 유효한 경로를 주입된 저장소에 기록한다. 이 설정 I/O도
  Rain World managed DLL을 로드하지 않는다.

단, **관리 DLL 비의존**과 **설치 자산 비의존**은 다르다. 현재 locator는 유효한 Rain
World 설치 구조를 확인할 때 `RainWorld.exe`, `Assembly-CSharp.dll`, `StreamingAssets`
존재를 sentinel로 검사하고, 원작 sprite를 표시하려면 `resources.assets` 또는 loose
atlas가 필요하다. 즉 게임 본체를 실행하거나 DLL을 로드하지는 않지만, 현재 앱은 원작
자산을 쓰기 위해 로컬 설치 위치를 찾는다. atlas load에 실패하면 renderer에는
procedural fallback이 있으나, 시작 단계의 설치 유효성 정책은 별도 제약으로 남는다.

## 현재 fidelity 경계 요약

| 영역 | 그대로 유지한 핵심 | 의도적 적응 또는 남은 차이 |
|---|---|---|
| clock | 40 Hz, frame당 최대 3 catch-up, backlog 폐기, draw interpolation | DWM/monitor refresh draw, strict `>1` 대신 epsilon 경계, elapsed `.25 s` clamp |
| Player body | radius `9/8`, 총질량 `.7*weight`, rest `17`, 기본 물리 상수, collision→1 solve→movement 순서 | Room collision 공간, 상태/입력 축약, window-only 12-tick drop-through |
| desktop AI | 가상 입력이 Player 대응 계층만 구동 | safer-edge, 55 px urgent Avoid, wall rising-edge·18-tick grace·urgent climb은 Windows 펫 전용 |
| variants | 네 기본색, run/weight, Gourmand `1.4/1.6`, 공통 head/tail | skills, malnourishment, exhaustion 미구현 |
| graphics | 물리 pose 후 atlas 선택, 보간, body-head축 FaceA, stretched tail 폭, moving-surface delta 동기화 | head/limb spring, frame subset, 9-point polygon 대 원작 13-triangle SpinePosition mesh, palette 축약 |
| environment | floor/wall/edge, wall identity, kind별 moving-surface delta와 persistent wall contact를 Windows에서 제공 | Room tile/slope/beam/water/object collision 대체 |
| dependency | Rain World/Unity managed DLL을 로드하지 않음; locator settings path 주입 가능 | original atlas를 위한 설치 자산 탐색은 유지 |
