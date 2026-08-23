# Rain World DLL 정적 분석 결과

이 문서는 로컬 Rain World 설치본의 `Assembly-CSharp.dll`과 직접 관련된 관리 DLL을
**게임을 실행하지 않고** 정적 분석한 결과다. 데스크톱 펫이 원작의 물리·자세·절차형
그래픽을 재현할 때 필요한 값과 호출 순서를 우선해서 기록한다.

## 1. 조사 범위, 안전성, 재현 기준

- 설치 루트:
  `C:\Program Files (x86)\Steam\steamapps\common\Rain World`
- 게임 버전:
  `RainWorld_Data\StreamingAssets\GameVersion.txt` = `v1.11.8`
- Steam manifest build id: `22785462`
- Unity 플레이어 버전:
  `2020.3.45f1 (660cd1701bd5)`
- 주 분석 대상:
  `RainWorld_Data\Managed\Assembly-CSharp.dll`
- 분석 방식: PE/CLR metadata 열람, ILSpyCmd 11.0 정적 디컴파일, 전체 IL 덤프,
  메서드 토큰/RVA와 핵심 상수의 IL 재대조
- 분석 도구와 산출 중간물은 gitignored `.tools/` 아래에만 두었다.

다음 동작은 하지 않았다.

- `RainWorld.exe`, Unity Editor, BepInEx loader를 실행하지 않았다.
- DLL을 `Assembly.Load*`로 로드하거나 정적 생성자를 실행하지 않았다.
- 게임 자산이나 DLL을 저장소에 복사하지 않았다.
- 원본 설치 파일을 수정하지 않았다.

현재 설치본에는 `BepInEx\plugins\HOOKS-Assembly-CSharp.dll`이 있다. 따라서 실제 게임을
실행하면 detour가 원본 메서드 동작을 바꿀 가능성이 있다. 이 문서는 그 플러그인을
실행하지 않았으며, 아래 동작 설명은 현재 `Assembly-CSharp.dll`의 정적 메서드 본문을
기준으로 한다.

### 파일 식별값

| 파일 | 크기(byte) | SHA-256 |
|---|---:|---|
| `Assembly-CSharp.dll` | 9,064,960 | `B6BE1D4E18CE219D21091B51564CB6A11C1E4106B41DE903EB8E58849CB16FDB` |
| `Assembly-CSharp-firstpass.dll` | 206,848 | `7180DA54E10BCFF9E068D04FC067F5A5225029D147120DF24814DAB909B442DB` |
| `UnityEngine.CoreModule.dll` | 1,167,384 | `3C97D6E8D9D9F2F26928FD1C5C2EC7752BD1394F744D58BCE59D4E45A1D09B55` |
| `UnityEngine.InputLegacyModule.dll` | 36,376 | `9D484E4282721E09FF55F021EFF146DC7D5C060060C43478A90A21EBDEFB2A1A` |
| `Rewired_Core.dll` | 1,939,968 | `9AD00D16D2330180DD0DACAE1CC56634B7DE7F784C2B4901E7CECE95F3DEE33B` |
| `Rewired.Runtime.dll` | 186,368 | `58A01BCC7358BC88BDF83D4192968B55DF05822F5C32C0FBF2726ACB9C8E6962` |
| `GoKit.dll` | 52,736 | `74644F1474244BFBD49C22C2AB7BEF491CBF43D03F4842BABA33BD5B7FE06176` |
| `HOOKS-Assembly-CSharp.dll` | 14,526,464 | `233BF3687919F125D248C75909C6795D82CBFE8E823DFAF1205EBC84473351EF` |

`Assembly-CSharp.dll`의 assembly version은 `0.0.0.0`, runtime version은
`v4.0.30319`, MVID는 `90dcf104-fbdf-4d42-b147-5089d56af681`이다. PE 플래그는
`I386`, `ILOnly`다. 이후 게임 업데이트 시 위 해시 또는 MVID가 다르면 상수와 IL
오프셋을 다시 검증해야 한다.

## 2. 가장 중요한 결론

1. 원작 플레이어 물리는 Unity `Rigidbody2D`/`Collider2D`가 아니다.
   `BodyChunk`, `PhysicalObject.BodyChunkConnection`, `Room` tile query,
   `SharedPhysics`로 구성된 자체 2D solver다. 이 assembly는
   `UnityEngine.Physics2DModule`을 직접 참조하지 않는다.
2. 정상 게임플레이의 논리 tick은 기본 **40 Hz**, 즉 tick당 `0.025 s`다.
   `timeStacker`는 물리 delta가 아니라 직전/현재 tick 상태 사이의 **렌더 보간값**이다.
3. Player는 반지름 9와 8인 두 `BodyChunk`를 거리 17의 constraint로 연결한다.
   자세와 이동은 두 점의 접촉 상태, 위치, 속도를 매 tick 바꾸는 방식이다.
4. 보이는 몸은 full-frame 애니메이션만 재생하는 구조가 아니다. 몸통·골반·머리·꼬리·
   손·다리를 물리/절차형 좌표로 계산하고, 머리/얼굴/팔/다리만 제한된 atlas element를
   상태에 맞춰 고르는 하이브리드 렌더러다.
5. `CreatureGraphics`라는 타입은 현재 assembly에 **존재하지 않는다**.
   공통 기반은 `GraphicsModule`이며 `PlayerGraphics`, `LizardGraphics` 등 구체 타입이
   직접 파생된다.
6. Survivor, Monk, Hunter의 내부 키는 각각 `White`, `Yellow`, `Red`다. Gourmand는
   `MoreSlugcatsEnums.SlugcatStatsName.Gourmand`이며 `ModManager.MSC`가 켜진 경로다.
7. Gourmand의 큰 체형은 별도 `GourmandHead`/`GourmandBody` sprite가 아니라 공통
   `BodyA`, `HipsA`, `HeadA*`, 기본 꼬리를 사용하면서 몸통과 골반의 `scaleX`, 그리고
   `bodyWeightFac=1.35`를 달리하는 방식이다.
8. 일반 Player는 사람 입력을 받는다. 원작에서 데스크톱 펫에 그대로 재사용할 수 있는
   “Survivor AI”는 없다. `SlugNPC`는 별도의 `SlugNPCAI` 경로이므로, 데스크톱 행동기는
   원작의 `InputPackage`에 해당하는 가상 입력을 생성하는 편이 구조적으로 가장 가깝다.

## 3. 핵심 타입 인벤토리

아래 field/method 수는 해당 TypeDef가 직접 소유한 개수이며 상속 멤버는 제외한다.

