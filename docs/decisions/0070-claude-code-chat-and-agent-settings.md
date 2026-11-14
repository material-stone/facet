# Claude Code CLI 채팅과 에이전트 연결 설정

2026-10-31: 런처·채팅 탭에서 Codex 앱 외에 Claude Code CLI로도 일반 채팅을 할 수 있게 한다.

- `Settings.ChatAgent`("codex"|"claude", 기본 codex)가 런처 요청과 채팅 탭 새 채팅의 에이전트를 정한다. 런처의 에이전트 버튼과 설정에서 바꾼다. 5분 이어 보내기는 같은 에이전트의 직전 런처 채팅에만 적용한다.
- `ClaudeCodeChat`은 요청마다 `claude.exe -p --output-format stream-json`을 실행한다. 첫 turn은 `--session-id <uuid>`, 이후는 `--resume`. 작업 폴더는 `<data>/chat-runtime/claude`(빈 폴더)이며 `--allowedTools mcp__facet`, 파일·셸·웹 도구는 `--disallowedTools`로 막는다. Facet 도구는 `--mcp-config`(같은 폴더의 JSON 파일, 현재 데이터 폴더 지정)로 넘기므로 사용자 등록 없이도 동작한다. 모델은 `ClaudeChatModel` 별칭(빈 값은 Claude Code 기본).
- 세션은 `CodexChatSession`을 `Reference.HostId="claude"`로 재사용해 토스트·채팅 탭·대화 기록(ADR 0069)을 공유한다. 대화 원본은 Claude Code가 보관하며 앱 재시작 후 채팅 탭에는 이전 turn을 다시 읽지 않고 대화 기록 폴더로 안내한다. 중단은 프로세스 종료다.
- 설정 **에이전트 연결** 그룹에 기본 에이전트, Codex 사용·기준 채팅·연결 확인, Claude Code 사용·모델·연결 확인(`claude --version`), Facet 도구(MCP)를 Codex·Claude Code 사용자 설정에 등록하는 버튼을 묶는다. 등록은 같은 이름 `facet`을 지우고 다시 추가한다.
- 로그인·사용량은 사용자의 Claude Code 계정을 따른다. Facet은 인증 정보를 저장하지 않는다.

## 채팅 탭 후속 정리

- 채팅 탭은 Codex·Claude Code 대화를 구분 없이 한 목록에 보이고 답변 머리글도 "에이전트"로 통일한다.
- **앱에서 열기**는 대화를 만든 앱을 연다. Codex는 `codex://threads/<id>`, Claude Code는 같은 runtime 폴더에서 `cmd /k claude --resume <id>`로 터미널을 연다(세션은 폴더 기준 저장).
- **대화 제거**는 확인 후 Facet 목록과 `AgentChatReferences`에서만 지운다. 에이전트 원본과 대화 기록 문서는 남긴다. 작업 중인 대화는 제거하지 않는다.
- Codex를 끈 상태의 연결 안내 문구는 표시하지 않는다.
