# Godot 프로토타입 검토 및 .NET Framework 4.8 이관 설계

검토 기준은 저장소 `fecb6f2` (`feat: add Godot procedural physics prototype`)와 사용자가 제공한 Rain World 독립 데스크톱 펫 요구사항이다. 이 문서는 현재 프로토타입을 완성품으로 평가하지 않고, 어떤 개념을 보존하고 어떤 구현을 폐기해야 하는지 결정하기 위한 이관 기준서다.

## 1. 검토 범위와 검증 한계

검토한 주요 파일은 다음과 같다.

- `project.godot`
- `scenes/main.tscn`
- `src/main.gd`
- `tests/slugcat_smoke_test.gd`
- `README.md`
- `tools/validate-dms-template.mjs`
- `package.json`

상태 표기는 다음과 같다.

- **구현**: 현재 코드에서 실제 동작 경로가 존재한다.
- **부분 구현**: 데모 수준의 개념은 있으나 요구사항의 의미 또는 완성도를 충족하지 못한다.
- **미구현**: 대응 코드가 없다.
- **근거 없음**: 동작은 있으나 Rain World DLL/자산 분석에서 유래했다는 추적성이 없다.

검증 환경에는 Godot 실행 파일이 없어 `tests/slugcat_smoke_test.gd`를 실제로 실행하지 못했다. 따라서 Godot 관련 평가는 정적 분석과 코드 수식의 독립 재생을 바탕으로 한다. `npm test`는 실행했으나 로컬 DMS fixture가 없어 종료 코드 1로 실패했다. 이 실패는 물리 프로토타입과 무관하지만, 현재 기본 테스트 명령이 저장소만 체크아웃한 환경에서 성공하지 않는다는 뜻이다.

## 2. 결론

현재 코드는 **투명 창, 두 몸통 점, 거리 제약, Verlet 꼬리, 드래그/던지기, 임시 디버그 실루엣을 빠르게 확인하는 Godot 스파이크**로서는 유용하다. 그러나 사용자의 핵심 목표인 “로컬 Rain World DLL과 자산을 분석한 뒤, 원본 동작 원리를 C# .NET Framework 4.8 독립 오버레이로 재구현”하는 기반으로 그대로 이식해서는 안 된다.

핵심 판단은 다음과 같다.

1. Rain World 설치 탐색, DLL 분석, 클래스/IL/상수 추적, `docs/RainWorldBehaviorMap.md`가 전혀 없다. 현재 상수와 수식은 모두 프로토타입 값이며 원본 근거가 없다.
2. 물리 업데이트 순서가 접촉 이후 몸통/꼬리 거리 불변식을 깨고, 위치 보정 결과를 속도 또는 Verlet 이력에 반영하지 않는다. 가만히 떨어뜨리는 최소 시나리오조차 안정적인 휴지 상태가 되지 않는다.
3. 걷기, 점프, 자세/애니메이션 상태, procedural limb, foot planting, Utility AI, `VirtualInput`, 창 표면 충돌은 없다.
4. 투명·상단·테두리 없음은 구성되어 있지만 창은 900×600 고정 viewport다. 실제 데스크톱, 작업 표시줄, 다중 모니터, DPI, top-level window를 다루지 않는다.
5. 테스트는 초기 배열 크기와 초기 몸통 거리만 확인한다. 한 번의 물리 tick, 충돌, 꼬리 제약, 입력, 장시간 안정성도 검증하지 않는다.

따라서 권장 전략은 **Godot 코드를 직역하지 않고**, 먼저 DLL 분석 결과를 데이터와 문서로 고정한 다음, Win32/GPU 계층과 분리된 순수 C# 시뮬레이션 커널을 작성하는 것이다. 기존 프로토타입은 시각적 디버그 리그, 상호작용 아이디어, 실패 회귀 시나리오의 참고 자료로 보존한다.

## 3. 현재 실행 흐름

`src/main.gd` 하나가 다음 책임을 모두 가진다.

```text
Godot physics tick (60 Hz)
  ├─ 마우스 속도 샘플
  ├─ 두 몸통 점 적분
  ├─ 꼬리 Verlet 적분
  ├─ 몸통/꼬리 거리 제약 × 8
  ├─ viewport 바닥/좌우 경계 보정
  ├─ 마우스 통과 polygon 갱신
  └─ 즉시 모드 벡터 렌더 예약

Godot input event
  ├─ 몸통 점 선택
  ├─ 드래그 동안 전체 창 입력화
  └─ 릴리스 시 샘플 속도로 던짐
```

이 구조에는 Simulation, Desktop collision, AI, Pose generation, Asset loading, Rendering, Overlay host 사이의 경계가 없다. C# 이관 때 이 책임 분리가 가장 먼저 이루어져야 한다.

## 4. 요구사항 대비 구현 현황

