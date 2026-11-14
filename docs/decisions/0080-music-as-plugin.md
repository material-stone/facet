# 음악을 플러그인으로 분리

2026-11-05: 음악 플레이어와 YouTube 다운로드를 앱에서 빼 `Facet.MusicPlugin`으로 옮긴다. 플러그인을 연결하지 않으면 앱에 음악 코드·도구·`yt-dlp`가 없다. ADR 0061~0068과 0078의 음악 내용은 소유 위치만 플러그인으로 바뀐다.

- 플러그인 계약을 세 가지 넓혔다. 위젯 없이도 일할 수 있도록 `IWidgetRegistry`에 `Directory`와 `Settings`, 에이전트 도구용 `AddAgentTool`을 더했다. 플러그인이 `IDisposable`이면 끄기·다시 불러오기·앱 종료 때 호출한다. `IPluginSettings.Save`는 입력란 없이 플러그인이 직접 관리하는 값을 저장한다.
- 재생은 위젯보다 오래 산다. 그래서 `PluginHost`와 `PluginRuntime`을 `Controller`가 소유하고, 켜진 플러그인은 한 번만 등록한다. 스킨 전환으로 오버레이를 다시 만들어도 재생이 끊기지 않는다.
- 에이전트 도구는 `Facet.Mcp`의 `tools/list`가 실행 중인 앱에 파이프로 물어 합친다. 목록 조회는 앱을 띄우지 않으므로 앱이 꺼져 있었거나 플러그인을 나중에 켰다면 에이전트 세션을 다시 시작해야 도구가 보인다. 앱 고유 도구와 이름이 겹치면 앱이 우선한다. `facet_today`의 음악 항목은 없앴다.
- 재생 목록은 플러그인 설정(`PluginSettings["teum.music"]["playlist"]`)에 앱 기록과 같이 암호화 저장한다. 음원과 곡 정보는 앱이 만들어 주는 `registry.DataDirectory`(`<data>/plugin-data/teum.music/MusicCache`)에 둔다. 플러그인 폴더는 코드만 담아 통째로 교체·삭제해도 음원이 남는다. 이전 위치의 폴더는 플러그인 첫 실행 때 옮기고, 이미 있는 데이터 폴더는 덮어쓰지 않는다. 플러그인을 지운 뒤 용량을 비우려면 이 폴더를 직접 지운다.
- `yt-dlp.exe`는 플러그인 폴더의 `Tools`에 있다. 지문은 DLL만 다루므로 실행 전 고정 SHA-256 검사를 그대로 둔다. 쿠키·자격 증명·DRM 우회를 쓰지 않는 경계도 그대로다.
- 앱 본체에 남는 음악 흔적은 `PluginData.MoveLegacyMusic`과 입력 전용 `AppData.Music`, `Settings.MusicIndicatorVisible`뿐이다. 이전 기록을 한 번 옮기기 위한 것으로, 그런 기록이 없어지면 지운다. 저장된 `builtin.music` 배치는 플러그인 위젯 ID로 바뀌고, 플러그인이 꺼져 있으면 자리표시로 보인다.
- 플러그인 코드는 자체 XAML을 pack URI로 불러올 수 없어 패널 XAML을 내장해 런타임에 읽는다. 플러그인은 앱 내부 타입을 못 쓰므로 표시기는 직접 창 스타일을 지정하고, 아이콘은 벡터 경로로 그린다.
- 이전과 달라진 점: 음성 결과 창은 음악 표시기를 피해 위로 올라가지 않아 겹칠 수 있다. 볼륨 저장 실패가 앱 숨김·종료를 막지 않고, 종료 때 저장을 한 번 더 시도한다. 브라우저 재생(`BrowserMusicTransport`)은 쓰이지 않아 삭제했다.
- 배포: `scripts/build.ps1`이 `artifacts/Facet/plugins/music`에 플러그인을 만든다. 이 폴더를 `<data>/plugins/music`에 복사하고 설정에서 켠다.
