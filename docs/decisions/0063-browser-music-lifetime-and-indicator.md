# 일반 브라우저 음악 재생과 독립 수명

후속 사용자 요청으로 보이는 재생 창과 앱 종료 시 연결만 해제하는 동작은 [0064](0064-windowless-background-music.md)의 창 없는 재생·운영 음소거 없음·앱 종료 시 전용 프로세스 정리로 교체했다. 재생·목록·Controller 독립 수명과 표시 경계는 유지한다.

2026-10-27: 사용자가 오버레이 숨김·탭 이동 후에도 음악 유지, 임베드 외 재생 및 작업표시줄 위 표시를 요청했다. 0061/0062의 WebView2/IFrame·가시성 일시 정지를 제거한다. 영상 스트림·오디오를 추출하지 않고 앱 전용 Edge/Chrome의 일반 YouTube watch 페이지에서 재생한다. 처음 명시적 재생 때 별도 브라우저 창이 열리며 사용자는 최소화하거나 직접 조작할 수 있다.

Desktop/DashboardMusic/BrowserMusicTransport는 기존 QuickLaunchWebSearch의 브라우저 경로 조회와 설치된 Edge/Chrome을 사용하며 Store.Root/MusicBrowser의 전용 프로필·loopback 임시 CDP 포트로 그 창만 제어한다. 기존 개인 프로필·탭·쿠키·토큰·재생 URL 스트림을 읽지 않는다. CDP Runtime.evaluate는 일반 video 요소의 play/pause/currentTime·duration/ended와 페이지 제목, 광고 표시 여부만 읽거나 조작하며 광고를 막거나 건너뛰지 않는다. Page.navigate는 검증한 video ID의 HTTPS watch 주소만 연다. 창 재사용·재연결을 지원한다. 사이트 구조·로그인·재생 제한은 안내하고 재생 버튼으로 재시도한다.

Controller 소유 MusicPlaybackService가 선택·실제 재생 상태·진행·한 곡 반복·목록 자동 다음 곡과 타이머를 소유한다. 오버레이·대시보드·UI Loaded/Unloaded와 수명을 분리한다. 숨김·빠른 실행·탭 이동·패널 해제는 재생 명령을 보내지 않는다. 종료는 제어 연결만 해제하며 사용자가 연 재생 창을 강제 종료하지 않는다. 기존 AppData.Music·암호화 Commit과 Core URL 검증은 유지한다.

MusicPlaybackIndicator는 Controller 소유의 작은 비활성·작업표시줄 항목 없는 창이다. 실제 playing 상태일 때만 주 모니터 작업표시줄 위에 ‘♫ 음악 재생 중’ 한 줄을 표시한다. 일시 정지·종료·재생 창 닫힘·오류에는 숨기며 자동 종료 타이머가 없다. 개인 제목은 표시하지 않는다.

기본 검사는 fake transport로 수명·반복·종료/광고·다음 곡·명시적 pause·표시 상태를, 별도 browser 검사는 전용 headless 프로필과 합성 로컬 video로 CDP의 실제 재생/탐색을 확인한다. 실제 YouTube 연결은 별도 live 검사로 분리한다.

근거: [CDP](https://chromedevtools.github.io/devtools-protocol/), [전용 프로필 원칙](https://developer.chrome.com/blog/remote-debugging-port).
