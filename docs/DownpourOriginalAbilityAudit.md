# Downpour original ability audit

이 문서는 Artificer와 Spearmaster 능력 구현에 사용한 로컬 원작 근거와 데스크톱 좌표 변환을 고정한다.

## 조사 대상

- 설치본: Rain World v1.11.8
- managed assembly: `RainWorld_Data/Managed/Assembly-CSharp.dll`
- SHA-256: `B6BE1D4E18CE219D21091B51564CB6A11C1E4106B41DE903EB8E58849CB16FDB`
- asset source: `RainWorld_Data/resources.assets`
- 확인한 원본 element: `BioSpear1`, `BioSpear2`, `BioSpear3`

분석 대상은 `Player.ClassMechanicsArtificer`, `Player.GrabUpdate`, `Player.ThrowObject`, `Player.ThrownSpear`, `Weapon.Thrown`, `Weapon.HitWall`, `Spear.Spear_makeNeedle`, `Spear.Update`, `Spear.DrawSprites`, `Spear.Umbilical`, `Explosion.ExplosionSmoke`, `Explosion.ExplosionLight`, `Spark`, `ShockWave`다.

## Artificer

폭발 점프와 parry 공통 생성물은 다음과 같다.

- `ExplosionSmoke` 8개: `RNV * 5 * random`, size 1
- `ExplosionLight`: radius 160, alpha 1, life 3
- `Spark` 10개: 위치 `center + RNV * random * 40`, 속도 `RNV * Lerp(4,30,random)`, life 4/18
- `Fire_Spear_Explode`: volume `.3 + random*.3`, pitch `.5 + random*2`
- parry에만 `ShockWave(200,.2,6,false)` 추가

`ExplosionSmoke`는 lifeTime 170-400, rad `.6-1.5`, rotation/rotVel, 목표점 drift, 두 `FireSmoke` sprite layer를 사용한다. `Futile_White`의 16px quad에 맞춰 world radius에는 scale의 8배를 적용한다. `Spark`는 4-frame 위치 history, gravity `.4-.9`, terrain bounce `.5`를 사용한다. 특히 원본 생성자는 `Random.Range(0, 4)`의 0을 허용하며, 그 Spark는 추가된 프레임에는 보이고 다음 `Update`에서 소멸한다. 살아 있는 Spark의 `TriangleMesh` alpha는 줄지 않고 마지막 10%에서만 trail 길이가 줄어든다. 이 수명식과 layer를 `AbilityEffect`/`SpriteRenderer` adapter가 보존한다. 연기 객체의 170-400 tick 수명도 그대로 두되, D3D11 절차형 fragment에서 원본의 `dist`, 세 noise 결합, noise 감산과 `.35` discard 구조를 근사한다. 낮아진 `life^1.8 × .8/.6`가 discard 문턱을 통과하지 못하면 객체가 살아 있어도 보이지 않는다. 절차형 noise는 입자의 UV와 고정 seed에 결합해 이동 중 마스크가 바뀌지 않으며, discard 경계만 화면 미분값으로 안티앨리어싱한다. 설치 texture를 CPU로 읽거나 GPU 결과를 readback하지 않고 DirectComposition 효과 surface에 직접 합성한다.

이 분기에는 `Room.ScreenMovement`가 없다. 따라서 폭발 점프에 임의 카메라 흔들림을 추가하지 않는다. 원작 room object graph가 필요한 `InGameNoise(8000)`과 주변 creature/weapon parry 반사는 데스크톱에 대상이 없으므로 실행하지 않는다.

## Spearmaster needle

완성 needle은 별도 대체 projectile이 아니다. 원작과 같이 radius 5, mass `.07`인 `Spear` 상태에 `Spear_makeNeedle(type,true)`를 적용한 구조다. connected 상태는 white `BioSpear{type+1}` element와 10-19 segment `Spear.Umbilical`을 만들고, disconnect 뒤 fade counter 400을 사용한다. Spearmaster throw damage bonus는 1.25다.

추출 progress는 `<.1: Lerp(p,.11,.1)`, 이후 `Lerp(p,1,.05)`, `>.95` 완료이며 고정 입력에서 0-based tick 79에 생성된다. `SM_Spear_Pull`과 `SM_Spear_Grab`, 4 WaterDrip/5 Spark, head impulse도 같은 tick/범위를 사용한다.

투척은 horizontal 정지 기준 X 48, Y -1.5(데스크톱 Y-down), vertical 40, free gravity `.9`, thrown 순 중력 `.45`, air friction `.999`, bounce/surface friction `.4`다. `ThrowObject`의 chest `+dir*8`, hips `-dir*4` 반동과 `PlayerGraphics`의 5-tick hand follow-through도 적용한다.

파지는 grasp 0의 실제 `SlugcatHand` 위치를 사용한다. Stand의 `spearDir` 보행 주기와 `ChangeOverlap` 조건을 적용해 창이 몸 앞/뒤 layer를 바꾸며, tail speckle의 선택 위치·성장 tint·Y-down 수직 벡터도 `TailSpeckles.DrawSprites` 변환을 따른다.

## 접촉 전이와 오디오

지형 충돌은 시간 debounce로 막지 않는다.

1. `Thrown`의 투척 방향 최초 접촉만 검사한다.
2. speed 10 이상이며 throw 지점에서 140 미만이거나 33% stick 판정을 통과하면 `StuckInWall` + `Spear_Stick_In_Wall`이다.
3. 실패하면 `Free` + `Spear_Bounce_Off_Wall` + 7 Spark다.
4. 이미 `Free`인 접촉은 bounce sound를 다시 만들지 않는다.
5. `Free`가 바닥에서 20 tick 정지하면 `StuckInGround` + `Spear_Stick_In_Ground`다.

비행 loop는 `Thrown`에서 `Spear_Thrown_Through_Air_LOOP`, `Free`에서 `Spear_Spinning_Through_Air_LOOP`이며 mode 전이에서 실제 audio loop를 시작/정지한다. release는 `Slugcat_Throw_Spear`다.

## AI와 검증

자율 행동은 `Idle`, `Moving`, `PreparingSpear`, `PullingSpear`, `HoldingSpear`, `Aiming`, `Throwing`, `Recovering` 상태를 사용한다. 타깃이 없으면 창을 든 채 유지하고, 유효 거리 밖이면 이동한 뒤 충분한 aim/recovery 시간을 거친다.

Release replay는 effect 수명/개수, needle flag/damage/umbilical, throw/flight SoundID, AI 전 상태, targetless hold, 장거리 wall bounce 뒤 반복 sound가 없음을 검증한다.
