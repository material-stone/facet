# Facet 기록 디자인 시스템

- [Facet 앱 아이콘과 재생성](app-icon.md)

- [컨디션 아이콘과 최근 7일 게이지](condition-icons.md)

- [대시보드 날씨 아이콘과 imagegen 프롬프트](weather-icons.md)
- [수동 날씨 지역 자료와 출처](weather-regions.md)

## 목표
은은한 배경 하이라이트를 적용하는 UI는 기본·포커스·호버 테두리를 제거하고 배경을 테두리 여백 없이 요소 전체에 채운다. 선택한 날짜·컨디션 등 상태를 나타내는 표시는 유지한다. 입력란은 공통 `TextBoxWithoutFocusOutline` 스타일과 기존 스크롤·텍스트 편집 동작을 사용한다.

오버레이 대시보드의 시계·날씨·컨디션과 날짜별 기록을 확인하고 달력·일정·빠른 실행으로 이동한다. 입력 화면은 아이콘과 짧은 이름으로 훑어본다. 항목의 뜻은 도움말에서, 입력 예시는 빈 양식 안에서 확인한다. 상세 계획과 기존 기록은 보존한다.

## 참고 사례와 적용
- [GOV.UK Task list](https://design-system.service.gov.uk/components/task-list/): 작업 이름과 상태를 분리하는 목록. 오늘 목록의 짧은 제목과 기록 상태에 적용.
- [GOV.UK Details](https://design-system.service.gov.uk/components/details/): 보조 설명을 필요할 때 펼치기. 보조 안내와 설정 설명에 적용.
- [GOV.UK Question pages](https://design-system.service.gov.uk/patterns/question-pages/): 질문과 입력에 집중. 수면의 핵심 입력을 우선하고 추가 기록을 접는다.
- [Atlassian Design tokens](https://atlassian.design/foundations/design-tokens): 공통 의미 기반 리소스. WPF 리소스 사전으로 팔레트와 컴포넌트를 분리한다.
- [Day One](https://dayoneapp.com/features/): 기록 제품 참고. 일자별 기록 접근을 유지한다. 시각 디자인을 복제하지 않는다.

## 토큰
`impls/Facet/Design/Tokens.xaml`이 원본이다.

| 토큰 | 값 | 용도 |
|---|---|---|
| Bg | #141A1D | 전체 배경 |
| OverlayBackdrop | #99141A1D | 전체 화면 오버레이의 반투명 배경 |
| Panel | #1E272B | 기록 카드 |
| Ink | #E6EEEB | 본문 |
| Muted | #A9BAB4 | 날짜·보조 정보 |
| Mint | #82CFAD | 주 행동·선택 강조 |
| ScheduleNow | #FFF0A8 | 일정 다이얼의 현재 시각 바 |
| Edge | #46565D | 경계 |
| Danger | #FF9D92 | 오류 |
| CalendarSunday / CalendarSaturday | #FF9D92 / #91BFFF | 달력 일요일 / 토요일 |
| SpaceSmall / Medium / Large | 8 / 16 / 24 | 간격 단위 |
| TypeCaption / Body / Heading | 12 / 14 / 20 | 글자 위계 |

`Components.xaml`에서 입력·버튼·탭·목록·카드 스타일을 공유한다. 기본 입력 높이는 패딩으로 확보하고 키보드 초점을 테두리로 표시한다. 상태는 색뿐 아니라 텍스트로 표시한다.

## 스크롤바

`impls/Facet/Design/Scrolling.xaml`의 공통 ScrollViewer/ScrollBar 스타일이 앱 전체 기본값이다. 세로·가로 모두 두께 4 DIP·끝 반경 2 DIP의 막대를 사용하며 화살표와 장식 트랙은 표시하지 않는다. 실제 막대가 Visible인 축에만 20 DIP의 전용 열/행을 확보해 내용과 겹치지 않게 한다. Auto 막대가 필요 없거나 Disabled/Hidden인 축은 공간을 두지 않는다. 잡기 영역은 전체 전용 폭이며 hover는 밝기, drag는 Mint로 표시한다.

앞으로 추가하는 ScrollViewer·TextBox·ListBox·ComboBox도 이 디자인을 사용한다. 별도 스크롤바 템플릿을 복사하지 않는다. 일반 ScrollViewer는 암시적 스타일로 적용되며 커스텀 템플릿의 내부 호스트는 `RoundedScrollViewer`를 명시한다. TextBox의 `PART_ContentHost`와 목록의 `CanContentScroll`, 세로·가로 스크롤 명령은 유지한다. 결정 근거는 [0026](../decisions/0026-shared-scrollbars.md)과 조건부 거터로 갱신한 [0036](../decisions/0036-planner-edit-modes.md)에 있다. ComboBox 펼침 표시는 기존 벡터 Chevron 아이콘을 사용한다. 선택할 수 없는 항목은 입력을 차단하고40% 진하기로 표시한다.

## 화면 규칙
앱 전체 글꼴은 `Design/Tokens.xaml`의 `AppFontFamily`에 선언한 Pretendard다. 본문·입력·버튼·후보·도움말·툴팁·팝업·노트의 코드 표시와 직접 그리는 차트·일정·디버그 텍스트에 같은 서체를 사용한다. 공통 Control 스타일을 재사용하고 새 화면에도 적용한다. 런처 입력란과 고스트 자동완성은 기존 Pretendard Bold 28 DIP를 유지하며 나머지 크기·굵기도 유지한다.

`impls/Facet/Design/Fonts/`에 Regular·Medium·SemiBold·Bold TTF를 WPF resource로 포함해 시스템 글꼴 설치에 의존하지 않는다. `Design/Typography.cs`가 C# 그리기와 WinForms 트레이 메뉴의 글꼴 로드를 소유한다. 트레이는 같은 Regular resource를 프로세스 내부 GDI/GDI+ 메모리 글꼴로 사용하고 메뉴 종료 때 해제한다. 원본은 [Pretendard v1.3.9](https://github.com/orioncactus/pretendard/releases/tag/v1.3.9)의 `public/static/alternative/`이며 원본 SIL OFL 라이선스·저작권 고지는 같은 폴더의 `Pretendard-LICENSE.txt`로 배포한다.

- 대시보드: 7/14일 수면 그래프와 작업 단계 다이어그램을 넓은 창에서 나란히, 좁은 창에서 세로로 배치. 결측과 실제 0을 구분하고 날짜별 수치를 제공.
- AI 로드맵: 목표 → 앱 내부 Codex 분석 → 추가/수정 카드 → 선택 적용. 원시 요청문·JSON 편집은 노출하지 않는다. 적용 버튼은 결과 스크롤과 분리해 고정한다.
- 양식: 벡터 아이콘+짧은 이름 → 입력. 긴 의미 설명은 옆 도움말 버튼으로 옮긴다. 예시는 실제 입력값으로 저장하지 않는다.
- 오늘: 날짜, 완료 수, 짧은 작업 목록, 상태·메모. 실행 방법과 최근 기록은 접기.
- 수면: 잠 시도·잠드는 데 걸린 시간·최종 기상·각성 시간·졸림·메모 우선. 나머지는 추가 기록.
- 제작: 프로젝트·작업 이름·현재 단계·실제 결과·다음 행동을 순서대로 작성. 문제/가설/완료 기준은 작업 설계에서 펼쳐 본다. 긴 입력은 내부 스크롤.
- 저장은 오늘 편집기 또는 나머지 화면의 하단에 하나만 둔다. Ctrl+S 유지.
- 상태창도 동일한 팔레트 사용. 장식 문구 제거.
- 전체 화면 오버레이: 상단 중앙의 둥근 플로팅 탭, 화면 중앙 월 달력. 선택 탭은 Selected/Mint, 오늘은 Mint 테두리와 Selected 배경, 일요일/토요일은 전용 주말 토큰 사용. 작은 화면에서는 달력을 균일하게 축소한다.
- 임상 안내·버전별 테스트 범위는 숨김 처리만 하며 데이터와 의미를 변경하지 않는다.

## 검증
2026-10-15: Release 빌드, Core 26개 검사 통과. 별도 데이터 경로로 오늘·수면·제작 WPF 창을 확인. 원래 사용자 기록에 테스트 입력을 추가하지 않음.

당시 배포: 이전 1.4 버전 배포본에 기본 실행 스크립트를 연결했다. 현재 실행은 루트의 `Start-Facet.cmd`를 사용하며 작업 표시줄에는 `artifacts/Facet/Facet.Launcher.exe`를 고정한다.

## 다크 모드 (1.2.1)
사용자 요청으로 기본 테마를 다크로 전환. 입력 #172024, 선택 #304C42, 강조 위 글자 #102C21. 드롭다운(편집 가능 포함)과 날짜 입력에 명시적 테마를 적용하고 Windows 제목 표시줄도 어둡게 표시한다. 기존 기록과 화면 구조는 유지한다.
\n## 아이콘\nDesktop/Design/Icon.cs가 24px 기반 벡터 아이콘을 그린다. 아이콘은 기존 벡터를 사용하며 요청한 프리텐다드 글꼴은 라이선스와 함께 앱에 포함한다. 주요 기능은 아이콘과 짧은 이름을 함께 쓰며 아이콘 버튼에는 접근성 이름과 도움말을 제공한다. 상태는 색만으로 구분하지 않는다.\n
