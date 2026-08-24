# Downpour Slugcat ability parity

이 문서는 데스크톱 런타임의 특수 능력이 어느 Rain World 코드에서 왔는지와, 운영체제 지형 어댑터 때문에 남는 차이를 기록한다. 아래 수치는 감각적으로 맞춘 값이 아니라 로컬 DLL의 해당 분기에서 옮긴 값이다.

## 분석 기준

- DLL: `RainWorld_Data/Managed/Assembly-CSharp.dll`
- 로컬 설치 SHA-256: `B6BE1D4E18CE219D21091B51564CB6A11C1E4106B41DE903EB8E58849CB16FDB`
- 분석 도구: ILSpy 11 decompile
- 2026-08-24 재검증: 위 해시의 `v1.11.8` DLL metadata에서 캐릭터별 원본 메서드,
  `AxolotlGills`, `TailSpeckles`, 사용 `SoundID`를 다시 확인하고 retail
  `StreamingAssets/soundeffects/sounds.txt`의 clip/PLAYALL/volume/pitch와 대조했다.
- 좌표 변환: 원작은 Y-up, 데스크톱 충돌계는 Y-down이다. 문서의 원작 값은 Y-up으로 쓰고, 구현 위치에는 부호 변환을 명시한다.
- 고정 시뮬레이션: 원작과 같은 40 Hz. 고주사율 렌더링은 `timeStacker` 보간만 수행한다.
- 공통 호출 순서:

```text
DesktopPetAI
  -> VirtualInput
  -> Slugcat.Step / Player movement-equivalent
  -> character-specific Player branch
  -> BodyChunk / ability object
  -> PlayerGraphics-equivalent
  -> SoundEvent -> local sounds.txt -> local UnityFS audio clip
```

원작 `Player.Update`에서 `MovementUpdate`가 먼저 실행되고, MSC 갱신에서 `ClassMechanicsSpearmaster`, `ClassMechanicsGourmand`, `ClassMechanicsArtificer`, `ClassMechanicsSaint`, `TongueUpdate`가 이어진다. 데스크톱 런타임도 일반 이동 뒤 Artificer/Spearmaster/Saint 분기를 실행한다. Roll/BellySlide는 원작 `MovementUpdate -> UpdateAnimation` 안의 분기이므로 Gourmand 컨트롤러가 일반 이동 직전에 그 상태를 갱신한다.

## 원본 위치 색인

| 영역 | 원본 클래스/메서드 | decompile 기준 위치 |
|---|---|---:|
| Artificer | `Player.ClassMechanicsArtificer` | `Player` 2996 |
| Artificer death | `Player.PyroDeath` | `Player` 3317 |
| Gourmand class state | `Player.ClassMechanicsGourmand` | `Player` 3353 |
| Saint class state | `Player.ClassMechanicsSaint` | `Player` 3643 |
| Saint rope input | `Player.TongueUpdate` | `Player` 4047 |
| Spearmaster class state | `Player.ClassMechanicsSpearmaster` | `Player` 4182 |
| terrain/roll entry | `Player.TerrainImpact` | `Player` 6507 |
| animation/roll/slide | `Player.UpdateAnimation` | `Player` 7500 |
| movement profiles | `Player.UpdateBodyMode` | `Player` 8967 |
| spear extraction | `Player.GrabUpdate` | `Player` 9823 |
| weapon throw | `Player.ThrowObject` | `Player` 11209 |
| movement dispatcher | `Player.MovementUpdate` | `Player` 12093 |
| wall jump | `Player.WallJump` | `Player` 12772 |
| jump | `Player.Jump` | `Player` 12836 |
| tongue state machine | `Player.Tongue` | `Player` 18 |
| tongue render particle | `PlayerGraphics.RopeSegment` | `PlayerGraphics` 443 |
| Spearmaster tail | `PlayerGraphics.TailSpeckles` | `PlayerGraphics` 936 |
| tongue stretch | `PlayerGraphics.RopeStretchFac` | `PlayerGraphics` 1687 |
| tongue mesh | `PlayerGraphics.InitiateSprites`, `DrawSprites`, `Update`, `ConnectRopeSegments` | 2669, 2825, 4348, 4391 |
| stats | `SlugcatStats` constructor MSC branches | `SlugcatStats` 208-255 |

## 상태 대응

원작 필드의 의미를 합치지 않고 캐릭터별 컨트롤러에 유지한다.