| 타입 | TypeDef token | 기반 타입 | 직접 field / method |
|---|---|---|---:|
| `RainWorldGame` | `0x02000114` | `MainLoopProcess` | 60 / 84 |
| `Room` | `0x02000116` | `UpdatableAndDeletable` 계열 | 114 / 167 |
| `BodyChunk` | `0x0200012F` | `object` | 30 / 25 |
| `BodyPart` | `0x02000131` | `object` | 9 / 7 |
| `Creature` | `0x02000133` | `PhysicalObject` | 37 / 70 |
| `GenericBodyPart` | `0x02000135` | `BodyPart` | 1 / 2 |
| `Limb` | `0x02000138` | `BodyPart` | 12 / 8 |
| `PhysicalObject` | `0x02000139` | `UpdatableAndDeletable` | 22 / 61 |
| `TailSegment` | `0x0200013F` | `BodyPart` | 6 / 3 |
| `Player` | `0x02000145` | `Creature` | 295 / 219 |
| `MainLoopProcess` | `0x0200014E` | `object` | 6 / 12 |
| `GraphicsModule` | `0x02000178` | `object` | 13 / 26 |
| `RoomCamera` | `0x0200017C` | `object` | 111 / 105 |
| `PlayerGraphics` | `0x02000257` | `GraphicsModule` | 68 / 48 |
| `SlugcatStats` | `0x0200025B` | `object` | 20 / 26 |
| `AbstractCreature` | `0x020002C2` | `AbstractWorldEntity` | 25 / 37 |
| `CreatureTemplate` | `0x020002C8` | `object` | 75 / 18 |
| `PhysicalObject.BodyChunkConnection` | `0x02000729` | `object` | 7 / 2 |
| `Player.AnimationIndex` | `0x0200073A` | `ExtEnum<AnimationIndex>` | 26 / 2 |
| `Player.BodyModeIndex` | `0x0200073B` | `ExtEnum<BodyModeIndex>` | 11 / 2 |

## 4. 40 Hz update와 `timeStacker`

### 4.1 호출 경로

기본 호출 경로는 다음과 같다.

```text
Unity frame
  RainWorld.Update()
    ProcessManager.Update(Time.deltaTime)
      currentMainLoop.RawUpdate(deltaTime)
        0..3 × logical Update()
        1 × GrafUpdate(myTimeStacker)
```

`MainLoopProcess.framesPerSecond`의 기본값은 40이고 `TimeSpeedFac`는
`framesPerSecond / 40f`다. `RawUpdate`의 의미를 그대로 쓰면 다음과 같다.

```csharp
myTimeStacker += dt * framesPerSecond;
int updates = 0;
while (myTimeStacker > 1f)
{
    Update();
    myTimeStacker -= 1f;
    updates++;
    if (updates > 2)
        myTimeStacker = 0f;
    if (myTimeStacker > 1f)
        RunRewiredUpdate();
}
GrafUpdate(myTimeStacker);
```

중요한 세부 사항은 다음과 같다.

- 비교는 `>=`가 아니라 엄격한 `> 1f`다.
- 한 Unity frame에서 논리 `Update`를 최대 3회 수행한 뒤 남은 backlog를 버린다.
- `GrafUpdate`는 매 frame 한 번 호출하고 `myTimeStacker`를 보간 알파로 넘긴다.
- `RainWorldGame.RawUpdate`는 일반적으로 fps를 40으로 되돌리지만 adrenaline, illness,
  pause/특수 장면, 개발 키 등에서 목표 fps를 낮추거나 바꾸는 분기가 있다. 데스크톱
  재현의 정상 기준값은 40이다.

IL 근거는 `MainLoopProcess.RawUpdate` token `0x06000DC2`, RVA `0x000D3D58`,
code size 124다. 누산은 IL `0000..0011`, 논리 update는 `001A`, 1 감소는
`0027`, 3회 cap은 `0036..0040`, loop의 `bgt`는 `0068..006D`, 렌더 호출은
`006F..0076`에 있다. `RainWorld.Update`는 IL `03ED`에서 `Time.deltaTime`을 읽어
`ProcessManager.Update`에 넘긴다.

### 4.2 논리 tick 내부 순서

정상적인 room/player 경로를 축약하면 다음 순서다.

```text
RainWorldGame.Update
  RoomCamera.Update
  Room.Update
    Player.Update
      checkInput / 상태 카운터
      Creature.Update
        PhysicalObject.Update
          각 BodyChunk.Update       // 중력, 적분, terrain collision
          각 BodyChunkConnection.Update
      Player.MovementUpdate         // 자세 판정과 다음 적분에 쓸 힘/속도
    PlayerGraphics.Update           // 머리, 꼬리, 손, 다리 절차형 simulation
    physical-object pair collision
```

즉 `Player.MovementUpdate`가 더한 속도는 같은 tick의 chunk 위치 적분이 끝난 뒤에
적용되며, 주효과는 다음 tick의 적분에 나타난다. 이 순서를 바꾸면 점프 반응, 지면
가속, constraint의 느낌이 눈에 띄게 달라진다.

`Room.Update`는 `updateList`를 역순회하고 각 객체의 `Update(eu)`를 부른 뒤,
`PhysicalObject.graphicsModule.Update()`와 `GraphicsModuleUpdated`를 처리한다. 그 후
physical object 쌍의 chunk overlap을 해결한다.

### 4.3 렌더 보간

`RainWorldGame.GrafUpdate`는 `RoomCamera.DrawUpdate(timeStacker, ...)`로 값을 전달한다.
카메라 위치는 `Lerp(lastPos, pos, timeStacker)`이고, 각 `SpriteLeaser`도 동일한 알파를
받는다. `PlayerGraphics.DrawSprites`는 다음 상태를 보간한다.

- 몸 chunk: `Lerp(drawPositions[i,1], drawPositions[i,0], timeStacker)`
- 머리: `Lerp(head.lastPos, head.pos, timeStacker)`
- 꼬리/손/다리: 각 body part의 `lastPos -> pos`

따라서 desktop renderer도 고정 40 Hz simulation 상태를 두 벌 보관하고, 표시 refresh
rate에서는 같은 방식으로 보간해야 한다. frame delta를 물리 수식에 직접 곱하는 것은
원작과 다르다.

### 4.4 단위

원작의 속도와 가속도 상수는 각각 pixel/tick, pixel/tick² 단위로 해석하는 것이 가장
안전하다.

```text
v_pixels_per_second = v_per_tick * 40
a_pixels_per_second_squared = a_per_tick2 * 40²
```

예를 들어 `gravity=0.9`는 환산하면 `1440 px/s²`다. 매 tick 마찰 `f`는 1초 뒤
`f^40`이 된다. `airFriction=0.999`이면 1초 뒤 약 `0.96077`이다. 포팅 시 내부 계산은
원작 tick 단위를 그대로 유지하고 Windows 좌표계 경계에서만 배율을 적용하는 편이
오차가 적다.

