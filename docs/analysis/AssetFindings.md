# Rain World atlas / sprite asset findings

이 문서는 로컬 설치본을 **읽기 전용**으로 조사한 결과다. PNG, atlas TXT, Unity
`TextAsset`/`Texture2D` 데이터는 저장소로 복사하지 않았다.

## 조사 기준

- 설치 루트: `C:\Program Files (x86)\Steam\steamapps\common\Rain World`
- 게임 버전: `RainWorld_Data\StreamingAssets\GameVersion.txt` = `v1.11.8`
- Unity serialized-file 버전: `2020.3.45f1`, format 22, little-endian,
  `StandaloneWindows64`
- 주요 DLL:
  `RainWorld_Data\Managed\Assembly-CSharp.dll`
- 조사한 주요 메서드:
  `RainWorld.LoadResources`, `RainWorld.LoadModResources`,
  `FAtlas.LoadTexture`, `FAtlas.LoadAtlasData`,
  `FAtlasManager.LoadAtlas`, `PlayerGraphics.InitiateSprites`,
  `PlayerGraphics.DrawSprites`, `PlayerGraphics.InitCachedSpriteNames`

이 결과를 재현할 때 참고할 현재 파일 SHA-256은 다음과 같다. 아래 값과
serialized object의 path ID/offset은 업데이트 때 달라질 수 있으므로 런타임에서
하드코딩해서는 안 된다.

- `resources.assets`:
  `8A7B4C1A688688E919C8230A7D7AC41073B8ED9D0F25378016576C1020C6EE60`
- `resources.assets.resS`:
  `FB0FF097C7EA64A8233A40DE702FBF4092C43E0CE0DC9E729168D96BE6DA9130`
- `Assembly-CSharp.dll`:
  `B6BE1D4E18CE219D21091B51564CB6A11C1E4106B41DE903EB8E58849CB16FDB`

## 가장 중요한 결론

기본 Slugcat 스프라이트는 현재 설치본의
`RainWorld_Data\StreamingAssets\atlases`에 loose PNG/TXT 쌍으로 존재하지 않는다.
기본 body/head/hips/arms/legs/face는 하나의 논리 atlas
`Atlases/rainWorld`에 들어 있으며, 실제 데이터는 다음 Unity 파일에 직렬화되어
있다.

```text
RainWorld_Data/resources.assets
RainWorld_Data/resources.assets.resS
```

`resources.assets`에는 `rainWorld` 이름의 `TextAsset`(TexturePacker JSON)과
`Texture2D` descriptor가 있고, 실제 compressed texture payload는
`resources.assets.resS`에 있다. 따라서 `StreamingAssets`만 재귀 검색하는 로더는
기본 캐릭터를 찾지 못한다.

반면 Dress My Slugcat(DMS) 스킨과 일부 내장/Workshop 모드는
`StreamingAssets\mods\...\atlases` 또는
`...\dressmyslugcat\<skin>` 아래의 loose `.png` + `.txt` 형식을 사용한다. 기본
atlas와 DMS atlas는 같은 TexturePacker JSON 계열이지만 물리적 위치와 묶음 방식이
다르므로 별도의 source provider로 취급해야 한다.

현재 구현 범위에서는 DMS를 자동 탐색하지 않는다. 원작 Survivor(white),
Monk(yellow), Hunter(red), Gourmand는 base `rainWorld`의 HeadA/FaceA 형태를
사용한다. Gourmand의 차이는 runtime body scale/color로 적용한다. `rainworldmsc`도
두 번째 atlas로 겹쳐 DLC head/face와 pup frame을 보존하지만 `HeadC`를 Gourmand
전용 head로 간주하지 않는다.
아래 DMS 조사 내용은 향후 명시적 opt-in 기능을 위한 참고이며 현재 선택 우선순위에
참여하지 않는다.

## 기본 및 DLC atlas의 실제 위치

게임 DLL의 호출은 다음과 같다.

- `RainWorld.LoadResources`:
  `Futile.atlasManager.LoadAtlas("Atlases/rainWorld")`