| 요구 영역 | 상태 | 현재 근거 | 판정 |
|---|---|---|---|
| Rain World 설치 경로 탐색 | 미구현 | 관련 코드 없음 | Steam library, 일반 경로, 수동 지정 모두 필요 |
| `Assembly-CSharp.dll` 분석 | 미구현 | Mono.Cecil/dnlib/Reflection 도구 없음 | 구현보다 먼저 수행해야 하는 선행 단계 |
| 핵심 클래스/메서드/IL/상수 추적 | 미구현 | `Player`, `PlayerGraphics`, `BodyChunk` 등 언급 없음 | 현재 수치에는 원본 추적성이 없음 |
| `docs/RainWorldBehaviorMap.md` | 미구현 | 문서 없음 | 원본 → 독립 구현 매핑 gate 필요 |
| Rain World 프로세스 비의존 | 구현 | 실행/참조 코드가 없음 | 단, 현재는 Godot runtime에 의존 |
| C# .NET Framework 4.8 | 미구현 | Godot/GDScript 프로젝트 | 목표 runtime으로 재구성 필요 |
| 두 `BodyChunk` 개념 | 부분 구현, 근거 없음 | `body_positions`, `body_velocities` 두 원소 | 클래스, 질량, 반지름별 상태, 접촉 상태가 없음 |
| `BodyChunkConnection` 개념 | 부분 구현, 근거 없음 | 반복 거리 projection | 충돌 후 불변식이 깨지고 속도 일관성이 없음 |
| 중력/공기 감쇠 | 부분 구현, 근거 없음 | 초 단위 적분과 기준 tick 보정 | 원본 값/순서 미확인 |
| 마찰/반발 | 부분 구현, 근거 없음 | viewport 경계에서 계수 곱 | contact model이 아니라 경계별 매직 넘버 |
| 속도 제한/CCD | 미구현 | clamp 없음 | 빠른 던지기와 얇은 창 표면에 필수 |
| 걷기/점프/낙하/착지 상태 | 미구현 | 자유낙하와 경계 반발만 존재 | locomotion과 state machine 필요 |
| 기어가기/웅크리기/미끄러짐/벽 | 미구현 | 관련 상태 없음 | DLL 분석 후 단계적으로 추가 |
| 여러 segment 꼬리 | 부분 구현, 근거 없음 | anchor 포함 8점, 7개 링크 | segment별 반지름/길이/접촉/이력 보정 없음 |
| Procedural body/head | 부분 구현, 근거 없음 | 두 몸통 점에서 축과 머리 위치 계산 | pose state, stretch/compression 규칙 미비 |
| Procedural arm/leg | 미구현 | 고정 offset 선분 | Limb state, 목표점, IK, foot planting 없음 |
| 눈/관심 대상 | 부분 구현, 근거 없음 | 마우스를 즉시 바라봄 | attention selection과 smoothing 없음 |
| 호흡/idle/sleep/blink | 미구현 | 관련 state/counter 없음 | 원본 관찰 후 구현 필요 |
| 실제 Rain World atlas 로딩 | 미구현 | 임시 도형만 렌더 | 설치 경로에서만 읽는 loader 필요 |
| DMS 호환 | 검증기만 존재 | Node 도구가 로컬 template 구조 검증 | Rain World atlas loader를 대신하지 않음 |
| fixed simulation | 부분 구현 | Godot physics 60 Hz 설정 | Rain World 원본 tick은 아직 미확인 |
| render interpolation | 미구현 | physics state를 그대로 그림 | `last/current/render` 분리 없음 |
| Utility AI | 미구현 | AI 코드 없음 | 모든 필수 행동과 scheduler 필요 |
| `AI → VirtualInput → Movement` | 미구현 | 입력 추상화 없음 | 아키텍처 불변식으로 강제해야 함 |
| DesktopCollisionWorld | 미구현 | viewport 하단/좌우 clamp | OS window/work area/monitor surface가 아님 |
| 움직이는 창 표면 | 미구현 | Win32 호출 없음 | 위치 변경 event와 snapshot 필요 |
| 투명/borderless/topmost | 구현 | `project.godot:13-17` | 프로토타입 수준 충족 |
| 클릭 통과 | 부분 구현 | bounding rectangle polygon | 빈 투명 영역도 입력을 막으며 정밀 hit-test가 아님 |
| Alt+Tab/taskbar/no-activate | 미구현/미검증 | 관련 Win32 style 없음 | 명시적 window style 필요 |
| 다중 모니터/DPI | 미구현 | 900×600 고정 viewport | 전역 물리 좌표계와 per-monitor 렌더 필요 |
| F1 디버그 표시 | 미구현 | 임시 실루엣 외 debug overlay 없음 | 요구된 state/target/surface 표기 필요 |
| 장시간 성능 설계 | 부분 구현 | 작은 배열은 재사용 | 매 tick 임시 배열/polygon과 OS 호출 발생 |
| 화면 밖 recovery | 미구현 | local viewport x만 clamp | monitor topology 변경까지 처리해야 함 |

## 5. 구현된 부분의 정밀 평가

### 5.1 고정 tick과 감쇠

`project.godot:21-23`은 물리 tick을 60 Hz로 고정한다. `src/main.gd:82-84`는 초 단위 `delta`를 사용하고, 감쇠를 `pow(AIR_DAMPING, delta * 60)`으로 환산한다. 이는 가변 `delta`에서 단순히 프레임당 계수를 곱하는 것보다 낫고, “렌더 주사율에 따라 이동 속도가 바뀌지 않아야 한다”는 방향과 맞는다.

그러나 60 Hz와 `0.992`가 Rain World에서 확인된 값은 아니다. 렌더 snapshot/interpolation도 없으므로 120/144/240 Hz 화면에서 pose는 물리 tick 단위로만 갱신된다. 이식할 것은 **기준 tick 감쇠를 시간 기반 계수로 변환하는 개념**이지 현재 수치가 아니다.

### 5.2 두 점 몸통과 거리 제약

`src/main.gd:97-110`은 두 점 사이 오차를 계산해, 일반 상태에서는 절반씩 나누고 드래그 중에는 잡히지 않은 점에 전부 적용한다. 디버그 리그용으로 간결하고 “연결된 두 질점”을 시각화하는 데 유용하다.

하지만 다음 정보가 없다.

- 점별 radius, mass/inverse mass, last position, contact normal, surface id
- 연결 종류, 탄성/강성, 허용 압축/신장, 방향성
- 접촉 이후 velocity reconciliation
- locomotion force와 자세 상태

특히 update 순서는 거리 제약을 먼저 푼 뒤(`src/main.gd:54-56`) 경계를 강제로 clamp한다(`src/main.gd:58-59`). 경계 보정이 두 점 중 하나만 움직이면 바로 앞에서 맞춘 거리가 같은 tick의 최종 state에서 다시 깨진다.

더 큰 문제는 constraint projection이 `body_positions`만 바꾸고 `body_velocities`를 갱신하지 않는다는 점이다. 한 chunk가 바닥에 반복 충돌하는 동안 다른 chunk의 큰 하향 속도는 남아 있고, 다음 tick마다 위치 projection이 이를 눈에 보이지 않게 취소한다. 이는 안정적인 접지가 아니라 높은 내부 에너지가 감춰진 상태다.

코드와 동일한 60 Hz 상수·순서로 두 몸통 점을 60초 동안 자유낙하시킨 수식 재생 결과는 다음과 같다. 꼬리는 몸통에 반력을 주지 않으므로 이 몸통 검증에서 제외해도 결과가 같다.

| 측정값 | 결과 |
|---|---:|
| 목표 몸통 거리 | 34.0 px |
| 관측 최소 거리 | 16.6185 px |
| 최대 절대 오차 | 17.3815 px |
| 60초 평균 절대 오차 | 9.6072 px |
| 오차가 1 px를 넘은 tick | 3,243 / 3,600 |
| 관측 최대 chunk 속력 | 1,028.7 px/s |