## 5. 네 캐릭터의 내부 이름, 기본색, 체형 상수

### 5.1 내부 이름과 `SlugcatStats`

`SlugcatStats.Name` 자체도 CLR enum이 아니라 `ExtEnum<Name>`다. base game 정적 값은
`White`, `Yellow`, `Red`, `Night`이고, Gourmand는 DLC 쪽
`MoreSlugcatsEnums.SlugcatStatsName.Gourmand`다. DLC registration IL은 문자열
`"Gourmand"`와 `register=true`로 `new SlugcatStats.Name(...)`을 만든다.

| 표시 캐릭터 | 내부 `Name` | food max / hibernate | run | weight | throw | pole | corridor | 정상 총질량 / chunk당 |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| Survivor | `SlugcatStats.Name.White` | 7 / 4 | 1.0 | 1.00 | 1 | 1.00 | 1.00 | 0.700 / 0.350 |
| Monk | `SlugcatStats.Name.Yellow` | 5 / 3 | 1.0 | 0.95 | 0 | 1.00 | 1.00 | 0.665 / 0.3325 |
| Hunter | `SlugcatStats.Name.Red` | 9 / 6 | 1.2 | 1.12 | 2 | 1.25 | 1.20 | 0.784 / 0.392 |
| Gourmand | `MoreSlugcatsEnums.SlugcatStatsName.Gourmand` | 11 / 7 | 1.0 | 1.35 | 2 | 0.80 | 0.86 | 0.945 / 0.4725 |

총질량 계산은 Player ctor의 `0.7f * slugcatStats.bodyWeightFac`이며 이를 두 chunk에
반씩 준다. Monk는 추가로 `loudnessFac=.75`, `generalVisibilityBonus=-.1`,
`visualStealthInSneakMode=.6`; Hunter는 각각 `1.35`, `.1`, `.3`; Gourmand는
`1.5`, `.3`, `.2`다. Monk의 `lungsFac`는 MMF Monk breath 옵션이 켜지면 `.8`,
그 외에는 `1.2`다.

malnourished 공통 경로는 throwing skill을 우선 0으로 만들고 일반 캐릭터의
`bodyWeightFac`을 최대 `.9`, run `.875`, pole `.8`, corridor `.86`으로 제한한다.
Gourmand는 예외 분기로 weight `1.15`, run `.875`, pole `.75`, corridor `.81`,
throwing skill `2`가 된다. Expedition agility unlock은 이 값들을 다시 덮을 수 있으므로
순수 캐릭터 프로필과 expedition modifier를 분리해야 한다.

`GetInitialSlugcatClass`는 NPC이면 `Slugpup`, Jolly story이면 player option의 class 또는
story character, 일반 story이면 `playerState.slugcatCharacter`, 그 외 일부 모드이면
`slugcatStats.name`을 고른다.

### 5.2 정확한 기본색과 결정 경로

네 기본색은 HSL 변환이 아니라 `PlayerGraphics.DefaultSlugcatColor`의 직접 RGB
constructor다.

| 캐릭터 | DLL의 normalized RGB | 8-bit RGB / hex |
|---|---|---|
| Survivor | `(1, 1, 1)` | `(255,255,255)` / `#FFFFFF` |
| Monk | `(1, 1, 23/51)` = `(1,1,0.4509804)` | `(255,255,115)` / `#FFFF73` |
| Hunter | `(1, 23/51, 23/51)` | `(255,115,115)` / `#FF7373` |
| Gourmand | `(0.94118, 0.75686, 0.59216)` | `(240,193,151)` / `#F0C197` |

Gourmand 분기는 `ModManager.MSC`가 참일 때만 평가된다. `SlugcatColor(i)`의 실제 우선
순서는 다음과 같다.

1. Jolly/coop이고 AUTO의 보조 player 또는 CUSTOM mode이면 `JollyColor(player, 0)`.
2. Jolly player class override가 있으면 `i`를 그 class로 교체.
3. `CustomColorsEnabled()`이면 `CustomColorSafety(0)`.
4. 그 외 `DefaultSlugcatColor(i)`.

`PlayerGraphics.CharacterForColor`는 arena default colors이면 `player.SlugCatClass`,
아니면 `playerState.slugcatCharacter`를 반환한다. `ApplyPalette`는 여기서 얻은 색에 다음
동적 효과를 더한다.

- malnourished이면 `Color.Lerp(base, gray, 0.4 * malnourishedAmount)`
- poison과 hypothermia blend
- body/hips/tail/head/legs/arms/hands 등 face index 9를 제외한 기본 sprite에 body color
- mark/flat-light에는 별도 원색/white blend

따라서 설정에서 “원작 기본색”을 선택했을 때만 위 hex를 그대로 사용하고, custom/Jolly
색상은 별도 사용자 프로필로 취급해야 한다. IL에서 White는 `000D..0021`, Yellow는
`002F..0043`, Red는 `0051..0065`, Gourmand는 `0172..0186`이다.
`DefaultSlugcatColor`는 token `0x06001C2C`, RVA `0x0021FAEC`, code size 412다.

### 5.3 Gourmand의 정확한 visual 차이

Gourmand도 sprite 0=`BodyA`, 1=`HipsA`, 3=`HeadA0`, 4=`LegsA0`으로 시작한다.
Draw 단계에서 head는 공통 `_cachedHeads[0, view]`, 즉 `HeadA0..HeadA17`을 사용한다.
Gourmand 전용 head element나 head scale 분기는 없다. 꼬리도 정상 성체 기본 4-segment
구성을 그대로 쓴다. 별도 cosmetic sprite도 추가하지 않는다.

큰 체형은 `DrawSprites`의 두 가로 배율로 만든다. 아래에서 `b`는 보간된 호흡 위상
`0.5 + 0.5*sin(2π*breath)`, `u`는 몸 축이 수직에 가까운 정도
`InverseLerp(.3,.5,abs(bodyDirection.y))`, `m`은 malnourished 0..1, `s`는
`sleepCurlUp`이다.

```text
BodyA.scaleX = 1.4
  + Lerp(Lerp(Lerp(-0.05, -0.15, m), 0.05, b) * u, 0.15, s)

HipsA.scaleX = 1.6 + 0.2*s + 0.05*b - 0.05*m
HipsA.scaleY = 1.0 + 0.2*s
```

일반 성체의 기준은 동일한 동적 항에 `BodyA.scaleX=1.0+...`,
`HipsA.scaleX=1.0+...`다. 즉 정상 awake Gourmand는 몸통 기준 폭이 약 1.4배,
골반이 약 1.6배지만 머리, 팔, 다리 element 자체는 공통이다. 물리 충돌 반지름도
9/8로 다른 캐릭터와 같고, 큰 물리적 존재감은 주로 chunk 질량 `.4725 + .4725`와
class mechanics에서 온다.

