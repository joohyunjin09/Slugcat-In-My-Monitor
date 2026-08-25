# 음식 업데이트 상세 보고서

작성일: 2026-08-25
대상 브랜치: `feature/food-update-pr`
대상 fork: `Blueslime0216/Slugcat-In-My-Monitor`

## 1. 업데이트 결과

이번 업데이트는 데스크톱 Slugcat에게 실제로 먹이를 주고, Slugcat이 먹이 쪽으로 이동해 집어 들어 베어 먹는 음식 시스템을 추가한다. 지원 아이템은 Rain World의 푸른 열매(영문 `Blue Fruit`, 내부 클래스 `DangleFruit`)와 알벌레 알 `EggBugEgg`다.

사용 절차는 다음과 같다.

1. 시스템 트레이의 Slugcat 아이콘을 우클릭한다.
2. `먹이 주기 · 슬러그캣 N` 메뉴를 연다.
3. `푸른 열매 주기` 또는 `알벌레 알 주기`를 선택한다.
4. 현재 선택된 Slugcat에서 무작위 방향과 거리의 상공에 먹이가 나타나 바닥으로 떨어진다.
5. Slugcat은 포만감과 무작위 appetite 판정에 따라 접근해 먹거나, 관심을 보이지 않고 남겨 둔다.

전역 단축키, 화면 위 고정 버튼, 다음 마우스 클릭으로 위치를 지정하는 모드는 추가하지 않았다. 따라서 게임, 작업 프로그램, 브라우저의 단축키와 충돌하지 않고 화면을 가리지 않는다. 먹이 자체도 마우스 히트테스트 대상이 아니므로 평상시 바탕화면 클릭을 통과시킨다.

## 2. 구현 범위

포함된 기능:

- `DangleFruit` 물리 객체
- `EggBugEgg` 물리 객체, 원본 3-layer sprite 조합과 5구간 꼬리 mesh
- 원작 `ApplyPalette`를 재현하는 음식 전용 desktop palette
- 자유, 예약, 들기, 먹는 중, 소비, 만료 상태
- 원작의 3 bites와 1 food point 계약
- 원작에 대응하는 반지름 8, 질량 0.2, 중력 0.9, air friction 0.999, surface friction 0.7, bounce 0.2
- 로컬 Rain World 설치본의 `DangleFruit0A/B`, `1A/B`, `2A/B` atlas frame 사용
- 선택된 Slugcat 전용 먹이 예약
- `VirtualInput`을 통한 자율 접근
- 머리 위치를 기준으로 한 들기와 3단계 섭취
- 움직이는 창 표면의 이동량 적용
- 자유 상태는 Slugcat 뒤, 들거나 먹는 상태는 Slugcat 앞에 그리는 레이어 순서
- 기존 DirectComposition 배치 bounds에 음식 범위 병합
- 140–360 desktop pixels의 무작위 거리, 45–120px 높이에서 낙하
- 포만감 최대 3점, 예약 먹이를 포함한 appetite 판정, 약 90초당 1점 소화
- Slugcat 한 마리당 최대 5개, 전체 최대 12개 제한
- 약 30초 동안 먹지 않은 자유 음식 자동 만료
- 선택된 Slugcat의 음식 치우기 메뉴

의도적으로 제외한 기능:

- 굶주림 벌점이나 동면을 강제하는 생존용 food meter
- 동면, cycle, karma와 연결된 생존 규칙
- 사운드 재생
- 사용자가 먹이를 직접 드래그하는 기능
- 서로 다른 Slugcat이 하나의 먹이를 두고 경쟁하는 기능
- 다른 높이의 창으로 이동하는 장거리 먹이 pathfinding
- Fly, Mushroom, WaterNut, JellyFish, KarmaFlower 등 복합 아이템

## 3. Rain World 원본 조사 결과

로컬 설치본은 `v1.11.8`이었으며, 프로젝트가 이미 사용하는 읽기 전용 Unity asset 추출 경로와 동일한 방식으로 atlas를 확인했다. 저장소나 빌드 산출물에 Rain World 이미지 파일을 복사하지 않는다.

원작 `IPlayerEdible`의 핵심 계약은 다음과 같다.

- `BitesLeft`
- `BitByPlayer(Creature.Grasp, bool)`
- `FoodPoints`
- `Edible`
- `AutomaticPickUp`
- `ThrowByPlayer()`

