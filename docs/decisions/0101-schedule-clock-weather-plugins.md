# 0101 — 시계·날씨·일정을 플러그인으로

2026-11-14: 순정 Facet에는 앱 자체 위젯을 두지 않는다. 시계·날씨는 이미 레지스트리에 있는 플러그인으로, 달력·일정·오늘 일정은 새 일정 플러그인(`Facet.SchedulePlugin`, `facet.schedule`)으로 제공한다. [0073](0073-widget-grid-and-plugins.md)의 내장 위젯과 [0076](0076-calendar-widget-and-store.md)의 `calendar.dat`을 대체한다.

## 결정

- **기본 레이아웃:** 새 설치는 빈 대시보드와 설정 탭만 있다. 저장된 레이아웃의 `builtin.clock`·`builtin.weather`·`builtin.agenda`·`builtin.calendar` 배치는 `PluginData`가 한 번 `plugin.facet.clock.clock`·`plugin.facet.weather.weather`·`plugin.facet.schedule.agenda`·`plugin.facet.schedule.calendar`로 바꾼다. 자리·크기·위젯 저장소(시계 정렬)는 그대로다. 플러그인을 설치하기 전에는 자리 표시로 보인다. 한 번도 편집하지 않은 레이아웃에 일정이 있으면 예전 기본 레이아웃(`LegacyDefault`)을 플러그인 위젯 ID로 남긴다.
- **일정 플러그인:** 달력·하루 화면·일정 편집기, 오늘 일정, `facet_today`·`facet_list_schedules`·`facet_add_schedule`·`facet_delete_schedule`을 갖는다. 화면 코드는 앱에서 그대로 옮겼다. 저장은 플러그인 저장소 `plugin-data/facet.schedule.dat`에 `CalendarStoreFile` 형식으로 한다. 규칙은 Core `CalendarNotes`·`DaySchedules`·`DashboardAgenda`·`AgentToolCatalog`의 순수 함수를 그대로 쓴다.
- **Core 공유:** 플러그인이 같은 규칙을 다시 쓰지 않도록 `PluginHost`가 `Facet.Plugins`와 함께 `Facet.Core`도 앱의 것을 넘긴다. Core는 WPF·파일·암호화 의존이 없는 순수 라이브러리이므로 계약 표면이 크게 늘지 않는다. 앱 어셈블리는 여전히 넘기지 않는다.
- **이전:** `Store.Load`가 `calendar.dat`과 `records.dat`에 남은 날짜·프리셋을 기록 백업 후 플러그인 저장소로 옮기고, 다시 읽어 같을 때만 `calendar.dat`을 `calendar.dat.moved`로 바꾸고 기록에서 지운다. 플러그인 저장소에 이미 있는 날짜와 프리셋이 이긴다. 플러그인 저장소를 읽을 수 없으면 아무것도 바꾸지 않는다.
- **계약 추가:** `IWidgetLayer`(위젯이 소유하는 오버레이 전체 레이어, 위젯 모달 아래)와 `IWidgetInput`(선택된 탭의 위젯이 Esc·키를 받고, 탭 이동·숨기기·Ctrl+S 전에 모든 위젯이 `TrySave`) 을 `Facet.Plugins`에 둔다. `CachedShadow`는 앱과 플러그인이 함께 쓰도록 `Facet.Plugins`로 옮긴다.
- **앱에 남는 것:** 사용자 알림(`UserReminders`, 알림 파이프, `Facet.Cli`, `facet_add_reminder`)과 날씨 클라이언트(월드 배경의 날씨, `facet_weather`)는 앱에 남는다. 대시보드 화면(`OverlayDashboardView`), 날씨 그림, 달력 캐러셀은 지운다.

## 대가

- 오늘 일정 위젯은 플러그인 저장소의 일정·할 일만 보이고 앱의 사용자 알림은 보이지 않는다. 알림 토스트는 그대로 뜬다. `facet_today`의 `agenda`도 같다.
- 일정 편집기 배경은 오버레이 배경색 설정 대신 스킨의 `Bg` 색(투명도 184)을 쓴다. 플러그인은 앱 설정을 읽지 않는다.
- 일정 플러그인 폴더에는 노트 Markdown용 `Markdig.dll`이 함께 들어간다.