`ClassMechanicsGourmand`는 `aerobicLevel >= .95`이면 `gourmandExhausted=true`,
`.4` 미만이면 false로 되돌린다. exhausted 중에는
`slowMovementStun=max(current, LerpMap(aerobic,.7,.4,6,0))`이고
`lungsExhausted=true`다. PlayerGraphics는 lungs exhausted일 때 머리와 몸에 큰 호흡
sinusoid를 추가하고 blink를 1로 고정하므로, 체형뿐 아니라 피로 pose도 Gourmand
인상에 중요하다.

## 6. 물리 객체와 핵심 수식

### 6.1 `PhysicalObject`

주요 필드는 다음과 같다.

- `bodyChunks[]`, `bodyChunkConnections[]`, `graphicsModule`
- `airFriction`, `gravity`, `bounce`, `surfaceFriction`
- `waterFriction`, `buoyancy`, `waterRetardationImmunity`
- `collisionLayer`, `collisionRange`, `grabbedBy`, `appendages`

`PhysicalObject.Update`는 날씨 관성 처리 후 각 chunk의 `Update`, abstract tile 갱신,
각 connection의 `Update`, grasp 정리, 기반 update, appendage update를 수행한다.
Player의 두 chunk가 먼저 각각 terrain과 충돌한 뒤 두 chunk 사이 거리 constraint가
해결된다는 점이 중요하다.

### 6.2 `BodyChunk`

핵심 상태는 `pos`, `lastPos`, `lastLastPos`, `setPos`, `vel`, `rad`, `mass`,
`contactPoint`, `lastContactPoint`, `onSlope`, `slopeRad`, `submersion`,
`terrainCurveNormal`, `goThroughFloors`, `collideWithTerrain`, `rotationChunk`다.

tick 적분의 기본 경로는 다음과 같다.

```text
vel.y -= owner.gravity

if in water:
  vel.y += owner.buoyancy * EffectiveRoomGravity * submersion
  waterDrag = Lerp(
      owner.waterFriction * waterRetardationImmunity,
      owner.waterFriction,
      Pow(1 / Max(1, |vel| - 10), 0.5))
  vel *= Lerp(owner.airFriction, waterDrag, submersion)
else:
  vel *= owner.airFriction

lastLastPos = lastPos
lastPos = pos
pos = setPos ?? (pos + vel)
vertical collision -> slope collision -> horizontal collision
```

수면을 매우 빠르게 스치는 별도 분기는 `vel.x > vel.y*5`, `abs(vel.x)>10`,
`vel.y<0`, `submersion<.5`일 때 `vel.y*=-.5`, `vel.x*=.75`를 적용한다.

terrain은 20 px tile grid에 대한 swept collision이다. 축 충돌 때 normal 성분을
`bounce`로 반사한 뒤 다음 임계치 미만이면 0으로 만든다.

```text
bounceStopThreshold = 1 + 9*(1-bounce)
tangentVelocity *= Clamp(surfaceFriction*2, 0, 1)
```

Player의 `bounce=.1`이면 임계치가 `9.1 px/tick`이므로 작은 착지는 튀지 않고 normal
속도가 0이 된다. 바닥은 반사된 `vel.y < gravity`인 경우도 0으로 만든다.

slope 바닥에서는 다음 항을 쓴다.

```text
vel.x *= 1 - surfaceFriction
vel.x += abs(vel.y) * Clamp(0.5-surfaceFriction, 0, 0.5)
         * slopeDirection * 0.2
vel.y = 0
```

Player의 `surfaceFriction=.5`에서는 두 번째 slope 가속항이 0이다.

### 6.3 `PhysicalObject.BodyChunkConnection`

필드는 `chunk1`, `chunk2`, `distance`, `elasticity`, `weightSymmetry`, `active`,
`type`이다. `Type` 역시 `ExtEnum`이며 `Normal`, `Pull`, `Push`가 있다.

`weightSymmetry == -1`이면 자동으로
`chunk2.mass / (chunk1.mass + chunk2.mass)`를 쓴다. 거리를 `d`, chunk1에서 chunk2로
향하는 단위벡터를 `u`, rest length를 `r`, symmetry를 `w`, elasticity를 `e`라 하면:

```text
c1 = u * (r-d) * w     * e
c2 = u * (r-d) * (1-w) * e

chunk1.pos -= c1; chunk1.vel -= c1
chunk2.pos += c2; chunk2.vel += c2
```

`Normal`은 항상, `Pull`은 `d>r`일 때만, `Push`는 `d<r`일 때만 푼다. 위치 보정량을
속도에도 동일하게 더하는 것이 특징이다. Player는 `r=17`, `w=.5`, `e=1`이므로 오차를
반씩 나눈다. roll에서는 Player가 rest distance를 10으로 줄이고, 일부 corridor turn은
connection type을 Pull로 바꾼다. 일반 상태는 17과 Normal로 되돌린다.

### 6.4 객체 간 충돌

`Room`은 terrain update가 끝난 뒤 같은 collision layer의 physical-object chunk 쌍을
검사한다. 두 원이 겹치면 질량 비율로 separation을 나누어 `pos`와 `vel` 양쪽에
보정하고, 한 쌍에 대해 `Collide` callback을 호출한다. 데스크톱 경계/아이콘 충돌을
추가하더라도 chunk 간 원 충돌과 terrain/boundary 충돌의 단계를 섞지 않는 편이 좋다.

## 7. `Player`의 몸, 상태, 입력, 이동

### 7.1 constructor 상수

Player ctor의 기본 물리값은 다음과 같다.

```text
customPlayerGravity = 0.9
totalMass = 0.7 * slugcatStats.bodyWeightFac
chunk[0] = radius 9, mass totalMass/2
chunk[1] = radius 8, mass totalMass/2
connection = rest 17, Normal, elasticity 1, symmetry 0.5
input history length = 10
animation = None
bodyMode = Default
airFriction = 0.999
gravity = 0.9
bounce = 0.1
surfaceFriction = 0.5
collisionLayer = 1
waterFriction = 0.96
buoyancy = 0.95
```

IL의 주요 literal은 ctor token `0x06000CDC`에서 gravity `00C1`, 질량 계수 `.7`은
`01ED`, radius 9/8은 `0223`/`024D`, rest 17은 `0282`, elasticity 1과 symmetry .5는
`028C`/`0291`, 마찰·중력·bounce·surface·water·buoyancy는
`036C`/`0377`/`0382`/`038D`/`039F`/`03AA`다.

### 7.2 상태 축

