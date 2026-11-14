# 수면을 플러그인과 전용 저장소로 분리

2026-11-05: 수면 그래프, 수면 탭의 7일 패턴, 수면 기록 모달을 앱에서 빼 `Facet.SleepPlugin`(`teum.sleep`)으로 옮긴다. 플러그인을 연결하면 `최근 7일 수면 패턴`(`pattern`)과 `최근 7일 수면량`(`duration`) 두 위젯, `facet_record_sleep`·`facet_list_sleep` 도구, 배경 투명도 설정을 쓸 수 있다. ADR 0049·0050·0052·0055의 수면 동작은 소유 위치만 플러그인으로 바뀐다. 구조는 [ADR 0081](0081-condition-plugin-and-store.md)과 같다.

- **저장소:** `<data>/plugin-data/teum.sleep.dat`에 `{SchemaVersion:1,Sleep:[...],Removed:[...]}`를 저장한다. `Removed`는 삭제한 기록의 보관본(옛 `RemovedSleep`)이다. 앱이 편집하지 않는 옛 필드도 그대로 보존한다.
- **이관:** `Store.Load`가 `records.dat`의 `Sleep`·`RemovedSleep`을 찾으면 `backups/records-before-sleep-store-*`로 백업하고, 저장소에 합쳐 쓰고, 다시 읽어 확인한 뒤에야 비운다. 플러그인을 켜지 않아도 앱 시작 때 옮긴다. 같은 기상일이면 저장소 쪽이 이긴다. 실패하면 시작을 멈추고 원본을 보존한다.
- **모달:** 기록 창은 `IWidgetModal`로 띄운다. Esc는 드롭다운을 먼저 닫고, Esc·바깥 클릭·숨김은 저장을 시도하며 실패하면 창과 초안을 유지한다. 앱의 `TrySaveEdits`가 열린 위젯 모달의 닫기 규칙을 먼저 물으므로 저장 실패는 종료도 막는다. Ctrl+S는 창 경로가 위젯 모달을 건너뛰고 편집기가 직접 처리한다.
- **수면 탭:** 탭은 일반 위젯 보드로 남는다. 고정 화면이던 패턴은 `GridVersion` 4 미만 배치에 위젯으로 한 번 들어가며 이후에는 지울 수 있다. 저장된 `builtin.sleep` 배치는 자리를 유지한 채 `duration`이 되고, 배치를 저장한 적 없어도 수면 기록이 있으면 그래프 자리를 유지한다. 플러그인이 꺼져 있으면 자리표시로 보인다. 새 기본 배치에는 수면 위젯이 없다.
- **앱에 남는 흔적:** 입력 전용 `AppData.Sleep`·`RemovedSleep`, `SleepEntry`, `Settings.SleepGraphTransparencyPercent`, `LegacySleep`, `PluginData.MoveSleep`뿐이다. 옛 기록을 옮기는 용도로, 그런 기록이 없어지면 지운다.
- **이전과 달라진 점:** 플러그인을 켜기 전에는 수면 위젯·기록 창·에이전트 도구를 쓸 수 없다. 모달 배경은 다른 위젯 모달과 같은 공용 배경을 쓴다. 투명도 설정은 설정의 **수면** 그룹에서 플러그인 구역으로 옮겼다. 플러그인은 앱의 아이콘을 쓸 수 없어 삭제 아이콘을 직접 그린다.
- **배포:** `scripts/build.ps1`이 `artifacts/Facet/plugins/sleep`에 플러그인을 만든다. 이 폴더를 `<data>/plugins/sleep`에 복사하고 설정에서 켠다.
