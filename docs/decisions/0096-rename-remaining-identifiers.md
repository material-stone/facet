# 0096 — 남은 옛 이름 식별자 이전

2026-11-14: 공개 저장소에 앱의 옛 이름이 남지 않도록, 사용자 요청에 따라 [0089](0089-rename-to-facet.md)에서 유지했던 식별자 중 다음을 Facet으로 옮긴다. 기존 기록은 자동으로 이어진다.

- **플러그인 ID:** 기본 플러그인 ID를 `facet.chat`, `facet.music`, `facet.condition`, `facet.sleep`, `facet.clock`, `facet.weather`로 바꾼다. Core `PluginIds.Rename`이 기록을 읽을 때 승인·플러그인 설정·위젯 배치·위젯 저장소의 옛 ID를 새 ID로 바꾼다. 새 ID에 이미 있는 값이 이긴다.
- **플러그인 데이터:** `Store.Load`가 기록을 읽기 전에 `plugin-data`의 옛 ID 파일과 폴더(백업 포함)의 이름을 바꾼다. 같은 이름이 이미 있으면 건드리지 않는다.
- **설치된 플러그인:** `PortablePlugins.UpgradeRenamed`가 옛 ID나 옛 계약 assembly를 쓰는 플러그인을 배포본의 현재 ID 플러그인으로 한 번 교체하고, 배포본 폴더 이름(`clock`, `weather` 등)으로 옮긴다. 켜 둔 플러그인은 [0079](0079-plugin-auto-reload-at-launch.md)대로 시작 때 다시 승인된다.
- **음성 모델:** `%LOCALAPPDATA%/Facet`을 쓰고, 옛 폴더의 `whisper`, `supertonic`은 처음 쓸 때 옮긴다. 옮기지 못하면 옛 폴더를 그대로 쓴다.
- **DPAPI 설명 문자열:** `Facet`으로 바꾼다. 설명은 복호화에 쓰이지 않으므로 기존 기록을 그대로 읽는다.

## 유지

- 데이터 경로·AppUserModelID·인스턴스 이벤트·Run 키의 `MaterialStone`, `DailyDock`은 0089대로 유지한다.
- 이전 코드와 검사에는 옛 ID 접두사 하나(`PluginIds.RenamedPrefix`)가 남는다. 과거 ADR은 기록으로 둔다.
- 등록 버튼은 옛 MCP 등록을 계속 지운다.