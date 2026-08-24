# Rain World 원작 충실도 보강 기록

이 문서는 데스크톱 포팅에서 발견한 이동, 능력, 렌더링, 오디오 및 외부 스킨의
차이를 로컬 Rain World 설치본과 다시 대조한 결과다. 분석은 게임이나 Workshop DLL을
실행하지 않는 정적 방식으로 수행했고, 실제 설치 자산은 검증 테스트에서 읽기 전용으로
사용했다.

## 1. 기준 설치본과 분석 범위

- 설치 루트: `C:\Program Files (x86)\Steam\steamapps\common\Rain World`
- 게임 버전: `v1.11.8`
- `Assembly-CSharp.dll`: 9,064,960 byte
- SHA-256:
  `B6BE1D4E18CE219D21091B51564CB6A11C1E4106B41DE903EB8E58849CB16FDB`
- DMS Workshop item: `2948971756`, 설치 `modinfo` 버전 `2.1.7`
- 핵심 분석 대상: `Player`, `PlayerGraphics`, `ExplosionSmoke`, `ExplosionLight`,
  `ShockWave`, `Spark`, `Player.Tongue`, DMS `SpriteDefinitions`/`SpriteSheet`

원본 DLL, BepInEx 플러그인, Workshop DLL은 로드하거나 실행하지 않았다. ILSpy로 만든
중간 산출물은 gitignored `.tools`에만 두었고, 게임/모드 자산은 저장소에 추가하지 않았다.
게임 또는 DMS가 업데이트되어 위 버전/해시가 달라지면 수치와 분기를 다시 검증해야 한다.

## 2. 고정 tick과 이동 오차

`MainLoopProcess.RawUpdate`는 기본 40 Hz 논리 tick을 누산하고 한 렌더 프레임에 최대
3회 업데이트한 뒤 `GrafUpdate(timeStacker)`로 보간한다. 데스크톱 포팅도 40 Hz 물리를
유지하며 렌더 주사율을 물리식에 곱하지 않는다.

### Crawl 반전

원작 `Player.UpdateBodyMode`의 Crawl 분기는 수평 `dynamicRunSpeed`를 2.5로 놓고, 입력
방향이 현재 두 BodyChunk의 수평 축과 반대이면 즉시 0.75배한다. 기존 포팅은 facing을
먼저 바꿔 이 비교를 잃었다. 수정본은 facing 갱신 전에 body axis를 잡고 반전 tick에
`2.5 × 0.75 = 1.875`를 적용한다. 이는 5 tick 뒤 발생하는 CrawlTurn 애니메이션 힘과
별개의 분기다.

### 일반 Backflip과 belly-slide 반전

원작 `Player.UpdateAnimation`의 Flip 각운동 힘은 다음과 같다.

```text
Perpendicular(body1, body0) × slideDirection
× Lerp(0.38, 0.8, Adrenaline)
× (flipFromSlide ? 2.5 : 1)
```

기존 포팅은 모든 Flip에 2.5를 곱해 일반 backflip이 과회전했다. `FlipFromSlide` 상태를
별도로 보존해 일반 점프 Flip에는 1, belly-slide 방향 반전에는 2.5만 적용한다.
일반 backflip 진입의 앞/뒤 chunk 발사값(-9/-7), jump boost 5, Flip 전환은 기존
`Player.Jump` 대응값을 유지한다.

## 3. Spearmaster 팔과 창 좌표계

`PlayerGraphics.DrawSprites`는 보간된 손 위치, 보간된 두 BodyChunk 중간의 어깨,
두 점 거리의 절반, `AimFromOneVectorToAnother + 90°`, 그리고
`Sign(DistanceToLine(...))`로 팔 sprite를 만든다. Futile은 y-up, Windows/GDI+는 y-down이므로
signed distance의 부호는 좌표 반사 때 한 번 뒤집어야 한다. 기존 코드는 이를 반영하지
않아 일부 자세에서 팔이 반대로 접혔다.

