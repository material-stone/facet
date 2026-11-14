# 에이전트 도구(MCP)와 대화 기록 문서

2026-10-31: 에이전트와 대화하며 Facet 기능을 쓰도록 한다. 기능을 에이전트별로 따로 붙이지 않고 stdio MCP 서버 `Facet.Mcp.exe` 하나로 노출해 Codex(런처·채팅 탭이 쓰는 Codex 앱)와 Claude CLI가 같은 도구를 쓴다.

## 앱 기능과 도구화 범위

| 기능 | 도구 | 비고 |
|---|---|---|
| 오늘 요약 | `facet_today` | 시각·오늘 일정/할 일·현재 컨디션·음악 상태 |
| 일정 | `facet_list_schedules`, `facet_add_schedule`, `facet_delete_schedule` | 30분 단위로 반올림, `CalendarNotes.SaveSchedule` 재사용 |
| 알림 | `facet_add_reminder` | 기존 `UserReminders` 규칙 그대로 |
| 음악 | `facet_music_status`, `facet_music_play`, `facet_music_pause`, `facet_music_volume` | 재생 목록 추가·삭제는 대시보드에서만 |
| 날씨 | `facet_weather` | 대시보드와 같은 지역 해석·open-meteo 요청 |
| 컨디션 | `facet_set_condition`, `facet_list_conditions` | 미래 시각 거절 |
| 수면 | `facet_record_sleep`, `facet_list_sleep` | 전달한 항목만 기상일 기록에 병합 |
| 지난 대화 | `facet_search_conversations`, `facet_read_conversation` | 아래 대화 기록 색인 |

도구로 만들지 않은 기능: 오버레이 표시·외관 설정, 단축키, 자동 실행, 빠른 실행·웹 검색(에이전트가 직접 할 수 있음), 프로젝트 일지·실행 계획(사용 빈도가 낮고 편집 UI 의존). 필요해지면 `AgentToolCatalog`에 추가한다.

## 연결 구조

`Facet.Mcp`는 데이터를 갖지 않는다. 도구 정의는 `Core/AgentToolCatalog`에 있고, 호출은 `ReminderProtocol` 프레이밍을 재사용한 별도 current-user pipe(`Facet-agent-<hash>`)로 실행 중인 앱에 전달한다. 앱이 없으면 고정 실행기를 `--tray`로 시작한다. Desktop `AgentToolHost`는 모든 호출을 UI dispatcher에서 처리하고 데이터 변경은 `Controller.Change`로 검증 실패 시 원상 복구, 저장 실패 시 롤백한다. 배포 시 `artifacts/Facet/Facet.Mcp.exe`는 release와 무관한 고정 경로라 MCP 등록을 다시 할 필요가 없다.

## 건강 기록 불변 조건 변경

사용자 결정으로 컨디션·수면 기록의 읽기·쓰기를 에이전트 도구에 허용한다. 따라서 사용자가 에이전트에게 요청하거나 에이전트가 조회한 건강 기록은 해당 에이전트의 모델 제공자(OpenAI·Anthropic)로 전송된다. 자동 첨부는 하지 않으며 도구 호출 시에만 전송된다. 그 밖의 외부 전송 금지는 유지한다.

## 채팅

- 런처·채팅 탭의 Codex 채팅은 항상 일반(projectless) 채팅이다. 프로젝트 선택과 채팅 탭의 프로젝트 그룹을 제거했다. 기존 `CodexChatProjectId`·`AgentChatReference.ProjectId`는 저장 호환을 위해 남기되 쓰지 않는다.
- 런처 요청은 직전 런처 채팅의 마지막 활동(전송·답변 완료)으로부터 5분(`ConversationArchive.SessionGap`) 안이면 같은 채팅에 이어 보내고, 아니면 새 채팅을 만든다. 이전 답변이 진행 중이면 이어 보내기를 거절한다.

## 대화 기록 구조

사용자 제안(연속 대화 요약을 상위 문서에 두고 키워드·날짜로 찾은 뒤 링크로 원문 참조)을 기반으로 다음을 보완했다.

```
<data>/conversations/
  index.md                    월별·주제별 진입점
  index.jsonl                 검색용 색인(세션 front matter에서 재생성 가능)
  months/2026-10.md           날짜 | 제목 | 키워드 | 요약 | 링크 표
  topics/건강.md              같은 주제의 세션 모음(최신순)
  sessions/2026/10/2026-10-31_1403_ab12cd.md   front matter 요약 + 전체 대화
```

- 요약은 상위 문서에 따로 쓰지 않고 세션 문서의 front matter가 원본이다. 월별·주제·JSONL 색인은 모두 여기서 생성되므로 요약과 색인이 어긋나지 않는다.
- 날짜(월별 표)와 주제(topics) 두 축으로 탐색하고, 키워드는 표·검색 도구에 쓴다. 에이전트는 `facet_search_conversations`로 JSONL을 검색해 `facet_read_conversation`으로 원문을 연다. 사람은 같은 폴더를 Markdown 뷰어(Obsidian 등)로 연다.
- 완료된 turn은 즉시 세션 문서에 추가된다. 세션이 닫히면(새 런처 세션 시작 시 5분 이상 쉰 세션들) 같은 Codex 채팅에 요약 요청 turn을 보내 JSON(title, summary, keywords, topics)을 front matter에 반영한다. 요약 turn은 채팅 탭과 기록 본문에서 숨긴다. 요약에 실패하면 다음 세션 종료 때 다시 시도한다.
- 기록 문서는 평문 Markdown이다(records.dat 암호화 대상 아님). 앱 데이터 폴더 안에 있으므로 다른 사용자에게 노출되지 않지만 백업·동기화 도구에 포함될 수 있다.
- Claude CLI 대화는 Claude가 자체 보관하므로 Facet 기록 대상이 아니다. Claude CLI는 검색 도구로 Facet 기록을 참조만 한다.