- `RainWorld.LoadModResources` (MSC 활성 시):
  `Futile.atlasManager.LoadAtlas("Atlases/rainWorldMSC")`

현재 build의 serialized objects는 다음과 같다.

| 논리 atlas | object | path ID | 실제 파일/위치 | 크기/형식 |
|---|---:|---:|---|---|
| `Atlases/rainWorld` | `TextAsset rainWorld` | 369 | `resources.assets`, object byte start 12,248,040 | JSON 120,262 bytes, 620 frames |
| `Atlases/rainWorld` | `Texture2D rainWorld` | 8 | descriptor: `resources.assets`; payload: `resources.assets.resS` offset 18,250,256, size 237,568 | 464x512, Unity TextureFormat 12 (`DXT5`), 1 mip |
| `Atlases/rainWorldMSC` | `TextAsset rainworldmsc` | 381 | `resources.assets`, object byte start 16,393,616 | JSON 36,221 bytes, 188 frames |
| `Atlases/rainWorldMSC` | `Texture2D rainworldmsc` | 7 | descriptor: `resources.assets`; payload: `resources.assets.resS` offset 17,890,592, size 359,660 | 367x245, Unity TextureFormat 4 (`RGBA32`), 1 mip |

두 texture 모두 point filtering(`m_FilterMode = 0`), anisotropy 1, mipmap 없음이다.
wrap U/V는 clamp 값 1이다. base JSON의 `meta.format`은 원본 packer 출력 기준
`RGBA8888`이지만, Unity build 안의 `rainWorld` pixel payload는 DXT5로 재압축되어
있다. JSON의 `meta.format`만 보고 raw bytes를 RGBA32로 해석하면 안 된다.

위 path ID와 offset은 진단값일 뿐이다. 런타임 로더는 serialized file의 object
table에서 class ID 49(`TextAsset`)와 28(`Texture2D`)을 읽고 **이름**
`rainWorld`/`rainworldmsc`로 찾아야 한다. `Texture2D.m_StreamData.path`, `offset`,
`size`, `m_TextureFormat`도 매번 descriptor에서 읽어야 한다.

## `PlayerGraphics` sprite index와 element

`PlayerGraphics.InitiateSprites`에서 확인한 기본 index는 다음과 같다. 10 이후
sprite는 특수 효과용이며 일반 신체 조립에는 필요하지 않다.

| index | 초기 element | 역할/예외 | Futile anchor |
|---:|---|---|---|
| 0 | `BodyA` | body; pup이면 추가 `scaleY = 0.5` | `(0.5, 0.7894737)` |
| 1 | `HipsA` | hips | 기본 `(0.5, 0.5)` |
| 2 | `Futile_White`를 쓰는 `TriangleMesh` | tail; `Tail*.png` sprite가 아님 | anchor 없음(동적 mesh vertices) |
| 3 | `HeadA0` (`Saint` 조건에서는 `HeadB0`) | head, 이후 `HeadA/B/C0..17` 교체 | 기본 `(0.5, 0.5)` |
| 4 | `LegsA0` | animation/body mode에 따라 legs element 교체 | `(0.5, 0.25)` |
| 5, 6 | `PlayerArm0` | 양팔; index 5는 초기 `scaleY = -1` | `(0.9, 0.5)` |
| 7, 8 | `OnTopOfTerrainHand` | 양손; index 8은 초기 `scaleX = -1` | 기본 `(0.5, 0.5)` |
| 9 | `FaceA0` | face/look/blink/death 상태에 따라 교체 | 기본 `(0.5, 0.5)` |

중요하게도 anchor/pivot은 atlas TXT에 기록되지 않는다. `FSprite`의 정적 기본값이
`anchorX = anchorY = 0.5`이고, 위 네 가지 override는 `PlayerGraphics` 코드에
있다. atlas parser만 구현해서 모든 부위를 가운데 pivot으로 그리면 body, legs,
arms가 원본과 맞지 않는다.

base tail은 13개의 triangle로 만든 solid-color `TriangleMesh`이며 atlas에
`TailTexture`나 `Tail0` element가 없다. DMS가 별도로 제공하는 `TailTexture`는
DMS hook이 tail mesh UV를 다시 매핑할 때 쓰는 확장 형식이다.

