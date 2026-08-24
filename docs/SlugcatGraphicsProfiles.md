# Downpour Slugcat graphics profiles

## 조사 기준

- 설치본: `C:\Program Files (x86)\Steam\steamapps\common\Rain World`, `v1.11.8`
- DLL: `RainWorld_Data\Managed\Assembly-CSharp.dll`
- DLL SHA-256: `B6BE1D4E18CE219D21091B51564CB6A11C1E4106B41DE903EB8E58849CB16FDB`
- 자산: `RainWorld_Data\resources.assets` / `resources.assets.resS`
- 확인 코드: `PlayerGraphics.ctor`, `InitiateSprites`, `Update`/`MSCUpdate`, `DrawSprites`, `ApplyPalette`, `AddToContainer`, `DefaultFaceSprite`, `SpinePosition`, `BodyPart`, `TailSegment`

로컬 Unity 자산에서 `rainWorld` 620개, `rainworldmsc` 188개 atlas element를 확인했다. 런타임에는 이 파일을 메모리에서 읽을 뿐 원본 파일을 수정하거나 자산을 빌드 결과에 복사하지 않는다. 8종 캐릭터 선택은 항상 유지되며, 원본 element가 없는 렌더 경로만 기존 procedural fallback을 사용한다.

## 공통 sprite index

| Index | 원작 목적 | Atlas/mesh | Desktop 객체 |
|---:|---|---|---|
| 0 | torso | `BodyA`, anchorY `0.7894737` | atlas torso |
| 1 | hips | `HipsA` | atlas hips |
| 2 | tail | `Futile_White`, 15 vertices/13 triangles | point-rasterized continuous tail mesh |
| 3 | head | `HeadA0..17`, Saint는 `HeadB0..17` | atlas head |
| 4 | legs | `LegsA*`, anchorY `0.25` | movement-selected legs |
| 5–6 | arms | `PlayerArm0..12`, anchorX `0.9` | 두 `Limb`의 보간 위치 |
| 7–8 | terrain hands | `OnTopOfTerrainHand*` | 현재 데스크톱 동작에서는 arm path로 통합 |
| 9 | face | `Face*` | 원작 movement/attention face resolver |
| 10 | mark light | `Futile_White` | 캠페인 mark 상태가 없어 비활성 |
| 11 | mark pixel | `pixel` | 캠페인 mark 상태가 없어 비활성 |

MSC의 마지막 gown 예약 슬롯은 cloak story state가 없는 데스크톱 펫에서는 만들지 않는다. 따라서 디버그의 `Base Sprite Count`는 실제 공통 인덱스 `0..11`인 12이며 `Extra Sprite Count`는 활성 프로필 전용 할당만 센다.

## White / Yellow / Red / Gourmand

**Original Slugcat ID:** `White`, `Yellow`, `Red`, `Gourmand` 각각이 독립
`SlugcatId`와 `SlugcatProfile`을 갖는다. `SlugcatVariant`는 이전 호출자와
V2 프리셋을 위한 호환 경로에서만 유지한다.

**PlayerGraphics branches:** 공통 `HeadA`, `FaceA`/blink `FaceB`, `FaceStunned`, `FaceDead` 경로다.

**Sprites:** `BodyA`, `HipsA`, `HeadA0..17`, movement별 `LegsA*`, `PlayerArm0..12`, `FaceA0..8`/`FaceB0..8`.

**Extra graphics:** 없음.

**Tail:** `DefaultTail`; 반경 `6, 4, 2.5, 1`, 길이 `4, 7, 7, 7`, root radius `6`.

**Face logic:** sleep/crawl/stand/air/wall/beam/attention 상태를 기존 원작 face resolver에 통과시킨다. unconscious/dead는 각각 `FaceStunned`/`FaceDead`다.

**Draw order:** body → hips → tail → head → legs → arms → face.

**Desktop implementation:** White/Yellow/Red/Gourmand의 원본 색, 무게,
이동/투척 통계를 각 프로필에 보존한다. Gourmand는 추가로
원본 피로·구르기·벨리 슬라이드 controller를 사용한다.

## Artificer / 기술병

**Original Slugcat ID:** `Artificer`.

**PlayerGraphics branches:** 정상 눈 scale이 양수면 `FaceC`, 음수면 `FaceD`; blink는 공유 `FaceB`; unconscious/dead는 DLL에서 공유하는 `FaceStunned`/`FaceDead`다.

**Sprites:** 공통 body/hips/head/arms/legs 외 `FaceC0..8`, `FaceD0..8`, `MushroomA`.

