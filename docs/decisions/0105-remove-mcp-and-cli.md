# ADR 0105: MCP 서버와 CLI 제거

2026-11-15: 사용자 요청에 따라 에이전트 연결(`Facet.Mcp`)과 알림 CLI(`Facet.Cli`)를 지운다. 나중에 "도구는 앱이 아니라 각 플러그인이 가진다"는 원칙으로 다시 만든다. [0019](0019-dashboard-reminders-cli-debug.md)의 CLI와 [0069](0069-agent-tools-and-conversation-archive.md)의 MCP 서버를 대체한다.

## 결정

- **지우는 것:** `impls/Facet.Mcp`, `impls/Facet.Cli`, 앱의 `AgentToolHost`·`AgentToolProtocol`(에이전트 파이프), `ReminderPipeServer`·`ReminderProtocol`(알림 파이프), `Controller.HandleReminderCommand`, 빌드·패키지·검사 스크립트의 두 실행 파일 단계, `docs/workflows/reminder-cli.md`.
- **앱 소유 도구 없음:** `AgentToolCatalog.Tools`(`facet_add_reminder`, `facet_weather`)와 `AgentToolCall`·`AgentToolReply`·`AgentToolDefinition`을 지운다. `AgentToolCatalog`에는 일정 플러그인이 쓰는 순수 인자 해석·일정 편집 함수만 남는다.
- **플러그인 도구는 남긴다:** `IWidgetRegistry.AddAgentTool`, `PluginAgentTool`, `PluginHost.AgentTools`는 그대로다. 일정·컨디션·수면 플러그인은 계속 도구를 등록하지만, 지금은 이를 에이전트에 내보내는 통로가 없다. 다시 만들 MCP·CLI는 이 목록을 그대로 호출하면 된다.
- **플러그인 계약:** `IPluginShell.AgentToolServer`(MCP 실행 파일 경로)를 지운다. 이를 쓰던 채팅 플러그인은 [0104](0104-public-plugin-sources-without-chat-and-music.md)에서 지웠다.
- **알림:** `UserReminders`와 저장된 알림의 토스트 전달은 남긴다. 새 알림을 등록하는 경로는 없다.

## 대가

- Codex·Claude에 등록한 `facet` MCP는 `Facet.Mcp.exe`가 더 배포되지 않으므로 동작하지 않는다. 등록을 해제해야 한다.
- 배포 폴더에 남은 예전 `Facet.Mcp.exe`·`Facet.Cli.exe`는 빌드가 지우지 않는다. 남은 `.cli-`·`.mcp-` 스테이징 폴더만 지운다.