| 원작 | 데스크톱 |
|---|---|
| `pyroJumpCounter` | `ArtificerAbilityController.explosiveJumpCounter` |
| `pyroJumpCooldown` | `ArtificerAbilityController.cooldown` |
| `pyroParryCooldown` | `ArtificerAbilityController.parryCooldown` |
| `pyroJumpDropLock` | `ArtificerAbilityController.jumpDropLock` |
| `pyroJumpped` | `ArtificerAbilityController.pyroJumped` |
| `TailSpeckles.spearProg/type/line/row` | `SpearmasterAbilityController.spearProgress/type/line/row` |
| `Player.grasps`의 needle | `heldSpear` + `DesktopSpear.Mode.Held` |
| `Tongue.mode/pos/vel/elastic/idealRopeLength` | `SaintAbilityController.mode/position/velocity/elastic/idealLength` |
| `Tongue.rope` | `DesktopRope` |
| `PlayerGraphics.ropeSegments[20]` | `rope/lastRope/ropeVelocity/ropeClaimed[20]` |
| `gourmandExhausted` | `GourmandAbilityController.exhausted` |
| `rollDirection/rollCounter/allowRoll` | 같은 이름의 Gourmand 컨트롤러 필드 |
| `consistentDownDiagonal/stopRollingCounter/exitBellySlideCounter` | 같은 이름의 Gourmand 컨트롤러 필드 |
| `aerobicLevel/slowMovementStun` | `SlugcatState.AerobicLevel/SlowMovementStun` |

---

## Artificer

### Explosive Jump

**Slugcat:** Artificer

**Ability:** Explosive Jump

**Original Classes:** `Player`, `MoreSlugcats.MoreSlugcats`, `Explosion.ExplosionSmoke`, `Explosion.ExplosionLight`, `Spark`

**Original Methods:** `Player.ClassMechanicsArtificer`, `Player.PyroDeath`

**Activation Condition:** `(wantToJump > 0 && pckp) || spec edge`, `!pyroJumpped`, `canJump <= 0`, conscious, not mauling, and not Crawl/Corridor/Shortcut/Beam/WallClimb/Swimming/Antler/Vine/ZeroGPole/onBack. 일반 중력에서는 원작 `input.y >= 0`, 즉 데스크톱 Y-down에서는 `Y <= 0`이다.

**Input Sequence:** AI는 공중에서 `Jump + Pickup`의 원작 조합만 낸다. 방향은 `X=-1/0/1`, 위 입력은 데스크톱 `Y=-1`이다. AI가 속도나 목표점을 능력 객체에 전달하지 않는다.

**Counters:** capacity 10, safe threshold `max(1, capacity-5)=5`, danger threshold `max(1, capacity-3)=7`. 사용 시 counter +1, cooldown 150. 회복 tick에서 counter가 safe 이상이면 다음 간격 40, 아니면 60. `pyroJumpDropLock=40`. counter 7 이상은 `60 * (counter-(danger-1))` stun, counter 10은 `PyroDeath`.

**Physics Changes:** 원작 Y-up 값을 Y-down으로 변환한다.

- X 입력이 있으면 chest `min(vy,0)+8`, hips `+7` -> desktop `max(vy,0)-8/-7`, `jumpBoost=6`.
- `X==0` 또는 위 입력이면 일반 chest/hips 11/10, danger 이상 16/15, `jumpBoost=8/10`.
- 위 입력이면 X 속도 chest/hips `10*x`, `8*x`; 그 외 `15*x`, `13*x`.
- `AnimationIndex.Flip`, `BodyModeIndex.Default`를 사용한다.

**Graphics:** 8 `ExplosionSmoke`, radius 160/life 3 white `ExplosionLight`, 10 white `Spark(standard=4, exceptional=18)`. `ExplosionSmoke`는 lifeTime 170-400, rad .6-1.5, 목표점 drift, 회전, 두 `FireSmoke` layer의 1.1/.9 scale과 .8/.6 alpha를 유지한다. `Futile_White`의 16px quad를 반영해 world radius에 half-size 8을 곱한다. `Spark`는 4-position trail, 초기 0-30 offset, 0.4-0.9 gravity, terrain bounce .5, 90%/10% lifetime 분기를 유지한다. safe 이상 idle tick에는 25% smoke와 50% `Spark(4,8)`를 원본 확률로 생성한다.

**SoundID:** `Fire_Spear_Explode`, 호출 volume `0.3 + random*0.3`, pitch `0.5 + random*2`. `sounds.txt`의 clip volume/pitch 범위를 그 위에 곱한다.

**Desktop Implementation:** `ArtificerAbilityController.UpdateAfterMovement`, `Slugcat.Step`, `SpriteRenderer.DrawAbilityObjects`, `RainWorldAudioEngine`.

