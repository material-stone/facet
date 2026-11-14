# 컨디션을 플러그인과 전용 저장소로 분리

2026-11-05: 컨디션 위젯과 7일 컨디션 위젯을 앱에서 빼 `Facet.ConditionPlugin`(`teum.condition`)으로 옮긴다. 플러그인을 연결하면 `컨디션`(`current`)과 `7일 컨디션`(`history`) 두 위젯과 `facet_set_condition`·`facet_list_conditions` 도구를 쓸 수 있다. ADR 0040·0046의 컨디션 동작은 소유 위치만 플러그인으로 바뀐다.

- **저장소:** 플러그인 계약에 `IWidgetRegistry.Store`(`IPluginStore`)를 더했다. 앱이 `<data>/plugin-data/<플러그인 ID>.dat`을 DPAPI·원자적 교체·`.bak1-5`로 관리하고 플러그인은 문자열 전체를 읽고 쓴다. 기록은 계속 늘어나 값당 65,536자 제한이 있는 플러그인 설정에 맞지 않는다. 플러그인 폴더 안의 파일은 폴더를 지우거나 새 빌드로 덮어쓸 때 건강 기록이 함께 사라질 수 있어 쓰지 않는다. 플러그인을 끄거나 폴더를 지워도 기록은 남는다.
- **이관:** `Store.Load`가 `records.dat`의 `Conditions`를 찾으면 `backups/records-before-condition-store-*`로 백업하고, 저장소에 합쳐 쓰고, 다시 읽어 확인한 뒤에야 `records.dat`에서 비운다. 플러그인을 켜지 않아도 앱 시작 때 옮긴다. 같은 시각이면 저장소 쪽이 이긴다. 실패하면 달력 이관과 같이 시작을 멈추고 원본을 보존한다.
- **앱에 남는 흔적:** 입력 전용 `AppData.Conditions`, `ConditionEntry`, `LegacyConditions`, `PluginData.MoveConditionWidgets`뿐이다. 옛 기록을 옮기는 용도로, 그런 기록이 없어지면 지운다. 저장된 `builtin.condition*` 배치는 자리를 유지한 채 플러그인 위젯 ID로 바뀌고, 플러그인이 꺼져 있으면 자리표시로 보인다. 새 기본 배치에는 컨디션 위젯이 없다.
- **위젯 영역:** 컨디션 위젯은 고정 크기를 없애고 격자 슬롯을 그대로 채운다. 타일 전체가 버튼이고 아이콘은 이름표가 남긴 높이만큼 커진다. 앱의 버튼 템플릿은 내용을 가운데 두므로 이 버튼만 자체 템플릿을 쓴다. 플러그인 위젯은 축소 맞춤 없이 타일을 채우므로 7일 위젯의 최소 높이는 내용이 잘리지 않는 2칸이다.
- **이전과 달라진 점:** `facet_today`의 컨디션 항목을 없앴다(ADR 0080의 음악과 같은 이유). 플러그인을 켜기 전에는 에이전트가 컨디션 도구를 쓸 수 없다. 건강 기록을 자동 첨부하지 않는 경계는 그대로다. 플러그인은 앱의 `ScheduleDateCalendar`를 쓸 수 없어 단일 날짜 달력을 자체로 가진다. Esc 처리를 `OverlayTabs`에서 받을 수 없으므로 팝업이 열릴 때 키보드 포커스를 받아 직접 닫는다.
- **배포:** `scripts/build.ps1`이 `artifacts/Facet/plugins/condition`에 플러그인을 만든다. 이 폴더를 `<data>/plugins/condition`에 복사하고 설정에서 켠다.