이 결과는 Godot runtime 캡처가 아니라 GDScript 수식을 같은 순서로 재생한 정적 검증이다. 그럼에도 알고리즘 차원의 결함을 보여 주기에는 충분하다. 실제 이관 커널은 “한 tick 종료 후 connection 오차와 penetration이 허용 오차 이내”라는 불변식을 테스트로 강제해야 한다.

그 밖의 문제는 다음과 같다.

- 거리가 0.001보다 작으면 아무 복구 없이 반환한다. 두 점이 겹친 극단 상태에서 rest direction을 회복할 수 없다.
- floor 좌표는 chunk 중심 clamp인지 실제 표면인지 의미가 불분명하고 radius가 충돌 수식에 들어가지 않는다.
- 속도 상한이 없어 빠른 마우스 이동이 임의로 큰 던지기 속도를 만든다.
- 바닥 접촉/착지 event나 grounded 상태가 없다.

### 5.3 Verlet 꼬리

`src/main.gd:87-94`는 이전 위치와 현재 위치 차이로 관성을 만들고 `gravity * dt²`를 더한다. `src/main.gd:113-126`은 hips anchor에서 시작하는 순차 거리 projection을 8회 수행한다. Bezier 장식 대신 segment chain을 사용한다는 요구 방향과 맞으며, 독립 커널의 첫 debug rig로 재사용할 수 있는 가장 좋은 부분이다.

다만 현재 구현은 완성된 `TailSegment` 모델이 아니다.

- 모든 링크 길이가 같고 segment radius/mass가 없다.
- 바닥 보정이 constraint 반복 후 실행되어 링크 길이를 다시 깬다.
- 바닥 projection이 `tail_previous`에 반영되지 않아 다음 tick의 암묵적 속도에 보정량이 섞인다.
- 바닥만 있고 window top/side, screen edge, moving surface와 접촉하지 않는다.
- tail root를 tick 마지막에 hips로 다시 옮기지만 그 뒤 첫 링크를 재해결하지 않는다.
- `_reset_rig`에서 tail root는 hips보다 5 px 아래에 생성되고, 초기 링크 길이는 `sqrt(13²+2²) ≈ 13.153` px다. 첫 physics tick 전에는 anchor/길이 불변식이 성립하지 않는다.
- 렌더 taper는 8점/7링크에서 `index / 7`을 사용하지만 마지막 그려지는 index는 6이므로 설정한 3 px tip 폭에 도달하지 않는다.

동일 수식 재생에서 20초 동안 최종 렌더 state의 꼬리 링크 오차는 최대 약 11.68 px였다. 이 값도 collision 뒤 constraint를 재해결하지 않는 순서에서 기인한다.

### 5.4 드래그와 던지기

구현된 상호작용 흐름은 명료하다.

- 가까운 몸통 점을 선택한다(`src/main.gd:176-184`).
- 드래그 중 선택된 점을 마우스 쪽으로 수렴시키고 다른 점은 connection으로 따라오게 한다.
- 저역 통과된 마우스 속도를 릴리스 velocity로 사용한다.
- 드래그 중 작은 passthrough polygon을 해제해 릴리스 event를 받을 가능성을 높인다.

이 UX 개념은 재사용 가치가 있다. 구현은 다음 이유로 다시 작성해야 한다.

- `lerp(..., 0.35)`의 smoothing 계수는 tick에 종속되어 있고 물리 근거가 없다. C#에서는 `1 - exp(-lambda * dt)` 또는 timestamp 기반 필터가 적합하다.
- 마우스 속도는 잡고 있지 않을 때도 매 tick 계산한다.
- 릴리스 event가 physics tick 사이에 오면 마지막 cursor 구간이 샘플에 충분히 반영되지 않을 수 있다.
- mouse capture API를 쓰지 않아 포인터가 900×600 창 자체를 벗어난 경우 릴리스 보장이 없다.
- grab은 별도 constraint가 아니라 chunk 위치를 직접 덮어쓰므로, collision/connection/velocity reconciliation과 일관된 해법이 없다.

C#에서는 `GrabConstraint`와 timestamped sample ring buffer를 두고, `SetCapture/ReleaseCapture`로 입력을 보장하며, 릴리스 속도는 clamp와 이상치 제거 후 물리계에 전달해야 한다.

### 5.5 임시 렌더와 시선

몸통 두 점에서 up/right basis를 만들고, 머리와 귀, 레이어 순서, taper 꼬리를 즉시 모드 도형으로 그린다. 실제 자산 없이 rig 방향과 제약을 확인하는 debug renderer로는 충분히 가치가 있다.

최종 procedural graphics로 볼 수 없는 이유는 다음과 같다.

- 손/발은 고정 offset이며 `position`, `lastPosition`, `velocity`, `targetPosition`, `connectionPosition`, `limbLength`가 없다.
- 발은 y만 floor로 clamp되고 몸통과 함께 x가 움직이므로 foot planting이 없다.
- 머리는 몸통 축에 고정되고 movement/landing/jump 반응이 없다.
- 눈은 매 draw마다 마우스를 즉시 바라보며 attention target과 smoothing이 없다.
- body stretch/compression은 connection이 일정 길이를 강제하므로 pose parameter로 표현되지 않는다.
- atlas element, anchor, scale, rotation, sprite ordering 규칙이 없다.

최종 renderer에 직접 옮기기보다 동일한 선/원 디버그 표현을 `DebugRigRenderer`로 이식해 물리, limb target, AI state를 검증하는 용도로 남기는 것이 좋다.

### 5.6 투명 overlay와 click-through

`project.godot:13-19`는 투명, topmost, borderless, non-resizable 900×600 창을 만든다. 배경 clear color와 viewport transparency도 설정되어 있다. 이는 최소 투명 오버레이 spike로는 성공이다.

그러나 `_update_mouse_passthrough`는 몸통과 꼬리 전체의 axis-aligned bounding rectangle에 큰 margin을 더한 한 개 사각형을 매 physics tick OS에 보낸다. 이 사각형의 투명한 모서리와 몸통 사이 빈 공간도 desktop click을 가로챈다. 반면 실제 pick은 몸통 원 두 개만 허용하므로, head/tail/빈 영역에서 클릭을 막으면서 아무 상호작용도 하지 않는 상태가 된다.

