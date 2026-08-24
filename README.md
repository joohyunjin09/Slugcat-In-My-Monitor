# SlugcatInMyMonitor

로컬 Rain World 설치본의 **원작 플레이어 atlas**를 읽어 Windows 바탕화면에서 독립 실행되는 Shimeji 스타일 Slugcat 펫입니다. Rain World `v1.11.8`의 `Assembly-CSharp.dll`을 정적 분석해 확인한 40 Hz 루프, 두 `BodyChunk`, 거리 제약, `TailSegment`, `Limb`, `PlayerGraphics` 구조를 데스크톱 환경에 맞게 재구현합니다.

RainWorld.exe, Steam 게임 프로세스, Unity Player, BepInEx를 실행하거나 런타임 의존성으로 사용하지 않습니다. 게임 자산도 저장소나 빌드 산출물에 복사하지 않고, 실행 중 사용자 PC의 설치 폴더에서만 읽습니다.

## 현재 구현

- 40 Hz 고정 시뮬레이션과 DWM/모니터 주사율 렌더링 보간(`lastPosition → position`)
- 원작 단위 물리를 유지하고 Windows 좌표 경계에서 X/Y 모두 적용하는 `DesktopWorldScale=2.20`
- 원작과 같은 반지름 9/8의 두 몸통 chunk, 17 px connection, 질량·탄성·대칭값
- 중력, 공기 저항, 바닥 마찰, 낙하, 착지 압축, 점프 준비, 벽 오르기, 원작 body-mode 힘
- 원작 기본형과 같은 4개 `TailSegment` 물리점과 보간된 stretched radius, 모든 렌더 경로에서 하나로 이어지는 15-vertex/13-triangle tail mesh
- 원작 `GenericBodyPart` 머리/단일 legs particle과 `SlugcatHand` mode·retract·20 px constraint
- `DesktopPetAI → VirtualInput → SlugcatMovement` 계층과 13개 utility 행동
- 모니터별 floor·작업 표시줄 상단·노출된 좌우 경계와 실제 top-level 창의 윗면/옆면을 하나의 충돌 snapshot으로 사용
- 창 끝 낙하 시 아래 창 또는 monitor floor와 swept 충돌하며, 음수 좌표·엇갈린 멀티 모니터 경계를 연속 topology로 추적
- 원작 공중 수평 제어와 충돌 직전 방향 성분 기반 `TerrainImpact`, Survivor/Hunter/Monk `35/60` 및 Gourmand `40/80` severity 임계값
- lethal terrain severity를 최대 3초(`120` tick) 기절로 바꾸고 연속 충돌에도 최초 recovery deadline을 연장하지 않는 데스크톱 펫 안전 계층
- 원작 충돌→연결 제약 순서는 유지하되 제약이 만든 모니터 바닥/외곽 모서리 관통은 같은 terrain snapshot에서 즉시 접촉 재투영
- 원작 tick stun 감소, 기절 중 계속되는 BodyChunk·꼬리 물리, `FaceStunned`, 손 retract와 mouse attention 차단
- HWND 수명/누락 유예, 이동하는 창 surface 및 멀티 모니터 추적
- 전체 virtual desktop persistent DIB 기반 투명 layered overlay, 음수 모니터 좌표, click-through, 트레이 디버그 메뉴
- 창·커서 desktop pixel을 원작 simulation unit으로 변환하고 렌더/화면 이동에 일관되게 적용하는 2.20배 world scale
- 마우스로 몸통을 잡아 끌고 놓아 던지는 상호작용
- 머리에서 원작 90-unit 이내의 실제 좌/우/중 클릭에만 1.5초간 활성화되는 임시 마우스 시선

## 원작 Slugcat

DMS 스킨 에디터는 실행 파일과 같은 폴더 또는 그 주변의 `skins`, 개발 저장소의 `assets/skins`,
`%LOCALAPPDATA%\SlugcatInMyMonitor\skins`, Rain World의 로컬 mod 폴더에서
`metadata.json`과 파츠별 PNG/TXT 아틀라스 쌍을 검색합니다. 검색된 세트를 선택하면
원본 `rainWorld` 아틀라스의 해당 파츠 위에 실제 프레임을 오버라이드합니다.

| 실행 이름 | 캐릭터 | 원작 내부 이름 | 색 |
|---|---|---|---|
| `white` | Survivor | `White` | `#FFFFFF` |
| `yellow` | Monk | `Yellow` | `#FFFF73` |
| `red` | Hunter | `Red` | `#FF7373` |
| `gourmand` | Gourmand | `Gourmand` | `#F0C197` |

Gourmand는 원작 `PlayerGraphics.DrawSprites`처럼 body의 X scale을 1.4, hips의 X scale을 1.6으로 적용합니다. Monk/Hunter/Gourmand의 몸무게와 Hunter의 달리기 계수도 `SlugcatStats`에서 확인한 값을 사용합니다.