`AnimationIndex`와 `BodyModeIndex`는 C# enum이 아니라 등록 가능한 `ExtEnum<T>`다.
두 값은 하나의 합쳐진 state가 아니라 서로 직교하는 축이다.

`AnimationIndex`의 현재 정적 값:

```text
None, CrawlTurn, StandUp, DownOnFours, LedgeCrawl, LedgeGrab,
HangFromBeam, GetUpOnBeam, StandOnBeam, ClimbOnBeam, GetUpToBeamTip,
HangUnderVerticalBeam, BeamTip, CorridorTurn, SurfaceSwim, DeepSwim,
Roll, Flip, RocketJump, BellySlide, AntlerClimb, GrapplingSwing,
ZeroGSwim, ZeroGPoleGrab, VineGrab, Dead
```

`BodyModeIndex`의 현재 정적 값:

```text
Default, Crawl, Stand, CorridorClimb, ClimbIntoShortCut, WallClimb,
ClimbingOnBeam, Swimming, ZeroG, Stunned, Dead
```

예를 들어 `bodyMode=Default`이면서 `animation=Roll`일 수 있다. 데스크톱 구현에서도
자세/환경 mode와 일시 animation을 별도 필드로 유지해야 원작 분기를 옮기기 쉽다.

### 7.3 입력

`Player.InputPackage`의 핵심 필드는 `int x,y`, `bool jmp,thrw,pckp,mp,spec`,
`bool gamePad,crouchToggle`, `ControllerType`, `Vector2 analogueDir`,
`int downDiagonal`이다. Player는 10개 history를 한 칸씩 밀고 새 package를 0번에 넣는다.

입력 source 우선순위는 controller `GetInput`, AI가 있으면 `AI.Update`, 그 외
`RWInput.PlayerInput`이다. dead/stunned이면 zero package를 넣는다. 데스크톱 자율 행동을
`DesktopPetAI -> Virtual InputPackage -> Player movement port`로 연결하면 원작의
press/release 및 상태 전이를 유지할 수 있다.

일반 점프 예약은 **누르는 순간** `input[0].jmp && !input[1].jmp`에 생긴다. 즉 release
edge가 아니다. 즉시 뛸 수 없으면 `wantToJump=5`, 지상 접촉 중 `canJump=5`를 유지하고
둘 다 양수일 때 `Jump()`를 부른다. 예외적으로 crouched charge/super jump는
`!input[0].jmp && input[1].jmp`, 즉 버튼을 놓는 순간 `wantToJump=1`을 둔다.

### 7.4 `MovementUpdate`의 대표 상수

매 tick 접촉점과 주변 tile/beam을 읽어 body mode, standing, flip, foot pin을 추론한 뒤
`UpdateAnimation`, `UpdateBodyMode`를 호출한다. 대표 지상 값은 다음과 같다.

- Default + standing 목표속도: upper chunk `4.2 * runspeedFac`, lower `4.0 * runspeedFac`
- Default + non-standing: 보통 `4.0`, y 입력이 있으면 `2.5`
- Crawl: 보통 `2.5`, y 입력이 있으면 `1.0`; 뒤로 기는 방향에는 `.75` 계수
- 수평 acceleration cap의 기준 `2.4`; 기본 지면에서는
  `2.4*surfaceFriction = 1.2 px/tick`씩 목표속도에 접근
- 지면 접촉 시 추가 velocity lerp 계수 `surfaceFriction^1.5`, 기본 약 `.35355`
- idle stand의 lower chunk foot pin은 tile top을 기준으로 하고, 한쪽 인접 floor만 있으면
  `1.6*surfaceFriction`만큼 옆으로 보정
- connection은 일반 rest 17, roll rest 10, corridor turn의 일부에서 Pull

기본 standing jump는 upper `vel.y=4`, lower `vel.y=3`, `jumpBoost=8`이다.
non-standing/crouched jump는 upper 위치를 y +6 하고 lower y속도 +4, upper +3을 주며,
접촉/입력에 따라 추가 +3과 수평 1.5 또는 charged 9가 붙는다. jumpBoost가 남고 버튼을
누르는 동안 tick마다 `jumpBoost -= 1.5` 후 두 chunk에
`vel.y += (jumpBoost+1)*.3`을 더한다.

일반 wall jump는 upper/lower y속도 8/7, 벽 반대쪽 x속도 6/5다. 바닥 도움 분기는
y 8/7을 주고 위치를 위로 10 옮긴다. class/slugpup/expedition modifier 분기가 있으므로
이 값을 한 개의 전역 jump impulse로 합치면 안 된다.

`TerrainImpact`는 대표적으로 speed >12에서 roll 진입 가능성을 보고, 일반 캐릭터는
>35에서 stun, >60에서 치명 fall을 처리한다. Gourmand는 stun 40, death 80으로 더
높다. 데스크톱 펫에서는 death를 생략하더라도 착지 압축과 roll 전환 임계값은 원작
느낌에 유용하다.

## 8. 절차형 그래픽: `GraphicsModule`, body parts, `PlayerGraphics`

### 8.1 `GraphicsModule`

주요 필드는 `owner`, `bodyParts[]`, `culled`, `internalContainers`, `lightSource`,
`debugSprite` 계열이다. `Update`는 culling과 body-part simulation 기반을 제공하고,
`InitiateSprites`, `DrawSprites`, `ApplyPalette`, `AddToContainer`, `Reset`은 파생 타입이
구현한다. `Room`이 physical object's 논리 update 직후 graphics update를 직접 부른다.

### 8.2 `BodyPart.ConnectToPoint`

`BodyPart`는 `lastPos`, `pos`, `vel`, `rad`, surface/air friction, terrain contact를 갖는다.
연결 수식은 다음 순서다.

```text
if elasticMovement > 0:
    vel += Dir(pos,pnt) * Distance(pos,pnt) * elasticMovement

vel += hostVel * exaggerateVel

if push || Distance(pos,pnt) >= connectionRad:
    correction = Dir(pos,pnt) * (connectionRad - distance)
    pos -= correction
    vel -= correction

vel -= hostVel
vel *= 1 - adaptVel
vel += hostVel
```

마지막 세 줄은 host velocity 기준 상대속도를 감쇠한다. `PushOutOfTerrain`은 3x3 tile,
slope, 선택적 custom terrain을 검사하여 body part를 밖으로 밀어낸다.

### 8.3 `GenericBodyPart`

머리와 다리에 쓰이는 최소 입자다.

```text
lastPos = pos
pos += vel
vel *= airFriction
PushOutOfTerrain(room, connection.pos)
```

PlayerGraphics의 머리는 `rad=4`, surface `.8`, air `.99`, chunk 0 연결이고, 다리는
`rad=1`, surface `.8`, air `.99`, chunk 1 연결이다.