또한 매 tick 다음 allocation/호출이 발생한다.

- `body_positions + tail_positions` 임시 배열
- 네 점 `PackedVector2Array`
- passthrough OS 호출
- `_draw_limbs`의 중첩 배열 네 개
- head/ear polygon 배열

현재 작은 데모에서는 치명적이지 않지만 장시간 실행 앱의 기준에는 맞지 않는다. Win32 이관에서는 `WM_NCHITTEST`와 최신 pose의 capsule/circle hit-test를 사용하고, drag 시작 시 `SetCapture`를 호출하는 편이 정확하고 allocation도 없다.

## 6. 결함 우선순위

### P0 — 이관 전에 반드시 해결

1. **원본 근거 부재**: 현재 모든 움직임 상수와 상태가 Rain World 분석 없이 만들어졌다. `RainWorldBehaviorMap.md`와 provenance manifest 없이는 값을 C#에 복사하지 않는다.
2. **물리 불변식 파괴**: constraint 후 collision clamp, position/velocity 불일치, tail history 미보정으로 안정적인 접지와 거리 보장이 없다.
3. **목표 runtime 불일치**: Godot 4.7 runtime 의존 구조는 .NET Framework 4.8 독립 Win32 앱 목표와 맞지 않는다.
4. **Simulation/API 경계 부재**: 단일 `Node2D`에 OS 입력, 물리, pose, draw가 섞여 headless deterministic test가 불가능하다.

### P1 — 기능 단계에서 해결

1. DesktopCollisionWorld, monitor/work area/window surface가 없다.
2. movement state와 `VirtualInput`이 없어 AI가 연결될 지점이 없다.
3. procedural limbs, foot planting, attention, breathing, animation/body mode가 없다.
4. render interpolation과 immutable snapshot이 없다.
5. passthrough 사각형이 투명 영역 click을 차단한다.
6. 다중 모니터, DPI, no-activate, Alt+Tab/taskbar 제외, offscreen recovery가 없다.
7. local Rain World atlas loader가 없다. DMS validator는 대체물이 아니다.

### P2 — 품질과 유지보수

1. tail 초기 anchor/링크 길이가 첫 tick 전 불일치한다.
2. tail taper가 설정 tip 폭에 도달하지 않는다.
3. mouse velocity 필터가 tick 의존이며 sample의 신선도를 보장하지 않는다.
4. per-tick 임시 배열과 OS passthrough 갱신이 있다.
5. debug display toggle과 telemetry가 없다.

## 7. 현재 테스트 평가

`tests/slugcat_smoke_test.gd`가 확인하는 것은 다음뿐이다.

- scene resource load
- 두 몸통 위치와 두 velocity 원소
- 꼬리 위치/이력 배열 개수
- reset 직후 몸통 거리

`await process_frame`만 사용하고 `physics_frame`을 기다리거나 `_physics_process`를 명시적으로 step하지 않는다. 따라서 테스트 이름과 달리 물리 동작 smoke test가 아니라 **초기화 shape test**다.

| 위험 | 현재 검증 여부 |
|---|---|
| 한 tick 뒤 몸통 거리 | 없음 |
| 바닥 접촉 뒤 몸통 거리 | 없음 |
| tail root/각 링크 길이 | 없음 |
| 장시간 안정/finite/속도 상한 | 없음 |
| drag/release/mouse capture | 없음 |
| fixed tick과 render rate 독립성 | 없음 |
| transparency/click-through | 없음 |
| window/monitor/DPI | 없음 |
| AI/VirtualInput | 해당 구현 자체가 없음 |
| atlas load/render transform | 해당 구현 자체가 없음 |

추가로 `package.json`의 기본 `npm test`는 DMS fixture validator만 실행한다. Godot smoke test와 연결되어 있지 않고 `.github/workflows`도 없다. 로컬 fixture가 의도적으로 gitignore되어 있으므로 깨끗한 checkout의 기본 `npm test`는 별도 fixture 준비 없이는 실패한다.

현재 구현에서 balance나 행동 다양성을 판단할 telemetry는 없다. 점수나 목적 함수도 없는 데스크톱 펫에 임의의 `exploratory_ratio`를 만드는 것은 적절하지 않다. 먼저 deterministic scenario harness와 행동/접촉 telemetry를 추가하고, 이후 명시적 평가 목표가 생길 때만 동일 seed·동일 budget의 정책 비교를 도입해야 한다.

## 8. 재사용할 것과 버릴 것

| 항목 | 재사용 수준 | 이관 시 조건 |
|---|---|---|
| 두 질점 + 연결이라는 debug 표현 | 개념 재사용 | 실제 `BodyChunk`/`BodyChunkConnection` 관찰 결과로 필드와 순서 재정의 |
| 거리 오차를 양 끝에 분배하는 projection | 알고리즘 골격 재사용 | inverse mass 가중치, kinematic anchor, collision 반복, velocity reconciliation 추가 |
| reference-tick 감쇠를 `pow`로 환산 | 패턴 재사용 | 원본 tick/계수 확인 후 이름 있는 config로 저장 |
| Verlet tail integration | 알고리즘 골격 재사용 | segment별 radius/length, surface collision, history 보정, render history 분리 |
| 몸통 축에서 head/right basis 계산 | pose helper로 재사용 | 원본 `PlayerGraphics` offset/anchor 규칙으로 교체 |
| debug 선/원 실루엣과 layer 순서 | 적극 재사용 | F1 debug renderer로 분리, 최종 sprite renderer와 병행 |
| 가까운 chunk 선택 | 재사용 | pose-aware capsule/circle hit-test와 global desktop 좌표계 적용 |
| drag 동안 입력 영역 확장 | UX 개념 재사용 | Win32 mouse capture와 정확한 hit-test로 구현 |
| PNG IHDR 및 atlas frame bounds 검증 | 검증 패턴 재사용 | C# asset validation 계층으로 포팅 가능; Rain World atlas schema는 별도 분석 |

다음은 이관하지 않는다.

