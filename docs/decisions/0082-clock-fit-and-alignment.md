# ADR 0082: 시계 위젯의 맞춤 배치와 9방향 정렬

## 결정

- 내장 시계(`builtin.clock`)와 요일 시계 플러그인은 타일 전체를 채우는 버튼이다. 누르면 `IWidgetModal`로 9개 버튼의 정렬 선택 창을 연다.
- 시계 면은 300x108 비율을 유지한 채 타일에 들어가는 최대 크기로 확대·축소한다(Uniform). 짧은 쪽이 가득 차고 남는 축에서 선택한 정렬을 적용한다. 숫자·요일에 따라 비율이 변하지 않도록 날짜 줄도 고정 틀(300x38)을 쓴다.
- 정렬은 배치별 `WidgetStores`의 `alignment` 키(`top-left` 형식)에 즉시 저장한다. 위젯을 제거하면 함께 지워지고, 누락·알 수 없는 값은 `top-left`다. 저장 실패 시 이전 정렬로 되돌리고 창에 안내한다.
- `ClockFitButton.cs`는 Desktop(`impls/Facet/Widgets`)이 소유하고 `Facet.ClockPlugin`이 source link로 공유한다. 플러그인 없이도 앱이 빌드되도록 2026-11-14에 소유를 옮겼다([0098](0098-installer-winget-plain-build.md)). 플러그인 계약은 바꾸지 않는다.

## 결과

내장 시계에도 저장소가 생길 수 있으므로 `PlacedWidget.Data`는 정렬을 저장한 뒤 null이 아닐 수 있다.