### 8.4 `Limb`와 `SlugcatHand`

`Limb.Mode` 값은 `HuntRelativePosition`, `HuntAbsolutePosition`, `Retracted`, `Dangle`다.
relative target은 connection의 `rotationChunk -> connection` 각도로 회전시켜 absolute
target으로 바꾼다.

```text
if distance(target,pos) < huntSpeed:
    vel = target-pos
    reachedSnapPosition = true
else:
    vel = Lerp(vel, Dir(pos,target)*huntSpeed, quickness)
```

그 후 mode에 따라 connection velocity를 더하고 위치 적분, air friction, terrain push를
수행한다. `huntSpeed`와 `quickness`는 tick 끝에서 기본값으로 되돌아간다.

`FindGrip`은 search 위치 주변 3x3 tile을 훑어 solid edge, floor, slope, horizontal/
vertical beam 가운데 목표에 가장 가까우면서 최대 연결 반경 안인 점을 고른다. custom
terrain이 있으면 x=-20..20을 5 간격으로 추가 probe한다.

Player의 두 손은 `SlugcatHand(chunk0, rad=3, surface=.8, air=1)`이고 기본
`huntSpeed=7`, `quickness=.5`다. 매 tick 먼저 `Limb.Update`, 그 뒤
`ConnectToPoint(connection.pos, 20, false, 0, connection.vel, 0, 0)`을 적용한다.

- Crawl: 앞쪽 `x + flip*28` 부근에서 grip 탐색, speed 12, quickness .7
- WallClimb: wall tile x에 고정하고 두 손 y를 -7/+3으로 교대
- beam: `animationFrame/20`의 sin/cos로 손을 교대
- 쓰지 않는 손: 5 tick 뒤 몸으로 retract를 시작, speed `1 + counter*.2`, 2 px 안에서
  snap되면 `Retracted`

### 8.5 `TailSegment`

정상 성체(Survivor/Monk/Hunter/Gourmand)의 4개 segment는 다음과 같다.

| index | radius | rest `connectionRad` | previous 영향 | surface / air |
|---:|---:|---:|---:|---:|
| 0 | 6 | 4 | root, 1.0 | .85 / 1 |
| 1 | 4 | 7 | .5 | .85 / 1 |
| 2 | 2.5 | 7 | .5 | .85 / 1 |
| 3 | 1 | 7 | .5 | .85 / 1 |

모두 `pullInPreviousPosition=true`다. `TailSegment.Update`의 constraint는 다음과 같다.

```text
lastPos = pos
pos += vel
vel *= airFriction
stretched = 1

if distance d to previous/root > rest r:
    u = Dir(pos, previous)
    selfCorrection = u * (r-d) * (1-affectPrevious)
    prevCorrection = u * (r-d) * affectPrevious
    pos -= selfCorrection
    vel -= selfCorrection
    previous.pos += prevCorrection       // pullInPreviousPosition일 때
    previous.vel += prevCorrection
    stretched = Clamp((r/(d*0.5)+2)/3, 0.2, 1)
```

root segment는 previous 대신 `connectedPoint`에 전체 correction을 적용한다.
`StretchedRad = rad * stretched`이므로 늘어날수록 렌더 폭이 가늘어진다. 마지막에
previous/root를 기준으로 terrain 밖으로 밀어낸다.

### 8.6 `PlayerGraphics.Update`

PlayerGraphics의 핵심 procedural 상태는 `tail[4]`, `head`, private `legs`,
`hands[2]`, `drawPositions[chunk,2]`, `legsDirection`, `lookDirection/lastLookDir`,
`blink`, `breath/lastBreath`, `airborneCounter`, `balanceCounter`, `disbalanceAmount`,
`PlayerObjectLooker`다.

호흡 phase는 sleeping일 때 tick당 `.0125`, 깨어 있을 때 다음만큼 증가한다.

```text
breath += 1 / Lerp(60, 15, Pow(aerobicLevel, 1.5))
```

렌더 시 `0.5 + 0.5*sin(2π*Lerp(lastBreath,breath,timeStacker))`로 쓴다.

standing adult의 대표 body offset은 upper x에
`flip*6*Clamp(abs(lower.vel.x)-.2,0,1)`, upper y에
`cos(animationFrame/6*2π)*2`를 더한다. crawl은
`sin(animationFrame/21*2π)`, `cos(animationFrame/14*2π)`를 body/hips/head에
서로 다른 배율로 섞는다. `animationFrame`은 단순 sprite index가 아니라 절차형 phase다.

꼬리 chain에는 segment 자체 update 뒤 다음 힘을 더한다.

```text
tail[0].connectedPoint = drawPositions[1,0]
pull = 28
anchor = state-dependent body point
previousPoint = hips chunk pos

for each segment i:
    segment.Update()
    segment.vel *= Lerp(.75,.95, shape*(1-lower.submersion))
    segment.vel.y -= Lerp(.1,.5,shape)
                     * (1-lower.submersion) * EffectiveRoomGravity
    shape = (shape*10+1)/11
    clamp distance from hips to 9*(i+1)
    segment.vel += Dir(anchor,segment.pos)
                   * pull / Distance(anchor,segment.pos)
    pull *= .5
    anchor = previousPoint
    previousPoint = segment.pos
```

머리는 먼저 `GenericBodyPart.Update`한 뒤 다음에 연결한다.

```text
neckTarget = Lerp(upperDraw, hipsDraw, .2)
           + Dir(hipsDraw, upperDraw)*3
head.ConnectToPoint(neckTarget,
    radius = HangFromBeam ? 0 : 3,
    push=false, elastic=.2,
    hostVel=upper.vel, adapt=.7, exaggerate=.1)
```

Crawl 성체에서는 neck 방향 x를 2.5배 한다. look target이 있을 때 standing idle은
`head.vel -= look*.5`, upper draw position은 `look*2`만큼 반대로 기울이고,
비-standing은 대체로 `head.vel += look`을 쓴다. object looker는 매 tick 10% 확률로
update하고 `.25%` 확률로 target을 지운다. blink는 매 tick 감소하며 음수 상태에서
`Random.Range(2,1800)` 조건을 넘으면 3..9 tick 범위로 다시 켠다.

일반 지면 다리 target은 다음과 같다.

```text
legs.Update()
legs.ConnectToPoint(
    lower.pos + (legsDirection.x*8, 1),
    radius=5, push=false, elastic=.25,
    hostVel=(lower.vel.x,-10), adapt=.5, exaggerate=.1)
```

beam, corridor, zero-G, hang 상태는 target과 radius를 바꾸지만 동일한 particle/
constraint 원리를 유지한다.

### 8.7 sprite 구성과 `DrawSprites`

기본 성체 sprite index는 다음과 같다.

