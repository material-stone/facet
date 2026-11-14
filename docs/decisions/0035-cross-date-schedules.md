# 날짜 경계를 넘는 일정과 날짜별 표시

2026-10-24: 반시계로 00시를 지나면 전날, 시계로24시를 지나면 다음 날 구간으로 생성한다. 드래그에서는 선택 날짜의 부분을 불투명하게, 이웃 날짜 부분을 반투명하게 표시한다. 저장한 뒤에는 보고 있는 날짜와 겹치는 부분만 표시한다.

일정의 StartMinute/DurationMinutes는 유지하고 실제 시작 날짜의 CalendarDayEntry에 한 번만 저장한다. Core/CalendarNotes.SchedulesForDate는 선택 날짜 및 이전 날짜의 정규 저장 항목을 읽어 표시 구간으로 투영한다. 날짜별 투영의 StartDayOffset은 원본 시작일과 표시 날짜의 차이이며 날짜 선택 모달의 초안은 선택 날짜에 대한 임의의 유효한 날짜 차이를 사용한다. StartDayOffset과 OwnerDate는 JsonIgnore이고 기존 저장 파일은 그대로 읽는다. VisibleStartMinute/VisibleDurationMinutes/ContainsSlot은 날짜 안으로 자른 구간을 사용한다. SaveSchedule은 기존 소유 날짜에서 제거/새 시작 날짜로 이동하는 작업과 현재 할 일/노트 초안을 하나의 후보에 적용·검증한 후 교체한다. 저장 실패 시 원본과 UI 초안을 보존한다.

Desktop/Dial은 음수 시작 분을 유지하고 경계별로 선택 구간을 나누어 투명도를 적용한다. 저장된 부채꼴·클릭·겹침·레이블 연결은 표시 날짜 구간에만 적용한다. 모달은 실제 시작 날짜와 종료 날짜를 보여주고 원래 시작/기간을 편집하므로 잘려 보이는 구간을 저장해 원본을 잃지 않는다. OverlayDayEditor는 기존 노트/할 일 저장과 별도로 Core의 일정 전용 원자 쓰기/날짜 투영 delegate를 연결하며 두 인자의 기존 Configure는 독립 검증용으로 유지한다.

00시/24시 경계, 양 방향·자정 역전·24시간·월말·연말, 이전 날짜 시작 일정의 생성/수정/삭제, 암호화 재로드·노트 자동 저장·쓰기 실패를 검증한다. 이 결정이0030의 날짜 안에서 순환 표시하던 동작을 대체한다.

[0037](0037-multiday-schedule-and-compact-editor.md)이 전날 한정 투영·24시간 상한을 대체하며 JSON DurationMinutes 정수의 표현 범위를 long으로 넓힌다. 긴 기간은 각 날짜의 표시 구간만 계산한다.