## 실제 element naming과 범위

Futile은 JSON key의 마지막 확장자를 제거한다
(`Futile.shouldRemoveAtlasElementFileExtensions = true`). 즉 JSON의
`"HeadA0.png"`는 런타임 lookup name `HeadA0`이 된다. element name의 대소문자는
그대로 유지되므로 이름을 임의로 lowercase하지 않는다.

### `rainWorld` (base, 464x512)

| 부위 | 실제 element |
|---|---|
| body | `BodyA` (1) |
| hips | `HipsA` (1) |
| head | `HeadA0` .. `HeadA17` (18) |
| face | `FaceA0` .. `FaceA8`, `FaceB0` .. `FaceB8`, `FaceDead`, `FaceStunned` (20) |
| arms/hands | `PlayerArm0` .. `PlayerArm12`, `OnTopOfTerrainHand`, `OnTopOfTerrainHand2` (15) |
| legs | `LegsA0` .. `LegsA6`; `LegsAAir0` .. `1`; `LegsAClimbing0` .. `6`; `LegsACrawling0` .. `5`; `LegsAOnPole0` .. `6`; `LegsAPole`; `LegsAVerticalPole`; `LegsAWall` (32) |
| tail | 해당 element 없음; `Futile_White` mesh 사용 |

대표 descriptor:

| element JSON key | frame `(x,y,w,h)` | trimmed/source offset | source size |
|---|---|---|---|
| `BodyA.png` | `(358,50,14,19)` | false / `(0,0)` | `14x19` |
| `HipsA.png` | `(256,388,14,20)` | false / `(0,0)` | `14x20` |
| `HeadA0.png` | `(239,369,16,17)` | false / `(0,0)` | `16x17` |
| `HeadA17.png` | `(130,471,24,16)` | false / `(0,0)` | `24x16` |
| `FaceA0.png` | `(379,301,12,7)` | true / `(2,3)` | `16x18` |
| `FaceDead.png` | `(393,401,12,7)` | true / `(2,3)` | `16x18` |
| `FaceStunned.png` | `(407,402,12,6)` | true / `(2,4)` | `16x18` |
| `LegsA0.png` | `(220,288,18,13)` | false / `(0,0)` | `18x13` |
| `LegsACrawling0.png` | `(289,498,15,12)` | true / `(7,2)` | `28x14` |
| `PlayerArm0.png` | `(445,473,5,5)` | true / `(23,0)` | `28x8` |
| `PlayerArm12.png` | `(395,234,28,6)` | true / `(0,0)` | `28x8` |
| `OnTopOfTerrainHand.png` | `(456,461,5,4)` | false / `(0,0)` | `5x4` |

`PlayerGraphics.InitCachedSpriteNames`가 legs 계열 문자열 cache를 31개까지
생성하지만, 실제 atlas에 `LegsA7..30` 등이 있다는 뜻은 아니다. 위 표의 실제
범위만 요청해야 한다. 선택 로직은 animation index를 이 실제 범위에 매핑한다.

### `rainWorldMSC` (DLC, 367x245)

MSC atlas에는 base body를 대체하는 완전한 세트가 아니라 추가 head/face 변형만
있다.

- `HeadB0` .. `HeadB17`
- `HeadC0` .. `HeadC17`
- `FaceC0` .. `FaceC8`
- `FaceD0` .. `FaceD8`
- `FaceE0` .. `FaceE8`
- `PFaceA0` .. `PFaceA8`
- `PFaceB0` .. `PFaceB8`

`PFace`는 `PlayerGraphics`가 pup face prefix로 만들 수 있는 이름이다. cache는
`PFaceC/D/E` 문자열도 구성할 수 있으나 현재 조사한 atlas에는 그 element가 없다.
요청 전 존재 여부를 확인해야 한다. DLC가 없거나 로드되지 않은 환경에서는
`HeadB/C`, `FaceC/D/E`, `PFace*`를 base 필수 자산으로 간주하지 않는다.

## atlas `.txt` 포맷