- `GRAVITY`, `AIR_DAMPING`, `BODY_DISTANCE`, 충돌 반발/마찰, tail 길이/개수 등 provenance 없는 현재 수치
- constraint 후 collision을 한 번만 수행하는 update 순서
- 위치 projection과 velocity/history를 서로 무관하게 두는 방식
- viewport rectangle을 desktop terrain으로 간주하는 방식
- `main.gd` 단일 클래스 책임 구조
- 고정 offset limbs와 즉시 snap 시선
- DMS template를 Rain World 원본 atlas의 대체 소스로 간주하는 설계

## 9. 권장 .NET Framework 4.8 구조

핵심 원칙은 **Core가 Win32, GPU, Mono.Cecil, 파일 시스템을 전혀 몰라야 한다**는 것이다. 이 조건이 headless deterministic test와 장기 유지보수를 가능하게 한다.

```text
RainWorldDesktopPet.sln
├─ src/
│  ├─ RainWorldDesktopPet.Core/          # net48, 순수 deterministic domain
│  │  ├─ Timing/
│  │  ├─ Physics/
│  │  ├─ Creature/
│  │  ├─ AI/
│  │  ├─ Pose/
│  │  └─ Telemetry/
│  ├─ RainWorldDesktopPet.Analysis/      # locator, Mono.Cecil 분석, manifest 생성
│  ├─ RainWorldDesktopPet.Assets/        # 설치 자산/atlas/texture 로딩과 검증
│  ├─ RainWorldDesktopPet.Desktop/       # monitor/window/mouse/WinEvent snapshot
│  ├─ RainWorldDesktopPet.Rendering/     # renderer interface, Direct2D/DirectComposition
│  └─ RainWorldDesktopPet.App/           # WinExe, composition root, settings, crash guard
├─ tests/
│  ├─ Core.Tests/
│  ├─ Analysis.Tests/
│  ├─ Assets.Tests/
│  ├─ Desktop.Tests/
│  ├─ Rendering.Tests/
│  └─ Integration.Tests/
└─ docs/
   ├─ RainWorldBehaviorMap.md
   └─ analysis/
```

권장 의존 방향은 다음과 같다.

```text
RainWorld DLL ──> Analysis ──> BehaviorManifest/config ──> Core
RainWorld assets ────────────> Assets ───────────────┐
Windows APIs ────────────────> DesktopSnapshot ──┐   │
Mouse/drag ──────────────────> Interaction ──────┤   │
AI observation ─> Utility scheduler ─> VirtualInput │
                                              ↓  │   │
                                   Movement + Physics
                                              ↓
                                 Previous/Current Snapshot
                                              ↓
                                  Pure SlugcatPoseBuilder
                                              ↓
                         Rendering + Assets + Overlay Window
```

`Analysis`는 원본 DLL을 런타임 게임 엔진처럼 호출하지 않는다. class/method/field/constant/IL/call relationship을 조사하고, 게임 버전과 DLL hash가 붙은 관찰 manifest를 만든다. 독립 구현은 그 관찰을 사람이 검토해 작성한 Core 코드로 실행되어야 한다. 자산은 사용자 설치 경로에서 읽되 저장소나 배포물에 복제하지 않는다.

## 10. 순수 시뮬레이션 계약

### 10.1 최소 public API

```csharp
public interface ISlugcatSimulation
{
    void Reset(SimulationScenario scenario, ulong seed);
    void Step(VirtualInput input, DesktopCollisionSnapshot world, float fixedDt);
    SimulationSnapshot CaptureSnapshot();
    void DrainEvents(ICollection<SimulationEvent> destination);
}
```

`Step`은 OS clock, global mouse, renderer, 파일 시스템에 접근하지 않는다. 같은 초기 state, seed, input sequence, surface snapshot sequence에는 bitwise 또는 명시된 tolerance 내에서 같은 결과를 내야 한다.

### 10.2 상태 모델

```text
SlugcatState
├─ BodyChunk[2]
│  ├─ Position
│  ├─ Velocity
│  ├─ Radius / InverseMass
│  ├─ ContactNormal / SurfaceId
│  └─ LastTickPosition
├─ BodyChunkConnection[]
│  ├─ A / B / RestLength
│  └─ 원본에서 확인한 type/elasticity 규칙
├─ TailSegment[]
│  ├─ Position
│  ├─ IntegrationPreviousPosition
│  ├─ Radius / ConnectionLength
│  └─ Contact state
├─ MovementState
├─ AnimationIndex / BodyMode
├─ LimbState[]
├─ AttentionState
└─ AIState
```

시뮬레이션의 이전 위치와 렌더 보간용 이전 snapshot을 혼동하지 않는다. 특히 Verlet의 `IntegrationPreviousPosition`은 속도 표현이고, `PreviousTickSnapshot`은 렌더 interpolation 데이터다.

### 10.3 fixed-step loop

원본 Rain World의 실제 logic tick을 DLL에서 확인하기 전까지 60 Hz를 정답으로 확정하지 않는다.

```text
accumulator += clampedWallDelta
while accumulator >= fixedDt and steps < maxCatchUpSteps:
    previousSnapshot = currentSnapshot
    simulation.Step(inputForTick, worldSnapshot, fixedDt)
    currentSnapshot = simulation.CaptureSnapshot()
    accumulator -= fixedDt

alpha = accumulator / fixedDt
renderSnapshot = Interpolate(previousSnapshot, currentSnapshot, alpha)
```

과도한 catch-up이 발생하면 무제한 step으로 UI를 멈추지 말고 drop/slowdown telemetry를 남긴다. 60, 144, 240 Hz render schedule 및 불규칙 wall-delta schedule이 같은 tick 수 뒤 같은 simulation state를 만드는지 자동 검증한다.

### 10.4 권장 physics step 순서

최종 순서는 반드시 DLL 관찰을 우선한다. 원본 확인 전의 안전한 독립 solver 골격은 다음과 같다.

1. tick 시작 snapshot 저장
2. movement state가 `VirtualInput`을 force/impulse/constraint target으로 변환
3. body/tail 자유 적분
4. body connection과 terrain contact를 같은 반복 solver 안에서 해결
5. tail anchor, tail connection, tail terrain contact를 반복 해결
6. 마지막 contact pass와 anchor pass로 penetration/anchor 불변식 보장
7. corrected displacement와 충돌 계수로 velocity/Verlet history reconciliation
8. grounded/landing/wall/edge event와 movement/animation state 갱신
9. finite, constraint error, penetration, speed guard 검사

