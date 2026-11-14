# 창 없는 백그라운드 음악

현재 기본 재생 경로는 [0065 다운로드 음원 재생](0065-cached-music-and-compact-title-indicator.md)으로 대체했다. 아래 headless CDP 방식은 이전 결정과 선택 검사로만 남는다.

2026-10-27: 사용자는 별도 브라우저 창을 원하지 않으며 오버레이에서만 재생·목록을 조작하도록 요청했다. 0063의 일반 창 표시를 제거하고 기존 CDP 재생 엔진을 항상 unified headless로 실행한다. 운영 실행에는 mute-audio를 넣지 않으며, 음소거는 검사 옵션으로만 분리한다. ProcessStartInfo.WindowStyle=Hidden·CreateNoWindow와 --headless=new를 함께 사용하며 새 창을 여는 fallback은 없다.

Store.Root/MusicBackground의 전용 프로필을 사용해 0063의 기존 보이는 MusicBrowser 세션에 잘못 붙지 않는다. 기존 개인 프로필·계정·쿠키를 가져오지 않고 영상/오디오 스트림을 추출하거나 광고를 건너뛰지 않는다. 로그인·연령·지역 제한 등 재생이 안 되는 경우 인라인 오류·재시도를 제공하고 별도 창을 열라고 안내하지 않는다.

오버레이 숨김·탭 이동과 Controller MusicPlaybackService·재생 중 표시·목록 저장 경계는 유지한다. 앱을 완전히 종료하면 제어할 수 없는 숨은 음악이 남지 않도록 전용 headless 브라우저에 Browser.close를 보낸다. 종료 실패 시 이 transport가 직접 시작한 프로세스만 정리하며 개인 브라우저나0063의 보이는 창은 종료하지 않는다. 일반 숨김·탭 이동은 종료하지 않는다.

검사는 운영 실행 인자의 무음 아님·headless·새 창 없음, 실제 재생 프로세스의 가시 창 부재, 숨김 이후 연속 재생과 제어, 앱 종료 시 전용 프로세스 해제를 확인한다.

근거: [Unified headless mode](https://developer.chrome.com/docs/automation-and-testing/headless).
