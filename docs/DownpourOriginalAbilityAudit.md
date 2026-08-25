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

1. `Thrown` 상태에서는 투척 방향과 정렬된 최초 지형 접촉만 wall-stick 후보로 검사한다.
2. impact speed가 10 이상이며 throw 지점에서 140 미만이거나 33% stick 판정을 통과하면 `StuckInWall` + `Spear_Stick_In_Wall`이다.
3. wall-stick 판정에 실패하면 `Free`로 전환하고 원본 범위의 회전을 시작하며 `Spear_Bounce_Off_Wall` + 7 Spark를 발생시킨다.
4. 수평 투척 등에서 바닥 접촉이 투척 방향과 정렬되지 않은 경우에는 `StuckInWall`로 남지 않고 `Free`로 전환한다.
5. `Free` 상태의 창은 별도의 `StuckInGround` mode를 사용하지 않는다. 원작 `Weapon.Mode`와 같이 `Free` 상태를 유지한 채 회전을 멈추고 원본의 지면 정착 각도 범위 `-50..50° + 180°`를 데스크톱 Y-down 좌표계로 변환하여 대각선 방향으로 놓인다.
6. 지면 정착 시 `Spear_Stick_In_Ground`를 한 번 발생시키며, 이후 정지한 `Free` 상태의 창은 같은 충돌음을 반복해서 생성하지 않는다.
7. 던져진 Spearmaster needle은 15초 동안 유지되며 마지막 0.5초 동안 자연스럽게 fade한 뒤 제거된다.

connected needle의 `Spear.Umbilical`은 투척 시 10-19개의 segment로 생성된다. 각 segment는 원작과 같이 life 2에서 시작하고 150-200 tick 범위의 개별 decay를 가진다. needle이 disconnect되어도 umbilical 전체를 즉시 제거하지 않고 각 segment의 life가 소진될 때까지 끊어지는 형태로 남는다.

비행 loop는 `Thrown`에서 `Spear_Thrown_Through_Air_LOOP`, 회전 중인 `Free`에서 `Spear_Spinning_Through_Air_LOOP`을 사용한다. mode와 속도 상태가 바뀌면 해당 loop도 종료된다. release sound는 `Slugcat_Throw_Spear`다.

## AI와 검증

Spearmaster의 자율 행동은 `Idle`, `Moving`, `PreparingSpear`, `PullingSpear`, `HoldingSpear`, `Aiming`, `Throwing`, `Recovering` 상태를 사용한다.

AI는 일정 시간이 지나면 확률 gate에 막히지 않고 반드시 명시적인 needle extraction sequence에 진입한다. 생성된 창은 즉시 던지지 않고 `HoldingSpear` 상태로 유지한다.

마우스 click-attention으로 유효한 타깃이 들어오면 원작의 방향 정렬 과정을 따라 `Aiming -> Throwing`으로 전환할 수 있다. 타깃 거리는 약 50-550 범위에서 유효하며, AI는 먼저 타깃 방향으로 몸을 돌린 뒤 다음 정렬된 update에서 throw input을 발생시킨다.

마우스 타깃이 없는 경우에도 창을 영구적으로 들고 있지는 않는다. 각 Spearmaster 개체는 `HoldingSpear` 진입 시 독립적인 자율 투척 cooldown을 가지며, cooldown이 끝나면 현재 facing을 중심으로 로컬 타깃을 생성하여 동일한 `Aiming -> Throwing` sequence를 수행한다. 따라서 여러 Spearmaster가 동시에 존재해도 동일한 시점에 창을 뽑거나 던지도록 강제되지 않는다.

투척 후에는 짧은 `Recovering` 상태를 거쳐 다시 `Idle`로 돌아가며, 다음 spear extraction까지의 대기 시간은 개체별로 달라진다.

Release replay는 다음을 검증한다.

- Artificer effect의 생성 개수와 수명
- needle flag, damage bonus, umbilical 생성 및 disconnect 후 decay
- throw/flight SoundID와 지형 충돌 sound
- Spearmaster extraction의 원본 progress timing
- tail marker와 `BioSpear` 성장 표현
- 투척 속도, 중력, 반동과 5-tick hand follow-through
- 던져진 needle의 15초 lifespan과 마지막 0.5초 fade
- `Free` spear의 원본 대각선 지면 정착 각도
- `Idle -> Moving -> PreparingSpear -> PullingSpear -> HoldingSpear -> Aiming -> Throwing -> Recovering` 상태 전이
- 마우스 타깃이 없는 상태에서도 cooldown 이후 자율 투척이 발생하는지
- 장거리 wall bounce 후 동일 충돌 sound가 반복되지 않는지