중요한 점은 collision과 connection을 완전히 분리된 단발 pass로 두지 않는 것이다. 한 pass의 projection이 다른 불변식을 깨므로, 함께 수렴시키거나 원본에서 관찰된 정확한 교정 순서를 재현해야 한다.

질량 가중 거리 projection은 다음 골격으로 일반화할 수 있다.

```text
C = |pB - pA| - restLength
n = normalize(pB - pA)
lambda = C / (inverseMassA + inverseMassB)
pA += n * lambda * inverseMassA
pB -= n * lambda * inverseMassB
```

잡힌 chunk는 해당 constraint에서 inverse mass를 0으로 취급할 수 있다. 두 점이 겹쳤을 때는 이전 유효 방향, movement facing, 또는 deterministic fallback axis를 써서 회복해야 한다.

### 10.5 tick 종료 불변식

각 `Step` 뒤 debug build와 test에서 다음을 검사한다.

- 모든 position/velocity가 finite다.
- 모든 body connection 오차가 정한 epsilon 이하다.
- tail root가 hips anchor와 epsilon 이내다.
- 모든 tail link 오차가 epsilon 이하다.
- chunk/segment penetration이 epsilon 이하다.
- contact normal은 정규화되어 있고 surface id가 snapshot에 존재한다.
- 속도가 설정된 안전 상한을 넘지 않는다.
- AI step은 position/velocity를 직접 변경하지 않는다.

epsilon은 좌표 단위와 원본 solver 정밀도를 관찰한 후 결정한다. 현재처럼 목표 길이의 절반에 가까운 오차는 허용할 수 없다.

## 11. DesktopCollisionWorld 설계

시뮬레이션 좌표는 **가상 데스크톱의 물리 pixel 좌표**로 하나만 정의하고, 앱 시작 전에 Per-Monitor DPI Awareness V2를 설정한다. Win32 API별 logical/physical coordinate virtualization이 섞이지 않도록 모든 adapter 경계에서 변환을 명시한다.

`DesktopCollisionSnapshot`은 immutable하고 다음을 포함한다.

```text
DesktopCollisionSnapshot
├─ Sequence / Timestamp
├─ Monitor[]
│  ├─ MonitorBounds
│  ├─ WorkArea
│  └─ Dpi
├─ Surface[]
│  ├─ StableId
│  ├─ Segment / Normal
│  ├─ SurfaceVelocity
│  ├─ Kind (taskbar, window-top, window-side, screen-edge)
│  └─ Enabled flags
└─ SafeRecoveryRegions[]
```

Desktop adapter는 다음 원칙을 따른다.

- `EnumWindows`, `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)`, `GetMonitorInfo`를 wrapper 뒤에 둔다.
- 보이지 않음, 최소화, cloaked, zero-area, 자체 overlay window를 제외한다.
- OS enumeration 순서가 simulation determinism에 영향을 주지 않도록 stable id로 정렬한다.
- `SetWinEventHook` location-change event로 dirty flag를 만들고, 제한된 주기의 reconciliation poll을 병행한다.
- OS callback thread에서 simulation state를 바꾸지 않고 새 snapshot을 publish한다.
- 움직이는 창은 이전/현재 rect와 timestamp로 surface velocity를 계산하되 teleport threshold를 둔다.
- topology 변경이나 창 삭제로 현재 support가 사라지면 낙하 또는 가까운 safe surface recovery를 명시적으로 수행한다.

창 위쪽은 one-way support surface로 시작하고, 좌우 벽/edge grab은 해당 behavior가 구현될 때 opt-in하는 편이 안전하다. 모든 window rectangle을 즉시 완전한 solid box로 만들면 창이 겹치거나 이동할 때 캐릭터가 갇힐 수 있다.

## 12. Movement와 Utility AI

### 12.1 강제할 계층

```text
WorldObservation + InternalNeeds
              ↓
      DesktopPetAI / Scheduler
              ↓
          VirtualInput
              ↓
        SlugcatMovement
              ↓
      BodyChunk force/impulse
```

`DesktopPetAI`에는 physics state를 쓰는 API를 제공하지 않는다. `Evaluate`와 `Tick`은 `VirtualInput`과 attention request만 반환한다. 이 구조로 “AI가 transform/position을 직접 이동”하는 금지사항을 컴파일 경계에서 막는다.

### 12.2 관찰과 입력 schema

```text
VirtualInput
├─ MoveX: -1..1
├─ MoveY: -1..1
├─ JumpPressed / JumpHeld
├─ GrabPressed / GrabHeld
├─ DropPressed
└─ LookTarget (optional request, not direct head position)

WorldObservation
├─ body/contact/movement state
├─ visible walkable surfaces and edges
├─ mouse position/velocity/distance
├─ current and recently changed windows
├─ fall risk / estimated landing surface
├─ fatigue / curiosity / recent behavior
└─ elapsed time in current behavior
```

### 12.3 scheduler

각 behavior는 `Score(observation)`과 `ProduceInput(observation, state)`를 가진다. 매 physics tick 행동을 재선택하지 않고 별도 decision cadence를 사용한다.

- current behavior inertia bonus
- minimum duration
- switch threshold/hysteresis
- behavior별 cooldown
- emergency override (유효 support 소실, 화면 밖 위험 등)
- decision 시점에만 seeded variation sampling
- 동점은 stable behavior id와 seeded PRNG로 결정

필수 행동은 처음부터 모두 빈 껍데기로 추가하지 말고 locomotion capability와 함께 vertical slice로 구현한다.

1. `Idle`, `LookAround`, `Walk`, `Explore`
2. `Sit`, `Sleep`, `ObserveWindow`, `FollowMouse`, `AvoidMouse`
3. `Jump`, `DropDown`, `BalanceNearEdge`
4. `ClimbWindow`

`FollowMouse`와 `AvoidMouse`도 좌표를 직접 변경하지 않고 이동 방향/점프의 가상 입력만 만든다. `BalanceNearEdge`는 visual pose만 바꾸는 행동이 아니라 낙하 위험, braking distance, support polygon을 사용해 실제 입력을 조절해야 한다.

### 12.4 AI telemetry

점수 없는 desktop pet에서는 다음을 기록한다.

