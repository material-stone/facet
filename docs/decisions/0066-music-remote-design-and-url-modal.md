# 음악 리모컨의 공통 디자인과 URL 모달

2026-10-27: 대시보드 음악 조작을 기존 화면 디자인에 맞춘다. 재생·추가·저장 재시도는 ButtonWithoutFocusOutline의 배경 hover를 사용하고 테두리를 없앤다. 목록은 ExpanderWithoutFocusOutline을 사용하며 곡 행은 할 일 항목과 같은 Input 배경·6 DIP 모서리·hover·안쪽 Trash 아이콘을 사용한다. 행 전체 클릭·Enter/Space는 재생하고 삭제 버튼·Delete는 그 항목만 삭제한다. 삭제 조작은 재생으로 전파하지 않는다.

DashboardMusicPanel은 목록·선택·반복·Store commit과 AddRequested 이벤트를 소유한다. 기존 inline URL 입력은 제거하고 목록 아래 한 행의 ‘곡 추가’ 버튼으로 교체한다. Desktop/DashboardMusic/MusicUrlDialog는 URL 초안·입력·검증 안내·IME 보호·Enter 제출을 소유하며 음악 재생을 소유하지 않는다. 성공한 commit 뒤에만 입력을 비우고 닫는다. 검증·중복·저장 실패는 입력과 모달을 유지하며 Enter로 재시도한다. 추가 초안은 자동 저장하거나 자동 재생하지 않는다.

OverlayTabs는 기존 수면·일정 모달과 같은 전체 영역 sibling 레이어에서 MusicUrlDialog를 중앙 배치한다. 배경은 대시보드·음악 카드·탭까지 어둡게 덮고 포인터 입력을 막는다. URL 입력 안에서 Tab 순환을 제한하고 모달 중 Ctrl+탭 이동을 막는다. Esc·바깥 클릭은 모달만 닫으며 초안은 앱 세션 안에서 유지한다. 바깥 클릭의 놓기 이벤트가 아래 화면으로 통과하지 않도록 기존 mouse capture 경계를 재사용한다. 숨김·탭 변경은 모달을 닫고 음악은 계속 재생한다. 성공·취소 후에는 보이는 추가 버튼으로 포커스를 돌린다.

MusicPlaybackService는 선택한 곡의 삭제 때 준비 요청을 취소하고 해당 세대를 무효화한다. 늦게 완료한 재생 준비를 정리한 뒤 pause하며, 그 사이 새로운 곡 선택이 생기면 새 요청을 우선해 삭제된 곡의 재생이 남지 않도록 한다.

중앙 최대800 DIP 대시보드와 오른쪽232 DIP 음악 카드의 측정·위치, 로컬 캐시 재생 수명과 암호화 Music DTO는 변경하지 않는다. 화면 밖 WPF 검사로 모달 중앙·전체 dim 레이어·키보드 제출·실패 입력 보존·중복·Escape·삭제와 재생 분리를 검증한다.