수정본은 일반 자세의 scaleY 부호만 y-down에 맞게 반전한다. Crawl과 WallClimb의 원작
특수 분기는 그대로 두고, 손 target과 쥔 창은 같은 world-to-render 변환 체인을 사용한다.
따라서 좌우/상하 자세에서도 팔, 손, 창이 한 관절 계통으로 움직인다.

## 4. Artificer 능력과 효과

`Player.ClassMechanicsArtificer`에서 확인한 폭발 점프 한 tick의 생성량은 다음과 같다.

- `ExplosionSmoke` 8개
- `ExplosionLight(position, 160, 1, 3, white)` 1개
- `Spark` 10개
- parry 때 위 구성과 함께 `ShockWave(position, 200, 0.2, 6)` 1개

수정본은 이 개수와 동일 tick 생성 순서를 보존한다. 원작 효과 렌더링 대조 결과도
다음과 같이 반영했다.

- `ExplosionSmoke`: `Futile_White` 두 장, `FireSmoke` shader, life 곡선과 11배
  radius, 1.1/0.9 scale, life^1.8의 0.8/0.6 alpha, 회전 및 두 palette 계층
- `ExplosionLight`: `FlatLight` 한 장과 `LightSource` 두 장, 초기 `lastLife=0`,
  `sqrt(life) × radius / 8` scale과 서로 다른 alpha 곡선
- `ShockWave`: `ShockWave` shader, `sqrt(progress)` scale과 원작의
  `(pow(progress, 0.1), intensity, progress)` sprite color
- `Spark`: 하나의 삼각형 trail

설치된 `resources.assets`에서 `Futile/FireSmoke`, `Futile/FlatLight`,
`Futile/LightSource`, `Futile/ShockWave` 등록을 확인했다. 연기는 D3D11 절차형 noise를
DirectComposition 효과 surface에 직접 그린다. 원본의 세 noise 결합과 `.35` discard,
개수, 수명, 크기/alpha 곡선, 두 계층은 유지하지만 Unity texture의 픽셀과 room palette는
근사치다. 나머지 효과는 시작 시 만든 64×64 alpha mask를 재사용한다.

## 5. Saint Tongue

`Player.Tongue.Attach`는 `elastic=1`과 현재 거리의 requested length를 설정한다.
`RequestRope`는 `min(requestedRopeLength, onRopePos × totalRope)`이며 기본 terrain
attachment에서는 `onRopePos=1`, 최대 rope는 200이다. `Elasticity`의 연결 계수는
다음과 같다.

```text
a = LerpMap(abs(0.5 - onRopePos), 0.5, 0.4, 1.1, 0.7)
target = RequestRope × Lerp(a, 1, elastic)
strength = Lerp(0.85, 0.25, elastic)
```

기존 0.7 고정값은 막 붙은 느슨한 줄도 Saint를 anchor로 끌어당겼다. 수정본은 위 식과
200 cap을 사용하며, 실제 rope가 target보다 길 때만 초과 길이 힘을 가한다. attachment
상태에서 점프하면 먼저 줄을 놓고 y-down 기준 -8/-7 launch와 boost 8을 적용한다.

## 6. Dress My Slugcat 파츠 격리

DMS `SpriteDefinitions`에서 확인한 공식 그룹은 HEAD, FACE, BODY, ARMS, HIPS, LEGS,
TAIL, FACESCAR, GILLS, TAILSPECKLES, ASCENSION, PIXEL의 12개다. `SpriteSheet.ParseAtlases`는
그룹의 일반 element 필수 frame이 전부 있을 때만 해당 파츠를 선택 가능하게 만든다.
비대칭 atlas는 그와 별도로 left/right 양쪽의 필수 frame 집합을 모두 검증한다.

수정된 Skin Editor는 12개 파츠 각각에 `Vanilla` 또는 그 파츠가 완전한 DMS skin만
선택하게 한다. 렌더러도 현재 element가 속한 파츠의 선택만 조회한다. 따라서 face-only
skin은 face 밖의 몸, 팔, 꼬리, 특수 파츠를 오염시키지 않는다. reload 뒤 사라지거나
불완전해진 파츠는 그 파츠만 vanilla로 돌아간다. 이전 whole-skin 설정/트레이 메뉴는
제거했고, `--dms-skin`은 호환용으로 한 skin의 완전한 파츠를 한 번에 적용하는 원자적
경로로만 남겼다. unsafe한 DMS BinaryFormatter 저장 파일과 mod DLL은 읽지 않는다.