| index | element / 역할 |
|---:|---|
| 0 | `BodyA`, anchorY `.7894737` |
| 1 | `HipsA` |
| 2 | `Futile_White`를 쓰는 custom 13-triangle tail mesh |
| 3 | `HeadA0` |
| 4 | `LegsA0`, anchorY `.25` |
| 5, 6 | `PlayerArm0`, anchorX `.9`; 5는 scaleY -1 |
| 7, 8 | `OnTopOfTerrainHand`; 8은 scaleX -1 |
| 9 | `FaceA0` |
| 10 | `Futile_White` mark glow |
| 11 | `pixel` mark |

MSC가 켜진 기본 캐릭터는 gown slot 때문에 보통 13개를 할당하지만 Gourmand 전용
cosmetic sprite는 없다.

cached name 범위는 다음과 같다.

- face: `Face`/`PFace` × `A..E` × `0..8`
- head: `HeadA`/`HeadB`/`HeadC` × `0..17`
- arms: `PlayerArm0..12`
- legs: `LegsA`, `LegsACrawling`, `LegsAClimbing`, `LegsAOnPole` 각 `0..30`

Draw 단계는 hips→upper 방향으로 BodyA 회전을 정하고, HipsA는
`(2*hips+upper)/3`에 놓아 tail root 쪽으로 회전한다. tail mesh는 각 segment 경계마다
4개 vertex를 만들고 `StretchedRad`를 폭으로 쓴다.

head view index는 몸 방향 각도를 34방향 기준으로 환산한 뒤 0..17 sprite로 접고,
상태별 clamp/override를 적용한다. face는 look direction을 22.5도 단위로 0..8에
매핑한다. 팔 frame은 손과 shoulder target 거리 `/2`를 0..12로 clamp/round한다.
다리는 body mode에 따라 `LegsA{frame}`, `LegsACrawling{frame/2}`,
`LegsAClimbing*`, `LegsAOnPole*`, `LegsAPole`, `LegsAVerticalPole`, `LegsAWall` 등을
고른다.

즉 atlas frame만 일정 시간마다 넘기는 방식으로는 원작 결과가 나오지 않는다. 두
chunk와 body parts를 먼저 계산하고 그 좌표/각도/거리에 따라 atlas element를 골라야
한다.

`SpinePosition(s,timeStacker)`은 body와 각 tail segment 사이를 보간하여 중심점,
tangent/perpendicular, radius를 산출하는 helper다. 등에 붙는 장식이나 carry point가
필요하면 임의의 sprite anchor보다 이 spine sampling 구조를 옮기는 편이 맞다.

## 9. `Room`, `RoomCamera`, abstract creature

### 9.1 `Room`

기본 tile 크기는 20 px다.

```text
PixelWidth  = TileWidth  * 20
PixelHeight = TileHeight * 20
MiddleOfTile(x,y) = (10+20*x, 10+20*y)
tile rectangle = center ± 10
```

`GetTilePosition(Vector2)`의 디컴파일 결과는 각 축에
`(int)((value+20)/20)-1`을 쓴다. 비음수 좌표에서는 사실상 `(int)(value/20)`과 같지만,
음수에서는 C#의 0 방향 정수 truncation까지 포함해 동작한다.

데스크톱 구현은 전체 Rain World room을 옮길 필요는 없지만 `GetTile`,
`GetTilePosition`, `MiddleOfTile`, floor/solid/slope/beam query에 해당하는 작은 collision
interface를 유지해야 `Player`, `Limb.FindGrip`, body-part terrain push 수식을 재사용할 수
있다. 화면 가장자리, 작업표시줄, 허용된 window top edge를 이 query layer의 surface로
변환하는 것이 적절하다.

### 9.2 `RoomCamera`

`Update`는 논리 카메라 상태를 갱신하고 `DrawUpdate`는 `timeStacker`로 카메라와 모든
drawable을 보간한다. 데스크톱 펫에서는 world camera가 고정이어도 SpriteLeaser와
같은 “논리 상태 → 보간된 draw state” 경계를 유지해야 40 Hz보다 높은 monitor refresh
rate에서 떨림이 없다.

### 9.3 `AbstractCreature`

주요 필드는 `creatureTemplate`, `state`, `abstractAI`, `realizedCreature`, den/spawn/
distance/controlled/personality 관련 상태다. unrealized이고 살아 있으며 den에 있지 않은
경우 abstract AI를 갱신한다.

`Realize`에서 template의 top ancestor가 Slugcat이면 `new Player(...)`를 만든다. 일반
Slugcat은 `InitiateAI`에서 player-controlled 경로라 AI를 만들지 않는다. DLC
`SlugNPC`는 `new Player(...)` 뒤 `SlugNPCAI`를 만든다. 그러므로 SlugNPC AI 전체를
Survivor/Monk/Hunter/Gourmand의 원작 AI라고 부르면 안 된다.

### 9.4 `CreatureTemplate`

template은 pathing preference, AI map, flight, grasp 수, body size, offscreen speed,
laziness, visual radius, water relationship, community/relationship 정보를 가진다.
relationship type도 `ExtEnum`이고 현재 값은 다음과 같다.

```text
DoesntTrack, Ignores, Eats, Afraid, StayOutOfWay, AgressiveRival,
Attacks, Uncomfortable, Antagonizes, PlaysWith, SocialDependent, Pack
```

relationship에는 intensity float가 붙고 `GoForKill`은 `Eats` 또는 `Attacks`이면서
intensity > 0일 때만 참이다. 데스크톱의 curiosity/rest/play behavior는 이 시스템에서
영감을 받을 수 있지만, 원작 Player의 독립 행동으로 추출된 것은 아니다.

## 10. 주요 메서드 IL 지도

