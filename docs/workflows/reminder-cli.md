# AI Agent와 알림 CLI

AI Agent는 CLI로 알림을 등록하고, 실행 중인 앱이 입력 검증·저장·표시를 처리한다. CLI가 암호화 데이터 파일을 직접 수정하지 않는다. 결과는 성공 시 stdout JSON, 오류 시 stderr JSON이다.

## 실행

`scripts/build.ps1`이 성공하면 고정 경로 `artifacts/Facet/Facet.Cli.exe`를 사용한다.

```powershell
$dockCli = '<저장소 경로>/artifacts/Facet/Facet.Cli.exe'
& $dockCli add --title '테스트 결과 확인' --body '기록한 결과를 살펴봐요' --at '2026-10-24T18:00:00+09:00' --kind schedule --key 'agent-review-20261024' --start
& $dockCli list
& $dockCli complete --id '등록 결과의 GUID'
& $dockCli cancel --id '등록 결과의 GUID'
```

시각에는 `+09:00` 또는 `Z`처럼 시간대를 포함한다. 종류는 `schedule` 또는 `reminder`이며 기본값은 reminder다. `--key`는 요청별 안정적인 식별자로 지정한다. 같은 key와 같은 내용의 재시도는 기존 ID를 반환하고, 같은 key의 다른 내용은 충돌로 거절한다. key를 생략하면 각 실행마다 새 key가 생기므로 Agent는 key를 명시하는 편이 좋다.

앱이 없을 때 `--start`는 고정 실행기를 --tray로 실행한다. 옵션이 없으면 앱 미실행 오류를 반환한다. `--data-dir <경로>`는 합성 테스트나 독립 저장소에만 사용하며 앱과 CLI에 같은 경로를 준다.

## 동작 범위

오늘 카드에는 사용자 일정·알림, 기존 시간 알림, 달력의 오늘 미완료 할 일이 함께 표시된다. 시간 없는 할 일에 임의 알림 시각을 만들지 않는다. CLI의 list는 사용자 등록 알림 목록만 반환한다.

앱이 실행 또는 트레이 상태일 때 5초마다 마감된 사용자 알림을 확인한다. 종료 중에는 알림을 띄울 수 없고, 재실행 후 미전달 항목을 확인한다. 완료·취소 항목은 전달하지 않는다. 알림 끄기·전체 화면 생략·개인 내용 숨김은 기존 설정을 따른다. 전달 시각을 저장한 뒤 표시하므로 같은 ID를 재시작 후 반복하지 않는다.

exit code는 0=성공, 1=앱 거절/저장 실패, 2=잘못된 CLI 입력, 3=앱 미실행/연결 시간 초과, 4=전송 오류다. 저장 실패는 성공으로 응답하지 않으며 기존 메모리·암호화 기록을 유지한다.

전송은 동일 Windows 사용자·권한의 로컬 Named Pipe이고 외부 HTTP 포트를 열지 않는다. 요청은 16 KiB, 응답은 8 MiB로 제한한다. 명령은 add/list/complete/cancel만 허용한다. GUI 조작이나 별도 MCP 서버에 의존하지 않으므로 shell을 쓸 수 있는 Agent가 바로 접근할 수 있다. MCP가 필요해지면 같은 명령 경계를 재사용할 수 있다.