Downpour가 설치되어 `rainworldmsc`의 필수 element가 모두 확인되면 tray의 `Slugcat skin`에서 Artificer, Spearmaster, Rivulet, Saint 외형을 별도로 선택할 수 있습니다. 이 선택은 물리/AI를 바꾸지 않습니다. 캐릭터별 DLL 분기와 sprite index는 [docs/SlugcatGraphicsProfiles.md](docs/SlugcatGraphicsProfiles.md)에 기록되어 있습니다.

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
.\artifacts\Release\SlugcatInMyMonitor.exe

# 원작 캐릭터 선택과 디버그 표시
.\artifacts\Release\SlugcatInMyMonitor.exe --slugcat gourmand --debug

# Player 물리는 유지하고 Downpour 외형만 선택
.\artifacts\Release\SlugcatInMyMonitor.exe --skin rivulet --debug

# 자동 탐색이 실패할 때 설치 경로 지정
.\artifacts\Release\SlugcatInMyMonitor.exe `
  --rain-world "C:\Program Files (x86)\Steam\steamapps\common\Rain World"
```

빌드 스크립트는 필요한 경우 Microsoft의 .NET Framework 4.8 reference-assembly NuGet 패키지를 `.tools/`에 내려받습니다. 실행 프로그램 자체에는 외부 런타임 패키지가 없습니다.

## 개발 및 릴리즈

일반 변경은 `feature/*` 또는 `fix/*`에서 작업한 뒤 `develop`으로 합칩니다.
배포할 변경이 모이면 `develop`에서 `main`으로 한 번의 PR을 만들고,
Release Drafter가 갱신한 초안 릴리즈를 게시합니다. 게시 후 Windows ZIP과
SHA-256 파일이 해당 GitHub Release에 자동으로 첨부됩니다. 자세한 규칙은
[`CONTRIBUTING.md`](CONTRIBUTING.md)를 참고하세요.

### 조작

- Slugcat 위에서 마우스 왼쪽 버튼: 잡기
- 잡은 채 이동 후 놓기: 던지기
- 모든 모니터 밖으로 던져진 경우: 1초 후 마지막 모니터의 안전한 바닥으로 자동 복귀
- 트레이 아이콘 왼쪽 클릭: `SlugcatInMyMonitor Settings` 창 열기
- Slugcat을 클릭하거나 잡기: 해당 개체 선택
- Settings 창: Slugcat 추가·선택·삭제, 캐릭터·스킨 설정, 외형 편집기, 디버그·일시정지, 렌더링 재시도, 종료
- 트레이 우클릭 메뉴: Settings 창을 열 수 없는 경우를 위한 보조 설정 경로

프로그램은 시스템 키 입력을 방해하지 않도록 전역 단축키를 등록하지 않습니다.

스킨 편집기는 일반적인 Windows 프로그램 구조를 사용합니다. 왼쪽 캐릭터 목록,
가운데 파츠별 스프라이트 선택·색상 버튼, 오른쪽 실제 아틀라스 미리보기와 하단
Reset/Copy/Paste/Reload 버튼을 제공합니다. DMS 폴더 탐색과 atlas 재적용을 지원하며,
여러 마리를 실행할 때 편집기와 외형 메뉴는 현재 선택된 Slugcat 한 마리에만 적용됩니다.

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

현재 실행 구현은 C#/.NET Framework 기반 네이티브 Windows 애플리케이션입니다. 이전 Godot 프로토타입의 검토 기록은 `docs/analysis/PrototypeReview.md`에 역사적 참고 자료로 남겨 두었습니다.

## 테스트

```powershell
.\build.ps1 -Configuration Debug
```

테스트는 fixed-step, 원작 connection/Stand/jump/air-control 식, monitor topology와 창 끝 낙하, pre-impact TerrainImpact, 극단·반복 충돌의 비치명 3초 stun cap, 단일 tail mesh topology, stunned graphics/mouse recovery, AI/physics 분리와 행동 도달성, DropDown, 휴식 frame, atlas metadata, 설치 경로 탐색을 검증합니다. 로컬 설치본이 있으면 embedded 원작 atlas가 DMS 없이 로드되는지도 검사합니다.

## 에셋 및 상표

이 저장소는 Rain World, Dress My Slugcat 또는 커뮤니티 스킨의 이미지와 게임 에셋을 배포하지 않습니다. 로컬 분석 도구와 추출 결과도 Git에서 제외됩니다. 자세한 내용은 [`THIRD_PARTY_TEST_ASSETS.md`](THIRD_PARTY_TEST_ASSETS.md)를 참고하세요.

이 프로젝트는 비공식 팬 프로젝트이며 Videocult 또는 Akupara Games와 제휴하거나 승인을 받은 프로젝트가 아닙니다. Rain World 및 관련 명칭과 자산의 권리는 각 권리자에게 있습니다.

프로젝트 코드는 [MIT License](LICENSE)로 배포됩니다. 제3자 자산에는 이 라이선스가 적용되지 않습니다.