원작 Player의 섭취 흐름은 개념적으로 `GrabUpdate → BiteEdibleObject → ObjectEaten → AddFood/AddQuarterFood`다. `DangleFruit`는 `PlayerCarryableItem` 기반의 한 개 BodyChunk 아이템이고, 초기 bites는 3, food points는 1, automatic pickup은 true다. 마지막 bite에서 `ObjectEaten`을 호출하고 grasp를 해제한 뒤 아이템이 소멸한다.

`EggBugEgg`도 한 개 BodyChunk를 사용하며 초기 bites는 2, food points는 1이다. 기본 swell 상태의 반지름은 약 4.6, 질량은 0.2다. 원작은 `DangleFruit0A/1A`, `EggBugEggColor/EggBugEggColorEaten`, `JetFishEyeA`를 겹쳐 그리고 5구간의 유연한 mesh를 덧붙인다. 데스크톱 구현은 세 atlas layer와 bite 교체를 보존하고, 원작 segment 수와 길이를 기준으로 가벼운 5구간 꼬리 mesh를 그린다. 방의 충돌과 particle system이 필요한 liquid drip만 제외했다.

사용자 스크린샷을 기준으로 원본 DLL의 색상 경로를 다시 조사했다. `DangleFruit.ApplyPalette`는 A 레이어를 `RoomPalette.blackColor`, B 레이어를 순청색 `(0, 0, 1)`과 `blackColor`의 darkness 혼합색으로 설정한다. 기존 데스크톱 코드는 B를 어둡게 그리고 A를 밝은 하늘색으로 덮어 레이어 역할과 순서가 모두 반대였다. 수정 후에는 A를 먼저 검은 외곽색으로, B를 나중에 짙은 포화 청색으로 그린다.

일반 `EggBug`의 hue는 전체 색상환의 균등 난수가 아니다. 개체 `EntityID` 시드로 `ClampedRandomVariation(0.5, 0.5, 2)`를 계산한 뒤 `-0.15–0.10` 범위로 보간하며, 떨어진 알은 부모의 hue를 상속한다. 기존 구현의 `Random.NextDouble()`은 `0–1` 전체를 사용했기 때문에 원작 일반 알벌레 분포와 무관한 조합이 대부분이었고, 낮은 확률로만 원작과 비슷해졌다. 현재 구현은 원작의 제한 범위와 S-curve 분포를 재현한다. 최종 shell, liquid, detail 색도 원작 `EggBugGraphics.EggColors`의 HSL 및 darkness 보간식을 따른다.

이 데스크톱 프로젝트에는 Rain World의 `Room`, `AbstractPhysicalObject`, creature graph, cycle 시스템이 없다. 원작 전체 객체 계층을 이식하면 작은 음식 기능 때문에 결합도와 메모리 비용이 과도하게 커진다. 그래서 `IPlayerEdible`의 사용자에게 보이는 계약만 `DesktopFood`로 옮기고, 기존 `BodyChunk`와 `DesktopCollisionWorld`를 재사용했다.

기존 `IGourmandEdible`과 `GourmandCraftingFramework`는 조합법을 위한 골격이며 런타임 물리 아이템 계약이 아니다. 이번 구현은 이를 억지로 확장하지 않고 별도의 데스크톱 음식 계층을 만들었다. 나중에 Gourmand crafting을 구현할 때 `DesktopFoodKind`와 recipe adapter를 연결하는 편이 안전하다.

## 4. 구조와 데이터 흐름

### `DesktopFood`

파일: `src/RainWorldDesktopPet/Physics/DesktopFood.cs`

한 개 음식의 물리와 edible 상태를 소유한다. `BodyChunk` 한 개를 사용하며, atlas element 이름은 정적 배열에서 조회한다. 렌더 프레임마다 문자열을 조합하지 않으므로 불필요한 GC 할당이 없다.

상태 흐름:

`Free → Claimed → Held → Biting → Consumed`

예외 흐름:

- 잡힌 Slugcat이 기절하거나 사용자가 직접 들어 올리면 `Held/Biting → Free`
- 자유 상태로 1200 simulation ticks, 약 30초가 지나면 `Free/Claimed → Expired`

### `DesktopFoodManager`

파일: `src/RainWorldDesktopPet/Core/DesktopFoodManager.cs`

