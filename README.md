# Slugcat in My Monitor

> A procedural desktop companion inspired by the flexible movement of Rain World's slugcats.

모니터 위를 돌아다니고, 마우스로 잡아당기거나 던질 수 있는 슬러그캣 데스크톱 컴패니언을 만드는 오픈소스 프로젝트입니다.

> [!IMPORTANT]
> 이 프로젝트는 개발 초기 단계입니다. 아직 실행 가능한 데스크톱 앱은 제공하지 않습니다.

## 목표

- 유연한 몸통, 팔다리 IK, 꼬리 체인을 이용한 절차적 애니메이션
- 마우스로 잡기, 끌기, 늘이기, 던지기
- 투명하고 클릭 통과가 가능한 데스크톱 오버레이
- 멀티 모니터와 화면 경계 충돌
- [Dress My Slugcat](https://github.com/MatheusVigaro/DressMySlugcat) 형식의 스킨 불러오기

## 개발 방향

첫 번째 프로토타입은 다음 범위에 집중합니다.

1. 두 개의 몸통 물리점과 꼬리 체인 구현
2. 바닥 충돌과 기본적인 대기·걷기 행동
3. 잡기와 던지기 상호작용
4. DMS atlas 파싱과 스프라이트 조립
5. 투명 데스크톱 오버레이 적용

데스크톱 앱은 Godot 4 기반으로 개발할 예정입니다. DMS atlas 파서와 검증 도구는 엔진에 종속되지 않도록 분리합니다.

## 현재 구현

현재 저장소에는 DMS 템플릿 호환성 검증기가 포함되어 있습니다. 검증기는 다음 항목을 확인합니다.

- `metadata.json` 필수 필드
- 같은 이름의 PNG/TXT atlas 쌍
- 필수 신체 파트 atlas
- PNG 크기와 각 프레임 사각형의 범위

### DMS 템플릿 검증

요구 사항:

- Node.js 18 이상
- DMS 1.6.6 릴리스의 `ModTemplate.zip`

1. [`ModTemplate.zip`](https://github.com/MatheusVigaro/DressMySlugcat/releases/tag/1.6.6)을 다운로드합니다.
2. 저장소에서 다음 경로가 만들어지도록 압축을 풉니다.

```text
.local-test-assets/dms-template-1.6.6/
└─ dms_template/
   └─ dressmyslugcat/
      └─ template/
```

3. 검증을 실행합니다.

```bash
npm test
```

다른 위치의 대칭 DMS 스킨 폴더를 직접 검사할 수도 있습니다.

```bash
node tools/validate-dms-template.mjs "path/to/dressmyslugcat/skin"
```

## 프로젝트 구조

```text
.
├─ .github/                    # 이슈 및 PR 템플릿
├─ tools/
│  └─ validate-dms-template.mjs
├─ package.json
└─ THIRD_PARTY_TEST_ASSETS.md
```

## 기여하기

버그와 제안은 GitHub Issues에 등록해주세요. 코드를 변경하기 전 관련 이슈가 있는지 확인하고, 가능하면 작은 단위의 PR로 제출해주세요.

- 버그 제보 시 재현 과정과 실행 환경을 포함해주세요.
- 기능 제안 시 해결하려는 문제와 예상 동작을 설명해주세요.
- PR에는 실행한 테스트와 결과를 적어주세요.
- Rain World 또는 제3자의 에셋을 저장소에 추가하지 마세요.

## 에셋 및 상표 안내

이 저장소는 Rain World, Dress My Slugcat 또는 커뮤니티 스킨의 이미지와 게임 에셋을 배포하지 않습니다. 테스트용 외부 에셋은 로컬에서만 사용하며 Git에서 제외됩니다. 자세한 내용은 [`THIRD_PARTY_TEST_ASSETS.md`](THIRD_PARTY_TEST_ASSETS.md)를 참고하세요.

이 프로젝트는 비공식 팬 프로젝트이며 Videocult, Akupara Games 또는 Dress My Slugcat 제작진과 제휴하거나 이들의 승인을 받은 프로젝트가 아닙니다. Rain World 및 관련 명칭과 자산의 권리는 각 권리자에게 있습니다.

## 라이선스

프로젝트 코드는 [`MIT License`](LICENSE)로 배포됩니다. 제3자 에셋에는 프로젝트 코드의 라이선스가 적용되지 않습니다.