`.txt`는 line-based 자체 포맷이 아니라 TexturePacker의 Unity/Futile용 JSON이다.
base `TextAsset`과 조사한 loose mod TXT는 UTF-8(JSON ASCII 범위), BOM 없음이었다.
CRLF와 LF가 모두 발견되었으므로 줄바꿈에 의존하면 안 된다.

필수 구조는 다음과 같다.

```json
{
  "frames": {
    "ElementName.png": {
      "frame": { "x": 0, "y": 0, "w": 16, "h": 18 },
      "rotated": false,
      "trimmed": true,
      "spriteSourceSize": { "x": 2, "y": 3, "w": 12, "h": 7 },
      "sourceSize": { "w": 16, "h": 18 }
    }
  },
  "meta": {
    "image": "atlas.png",
    "format": "RGBA8888",
    "size": { "w": 464, "h": 512 },
    "scale": "1"
  }
}
```

관찰된 Futile parser 동작:

- 실제로 소비하는 root key는 `frames`다. `meta`는 좌표 계산에 사용하지 않는다.
- `frame`의 원점은 texture **좌상단**이다. Unity UV를 만들 때 Futile은
  `uvY = (textureHeight - y - h) / textureHeight`로 뒤집는다. PNG를 일반적인
  top-left raster로 디코딩하는 Windows renderer에서는 JSON 좌표를 그대로 crop할
  수 있다.
- `sourceSize`는 trim 전 logical canvas 크기다.
- `spriteSourceSize.x/y`는 그 logical canvas 좌상단에서 실제 crop까지의 offset이다.
- `spriteSourceSize.w/h`는 관찰된 파일에서 `frame.w/h`와 같지만, 로더는 양쪽을
  검증하는 편이 안전하다.
- `rotated: true`이면 원본 Futile도 `NotSupportedException`을 던진다. 조용히
  잘못 회전해 그리지 말고 명시적으로 거부한다.
- 숫자는 invariant-culture float로 파싱된다. 현재 값은 정수지만 JSON number를
  정수형으로만 제한할 이유는 없다.
- 모든 관찰된 relevant frame은 `rotated: false`였다.

### trim과 anchor를 함께 적용하는 방법

`sourceSize = (W,H)`, `spriteSourceSize = (sx,sy,sw,sh)`, Futile anchor를
`(ax,ay)`라고 하면, y-down인 Windows renderer에서 sprite origin(원본의
`FSprite.x/y`)에 대해 실제 crop의 좌상단을 다음처럼 놓으면 원본의 trim 보정과
같다.

```text
drawX = originX + sx - ax * W
drawY = originY + sy - (1 - ay) * H
```

그 뒤 `frame(x,y,w,h)` crop을 `sw x sh`로 그린다. flip/rotation은 이 logical
origin 주변에 적용해야 한다. 단순히 crop된 bitmap의 중앙을 origin에 놓으면
특히 `PlayerArm0`(source offset x=23)과 face가 크게 어긋난다.

PC build의 `Futile.resourceScaleInverse`는 1이므로 위 크기는 logical unit과
1:1이다. 데스크톱 펫 자체의 확대 배율은 이 계산 뒤 부모 transform에서 적용하는
것이 안전하다.

## loose atlas 탐색 규칙

`FAtlas.LoadTexture`/`LoadAtlasData`는 먼저 각각
`<logicalPath>.png`/`<logicalPath>.txt`를 `AssetManager.ResolveFilePath`로 찾는다.
`RWCustom.Custom.rootFolderDirectory`는 `Application.streamingAssetsPath`로
초기화된다. 대략적인 우선순위는 다음과 같다.

1. `StreamingAssets\mergedmods\<lowercase logical path>`
2. active mods의 targeted/newest/base 폴더(역순 우선순위)
3. console-specific path(해당 시)
4. `StreamingAssets\<lowercase logical path>`
5. loose file이 없으면 Unity `Resources.Load(logicalPath)`