**Extra graphics:** `ArtificerScar`, 원작 index 12. `MushroomA`를 face 뒤에 둔다. `FaceC<n>`은 `scaleX=1-n/8`, `x=face.x+3+4n/8`; `FaceD<n>`은 `x=face.x+3(1-n/8)`; 그 외 `x=face.x+3`. 원작 Y-up `face.y+3`은 데스크톱 Y-down에서 `face.y-3`이다. 색은 `#45283C`.

**Tail:** `DefaultTail`.

**Face logic:** normal `FaceC/D`, blink `FaceB`, stunned/dead 공유 특수 element.

**Draw order:** body → hips → tail → head → legs → arms → scar(index 12) → face(index 9). 이는 `AddToContainer`가 index 9를 넣기 직전에 scar를 추가하는 순서와 같다.

**Desktop implementation:** `DefaultSlugcatColor(Artificer)=(0.43922,0.13725,0.23529)`를 반올림한 `#70233C`, 흰 눈, 독립 scar extension을 사용한다. 폭발 점프/패리/과열 분기는 `ArtificerAbilityController`가 담당한다.

## SpearMaster / 창술가

**Original Slugcat ID:** enum 필드명은 `Spear`, 등록 문자열도 `Spear`다.

**PlayerGraphics branches:** torso/hips X scale `0.76`, head X scale에 `0.85`를 곱하고 arm shoulder의 좌우 폭에 `0.6`을 곱한다.

**Sprites:** 공통 세트, `tinyStar` 15개, `BioSpear1..3` 중 하나, story cosmetic pearl의 `JetFishEyeA`/`Futile_White`/`BodyPearl`.

**Extra graphics:** `TailSpeckles` index `12..27`(15 `tinyStar` + 1 `BioSpear`)와 `CosmeticPearl` index `28..30`. needle 생성 중에는 선택 speckle 위치에서 `BioSpear1..3`가 progress에 따라 자라고, progress 0에서만 0 scale/비표시다. Pearl은 원작 constructor의 `visible=false`, `globalAlpha=0`, `scarVisible=false` 상태를 유지한다.

각 speckle row는 `s=Lerp(0.4,0.95,Pow(row/4,0.8))`에서 원작 `SpinePosition`을 샘플하고, 교차된 세 line을 `tinyStar`로 그린다. 색은 body에서 `Lerp(white,body,0.3)` 쪽으로 row별 보간한다.

**Tail:** `SpearmasterTail`; 물리 segment 반경 `8, 6, 4, 2`, 길이 `4, 7, 7, 7`. `DrawSprites`의 별도 `num4=6` 때문에 렌더 mesh root radius는 `6`(지름 12)이다. topology는 공통 15-vertex/13-triangle continuous mesh이며 개별 tail sprite로 바꾸지 않는다.

**Face logic:** `FaceA`/blink `FaceB`, unconscious/dead 공유 `FaceStunned`/`FaceDead`.

**Draw order:** body → hips → (비활성 pearl) → tail → tail speckles → head → legs → arms → face.

**Desktop implementation:** 기본색 `#4F2E69`, 흰 눈, 전용 비율과 tail profile 및 speckle extension을 사용한다. 원본 progress/speckle/hand target을 따르는 needle 추출·파지·투척은 `SpearmasterAbilityController`가 담당한다.

## Rivulet / 물살이

**Original Slugcat ID:** `Rivulet`.

**PlayerGraphics branches:** `PlayerGraphics.AxolotlGills`가 index 12에서 시작한다. `AxolotlScale : BodyPart` 6개와 base/effect sprite 12개를 별도로 관리한다. 머리 texture에 합쳐진 장식이 아니다.

**Sprites:** 공통 세트와 `LizardScaleA3` index `12..17`, `LizardScaleB3` index `18..23`. A는 body color, B는 effect color다.

**Extra graphics:** `AxolotlGills`, 머리 양쪽 각 3개. constructor 상수는 다음과 같다.

| Row | scalesPosition.y | side magnitude | length factor | backwards factor |
|---:|---:|---:|---:|---:|
| 0 | 0.03570603 | 0.659981 | 0.9722961 | 0.3644831 |
| 1 | 0.02899241 | 0.76459 | 0.6056554 | 0.9129724 |
| 2 | 0.02639332 | 0.7482835 | 0.7223744 | 0.4567381 |

`rigor=0.5873646`, multiplier `1.310689`, `length=Lerp(2.5,15,multiplier*factor)`, `width=Lerp(0.65,1.2,0.1542603*multiplier)`, `backwards=0.1759363*factor`를 사용한다.