## 7. 오디오 경로

설치된 `sounds.txt`와 UnityFS `soundeffects` bundle은 시작 시 한 번 색인한다. bundle은
FSB5 PCM16(codec 2), 1,619 clips였다. 원작 SoundID의 clip 이름 `jump2`처럼 정확한
파일이 없고 `jump2A..G` 또는 `jump2_1..7`만 있는 경우를 위해 숫자 뒤 대문자와 `_숫자`
suffix를 clip family로 묶는다.

메인 tick은 최대 32개 bounded queue에 재생 요청만 넣는다. bundle range read, PCM 추출,
gain/pan/pitch 처리, WAV 작성과 `SoundPlayer.Load/Play`는 전용 worker가 수행한다. PCM
cache는 64개로 제한하고 loop stop/cancel을 동기화한다. 실패는 Debug/Trace에 SoundID,
clip 및 이유를 명시한다. 실제 설치본 회귀 테스트는 `jump2` family를 bundle에서
디코드하고 메인 호출이 100 ms 안에 반환한 뒤 playback-start 상태가 되는지 확인한다.
스턴 ID는 실제 등록된 `UI_Slugcat_Stunned_Init`로 수정했다. jump 외에도 step,
terrain impact, floor-impact landing, Artificer explosion SoundID의 실제
playback-start를 설치 bundle로 검증한다. 원작 코드가 호출하는
`Slugcat_Regain_Footing`은 v1.11.8 `sounds.txt`에서 주석 처리되어 있어 임의 clip으로
대체하지 않는다.

Push To Meow WAV도 별도 worker에서 읽고 재생한다. 현재 활성 모드에서 Survivor clip의
실제 MCI/SoundPlayer playback-start를 검증한다. 추가 codec 없는 OGG 재생은 지원하지
않는다.

## 8. 멈춤과 할당 경로

Windows window/monitor/DWM 열거는 주기 갱신 요청을 background worker로 보내고, main
tick에서는 준비된 immutable snapshot만 교체한다. 창이 한 번 누락됐을 때 즉시 terrain을
없애지 않도록 grace를 유지한다. 오디오 I/O와 parity 로그 파일 쓰기도 background로
이동했고 queue를 bounded 처리한다.

steady render 경로에서는 rope/input/effect/spear 배열의 복사 대신 내부 read-only view를
사용하고, tail UV/triangle, ability polygon, brush와 effect mask를 재사용한다. sprite별
debug placement와 긴 debug 문자열은 debug overlay가 켜졌을 때만 만든다.

## 9. 검증과 남은 한계

회귀 테스트는 40/60/144/240 Hz 재현, crawl 반전, 두 Flip 종류, Spearmaster 팔 반사,
Saint shoot/attach/jump-release/탄성, Artificer 효과 수명, 설치 DMS 12파트 및 contamination
방지, 비동기 desktop refresh, 실제 설치 UnityFS 오디오 decode/queue를 포함한다. 창 구조를
바꾸는 5분 분량의 40 Hz 자동 시뮬레이션도 sprite/physics 무결성을 검사한다.

남은 경계는 명확하다.

- Rain World room tile/shortcut/creature/oracle 전체와 Unity shader를 호스팅하지 않는다.
- Windows 창/모니터 윤곽은 원작의 tile slope, beam, water, shortcut 지형과 동일하지 않다.
- Unity shader 픽셀 결과와 room별 palette는 GDI+ mask/고정 palette로 근사한다.
- 데이터만으로 정의된 DMS 공식 파츠는 지원하지만, 외부 DLL이 동적으로 추가한 sprite
  group이나 shader 코드는 실행하지 않는다.
- 실제 Rain World 런타임 영상과의 픽셀 비교 및 장시간 실제 GUI 벽시계 soak는 이번
  검증에 포함하지 않았다. 정적 설치본 분석과 자동 5분 시뮬레이션을 기준으로 한다.
