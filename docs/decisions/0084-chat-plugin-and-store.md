# 채팅·에이전트 연결·음성을 플러그인과 전용 저장소로 분리

2026-11-05: 채팅 화면, Codex·Claude Code 연결, 런처 `"` 채팅, 작업 토스트, 음성 에이전트, 대화 기록을 앱에서 빼 `Facet.ChatPlugin`(`teum.chat`)으로 옮긴다. ADR 0047·0053·0069·0070·0071의 동작은 소유 위치만 플러그인으로 바뀐다. 저장 구조는 [ADR 0083](0083-sleep-plugin-and-store.md)과 같다.

- **계약 확장:** 런처·단축키·트레이는 앱의 자리라 플러그인이 직접 쓸 수 없었다. 채팅 전용 분기 대신 일반 확장점을 더했다. `AddLauncherMode`(입력 위·아래 컨트롤, 공용 플라이아웃, Enter 전송), `AddHotkey`, `AddTrayCommand`, `IPluginShell`(데이터 폴더, `Facet.Mcp.exe` 경로, 화면 공유 모드, 오버레이 숨김, 위젯이 있는 탭 열기, 내 설정 열기, 단축키 변경, 오류 알림)이다. 설정 필드에는 `ToggleSetting`, `CustomSetting`, 필드의 `Group`, `TextSetting.Check`를 더했다.
- **저장소:** `<data>/plugin-data/teum.chat.dat`에 `{SchemaVersion:1,Chats:[...]}`로 채팅 참조(최대 40개)를 저장한다. 대화 원본은 계속 Codex 앱과 Claude Code가 가진다. `Store.Load`가 `records.dat`의 `AgentChatReferences`를 찾으면 `backups/records-before-chat-store-*`로 백업하고, 저장소에 합쳐 쓰고, 다시 읽어 확인한 뒤에야 비운다. 같은 채팅이면 저장소 쪽이 이긴다.
- **대화 기록:** 평문 Markdown 문서는 `<data>/plugin-data/teum.chat/conversations`로 옮긴다. 플러그인 첫 실행 때 옛 `<data>/conversations`를 통째로 옮기고, 새 폴더가 이미 있으면 덮어쓰지 않는다. 옮기지 못하면 옛 폴더를 그대로 쓴다. `chat-runtime`은 옮기지 않는다. Claude Code가 작업 폴더별로 세션을 찾으므로 옮기면 이어 하기가 끊긴다.
- **설정:** 에이전트 연결·대화 기록·음성 에이전트는 앱 설정 그룹에서 빠져 설정 → 플러그인 → 채팅 구역의 세 묶음이 된다. 값은 `PluginSettings["teum.chat"]`에 있고, 옛 `Settings` 필드는 입력 전용으로 `PluginData.MoveChat`이 한 번 옮기고 비운다.
- **채팅 탭:** 탭은 일반 위젯 보드로 남는다. 고정 화면이던 채팅은 `GridVersion` 5 미만 배치에 위젯으로 한 번 들어가며 이후에는 지울 수 있다. 플러그인이 꺼져 있으면 자리표시로 보인다. 새 기본 배치에는 채팅 위젯이 없다.
- **에이전트 도구:** `facet_search_conversations`·`facet_read_conversation`은 플러그인 도구가 된다. 다른 플러그인 도구처럼 앱이 실행 중이고 플러그인이 켜져 있어야 목록에 보인다.
- **의존성:** Supertonic이 쓰는 ONNX Runtime·DirectML과 채팅 Markdown 렌더러는 플러그인 폴더에 있다. 그래서 `PluginHost`의 load context가 플러그인 폴더의 네이티브 DLL을 찾는다. `WinRT.Runtime`과 Windows SDK 투영은 앱 것을 함께 쓴다. 전역 상태가 둘로 갈라지면 Windows 음성이 깨지기 때문이다.
- **Whisper 상태:** 설정의 한 줄 문자열을 상태 카드로 바꾼다. 준비 안 됨·설치됨·불러오는 중·사용 중·오류를 점과 제목으로 구분하고, 서버·모델 파일을 행으로 보여 주며, 다음 할 일과 `다시 확인`·`지금 불러오기`·`폴더 열기`를 둔다. 음성 요청은 파일이 없으면 녹음 전에 멈추고, 카드를 누르면 플러그인 설정이 열린다. 전사 전에 모델을 불러오는 중이면 그렇게 표시한다.
- **앱에 남는 흔적:** 입력 전용 `AppData.AgentChatReferences`, `AgentChatReference`, 옛 `Settings` 필드, `LegacyChat`, `PluginData.MoveChat`뿐이다. 옛 기록을 옮기는 용도로, 그런 기록이 없어지면 지운다.
- **이전과 달라진 점:** 플러그인을 켜기 전에는 채팅·런처 `"`·음성 단축키·트레이 음성 메뉴·대화 기록 도구를 쓸 수 없다. 플러그인 설정은 설정 검색에 나오지 않는다. 음성 단축키가 다른 단축키와 겹치면 설정 입력란에 오류가 나고 이전 단축키가 유지된다. 화면 공유 모드를 바꾸면 열린 작업 토스트에는 다음 상태 변화 때 반영된다.
- **배포:** `scripts/build.ps1`이 `artifacts/Facet/plugins/chat`에 플러그인을 만든다. 이 폴더를 `<data>/plugins/chat`에 복사하고 설정에서 켠다.