각 `GameLoop`가 관리자 한 개를 소유한다. 이 소유 관계가 예약 역할을 하므로 여러 Slugcat이 같은 먹이를 동시에 선택하지 않는다. 음식 접근은 기존 AI가 직접 물리를 바꾸는 방식이 아니라 최종 `VirtualInput`만 덮어쓴다. 실제 걷기, 마찰, 충돌은 기존 Slugcat movement 경로가 계속 담당한다.

먹이는 현재 지지 표면 위에서 140–360 desktop pixels 떨어진 무작위 방향에 생성된다. 68%는 현재 바라보는 방향, 32%는 반대 방향이며, 바닥 위 45–120px 높이에서 실제 물리로 떨어진다. 지지 표면을 찾지 못하면 가장 가까운 monitor work area의 floor를 사용하고, 생성 위치는 표면 좌우 범위 안으로 clamp한다.

접근 거리가 충분히 가까워지고 Slugcat이 grounded 상태이면 먹이를 집는다. 8 ticks 동안 들기 자세를 유지한 뒤 18 ticks 간격으로 bite한다. 푸른 열매는 3회, 알벌레 알은 2회 뒤 1 food point를 얻는다.

각 Slugcat은 0–3점의 세션 포만감을 가진다. 공복이면 첫 제안을 항상 수락하지만, 이후에는 포만감이 높을수록 수락 확률이 78%에서 12%까지 낮아진다. 이미 수락했지만 아직 먹지 않은 아이템도 예상 포만감에 합산하므로 여러 개를 빠르게 놓아도 전부 예약하지 않는다. 예상 포만감이 3점이면 반드시 거절한다. 거절한 먹이는 `Ignored` 상태로 화면과 물리에 남지만 AI target이 되지 않는다. 포만감 1점은 3600 ticks, 약 90초에 걸쳐 소화된다.

### `GameLoop`

파일: `src/RainWorldDesktopPet/Core/GameLoop.cs`

40Hz 고정 tick의 처리 순서는 다음과 같다.

1. 음식 자유 물리 업데이트
2. 기존 AI 입력 계산
3. 활성 음식이 있으면 음식 접근 입력으로 최종 intent 조정
4. 기존 Slugcat 물리와 movement 실행
5. 기존 Slugcat graphics 업데이트
6. 최신 머리 위치에 든 음식을 고정하고 bite timer 진행

기존 AI를 매 tick 계속 실행하므로 성격, 필요도, cooldown 값이 음식 섭취 중에도 멈추지 않는다. 음식 controller는 먹이가 활성화된 동안 최종 이동 intent만 제한한다.

### 렌더링과 합성

파일: `src/RainWorldDesktopPet/Graphics/SpriteRenderer.cs`, `src/RainWorldDesktopPet/UI/LayeredOverlayWindow.cs`

음식을 위한 별도 DirectComposition surface를 생성하지 않는다. 각 음식은 소유 Slugcat의 기존 render batch에 포함되고, 해당 loop의 bounds만 필요한 만큼 union한다. 기존 최소 surface 크기가 384px이고 먹이가 가까운 곳에 생기므로 대부분의 경우 surface resize도 발생하지 않는다.

렌더링은 로컬 atlas에 frame이 있으면 푸른 열매의 두 레이어 또는 알벌레 알의 세 레이어를 사용한다. `FoodRenderPalette`가 원작의 레이어별 tint와 알벌레 hue 분포를 한곳에서 계산한다. 데스크톱에는 `RoomPalette`, `Room.Darkness`, `LightSourceExposure`가 없으므로 중립적인 고정 black/fog palette와 reference darkness `0.4`를 사용한다. 이 값은 사용자가 제공한 어두운 인게임 푸른 열매와 바탕화면 위 가시성을 함께 맞추기 위한 desktop 기준값이다.

알벌레 알의 꼬리는 별도 bitmap이나 물리 객체를 만들지 않고 renderer가 재사용하는 12개 꼭짓점 배열로 그린다. 색상 brush도 기존 `bodyBrushes` 캐시를 공유해 매 프레임 GC 할당을 만들지 않는다. 로컬 설치본이 예상과 달라 frame을 찾지 못할 경우 앱 전체를 중단하지 않고 작은 procedural fallback을 그린다. 정상 설치본에서는 자동 테스트가 모든 사용 frame과 `#rainWorld` 출처를 확인한다.