따라서 base 호출 `Atlases/rainWorld`의 loose override 후보는
`...\atlases\rainworld.png` + `.txt`이지만 현재 설치본에는 그 쌍이 없어서
`resources.assets`로 fallback한다. 독립 프로그램에서 mod 적용까지 지원한다면
동일한 우선순위를 별도 manifest/cache로 재구현할 수 있으나, 최초 구현은
embedded base atlas와 명시적으로 선택한 DMS skin을 분리하는 편이 예측 가능하다.

## 설치된 Dress My Slugcat 형식

현재 설치본에서 확인한 사용자 스킨 위치:

```text
RainWorld_Data/StreamingAssets/mods/My_Slugcat/
  dressmyslugcat/My-Slugcat_skin/
    metadata.json
    arm.png       arm.txt
    body.png      body.txt
    face.png      face.txt
    head.png      head.txt
    hips.png      hips.txt
    legs.png      legs.txt
    tail.png      tail.txt
    extras.* / pixel.* (선택 확장)
```

이 폴더에서 관찰한 atlas는 다음과 같다.

| basename pair | PNG / `meta.size` | frames | element naming |
|---|---:|---:|---|
| `body.png` + `body.txt` | 28x25 | 1 | `BodyA` |
| `hips.png` + `hips.txt` | 32x38 | 1 | `HipsA` |
| `head.png` + `head.txt` | 1008x240 | 18 | `HeadA0..17` |
| `face.png` + `face.txt` | 242x202 | 20 | `FaceA0..8`, `FaceB0..8`, `FaceDead`, `FaceStunned` |
| `arm.png` + `arm.txt` | 248x874 | 15 | `PlayerArm0..12`, 두 `OnTopOfTerrainHand*` |
| `legs.png` + `legs.txt` | 1762x49 | 32 | base와 같은 legs 32종 |
| `tail.png` + `tail.txt` | 154x79 | 1 | `TailTexture` |

`metadata.json`의 필수 식별 필드는 이 스킨에서 `id`, `name`, `author`였다.
DMS DLL은 각 element에 `dressmyslugcat_<id>_` prefix를 붙여 전역 Futile atlas
namespace 충돌을 피한 뒤, 자체 `SpriteSheet` dictionary에서는 prefix를 벗겨
`BodyA` 같은 원래 이름으로 조회한다. 독립 로더도 `(skin id, element name)`의
복합 key 또는 atlas-local dictionary를 사용해야 한다.

### DMS에서 확인한 예외

- 파일 쌍은 **같은 basename**으로 결합해야 한다. 실제 `body.png` 옆
  `body.txt`의 `meta.image`는 `EXtemplatebody.png`, `head.txt`는
  `EXtemplateHead.png`, `hips.txt`는 `EXtemplatehips.png`라고 기록되어 있으며
  그런 파일은 해당 스킨 폴더에 없다. DMS 원본 loader도 검색한 PNG의
  extension을 제거해 같은 경로의 TXT를 찾고 `meta.image`는 사용하지 않는다.
- DMS canvas는 실제 opaque art보다 의도적으로 훨씬 크다. 예를 들어
  `PlayerArm0` frame은 244x64지만 opaque pixels는 anchor 부근의 작은 영역에만
  있고, `HeadA0` frame은 96x120 canvas 중앙에 16x17 정도의 sprite가 있다.
  이것은 원본 pivot과 accessory 여백을 보존하기 위한 것이다. alpha trim 후
  중앙 정렬하면 위치가 깨진다.
- DMS element로 교체해도 원래 `FSprite`의 anchor가 유지된다. 따라서 위
  `PlayerGraphics` anchor 표를 DMS에도 적용한다.
- asymmetry template에는 `bodyleft/right`, `headleft/right`, `armleft/right`,
  `legsleft/right`, `faceleft/right`, `hipsleft/right`가 있으며 frame 이름은
  `LeftBodyA`, `RightBodyA`처럼 `Left`/`Right` prefix를 사용한다. 처음 구현에서는
  optional capability로 다룬다.
- DMS `TailTexture`는 평면 tail sprite가 아니다. segment 기반 triangle mesh에
  입힐 texture다. DMS 미지원 시 base처럼 solid-color procedural mesh로 그린다.

