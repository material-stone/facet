# New Session Protocol

요청에 답하는 데 필요한 최소 context ladder만 사용합니다.

1. root `AGENTS.md`
2. `docs/README.md`
3. `docs/architecture/README.md`
4. `architecture-contract.yaml`
5. task-relevant ADR
6. 제한된 Graphify query 1회, 예:
   `graphify query "what calls <symbol>?" --budget 1200`
7. relevant path의 file만 읽기

Graphify node label은 English identifier이므로 한국어 요청은 핵심 symbol·module·feature 이름을 English term으로 바꿔 query합니다.
좁은 task에 전체 repository나 full Graphify report를 읽지 않습니다. graph evidence가 inferred 또는 ambiguous이면 source에서 검증합니다. handoff 전에 변경된 boundary, validation 결과, contract 또는 documentation update를 보고합니다.

검사는 [변경 범위별 검증](testing.md)을 따른다. 기본 검사에서는 화면을 띄우지 않으며 기능 추가마다 격자·빨간 배경·탭 조작을 재생하지 않는다.
