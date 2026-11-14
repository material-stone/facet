# 컨디션 아이콘과 7일 게이지

[구조 결정](../decisions/0040-dashboard-condition-history.md). 자산은 `impls/Facet/DashboardConditions/Icons/`에 생성 원본 PNG로 저장한다. ConditionIcons만 pack URI로 읽고 228px로 디코딩해 Freeze한다. 대시보드는 제목 없이 76 DIP 아이콘과 상태 이름을 표시하며 최초 미기록 상태는 +와 기록하기로 표시한다. 아이콘·게이지·선택 목록은 포커스를 받지 않는다. 구간 클릭은 원본 기록 시각·종류의 별도 편집 팝업을 열며 날짜·시각 입력에서만 포커스를 허용한다.

| ID | 표시 | 게이지 색 | 표정 |
|---|---|---|---|
| good | 좋음 | #79CCAE | 민트·밝은 미소·작은 별 |
| okay | 괜찮음 | #90BDE5 | 하늘색·편안한 미소 |
| tired | 피곤함 | #B69ACF | 연보라·무거운 눈꺼풀 |
| sleepy | 졸림 | #8795D6 | 푸른 보라·감은 눈·작은 달 |
| unwell | 아픔 | #E9B18E | 복숭아색·이마의 쿨링 패치 |
| stressed | 스트레스 | #E58D9A | 코랄·걱정 표정·긴장 표시 |

2026-10-24 built-in imagegen으로 각 자산을 개별 생성했다. 스타일 참조는 같은 프로젝트의 `DashboardWeather/Icons/cloudy.png`이다. alpha를 유지하며 Python 보정·크롭·리사이즈를 하지 않는다.

## 공통 프롬프트

Generate ONE personal condition UI icon. Reference image is STYLE REFERENCE ONLY: match its cute puffy rounded 3D pastel mascot, soft shading, glossy tiny charcoal eyes, clean silhouette. Create a simple round cushion/blob face mascot (not a weather cloud). Condition: [ID]: [subject]. Same front-facing round silhouette and scale across set. Readable at 56px. Centered within central 72% of square canvas, generous padding. Fully transparent background, actual alpha. No text, numbers, letters, border, watermark, scenery or checkerboard.

## subject 치환값

- good: mint-ivory, bright happy eyes and cheerful smile, one tiny golden sparkle
- okay: pale sky-blue ivory, calm dot eyes and a gentle small content smile
- tired: pale lavender ivory, heavy half-open eyelids and tiny weary mouth, faint under-eye arcs
- sleepy: periwinkle ivory, closed sleeping eyes, small peaceful mouth and tiny crescent beside head, absolutely no letters or Z marks
- unwell: peach ivory, flushed cheeks, small white cooling patch on forehead and uneasy mouth, no crosses
- stressed: soft coral ivory, slightly furrowed brows and small worried mouth, two tiny curved tension strokes beside head

게이지는 하나의 연속 띠이며 최근 7일의 날짜별 경계만 표시한다. 최초 기록 전·미래에는 배경색을 채우지 않는다. 이전 범위에서 이어진 상태도 다음 선택 전까지 색을 이어간다. 각 구간 툴팁과 범례로 색상의 뜻을 확인한다. 최근 7일은 오늘을 포함하고 자정에 한 날짜씩 이동한다.