**Known Difference:** 원작 room의 creature/weapon 목록이 없으므로 `InGameNoise(position,8000,...)`는 생성하지 않는다. `FireSmoke`는 설치된 원본 fragment 식과 `Palettes/noise`·`noise2` Texture2D를 CPU로 직접 실행한다. Unity shader runtime이 없는 GDI에서도 임의 방사형 연기 마스크로 대체하지 않는다. 이 원작 jump/parry 분기에는 `Room.ScreenMovement` 호출이 없으므로 임의 카메라 흔들림을 추가하지 않는다. ZeroG와 수중 room 상태는 데스크톱 지형 모델에 없어 해당 분기는 비활성이다.

### Explosive Parry

**Slugcat:** Artificer

**Ability:** downward parry / concussive blast

**Original Classes:** `Player`, `Weapon`, `Creature`, `ShockWave`

**Original Methods:** `Player.ClassMechanicsArtificer`

**Activation Condition:** 같은 요청 조합, not submerged/mauling, 원작 `y<0`(desktop `Y>0`) 또는 Crawl, `canJump>0 || y<0`, conscious, `!pyroJumpped`, `pyroParryCooldown<=0`.

**Input Sequence:** `Jump + Pickup + Down`. 특수 버튼을 합성할 필요가 없는 현재 AI 경로는 원작 버튼 조합을 사용한다.

**Counters:** counter가 safe 이하이면 +2, 아니면 +1. parry cooldown 40, jump cooldown 150. danger stun/death는 explosive jump와 동일하다.

**Physics Changes:** 공중이면 chest/hips Y-up 8/6 -> desktop -8/-6, `jumpBoost=6`, `pyroJumpped=true`.

**Graphics:** jump VFX에 `ShockWave(size=200,intensity=0.2,life=6)` 추가.

**SoundID:** `Fire_Spear_Explode`, jump와 같은 volume/pitch 호출식.

**Desktop Implementation:** `ArtificerAbilityController.ExplosiveParry`.

**Known Difference:** 데스크톱 런타임에는 주변 Rain World `Creature`와 thrown `Weapon` object graph가 없으므로 200/300 범위 stun·반사 목록은 비어 있다. 자기 BodyChunk, counter, cooldown, VFX, SoundID는 원본 분기 그대로 실행한다.

### PyroDeath

**Slugcat:** Artificer

**Ability:** over-capacity death

**Original Classes:** `Player`, `Explosion`, `SootMark`, `ExplosionLight`, `ExplosionSpikes`, `ShockWave`, `Spark`, `Explosion.FlashingSmoke`

**Original Methods:** `Player.PyroDeath`

**Activation Condition:** `pyroJumpCounter >= capacity`.

**Input Sequence:** 직접 입력 없음. 앞선 원작 explosive branch의 결과다.

**Counters:** counter를 capacity에 고정하고 `Die()`.

**Physics Changes:** 폭발 중심은 `Lerp(firstChunk.pos, firstChunk.lastPos, 0.35)`.

**Graphics:** SootMark 80; Explosion life 7/radius 350; lights 280/7 및 230/3; 14 spikes/radius 170; shockwave 430/0.045/5; 25개 방향마다 Spark 3개(`11,28`, position 30-60, velocity 7-38 + RNV*0-20)와 FlashingSmoke를 생성한다.

**SoundID:** `Bomb_Explode`, volume 1, pitch 1.

**Desktop Implementation:** `ArtificerAbilityController.PyroDeath`.

**Known Difference:** Unity shader, screen shake, room noise와 피해 object graph는 GDI 데스크톱 렌더러에 없다. 객체 수·생성 tick·반경·수명·초기 속도와 SoundID는 유지한다.

**조사한 다른 Artificer 분기:** spear 폭발 crafting과 `MaulingUpdate`는 각각 Rain World food/grasp/abstract-object 및 creature graph가 필요하다. 가짜 아이템이나 가짜 생물을 만들지 않고 비활성으로 남긴다. Garbage Wastes story flag는 능력이 아니므로 제외한다.

---

## Spearmaster

### Needle extraction

**Slugcat:** Spearmaster

**Ability:** body needle creation and grasp

**Original Classes:** `Player`, `PlayerGraphics.TailSpeckles`, `AbstractSpear`, `Spear`, `WaterDrip`, `Spark`

**Original Methods:** `Player.GrabUpdate`, `TailSpeckles.newSpearSlot`, `TailSpeckles.setSpearProgress`

**Activation Condition:** conscious, pickup held, free hand, no edible target, neutral `x/y/jump/throw`, graphics available. 현재 한 손 grasp 모델에서 `heldSpear == null`이 free-hand 판정이다.

