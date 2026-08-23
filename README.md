# Slugcat in My Monitor

로컬 Rain World 설치본의 **원작 플레이어 atlas**를 읽어 Windows 바탕화면에서 독립 실행되는 Shimeji 스타일 Slugcat 펫입니다. Rain World `v1.11.8`의 `Assembly-CSharp.dll`을 정적 분석해 확인한 40 Hz 루프, 두 `BodyChunk`, 거리 제약, `TailSegment`, `Limb`, `PlayerGraphics` 구조를 데스크톱 환경에 맞게 재구현합니다.

RainWorld.exe, Steam 게임 프로세스, Unity Player, BepInEx를 실행하거나 런타임 의존성으로 사용하지 않습니다. 게임 자산도 저장소나 빌드 산출물에 복사하지 않고, 실행 중 사용자 PC의 설치 폴더에서만 읽습니다.

## 현재 구현

- 40 Hz 고정 시뮬레이션과 가변 렌더링 보간(`lastPosition → position`)
- 원작과 같은 반지름 9/8의 두 몸통 chunk, 17 px connection, 질량·탄성·대칭값
- 중력, 공기 저항, 바닥 마찰, 낙하, 착지 압축, 점프 준비, 벽 오르기, 자세 안정화
- 원작 기본형과 같은 4개 segment의 절차적 꼬리, 보간된 stretched radius, 15-vertex/13-triangle tail mesh
- 원작 `GenericBodyPart` 머리/단일 legs particle과 `SlugcatHand` mode·retract·20 px constraint
- `DesktopPetAI → VirtualInput → SlugcatMovement` 계층과 13개 utility 행동
- 모니터 work area, 작업 표시줄, 실제 top-level 창의 윗면/옆면을 충돌 지형으로 사용
- 이동하는 창 surface 추적, 멀티 모니터 및 화면 밖 recovery
- 전체 virtual desktop persistent DIB 기반 투명 layered overlay, 음수 모니터 좌표, click-through, tray/F1 디버그
- 물리는 유지하고 전체 캐릭터 렌더 좌표에만 적용하는 단일 2.20배 visual scale
- 마우스로 몸통을 잡아 끌고 놓아 던지는 상호작용

## 원작 Slugcat

DMS 스킨은 현재 자동 탐색하거나 적용하지 않습니다. 먼저 아래 원작 캐릭터를 동일한 기본 `rainWorld` atlas와 DLL에서 확인한 색/체형 값으로 렌더링합니다.

| 실행 이름 | 캐릭터 | 원작 내부 이름 | 색 |
|---|---|---|---|
| `white` | Survivor | `White` | `#FFFFFF` |
| `yellow` | Monk | `Yellow` | `#FFFF73` |
| `red` | Hunter | `Red` | `#FF7373` |
| `gourmand` | Gourmand | `Gourmand` | `#F0C197` |

Gourmand는 원작 `PlayerGraphics.DrawSprites`처럼 body의 X scale을 1.4, hips의 X scale을 1.6으로 적용합니다. Monk/Hunter/Gourmand의 몸무게와 Hunter의 달리기 계수도 `SlugcatStats`에서 확인한 값을 사용합니다.

## 빌드와 실행

요구 사항:

- Windows 10/11
- .NET Framework 4.8 runtime
- PowerShell 5.1 이상
- 로컬 Rain World 설치본

```powershell
# Release 빌드 + 테스트
.\build.ps1

# 기본 Survivor
.\artifacts\Release\RainWorldDesktopPet.exe

# 원작 캐릭터 선택과 디버그 표시
.\artifacts\Release\RainWorldDesktopPet.exe --slugcat gourmand --debug

# 자동 탐색이 실패할 때 설치 경로 지정
.\artifacts\Release\RainWorldDesktopPet.exe `
  --rain-world "C:\Program Files (x86)\Steam\steamapps\common\Rain World"
