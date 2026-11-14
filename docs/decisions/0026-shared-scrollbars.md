# 공통 스크롤바 디자인

2026-10-24: 런처에 적용한 폭 4 DIP·끝 반경 2 DIP의 막대와 20 DIP 전용 영역을 앱 전체의 기본값으로 통일한다. Desktop/Design/Scrolling.xaml이 세로·가로 ScrollBar와 ScrollViewer의 공통 템플릿을 소유하며 Components.xaml에서 병합한다. 기존 런처·대시보드의 개별 스타일은 제거하고 같은 리소스를 사용한다.

ScrollViewer는 스크롤을 허용한 축(Auto/Visible)에 전용 열/행을 항상 예약하고 내용과 막대를 분리한다. Disabled/Hidden 축에는 공간을 두지 않아 한 줄 입력창·날짜 입력의 크기와 커서 위치를 보존한다. 스크롤이 필요 없으면 Auto 막대만 숨기며 예약 공간은 유지한다. 화살표·장식 트랙은 제거하고 전체 20 DIP 영역으로 잡기·페이지 이동을 지원한다. hover는 밝기, drag는 Mint로 표시한다.

TextBox 내용 호스트, ListBox, ComboBox 팝업과 코드로 만드는 ScrollViewer에도 공통 스타일을 적용한다. PART_ContentHost, PART_ScrollContentPresenter, PART_VerticalScrollBar, PART_HorizontalScrollBar, PART_Track과 기본 스크롤 명령을 보존한다. ItemsPresenter의 CanContentScroll을 유지해 목록 가상화·선택 항목 이동을 보존한다. 앞으로 화면별 스크롤바 템플릿을 복제하지 않는다. 시각 치수·색은 Design 소유이며 Core·저장 형식은 변경하지 않는다.

2026-10-24 후속: [0036](0036-planner-edit-modes.md)에서 고정 Auto 거터 정책을 대체했다. 실제 Computed scrollbar visibility가 Visible일 때만20 DIP를 확보하고 막대가 사라지면 내용을 전체 폭으로 복구한다.