| 타입.메서드 | token | RVA | IL code size |
|---|---|---:|---:|
| `RainWorld.Update` | `0x06000E6F` | `0x000DD4A8` | 1,107 |
| `ProcessManager.Update` | `0x06000E2E` | `0x000D9A30` | 3,496 |
| `MainLoopProcess.RawUpdate` | `0x06000DC2` | `0x000D3D58` | 124 |
| `RainWorldGame.RawUpdate` | `0x0600096B` | `0x0006D894` | 1,871 |
| `RainWorldGame.Update` | `0x0600096C` | `0x0006E000` | 3,877 |
| `Room.Update` | `0x060009DA` | `0x0007F7AC` | 5,462 |
| `BodyChunk..ctor` | `0x06000B69` | `0x00097EE0` | 178 |
| `BodyChunk.Update` | `0x06000B6A` | `0x00097FA0` | 1,565 |
| `BodyChunk.CheckHorizontalCollision` | `0x06000B6F` | `0x000986F0` | 1,484 |
| `BodyChunk.CheckVerticalCollision` | `0x06000B70` | `0x00098CC8` | 2,143 |
| `BodyChunk.checkAgainstSlopesVertically` | `0x06000B71` | `0x00099534` | 1,321 |
| `BodyPart.ConnectToPoint` | `0x06000B78` | `0x00099BBC` | 236 |
| `BodyPart.PushOutOfTerrain` | `0x06000B7B` | `0x00099E50` | 2,141 |
| `GenericBodyPart.Update` | `0x06000BD3` | `0x0009E460` | 92 |
| `Limb.Update` | `0x06000BDB` | `0x0009E760` | 606 |
| `Limb.FindGrip` | `0x06000BDD` | `0x0009E9D4` | 1,498 |
| `TailSegment.Update` | `0x06000C40` | `0x000A119C` | 632 |
| `PhysicalObject.Update` | `0x06000C04` | `0x0009F444` | 480 |
| `BodyChunkConnection.Update` | `0x0600478A` | `0x004F6170` | 298 |
| `Creature.Update` | `0x06000B9F` | `0x0009ACF4` | 4,103 |
| `Player.ClassMechanicsGourmand` | `0x06000CA0` | `0x000A7BEC` | 128 |
| `Player..ctor` | `0x06000CDC` | `0x000ABD7C` | 3,156 |
| `Player.Update` | `0x06000CE0` | `0x000ACA04` | 15,523 |
| `Player.checkInput` | `0x06000CE3` | `0x000B0F04` | 917 |
| `Player.TerrainImpact` | `0x06000CE9` | `0x000B2828` | 2,417 |
| `Player.GetInitialSlugcatClass` | `0x06000D04` | `0x000B4E34` | 222 |
| `Player.UpdateAnimation` | `0x06000D10` | `0x000B5C78` | 24,787 |
| `Player.UpdateBodyMode` | `0x06000D13` | `0x000BC040` | 13,112 |
| `Player.MovementUpdate` | `0x06000D2D` | `0x000C5CA0` | 11,433 |
| `Player.WallJump` | `0x06000D2E` | `0x000C8958` | 1,082 |
| `Player.Jump` | `0x06000D2F` | `0x000C8DA0` | 6,105 |
| `RoomCamera.Update` | `0x060010B3` | `0x0010BDC0` | 6,756 |
| `RoomCamera.DrawUpdate` | `0x060010B4` | `0x0010D84C` | 3,586 |
| `PlayerGraphics..ctor` | `0x06001C1E` | `0x002165AC` | 2,707 |
| `PlayerGraphics.Update` | `0x06001C20` | `0x0021708C` | 15,526 |
| `PlayerGraphics.InitiateSprites` | `0x06001C27` | `0x0021B3B4` | 2,306 |
| `PlayerGraphics.DrawSprites` | `0x06001C28` | `0x0021BCC4` | 13,483 |
| `PlayerGraphics.ApplyPalette` | `0x06001C29` | `0x0021F17C` | 2,075 |
| `PlayerGraphics.SlugcatColor` | `0x06001C2A` | `0x0021F9A4` | 173 |
| `PlayerGraphics.DefaultSlugcatColor` | `0x06001C2C` | `0x0021FAEC` | 412 |
| `PlayerGraphics.SpinePosition` | `0x06001C4C` | `0x00221588` | 530 |
| `SlugcatHand.Update` | `0x06001C5F` | `0x0022216C` | 3,958 |
| `SlugcatHand.EngageInMovement` | `0x06001C60` | `0x002230F0` | 7,671 |
| `SlugcatStats.SlugcatFoodMeter` | `0x06001C61` | `0x00224EF4` | 261 |
| `SlugcatStats..ctor` | `0x06001C62` | `0x00225008` | 1,529 |
| `AbstractCreature.Update` | `0x06002243` | `0x002A7138` | 545 |
| `AbstractCreature.Realize` | `0x06002247` | `0x002A7648` | 1,867 |
| `AbstractCreature.InitiateAI` | `0x06002248` | `0x002A7DA0` | 1,560 |
| `CreatureTemplate..ctor` | `0x060022E2` | `0x002B0880` | 3,531 |

표의 code size는 각 메서드 IL header를 기준으로 기록했다. 포팅 중 decompiler 출력과
동작이 모호할 때 token과 RVA로 원 IL을 다시 찾으면 된다.

## 11. 구현에 바로 적용할 권고

1. 내부 simulation을 정확히 40 Hz로 유지하고 renderer만 monitor refresh에 맞춰
   `timeStacker` 보간한다.
2. 먼저 두 BodyChunk와 하나의 connection을 원작 상수로 구현하고, surface adapter를
   통해 screen/taskbar/window 경계를 `Room` query처럼 제공한다.
3. `BodyModeIndex`와 `AnimationIndex`를 분리하고, 자율 행동기는 `InputPackage` history를
   생성한다. position을 직접 순간이동시키는 behavior는 피한다.
4. 시각층은 body physics와 분리한다. head/legs/hands/tail particle을 논리 tick에서
   갱신한 후 sprite element를 고르고 보간 draw한다.
5. 네 캐릭터는 공통 solver/profile 위에 `SlugcatStats`와 palette를 적용한다.
   Gourmand는 별도 head asset을 찾지 말고 body/hips 폭, 질량, exhaustion pose를
   우선 구현한다.
6. 원작의 random blink/look을 그대로 옮기려면 simulation RNG seed를 명시적으로
   관리한다. 그렇지 않으면 replay와 테스트가 비결정적이 된다.
7. 화면 좌표는 y-down이므로 원작 y-up 수식과의 경계에서 한 번만 변환한다. 각 수식의
   부호를 개별적으로 뒤집으면 slope/jump/limb 방향 오류가 생기기 쉽다.

## 12. 한계

- 이 문서는 static analysis 결과이며 실제 Unity/BepInEx runtime trace가 아니다.
- 설치된 runtime detour의 최종 동작, mod config 값, Jolly/custom color 설정은 실행하지
  않았으므로 반영하지 않았다.
- DLC/Watcher/Expedition 분기가 같은 메서드에 섞여 있다. 본문은 요청된
  Survivor/Monk/Hunter/Gourmand의 정상·비-malnourished story 기준을 우선했다.
- asset pixel rect와 atlas 추출 경로는 별도 `AssetFindings.md`에 기록되어 있다.
- 완전한 Player port에는 이 문서의 대표 수식 외에도 수영, beam, corridor, shortcut,
  grasp/carry 등 큰 상태별 메서드가 필요하다. 다만 desktop surface walking의 최소
  충실도 기준은 40 Hz update order, 두 chunk constraint, 지면 접촉 기반 mode,
  procedural body parts, timeStacker 보간이다.
