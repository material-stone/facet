# 구현 및 검증 — 2026-10-15

## 2026-10-27 hover 즉시 시작과 빠른 제목 이동

상단·목록 hover 제목의 최초1.2초 대기를 없애고 진입 즉시 이동하도록 바꿨다. 속도는28→36 DIP/초로 약29% 높였으며 왕복 양 끝에서1.2초 읽기 대기는 유지한다. 마우스 이탈 시 초기화와 짧은 제목 고정도 유지한다. 토스트의 자동 이동 속도·최초 대기는 변경하지 않았다.

`scripts/build.ps1 -OverlayUi`에서 Core38·Journal/WPF1188개와1.22.4 Release 발행이 통과했다. 상단/목록의 진입200ms 안에 실제 음의 이동과 상단의600ms 구간 실제 속도가36 DIP/초 근처인지 확인했다. 기존 이탈/숨김/접기/해제 정리·토스트·볼륨·재생·모달·배치 검사도 통과했다. 자료는 `.test-data/journal-91aab98e6dd147bd872a95ad19659131/`와 `.test-data/music-hover-speed-release.log`에 있다. 실제 사용자 음악·입력·위치는 사용하지 않았다.

`latest.json`은 `releases/1.22.4-20261027-232747-a82e86b6/Facet.exe`를 선택하며 실행 중인1.22.3은 강제 종료하지 않았다. 환경 검증은 통과했고 architecture 명령은 null로 미설정이다.

## 2026-10-27 상단 제목도 hover 동안만 이동

후속 요청에 따라 플레이어 상단 제목의 자동 이동을 hover 전용으로 바꿨다. 제목 영역에 마우스를 올린 동안만 긴 제목이 좌우로 이동하고 이탈하면 clock을 제거하며 처음 위치의 말줄임표 표시로 복구한다. 숨김/재표시 후에도 새 hover를 기다리며 짧은 제목은 고정한다. 작은 카드·목록 hover·토스트·음악 재생 수명은 유지한다.

`scripts/build.ps1 -OverlayUi`에서 Core38·Journal/WPF1187개와1.22.3 Release 발행이 통과했다. 상단 hover 전 정지·진입 후 이동·같은 제목 갱신 시 위치 유지·이탈 시 즉시 초기화·짧은 제목 전환·숨김 후 새 hover 대기·재진입 재개·해제 정리를 확인했다. 합성 저장소와 화면 밖 WPF 이벤트를 사용했으며 실제 사용자 입력·음악·위치는 사용하지 않았다. 자료는 `.test-data/journal-f1884b6464b64bda92d93ae363bcdd79/music-player-title-scrolling.png`다.

`latest.json`은 `releases/1.22.3-20261027-232020-cb92c8e8/Facet.exe`를 선택하며 실행 중인1.22.1은 강제 종료하지 않았다. 환경 검증은 통과했고 architecture 명령은 null로 미설정이다.

## 2026-10-27 플레이어 상단 현재 제목 이동

플레이어 상단 현재 제목도 MusicTitleLine으로 표시해 길면 hover 없이 자동으로 좌우 이동한다. 짧은 제목은 고정하고 기존12 DIP SemiBold·작은 카드·목록 hover 이동은 유지했다. 같은 제목을 주기 갱신해도 이동 위치를 유지하며 제목 변경·숨김·해제는 clock을 정리하고 재표시는 처음부터 다시 읽을 수 있게 한다.

최종 `scripts/build.ps1 -OverlayUi`에서 Core38·Journal/WPF1184개와1.22.2 Release 발행이 통과했다. 상단 자동 이동·동일 제목 위치 유지·짧은 제목 전환·숨김 시 정지·재표시 재개·해제 후 정리를 확인했고 기존 목록/토스트 이동·볼륨·재생·모달·배치 검사도 통과했다. 화면 밖 렌더도 확인했다. 자료는 `.test-data/journal-ca9f145cd6614b10891c480dc166039b/music-player-title-scrolling.png`다. 사용자 음악·포인터·키 입력·위치는 사용하지 않았다.

`latest.json`은 `releases/1.22.2-20261027-224057-26a81e3c/Facet.exe`를 선택하며 실행 중인1.22.1은 강제 종료하지 않았다. 환경 검증은 통과했고 architecture 명령은 null로 미설정이다.

## 2026-10-27 작은 음악 카드와 목록 제목 hover

목록의 긴 제목은 항목 hover 동안만 양 끝에서 쉬며 좌우로 이동하고 짧은 제목은 고정한다. 이탈·접기·숨김·목록 교체·해제는 animation을 제거하고 처음 위치의 말줄임표 표시로 되돌린다. 기존 토스트 제목 렌더러를 MusicTitleLine으로 옮겨 실제 두 화면에서 같은 clip/ink 중앙/이동 구현을 재사용했다. 곡 행52→34 DIP·간격8→4 DIP, 휴지통32→26 DIP, 목록 높이150→114 DIP로 줄이고 카드·제어·추가 행의 여백도 줄였다. 두 곡 카드의 높이는 약245 DIP이며 원래 대시보드 위치와 카드 폭232 DIP는 유지한다. [0068](../decisions/0068-compact-playlist-hover-titles.md).

최종 `scripts/build.ps1 -OverlayUi`에서 Core38·Journal/WPF1179개와1.22.1 Release 발행이 통과했다. 실제 행 높이·두 곡 카드250 DIP 미만, hover 전 정지·긴 제목 이동·짧은 제목 정지·이탈/접기 정리·재펼침 후 새 hover 대기·목록 교체와 해제 후 clock 정리를 확인했다. 해제 후 다시 layout해도 animation이 재시작하지 않는지 확인했으며 기존 토스트·볼륨·곡 재생/삭제·URL 모달·여러 화면 크기의 대시보드 고정 배치 검사도 통과했다. 화면 밖 렌더도 확인했다. 자료는 `.test-data/journal-84ae9df0fa9543d9834c40fe62ccfc91/music-playlist-compact.png`와 `music-playlist-hover.png`다. 사용자 포인터·키 입력·음악·위치를 사용하지 않았다.

`latest.json`은 `releases/1.22.1-20261027-220755-e6f6e09d/Facet.exe`를 선택하며 실행 중인1.21.0은 강제 종료하지 않았다. 환경 검증은 통과했고 architecture 명령은 null로 미설정이다.

## 2026-10-27 음악 소리 크기

음악 카드에0~100% 소리 슬라이더와 현재 퍼센트를 추가했다. MediaPlayer.Volume에 즉시 적용하고0%에서도 곡 진행·재생 상태는 유지한다. 설정은300ms debounce로 기존 암호화 Store의 선택 필드 Music.VolumePercent에 저장하며 구형 기본값은50%다. 곡 변경·목록 저장·다시 실행에서도 값을 유지하고, 숨김/종료 전 저장 실패에는 현재 소리와 초안을 유지하며 재시도를 제공한다. [0067](../decisions/0067-music-volume.md).

최종 `scripts/build.ps1 -OverlayUi`에서 Core38·Journal/WPF1170개와1.22.0 Release 발행이 통과했다. 구형 기본값·복사·JSON 왕복·범위 거절, 슬라이더 즉시 적용과 퍼센트·debounce 저장·잠긴 파일의 저장 실패/재시도·숨김 전 flush·목록 저장 후 유지·새 서비스의 첫 곡 재생 전 복원을 확인했다. 실제 합성 무음 M4A의 MediaPlayer.Volume35%·0%·100%와 음소거 중 계속 재생·다음 곡 유지도 확인했다. 실제 하드웨어 청취·시스템 볼륨·사용자 음악/기록은 사용하지 않았다. 화면 자료는 `.test-data/journal-b2735a14b64146e589b7ace1c77d992e/store/music-dashboard-design.png`다.

`latest.json`은 `releases/1.22.0-20261027-215613-55c0cbe7/Facet.exe`를 선택하며 실행 중인1.21.0은 강제 종료하지 않았다. 환경 검증은 통과했고 architecture 명령은 null로 미설정이다.

## 2026-10-27 음악 리모컨 공통 디자인과 URL 추가 모달

재생·추가·재시도 버튼을 공통 테두리 없는 배경 hover 스타일로 변경하고 목록 헤더에 공통 disclosure 화살표를 사용했다. 곡은 할 일과 같은 Input 배경·모서리·여백·hover의 전체 행에서 재생하며 안쪽 Trash 버튼으로 삭제한다. inline URL을 제거하고 ‘곡 추가’ 한 행에서 전체 오버레이를 어둡게 덮는 중앙 입력 모달을 연다. 정상 상태에는 입력칸만 표시하고 Enter의 성공한 Store commit 뒤에만 비우고 닫는다. 중복·잘못된 URL·저장 실패는 입력·오류를 유지하며 Enter로 재시도한다. Esc·바깥 클릭은 모달만 닫고 초안은 세션에 남긴다. [0066](../decisions/0066-music-remote-design-and-url-modal.md).

일시 정지·삭제 후 busy 상태 해제도 화면에 통지해 재생 버튼을 즉시 다시 사용할 수 있게 했다. 준비 중인 곡의 삭제는 해당 요청을 무효화하고 늦은 완료 뒤 pause하므로 삭제한 곡의 재생이 남지 않는다. 기본 로컬 재생·숨김 이후 유지·기존 대시보드 위치는 보존했다.

최종 `scripts/build.ps1 -OverlayUi`에서 Core37·Journal/WPF1158개와 Release 발행이 통과했다. 실제 WPF Enter 이벤트·암호화 저장 재로드·실패 파일 잠금과 Enter 재시도·IME 확정 보호·Escape·바깥 클릭 소비·1440×900/480×360 전체 dim과 중앙 배치·Tab cycle 설정과 탭 이동 차단·대시보드 경계 불변·휴지통과 재생 분리·전체 행 재생·일시 정지/재개 버튼·준비 중 삭제를 확인했다. 렌더링 자료는 `.test-data/journal-6d5f2634f215414c8dcf6f6d13683a24/store/music-dashboard-design.png`와 `music-url-modal.png`다. 실제 사용자 음악·위치·전역 키 입력 없이 합성 저장소·fake transport·숨긴 이벤트 source를 사용했다.

`latest.json`은 `releases/1.21.0-20261027-214416-cf211307/Facet.exe`를 선택하며 실행 중인1.20.3은 강제 종료하지 않았다. 환경 검증은 통과했고 architecture 명령은 null로 미설정이다.

## 2026-10-27 음악 토스트 전체 반투명

음악 표시의 Window.Opacity를0.7로 설정해 배경·테두리·상태·곡 제목 전체를 불투명도70%·투명도30%로 합성한다. 공유 brush는 수정하지 않으며 기존 중앙 정렬·280 DIP 폭·긴 제목 이동과 비활성·클릭 통과 수명은 유지한다.

`scripts/build.ps1`에서 기존 Core37·Journal107개와1.20.3 Release 발행이 통과했다. `latest.json`은 `releases/1.20.3-20261027-210911-ab5b24a6/Facet.exe`를 선택하며 실행 중인1.20.1은 강제 종료하지 않았다. 검사 로그는 `.test-data/music-toast-opacity-release.log`, 합성 자료는 `.test-data/journal-1c07f3a54ccb4bfb91b3c35b1adbbcdd/`에 있다. 내용만 렌더하는 기존 PNG는 최상위 Window 투명도의 시각 검증 자료로 사용하지 않는다. 환경 검증은 통과했고 architecture 명령은 null로 미설정이다.

## 2026-10-27 음악 토스트 중앙 정렬과 긴 제목 이동

음악 표시의 전체 최대 폭을446에서280 DIP로 줄이고 고정 ‘♫ 재생 중 · ’ 오른쪽에서 제목만 이동하도록 했다. TitleLine은20 DIP 행에서 FormattedText의 실제 ink bounds를 수직 중앙에 놓아 글꼴 line box의 빈 공간 때문에 높이가 어긋나던 표시를 수정한다. 긴 제목은 양 끝1.2초 대기와28 DIP/초 왕복 이동으로 전부 읽을 수 있으며 짧은 제목은 고정한다. 같은 제목의 주기 갱신은 clock을 재시작하지 않고 숨김·제목 변경·종료는 이전 animation을 정리한다. 실행 토스트의 여백·테두리·둥근 모서리·작업표시줄 간격은 유지한다.

`scripts/build.ps1`에서 Core37·Journal107개와1.20.2 Release 발행이 통과했다. 실제 drawing ink 중앙, 최대 폭, 읽기 대기 후 음의 이동, 같은 제목 갱신 후 연속 이동, 숨김 시 clock 해제·재표시 재개와 짧은 제목의 정지를 화면 밖 호스트에서 확인했다. 렌더링 결과도 확인했다. 자료는 `.test-data/journal-c042a2819d044b2a97a60edc29f6cf32/`의 `music-playing-indicator.png`, `music-title-start.png`, `music-title-scrolling.png`다. `latest.json`은 `releases/1.20.2-20261027-210556-75465ed1/Facet.exe`를 선택하며 실행 중인1.20.1은 강제 종료하지 않았다. 환경 검증은 통과했고 architecture 명령은 null로 미설정이다.

## 2026-10-27 음악 재생 표시를 실행 토스트에 맞춤

여백 제거 요청의 후속 설명에 따라 현재 제목을 유지하면서 실행 알림 토스트의 외형을 복원했다. Panel 배경·Mint1 DIP 테두리·8 DIP 모서리·좌우12/상하7 DIP padding·13 DIP 글씨와 주 모니터 작업표시줄 위16 DIP 간격을 동일하게 사용한다. 재생 중 유지·중지 시 숨김·비활성·클릭 통과는 유지한다. 0065의 여백0 규칙은 이 후속 요청으로 대체했다.

`scripts/build.ps1`에서 Core37·Journal100개 검사와1.20.1 Release 발행이 통과했다. 실제 startup ToastWindow 인스턴스와 음악 표시의 배경·테두리·모서리·padding·글씨 크기를 비교하고 DPI를 반영한16 DIP 간격과 native 비활성·클릭 통과를 확인했다. 화면 밖 렌더는 `.test-data/journal-43b0524e9e8649e585eee160b66f895e/music-playing-indicator.png`다. `latest.json`은 `releases/1.20.1-20261027-205918-30161be1/Facet.exe`를 선택하며 실행 중인1.20.0은 강제 종료하지 않았다. 환경 검증은 통과했으며 architecture 명령은 null로 미설정이다.

## 2026-10-27 다운로드 캐시·로컬 재생과 현재 제목

기본 음악 transport를 MusicDownloadCache와 Windows MediaPlayer로 변경했다. 최초 재생에서 고정 버전 yt-dlp로 호환 M4A를 받고 완료한 파일만 재사용한다. 브라우저·임베드·광고 재생 경로를 사용하지 않으며 오버레이 숨김·탭 이동에도 로컬 음악은 유지된다. 로딩 중 새 곡을 누르면 이전 준비 요청을 취소하고 최신 선택을 실행한다. 재생 중 표시는 ‘♫ 재생 중 · 현재 곡 제목’이며 padding·margin·테두리와 작업표시줄 사이 간격은0이다. [0065](../decisions/0065-cached-music-and-compact-title-indicator.md).

Core37·화면/통합1137개 검사가 통과했다. 합성 무음 M4A의 실제 native 재생·탐색·중지/재개, 다운로드 없이 캐시 재사용, 실패 시 기존 파일 보존, ID 경로 검증, 패널 해제 후 재생 유지, 기본 로컬 transport와 준비 중 새 선택을 확인했다. 자료는 `.test-data/journal-cce97759f31a4549bed5ec59ce2621d0/`와 그 안의 `music-playing-indicator.png`다. 제목 표시·여백 속성과 native 창 하단=작업 영역 하단을 별도로 검사하며 스크린샷은 검사 전용240×16 크기다. 운영 창은 내용 크기로 맞춘다.

