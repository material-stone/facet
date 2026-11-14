# 대시보드 YouTube 플레이어

2026-10-27: WebView2/IFrame 및 숨김 일시 정지 정책은 [0063](0063-browser-music-lifetime-and-indicator.md)의 일반 브라우저·Controller 독립 수명으로 교체했다. 아래 구현은 당시 기록이다.

아래 최초 배치·높이 보정·360 폭·이전/다음 표시는 [0062](0062-independent-compact-music-float.md)에서 중앙 고정·232 폭 독립 플로팅·최소 UI로 교체했다. 저장·재생 backend 경계는 유지한다.

2026-10-26: 대시보드 오른쪽에 360 DIP 플로팅 카드를 추가한다. 넓은 화면은 기존 800 DIP 대시보드와 28 DIP 간격으로 나란히 배치하고 좁은 화면에서는 아래로 이어지며 세로 스크롤을 허용한다.

같은 날 높이 회귀 수정: 넓은 화면에서는 전체 대시보드 스크롤을 제거한다. DashboardHost의 MaxHeight를 실제 DashboardViewport 높이로 제한해 기존처럼 전체 일정·그래프를 한 화면에 균일 축소한다. 음악은 독립 높이 경계를 갖고 재생 목록만 남은 높이에 맞춰 줄이며 URL·재생 제어를 고정한다. 사용 가능 높이가 620 DIP 미만이면 카드 여백을 줄이고 영상 200 DIP 높이를 보존한다. 520 DIP보다도 낮은 가용 높이에서만 음악 카드의 독립 스크롤을 허용한다. 좁은 화면의 세로 배치는 기존처럼 스크롤하며 대시보드 자체는 항상 viewport 높이 이내에 표시한다.

공식 YouTube IFrame API를 WebView2CompositionControl에 표시한다. WPF 투명 오버레이·모달과 함께 합성하기 위해 HWND 기반 WebView 대신 composition control을 사용한다. 영상은 320×200 이상으로 표시하며 오디오 추출은 하지 않는다. feature-owned HTML은 DashboardMusic 아래 embedded resource로 두고 가상 HTTPS origin에서 제공해 Referer를 유지한다. 브라우저에는 video ID와 재생 명령만 전달한다. API key는 필요 없다.

현재 제목, 재생/일시 정지, 시간 슬라이더, 한 곡 반복, 이전/다음, 접는 목록, URL 추가와 항목 삭제를 제공한다. Core가 제한된 YouTube URL과 ID·최대 100곡을 검증하며 AppData.Music 선택 필드는 schemaVersion 2를 유지한다. 제목은 공식 플레이어가 보고한 값으로 갱신하고 목록·반복은 기존 암호화 Store에 저장한다. 저장 실패는 기존 재생·목록과 입력을 유지하며 인라인 재시도를 제공한다.

시작 시 자동 재생하지 않는다. 명시적 재생 때만 WebView2를 만들며 다른 탭·오버레이 숨김은 일시 정지한다. 종료 시 WebView2를 해제한다. 임베드 제한·네트워크·런타임 오류를 인라인으로 표시하고 재생 버튼으로 재시도한다.

근거: [YouTube IFrame API](https://developers.google.com/youtube/iframe_api_reference), [플레이어 요구사항](https://developers.google.com/youtube/terms/required-minimum-functionality), [WPF composition control](https://learn.microsoft.com/en-us/microsoft-edge/webview2/platforms/wpf).