```

빌드 스크립트는 필요한 경우 Microsoft의 .NET Framework 4.8 reference-assembly NuGet 패키지를 `.tools/`에 내려받습니다. 실행 프로그램 자체에는 외부 런타임 패키지가 없습니다.

### 조작

- Slugcat 위에서 마우스 왼쪽 버튼: 잡기
- 잡은 채 이동 후 놓기: 던지기
- `F1`: physics/AI/procedural graphics 디버그 표시
- tray 메뉴: 원작 Slugcat 변경, 일시 정지, 종료

## 로컬 자산 처리

앱은 Steam registry, `libraryfolders.vdf`, `appmanifest_312520.acf`, 일반 설치 경로 순으로 Rain World를 찾습니다. 못 찾으면 폴더 선택 창을 표시합니다.

원작 플레이어 atlas는 loose PNG가 아니라 보통 다음 Unity resource 안에 있습니다.

```text
Rain World/
└─ RainWorld_Data/
   ├─ resources.assets
   └─ resources.assets.resS
```

loader는 asset의 class/name과 Futile atlas descriptor를 찾아 texture와 frame metadata를 메모리에서 조립합니다. 분석 중 확인한 path ID나 byte offset을 런타임 상수로 사용하지 않습니다. 지원하지 않는 설치 버전에서는 원작 파일을 수정하거나 추출본을 남기지 않고 procedural fallback으로 동작합니다.

## 구조

```text
src/RainWorldDesktopPet/
├─ Core/       # 40 Hz loop, interpolation, constants
├─ RainWorld/  # install discovery, Unity resource/atlas loading
├─ Physics/    # BodyChunk, connection, tail, desktop collision
├─ Creature/   # Slugcat state/movement and VirtualInput
├─ Graphics/   # body parts, limbs, tail, pose, sprite renderer
├─ AI/         # utility selection and attention
├─ Desktop/    # Win32 window/monitor/mouse wrappers
└─ UI/         # transparent layered overlay and tray controls
```

전체 데이터 흐름은 [`docs/Architecture.md`](docs/Architecture.md), 원본과 독립 구현의 대응은 [`docs/RainWorldBehaviorMap.md`](docs/RainWorldBehaviorMap.md), 자산 조사 근거는 [`docs/analysis/AssetFindings.md`](docs/analysis/AssetFindings.md), DLL 조사 근거는 [`docs/analysis/DllFindings.md`](docs/analysis/DllFindings.md)에 기록합니다.

기존 Godot 프로토타입(`project.godot`, `scenes/`, `src/main.gd`)은 비교 자료로 보존되어 있으며 현재 네이티브 실행 프로그램의 진입점이 아닙니다.

## 테스트

```powershell
.\build.ps1 -Configuration Debug
```

테스트는 fixed-step, 원작 connection/Stand/jump 식, desktop collision, AI/physics 분리와 행동 도달성, DropDown, 휴식 frame, atlas metadata, 설치 경로 탐색을 검증합니다. 로컬 설치본이 있으면 embedded 원작 atlas가 DMS 없이 로드되는지도 검사합니다.

## 에셋 및 상표

이 저장소는 Rain World, Dress My Slugcat 또는 커뮤니티 스킨의 이미지와 게임 에셋을 배포하지 않습니다. 로컬 분석 도구와 추출 결과도 Git에서 제외됩니다. 자세한 내용은 [`THIRD_PARTY_TEST_ASSETS.md`](THIRD_PARTY_TEST_ASSETS.md)를 참고하세요.

이 프로젝트는 비공식 팬 프로젝트이며 Videocult 또는 Akupara Games와 제휴하거나 승인을 받은 프로젝트가 아닙니다. Rain World 및 관련 명칭과 자산의 권리는 각 권리자에게 있습니다.

프로젝트 코드는 [MIT License](LICENSE)로 배포됩니다. 제3자 자산에는 이 라이선스가 적용되지 않습니다.