## 런타임 로더 권장 구조

```text
RainWorldInstallation
  -> EmbeddedUnityAtlasProvider (resources.assets + .resS)
  -> LooseAtlasProvider         (future explicit opt-in DMS/mod PNG+TXT; disabled now)
  -> ParsedAtlas
       Texture pixels
       Elements[name]
         frame
         sourceSize
         sourceOffset
         trimmed
  -> SlugcatSpriteSet
       Body/Hips/Head/Face/Arms/Hands/Legs
       Tail = procedural mesh (optional TailTexture)
```

구현 시 지켜야 할 사항:

1. Unity runtime, `RainWorld.exe`, `Assembly-CSharp.dll`을 실행 시 의존성으로 삼지
   않는다. DLL은 위 mapping을 얻기 위한 분석 자료일 뿐이다.
2. 최초 실행 또는 설치본 변경 시만 `resources.assets`를 읽고, texture와 parsed
   metadata를 메모리 또는 사용자 cache에 둔다. 매 frame 재분석하지 않는다.
3. base DXT5와 DLC RGBA32를 format-aware하게 디코딩한다.
4. path ID, byte offset, atlas 전체 크기를 compatibility signature로 검증하되
   이름 기반 탐색을 우선한다.
5. `frame` bounds, positive size, `sourceOffset + frameSize <= sourceSize`,
   `rotated == false`, PNG/Texture2D 실제 크기와 `meta.size`를 검증한다.
6. element lookup 실패 시 다음 fallback을 사용한다: 요청한 optional variant ->
   같은 자세의 `HeadA`/`FaceA` -> frame 0. 존재하지 않는 generated name을 무작정
   요청하지 않는다.
7. 원본/DMS bitmap을 저장소, installer, release artifact에 포함하지 않는다.
   필요하면 사용자 PC의 app-data cache에 현재 설치본에서 파생한 데이터만 두고
   install hash 변경 시 무효화한다.

## 구현 및 설치본 smoke test

현재 .NET Framework 4.8 구현은 `UnitySerializedFileReader`가 SerializedFile v22의
type/object table을 순회하고 class ID 49/28 객체를 **이름으로** 찾는다. 이어
`Texture2D.m_StreamData`가 가리키는 sibling resource payload를 읽어 base DXT5와
MSC RGBA32를 top-left `Bitmap`으로 디코딩한다. path ID와 object/stream offset은
코드에 하드코딩하지 않았다. 지원하지 않는 SerializedFile 버전이나 texture format은
오해석하지 않고 명시적 오류를 반환하여 기존 procedural fallback이 동작하게 한다.
embedded 실패 시 자동 loose fallback은 non-mod
`StreamingAssets\atlases`만 검사하며 `mods`/`mergedmods`는 현재 범위에서 읽지 않는다.

원본 파일을 복사하지 않는 로컬 검증 명령:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\tools\smoke-embedded-atlas.ps1 `
  -RainWorldRoot 'C:\Program Files (x86)\Steam\steamapps\common\Rain World'
```

이 smoke test는 base/MSC 2개 atlas, base 필수 element, `BodyA` geometry와 DXT5
opaque pixel, MSC `HeadC0` override와 RGBA32 opaque pixel을 확인한 뒤 모든
`Bitmap`을 dispose한다. 게임 실행 파일이나 Unity runtime은 시작하지 않는다.

## 최소 회귀 검증값

loader smoke test는 현재 build에서 최소 다음을 확인하면 좌표계/이름 처리 오류를
빠르게 찾을 수 있다.

- `rainWorld` texture = `464x512`, element count = 620
- `BodyA`: frame `358,50,14,19`, source `14x19`
- `FaceA0`: frame `379,301,12,7`, offset `2,3`, source `16x18`
- `PlayerArm0`: frame `445,473,5,5`, offset `23,0`, source `28x8`
- `LegsACrawling0`: frame `289,498,15,12`, offset `7,2`, source `28x14`
- `TailTexture`가 base `rainWorld`에는 없음
- `rainworldmsc` texture = `367x245`, element count = 188
- 모든 relevant frame의 `rotated` = false
