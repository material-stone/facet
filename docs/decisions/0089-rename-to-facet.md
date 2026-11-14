# 0089 — 앱 이름을 Facet으로 변경

2026-11-05: 사용자 요청에 따라 제품·프로젝트·namespace·assembly·실행 파일·배포 경로·에이전트 도구(`facet_*`)·MCP 서버 이름을 Facet으로 바꾼다. 위젯과 플러그인이 보석의 깎인 면처럼 모인다는 뜻이며, 아이콘도 보석으로 바꾼다. [0056](0056-teum-project-naming.md)의 이름을 대체한다. 과거 ADR의 경로와 이름도 현재 코드를 가리키도록 함께 바꾸었고 0056만 기록으로 남긴다.

## 유지하는 식별자

바꾸면 기존 기록이 끊기므로 다음은 그대로 둔다.

- 플러그인 ID `teum.chat`, `teum.music`, `teum.condition`, `teum.sleep`, `teum.clock`, `teum.weather`: 승인, 위젯 배치, 설정, `plugin-data` 파일이 이 ID를 키로 쓴다.
- 데이터 경로·AppUserModelID·인스턴스 이벤트·Run 키의 `MaterialStone`, `DailyDock`([0056](0056-teum-project-naming.md)).
- `%LOCALAPPDATA%/Teum`의 음성 모델: 다시 받으면 1GB 가까이 든다.
- 저장소 폴더 `D:\project\dev\alone\Teum`: 작업 세션 밖에서 옮긴다.

## 이전

- 설치된 플러그인은 옛 계약 assembly를 참조해 로드되지 않는다. 시작할 때 `PortablePlugins.UpgradeRenamed`가 assembly가 `Teum.`으로 시작하는 플러그인을 배포 폴더의 같은 ID 플러그인으로 한 번 교체한다. 교체본이 없으면 건드리지 않는다.
- 등록 버튼은 옛 MCP 이름 `teum`을 지우고 `facet`을 등록한다. 등록은 자동으로 바뀌지 않으므로 다시 눌러야 한다.
- 배포 경로가 `artifacts/Facet`으로 바뀌어 고정 바로가기는 한 번 다시 고정한다. 켜진 자동 실행은 앱이 새 실행기로 보정한다.
- 공개 전이라 외부 플러그인의 계약 변경(`IFacetPlugin`, `Facet.Plugins`)은 호환 처리를 하지 않는다.