각 40 Hz update에서 body chunk 0의 X를 좌/우 `5`만큼 옮긴 connection을 만들고, row의 30도 간격 기본 방향, body 방향, `lookDirection`, backwards factor로 target을 계산한다. target에서 길이 절반 이상 벗어나면 pos/vel을 같은 양만큼 당기고, clamp magnitude 10의 target force, rigor damping, `ConnectToPoint(root,length,push:true)`를 차례로 적용한다. 로컬 DLL의 `BodyPart.Update()`는 빈 메서드이며 `AxolotlScale.Update()`가 air damping `0.9`, `lastPos=pos`, `pos+=vel`의 단일 적분을 담당한다.

**Tail:** `DefaultTail`.

**Face logic:** `FaceA`/blink `FaceB`, unconscious/dead 공유 특수 face. Gill AI가 mouse를 직접 보지 않고 기존 look/head/body 상태의 결과만 받는다.

**Draw order:** base PlayerGraphics 뒤에 A 6개와 B 6개를 Midground에 append하므로 모두 face 앞이다. 좌측은 `scaleX` 음수, 우측은 rotation에 180도를 더한다. anchorY `0.1`, scaleY `length / LizardScaleA3.sourcePixelHeight`다.

**Desktop implementation:** `RivuletGillsExtension`이 6개 독립 BodyPart를 40 Hz에서 update하고, head와 gill control point 모두 같은 `timeStacker`로 보간한다. 기본색 `#91CCF0`, effect `#DF2DEA`, 어두운 눈 `#101010`을 쓴다.

## Saint / 성자

**Original Slugcat ID:** `Saint`.

**PlayerGraphics branches:** 정상 머리는 공통 `HeadA`가 아니라 `HeadB0..17`이다. `DefaultFaceSprite`는 `SaintFaceCondition()` 때문에 일반 플레이 중 항상 눈을 감은 `FaceB0..8`을 선택하고, unconscious/dead는 공유 특수 face를 쓴다.

**Sprites:** 공통 세트에서 head만 `HeadB`로 교체된다. 원작은 추가로 tongue rope mesh, god pips, tentacle/ascension sprite를 할당한다.

**Extra graphics:** tongue mesh는 `player.tongue.Free || Attached`일 때만 visible이고, ascension 계열도 해당 gameplay state가 있어야 활성화된다. 데스크톱 스킨에는 tongue/ascension gameplay를 만들지 않으므로 이 슬롯은 생성하지 않고 디버그에 inactive 이유만 표시한다.

**Tail:** `DefaultTail`.

**Face logic:** 데스크톱의 활성 Saint는 원작의 `player.room != null`, 비승천 상태에 대응하므로 정상 상태에서 항상 `FaceB`; stunned/dead에서는 공유 특수 element를 사용한다. 원작 DLL의 승천 분기만 `killWait < 0.02`일 때 이 조건을 잠시 해제하지만, 데스크톱에는 승천 gameplay가 없다.

**Draw order:** 활성 normal 외형은 공통 순서이며 head index 3만 `HeadB`다.

**Desktop implementation:** 기본색 `#AAF156`, 어두운 눈 `#101010`, `HeadB` 머리와 닫힌 눈 `FaceB` family를 사용한다. 일반 tongue 발사·부착·스윙·해제와 20개 rope segment는 `SaintAbilityController`/`DesktopRope`가 담당하며, story ascension/karma 분기만 데스크톱 세션에 없다.

## 런타임 전환과 안전성

트레이, 설정 창, 스킨 편집기, `--slugcat`은 모두 같은
`SlugcatProfiles.All`의 White/Yellow/Red/Gourmand/Artificer/SpearMaster/Rivulet/Saint
8종을 선택한다. 전환은 stats, 능력 controller, audio, graphics를 하나의
profile로 같이 바꾸고 체형에 맞게 `BodyChunk` 질량을 갱신한다. 위치/속도,
AI, stun, terrain state는 보존하되 이전 캐릭터의 effect, spear, tongue/rope,
대기 SoundEvent, 전용 graphics extension은 즉시 정리한다. 같은 4-segment topology에서
tail control point의 pos/lastPos/vel은 승계한다.

트레이 메뉴의 `Debug Overlay`에는 skin, 원작 ID, profile, base/extra sprite count, face, tail profile, extension 목록과 각 extra part의 last/current/render position, element, rotation, layer를 표시한다. Rivulet은 connection-control-target wire도 함께 그린다.