공식 YouTube API 예제의 다운로드 없는 metadata probe는101개 검사가 통과했다. 실제 Creative Commons Blender 영상 `YE7VzlLtp-4`의 M4A 다운로드·native 음소거 재생 검사는102개가 통과했다. 완성 파일·실제 제목·재생 길이와 상태를 확인했고 FFmpeg 변환 없이 재생했다. 자료는 `.test-data/journal-56d06c84de8d4d118bb4472c30a8d480/`와 `.test-data/journal-31270fb658204725b342a3913cc77281/`다. 테스트 미디어는 [Big Buck Bunny, Blender Foundation, CC BY3](https://studio.blender.org/films/big-buck-bunny/)이며 사용자 음악이나 기록은 사용하지 않았다. 테스트 당시 Node24.12.0과 yt-dlp2026.10.26을 사용했다. 모든 YouTube 영상의 다운로드 가능성이나 하드웨어 청취를 보장하는 검사는 아니다.

최종 `scripts/build.ps1 -OverlayUi`에서도 Core37·Journal1137개와 Release 발행이 통과했다. `latest.json`은 `releases/1.20.0-20261027-203234-0d737146/Facet.exe`를 선택하며 실행 중인1.19.1은 강제 종료하지 않았다. 플러그인·원격 구성 요소 다운로드를 차단한 최종 인자로 metadata probe101개도 통과했다(`.test-data/journal-e247e10ee2f3482aa8bd7f454a33810b/`). NuGet 취약성 feed 조회에는 NU1900 네트워크 경고가 있었으나 컴파일 오류는 없었다.

## 2026-10-27 창 없는 백그라운드 재생

운영 브라우저 엔진도 항상 headless·Hidden·CreateNoWindow로 실행하고 테스트용 mute-audio 옵션을 별도로 분리했다. 보이는 --app 창 인자는 제거했으며 MusicBackground 전용 프로필로 예전 보이는 음악 창에 재연결하지 않는다. 오버레이 숨김·탭 이동 후 재생과 작은 재생 중 표시는 유지하고, 앱을 완전히 종료할 때는 전용 엔진을 닫아 숨은 음악이 남지 않게 했다. 오류에서 별도 창을 누르라는 안내도 제거했다. [0064](../decisions/0064-windowless-background-music.md).

Core37·화면/통합1126개가 통과했다. 실제 합성 브라우저97개·실제 YouTube96개 검사에서 가시 주 창 없음, 재생/제어/제목, 리모컨 숨김 뒤 유지, 앱 종료 시 소유 프로세스 종료를 확인했다. 운영과 같은 음소거 없는 프로세스에서 합성 무음 PCM을 재생한 오디오 경로 검사는98개가 통과했다. 실제 하드웨어 청취 검사를 의미하지 않는다. 자료는 `.test-data/journal-f12d0caa99c849c8a42143cee7b19fab/`, `.test-data/journal-3952a3516fa04534a1f4cf9861478f6d/`, `.test-data/journal-6b99f6df966f422eaa60a98e01eb93c3/`에 있다.

## 2026-10-27 일반 브라우저 음악과 숨김 후 유지

WebView2/IFrame을 제거하고 일반 YouTube watch 페이지를 전용 Edge/Chrome 프로필에서 재생한다. Controller의 MusicPlaybackService가 재생·제목·진행·반복·다음 곡을 소유하며 리모컨 탭 이동·숨김·해제는 pause를 보내지 않는다. 실제 playing 상태에만 포커스를 받지 않는 ‘♫ 음악 재생 중’ 한 줄을 주 모니터 작업표시줄 위에 표시한다. 기존 중앙 대시보드와232 리모컨·암호화 재생 목록은 보존한다. [0063](../decisions/0063-browser-music-lifetime-and-indicator.md).

Core37·화면/통합1124개가 통과했다. fake transport로 탭 이동·숨김·패널 해제 후 유지, 명시적 pause, 중복 창 없는 resume, 반복·다음 곡·광고 미건너뜀·실패와 재연결·앱 종료 시 브라우저 제어만 해제와 작은 표시의 passive/click-through/주 작업표시줄 위 위치를 확인했다. 합성 로컬 미디어의 실제 headless 브라우저 검사는93개, 실제 일반 YouTube의 재생·중지/재개·제목·리모컨 숨김 후 유지는92개가 통과했다. 각 검사는 전용 프로필과 음소거를 사용하고 연 브라우저만 종료했다. 자료는 `.test-data/journal-9bcb0645a04348439041093c1bda7fab/music-playing-indicator.png`, `.test-data/journal-4750a0facee340e88d7fa80c97e666e4/`, `.test-data/journal-ea8e7dab7f154d46b369a1d8cf087353/`에 있다.

## 2026-10-26 원래 중앙 대시보드와 작은 독립 음악 카드

음악 추가 전의 최대800 Viewbox·중심·화면 맞춤을 복원하고 오른쪽 경계+24에232 폭 카드를 별도 Canvas로 배치했다. 제목·재생·탐색·반복·접는 목록과 URL 추가·삭제만 남기고 헤더·설명·빈 영상 자리·이전/다음을 제거했다. 명시적 재생 때206×200 공식 임베드만 표시하며 중앙 측정에 영향을 주지 않는다. 오른쪽 공간 부족 시 ♫ 버튼으로 겹쳐 연다. [0062](../decisions/0062-independent-compact-music-float.md).

Core37·Journal/WPF1110 검사가 통과했다. 원래 중심/표시 크기와 전체 일정 표시, 목록 펼침·영상 표시에도 대시보드 경계가 그대로임,232 폭·200 이상 영상과 좁은 화면 flyout을 확인했다. 자료는 `.test-data/journal-1ce8ba91e03c4d3a8fd0532211406504/overlay-dashboard-1440.png`다. 실제232 폭 음소거 검사는80개가 통과했고 공식 예제의 재생·일시 정지·재개·제목과 숨김 정지를 확인했다. 자료는 `.test-data/journal-9438b5d2c8f44e04a1f87f6886a38fcd/music-live.png`다.

## 2026-10-26 대시보드 높이 회귀 수정

음악 패널 추가 때 생긴 전체 ScrollViewer의 무한 높이 측정으로 기존 Viewbox의 높이 맞춤이 풀려 하단 일정이 잘리던 문제를 수정했다. 대시보드 높이를 실제 viewport로 제한하고 넓은 화면의 전체 스크롤을 제거했다. 음악 목록은 남은 높이만 사용하고 620 DIP 미만에서는 여백을 줄여 재생 제어·URL을 고정한다. 극단적으로 낮은 화면에서만 음악 카드 독립 스크롤을 사용한다. 1366×768·1440×900·1920×1080과 좁은 화면에 긴 목록 20곡을 넣어 대시보드 전체·일정 표시, 넓은 화면 전체 스크롤 없음, URL 표시·목록 내부 스크롤을 검사했다. 화면 자료는 `.test-data/journal-baeca6c9f396437c9cad6c6da7f9351a/overlay-dashboard-1366.png` 등에 있다.

## 2026-10-26 대시보드 YouTube 음악 패널

대시보드를 오른쪽 음악 카드까지 확장했다. YouTube URL 추가·시간 슬라이더·재생/일시 정지·한 곡 반복·제목·이전/다음·목록 접기와 삭제를 제공하며 100곡 이내 목록·반복 설정을 기존 Store로 암호화 저장한다. 좁은 화면에서는 카드가 아래로 이어진다. 다른 탭·숨김은 일시 정지하고 시작 시 자동 재생하지 않는다. [0061](../decisions/0061-dashboard-youtube-player.md).

배포 회귀, Core 37개와 화면 밖 Journal/WPF 1076개 검사가 통과했다. URL host/ID 검증·중복·경계·구형 기본값·깊은 복사와 저장 실패 후 재시도·반복 재로드·항목 삭제·브라우저 lazy 생성을 포함한다. 실제 음소거 WebView2 검사는 80개가 통과했으며 공식 예제 영상으로 API ready·native 버튼의 실제 재생/일시 정지/재개·제목과 숨김 정지를 확인했다. 라이브 방송 예제는 연결은 성공했지만 제목을 얻지 못해 공식 API 예제 영상으로 검증했다. 화면은 `.test-data/journal-3ce2e493957d4a6ab39afc3c288987da/music-live.png`, 넓은/좁은 배치는 `.test-data/journal-ea758f808b7740aa9054f043c7929f9a/overlay-dashboard-1440.png` 등에 있다. 실제 사용자 기록·위치·전역 키 입력은 사용하지 않았다.

architecture 환경 검증은 통과했고 별도 architecture 명령은 null로 미설정이다. Graphify는 전체 색인을 갱신해 semantic pending과 dangling edge가 없는지 검사한다.

## 2026-10-25 포커스 이탈 시 표시 유지

일반 오버레이와 빠른 실행의 포커스 이탈 자동 숨김을 제거했다. Deactivated는 캡처 도구에 따른 Topmost만 갱신한다. 단축키 토글·모드별 Esc·빠른 실행 내부 바깥 클릭과 명시적 숨김은 유지한다. [0060](../decisions/0060-overlay-keeps-visible-on-focus-loss.md)이 0027의 비활성화 숨김 정책을 대체한다.

기본 검사에서 Core 35개·Journal 63개가 통과했다. `--quick-launch --overlay-focus` 합성 검사 251개에서 두 모드의 비활성화 후 표시·빠른 실행 입력 보존, 0/160ms 명시적 숨김, 새 창 포커스와 주기 갱신, 표시 중 포커스 이동(블러 0/18), 스냅샷과 캡처 도구 우선 표시를 확인했다. 자료는 `.test-data/focus-retention-checks.txt`와 `.test-data/journal-3e4b07b3dd204ff5ab07d3ad39653af0/`에 있다. 실제 사용자 기록·배경 캡처·전역 키 입력은 사용하지 않았다.

`scripts/build.ps1`의 기본 검사와 Release 배포가 통과했고 `latest.json`은 `releases/1.16.1-20261025-224119-dcf0fe71/Facet.exe`를 선택한다. 실행 중인 이전 앱은 종료하지 않았으며 재시작 시 수정본을 적용한다. architecture check 명령은 null로 미설정이다.

## 2026-10-25 대시보드 평균 수면 시간

수면량 그래프 아래에는 최근 7일 평균 시간만 표시하며 표본 일수 문구는 제거했다. 수면 탭과 같은 Minutes 값 선택·분 반올림·시간 형식을 사용하며 시각 결측 제외·실제 0 포함·기록 구간 대체를 유지한다. 빈 기록은 평균 —로 표시하고 기록 저장·수정·삭제와 날짜 갱신 때 평균을 함께 갱신한다.

`scripts/build.ps1` 기본 검사와 Release 배포가 통과했고 `--sleep` 검사 146개가 통과했다. 신규 검사는 빈 평균, 두 시각만 저장한 직후 반영, 혼합 평균과 수정/삭제 갱신, 실제 0 포함·결측 제외·반올림을 확인한다. 화면 자료는 `.test-data/journal-665f0f1937344950aeb4963f9582b096/dashboard-sleep-line.png`다. 실제 사용자 데이터를 사용하지 않았다.

## 2026-10-25 수면 탭 기록 구간과 평균

수면 탭의 날짜별 시간과 평균이 대시보드와 같은 SleepDurationChart.Minutes를 사용하도록 변경했다. 입면·각성 분이 모두 있으면 추정 수면, 어느 하나라도 비어 있으면 잠 시도~기상 전체 시간을 사용한다. 평균은 같은 표시 값을 포함하고 실제 0을 포함하며 시각이 부족한 기록은 제외한다. 평균 옆의 표본 일수 문구는 제거했다. 공통 시간 형식·분 단위 반올림·원자료 null·Core 계산과 저장 형식을 유지한다. 구조 정책은 [0055](../decisions/0055-sleep-tab-interval-average.md)를 따른다.

합성 UI 검사로 두 시각만 저장한 직후 일별/평균 반영, 추정값과 대체 값 혼합, 한쪽 분 입력·자정/24시간·실제 0 포함·시각 결측 제외·표본 수·평균 반올림과 원자료 보존을 확인했다. `scripts/build.ps1`에서 Core 34개·기본 Journal 61개 검사와 Release 배포가 통과했으며, `--sleep` 화면 밖 검사는 141개가 통과했다. 화면 자료는 `.test-data/journal-1028da5c513d4fc49850a6229c072785/sleep-tab-interval-average.png`다. 실제 사용자 데이터를 사용하지 않았다.

## 2026-10-25 채팅 탭과 런처 작업 토스트

런처 요청은 즉시 숨김 페이드를 시작하고 주 모니터 작업 표시줄 위의 비활성 토스트에 프롬프트·작업 상태를 표시한다. 완료·실패·중단 상태와 오른쪽 닫기는 명시적 닫기까지 유지한다. 본문 클릭은 해당 세션의 채팅 탭을 열고 닫기는 토스트만 닫는다. 일반/프로젝트별 세션 접기·펼치기, 세션별 상태, 사용자 말풍선·에이전트 Markdown, 같은 세션 이어 보내기, 모델/effort/Fast 선택, Enter 전송·Shift+Enter 줄바꿈과 IME 확정 보호를 구현했다. 대화·초안은 메모리에만 두고 Codex 원본을 turn ID로 재조회·병합한다. [0053](../decisions/0053-chat-tab-and-task-toast.md).

실제 연동 포함 73 checks에서 즉시 숨김·토스트의 noActivate/clickThrough 해제·주 모니터 작업 영역 배치·완료 표시·토스트 클릭/닫기, 모델/effort/Fast, 스트리밍, 같은 세션의 이전 문맥을 사용하는 후속 요청, 새 서비스의 두 턴 복원과 선택 프로젝트 cwd를 확인했다. 두 임시 세션은 archive 후 정확한 ID로 삭제했다. API 최초 준비 turn의 사용자 항목 생략을 반영해 준비 ID와 가장 오래된 페이지의 정확한 READY만 제외한다. 전송 전 실패가 준비 완료로 성공 표시되지 않는 합성 검사도 통과했다.

추가 읽기 전용 MCP 검사 포함 52 checks에서 세션 메타데이터 조회의 50개 제한을 확인했다. 대화 페이지는 최대 10턴·항목당 20,000자로 조회한다. 최종 전체 검사에서 배포4·Core34·Journal/WPF965 checks와 Release 발행을 수행했다. 1.16.0 실행본을 latest로 선택하며 실행 중인 사용자 앱은 강제 재시작하지 않는다. 화면 공유 모드에서는 토스트 프롬프트·오류 상세·접근성 이름을 일반 안내로 바꾸고 모드 해제 시 원래 내용을 복원하는 두 검사도 통과했다. 사진/녹화·실제 사용자 기록·위치는 검사에 사용하지 않았다.

자료: `.test-data/journal-c59a5ca8c4974d21a59ea1a4c549cc75/`의 `chat-task-toast.png`, `overlay-chat-conversation.png`와 실제 연동 결과, `.test-data/journal-b32f6796750e4fd8b41c0b4f14abfe08/`의 읽기 전용 연결 화면, `.test-data/journal-22e8f958e6e84b35a8326fe97fc41567/`의 최종 전체 회귀 결과. 50개 제한 수정 전의 실제 연결 화면에는 오류 안내가 포함되어 있어 최종 연결 근거로 사용하지 않는다.

## 2026-10-25 대시보드 수면 기록 구간 대체 표시

입면·각성 분 중 하나라도 비어 있을 때 대시보드가 잠 시도~기상 전체 구간을 표시한다. 수치는 시간만 표시하고 툴팁·접근성 이름은 모두 ‘수면 시간’으로 통일하며 대체 값은 민트 빈 점으로 구분한다. 값 선택을 점·선·축·수치에 함께 적용하며 두 분이 모두 입력되면 기존 추정 수면을 우선한다. Core 계산·원자료 null·수면 탭 평균은 변경하지 않는다. [0052](../decisions/0052-dashboard-sleep-interval-fallback.md).

합성 UI 검사로 두 시각만 저장한 직후 표시, 한쪽 분만 입력한 두 경우, 보정된 추정값 우선·실제 0, 자정 통과·24시간 축 확장, 시각 결측의 선 단절, 마커/접근성 구분·날짜 클릭과 원자료 보존을 확인했다. 최종 `scripts/build.ps1 -OverlayUi`에서 Core 34개·Journal/WPF 917개 검사와 Release 배포가 통과했다. 화면 자료는 `.test-data/journal-dac32339031a4735b490fd8e829dce80/`의 `sleep-duration-intervals.png`와 `dashboard-sleep-line.png`다. 실제 사용자 데이터를 사용하지 않았다.

## 2026-10-25 수면 삭제·투명도·대시보드 꺾은선

대시보드의 기존 수면 요약을 배경이 투명한 7일 추정 수면량 꺾은선으로 교체했다. 날짜별 점·날짜 버튼으로 공유 기록 창을 열며 결측/부분 기록은 미확인 줄과 선 단절로 실제 0시간과 구분한다. 수면 수정 창은 기존 기록 삭제·실패 유지·재시도를 지원하고 삭제 원본은 기존 RemovedSleep에 보관한다. 수면 탭의 하단 힌트를 제거하고 설정의 수면 그래프 배경 투명도는 배경 brush에만 적용한다.

Core 검사는 삭제 충돌의 원자성·삭제 보관 복사·구형 설정 기본값/왕복을 포함한다. 합성 UI 검사는 삭제 실패 시 Sleep/RemovedSleep rollback·초안 유지·재시도·두 그래프 갱신, 저장된 점과 미기록 날짜의 편집 진입, 결측 단절/실제 0의 분리, 배경 투명 픽셀과 전경 렌더, 0/50/100% 배경·슬라이더 저장/실패/재시도·잘못된 설정 거절, 다중 크기 렌더를 수행한다. 실제 사용자 기록이나 네이티브 포인터/키 입력은 사용하지 않는다. 구조 정책은 [0050](../decisions/0050-sleep-chart-and-delete.md)을 따른다.

최종 `scripts/build.ps1 -OverlayUi`에서 Core 33개·Journal/WPF 905개 검사와 Release 배포가 성공했다. 화면 자료는 `.test-data/journal-c4e84b907de44447bc5620f4fdaad716/`의 `dashboard-sleep-line.png`, `dashboard-sleep-line-narrow.png`, `sleep-duration-line.png`, `sleep-pattern-transparent.png`, `sleep-editor.png`다. 배포 검사 중 로컬 Named Pipe 왕복은 합성 데이터로 샌드박스 밖에서 실행했다.

## 2026-10-25 오버레이 수면 기록

대시보드의 빠른 수면 입력, 수면 탭의 기상일 기준 최근 7일 공통 시간축, 날짜/행 클릭 수정과 공유 편집 모달을 추가했다. 두 시각을 우선 입력하고 입면·각성 분과 메모는 추가 기록으로 펼친다. 막대는 잠 시도~기상 구간이며 추정 실제 수면과 구분한다. 미기록·부분 기록·실제 0을 구분하고 구형 추가 필드와 암호화 저장 형식을 보존한다.

Core 검사는 자정 통과·동일 시각 24시간·낮 수면·7일 범위 이동·부분 기록·결측·0·유효성·원본 충돌·구형 필드 보존을 확인한다. 합성 저장소 UI 검사는 대시보드 입력/요약, 행 클릭 수정, 저장 실패의 rollback·재시도·Esc 유지·종료 취소, 바깥 클릭 저장·입력 소비, 미완성 시각/분 검증, 취소, 날짜 이동과 주기 갱신의 행 identity 보존을 확인한다. 1440×900·800×600·480×360 화면 밖 렌더는 투명하지 않은 실제 픽셀을 검증하며 작은 화면은 공통 세로 스크롤을 사용한다. 테스트는 실제 사용자 수면 기록을 읽거나 수정하지 않는다.

`scripts/check.ps1 -OverlayUi`와 최종 `scripts/build.ps1 -OverlayUi`가 통과했다. 최종 Core 31개·Journal/WPF 864개 검사와 Release 배포가 성공했다. 화면 자료는 `.test-data/journal-6542e8c2cd2746f79ce7d3daea68c990/sleep-pattern-1440.png`, `sleep-pattern-800.png`, `sleep-pattern-480.png`, `sleep-editor.png`에 있다. 로컬 Named Pipe 왕복은 샌드박스 권한 제한 밖에서 합성 데이터로 실행했다. 실제 포인터/전역 키 전달·열린 ComboBox의 네이티브 키 처리는 이 화면 밖 검사 범위에 포함하지 않는다. 구조 정책은 [0049](../decisions/0049-overlay-sleep-tracking.md)를 따른다.

## 2026-10-25 Codex 앱 런처 채팅

`scripts/build.ps1 -OverlayUi` 성공: 배포4·Core76·WPF841 checks. `1.15.0-20261025-070306-96a4eef6`를 발행하고 latest 포인터를 갱신했다. 실행 중인 사용자 앱과 기록은 재시작·수정하지 않았다. 환경 validator PASS이며 architecture 명령은 null로 별도 자동 경계 검사는 미설정이다. 외부 NuGet 취약성 조회 불가에 따른 NU1900 경고가 남는다.

실제 `--codex-chat-live` 46 checks 통과. 상속된 파이프 주소 없이 bundled MCP와 공유 IPC를 연결해 현재 프로젝트·모델·effort/Fast를 조회했다. 실제 런처 입력 경로에서 projectless 채팅을 만들고 앱 owner가 받은 model/effort/serviceTier=priority를 확인했다. Fast off인 선택 프로젝트의 별도 채팅도 완료했다. 여러 답변 길이 증가, 런처 종료 후 수신 지속, 원본 답변 재조회, 암호화된 최소 참조와 답변 전문 미저장, 실제 Fast 선택 저장을 검증했다. 임시 채팅은 archive 후 정확한 ID로 삭제했다. 초기 준비 단계 검사에서 남았던 채팅도 별도로 삭제했다. 사용자 프로젝트 파일·실제 기록을 모델에 첨부하거나 수정하지 않았으며 도구 사용을 금지한 검사 요청만 보냈다.

합성 검사는 정확한 `" ` 제출 경계, 구버전 기본값, 연결 없는 선택 UI, 입력 중앙 고정, 위/아래 모달과 바깥 클릭/Esc, 모델 선택 후 조절 창 복귀, UTF8 분할 framing·응답 순서 변경·discovery 구분·연결 종료·snapshot/Immer patch와 revision 불일치 처리를 포함한다. 상태 수신은 앱의 텍스트 변경 push이며 모델 원시 토큰 이벤트와는 다르다.

화면 자료: `.test-data/journal-76a2599696b5488c9f9d0174b8bbfad9/chat-model-tuning.png` 등. 전체 회귀 자료: `.test-data/journal-5e9fc2e98dfd4a1aa4c450cfa17fbb6e`. 상세 연결·저장 경계는 [0047](../decisions/0047-launcher-codex-chat.md), 사용법은 [사용 안내](README.md#codex-런처-채팅)에 기록했다.

## 2026-10-25 컨디션 클릭 완료·즉시 저장·단일 기록 시각

`scripts/build.ps1 -OverlayUi`: Core76·WPF820 통과, 실행본 `1.14.4-20261025-064534-fc909b1e`로 latest 갱신. 별도 `--condition-input`은 합성 작은 창에서 실제 마우스 누름→뗌, 누르는 동안 미열림·뗀 뒤500ms 유지, 종류 선택 자동 저장 후 창 유지, 중첩 단일 시각 선택창/부모 유지, 바깥 클릭 닫힘·바깥 놓기 취소를 확인했다(9개 native 항목, 기본12개 포함21 checks). 포인터와 전경 창을 finally에서 복원했다. 사용자 기록·실제 위치·외부 연결은 사용하지 않았다.

합성 암호화 Store로 즉시 저장 실패의 초안/이전값 보존·재시도, 연속 시각 변경의 원본 키 갱신·중복 방지, 달력 날짜 클릭 즉시 저장과 미래 날짜 차단, 삭제 실패/성공, 저장·닫기 버튼 제거와 삭제의 오른쪽 끝 정렬을 검증했다. 단일 날짜·시/분/초 선택창은 ScheduleDateCalendar를 재사용하며 시작·끝 구분을 포함하지 않는다. 화면 자료는 `.test-data/journal-2c7b2cf831984af2a6754265a054e873/condition-record-editor.png`, `condition-date-time-picker.png`이다. 구조 정책은 [0046](../decisions/0046-condition-autosave-and-time-picker.md)을 따른다.

## 2026-10-24 컨디션 구간 편집·삭제와 포커스 차단

`scripts/build.ps1 -OverlayUi` 성공: Core 76, Journal 680 checks. 실행본 `1.14.2-20261024-183240-e08bfc23`로 latest 갱신. 화면 밖 합성 자료는 `.test-data/journal-7d398251fe3d4249b9fd369304acdf93`의 `dashboard-conditions.png`, `condition-record-editor.png`이다.

기록 구간만 클릭 대상으로 조회하고 잘린 구간도 원본 RecordedAt을 표시한다. 날짜·시각·종류 수정의 암호화 저장, 미래·중복 시각·오래된 원본 거절, 수정·삭제 저장 실패의 파일/메모리 복구와 초안 유지, 삭제 후 이전 상태 연장·최초/최후 기록 삭제·빈 기록과 투명도를 검증했다. 76 DIP 아이콘을 228px로 디코딩하고 화면 제목을 제거했다. 메인 아이콘·선택 목록·게이지의 Focusable=false와 Tab 제외를 확인했으며 별도 기록 편집 팝업의 입력만 포커스를 허용한다. 사용자 기록·실제 위치·외부 네트워크·전역 키 입력은 사용하지 않았다.

## 2026-10-24 컨디션과 최근 7일 게이지

`scripts/build.ps1 -OverlayUi` 성공: Core 73, Journal 649 checks. 실행본 `1.14.2-20261024-182046-5e4d2a0f`로 latest 포인터 갱신.

6종 생성 PNG의 실제 alpha·로컬 pack URI·168px 디코딩·Freeze, 선택 시각 저장·DPAPI 재로드, 저장 실패의 메모리/파일 롤백과 재시도, 동일 시각 재선택·종류 검증, 이전 범위 상태 이어짐·자정의 최근 7일 이동, AI 스냅샷 제외를 확인했다. 게이지는 렌더 픽셀로 미기록·미래 alpha=0, 기록 구간 alpha=255를 확인했다. 800 DIP 화면 밖 렌더에서 시계/날씨/오른쪽 컨디션 사이 동일 간격과 작은 화면 축소·고정 시각 높이를 확인했다. 실제 사용자 기록·위치·외부 네트워크·전역 키 입력을 사용하지 않았다.

화면 자료: `.test-data/journal-1af896da9c9148e0bdfe96723ebed152/dashboard-conditions.png`, `condition-choices.png`. 생성 원본과 프롬프트는 [컨디션 아이콘](../design/condition-icons.md)에 기록한다.

## 2026-10-24 숨김 시작의 실행 토스트

직접 cold start에서 ShowOverlayOnLaunch=false이면 기존 ToastWindow의 startup 모드로 실행 안내와 저장된 단축키를 표시한다. 주 모니터 WorkingArea 오른쪽 아래에 16 DIP 여백으로 배치하고 비활성·클릭 통과·최상단 창을 사용한다. 200ms 페이드인 완료 후 1초 유지하고 200ms 페이드아웃 후 닫는다. 자동/트레이 시작과 기존 인스턴스 재실행은 무표시이며, 기존 일정 알림의 버튼·20초 수명과 저장 스키마는 유지한다. ADR 0029에 기록했다.

`scripts/build.ps1 -Launcher`에서 배포 보존 4개, Core 검사와 Windows/WPF 통합 94개가 통과했다. 합성 앱의 최초 토스트 표시, 주 모니터 작업 영역과 native noActivate/clickThrough/topmost, 완전 불투명 유지, 양쪽 페이드 중간 투명도, 자동 닫힘 및 재실행 무반복을 확인했다. QA: `.test-data/journal-df03f63c92ba46969507f99f651b8dac/launch reports/startup-toast.png`를 시각 검토해 안내·단축키와 공통 디자인을 확인했다. 전체 화면 배경 회귀와 실제 사용자 기록 변경은 하지 않았다.

`releases/1.13.1-20261024-135412-d1c9c935`에 앱·고정 실행기·CLI를 배포하고 latest.json을 원자적으로 갱신했다. 사용자 앱은 재시작하지 않았다. 컴파일 오류는 없고 외부 NuGet 연결 불가에 따른 NU1900 취약성 조회 경고가 남는다. 환경 validator PASS이며 architecture 명령은 null로 별도 자동 경계 검사는 미설정이다.

## 2026-10-23 화면 회귀 검사의 선택 실행

- 사용자 요청에 따라 기본 check/build에서 격자·빨간 배경·탭 조작을 포함한 전체 화면 검사를 제외했다. 실행기 창 검사도 -Launcher로 분리했다. 기존 회귀 검사는 -OverlayVisuals로 보존하며 블러·캡처·배경 전환 변경 때만 선택한다.
- 달력·탭 검사는 -OverlayUi에서 화면 밖 렌더와 직접 명령 호출로 수행한다. 이를 실제 OS 키 전달 검증으로 보고하지 않는다. build.ps1은 같은 선택 옵션을 check.ps1로 전달한다.
- 기본 검사: 배포 3개 + Core 49개 + 합성 프로젝트/WPF 12개 = 64개 통과. -OverlayUi 선택 검사: 같은 기본 검사와 추가 UI 검사로 총 109개 통과. 두 실행 모두 테스트 창·격자·빨간 배경·전역 키 입력 없이 완료했다. 전체 배경 검사와 실행기 창 검사는 재생하지 않았다.
- 앱 실행 동작과 사용자 데이터는 변경하지 않았다. Release 빌드 및 환경 validator를 확인하고 소스 Graphify 인덱스를 전체 갱신했다. 별도 architecture 명령은 여전히 미설정이다.

## 2026-10-23 오버레이 시작 화면 1.11.0

- 일반 앱 실행은 관리 창 대신 오버레이를 표시한다. 기존 인스턴스 재실행과 트레이 더블클릭도 같은 표시 경로를 사용하며 열린 오버레이를 닫는 토글로 처리하지 않는다. 명시적 관리 단축키·트레이 메뉴와 --tray 조용한 시작은 유지한다.
- 재실행의 최신 배포 확인은 유지한다. 사용자가 조용히 실행된 앱을 다시 열면서 새 버전으로 전환하면 --tray 인자를 제거해 새 오버레이가 표시되게 한다. 종료 확인 중의 재실행 요청은 확인 창을 가리지 않는다.
- 합성 배포의 실제 App.OnStartup에서 최초 실행의 오버레이 표시·관리 창 숨김, 숨긴 후 동일 프로세스 재실행, 표시 중 재실행, 기존 --tray 인스턴스의 새 버전 표시, 저장된 표시 설정이 있어도 --tray 시작은 숨김인 것을 확인했다.
- Core 49개, WPF·Windows 통합 173개, 배포 검사 3개로 총 225개 통과했다. 실행 중인 사용자 앱이 Win+Alt+End를 등록한 경우 통합 검사만 비어 있는 대체 키로 종료 경로를 검사한다. 일반 앱의 종료 키는 그대로 Win+Alt+End이며 실제 앱을 종료하거나 사용자 기록을 변경하지 않았다.
- Release 빌드·고정 실행기 배포 성공. 오류/경고 0개. 환경 validator PASS이며 별도 architecture 명령은 미설정이다. 소스 한정 Graphify 인덱스를 전체 갱신했다.

## 2026-10-23 고정 실행기 1.10.0

- 고정 실행 경로는 `artifacts/Facet/Facet.Launcher.exe`다. 루트 cmd도 같은 실행기를 호출하며 이후 버전마다 바꿀 필요가 없다. 빌드는 고유한 releases 폴더에 publish한 뒤 latest.json을 원자적으로 교체하고 직전 경로를 latest.previous.json에 보존한다.
- 실행기와 관리 창에 버전 없는 AppUserModelID를 적용하고 창의 재실행 명령·이름·아이콘을 고정 실행기로 지정했다. 이미 실행 중인 작업 표시줄 버튼의 창 활성화에서도 새 배포를 확인해 전환하며 같은 배포를 취소한 뒤에는 반복 제안하지 않는다.
- 현재 배포본의 재실행은 기존 창만 활성화한다. 이전 배포본은 데이터 경로별 IPC로 정상 종료를 요청한 뒤 새 앱을 실행한다. 미저장 입력의 취소는 기존 앱과 초안을 보존한다. 프로토콜 이전 버전에는 최초 한 번 직접 종료하도록 안내한다.
- Core 49개, JournalChecks 140개, 배포 스크립트 검사 3개로 총 192개 통과했다. 실제 실행기 프로세스의 공백·한글·따옴표 인자 전달, 포인터 교체, 최신 인스턴스 재사용, 구버전 정상 종료·취소, Activated를 통한 전환, Windows 창 속성 읽기를 확인했다. 작업 표시줄 고정 자체는 사용자가 한 번 수행한다.
- 앱/실행기 publish 실패 시 기존 manifest·실행기 보존과 기존 manifest의 성공 교체를 Windows PowerShell에서 검증했다. 반복 빌드 중 발견한 File.Replace의 null 인자 바인딩 차이는 실제 백업 경로를 지정해 수정했다.
- 최종 Release 빌드·배포 오류/경고 0개. 실제 배포 실행기의 --storage-check를 격리 데이터 경로로 실행해 최신 Release 앱의 DPAPI·복구 검사를 통과했다. 사용자 실행본·실제 기록·작업 표시줄 고정 목록·기존 자동 시작 등록은 변경하지 않았다.
- 환경 validator PASS. 별도 architecture 명령은 미설정이며 Launcher의 Core/Desktop 무참조, Desktop의 배포 규약 source link와 검증 전용 참조를 확인했다. Graphify 소스 인덱스를 전체 갱신했다.

## 2026-10-23 전역 종료 단축키 1.9.0

- `Win+Alt+End`를 고정 전역 종료 단축키로 추가했다. 기존 종료 경로를 재사용하며 미저장 입력의 확인 창은 관리 화면을 앞으로 가져와 표시한다. 중복 종료 요청은 확인 창을 추가로 열지 않는다.
- 다른 기능에 종료 조합을 지정하면 등록을 변경하기 전에 거부한다. 등록 실패 시 기존 네 단축키를 복구하며 종료 시 네 단축키를 모두 해제한다.
- Core 49개, 합성 데이터 기반 WPF·Windows 통합 80개 검사 통과. 실제 키 입력으로 미저장 입력의 종료 취소·입력 보존, 오버레이가 열린 상태의 완전 종료, 종료 후 단축키 해제를 확인했다. 사용자 실행본과 기록은 변경하지 않았다.
- Release 빌드 오류·경고 0개. 배포 경로는 이전 1.9 버전 배포본이며 기본 런처를 연결했다. 환경 validator PASS이며 contract의 별도 architecture 명령은 미설정이다. Graphify 소스 인덱스를 전체 갱신했다.

## 2026-10-17 프로젝트 일지·달력 1.5.0

- 제작 작업에 프로젝트 일지/상세 작업 화면을 추가했다. 여러 루트 폴더 연결·재연결·일지 보존 연결 해제, 개별/전체 새로고침, 중단, 월별 달력과 날짜 선택, 일지 마지막의 다음 작업 Agent 코멘트를 제공한다.
- Core는 projectId/date별 일지 갱신과 구조화 응답을 검증한다. 기존 AppData v2의 선택적 필드를 추가하고 Store의 DPAPI 저장·백업·복원 경계를 재사용한다. 같은 날짜의 재분석은 하나의 기록을 갱신하며 실제 작업·단계·집중 상태는 변경하지 않는다.
- 수집은 읽기 전용이다. 오늘 Git 커밋, 미커밋 diff, 수정 시각과 제한된 소스·문서 본문/README를 분석한다. 게임 이미지·음원·모델은 메타데이터만 전달한다. 제외·잘림·Git 없는 폴더의 한계를 기록하고 도구가 없는 기존 Codex 실행을 재사용한다.
- 최종 scripts/build.ps1 통과: Core 47개와 JournalChecks 12개, 빌드 오류·경고 0개, 당시 1.5.0.0 배포본 생성. 기본 런처를 1.5로 연결했다. 이전 실행본은 보존했다.
- .test-data/journal-3054eb78144449569a775cc971a511cb에서 합성 일반/Git 폴더의 날짜·오늘 커밋·미커밋/신규/삭제 파일·ignored/비밀/생성물 제외·게임 에셋 본문 제외·취소·암호화 재조회·기존 7개 경로를 검증했다. 1240×900/940×680 화면과 스크롤 하단의 일지·근거·마지막 코멘트, 상세 작업 화면을 렌더해 확인했다.
- .test-data/journal-2421b1a824374076a2f85d2e1150e7d2에서 실제 WPF 새로고침 → 기존 Codex 인증의 실제 응답 → 같은 날짜 일지 갱신 → 암호화 재로드 → UI 중단 시 기존 일지 보존을 확인했다(13개 검사). 실제 사용자 프로젝트나 건강 기록은 사용하지 않았다.
- 환경 validator PASS. architecture 명령은 null이며 자동 경계 검사는 미설정이다. Core의 OS/UI/프로세스 참조 금지와 검증 전용 JournalChecks → Desktop/Core 방향을 소스·참조로 확인했다. 기존 impls 소스 범위의 Graphify 인덱스를 full rebuild하고 HTML/보고서를 갱신했다.

## 완료 범위

WPF Windows 앱, 수면 기록 추가/수정/삭제 복원, 7일 요약·CSV, 작업 관리·집중 작업·증거·출시 점검표, 설정 가능한 알림, 상시 상태창, 전역 단축키, 트레이, DPAPI 저장과 자동 백업, 사용자 선택 자동 시작.

## 수행한 검증

- .NET 10 Release 빌드 및 폴더 배포 성공. 최종 빌드 오류/경고 0개.
- Core 검사 16개 통과: 자정 경계, 23시 취침/다음날 기상, 야간 모드, 늦은 로그인, 이미 기록한 날, 알림 중복 방지/절전 복귀/오래된 알림 제외, 설정 비활성화, 결측·추정 수면, 입력 검증, CSV 인용/수식 방어, 이력 정리.
- 실제 Windows DPAPI 검사 통과: 암호화 확인, 저장/읽기, 원자적 교체 백업, null 보존, 의도적으로 손상시킨 테스트 주 파일에서 백업 복구, 단축키 파싱.
- 별도 `.test-data/ui`에서 실제 UI로 수면 메모와 게임 작업을 저장했다. 재실행 후 수면 1개·작업 1개가 유지되는 것을 확인했다. 사용자의 실제 수면 기록으로 만들지 않았다.
- 실제 `Ctrl+Alt+P` 입력으로 게임 제작 탭이 열리는 것을 확인했다. Windows 전역 단축키 3개 등록 성공.
- 실제 설정의 알림 테스트 버튼을 실행했다.
- 실행 중 상태창 Win32 속성 확인: visible=true, topmost=true, noActivate=true. 150% DPI에서 너비 350 DIP가 525 px로 표시됐다.
- 최종 화면을 직접 확인하고 제목 배경/선택 목록 대비 문제를 수정했다. 하단 고정 저장 버튼을 추가했다.
- bootstrap 환경 검증 PASS. 소스 한정 Graphify 인덱스를 생성·조회했다. 전역 Graphify 스킬 복사 명령은 환경 권한으로 수행되지 않았으나 기존 설치 CLI로 인덱스 생성은 완료했다.

## 1.1 일별 실행 계획 추가 검증

- Core 검사 총 24개 통과. 56일 수면/30일 게임 범위, 시작일 고정·직렬화 복원, 6주/8주 평가, 날짜+항목 중복 방지, 미래/잘못된 상태 저장 거부, 수면 기록 연결, 주력 선택, 제작 작업 중복 생성 방지를 추가했다.
- 새 버전 Release 빌드와 별도 배포 폴더 생성 성공. 실행 중인 기존 배포 파일은 잠겨 있어 최초 덮어쓰기 빌드는 실패했다. 별도 폴더에서 검증한 후 기존 앱의 저장·정상 종료를 거쳐 버전 1.1.0으로 교체했다.
- 별도 `plan-storage-*` 디렉터리에서 실제 DPAPI 저장 검사 성공. v1 → v2 읽기, 기존 수면 데이터 보존, 계획 상태·메모 저장 복원을 검증했다.
- `.test-data/plan-ui`에서 실제 UI로 게임 항목에 일부 실천과 QA 메모를 입력하고 저장했다. 재실행 후 시작일 2026-10-15, 실천 기록 1개 및 메모 복원을 확인했다. 다음 날 미리 보기에서 입력과 저장 버튼 비활성화를 확인했다.
- 실제 UI의 제작 작업 가져오기로 문제·제목이 채워진 제작 화면이 열렸다. 테스트 기록은 사용자 데이터와 별도다.
- 150% 배율 화면에서 오늘의 네 항목과 저장·연결 버튼을 확인했다. 카드 아래에 버튼을 고정해 상세 내용이 길어도 접근할 수 있게 조정했다.
- 데이터 스키마를 확장했으며, 체크 여부가 치료 효과·게임 출시를 대신하지 않는다는 경계를 유지했다.

## 1.1.1 프로젝트별 테스트 범위

Core 검사 총 26개 통과. 프로젝트별 작업의 중복 생성 방지·직렬화 후 사용자 결과/보관/집중 상태 보존·필드 길이·미실행 양식을 확인했다. Release 별도 폴더 배포 성공. 실제 게임 프로젝트 소스와 APK 내부 mvp-6를 읽어 실행 절차를 작성했으며 게임 플레이 결과는 생성하지 않았다. 사용자 앱을 1.1.1로 교체하고 실제 UI에서 첫날 상세·게임별 버튼·두 제작 작업·Afterlife 절차와 미실행 결과 양식을 확인했다. 빈 수면 초안은 건강 기록으로 만들지 않았고 기존 설정은 저장했다.

## 검증 범위의 한계 (공통)

실제 밤까지 기다린 장기 알림 검증, Windows 로그아웃/로그인 자동 시작, 물리적 모니터 분리·재연결, 독점 전체 화면 게임, 다른 PC 설치는 이번 검증에 포함하지 않았다. 일정 조건은 가상 시각을 사용한 결정적 검사로 확인했다. 무호흡증 또는 불면증 치료 효과를 검증하는 앱은 아니다.

## 이후 우선순위

실제 사용에서 기록이 번거로운 항목과 불필요한 알림을 먼저 줄인다. 재무 화면, 여러 장치 동기화, 자동 플랫폼 수집은 첫 버전 범위에 넣지 않았다.

## 2026-10-15 다크 테마 1.2.1
- Core 26개 검사 및 환경 검증 통과.
- 별도 dark-ui 데이터로 오늘 화면의 어두운 입력/목록/선택 색 확인.
- 최종 수정: 저장 버튼 전경 명시, Windows 제목 표시줄 다크 처리.
- 실행 경로: 이전 1.2-dark 버전 배포본. 기존 데이터 형식과 계획 유지.


## 2026-10-17 대시보드·AI 로드맵·입력 양식 1.3.0

- Release 빌드 오류·경고 0개. scripts/build.ps1에서 Core 32개 검사 후 이전 1.3 버전 배포본 배포 성공. 당시 기본 실행 스크립트 경로를 새 버전에 연결했다. 현재 실행 스크립트는 루트의 `Start-Facet.cmd`다.
- 첫 화면에 7/14일 수면 막대그래프(추정 수면/밤중 각성 지표), 실제 입력일 기준 평균, 오늘 실천 수, 프로젝트별 제작 단계 다이어그램, 실행 계획의 현재 구간을 추가했다. 넓은 창에서는 그래프와 로드맵을 나란히 배치한다.
- 모든 입력 탭에 질문형 구역, 항목 의미, 단위·예시를 추가했다. 예시는 저장값이 아니다. 잠들기 지연은 수면 기본 입력으로 옮겼다. 저장 버튼은 현재 편집 화면을 명시한다.
- AI 로드맵은 제작 정보 요청문 생성·검토·복사, 외부 Agent의 JSON 답변 검증·미리보기·명시적 작업 추가를 제공한다. 앱 내부 AI 자동 실행/API 연결은 없다.
- Core 검사 32개 통과. AI 요청에서 건강/설정/증거/보관 작업 제외, 미리보기 무변경, JSON 형식·필드·개수·중복 검증, 일괄 검증 후 반영, 재반영 중복 방지, 직렬화 이후 사용자 편집·증거·보관·집중 상태 보존, 자정 수면 계산·실제 0·결측을 확인했다.
- 격리된 .test-data/dashboard-smoke-v3의 합성 수면·작업 데이터로 7개 탭과 940×680 창의 6개 화면을 렌더했다. 그래프의 결측 공백과 두 열 배치, 입력 예시와 고정 저장 버튼을 직접 확인했다.
- WPF 통합 검사 10개 통과: 탭 수, 기존 수면 경로, 수면 양식 저장, 잘못된 AI 답변 거부, 반영 전 데이터 불변, 제안 저장, 재반영 차단, 작업 수정 후 SourcePlanKey 보존, DPAPI 재읽기, 최소 창 크기.
- 실제 수정 없이 프로젝트 선택 편집기가 생성될 때 미저장 안내가 뜨던 문제를 재현했다. 로드/저장 시점의 입력 스냅샷과 실제 값을 비교하도록 수정하고 통합 검사를 다시 통과했다.
- 환경 validator PASS. contract의 architecture 명령은 null이므로 자동 경계 검사는 미설정이다. Core에 WPF/WinForms/DPAPI 의존성이 없는지 소스와 프로젝트 참조를 확인했다. 소스 한정 Graphify 인덱스를 full rebuild했다.
- 테스트 데이터는 사용자 기록과 분리했으며 기존 저장 스키마 v2를 유지한다.

## 2026-10-17 앱 내부 Agent·아이콘 UI 1.4.0

- Release 빌드 및 이전 1.4 버전 배포본 배포 성공. 기본 런처를 새 버전으로 연결했다. 이전 실행본과 사용자 저장소를 덮어쓰지 않았다.
- 수동 요청문/JSON 화면을 제거했다. 저장된 제작 작업의 단계·결과·다음 행동과 게임 실천 기록을 앱에서 직접 분석하고 기존 작업 수정/새 작업 제안을 받는다. Codex CLI의 저장된 ChatGPT 인증을 사용하며 앱은 인증 파일이나 API 키를 읽지 않는다.
- 원격 요청은 제작 전용 allowlist 스냅샷이다. 수면·치료·설정·수면 실천·보관 작업은 포함하지 않는다. 개인 CLI 설정, shell, 앱·플러그인·브라우저·컴퓨터 도구를 끈 read-only/ephemeral 추론이며 요청문은 표준입력으로만 전달한다.
- Core 검사 42개 통과. 제작 진행 정보 포함/건강 정보 제외, 개별 선택, 새 작업 추가, 기존 계획 필드 수정, 단계·증거·집중·원본 키 보존, 충돌·잘못된 대상·중복·타 프로젝트 이동 거부, 일괄 검증, 변경 없음, v2 목표 기본값/직렬화 호환을 확인했다.
- .test-data/agent-live에서 실제 Codex 구조화 응답으로 기존 작업 수정 1개+새 작업 1개를 받고 적용했다. 사전 취소·실행 중 취소와 임시 실행 디렉터리 정리(잔여 0개)를 확인했다. 개발 샌드박스의 CLI 상태 DB 접근 제한 이후, 승인된 실행 환경에서 기존 인증과 실제 네트워크 연결을 검증했다.
- .test-data/agent-ui-live에서 실제 WPF 분석 버튼의 처리 경로 → Codex 응답 → 두 제안 미리보기 → 기존 작업 수정+새 작업 추가 → DPAPI 재읽기를 통과했다. 사용자 기록 대신 합성 작업만 전달했다.
- .test-data/connected-ui-v2에서 WPF 통합 검사 11개 통과. 7개 탭, 기존 수면 경로, 수면 양식 저장, AI 미리보기의 무변경, 개별 선택, 선택된 기존 작업만 수정, 실제 단계 보존, 적용 후 미리보기 해제, 암호화 재읽기, 도움말 버튼, 최소 창 레이아웃을 확인했다.
- 모든 탭과 940×680의 6개 화면을 렌더해 아이콘·짧은 제목·도움말·저장·고정 AI 적용 버튼을 확인했다. Desktop/Design/Icon은 벡터를 직접 그리며 이미지·아이콘 패키지는 추가하지 않았다.
- 환경 validator PASS. architecture 명령은 null이며 자동 경계 검사는 미설정이다. Core의 OS/UI 참조 금지는 소스와 프로젝트 참조로 확인했다. 구현 소스의 Graphify full rebuild를 수행했다.

## 2026-10-23 오버레이 플로팅 탭과 월 달력

- 상단 중앙의 둥근 캘린더 탭과 전체 화면 기준 중앙 월 달력을 추가했다. PC 로컬 현재 월, 일~토 7열, 주말 전용 색, 오늘 테두리를 표시한다. Ctrl+[/]로 탭을 순환하고 한 탭일 때는 선택을 유지한다.
- Core 49개와 WPF 통합 106개 검사 통과. 실제 Ctrl+[/]의 양방향 이동·끝 순환(합성 추가 탭), 윤년 2월·평년 2월·6주 월·연도 변경, 월 시작·끝·오늘·주말 색을 검증했다. 배경 페이드·블러·Esc·포커스 복귀 검증도 통과했다.
- 1440×900, 800×600, 480×360에서 상단 탭과 중앙 달력의 위치를 검사하고 렌더를 확인했다. 합성 QA 출력: `.test-data/journal-bedeb04c6b694996959a39df3dd21bd8`.
- 샌드박스에서는 전경 포커스와 RegisterHotKey 접근이 제한되어 데스크톱 접근 실행으로 통합 검사를 완료했다. 실제 사용자 기록은 사용하지 않았다.
- Release 빌드 경고·오류 0개. 이전 1.9 버전 배포본 갱신. 환경 validator PASS이며 contract의 architecture 명령은 null이므로 자동 경계 검사는 미설정이다. UI 변경은 Desktop에 한정했고 Core·저장 스키마는 유지했다.

## 2026-10-24 관리 캐러셀 전체 항목 표시와 드래그 CPU 분석

- 선택 항목 최대 폭 680 DIP, 이웃은 50%, 간격은 가용 폭의 2%를 12~32 DIP로 제한한다. 1440×900에서 선택 항목은 약 660×413 DIP다. 960×600 내부 설계를 함께 축소해 달력과 오른쪽 패널 높이 정렬을 유지한다. 세 항목은 선택 인덱스와 드래그 진행도에 관계없이 화면 안에 들어온다. 1440×900 / 800×600 / 480×360 × 선택 3가지 조합에서 모든 항목의 경계와 간격, 탭 표시를 검사했다.
- 기존 코드는 MouseMove마다 각 항목의 애니메이션을 해제하고 키보드 탐색 DP를 다시 설정했다. 현재는 마지막 좌표만 보관하고 CompositionTarget.Rendering에서 프레임당 한 번 transform만 갱신한다. 탐색 속성과 애니메이션 취소는 드래그 시작·선택 확정에만 적용한다. 드래그 종료·캡처 상실·Unloaded에서 Rendering 구독을 해제한다. 측정 중 Measure/Arrange는 유효한 상태를 유지했다.
- 저장 확인과 노트 편집 종료는 드래그 미리보기에서 실행하지 않고 mouse-up의 실제 선택 변경 직전에 한 번 실행한다. 저장 실패 시 기존 선택·초안·편집 모드를 유지하고 포커스 이동 알림을 보내지 않는다. 깨끗한 문서의 기존 guard 시간은 0.13ms여서 이번 측정에서 주 병목이 아니었다. 변경된 기록의 암호화/디스크 저장 지연은 별도 측정하지 않았다.

| 1440×900 Release, 화면 밖 WPF CPU 작업 | 기존 | 수정 후 |
| --- | ---: | ---: |
| 입력 이벤트 1,000회 처리 | 4.22ms | 1.65ms |
| 해당 UI 스레드 메모리 할당 | 144,080B | 656B |
| 렌더 30프레임 평균 | 33.11ms | 12.94ms |
| 렌더 30프레임 p95 | 34.65ms | 13.36ms |
| 드래그 중 저장 guard 호출 | 1 | 0 |

- 같은 테스트 초기화와 1440×900 RenderTargetBitmap, 준비 렌더 2회 후 30프레임을 측정했다. 입력 1,000회는 `ContinueDrag`를 직접 호출한 모의 입력 측정이며 실제 OS 마우스 이벤트 전달 비용은 제외했다. bitmap을 재사용하고 CopyPixels로 완료를 확인한다. 수정 후 평균 CPU 렌더 시간은 약 61% 감소했다. 입력 처리보다 큰 항목·효과의 렌더 비용이 주 병목 후보라는 증거다. 수정 후 BitmapCache를 추가한 비교 실험은 평균 68.59ms로 느려져 제품에 채택하지 않았다. 글자는 정지·이동 중 모두 직접 렌더하며 그림자는 빈 배경 형제에 유지한다.
- 이 값은 실제 화면의 presentation/GPU FPS가 아니다. 실제 배경 캡처·블러는 측정 대상에 포함하지 않았다. 코드상 기존 OverlayWindow의 50ms DispatcherTimer가 UI 스레드에서 StretchBlt/GdiFlush/WritePixels를 동기 호출하므로 추가 hitch 후보지만 이번 실험에서는 비용을 확인하지 않았다. 배경 캡처·블러·커서 처리는 변경하지 않았고 전체 배경 재현 검사는 재실행하지 않았다.
- 재현: DOTNET_CLI_HOME을 프로젝트의 .dotnet으로 설정한 뒤 `dotnet build impls/Facet.JournalChecks -c Release --nologo -m:1 /nodeReuse:false`, `dotnet run --project impls/Facet.JournalChecks -c Release --no-build --no-launch-profile -- --carousel-profile`. 합성 데이터만 사용하며 사용자 기록과 화면 캡처 파일은 사용하지 않는다. 성능 기준 경로: `.test-data/journal-e7ff978d59bf4a1ca1872f0cf219b37e/carousel-profile.json`, 수정 후: `.test-data/journal-178b680006b04e7ab0fb41d45fce7fbc/carousel-profile.json`.
- `scripts/build.ps1 -OverlayUi` 성공: 배포 실패 보존 3개, Core 53개, WPF·통합 306개. QA 이미지: `.test-data/journal-b3cb41e1285745f2a77d786926e7d32a/management-carousel-<width>-<index>.png`. 경고·오류 0개로 `releases/1.11.0-20261024-094827-e128fd50`에 배포했고 latest.json을 원자적으로 갱신했다. 환경 validator PASS, 별도 architecture 명령은 contract에 null로 남아 있다.

## 2026-10-24 중앙 고정 순환 캐러셀과 구간별 성능 분석

선택 항목은 창 가로·세로 중앙, 이전/다음 항목은 좌우 절반 크기로 표시한다. 첫 항목의 이전과 마지막 항목의 다음도 연결한다. 드래그 시 새 선택 항목이 커지며 중앙으로 이동하고 반대편으로 돌아가는 이웃은 전경 카드 뒤로 지나간다. 세 항목은 정지 및 보간 중 화면 안에 유지한다. 크기별 검사는 중앙 좌표, 좌우 간격, 전체 경계, 양방향 순환과 첫 레이아웃 전 Select를 포함한다.

앱은 C# / .NET 10 WPF다. MainWindow.xaml/OverlayTabs.xaml 및 WPF Panel·Border·Viewbox를 사용하고 WinForms는 Controller의 NotifyIcon과 Native/OverlayWindow의 Screen 조회에 사용한다. UseWindowsForms=true는 그 Windows 통합 API 참조이며 화면을 WinForms로 만든 것은 아니다. Core는 UI와 OS에 의존하지 않는다.

### 측정 조건과 의미

- Release, 1440×900 DIP, 준비 프레임 5개 제외 후 각 40프레임. RenderTargetBitmap을 재사용하고 1픽셀 CopyPixels로 완료를 확인했다. 표의 p95는 40개 중 38번째 값이다. 입력은 ContinueDrag 직접 호출이며 실제 OS 이벤트 전달 비용을 제외한다. RenderCapability.Tier=2는 실행 환경의 하드웨어 기능 지원 등급일 뿐 아래 렌더가 GPU 측정이라는 뜻이 아니다.
- RenderTargetBitmap은 소프트웨어 렌더 경로이므로 실제 창의 GPU 프레임 시간/FPS와 구분한다. 실제 화면 캡처는 3440×1440 모니터를 합성 체크 패턴 창으로 덮고 1280×536으로 읽었다. 각 축소 모드의 준비 샘플 3개를 제외하고 40회, 샘플 사이 45ms dispatcher pump를 적용했다. 픽셀은 저장·출력·전달하지 않고 구간별 경과 시간만 남긴다.
- 마크다운은 1,050자/목록 20개 원문을 WPF 요소로 생성하는 40회, 저장은 같은 합성 노트의 JSON 직렬화·DPAPI·WriteThrough/Flush·백업 교체를 포함한 20회다. 날짜 입력·사용자 기록은 사용하지 않는다. 서로 다른 경로와 주기의 값이므로 표를 단순 합산해 실제 프레임 시간으로 해석하지 않는다.

| 구간 | 평균 ms | p95 ms | 실행 시점/측정 경로 |
| --- | ---: | ---: | --- |
| 드래그 좌표 입력 처리 | 0.00024 | 0.00030 | 매 모의 입력 |
| 항목 위치·배율 transform 반영 | 0.01661 | 0.02150 | 드래그 렌더 프레임 |
| 변화 없는 UpdateLayout | 0.00126 | 0.00140 | 드래그 프레임, 재배치 없음 |
| 강제로 다시 요청한 루트 Measure/Arrange | 0.00367 | 0.00810 | 별도 반복; 하위 캐시는 유지 |
| HUD 렌더: 그림자 5개 포함 | 12.69979 | 13.71230 | 화면 밖 소프트웨어 렌더 |
| HUD 렌더: 그림자 제거 A/B | 1.89591 | 1.97220 | 같은 경로, 비용 분리용 |
| HUD 렌더: 그림자 영역에만 BitmapCache 실험 | 21.66808 | 22.17140 | 비용 증가로 미채택 |
| 전체 장면: 합성 배경·색조, 블러 없음 | 36.74340 | 38.04890 | 화면 밖 소프트웨어 렌더 |
| 전체 장면: 같은 배경·블러 18 | 119.08564 | 121.50320 | 화면 밖 소프트웨어 렌더 |
| 전체 장면: 640×400 블러 후 Viewbox 확대 실험 | 120.11610 | 122.82090 | 유의한 개선 없어 미채택 |
| 마크다운 AST→WPF 요소 생성 | 3.52478 | 4.18220 | 노트 기본 모드 전환 시 |
| 암호화·디스크 저장 | 6.72248 | 7.66800 | 변경 후 자동 저장/전환 시 |
| 캡처 GetDC | 0.02582 | 0.03930 | 실제 GDI, HALFTONE |
| 캡처 크기 확인/버퍼 재사용 | 0.00099 | 0.00150 | 준비 버퍼 생성은 제외 |
| 캡처 StretchBlt | 47.28054 | 62.54270 | 실제 GDI, HALFTONE |
| 캡처 GdiFlush | 0.00578 | 0.00730 | 실제 GDI, HALFTONE |
| WriteableBitmap.WritePixels | 0.10348 | 0.12430 | 실제 GDI 결과 UI 반영 |
| ReleaseDC | 0.01461 | 0.01990 | 실제 GDI, HALFTONE |
| 캡처 전체 | 47.43147 | 62.72290 | 실제 GDI, HALFTONE |
| 캡처 전체: COLORONCOLOR A/B | 28.96521 | 47.58480 | 더 빠르지만 여전히 UI 블록이 큼 |

### 적용한 최적화와 남은 후보

현재 드래그의 주 병목은 작은 transform 계산이 아니라 UI 스레드에서 동기 실행하는 화면 캡처다. 단일 캡처 평균 47ms는 60Hz의 프레임 간격 약 16.7ms보다 길다. 기존 50ms 타이머는 갱신 상한이며 실제 20fps 보장을 뜻하지 않는다. 실제 CPU 점유율이나 실제 표시 FPS는 이 샘플만으로 추정하지 않는다.

드래그와 220ms 스냅 동안 마지막 블러 프레임을 유지하고 동기 캡처를 생략한다. 종료 후 기존 타이머에서 실시간 갱신을 재개한다. 블러 자체와 HALFTONE 품질·SRCCOPY 커서 처리를 유지한다. 이 최적화는 캡처 한 번의 47ms를 줄이는 것이 아니라 입력 중 그 호출을 실행하지 않는 것이다. 끼어든 새 드래그를 이전 애니메이션 완료가 해제하지 못하도록 세대 번호를 검사한다.

추가 개선 우선순위는 (1) GDI 읽기를 전용 작업 스레드로 분리하고 UI는 완료된 버퍼를 WritePixels로 전달받는 구조, (2) 그림자만 미리 렌더한 이미지를 재사용해 효과 계산을 줄이는 방식, (3) 실제 창의 GPU/Present 추적이다. 첫 후보는 버퍼 소유권·취소·숨김/모니터 변경 시 수명 설계가 필요하다. 그림자 제거 A/B의 CPU 차이는 약 10.8ms지만 그림자를 없애는 변경은 채택하지 않았다. 단순 BitmapCache나 Viewbox 축소는 이 실험에서 개선이 없었으므로 제품에 추가하지 않았다. WinForms로 다시 만드는 것보다 관측된 캡처·효과 경로를 먼저 개선하는 편이 범위와 근거가 명확하다.

근거: Microsoft의 [WPF/Direct3D interop](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/wpf-and-direct3d9-interoperation)은 RenderTargetBitmap의 동기 소프트웨어 렌더 경로를 설명한다. [하드웨어 성능 지침](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-taking-advantage-of-hardware)은 픽셀 수와 fill rate의 영향을 설명한다. 앱의 DropShadowEffect/BlurEffect를 폐기된 BitmapEffect와 혼동하지 않는다.

재현: 프로젝트 .dotnet을 DOTNET_CLI_HOME으로 설정한 뒤 Release JournalChecks에서 `--overlay-profile`(화면 밖 합성 렌더) 또는 `--backdrop-profile`(잠깐 표시되는 합성 패턴의 실제 GDI 구간 측정). 원본 결과는 `.test-data/journal-45502b80292a49a7b5be7e138b7cdad8/overlay-components.json`, `.test-data/journal-4bb05c3007124673873e099ca04b66a0/backdrop-components.json`이다. 진단은 일반 앱에서 꺼져 있다.

최종 검증: sandbox의 현재 사용자 전용 Named Pipe가 AccessDenied로 차단돼 동일 Windows 사용자 환경에서 `scripts/build.ps1 -OverlayUi`를 재실행했다. 배포 실패/성공 보존 4개, Core 56개, UI·통합 347개를 통과하고 `releases/1.11.0-20261024-100759-7c74d8e0`과 고정 실행기·CLI를 배포했다. 추가 `--overlay-visuals` 132개도 통과했다. 실제 창에서 정지 시 캡처 증가 → 드래그/스냅/중단된 새 드래그 중 캡처 0회 → 이동 종료 후 캡처 재개와 blur radius18 유지, 기존 배경 색 변경 반영·입력·Esc·블러설정·리소스 해제·종료 단축키를 확인했다. Native 회귀의 합성 드래그는 WPF의 실제 버튼 상태와 맞추고 finally에서 반드시 버튼을 해제한다. UI QA는 `.test-data/journal-42e06f5931b441efa04efcaba46acafb`, 실제 배경 QA는 `.test-data/journal-d52c6f86e09a4262acaca24b6956f869`다. 빌드 오류는 없고 네트워크 제한으로 NuGet 취약성 메타데이터 조회 NU1900 경고가 있다. 고정 패키지 버전/의존성은 변경하지 않았다. 환경 validator는 PASS이며 architecture 명령은 null이어서 별도 자동 경계 검사는 미설정이다.

## 2026-10-24 중앙 빠른 실행 (1.12.0)

Win+Alt+Space 입력 모드, PATH/PATHEXT 우선순위 검색, @ 절대 경로와 @@ alias, 고스트 완성, 선택 후보 아이콘, 둥근 입력란과 지연 표시 후보 목록을 추가했다. 신규 오버레이 기본값은 Win+Alt+O이며 기존 저장 값은 보존한다. alias는 설정의 이름=절대경로 목록에서 등록하고 Settings.LaunchAliases에 암호화 저장한다.

- `scripts/build.ps1 -OverlayUi`: 배포 보존 4개, Core 56개, UI·통합 389개 통과. 빠른 실행 관련 검사는 합성 PATH 중복/확장자/이름 순위, alias 검증·암호화 저장, 폴더 열기/파일 선택 Explorer 인자, 취소된 검색, 고스트·커서·Tab·방향키·Enter·클릭·모드 전환을 포함한다.
- 화면 밖 1280×800 렌더에서 입력란 중심 Y=400과 후보 표시 전후의 위치 유지를 확인하고 빈 입력·앱 후보·alias 이미지를 시각 검토했다. QA: `.test-data/journal-7e7fcdfbe8174049b0a514e411eecd8d`. 실제 전역 키 전송·앱/탐색기 프로세스 실행은 이번 화면 밖 검사의 범위가 아니다. 선택된 실행 대상과 인자는 주입된 실행 경계로 확인했다.
- `artifacts/Facet/releases/1.12.0-20261024-102840-1f39e280` 배포 후 latest.json 갱신 완료. 실행 중인 기존 앱은 강제 종료하지 않았다. 고정 실행기를 다음에 실행하면 새 배포본을 연다.
- 샌드박스의 기존 알림 CLI Pipe AccessDenied는 동일 Windows 사용자 환경에서 전체 검사를 재실행해 해결했다. NuGet 취약성 메타데이터 네트워크 조회 NU1900 경고는 남으며 빌드 오류는 없다. 환경 validator PASS, architecture 명령은 null이므로 별도 자동 경계 검사는 미설정이다.
## 2026-10-24 빠른 실행 배경·아이콘 수정 (1.12.1)

입력 모드는 배경 alpha와 blur radius를 0으로 강제하고 진입 시 이전 캡처·이미지·효과 애니메이션을 해제한다. 일반 모드로 복귀하면 기존 설정을 다시 적용한다. 입력 중 기존 아이콘과 후보를 유지하되 오래된 후보의 클릭·완성·실행은 막고 최신 텍스트 후보 확인 후 다시 허용한다. 같은 대상의 완료된 아이콘은 재사용하고 새 대상의 조회 완료 후에만 입력 아이콘을 교체한다. 후보 없음·완료된 null·빈 입력·종료는 아이콘을 숨긴다.

`scripts/build.ps1 -OverlayUi`로 배포 보존 4개, Core 56개, UI·통합 422개 통과. 별도 `--quick-launch` 65개 통과. 입력 직후 아이콘 유지, 같은 대상 재사용, 대상 변경 후 교체까지 Source가 null을 거치지 않음, 대기 후보 입력 차단, 이전 블러·색조·캡처 즉시 제거, 일반 모드 설정 복원을 화면 밖에서 검증했다. 실제 화면 캡처·전역 키 입력 없이 합성 캡처 세션과 별도 저장소를 사용했다. QA는 `.test-data/journal-cd96a5fcd9e049a29df5f2167c17014f`이다.

`releases/1.12.1-20261024-104748-2e01e4ba` 배포와 latest.json 갱신 완료. 환경 validator PASS이며 architecture 명령은 null이다. 기존 NuGet 취약성 메타데이터 조회 NU1900 경고만 남고 빌드 오류는 없다.
## 2026-10-24 화면 캡처 전용 스레드와 UI 비용 분리

사용자 요청에 따라 GDI 화면 읽기를 전용 background thread로 분리했다. 블러/그림자·HALFTONE·SRCCOPY와 기존 전환은 유지한다. BackdropCaptureSession이 네이티브 source 생성·DC/DIB·복사 buffer·해제를 소유하고, OverlayWindow만 UI 스레드에서 WriteableBitmap을 생성/갱신한다. source와 UI 사이에 native 포인터를 공유하지 않는다.

request 신호와 대기 프레임은 각각 하나로 병합한다. worker는 UI callback 완료/취소를 확인하기 전 재사용 byte[]를 변경하지 않는다. UI 전달은 Dispatcher Background priority이며 UI에서 Task.Wait/Join을 하지 않는다. 캡처 시작 간격은 clock을 재확인하여 최소50ms로 제한한다. 숨김/블러 해제/종료는 세션과 화면을 즉시 분리하고 queued callback을 취소한다. 진행 중 GDI는 반환 후 worker finally에서 해제한다. 재열기는 이전 Stopped task를 비동기로 기다린 뒤 새 native owner를 시작하여 급한 토글에도 GDI 스레드가 겹치지 않는다. 이전 세션과 좌표/크기가 바뀐 프레임은 버린다. 캡처 제외 미지원은 숨긴 상태의 한 프레임 뒤 표시하며, 취소되면 늦은 완료로 다시 표시하지 않는다. 실패 시에는 기존 색조만 표시한다.

### 실제 오버레이 구간 측정

Release, 3440×1440 모니터의 합성 패턴, 1280×536 캡처. 실제 오버레이의 블러18과 관리 HUD/그림자를 켜고 준비5프레임 뒤 적용60프레임을 측정했다. 화면 픽셀은 저장/출력하지 않고 시간만 기록했다. 이전 동기 측정의47.43ms는 UI에서 실행하던 전체 캡처 경로이며 새 값과 총 표시 FPS를 뜻하지 않는다. native 처리 시간의 소폭 차이는 실행 환경/스케줄링 차이를 포함하며 HALFTONE 알고리즘을 바꿔 빨라진 것으로 해석하지 않는다.

| 새 경로 | 실행 위치 | 평균 ms | p95 ms | 최대 ms |
| --- | --- | ---: | ---: | ---: |
| GDI 캡처 | worker | 42.4057 | 48.4623 | 51.1000 |
| DIB→재사용 managed buffer 복사 | worker | 0.0957 | 0.1065 | 0.1208 |
| Dispatcher 전달 대기 | worker의 ack 대기, UI 실행 비용 아님 | 0.2153 | 0.0754 | 10.2621 |
| 실제 UI bitmap 갱신·모니터 확인 | UI | **0.2446** | **0.3447** | 0.3865 |

managed buffer 할당은 준비 포함66프레임 동안1회이며, 진단상 worker managed thread12 / UI managed thread2로 분리됐다. Dispose의 취소 요청 호출 샘플은0.0901ms였다. 이 값은 native 작업의 종료 완료 시간이 아니며 진행 중 GDI가 끝나면 비동기로 자원을 해제한다. UI가 더 이상 약47ms의 GDI 완료를 기다리지 않는 것이 개선점이다. 블러/그림자 GPU 렌더 시간이나 실제 presentation FPS는 이 측정에 포함하지 않았다. 드래그/스냅 중 마지막 블러 프레임 유지 정책도 보존하여 진행 중 완성된 프레임이 와도 화면 반영을 건너뛴다.

원본: `.test-data/journal-adfc4d44e3b34844b409763675832b9b/async-backdrop-components.json`. 재현은 DOTNET_CLI_HOME을 프로젝트 .dotnet으로 지정하고 Release JournalChecks를 빌드한 뒤 `--async-backdrop-profile`로 실행한다. 합성 패턴 창과 실제 오버레이가 잠시 표시되며 사용자 기록은 사용하지 않는다.

### 검증·배포

- 느린 source를 막아 둔 동안 UI Input dispatcher callback 실행,1000요청 병합, UI ack 전 buffer 미변경/동일 buffer 재사용, GDI/복사/해제의 worker 소유, 대기 UI callback의 비차단 취소, monitor/size stale 거절, 오류 후 회복, fallback 성공/실패, 반복 재열기 barrier와20Hz 상한을18개 결정적 검사로 확인했다. 단독 hidden 검사 포함30개: `.test-data/journal-7dbb6273b07547c4a50a48bf6430b11e`.
- 실제 Windows 오버레이 회귀157개 통과. 실시간 배경 변경 반영·bitmap 재사용·블러 전환/설정/0값·드래그/중단/재개·Esc·저장 취소·종료 단축키·hidden 첫 프레임 취소·fallback의 단1회 표시/실패를 확인했다. QA: `.test-data/journal-57881b79d05c4612935789d41ed8ead6`.
- 실행 중 앱의 고정 Win+Alt+Space와 충돌하지 않도록 native QA는 기존 종료 단축키 주입 패턴을 재사용해 테스트 인스턴스에만 빈 quick-launch 키를 주입한다. 일반 생성자의 기본값/사용자 설정/제품 단축키는 그대로다.
- `scripts/build.ps1 -OverlayUi`가 동일 Windows 사용자 환경에서 배포4 / Core57 / WPF·통합411개를 통과했다. 달력/날짜 패널의 그림자와 foreground pixel 동일성·탭/포커스·할 일/마크다운·알림CLI·동시 추가된 QuickLaunch도 포함한다. QA: `.test-data/journal-4cf264305d4846acbac5109d58360334`.
- `artifacts/Facet/releases/1.12.0-20261024-104548-1a8ad56f`에 앱·고정 실행기·CLI를 배포하고 latest.json을 원자적으로 갱신했다. 현재 실행 중인 사용자 앱과 기록은 강제 종료/변경하지 않았다. 패키지 추가/버전 변경은 없다. 오류0개, 기존 네트워크 제한의 NuGet 취약성 메타데이터 조회 NU1900 경고는 남는다. 환경 validator PASS이며 contract의 architecture 명령은 null로 별도 자동 경계 검사는 미설정이다.

## 2026-10-24 빠른 실행 자동 숨김 (1.12.2)

빠른 실행에서 입력란·후보 목록 밖의 마우스 클릭 또는 Window.Deactivated가 발생하면 입력창과 후보를 함께 숨기고 대기 중 검색을 취소한다. 입력란·후보 목록 내부 클릭과 내부 키보드 포커스 이동은 유지한다. 자동 숨김은 포커스를 복원하지 않으며 0ms 숨김에서도 정책을 저장·표시 갱신 전에 적용한다. 일반 오버레이는 기존 표시 정책을 유지한다.

`--quick-launch` 70개, `scripts/build.ps1 -OverlayUi` 배포 보존 4개·Core 56개·UI/통합 441개 통과. 실제 사용자 창을 활성화하거나 전역 입력을 보내지 않고 WPF routed mouse/keyboard focus 및 Deactivated 이벤트를 합성했다. 바깥 클릭·비활성화 후 저장된 표시 상태와 후보 숨김·검색 취소, 내부 클릭 유지, 포커스 복원 비활성화, 일반 모드 보존을 확인했다. QA: `.test-data/journal-fefaaeb55a614d60b491e11260e0ad45`.

`releases/1.12.2-20261024-112251-d36e19ce` 배포와 latest.json 갱신 완료. 빌드 오류·경고 없음. 환경 validator PASS이며 architecture 명령은 null이므로 별도 자동 경계 검사는 미설정이다.
## 2026-10-24 후보 목록 스크롤바 (1.12.3)

QuickLaunchView 후보 목록에만 세로 ScrollViewer/ScrollBar 템플릿을 적용했다. 스크롤 막대는 폭 4 DIP·끝 반경 2 DIP이며 화살표·트랙 배경을 제거했다. 오른쪽에는 항상 폭 20 DIP의 전용 열을 확보하고 스크롤이 필요할 때만 막대를 표시한다. 막대의 입력 영역은 전체 열 폭을 사용하며 hover/drag에 기존 Muted/Mint 토큰을 적용한다. 다른 화면의 스크롤바·데이터·경계는 변경하지 않았다.

합성 후보 8개를 표시한 화면 밖 1280×800 렌더의 최상단·최하단을 시각 검토하여 막대의 모양·이동과 텍스트 영역 분리를 확인했다. QA 이미지: `.test-data/journal-bc06b85f9fb944adb5f651e03e36d205/quick-launch-scrollbar-top.png`, `quick-launch-scrollbar-bottom.png`. 기존 빠른 실행 검사 70개 통과. `scripts/build.ps1 -OverlayUi` 재실행에서 배포 보존 4개·Core 56개·UI/통합 441개가 통과했고 `releases/1.12.3-20261024-130122-ac7251b7`과 latest.json을 갱신했다. 첫 전체 검사에서는 테스트 Application 종료 상태로 XAML 로드 예외가 발생했으나 같은 소스로 재실행해 통과했다. 종료 요청의 원인은 확인하지 못했다. 최종 QA는 `.test-data/journal-9a4e9a4f11d246edad9a828e9b44b154`이다. 빌드 오류·경고 없음. 환경 validator PASS, architecture 명령은 null이다.
## 2026-10-24 그림자 배경 사전 렌더링과 전경 선명도

사용자 요청에 따라 상단 탭·달력·날짜 패널·캐러셀 빈 항목 둘의 빈 그림자 배경만 CachedShadow로 바꿨다. 같은 Border 입력과 기존 검정/opacity0.42/blur24/depth6/direction270을 사용한다. 크기·배경/색·모서리·effect·DPI가 바뀔 때만 작은 frozen Pbgra32 atlas를 만들고 모서리·가장자리의 최대9개 cropped view를 재사용한다. 직선 부분만 늘리고 우측/하단 소수 pixel 위상을 유지한다. 단색 불투명 중심은 같은 brush로 직접 그린다. gradient/테두리는 전체 bitmap 경로를 사용한다. 현재 atlas/조각만 보유하므로 무제한 전역 캐시는 없다.

글자·입력란·컨트롤과 부모는 bitmap에 포함하지 않는다. CachedShadow는 기존 빈 배경 형제 위치와 Border 레이아웃을 유지하고 hit test를 받지 않는다. foreground의 CacheMode/Effect도 null로 유지한다. blur 배경 캡처 worker와 저장/입력 정책은 변경하지 않았다.

### 모양·선명도 검증

1440×900 / 800×600 / 480×360 각각에서 선택0/1/2를 비교했다. 같은 요소의 원래 live Effect 경로와 사전 렌더 경로를 전환하여 캘린더/날짜 패널/탭 글자 내부 영역이27조합 모두 pixel 단위로 완전히 동일함을 확인했다. 각 상태에서8번의 드래그 프레임을 그려도 cache build 수가 증가하지 않는다. 빈 배경5개만 대상이며 foreground에는 cache/effect가 없음을 확인했다.

기존 벡터 effect와 bitmap 리샘플링 사이에 일부 외곽 AA pixel 차이는 있다. 글자 영역과 구분해서 측정했다. 세 크기×세 선택의 전체 Pbgra32 채널 평균 차이는0.051~0.141/255, 배경 사각형 밖2px 이후 그림자의 alpha 평균 차이는0.454~1.307/255, 최대15/255였다. 전체 채널 평균<0.2, 그림자 alpha 평균<2/최대≤20의 재샘플링 허용 범위를 검사한다. 그림자 매개변수·offset·corner 모양을 유지했으며 회색 배경 위 live/cached 이미지도 시각 비교했다. 모양의 모든 pixel이 수치적으로 완전히 같다는 뜻은 아니다. 실무 색·크기 변경 후 재생성, mutable brush/effect 변경, bitmap factory의 DPI1/1.25/2와 effect padding도 확인했다. 실제 offscreen HUD는 DPI1.5에서 생성됐다.

형상/선명도 최종 QA: `.test-data/journal-b966773a81c44382958a80f77330f009/shadow-comparison.json`과 `shadow-live-gray.png`, `shadow-cached-gray.png`. 중간 시각 비교는 `.test-data/journal-a4dcae54ea934ae6b3d119baf52d93e9`에도 있다. 그림자 비활성 A/B snapshot 전에는 UpdateLayout으로 custom OnRender 명령을 갱신한 뒤 검사한다.

### 성능

Release, 동일1440×900 HUD와 동일 그림자 설정, software RenderTargetBitmap 재사용/CopyPixels 완료 확인, 준비5프레임 제외 후 드래그60프레임씩 비교했다. 좌표·배율 반영과 HUD 렌더를 포함하며 실제 창의 GPU/presentation FPS를 뜻하지 않는다.

| 방식 | 평균 ms | p95 ms | 최대 ms |
| --- | ---: | ---: | ---: |
| 원래 live DropShadowEffect | 16.6713 | 20.2715 | 22.1147 |
| 사전 렌더 atlas 재사용 | **4.9134** | **6.8130** | 7.6493 |

이 실행의 평균 CPU 렌더 작업 시간은 약70.5% 감소했다. cache atlas 원본 픽셀 용량은 다섯 곳 합계1,175,576B(약1.18MB)다. 전체 프로세스/GPU 메모리 사용량을 뜻하지 않는다. 새 HUD의 첫 layout+렌더70.7981ms 중 사전 렌더 누적27.8688ms는 최초1회 비용이며 이후130프레임 동안 build 수5를 유지했다. 즉 초기 준비 비용을 매 프레임 비용에서 분리하는 최적화다.

원본 성능 기록: `.test-data/journal-5d92cc646841418eb20e6877d72100be/shadow-profile.json`. 재현: 프로젝트 .dotnet을 DOTNET_CLI_HOME으로 지정하고 Release JournalChecks에서 `--shadow-check` 또는 `--shadow-profile`를 사용한다. 기존 `--overlay-profile`도 cached/live/none 세 경로를 구분하도록 갱신했다.

### 검증·배포

`scripts/build.ps1 -OverlayUi`가 배포4 / Core57 / WPF·통합500개를 통과했다. 글자 foreground pixel, 그림자 시각/경계·재사용/변경/DPI 검사와 기존 날짜·할 일·노트·포커스·알림CLI·QuickLaunch 검사를 포함한다. 오류/경고0개. 환경 validator PASS, contract의 architecture 명령은 null로 별도 자동 경계 검사는 미설정이다. 배경 blur/capture는 수정하지 않아 전체 화면 캡처 회귀를 재생하지 않았다. 앱/고정 실행기/CLI는 `artifacts/Facet/releases/1.12.3-20261024-131350-99282d46`에 배포하고 latest.json을 원자적으로 갱신했다. 실행 중인 사용자 앱/기록을 강제로 닫거나 바꾸지 않았다. 의존성/패키지 추가는 없다.

## 2026-10-24 오버레이 캡처 허용 (1.12.4)

오버레이의 Windows 캡처 제외를 제거했다. 배경은 오버레이를 숨긴 상태에서 비동기로 한 프레임만 읽고 표시 중에는 고정하며, 다시 열거나 블러를 재활성화하면 새 프레임을 읽는다. 캡처 도구의 보이는 편집/선택 창을 150ms 간격과 비활성화 시점에 확인해 Topmost를 일시 해제한다. 최소화·DWM 숨김 창은 무시하며 도구가 사라지면 포커스를 빼앗지 않고 복구한다. CoverScreen의 위치/크기 갱신도 해제 상태를 유지한다. 빠른 실행은 캡처 도구로 인한 비활성화 동안 유지한다. 결정은 ADR 0025에 기록했다.

검증은 합성 화면만 사용했다. native 캡처 속성 WDA_NONE, 실제 layered-window 포함 화면 픽셀에 오버레이가 들어가는 것, 뒤 화면이 빨강으로 바뀌어도 배경 프레임이 고정되는 것, 첫 프레임 취소·실패·늦은 완료 무시, 캡처 UI의 표시·최소화·복원·숨김·닫기, native Topmost 해제/복구와 포커스 보존, 숨김 뒤 감시 중지를 확인했다. 실제 Discord 송출이나 실제 Windows 캡처 도구 UI를 자동 조작한 검사는 아니며, 캡처 도구의 창 수명은 합성 창을 주입해 검증했다.

최종 native 화면/단축키 회귀는 170개 통과했다. QA: `.test-data/journal-62fe2bf5c1d64497befedcdc4089a786`, 로그: `.test-data/capturable-overlay-native.log`. `scripts/build.ps1 -OverlayUi`는 배포 보존 4개, Core 57개, WPF·통합 504개를 통과하고 `artifacts/Facet/releases/1.12.4-20261024-134506-93c1a205`에 배포해 latest.json을 원자적으로 갱신했다. UI QA: `.test-data/journal-b4c77716161845d1958184460ecef899`, 로그: `.test-data/capturable-overlay-release.log`. 초기 UI 실행 한 번에서 기존 노트 Escape 검사가 실패했으며 같은 소스 재실행에서 통과했다. 원인은 확인하지 못했다. 샌드박스의 단축키/Named Pipe 접근 제한 검사는 데스크톱 권한으로 다시 실행했다. 컴파일 오류는 없으며 NuGet 서버 연결 불가에 따른 NU1900 취약성 조회 경고가 남았다. 기존 캐시로 빌드·배포는 완료했다.

환경 validator는 PASS다. architecture 명령은 null로 별도 자동 경계 검사는 미설정이다. 전체 Graphify 색인과 evidence 무결성을 갱신·확인한다. 사용자 앱이나 기록은 강제 종료/변경하지 않았다.

## 2026-10-24 주 모니터 표시와 포커스 이탈 숨김 (1.13.1)

일반 오버레이와 빠른 실행은 Windows 주 모니터 전체 Bounds에 표시한다. 창 비활성화는 기존 저장·페이드 경로로 숨기고 새 앱의 포커스를 유지한다. 스냅샷용 내부 Hide와 캡처 도구 예외를 보존했다. ADR 0027과 architecture contract에 기록했다.

실제 2모니터에서 보조 모니터에 합성 전경 창을 놓고 주 모니터 배치를 확인했다. --overlay-focus 55개 검사는 두 모드의 0ms/160ms 숨김, 새 포커스 유지, Tick 후 숨김 유지, 빠른 실행→일반 모드의 합성 배경 프레임, 캡처 도구 Topmost 양보/복구를 통과했다. 화면 캡처·전역 키 입력·격자/빨간 배경은 사용하지 않았다. QA: .test-data/journal-5a545522ead4401094d1e64b49507949, 로그: .test-data/primary-focus-native.log. 화면 밖 --quick-launch 72개 검사도 통과했다(.test-data/primary-focus-input.log).

기본 scripts/build.ps1은 배포 실패 보존 4개, Core 57개, 기본 통합 12개를 통과하고 releases/1.13.1-20261024-135035-249efc26에 배포했다. latest.json을 원자적으로 갱신했고 실행 중인 사용자 앱은 재시작하지 않았다. NU1900 취약성 조회 경고는 외부 NuGet 연결 불가 때문에 남았다. 기본 Debug 검사 실행 파일이 다른 검사에 사용 중이어서 검사는 PrimaryFocus 구성으로 분리했다.

전체 -OverlayUi 검사에서는 기존 날짜 편집 삭제 검사가 한 번 실패했고 재실행에서는 통과했다. 재실행은 별도 공통 스크롤바의 목록 선택 이동 검사(offset=0)에서 실패했다. 이 둘은 이번 포커스 변경의 통과 증거로 계산하지 않았다. 환경 validator PASS, architecture 명령은 null이므로 별도 자동 경계 검사는 미설정이다. Graphify는 전체 색인과 evidence 검증을 갱신한다.

## 2026-10-24 앱 전체 공통 스크롤바 (1.13.1)

Desktop/Design/Scrolling.xaml을 Components.xaml에 병합해 ScrollViewer/ScrollBar의 앱 기본 스타일로 지정했다. 런처와 대시보드의 개별 템플릿은 제거했다. 세로·가로 모두 4 DIP 막대·끝 반경 2 DIP·20 DIP 전용 영역을 사용하며 Auto/Visible 축은 예약 공간을 유지하고 Disabled/Hidden 축은 공간을 제거한다. TextBox/DatePickerTextBox 내용 호스트, ListBox, ComboBox 드롭다운도 공통 스타일을 사용한다. 앞으로 만들 화면의 적용 규칙은 docs/design/README.md와 ADR0026에 기록했다.

공통 검사 23개(기본 프로젝트 검사 12개 포함) 통과. 양방향 스크롤, 내용/막대 영역 분리, 휠, 수평 Thumb 합성 drag, Auto 막대가 사라질 때 내용 폭/높이 유지, Hidden/Disabled 축과 한 줄 입력의 빈 공간 제거, 여러 줄 입력의 선택·스크롤, 목록의 마지막 선택 항목 이동, 드롭다운의 공통 스타일을 확인했다. 목록 이동과 갤러리 렌더는 활성화하지 않는 화면 밖 창에서 수행하고 전역 키·마우스 입력을 보내지 않았다.

`scripts/build.ps1 -OverlayUi`에서 배포 보존 4개·Core 56개·UI/통합 508개가 통과했다. 실행 중인 다른 진단 프로그램의 Debug/Release 실행 파일 잠금을 피하려고 BaseOutputPath를 `.test-data/shared-scroll-full/`로 분리했다. 동시 캐러셀 변경의 이전 검사 기준과 충돌한 첫 전체 검사는 최신 기준이 반영된 소스로 재실행해 통과했다. QA `.test-data/journal-e3a0f5c99eb14216a378df43f1b918ca/shared-scrollbars.png`와 `journal-narrow.png`를 시각 검토하여 메모·기록·프로젝트 목록·가로 카드의 동일한 막대와 전용 공간을 확인했다.

`releases/1.13.1-20261024-135226-58b892cc` 배포 후 latest.json을 갱신했다. 빌드 오류 없음. NuGet 취약성 메타데이터 조회 NU1900 경고는 남아 있다. 환경 validator PASS이며 architecture 명령은 null이다.

## 2026-10-24 큰 캐러셀·순환 경로 숨김·실제 드래그 측정

중앙 항목을 최대 1280 DIP로 키우고 양쪽 이웃 일부만 표시한다. 반대편으로 순환하는 항목은 드래그/스냅 중 숨기며 완료 후 목적 위치에서 복구한다. 그림자·블러·전경 직접 렌더링을 유지한다. ADR0028과 contract에 반영했다.

실제 3440×1440 / DPI150% 창에서 native 드래그를 측정했다. 정상 마지막 반복의 WPF 갱신은 58.56 FPS, 평균 간격 17.03ms, p95 20.29ms, 최대 46.32ms다. transform 평균 0.016ms, 레이아웃/그림자 재생성/GC는 0회다. 효과 제외 비교도 58~59 FPS여서 특정 효과가 갱신을 제한한다는 증거는 없지만 간헐 지연은 남는다. PresentMon의 앱 PID 이벤트가 0개여서 GPU 표시 FPS로 해석하지 않는다. 전체 프로세스 CPU와 초기/반복 순서 효과를 포함한 [측정 보고서](carousel-performance.md)에 원본·재현·한계를 기록했다.

scripts/build.ps1 -OverlayUi는 배포 보존4 / Core57 / WPF508개 통과(.test-data/carousel-final-build.log). native 최종4조건은20개 통과(.test-data/carousel-final-native.log). 크기별 중앙/이웃 경계와 가시성, 저장/입력/포커스, 그림자 전경 픽셀·atlas 재사용을 확인했다. 환경 validator PASS, architecture 명령은 null이다. 빌드 오류는 없으며 NU1900 연결 경고는 남았다. releases/1.13.1-20261024-135418-838f731d에 배포하고 latest.json을 갱신했다. 사용자 앱은 강제 재시작하지 않았다.

## 2026-10-24 달력에서 전환하는 하루 파이 일정

관리 첫 항목은 중앙 달력으로 시작하고 날짜 활성화 시 왼쪽 달력 fade-out/오른쪽 하루 화면 fade-in으로 전환한다. Esc는 모달 취소 → 하루 저장 후 달력 역전환 → 오버레이 숨김 순서다. 왼쪽 날짜·24시간 파이·연결 레이블·색상/레이블 범례와 오른쪽 할 일·Markdown 기록을 배치했다. 하루 화면에서는 캐러셀 입력을 잠근다. ADR0030과 선택 CalendarDayEntry.Schedules 저장 계약에 반영했다.

30분 48칸 hover, 같은 칸 8 DIP 이동, 양방향·자정 통과·24시간 드래그, 짧은 겹침 일정의 안쪽 반경/상위 hit test, 일정 모달 생성·수정·삭제, 색상 견본/기존 쌍 재사용, 날짜 격리와 암호화 재읽기, 저장 실패의 초안 보존·달력 복귀 차단을 확인했다. 1440×900 / 800×600 / 480×360 화면 밖 렌더에서 파이/기록 배치와 모달 고정 하단 버튼·색상 견본을 육안 확인했다. 화면 밖 검사는 실제 OS 포인터/키 전달 검사로 보고하지 않는다.

scripts/build.ps1 -OverlayUi 최종 실행은 배포 보존 4 / Core 59 / WPF 453개 통과했다. 로그는 .test-data/day-planner-final-build.log, 렌더 이미지는 해당 실행의 QA output에 있다. 최초 샌드박스 실행에서 기존 Reminder CLI의 현재 사용자 Named Pipe 접근이 거부되어 동일한 합성 데이터 검사를 샌드박스 밖에서 재실행했고 전체 통과했다. 사용자 저장소·전역 단축키·배경 캡처 화면은 사용하지 않았다.

1.14.0-20261024-142531-94c5f210에 배포하고 latest.json을 원자적으로 갱신했다. 실행 중인 앱은 강제 재시작하지 않았다. 환경 validator PASS이며 commands.architecture는 null로 별도 architecture 명령은 미설정이다. Core 프로젝트의 UI 의존성 없음과 Desktop → Core 방향은 보존했다. Graphify는 문서·설정 변경을 포함한 전체 refresh와 verify를 실행한다.

## 2026-10-24 런처 ? 도움말 (1.14.1)

런처의 기본 플레이스홀더를 제거했다. 입력 전체가 ?이면 앱 검색을 취소하고 입력창 위에 앱/폴더/링크 아이콘, 기본 입력/@/@@ 배지, 예시와 키 안내가 있는 패널을 표시한다. 입력창의 중앙 위치는 고정한다. 다른 내용을 입력하면 도움말을 숨기고 검색으로 돌아가며 Enter/Tab은 도움말을 실행하지 않는다. 내부 클릭·스크롤은 입력 모드를 유지하고 작은 화면에서는 기존 공통 막대 스크롤과 방향키/PageUp/PageDown을 사용한다. 도움말을 다시 열면 처음으로 스크롤한다.

공용 QuickLaunchChecks를 연결한 분리된 전용 실행기에서 67개 검사 통과. 빈 입력, 검색 취소, 패널의 위쪽 위치와 중앙 입력 고정, 내부 상호작용, Enter/Tab 실행 방지, 작은 화면 스크롤과 일반 검색 복귀를 검증했다. 1280×800 및 480×360 렌더를 시각 검토했다. QA `.test-data/quick-help-qa-bcdf5f719bdd4af2a74362bfa7b0e7f1/quick-launch-help.png`, `quick-launch-help-small.png`, `quick-launch-empty.png`.

동시 Windows SDK 타깃·날씨 변경 중 기존 검사 컴파일이 일시 실패해 먼저 런처 검사만 분리했다. 갱신된 검사로 `scripts/build.ps1 -OverlayUi`를 실행하여 배포 보존4·Core·UI/통합468개가 모두 통과했다. 빌드 출력은 `.test-data/quick-help-full/`로 분리했고 QA는 `.test-data/journal-be17dd3215ca44daa6c3f4b4b2ebb852`이다. `releases/1.14.1-20261024-143128-66088265` 배포 및 latest.json 갱신 완료. 빌드 오류 없음, NuGet 취약성 메타데이터 조회 NU1900 경고가 있다. 환경 validator PASS, architecture 명령은 null이다.
## 2026-10-24 관리 화면을 단일 달력으로 정리

관리 탭의 두 배경 전용 항목을 제거했다. 현재의 설계 크기·중앙 배치를 재사용하며 항목이 하나일 때 캐러셀 드래그를 시작하지 않아 마우스 캡처와 프레임 갱신 구독도 생기지 않는다. 날짜 클릭으로 여는 하루 파이 일정·할 일·마크다운 기록, 저장·포커스·Esc 복귀와 기존 블러·그림자를 유지했다. ADR0032와 architecture contract에 반영했다. 이전 캐러셀 전용 성능 명령은 제거하고 측정 보고서는 당시 구성의 기록으로 표시했다.

scripts/build.ps1 -OverlayUi에서 배포 보존4 / Core60 / WPF·통합366개가 통과했다. 단일 콘텐츠·양쪽 항목 부재·좌우 드래그 미소비·1440×900/800×600/480×360 중앙 배치와 기존 하루 화면/입력/저장을 확인했다. 그림자 검사는 달력과 하루 화면 양쪽의 실제 보이는 전경 픽셀과 atlas 재사용을 비교한다. 초기 실행은 숨겨진 달력의 과거 영역을 하루 화면 픽셀로 비교해 실패했으며, 숨겨진 조상을 제외하도록 검사를 바로잡은 뒤 별도 그림자50개와 전체366개가 통과했다. QA: .test-data/journal-d1b38f79f8674a158af5aa6441e5ff59의 management-calendar-1440.png 등. 중앙 달력만 표시되는 렌더를 시각 확인했다.

로그 .test-data/single-calendar-build.log. 빌드 오류0개, 외부 NuGet 취약성 메타데이터 조회 NU1900 경고는 남았다. 환경 validator PASS이며 architecture 명령은 null이다. releases/1.14.1-20261024-143743-9c032bd6에 배포하고 latest.json을 원자적으로 갱신했다. 검증한 Release Desktop DLL과 배포 DLL SHA256이 일치한다. 실행 중인 사용자 앱이나 기록을 강제 종료·변경하지 않았다.

## 2026-10-24 런처 예약 명령어 (1.14.2)

> 접두사 입력은 예약 명령어 후보를 표시한다. 현재 help를 제공하며 > h의 고스트와 Tab 완성, 방향키·Enter·후보 클릭을 기존 앱 검색과 공유한다. > help를 실행하면 도움말을 토글하고 입력·후보를 비운다. 도움말을 다른 검색 중에도 유지하고 같은 명령을 다시 실행하면 닫는다. ? 입력의 특수 동작은 제거했다. 예약 명령어에는 벡터 도움말 아이콘을 사용하며 OS 아이콘 조회나 외부 프로세스 실행으로 전달하지 않는다.

`--quick-launch` 93개 통과(기본 프로젝트 검사 포함). 예약 명령어 목록·접두사/대소문자 검색·자동완성·키보드 및 클릭 토글·입력 초기화·도움말 유지·? 동작 제거·외부 실행 경계 차단을 확인했다. 1280×800 후보/도움말과 480×360 스크롤을 시각 검토했다. QA `.test-data/journal-e14a280552794225a933fdb823d20601/quick-launch-commands.png`, `quick-launch-help.png`, `quick-launch-help-small.png`.

`scripts/build.ps1 -OverlayUi`로 배포 보존4·Core·UI/통합380개 통과. 출력 경로는 `.test-data/reserved-command-full/`, 최종 QA는 `.test-data/journal-1e73b7b0833c48b59a7d66a783abc5b8`이다. `releases/1.14.2-20261024-144345-66a15e46` 배포와 latest.json 갱신 완료. 빌드 오류 없음, NuGet 취약성 메타데이터 조회 NU1900 경고가 있다. 환경 validator PASS, architecture 명령은 null이다.
## 2026-10-24 중앙 달력 확대

CalendarHost의 Viewbox 확대 제한(DownOnly)을 해제해 가용 영역 안에서 비율을 유지하며 확대한다. 달력 자체의 글자·날짜·버튼·그림자가 함께 커지고 중앙 정렬은 유지한다. 하루 화면과 바깥 콘텐츠 크기는 변경하지 않았다.

1440×900 렌더에서 불투명 달력 경계가 기존538×488에서692×628로 약28% 커졌으며 중심은 두 이미지 모두 정확히(720,450)다. QA .test-data/journal-7fb7b18919444b4cb3196b10c1cfc648/management-calendar-1440.png를 시각 확인했다. scripts/build.ps1 -OverlayUi는 배포 보존4/Core60/WPF·통합390개를 통과했다(.test-data/larger-calendar-build.log). 기존 날짜·월 이동·하루 일정/편집/복귀·작은 화면과 그림자 전경 픽셀 검사를 포함한다. 환경 validator PASS, architecture 명령은 null이다. 오류0개, NU1900 외부 취약성 메타데이터 조회 경고는 남았다. releases/1.14.2-20261024-144528-bd3b56e3에 배포하고 latest.json을 갱신했다. 실행 중인 사용자 앱/기록을 강제 종료·변경하지 않았다.

## 2026-10-24 하루 일정 화면 정리

왼쪽 꺾쇠 뒤로 가기 버튼과 연월일/요일을 같은 행의 세로 중앙에 배치했다. 오른쪽 패널의 중복 날짜, 하루 소개 문구와 상시 30분 드래그/클릭 안내, 일정 추가 버튼과 일정 수정 선택 목록을 제거했다. 제거된 컨트롤의 이벤트와 포커스 경로도 정리했다. 새 일정은 파이 드래그로 만들고 기존 일정은 부채꼴을 클릭해 수정 모달을 연다. 모달의 제출 버튼은 확인으로 표시하며 저장·취소·삭제·실패 시 초안 보존은 유지한다. 날짜는 패널의 접근성 이름에 유지하되 화면에 중복 표시하지 않는다.

scripts/build.ps1 -OverlayUi는 배포 보존4/Core60/WPF·통합397개를 통과했다(.test-data/planner-cleanup-build.log). 실제 dial 선택 처리 경로에서 기존 일정 클릭→해당 내용으로 모달 열기, 확인 저장·암호화 재조회·자정 통과·삭제/취소/실패 보존과 기존 노트/할 일 기능을 확인했다. 1440×900/800×600/480×360에서 헤더의 두 요소 중심 높이, 날짜 단일 표시·제거 컨트롤 부재·모달 footer 노출을 검증했다. QA .test-data/journal-0318fb4c9121446992c351a574e20acc/day-pie-planner-1440.png와 day-pie-modal-1440.png를 시각 검토했다.

환경 validator PASS, architecture 명령은 null이다. 빌드 오류0개, 외부 NuGet 메타데이터 조회 NU1900 경고는 남았다. releases/1.14.2-20261024-145036-5c13a2e7에 배포하고 latest.json을 갱신했다. 배경 캡처·저장 형식·사용자 기록은 변경하지 않았고 사용자 앱을 강제 종료하지 않았다.

## 2026-10-24 기록 패널 여백·높이와 일정 부채꼴 표시

OverlayDayEditor는 기존 전체 스크롤 StackPanel의 추가 오른쪽 여백을 없애고 할 일 목록·기록 제목·남은 높이를 채우는 기록 Grid로 배치한다. 입력/기록 프레임은 대칭 padding을 사용하며 기록 프레임 하단과 패널의 간격은 상단 여백과 같다. 저장 상태는 기록 제목 행으로 옮긴다. 긴 할 일은 높이가 제한된 별도 목록에서, Markdown 미리보기와 편집 텍스트는 같은 크기 프레임 안에서 각각 스크롤한다. 기록 영역 전체가 휠 입력을 받는다.

일정의 기본 반경은 최대 반경의90%이며 겹침 축소도 그 안에서 적용한다. 저장된 부채꼴은 alpha255로 격자보다 나중에 그려 아래 격자와 hover 가이드가 비치지 않는다. 바깥10% 링은 전체24시간 일정이 있어도 새 시간을 드래그할 수 있고 hit-test는 실제 줄어든 반경을 따른다. 30분 방사형 격자는 원 안 hover 중에만 보인다. 드래그를 캡처한 상태라도 원 밖으로 이동하면 격자는 숨기며 시각 눈금과 시간 글자는 유지한다.

scripts/build.ps1 -OverlayUi: 배포 보존4/Core60/WPF·통합411개 통과(.test-data/planner-spacing-build.log). 396×752/320×440 패널에 할 일60개와 긴 Markdown100문단을 넣어 좌우·상하 경계, 보기/편집 프레임 일치, 목록·노트 내부 스크롤과 휠을 확인했다. 픽셀 비교로 hover 전후 격자 변화와 저장 부채꼴의 정확한 불투명 색상을 확인하고, 원 밖 드래그 중 격자 숨김과10% 링에서의 생성도 검사했다. 겹침 클릭 수정·날짜 전환·저장 실패 초안 보존 등 기존 검사를 포함한다. 초기 검사 두 곳은 새 ScrollViewer 템플릿/hover 무효화를 레이아웃에 반영하기 전에 읽어 실패했으며 배치 후 비교하도록 보정해 통과했다.

패널 전체 렌더는 .test-data/journal-83aed43be61c43708a447f7b59af1368/day-pie-planner-1440.png로 시각 검토했고 최종 hover 전후 도형 렌더와 검사 원본은 .test-data/journal-6f5edb77715342bfa4c139fd11d4d730에 있다. 환경 validator PASS, architecture 명령은 null이다. 빌드 오류0개, 외부 NuGet 메타데이터 조회 NU1900 경고는 남았다. releases/1.14.2-20261024-150041-c9cdafc5에 배포하고 latest.json을 원자적으로 갱신했다. 사용자 앱을 강제 종료하거나 사용자 기록을 변경하지 않았다.

## 2026-10-24 넓은 하루 화면과 일정 이름 callout

초기 안내 문구를 자동 저장 중으로 바꿨다. 달력 host는 그대로 두고 하루 화면만 독립 Viewbox(1560×752, 다이어그램1140/간격24/패널396)로 넓혔다. 1440×900에서 편집 패널의 왼쪽은 약899→1047로 오른쪽으로 이동하며 설계 폭396은 유지한다. 뒤로 가기·날짜와 기존 일정 모달/할 일/기록 동작을 보존했다.

이름 레이블은 다이어그램 양쪽과 모서리의 가까운 빈 공간에서 배치한다. 시계 눈금·범례·다른 레이블 영역은 제외하고 부채꼴 내부의 가장 가까운 각도를 연결점으로 택해 중앙각 고정을 없앴다. 연결선은2.4 DIP로 원 바깥을 우회하며 이름은 일정 색상2 DIP 테두리/반경8의 불투명 둥근 상자로 마지막에 그려 선에 가리지 않는다. 긴 이름은 최대 두 줄/줄임표로 표시하고 전체 이름은 수정 모달에 유지한다. 수용할 수 없는 초과 레이블은 개수로 알리며 부채꼴 클릭 편집은 계속 가능하다. 레이블과 부채꼴 배치는 일정/크기/DPI 변경 때 계산하고 hover에서 geometry를 재사용한다.

scripts/build.ps1 -OverlayUi는 배포 보존4/Core60/WPF·통합480개를 통과했다(.test-data/schedule-callouts-build.log). 혼합·같은 시간14개·자정을 넘긴24시간 일정의 이름 표시, 상호 비겹침·시계 바깥 경계·sector 내 연결점·중앙각 비고정·캐시 재사용/resize 재계산을 확인했다. 1440×900/800×600/480×360 화면 경계와 편집 폭, 기존 저장/실패/키보드/기록/그림자 검사를 포함한다. 첫 실행에서는 높이로 축소되는480화면에도 패널 왼쪽을 화면70% 이상으로 요구한 검사만 실패했으며, 실제 오른쪽 배치와 편집 폭 유지/경계를 확인하는 기준으로 보정해 통과했다.

QA .test-data/journal-14319ae54408475e985bcafe05a597b7의 day-pie-planner-1440.png, day-pie-modal-1440.png와 schedule-callouts-0/1/2.png를 시각 검토했다. 환경 validator PASS, architecture 명령은 null이다. 빌드 오류0개, 외부 NuGet 취약성 메타데이터 조회 NU1900 경고는 남았다. releases/1.14.2-20261024-151428-2da091b2에 배포하고 latest.json을 갱신했다. 사용자 앱을 강제 종료하거나 사용자 기록을 변경하지 않았다.

## 2026-10-24 일정 이름·부채꼴 클릭 수정과 전체 hover

OverlayScheduleDial은 이름 badge 경계와 부채꼴의 일정 판정을 통합했다. 원 밖의 이름도 mouse capture와 클릭을 받고, 누른 일정과 놓은 일정이 같고 이동이 클릭 임계값 안이면 해당 일정의 기존 수정 모달을 연다. 저장 일정에서 시작한 이동은 새 범위로 전환하지 않는다. 저장 일정/이름 위에서는 손 커서, 부채꼴 전체의 밝은 불투명 채움과 두꺼운 테두리·이름 테두리를 사용하고 30분 칸 hover 강조를 생략한다. 빈 영역/바깥10% 링의 생성, 겹침 반경·클릭 순서와 저장/취소 동작은 유지한다.

scripts/build.ps1 -OverlayUi: 배포 보존4/Core60/WPF·통합485개 통과(.test-data/schedule-click-hover-build.log, QA .test-data/journal-d71bbb632b6b4deca278f16325cd80e7). 이름 클릭→해당 모달, 부채꼴 클릭, 여러30분 칸에 걸친 전체 부채꼴 색상 변화, 겹친 짧은 일정만 강조, 저장 일정의 이동 중 새 생성/잘못된 수정 방지와 기존 링 생성/저장을 확인했다.

별도 --schedule-native-click는 합성 저장소의 실제 전체 화면 OverlayWindow에서 이름과 부채꼴로 커서를 옮겨 mouse-down/up을 입력했다. 실제 hit-test 대상과 손 커서, 두 경로 모두 해당 이름의 수정 모달이 열림을 확인했다. 기본12개 포함17개 통과(.test-data/schedule-native-click.log, QA .test-data/journal-b61887bac1d046aeaa04d267a5de304b). 마우스 위치를 복원했고 사용자 기록이나 앱 인스턴스를 변경하지 않았다. 이 검사는 별도 opt-in이며 기본 빌드에서 실행하지 않는다.

환경 validator PASS, architecture 명령은 null이다. 빌드 오류0개, 외부 NuGet 취약성 메타데이터 조회 NU1900 경고는 남았다. releases/1.14.2-20261024-153718-6dead157 배포 및 latest.json 갱신 완료. 배경 캡처 정책·Core·저장 형식은 변경하지 않았다.

## 2026-10-24 이름·부채꼴 hover 채움 통일과 격자 제거

이름 badge hover와 sector hover는 동일한 일정 ID를 사용한다. 일정 전체의 불투명 채움과 이름 상자 배경에 같은22% white blend를 적용하고 기존 테두리 색상/두께를 유지한다. 방사형30분 격자와 빈 칸 hover geometry 및 불필요한 pointerInside 상태를 제거했다. 시간 눈금, 드래그 중 선택 미리보기, 이름/부채꼴 클릭 수정과 바깥 링 생성은 유지했다.

scripts/build.ps1 -OverlayUi는 배포 보존4/Core60/WPF·통합487개 통과(.test-data/schedule-fill-hover-build.log). 픽셀 검사에서 원 안/밖의 격자 미표시, 이름/sector hover의 같은 부채꼴·이름 배경 색상, 양 채움의 정규화된 밝기 증가 비율 일치, 겹친 짧은 일정만 강조를 확인했다. 기존 실제 편집 경로·링 드래그·기록·여백·레이블 배치/캐시 검사를 포함한다. QA .test-data/journal-ac4b9900ab374827b76e33c7bda7bbb2의 schedule-callouts-0.png와 schedule-name-hover.png에서 기본 도형/이름과 hover 부분 렌더를 시각 검토했다.

환경 validator PASS, architecture 명령은 null이다. 빌드 오류0개, 외부 NuGet 메타데이터 조회 NU1900 경고는 남았다. releases/1.14.2-20261024-161421-5923ed5a 배포 및 latest.json 갱신 완료. 사용자 앱을 강제 종료하거나 사용자 기록을 변경하지 않았다.

## 2026-10-24 자정 양방향 드래그와 날짜별 구간 표시

반시계00시 통과 시 선택 시작을 음수 분으로 유지해 전날 시작으로 전달한다. 시계24시 통과는 다음 날 종료다. 실시간 선택은 날짜 경계에서 나누어 선택한 날의 부분을 불투명하게, 전날/다음 날 부분을35% 불투명도로 그린다. 저장 후에는 선택 날짜와 교차하는 부분만 표시하고 부채꼴/클릭/겹침/레이블 anchor도 그 잘린 구간을 사용한다.

일정은 실제 시작 날짜의 CalendarDayEntry에 정규 시작 분/기간으로 한 번만 저장한다. Core의 SchedulesForDate가 당일/전날을 조회하며 화면용 StartDayOffset/OwnerDate는 JsonIgnore라 기존 schema2 JSON 형식은 유지한다. SaveSchedule은 현재 노트/할 일 초안과 원본 일정의 수정/날짜 이동/삭제를 별도 후보에 적용·검증한 뒤 원자적으로 교체한다. 실패 시 전체 원본과 모달 초안은 유지한다. 모달은 실제 시작 날짜 선택과 날짜가 포함된 시작/종료 안내를 표시해00시부터 잘라 저장하지 않게 한다. 이어지는 다음 날짜도 달력 기록 표시를 포함한다. ADR0035와 contract에 기록했다.

scripts/build.ps1 -OverlayUi에서 배포 보존4/Core64/WPF·통합508개 통과(.test-data/cross-date-build.log). Core는 기존 JSON, 전날/당일 투영, 시작 날짜 이동/삭제, 월말·윤년·연말,24시간,200개 제한과 날짜 범위 실패 시 원본 불변을 검사했다. WPF에서는 실제 dial 드래그→전날 날짜/23시~01시 모달→확인→암호화 재조회→당일00시~01시 부채꼴 투영을 확인했다. 시계 방향 당일23시~다음날1시도 당일23시~24시/다음날00시~01시로 나뉘며, 잘린 구간에서 원래 전체 일정 수정·삭제, 노트 자동 저장의 일정 보존과 실패한 쓰기의 초안/기록 보존을 검증했다. 기존 월 이동·편집·레이블·호버·포커스·그림자 검사를 포함한다.

QA .test-data/journal-7faad6456a734635ac31d6d4bd2ecea9/cross-date-preview-previous.png, cross-date-preview-next.png와 day-pie-modal-1440.png를 시각 검토했다. 전날23~당일01은 왼쪽23~24가 반투명/오른쪽00~01이 불투명이고, 다음날로 넘어가면 반대임을 확인했다. 환경 validator PASS, architecture 명령은 null이다. 빌드 오류0개, 외부 NuGet 메타데이터 조회 NU1900 경고는 남았다. releases/1.14.2-20261024-164128-a86d5b89 배포와 latest.json 갱신 완료. 사용자 앱을 강제 종료하거나 사용자 기록을 변경하지 않았다.

## 오버레이 투명도와 배경색 팔레트 · 1.14.3

2026-10-25: 두 설정 화면의 배경 진하기를 투명도 0–100%로 변경했다. 0%는 선택색으로 완전 불투명, 100%는 배경이 완전 투명하며 전경은 유지한다. 24색 공통 팔레트의 선택 표시·자동 저장·실패 초안/재시도를 추가했다. 구버전 Opacity와 60% 색조 alpha를 변환해 기존 모습을 유지하고 Settings.OverlayTransparencyPercent/OverlayBackgroundColor를 선택 필드로 저장한다. 완전 투명에서는 블러·캡처도 해제한다. Windows 전역 투명 효과는 변경하지 않았다.

합성 Store의 별도 설정 검사74개, scripts/check.ps1 -OverlayUi의 WPF·통합770개, -OverlayVisuals의 실제 창·캡처175개, Core76개와 배포 보존 검사가 통과했다. 실제 화면 픽셀에서0%의 RGB 일치,50%의 뒤 화면 빨강→초록 실시간 반영과 캡처 버퍼 미사용,100%의 투명 배경·전경 유지·블러 미시작을 검증했다. 관리 창의 변경 감지와 재로드, 오버레이 설정의 즉시 저장·색상 검색·오류 재시도를 확인했다. overlay-settings.png와 작은 화면 렌더를 시각 검토했다. 실제 키/로컬 Pipe 검사는 샌드박스의 접근 거부(error5) 때문에 같은 검사를 허용된 실행 환경에서 다시 실행해 통과했다.

scripts/build.ps1로 releases/1.14.3-20261025-053027-8c4c72ff 배포와 latest.json 갱신을 완료했다. 컴파일 오류는 없으며 NuGet 취약성 메타데이터 네트워크 조회의 NU1900 경고는 남았다. 환경 validator는 PASS, 선언된 architecture 명령은 null이며 Desktop→Core 프로젝트 경계를 유지했다. 사용자 앱을 강제 종료하거나 사용자 기록을 변경하지 않았다. 로그: .test-data/transparency-settings.log, transparency-ui.log, transparency-visuals.log, transparency-build.log.

## 일반 관리 창과 전용 기능 제거

2026-10-25: MainWindow와 기존 대시보드·실천 계획·수면/제작 편집·프로젝트 일지·AI 로드맵·알림 이력·수동 백업/CSV 화면 및 전용 실행 코드를 제거했다. 전역 메시지 수신과 Dispatcher는 OverlayWindow로 이동했다. 트레이의 일반 창 메뉴와 수면/제작 단축키, 시작 시 계획/작업 생성, 수면·저녁 준비 예약 알림을 제거했다. 사용자 알림의 열기 버튼은 오버레이를 연다. 기존 v1/v2 데이터는 저장 호환 DTO로 보존하며 다른 작업에서 추가한 오버레이 기능은 유지했다. ADR0048과 contract에 현재 경계를 기록했다.

`powershell -NoProfile -ExecutionPolicy Bypass -File scripts/check.ps1 -OverlayUi -Launcher`에서 배포 보존4개·Core31개·WPF/통합962개가 통과했다. 합성 구형 저장소의 전체 기록 보존, 계획 미생성, 일반 창 타입/메뉴 부재, 저장 실패 종료 취소, 기존 인스턴스 재실행과 업데이트 취소·정상 전환을 확인했다. 별도 `--hotkeys` 검사42개는 숨긴 오버레이 HWND의 세 단축키 등록·수신·해제, 폐기 키의 타 앱 사용 가능, 사용자 알림의 저장 후 전달과 열기 버튼의 오버레이 전환을 확인했다. 키 입력 전송이나 격자 배경은 사용하지 않았다. 로컬 Pipe와 전역 키 검사는 샌드박스 접근 제한 때문에 허용된 실행 환경에서 격리 데이터로 실행했다.

선언된 Release 빌드는 경고0개·오류0개로 통과했다. 환경 validator는 PASS이며 architecture 명령은 null이므로 Desktop→Core, CLI의 UI 없는 source-link와 저장 경계를 소스에서 직접 확인했다. 다른 채팅의 실행 중인 검사와 파일 잠금이 겹친 중간 검증은 LegacyRemovalQA 구성으로 격리했으며 최종 검사는 기본 구성으로 다시 통과했다. 로그는 `.test-data/overlay-only-final-check.log`, `overlay-only-hotkeys.log`, `overlay-only-release.log`에 있다. 사용자 기록과 실행 중 앱은 검증에 사용하지 않았다.

## 트레이 메뉴 정리

2026-10-25: 중복된 전체 화면 오버레이 표시/숨김과 알림 테스트 항목을 제거했다. 트레이 메뉴는 오버레이 열기·빠른 실행·구분선·종료만 남기며 더블클릭의 ResetOverlay 연결은 유지한다. 호출이 없어진 TestNotice도 제거했다. 실제 사용자 알림과 전역 단축키 동작은 유지한다.

`scripts/build.ps1`에서 기본 배포 보존·Core·통합 검사가 통과하고 Release 배포와 latest.json 갱신을 완료했다. 로그: `.test-data/tray-menu-build.log`. 화면을 띄우는 추가 검사는 실행하지 않았다.