- seed, tick, observation id
- 모든 behavior utility와 선택 이유
- behavior enter/exit, 체류 시간, hysteresis/cooldown 상태
- 생성된 `VirtualInput`
- contact/landing/fall/recovery event
- 현재 surface와 edge 거리
- attention target과 변경 이유
- offscreen 또는 stuck detector 발생

이 telemetry로 “랜덤 타이머만 도는가”, “행동 하나가 항상 지배하는가”, “위험에서 회복하는가”를 판단한다. 행동 다양성 수치만 높이려고 숨은 randomness를 늘리는 방식은 금지한다.

## 13. Procedural pose와 렌더 구조

### 13.1 순수 pose builder

`SlugcatPoseBuilder`는 GPU나 OS를 모르고 interpolated simulation snapshot과 asset metrics를 받아 sprite transform을 반환한다.

```text
SlugcatPose
├─ Body/Hips/Head sprite transforms
├─ Arm/Hand transforms and debug targets
├─ Leg/Foot transforms and plant state
├─ Face/look transform
├─ Tail polyline/segment transforms
├─ Layer order
└─ Interaction hit shapes
```

다음은 mechanics와 presentation을 분리한다.

- body chunk와 terrain contact가 authoritative하다.
- landing compression, jump anticipation, head lag, breathing은 pose parameter다.
- render-only squash/rotation은 collision shape를 바꾸지 않는다.
- foot target/plant는 simulation 또는 pose state로 명시하고, body 이동 때 매 frame 즉시 재계산해 미끄러지게 하지 않는다.
- 눈은 `AttentionSystem`의 smoothed target을 사용한다.
- tail은 실제 simulated segment를 그리며 장식용 Bezier 하나로 대체하지 않는다.

이 프로젝트의 표현 register는 장난스럽지만 효과가 많은 arcade 캐릭터가 아니라, 물리적 secondary motion이 중심인 절제된 생물형이다. 따라서 generic particle, camera shake, hit stop보다 landing compression, 방향 전환 시 질량 지연, head/eye attention, foot plant, tail follow-through를 우선한다.

### 13.2 sprite/atlas 경계

`RainWorldAssetLoader`는 사용자 설치 경로에서 atlas texture와 metadata를 읽고 다음 정보를 정규화한다.

- source game version과 asset file hash
- element name/index
- source rectangle, trim/original size
- pivot/anchor
- scale/flip/rotation 규칙
- layer order

parser와 renderer 사이에는 `AtlasCatalog` DTO를 두고 synthetic atlas fixture로 테스트한다. 원본 PNG나 추출 sprite는 저장소 test fixture/golden image에 포함하지 않는다.

### 13.3 Windows renderer/overlay

.NET Framework 4.8에서는 WPF `AllowsTransparency` 창에 전체 설계를 묶지 않는다. 먼저 다음 interface를 고정한 뒤 Direct2D/DXGI/DirectComposition 기반 구현의 net48 호환성과 alpha composition을 작은 spike로 검증한다.

```csharp
public interface IOverlayRenderer
{
    void Resize(RenderTargetDescriptor target);
    void Render(SlugcatPose pose, DebugOverlay debug);
    void Present();
}
```

overlay window는 Win32 style과 message 처리를 명시적으로 소유한다.

- borderless topmost popup
- tool window로 taskbar/Alt+Tab 노출 최소화
- no-activate 및 `WM_MOUSEACTIVATE` 처리
- premultiplied-alpha 투명 composition
- 캐릭터 밖 `WM_NCHITTEST → HTTRANSPARENT`
- 캐릭터 hit 시에만 client input, drag 동안 `SetCapture`
- DPI/monitor 변경 message 처리
- 자체 HWND를 DesktopCollisionWorld 열거에서 제외

하나의 거대한 가상 화면 window와 monitor별 window 중 선택은 renderer spike에서 측정한다. 물리는 어느 경우에도 하나의 global coordinate system을 유지해야 한다.

## 14. 테스트 가능한 구조와 필수 시나리오

### 14.1 deterministic harness

Core test harness는 renderer 없이 다음 계약을 가진다.

```text
Reset(scenario, seed)
Step(public VirtualInput, fixedDt, DesktopCollisionSnapshot)
GetSnapshot()
DrainEvents()
```

공개 입력 schema, AI가 볼 수 있는 observation schema, seed, fixed dt, 최대 tick을 각 test report에 기록한다. renderer frame rate나 wall clock을 정책 입력으로 사용하지 않는다.

### 14.2 물리 단위/속성 테스트

| 시나리오 | 검증 내용 |
|---|---|
| 초기화 직후 | 두 body와 모든 tail link/anchor가 epsilon 내 rest length |
| 무중력 자유 운동 | 외력이 없을 때 예상 속도/위치, 불필요한 에너지 증가 없음 |
| 중력 자유낙하 | 정해진 적분식의 analytic/recorded trace와 일치 |
| 평평한 바닥에 60초 방치 | finite, no penetration, connection 오차, 속도 상한, 안정적인 contact |
| 60/144/240 Hz render schedule | 동일 tick 뒤 simulation snapshot 동일 |
| 불규칙 wall delta | accumulator 결과가 같은 fixed-step 입력열과 동일 |
| 한 chunk 바닥/다른 chunk 공중 | connection/contact가 함께 수렴 |
| corner와 좁은 window top | deterministic contact ordering, tunneling 없음 |
| 움직이는 창 | surface relative velocity 적용, 순간이동/폭발 없음 |
| tail 바닥/edge 접촉 | 모든 link, anchor, penetration 불변식 유지 |
| 극단 drag/throw | velocity clamp, NaN 없음, release 후 회복 |
| body 두 점 겹침 | deterministic fallback 방향으로 rest length 회복 |
| monitor 제거/해상도 변경 | 지정된 safe region으로 유한 시간 내 recovery |

현재 프로토타입의 “바닥 자유낙하 60초” 수식 재생은 새 커널에서 반드시 통과해야 할 실패 회귀 fixture로 남길 가치가 있다.

### 14.3 AI 테스트