### 트레이 UI

파일: `src/RainWorldDesktopPet/UI/LayeredOverlayWindow.cs`

트레이 우클릭 메뉴에 다음 항목을 추가했다.

- `먹이 주기 · 슬러그캣 N`
  - `푸른 열매 주기`
  - `알벌레 알 주기`
  - `포만감 0.0/3.0`
  - `선택한 슬러그캣의 먹이 치우기`

메뉴를 열 때 현재 선택 번호, 포만감, 제한 상태를 갱신한다. 수락한 경우 balloon을 띄우지 않고, 먹이를 거절했거나 개수 제한에 도달했을 때만 짧은 안내를 표시한다.

## 5. 입력과 데스크톱 사용성 검토

이 프로젝트의 overlay window는 기본적으로 `WS_EX_TRANSPARENT`와 `WS_EX_NOACTIVATE`를 사용한다. 저수준 mouse hook도 Slugcat을 직접 잡는 left-click에만 개입한다. 이번 업데이트는 `GameLoop.HitTest`에 음식을 추가하지 않았으므로 음식 위 클릭은 기존처럼 아래 프로그램으로 전달된다.

채택하지 않은 UI:

- 전역 키: 게임과 편집기 단축키 충돌 가능
- Slugcat right-click 또는 middle-click: 브라우저/게임/마우스 유틸리티와 충돌 가능
- 화면 고정 food palette: 화면 가림과 항상 위 창 증가
- `먹이 배치 모드` 후 다음 클릭: 사용자가 모드 종료를 잊으면 일반 클릭을 가로챌 위험

현재 방식의 비용은 트레이를 두 번 클릭해야 한다는 점이다. 그러나 자주 반복하는 핵심 작업이 아니라 간헐적인 상호작용이고, 충돌과 화면 점유를 확실히 줄인다는 장점이 더 크다고 판단했다.

## 6. 성능과 안정성

- 물리는 기존 40Hz fixed timestep을 사용한다.
- 음식은 한 개 BodyChunk만 사용한다.
- 같은 tick의 immutable `DesktopCollisionSnapshot`을 재사용한다.
- 음식별 renderer, bitmap, composition surface를 만들지 않는다.
- atlas image는 기존 `RainWorldAtlasSet` 캐시를 공유한다.
- 음식 palette 계산은 작은 값 형식으로 반환하며 bitmap을 만들지 않는다.
- 알벌레 꼬리 꼭짓점 배열과 색상 brush를 renderer가 재사용한다.
- 알벌레 알의 procedural tail을 포함한 시각 반경 23을 합성 bounds에 사용해 가장자리 잘림을 방지한다.
- 앞/뒤 두 음식 레이어 중 비어 있는 pass는 Matrix와 GraphicsState를 만들기 전에 종료한다.
- 로컬 atlas가 없거나 일부 layer만 빠진 경우에도 각 layer별 procedural fallback으로 형태와 핵심 색을 유지한다.
- 장시간 여러 hue를 생성해도 GDI `ImageAttributes`와 `SolidBrush` cache가 각각 1024개를 넘지 않게 정리한다.
- element 이름은 정적 문자열 배열로 캐시한다.
- 음식은 Slugcat당 5개, 전체 12개로 제한한다.
- 먹지 않은 음식은 1200 ticks 후 제거한다.
- 렌더링은 음식 수가 Slugcat당 최대 5개인 작은 선형 loop 두 번으로 제한된다.
- 움직이는 window surface의 delta를 음식에도 적용하여 창 이동 시 떠 있거나 뒤처지는 현상을 줄였다.

## 7. 테스트와 빌드

추가된 자동 검증:

- DangleFruit 초기 bites, food points, radius, mass
- bite마다 `0 → 1 → 2` atlas frame 진행
- 마지막 bite 뒤 consumed 상태
- 음식 방향으로 생성되는 `VirtualInput`
- 소유 Slugcat의 target reservation
- food attention target
- 들기, 세 번 bite, 1 food point 완료
- 로컬 Rain World atlas의 `DangleFruit0/1/2A/B` 여섯 frame 존재
- frame이 설치된 원본 `#rainWorld` atlas에서 왔는지 확인
- EggBugEgg의 2 bites, radius, mass와 세 sprite layer
- DangleFruit A/B의 검은 외곽/짙은 청색 역할과 이전 하늘색 제거
- 일반 Eggbug hue 4,096개가 원작 `-0.15–0.10` 범위를 벗어나지 않는지 확인
- 대표 Eggbug palette가 cyan liquid와 warm detail을 생성하는지 확인
- 로컬 Rain World atlas를 실제 bitmap으로 렌더링해 deep blue, cyan, warm pixel 검출
- 푸른 열매 영역에 이전 pale sky-blue pixel이 남지 않는지 확인
- 140–360px 무작위 생성 범위와 바닥 위 낙하 시작
- 다섯 번 연속 제안에서 섭취와 거절이 모두 발생하는지 확인
- 최대 포만감 제한과 90초당 1점 소화
- 푸른 열매 몸체와 알벌레 알 꼬리를 모두 포함하는 composition 시각 반경
- 알려지지 않은 food kind가 푸른 열매로 조용히 처리되지 않고 명시적으로 실패하는지 확인
- 음식 치우기 후 target, interaction countdown, accepted 상태가 남지 않는지 확인
- 로컬 atlas 전체가 없어도 두 음식 fallback이 모두 보이는지 bitmap으로 확인
- 1,100개 색상을 연속 요청해도 GDI 색상 resource cache가 상한을 지키는지 확인

검증 명령:

```powershell
.\build.ps1 -Configuration Release
.\artifacts\Release\RainWorldDesktopPet.Tests.exe --food-preview .\artifacts\FoodPalettePreview.png
```

두 번째 명령은 저장소에 에셋을 복사하지 않고 로컬 Rain World atlas에서 푸른 열매 한 개와 서로 다른 hue의 알벌레 알 네 개를 렌더링하는 시각 검증용 명령이다. 최종 Release 빌드는 경고 0개, 오류 0개로 완료했고 기존 전체 회귀 테스트와 새 음식 테스트가 모두 통과했다. 실행 파일은 `artifacts/Release/SlugcatInMyMonitor.exe`에 생성된다. 네이티브 렌더러인 `SlugcatInMyMonitor.DirectComposition.dll`도 같은 폴더에 있어야 한다.

빌드 도중 기존 실행 파일이 실행 중이면 Windows가 산출물 교체를 막는다. 이 경우 트레이에서 앱을 종료한 뒤 다시 빌드해야 한다.

## 8. 커밋 이력

- `8f71ba5` — `feat: add desktop Dangle Fruit edible model`
- `586cc63` — `feat: add tray feeding and autonomous eating flow`
- `7a78ead` — `feat: render food from local Rain World atlas`
- `f0469b8` — `docs: document food update and extension plan`
- `f638d0f` — `feat: add Eggbug Eggs and appetite-driven feeding`
- `4efd419` — `docs: record appetite update and fruit color issue`
- `a26036c` — `fix: restore original food palette behavior`
- `cb06092` — `docs: record food palette correction`
- `c217fcb` — 신규 4종 보고서 변경 revert
- `96d9b85` — 신규 4종 구현 revert
- `f8fd5d5` — `fix: stabilize two-food desktop interactions`
- `d6e9b13` — 최신 `upstream/develop` 통합

각 커밋은 `origin/feature/food-update`에 순차적으로 push했다.

신규 4종 실험은 사용자 검토 결과 채택하지 않아 두 revert 커밋으로 완전히 취소했다. 현재 PR의 파일 변경 결과에는 `SlimeMold`, `DandelionPeach`, `GlowWeed`, `Mushroom` 코드와 UI가 남아 있지 않으며, 지원 범위는 푸른 열매와 알벌레 알 2종뿐이다.

PR 안정화 시점에는 저장소 규칙에 따라 `main`이 아니라 최신 `develop`을 기준으로 동기화했다. `upstream/develop` 대비 뒤처진 커밋은 0개이며 Release 전체 테스트, `node --check tools/validate-dms-template.mjs`, 배포 ZIP 생성과 SHA-256 생성까지 확인했다.

## 9. 새 음식 추가 방법

복합 동작이 없는 정적 edible부터 추가하는 것이 안전하다. 추천 순서는 다음과 같다.