**Input Sequence:** `Pickup=true`를 중립 상태로 계속 유지한다. Pickup을 놓으면 progress가 `Lerp(progress,0,.05)`로 돌아가고 `<.025`에서 0이다. Pickup을 계속 누른 채 방향/jump/throw가 중립 gate를 깨면 원작처럼 progress는 감소하지 않고 그 tick 값에 고정된다.

**Counters:** progress 0에서 line `Random.Range(0, lines-1)`에 해당하는 0/1, row 0-3, type 0-2 선택. `<.1`은 `Lerp(p,.11,.1)`, 이후 `Lerp(p,1,.05)`. `>.95`에서 1로 고정되고 같은 tick에 생성한다. 고정 입력 replay에서 실제 생성 tick은 79다.

**Physics Changes:** 생성 위치는 main BodyChunk. 세로 자세에서는 body direction을 뒤집고 `x += facing*.4`. 초기 velocity는 `ClampMagnitude((dir*2 + RNV*random)/.07, 6)`.

**Graphics:** 선택한 speckle과 인접 speckle scale 및 설치된 게임 `resources.assets`의 `BioSpear1..3` atlas element를 progress에서 계산한다. 임시 보라색 선/삼각형 추출 표현은 사용하지 않는다. progress >.6 동안 head velocity에 `RNV*((p-.6)/.4*2)`를 매 tick 더한다. 완료 시 tail middle에 WaterDrip 4개(`life Random.Range(10,120)`, gravity .9)와 white Spark 5개(`4,18`)를 만든다.

**SoundID:** 첫 `.1` 통과 시 `SM_Spear_Pull`, pitch `1+random*.5`; 완료 시 `SM_Spear_Grab`, pitch `.5+random*1.5`.

**Desktop Implementation:** `SpearmasterAbilityController`, `SpearmasterTailSpecklesExtension`, `SlugcatGraphics`.

**Known Difference:** 원작의 2-slot `Player.grasps` 대신 현재 데스크톱 object layer는 한 개 needle grasp만 갖는다. 따라서 한 손에 다른 Rain World item을 든 채 반대 손로 생성하는 조합은 object catalog가 추가될 때까지 비활성이다.

### Needle throw and terrain physics

**Slugcat:** Spearmaster

**Ability:** original `ThrowObject` needle throw

**Original Classes:** `Player`, `Weapon`, `Spear`, `BodyChunk`

**Original Methods:** `Player.ThrowObject`, `Player.ThrownSpear`, `Spear.Update`

**Activation Condition:** held needle, throw edge, conscious. MMF upward throw와 같은 조건에서 Flip + vertical input일 때 세로 throw를 허용한다.

**Input Sequence:** extraction 완료 후 `Throw` edge. AI는 `Idle -> Moving -> PreparingSpear -> PullingSpear -> HoldingSpear -> Aiming -> Throwing -> Recovering` 상태를 거친다. 타깃이 없으면 `HoldingSpear`에서 유지하며, 타깃이 너무 가깝거나 멀면 거리부터 조정한다. 조준은 몸/팔/창 방향만 정하고 투척 뒤 유도는 없다.

**Counters:** spear age는 throw 후 증가한다. 별도 임의 lifetime 삭제를 두지 않는다.

**Physics Changes:** horizontal base velocity `(player.vx*.2 + dir*40, player.vy*.5 + originalY 1.5)`; Spearmaster skill 2에서 X ×1.2, 즉 정지 기준 48. vertical은 `(player.vx*.5, dirY*40)`. throw position `chest + dir*10 + originalY 4`. `ThrowObject`의 몸 반동도 chest `+dir*8`, hips `-dir*4`로 적용한다. spear chunk radius 5/mass .07, air friction .999, free gravity .9, thrown 상태의 순 중력 .45, bounce/surface friction .4, damage bonus 1.25다. 생성 직후 `Spear_makeNeedle(type,true)` 상태와 10-19 segment `Spear.Umbilical`을 유지한다.

**Graphics:** 완성 창도 절차적 대체 그림이 아니라 원본 `BioSpear1..3` atlas element를 직접 그린다. grasp 0의 실제 `SlugcatHand` 위치와 `spearDir` 보행 주기를 사용하고, 원본 `ChangeOverlap` 조건으로 몸 앞/뒤 layer를 바꾼다. 투척 뒤 5 tick 동안 던진 손은 spear를 추적하고 반대 손에는 `-dir*3` follow-through를 준다. connected needle은 white, disconnect 뒤에는 400 tick fade를 적용한다. `Thrown`/`StuckInCreature` pivot은 anchorY .85, 그 외는 .5이며 umbilical segment를 보간 렌더링한다.