| 시나리오 | 검증 내용 |
|---|---|
| 같은 seed/관찰열 | behavior 선택과 `VirtualInput` 완전 재현 |
| utility 동률 | stable tie-break와 seeded variation |
| 최소 행동 시간 중 작은 점수 역전 | 불필요한 thrashing 없음 |
| 명백한 낙하 위험 | emergency override가 허용 시간 내 작동 |
| 마우스 접근/이탈 | Follow/Avoid score의 단조성과 hysteresis |
| 새 window 등장 | Inspect/Observe가 cooldown과 시야 조건을 지킴 |
| 장시간 idle | breathing/look는 있으나 position 직접 변경 없음 |
| AI mutation guard | AI 호출 전후 physics state 동일, output은 VirtualInput뿐 |
| 행동별 scripted surface | 모든 필수 behavior가 도달 가능하며 stuck loop 없음 |

행동 다양성을 판단하려면 단일 실행 결과가 아니라 동일 seed set과 동일 tick budget의 여러 정책/AI config를 비교한다. 단, 명시적 게임 점수가 없는 현재 목표에는 인위적 score를 도입하지 않는다.

### 14.4 pose/render 테스트

- interpolation `alpha=0/1`이 정확히 previous/current pose endpoint다.
- body sprite rotation과 head basis가 zero-length/flip에서 finite다.
- foot plant 중 support surface가 유지되면 foot world position drift가 tolerance 이하다.
- landing squash와 head lag가 정해진 시간 내 rest pose로 복귀한다.
- render-only deformation이 physics snapshot을 변경하지 않는다.
- atlas source rectangle이 PNG bounds 안에 있고 missing/corrupt asset은 진단 가능한 오류를 낸다.
- synthetic atlas golden image로 layer, pivot, flip, alpha composition을 검증한다.
- debug overlay on/off가 simulation 결과를 바꾸지 않는다.

### 14.5 Desktop/통합 테스트

- fake `IWindowEnumerator`로 visible/minimized/cloaked/self-window filter를 검증한다.
- 서로 다른 DPI의 monitor snapshot에서 global↔local 좌표 왕복 오차를 검증한다.
- `WM_NCHITTEST`가 body/head/limb/tail hit shape에서만 client를 반환한다.
- drag 시작/종료 시 capture가 정확히 설정/해제되고 예외 경로에서도 복구된다.
- no-activate/tool-window/topmost style을 HWND integration test에서 확인한다.
- 8시간 soak test에서 allocation rate, handle/GPU resource 수, working set, dropped simulation tick을 기록한다.

## 15. 이관 단계와 완료 gate

### Phase 0 — 원본 증거 확보

- Rain World 설치와 version/hash 탐색
- 필수 클래스와 주변 호출 관계 분석
- logic tick, integration/collision/connection 순서 확인
- `PlayerGraphics`, `Limb`, `TailSegment` pose/physics 관찰
- `docs/RainWorldBehaviorMap.md` 작성

**Gate:** provenance 없는 현재 Godot 상수를 production Core에 복사하지 않는다.

### Phase 1 — 순수 C# debug physics

- fixed-step harness
- 두 BodyChunk와 connection
- flat synthetic surface
- gravity, landing, walk, jump의 최소 movement
- line/circle debug renderer

**Gate:** 60초 안정성, render-rate 독립성, 모든 tick 종료 불변식 테스트 통과.

### Phase 2 — DesktopCollisionWorld

- monitor/work area/taskbar/window top snapshot
- moving/removed window 처리
- DPI/global coordinate 및 recovery

**Gate:** fake OS tests와 실제 다중 monitor smoke test 통과.

### Phase 3 — procedural graphics와 자산

- local Rain World atlas catalog
- body/head/hips
- arms/legs/foot plant
- attention/breathing
- TailSegment 기반 렌더
- interpolation

**Gate:** 원본 관찰 trace/스크린 비교, pose invariant, synthetic atlas test 통과.

### Phase 4 — Utility AI

- behavior scheduler와 seeded PRNG
- 필수 행동 vertical slice
- `VirtualInput` 전용 출력
- telemetry와 stuck/recovery

**Gate:** deterministic scenario suite와 AI mutation guard 통과.

### Phase 5 — Win32 GPU overlay와 장시간 품질

- alpha composition, no-activate, click-through/capture
- monitor별 DPI와 topology change
- F1 debug overlay
- crash isolation, settings, asset error UX
- soak/performance 측정

**Gate:** 캐릭터 밖 click 통과, Alt+Tab/taskbar 방해 최소화, 8시간 resource 안정성 확인.

## 16. 구현 전 확정해야 할 관찰값

다음 질문은 Godot 값이나 추측으로 답하지 않는다.

1. Rain World의 실제 logic update 주기와 `timeStacker` 계산/사용 위치는 무엇인가?
2. `Player`의 두 BodyChunk 수, radius, mass, connection type/length와 update 순서는 무엇인가?
3. terrain collision 전후 velocity, friction, bounce, connection correction 순서는 무엇인가?
4. walk/jump/crawl/stand/slide/wall 상태는 어떤 `BodyModeIndex`/`AnimationIndex`와 입력 조건으로 전환되는가?
5. `PlayerGraphics`가 body/head/hips/limb target을 계산할 때 사용하는 원본 상태와 상수는 무엇인가?
6. `TailSegment` 수, radius/length 변화, gravity/air friction, terrain 접촉, anchor 처리 순서는 무엇인가?
7. face/eye/look/breath/sleep/blink의 smoothing과 counter는 무엇인가?
8. atlas element 선택, anchor, scale, flip, layer order는 어떻게 결정되는가?

각 답은 DLL hash, class, method, field/constant, 관찰된 수식, 독립 구현 차이를 `RainWorldBehaviorMap.md`에 남겨야 한다.

## 17. 최종 권고

현재 Godot 프로토타입은 폐기할 대상이 아니라 **세 가지 제한된 역할**로 보존한다.

1. 투명 overlay와 drag UX를 빠르게 설명하는 시각 reference
2. 두 chunk와 segment tail을 보여 주는 debug rig reference
3. collision/constraint 순서 결함을 재현하는 regression scenario

반면 production C# 구현의 출발점은 이 파일의 번역본이 아니라, `RainWorldBehaviorMap.md`로 추적 가능한 순수 시뮬레이션 계약이어야 한다. 특히 BodyChunk 물리, PlayerGraphics pose, TailSegment chain, Utility AI→VirtualInput 네 축은 서로 다른 계층과 테스트를 가져야 하며, 렌더/Win32 adapter가 이 state를 수정하지 못하도록 의존 방향을 고정해야 한다.