1. `DesktopFoodKind`에 종류를 추가한다.
2. bites, food points, radius, mass, friction, bounce, lifetime을 immutable definition으로 분리한다.
3. 로컬 atlas element 목록을 정의하고 `RainWorldAtlasSet.TryGet`으로 availability를 검사한다.
4. 원작의 최종 bite 부가 효과를 작은 desktop effect interface로 표현한다.
5. `DesktopFoodManager`의 spawn factory와 tray submenu를 추가한다.
6. 원작 계약, 상태 전이, atlas 출처, fallback을 자동 테스트한다.
7. 음식이 기존 composition bounds와 click-through를 깨지 않는지 실제 데스크톱에서 확인한다.

다음 후보 평가:

- EggBugEgg: 두 번째 음식과 가벼운 5구간 꼬리 mesh 구현 완료. liquid drip은 후속 시각 개선 후보
- Mushroom: 먹는 동작은 단순하지만 time slowdown을 데스크톱에서 어떻게 표현할지 제품 결정이 필요
- Fly: creature AI, 날개 animation, capture, sound가 필요하므로 별도 creature 시스템 이후로 연기
- WaterNut/JellyFish: 물, 전기, tentacle 의존성이 커서 현재 desktop terrain 모델과 맞지 않음
- KarmaFlower: karma와 death persistence가 없으므로 장식 이상의 의미를 정하기 전에는 추가하지 않음

정적 아이템이 2개 이상이 되면 `DesktopFood`의 종류별 조건문을 늘리기보다 `DesktopFoodDefinition` registry로 radius, mass, bites, atlas layers, tint, effect를 데이터화해야 한다. Fly처럼 살아 있는 먹이는 `DesktopFood`에 넣지 말고 별도 `DesktopCreature` 계층으로 분리해야 한다.

## 10. 알려진 제한과 다음 권장 작업

- 푸른 열매의 A/B 색상·순서 오류와 알벌레 알의 전체 hue 난수 오류는 `a26036c`에서 교정했다. 다만 데스크톱 앱에는 Rain World의 현재 방 정보가 없으므로 방마다 달라지는 `blackColor`, `fogColor`, darkness와 광원 노출을 실시간으로 재현하지는 않는다. 현재는 reference darkness `0.4`의 고정 중립 palette를 사용하므로 특정 방의 스크린샷과 픽셀 단위로 완전히 같지는 않을 수 있다.
- 음식은 현재 Slugcat이 지지받는 같은 표면 또는 가까운 monitor floor에 생성하도록 최적화되어 있다. 사용자가 창을 급격히 옮겨 먹이가 다른 층으로 떨어지면 Slugcat이 장거리 pathfinding을 하지 못할 수 있으며, 30초 후 자동 제거된다.
- 현재 들기 위치는 head 기반 mouth anchor다. 원작처럼 grasp별 손 animation을 완전히 재현하려면 `SlugcatGraphics`에 food hand target mode를 추가해야 한다.
- bite event 이름은 남기지만 사운드는 재생하지 않는다. 프로젝트 전체 sound backend가 생길 때 event를 연결할 수 있다.
- 포만감은 세션 동안만 유지되고 앱을 다시 실행하면 공복으로 시작한다. 장기 저장은 방치형 사용에서 원치 않는 벌점이 될 수 있으므로 현재는 의도적으로 제외했다.
- 실제 사용 피드백에서 트레이 단계가 번거롭다는 의견이 많을 경우에만 사용자가 직접 지정하는 optional hotkey를 설정 화면에 추가한다. 기본값은 계속 비활성으로 두는 것이 좋다.

## 11. 라이선스와 배포

프로젝트 코드는 MIT License이므로 원래 copyright 및 license notice를 유지하면 수정, fork 공개, 배포가 가능하다. 이번 업데이트는 Rain World asset을 저장소에 포함하지 않는다. 사용자의 로컬 정품 설치본을 런타임에 읽는 기존 구조를 그대로 사용한다.

fork나 release에 다음 항목을 넣지 않아야 한다.

- 추출한 Rain World PNG 또는 atlas 파일
- `Assembly-CSharp.dll` 등 게임 바이너리
- Rain World 설치 데이터의 복사본

배포 ZIP에는 이 프로젝트에서 빌드한 실행 파일과 네이티브 렌더러만 넣고, README에 정품 Rain World 로컬 설치 요구 사항을 유지한다.
