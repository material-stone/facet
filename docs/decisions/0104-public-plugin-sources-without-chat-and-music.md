# ADR 0104: 플러그인 소스 공개, 채팅·음악 플러그인 제거

2026-11-15: 사용자 요청에 따라 기본 플러그인 소스를 공개 저장소(`material-stone/facet`)에 올린다. [0101](0101-schedule-clock-weather-plugins.md)의 "공개 저장소에는 플러그인 소스가 없다"는 전제를 대체한다.

## 결정

- **공개:** `Facet.ClockPlugin`, `Facet.WeatherPlugin`, `Facet.SchedulePlugin`, `Facet.ConditionPlugin`, `Facet.SleepPlugin`, `Facet.SamplePlugin` 소스와 아이콘을 공개한다. 아이콘은 직접 만든 것이다.
- **제거:** 더 쓰지 않는 `Facet.ChatPlugin`([0084](0084-chat-plugin-and-store.md))과 `Facet.MusicPlugin`([0080](0080-music-as-plugin.md))을 소스, 검사, 빌드·패키지 스크립트에서 지운다. 음악 플러그인만 쓰던 `scripts/setup-tools.ps1`(yt-dlp 내려받기)도 지운다.
- **남기는 것:** 저장된 예전 채팅·음악 데이터를 읽어 옮기는 Core 코드(`AgentChat`, `PluginData`의 `facet.music` 이전)와 레이아웃의 채팅 탭 ID는 그대로 둔다. 기존 사용자 데이터를 깨지 않기 위해서다.
- **검사:** `Facet.JournalChecks`는 플러그인 소스가 있으면(`PLUGIN_CHECKS`) 남은 플러그인 검사를 빌드한다. 이제 공개 저장소에서도 같다.

## 대가

- 이미 설치된 채팅·음악 플러그인 폴더는 그대로 남지만 새 빌드와 패키지에는 들어가지 않는다.
- 런처 `"` 채팅, 음성 에이전트, 대화 기록, `facet_search_conversations`, `facet_music_*` 도구가 사라진다.