**SoundID:** 추출 `SM_Spear_Pull`/`SM_Spear_Grab`, release `Slugcat_Throw_Spear`, 비행 `Spear_Thrown_Through_Air_LOOP`, free 회전 `Spear_Spinning_Through_Air_LOOP`, 지형 `Spear_Stick_In_Wall`/`Spear_Stick_In_Ground`/`Spear_Bounce_Off_Wall`, creature adapter `Spear_Stick_In_Creature`/`Spear_Damage_Creature_But_Fall_Out`/`Spear_Bounce_Off_Creauture_Shell`을 사용한다. loop는 mode 전이에 맞춰 시작/정지한다.

**Desktop Implementation:** `DesktopSpear`, `SpearmasterAbilityController.UpdateAfterMovement`.

**Known Difference:** Rain World tile stick target을 window top/side/monitor surface로 바꾼다. creature collision 결과/state adapter는 원작 SoundID와 needle disconnect를 보존하지만, 현재 데스크톱에는 실제 Rain World creature/feeding object graph가 없어 자동 호출되지 않는다. abstract spear persistence도 비활성이다.

---

## Rivulet

### Movement profile

**Slugcat:** Rivulet

**Ability:** complete available Player movement profile

**Original Classes:** `SlugcatStats`, `Player`, `BodyChunk`, `PlayerGraphics.AxolotlGills`

**Original Methods:** `SlugcatStats` constructor, `Player.MovementUpdate`, `Player.UpdateBodyMode`, `Player.Jump`, `Player.WallJump`, `PlayerGraphics.Update/DrawSprites`

**Activation Condition:** character selection itself. 별도 ability button이 없다.

**Input Sequence:** 모든 이동은 다른 캐릭터와 같은 `VirtualInput` x/y/jump sequence를 사용한다.

**Counters:** original animation frame/run cycle, jumpBoost와 contact state를 공통 Player state에서 사용한다.

**Physics Changes:** stats는 `runspeedFac=1.75`, `bodyWeightFac=.95`, `throwingSkill=1`, `lungsFac=.15`, `poleClimbSpeedFac=1.8`, `corridorClimbSpeedFac=1.6`, `swimBoostCost=.025`, `swimBoostCooldown=10`. Stand chest/hips dynamic speed는 `4.2/4.0 * runspeedFac`; Default 비서기 4(수직 입력 2.5), Crawl 2.5(수직 입력 1)이다. 일반 공중 가속은 다른 Player와 같은 2.4이며 결과 velocity multiplier를 쓰지 않는다. standing jump 6/5, wall jump chest/hips Y 10/9와 X 9/7, jumpBoost 4를 적용한다.

**Graphics:** 6개 AxolotlGill procedural part는 공통 PlayerGraphics step 뒤 같은 `timeStacker`로 렌더한다. Rivulet 속도용 추가 spring이나 smoothing은 없다.

**SoundID:** 공통 `Slugcat_Normal_Jump`, `Slugcat_Wall_Jump`, 이동/충돌 SoundID를 같은 simulation event에서 사용한다.

**Desktop Implementation:** `SlugcatProfiles.Rivulet`, `SlugcatMovement`, `RivuletGillsExtension`.

**Known Difference:** 데스크톱 지형에는 pole, narrow corridor, water volume이 없어 해당 stats 값은 보존되지만 활성 surface가 없다. 따라서 swim boost/수중 lung 결과는 실행되지 않는다. floor/wall/window movement에는 차이가 없다.

---

## Saint

### Tongue shoot, attach, retract and release

**Slugcat:** Saint

**Ability:** `Player.Tongue`

**Original Classes:** `Player`, `Player.Tongue`, `Rope`, `PlayerGraphics.RopeSegment`

**Original Methods:** `Player.ClassMechanicsSaint`, `Player.SaintTongueCheck`, `Player.TongueUpdate`, `Tongue.Update/Shoot/AutoAim/Release/Elasticity`

**Activation Condition:** conscious Saint, tongue Retracted, jump edge, not pickup, airborne/canJump<=0, not Crawl/Corridor/Shortcut/WallClimb/Swimming/Beam/Antler/Vine/ZeroGPole. Story `monkAscension`은 데스크톱에 없다.

**Input Sequence:** 기본 `(facing,.7).normalized`를 Y-down으로 `(facing,-.7)` 변환한다. up 입력은 `(0,1)` -> desktop `(0,-1)`. main velocity normalized ×.2를 더한 뒤 정규화한다.

