# Visual Design: Slugcat Command Wheel

**Concept-Derived Visual Tags**: #render-pixel-step, #geometry-organic-radial, #motionviz-slow-bloom

## 1. Visual Concept

Rain World의 어둡고 계단진 메뉴 문양이 슬러그캣의 몸에서 조용히 피어나는 명령 휠.

## 2. Color Palette

| Role | Color | Hex | Usage |
|:---|:---|:---|:---|
| 투명 배경 | Clear | #00000000 | 슬러그캣과 바탕 화면을 가리지 않는 중앙과 섹터 간격 |
| 비활성 면 | Charcoal | #2A2C2D | 반투명 기본 명령 섹터 |
| 선택 면 | Soft graphite | #303335 | 현재 명령을 테두리 없이 구분 |
| 호버 면 | Ash | #565A5C | 천천히 명도와 불투명도가 올라가는 섹터 |
| 문자/표식 | Bone grey | #E8EBE8 | 단색 픽셀 문자와 작은 현재 명령 표식 |

## 3. Object Rendering Specifications

- 명령 휠의 도넛 면은 절반 해상도의 투명 캔버스에 렌더한 뒤 2배 최근접 확대한다. 글자는 가독성을 위해 최종 해상도에서 별도로 렌더링한다.
- 세 섹터는 외곽선 없이 투명한 각도 간격으로 분리한다. 중앙은 완전히 비워 슬러그캣의 얼굴과 행동을 가리지 않는다.
- 도넛 면은 2×2 픽셀 격자를 유지하되 글자는 최종 해상도에서 부드러운 그리드 맞춤 안티앨리어싱으로 별도 렌더링한다. 한국어는 가독성 좋은 고정폭 글꼴, 영어는 설치된 `RAIN WORLD MENU` 글꼴을 우선 사용한다.
- 현재 명령은 외곽선 대신 약간 밝은 면과 안쪽의 2×2 논리 픽셀 표식으로 표시한다.

## 4. Background & Environment

별도 배경 패널, 그림자, 장식 프레임을 만들지 않는다. 반투명 섹터 사이로 바탕 화면과 슬러그캣이 그대로 보여야 하며, UI는 생물 위에 붙은 HUD가 아니라 순간적으로 나타나는 Rain World식 문양처럼 보인다.

## 5. Feedback Effects

| Event | Visual Response | Tag Reference |
|:---|:---|:---|
| 메뉴 열기 | 중심에서 0.38배로 시작해 0.5초 동안 페이드와 크기가 함께 완만하게 증가 | #motionviz-slow-bloom |
| 명령 호버 | 0.23초 동안 면이 조금 커지고 명도와 불투명도가 상승 | #motionviz-slow-bloom |
| 호버 해제 | 0.23초 동안 원래 면으로 복귀 | #motionviz-slow-bloom |
| 명령 선택 | 선택 면과 작은 픽셀 표식이 다음 메뉴 호출 때 현재 상태를 표시 | #render-pixel-step |
| 메뉴 닫기 | 0.5초 동안 중심으로 수축하며 사라짐 | #geometry-organic-radial |

## 6. Relationship with Visual Tags

`#render-pixel-step`은 저해상도 최근접 확대와 단일 비트 글꼴을, `#geometry-organic-radial`은 슬러그캣 중심의 비어 있는 도넛 실루엣을, `#motionviz-slow-bloom`은 급격한 팝업 대신 시작과 끝의 속도가 모두 0에 가까운 완화 곡선을 결정했다.

## 7. AI-Generated Look Suppression Rules

### 7.1 Visual Hierarchy Rules

- Protagonist: 도넛 중앙에 그대로 보이는 슬러그캣
- Threat: 없음; 명령 UI는 위협 경고가 아니다
- Reward: 호버되거나 현재 선택된 명령 섹터
- 2-second recognition check: 2초 안에 슬러그캣, 세 명령, 현재 호버 면의 순서가 읽혀야 한다

### 7.2 Limits on Familiar Template Symbols

- Adopted familiar elements (max 2): 방사형 메뉴, 밝기 기반 호버
- Replaced unique element: 일반 아이콘과 카드 테두리를 Rain World식 픽셀 링 조각과 작은 상태 표식으로 대체

### 7.3 UI-Independent Feedback

| Event | Non-UI visual response | Intensity (Low/Med/High) |
| :---- | :--------------------- | :----------------------- |
| Score | 해당 없음 | Low |
| Damage | 해당 없음 | Low |
| Near miss | 해당 없음 | Low |
| 명령 상태 | 슬러그캣의 실제 이동, 정지, 시선 및 자세 변화 | Med |

### 7.4 Composition and Gaze Guidance

- Initial focal point: 도넛 중앙의 슬러그캣 얼굴
- Visual flow: 슬러그캣에서 바깥쪽 세 명령으로 방사형 이동
- Anti-center-clutter implementation: 중앙 링을 완전히 투명하게 유지하고 외곽선과 중앙 아이콘을 사용하지 않음