**Counters:** mode `Retracted -> ShootingOut -> AttachedToTerrain -> Retracting -> Retracted`. `idealLength=150`, min 50, max 170, `totalRope=200`, shot request 140. attached tick 2 이후 jump edge release. attached에서 up/down은 ideal을 tick당 -3/+3, elastic은 tick당 -.05, request는 `(1-elastic)*2`만큼 ideal로 이동한다.

**Physics Changes:** Shoot pos `mouth+dir*5`, velocity `dir*70`, elastic 1. Shooting은 request -4/tick, 60 거리 이후 gravity-like `.9*InverseLerp(.8,0,elastic)`. rope excess에 strength `Lerp(.85,.25,elastic)`, position correction multiplier `Lerp(1,.5,elastic)`을 사용한다. attached terrain mass share 1, free tongue share 0; attached multiplier 1.1, free .7. release jump는 chest/hips original Y 8/7 -> desktop -8/-7, jumpBoost 8. rope path가 500을 넘거나 surface가 사라지면 release한다.

**Graphics:** `DesktopRope`는 최대 50 bend를 보존/해제한다. `PlayerGraphics.RopeSegment`와 같은 20개 segment, velocity .98, target velocity +.2, position Lerp .4, neighbor constraint와 arc-length claim을 사용한다. long mesh width는 `0.2 + 1.6*Lerp(1,stretch,sin(f*pi)^.7)`이며 마지막은 face connection이다. 모든 점은 동일 `timeStacker`로 보간한다. 기본 tongue HSL(.95..1, 1, .75..9) gradient와 fog 70% 식을 사용한다.

**SoundID:** `Tube_Worm_Shoot_Tongue`, `Tube_Worm_Tongue_Hit_Terrain`, `Tube_Worm_Detach_Tongue_Terrain`, jump release의 `Slugcat_Normal_Jump`.

**Desktop Implementation:** `SaintAbilityController`, `DesktopRope`, `SpriteRenderer.DrawAbilityObjects`.

**Known Difference:** 원작 tile raycast를 `Window Top`, `Window Side`, monitor/taskbar surface segment raycast로 바꾼다. `AttachedToObject`는 데스크톱 runtime에 attachable Rain World physical object가 없어 비활성이다. room palette가 없으므로 tongue fog input은 고정 neutral desktop fog color를 쓴다.

### AutoAim 규칙

원본 `Tongue.AutoAim`은 AI 편의용 target aim이 아니다. 230 ray가 clear하면 입력 방향을 그대로 쓰고, 막혔을 때만 ±5/10/15/20/25도 순서에서 첫 clear ray를 고른다. 데스크톱 구현도 같은 순서이며 목표 좌표나 창문 중심으로 보정하지 않는다.

**조사한 다른 Saint 분기:** Karma 9/Challenge ascension, ghost ping, Rubicon/void cutscene은 story/Karma/session 시스템이므로 제외한다. 일반 tongue 사용과 섞지 않는다.

---

## Gourmand

### Stats and exhaustion

**Slugcat:** Gourmand

**Ability:** movement weight and aerobic exhaustion

**Original Classes:** `SlugcatStats`, `Player`

**Original Methods:** `SlugcatStats` constructor, `Player.ClassMechanicsGourmand`, `Player.LungUpdate`, `Player.Jump`, `Player.TerrainImpact`

**Activation Condition:** character selection. exhaustion은 `aerobicLevel>=.95`에서 켜지고 `<.4`에서 꺼진다.

**Input Sequence:** 공통 이동 입력. 별도 stamina/ability 버튼은 없다.

**Counters:** `gourmandExhausted`, `aerobicLevel`, `slowMovementStun`을 그대로 분리한다. exhausted recovery denominator는 moving/idle 800/200, Crawl 400/125; normal은 1100/400. 모두 `(1+3*InverseLerp(.9,1,aerobic))`를 곱한다. exhausted slow stun은 `max(current,LerpMap(aerobic,.7,.4,6,0))`.

**Physics Changes:** bodyWeightFac 1.35, pole .8, corridor .86, throwingSkill 2. 일반 이동 수식은 공통 Player를 사용하며 피로 시 slowMovementStun만 반영한다. terrain impact 원본 death/stun threshold 80/40도 보존해 계산한다.

**Graphics:** body/hips scale, breath와 exhausted animation state는 같은 `SlugcatState`에서 나온다.

**SoundID:** 공통 movement/impact SoundID.

**Desktop Implementation:** `SlugcatProfiles.Gourmand`, `SlugcatMovement`, `GourmandAbilityController`.

**Known Difference:** 기존 데스크톱 펫의 전 캐릭터 공통 안전 어댑터는 원본 lethal terrain result를 진단 state에 기록한 뒤 실제 죽음 대신 최대 3초 stun으로 제한한다. Gourmand의 80/40 판정 자체는 변경하지 않는다.

### Roll

**Slugcat:** Gourmand

**Ability:** Roll

**Original Classes:** `Player`, `BodyChunkConnection`

**Original Methods:** `Player.TerrainImpact`, `Player.UpdateAnimation`, `Player.MovementUpdate`

**Activation Condition:** `downDiagonal!=0`, not already Roll, speed>12 또는 Flip/RocketJump, floor impact direction Y<0, `allowRoll>0`, `consistentDownDiagonal > (speed>24 ? 1 : 6)`.

**Input Sequence:** airborne/falling 동안 같은 diagonal down 입력을 유지해 `consistentDownDiagonal`을 쌓고 실제 terrain impact가 발생해야 한다. ability 버튼으로 직접 시작하지 않는다.

**Counters:** allowRoll 감소/air gap에서 15, rollCounter, rollDirection, stopRollingCounter. rollDirection이 있으면 connection distance 10, 200 tick 초과 또는 mode 이탈 시 reset.

**Physics Changes:** 시작 X velocity는 각 chunk `Lerp(vx,9*input.x,.7)`. 매 tick 양 chunk velocity ×.9, body perpendicular `2*rollDirection`을 반대로 적용, floor에서 각 X에 `1.1*direction`, AerobicIncrease(.01). 15/30/60, opposite input, exhausted, body orientation, blocked>6의 원본 종료 조건을 유지한다.

**Graphics:** `AnimationIndex.Roll`, body orientation에서 sprite pose와 roll loop pitch/volume을 계산한다.

**SoundID:** `Slugcat_Roll_Init`, `Slugcat_Roll_LOOP` pitch `.85..1.15`/volume `.5..1`, `Slugcat_Roll_Finish`.

**Desktop Implementation:** `GourmandAbilityController.TerrainImpact/UpdateBeforeMovement`.

**Known Difference:** tile slope/one-tile step 보정은 연속 window segment terrain에는 해당 tile이 없어 실행되지 않는다. window top/side contact와 gap이 roll의 terrain adapter다.

### Belly Slide

**Slugcat:** Gourmand

**Ability:** BellySlide

**Original Classes:** `Player`

**Original Methods:** `Player.UpdateBodyMode`, `Player.UpdateAnimation`

**Activation Condition:** DownOnFours, hips floor contact, `downDiagonal==flipDirection`.

**Input Sequence:** 바라보는 방향의 down diagonal을 유지한다.

**Counters:** rollDirection, rollCounter, exitBellySlideCounter, slowMovementStun.

**Physics Changes:** 첫 6 tick hips original `vy+=2.7`, `vx-=9.1*direction` -> desktop `vy-=2.7`. short slide chest force는 exhausted 14, normal 45에 `sin(counter/15*pi)`. 종료 후 X 절대값>8이면 velocity ×.5, success/fail slow stun 20/40.

**Graphics:** `AnimationIndex.BellySlide`에서 procedural body/limb pose를 사용한다.

**SoundID:** `Slugcat_Belly_Slide_Init`, loop `Slugcat_Belly_Slide_LOOP`, finish success/fail SoundID. retail `sounds.txt`에서 finish 두 ID는 주석 상태이므로 이벤트는 기록하지만 임의 WAV를 대체하지 않는다.

**Desktop Implementation:** `GourmandAbilityController.UpdateBellySlide`.

**Known Difference:** long belly slide/whiplash/rocket-jump는 heavy held weapon throw 상호작용이 필요한데 현재 held-object catalog가 needle 하나뿐이므로 비활성이다. narrow tile hole 진입도 window terrain에 대응하지 않는다.

### Impact, crafting, regurgitation, throwing

**Slugcat:** Gourmand

**Ability:** slam/roll collision, recipe crafting, regurgitation and heavy throwing

**Original Classes:** `Player`, `GourmandCombos`, `AbstractPhysicalObject`, `Creature`, `Weapon`

**Original Methods:** `SlugSlamConditions`, `Collide`, `CraftingResults`, `GraspsCanBeCrafted`, `SpitUpCraftedObject`, `Regurgitate`, `ThrowObject`, `ThrownSpear`

**Activation Condition:** 각 원본 grasp/creature/object predicate.

**Input Sequence:** 원본 recipe는 두 grasp와 up+pickup/crafting state를 요구한다.

**Counters:** `gourmandAttackNegateTime`, crafting state와 object-in-stomach state는 해당 object가 있을 때만 의미가 있다.

**Physics Changes:** roll/slide/falling body terrain 반응은 유지된다. Creature damage/stun과 heavy object throw bonus는 대상 object가 있을 때만 적용 가능한 구조다.

**Graphics:** 현재 object가 없으면 가짜 sprite를 생성하지 않는다.

**SoundID:** 실제 interaction이 없으므로 swallow/regurgitate/weapon impact SoundID도 거짓으로 발생시키지 않는다.

**Desktop Implementation:** `GourmandCraftingFramework`는 recipe와 두 input object를 받는 원본 구조를 유지하되 기본 recipe 목록은 비어 있다.

**Known Difference:** 데스크톱 런타임에 Rain World edible/abstract-object/creature catalog가 없어서 이 세 기능의 대상 집합이 비어 있다. 임의 item, recipe, damage target을 만들지 않는다.

---

## 통합 선택과 AI 제한

트레이 메뉴, 설정 창, 스킨 편집기, `--slugcat`은 모두 같은
`SlugcatProfiles.All` 목록을 사용한다. 목록은 정확히 `White`, `Yellow`, `Red`,
`Gourmand`, `Artificer`, `SpearMaster`, `Rivulet`, `Saint` 순서의 8종이다.
`Yellow`와 `Red`는 더 이상 `White` ID에 숨은 legacy 외형이 아니라 독립
`SlugcatId`/movement/graphics profile이다.

선택한 하나의 ID에서 stats, ability controller, audio profile, graphics profile을
함께 재구성한다. Downpour graphics만 따로 골라 White physics를 남기는 실행 경로는
없다. 기존 DMS part/color 편집은 선택된 profile 위의 시각 override일 뿐 physics나
ability를 바꾸지 않는다. 전환 시 이전 controller를 reset하고 ability effect, spear,
tongue/rope state, 대기 중 SoundEvent를 비운 뒤 전용 graphics extension 배열을 다시 만든다.

`DesktopPetAI`는 다음 원본 입력만 출력한다.

- Artificer: airborne `Jump+Pickup` 및 방향 입력
- Spearmaster: neutral `Pickup` 유지 후 `Throw` edge
- Saint: airborne `Jump` edge, attached 상태에서 rope length/release 입력
- Gourmand: 실제 낙하 중 diagonal 유지
- Rivulet: 공통 movement input

AI에서 velocity, anchor, spear target, rollDirection, cooldown을 직접 쓰는 경로는 없다.

## SoundID 해석

`RainWorldAudioEngine`은 특정 캐릭터용 WAV 이름을 하드코딩하지 않는다. 로컬 `StreamingAssets/soundeffects/sounds.txt`를 읽어 SoundID의 clip 목록, `PLAYALL`, `vol/minVol/maxVol`, `pitch/minPitch/maxPitch`를 해석한다. 능력 코드가 낸 호출 volume/pitch와 map 값을 곱하고 로컬 `AssetBundles/loadedsoundeffects` UnityFS의 AudioClip을 재생한다. 지원되지 않는 codec이나 주석 처리된 SoundID에는 대체음을 합성하지 않는다.

## Input replay 검증

`tests/RainWorldDesktopPet.Tests/AbilityParityReplayTests.cs`의 `AbilityInputReplay`는 tick마다 다음을 캡처한다.

- chest/hips position과 velocity
- `AnimationIndex`, `BodyModeIndex`
- ability debug state와 원본 대응 counter
- 생성 spear mode/position/velocity
- effect kind/count
- SoundID event

포함된 replay:

1. 정확한 8종 ID/profile/graphics 순서와 White/Yellow/Red CLI 호환 별칭.
2. 캐릭터 전환 시 Artificer effect/sound, SpearMaster needle, Saint tongue,
   Rivulet 전용 sprite extension 정리.
3. Artificer horizontal/up explosive jump chunk assignment와 parry counter.
4. SpearMaster 79 tick extraction, Pickup 유지 중 non-neutral progress freeze, `Spear_makeNeedle`, grasp 0 hand target, throw SoundID/velocity/body recoil, 5-tick follow-through, umbilical 10-19 segments, thrown 순 중력 .45.
5. SpearMaster 8단계 AI, 타깃 없는 hold, 투척 후 recovery 및 접촉 상태 전이당 1회 bounce sound.
6. Rivulet 공통 air-control과 stats-driven standing jump 6/5.
7. Saint shoot, window terrain attach, rope state, jump release.
8. Gourmand falling diagonal TerrainImpact roll gate, aerobic exhaustion/recovery.

이 테스트는 화면상 유사성 대신 발동 tick과 simulation state를 검증하며 기존 40 Hz/60-240 Hz 독립성 회귀 테스트와 함께 실행된다.
